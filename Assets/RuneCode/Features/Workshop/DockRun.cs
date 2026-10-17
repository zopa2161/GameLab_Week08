using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 작업실 시험 도크의 독립 시뮬레이션 구동을 담당하는 일반 클래스다.
    /// 작업실 화면이 생성하고, 마법은 세션의 ISpellProvider 컴파일 결과로만 연결하며
        /// 더미 수·자동 시전·적응·배속은 세션에 저장해 작업실 재진입 후에도 유지한다.
    /// </summary>
    public sealed class DockRun : IDisposable
    {
        private const double FIXED_STEP = 1.0 / RuneSimulation.TICK_RATE;

        private readonly RuneCodeSession _session;

        private RuneSimulation _dock;
        private double _accumulator;
        private int _telemetryCount;
        private bool _wasFiring;
        private bool _wasAutomatic;
        private bool _hasPendingCastPress;
        private bool _hasPendingCastRelease;

        public RuneSimulation Dock => _dock;
        public string Scenario => _session.DockScenario;
        public int DummyCount => _session.DockDummyCount;
        public bool AutoFire => _session.DockAutoFire;
        public bool AdaptationEnabled => _session.DockAdaptation;
        public float Speed => _session.DockSpeed;

        /// <summary>세션을 보관하고 세션 마법의 컴파일 결과 갱신을 구독한다.</summary>
        public DockRun(RuneCodeSession session)
        {
            _session = session;
            _session.Spells.Compiled += OnSpellCompiled;
        }

        /// <summary>세션 마법의 컴파일 이벤트 구독을 해제한다. 작업실 화면 파기 시 호출한다.</summary>
        public void Dispose()
        {
            _session.Spells.Compiled -= OnSpellCompiled;
        }

        /// <summary>dummyCount를 세션에 보관하고 같은 종류의 더미를 일렬로 배치한 독립 시험과 세션 마법을 연결한다.</summary>
        public void StartDock(int dummyCount)
        {
            _session.SetDockDummyCount(dummyCount);
            _session.SetDockOptions("dummy_line", _session.DockAutoFire, _session.DockAdaptation, _session.DockSpeed);
            _dock = new RuneSimulation(1, false, _session.MaxHp, _session.MaxEnergy, energyRegen: _session.EnergyRegen,
                damageMultiplier: _session.DamageMultiplier, moveSpeedMultiplier: _session.MoveSpeedMultiplier,
                scrapGainMultiplier: _session.ScrapGainMultiplier, carrier: CarrierSpawnSettings.LoadDefinition(),
                killEnergyBonus: _session.KillEnergyBonus);
            _dock.ResetBench(Scenario, DummyCount);
            _dock.SetAdaptationEnabled(_session.DockAdaptation);
            _dock.SetUnlockedElements(_session.GetUnlockedElements());
            ApplyLoadout();
            _accumulator = 0;
            _telemetryCount = 0;
            _wasFiring = false;
            _wasAutomatic = false;
            _hasPendingCastPress = false;
            _hasPendingCastRelease = false;
        }

        /// <summary>현재 더미 수와 시험 옵션을 유지해 도크를 초기 상태로 복구한다.</summary>
        public void ResetDock()
        {
            StartDock(DummyCount);
        }

        /// <summary>유효한 세션 마법을 도크에 한 번 시전하고 튜토리얼 시험 단계를 저장한다.</summary>
        public void FireDock()
        {
            if (LocalTelemetry.IsUsabilityTestActive)
                LocalTelemetry.RecordUsabilityEvent("simulation_request", "test_dock", new Dictionary<string, object>
                {
                    ["input_method"] = "test_button",
                    ["scenario"] = Scenario,
                });
            _session.Spells.Recompile();
            CompileResult result = _session.Spells.CompileResult;
            if (!result.Ok)
            {
                _session.SetStatus("editor.invalidCast");
                if (LocalTelemetry.IsUsabilityTestActive)
                    LocalTelemetry.RecordUsabilityEvent("simulation_cast_result", "test_dock", new Dictionary<string, object>
                    {
                        ["result"] = "rejected",
                        ["reason"] = "compile_failed",
                    });
                return;
            }
            if (_dock == null || !_dock.TryCast(result.Spell))
            {
                if (LocalTelemetry.IsUsabilityTestActive)
                    LocalTelemetry.RecordUsabilityEvent("simulation_cast_result", "test_dock", new Dictionary<string, object>
                    {
                        ["result"] = "rejected",
                        ["reason"] = "dock_unavailable_or_cooldown",
                        ["signature"] = result.Spell.Signature,
                    });
                return;
            }
            _session.AdvanceTutorial(3);
            LocalTelemetry.Record(_dock.Tick, "bench.cast", result.Spell.Signature);
            if (LocalTelemetry.IsUsabilityTestActive)
                LocalTelemetry.RecordUsabilityEvent("simulation_cast_result", "test_dock", new Dictionary<string, object>
                {
                    ["result"] = "success",
                    ["signature"] = result.Spell.Signature,
                });
        }

        /// <summary>도크의 적응 활성 상태를 세션과 시뮬레이션에 저장한다.</summary>
        public void SetDockAdaptation(bool enabled)
        {
            _session.SetDockOptions(_session.DockScenario, _session.DockAutoFire, enabled, _session.DockSpeed);
            if (_dock != null) _dock.SetAdaptationEnabled(enabled);
        }

        /// <summary>도크 자동 시전·적응·시간 배율을 세션에 저장하고 시뮬레이션에 반영한다.</summary>
        public void SetDockOptions(bool autoFire, bool adaptation, float speed)
        {
            _session.SetDockOptions(_session.DockScenario, autoFire, adaptation, speed);
            if (_dock != null) _dock.SetAdaptationEnabled(adaptation);
        }

        /// <summary>조준 방향·선택적 포인터 위치와 시전 입력으로 도크 고정 스텝을 진행하고 새 노드 실행 텔레메트리를 기록한다.</summary>
        public void Step(SimVector aim, bool fire, bool isAutomatic = false, bool castPressed = false, bool castReleased = false,
            SimVector? aimPoint = null)
        {
            if (_dock == null) return;
            if (isAutomatic != _wasAutomatic) _dock.CancelBeamInput();
            _wasAutomatic = isAutomatic;
            _hasPendingCastPress |= castPressed || fire && !_wasFiring;
            _hasPendingCastRelease |= castReleased || !fire && _wasFiring;
            _wasFiring = fire;
            int maxSteps = GameData.Balance.Limits.MaxFrameSteps;
            _accumulator = Math.Min(_accumulator + Time.unscaledDeltaTime * _session.DockSpeed, FIXED_STEP * maxSteps);
            int steps = 0;
            while (_accumulator >= FIXED_STEP && steps++ < maxSteps)
            {
                _dock.Step(new SimulationInput(SimVector.Zero, aim, fire, castPressed: _hasPendingCastPress,
                    castReleased: _hasPendingCastRelease, isAutomatic: isAutomatic, aimPoint: aimPoint));
                _hasPendingCastPress = false;
                _hasPendingCastRelease = false;
                _accumulator -= FIXED_STEP;
            }
            CaptureNodeTelemetry();
        }

        /// <summary>탭 전환 등으로 스텝이 멈출 때 남은 누적 시간을 버린다.</summary>
        public void Suspend()
        {
            _accumulator = 0;
            _wasFiring = false;
            _hasPendingCastPress = false;
            _hasPendingCastRelease = false;
            _dock?.CancelBeamInput();
        }

        /// <summary>컴파일 결과가 갱신되면 현재 도크의 시전 프로그램을 교체한다.</summary>
        private void OnSpellCompiled()
        {
            if (_dock != null) ApplyLoadout();
        }

        /// <summary>세션 마법의 컴파일 결과를 도크 장착 마법으로 반영한다. 컴파일 실패면 장착을 비운다.</summary>
        private void ApplyLoadout()
        {
            CompileResult result = _session.Spells.CompileResult;
            _dock.SetLoadout(new[] { result.Ok ? result.Spell : null });
        }

        /// <summary>새로 실행된 노드만 로컬 링 버퍼에 기록하여 동일 tick 실행도 보존한다.</summary>
        private void CaptureNodeTelemetry()
        {
            int count = _dock.NodeExecutionCount - _telemetryCount;
            int start = Math.Max(0, _dock.NodeEvents.Count - count);
            for (int index = start; index < _dock.NodeEvents.Count; index++)
            {
                NodeExecutionEvent entry = _dock.NodeEvents[index];
                LocalTelemetry.Record(entry.Tick, "dock.node", entry.NodeId);
                GraphNode node = _session.Spells.Graph.FindNode(entry.NodeId);
                if (LocalTelemetry.IsUsabilityTestActive && node != null && node.RuneId == SpellGrammar.CORE_RUNE)
                    LocalTelemetry.RecordUsabilityEvent("simulation_cast_result", "test_dock", new Dictionary<string, object>
                    {
                        ["result"] = "success",
                        ["source"] = "continuous_input",
                        ["node_id"] = entry.NodeId,
                    });
            }
            _telemetryCount = _dock.NodeExecutionCount;
        }
    }
}
