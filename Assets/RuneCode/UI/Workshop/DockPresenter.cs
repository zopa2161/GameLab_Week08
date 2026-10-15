using System;

using UnityEngine;

using UnityEngine.InputSystem;

namespace RuneCode
{
    /// <summary>
    /// 시험 도크 Presenter다. DockRun(모델)의 시나리오·자동 발사·적응·배속을 바꾸고, 매 프레임 포인터 조준·좌클릭·R 리셋 입력으로
    /// 도크를 진행하며 지표를 표시한다. 팝업·편집 모달이 열려 있으면 클릭 시전을 막는다.
    /// </summary>
    public sealed class DockPresenter
    {
        private static readonly string[] SCENARIOS = { "dummy_single", "dummy_line", "dummy_swarm", "aegis", "adapt_loop" };

        private readonly RuneCodeSession _session;
        private readonly DockRun _run;
        private readonly DockPanel _view;
        private readonly Func<bool> _isInputBlocked;
        private bool _wasClicking;

        /// <summary>세션·도크 구동·도크 View와 클릭 입력 차단 조건을 받아 View를 연결하고 보존된 시나리오로 도크를 시작한다.</summary>
        public DockPresenter(RuneCodeSession session, DockRun run, DockPanel view, Func<bool> isInputBlocked)
        {
            _session = session;
            _run = run;
            _view = view;
            _isInputBlocked = isInputBlocked;
            _view.Bind(() => _run.Dock, () => _session.Save.ScreenShake, () => _session.Save.HitStop);
            _view.ScenarioClicked += CycleScenario;
            _view.TestClicked += () => _run.FireDock();
            _view.ResetClicked += () => _run.ResetDock();
            _view.AutoFireClicked += () => _run.SetDockOptions(!_run.AutoFire, _run.AdaptationEnabled, _run.Speed);
            _view.AdaptationClicked += () => _run.SetDockAdaptation(!_run.Dock.Adaptation.Enabled);
            _view.SpeedClicked += CycleSpeed;
            _view.SetScenario(GameData.L("scenario." + _run.Scenario));
            _run.StartDock(_run.Scenario);
        }

        /// <summary>프레임마다 입력으로 도크를 진행하고 지표·상태 문구를 표시한다. 도크가 없으면 무시한다.</summary>
        public void Tick()
        {
            if (_run.Dock == null) return;
            RenderStatus();
            StepWithInput();
        }

        /// <summary>도크가 화면에서 빠질 때 누적 시간을 버린다.</summary>
        public void Suspend()
        {
            _run.Suspend();
        }

        /// <summary>시험 시나리오를 다음 항목으로 바꾸고 독립 시뮬레이션을 다시 시작한다. 선택은 세션에 보존된다.</summary>
        private void CycleScenario()
        {
            int index = Array.IndexOf(SCENARIOS, _run.Scenario);
            string scenario = SCENARIOS[(index + 1) % SCENARIOS.Length];
            _view.SetScenario(GameData.L("scenario." + scenario));
            _run.StartDock(scenario);
        }

        /// <summary>시간 배속을 0.5 → 1 → 2 순서로 순환한다.</summary>
        private void CycleSpeed()
        {
            float next = _run.Speed == 1 ? 2 : _run.Speed == 2 ? 0.5f : 1;
            _run.SetDockOptions(_run.AutoFire, _run.AdaptationEnabled, next);
        }

        /// <summary>포인터 조준, 좌클릭·자동 시전, R 리셋 입력을 도크 고정 스텝으로 전달한다.</summary>
        private void StepWithInput()
        {
            bool isTyping = UiFactory.IsTyping();
            bool isInputBlocked = _isInputBlocked();
            bool canReadInput = Application.isFocused && !isTyping && !isInputBlocked;
            SimVector point = SimVector.Zero;
            bool isPointerInside = !isInputBlocked && _view.TryGetPointer(out point);
            SimVector aim = isPointerInside ? point - _run.Dock.Player.Position
                : isInputBlocked ? _run.Dock.Player.AimDirection : new SimVector(1, 0);
            Mouse mouse = Mouse.current;
            bool isClicking = canReadInput && isPointerInside && mouse != null && mouse.leftButton.isPressed;
            if (LocalTelemetry.IsUsabilityTestActive && isClicking && !_wasClicking)
                LocalTelemetry.RecordUsabilityEvent("simulation_request", "test_dock", new System.Collections.Generic.Dictionary<string, object>
                {
                    ["input_method"] = "pointer_hold",
                    ["scenario"] = _run.Scenario,
                });
            _wasClicking = isClicking;
            if (canReadInput && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) _run.ResetDock();
            _run.Step(aim, canReadInput && (_run.AutoFire || isClicking), canReadInput && _run.AutoFire,
                isClicking && mouse.leftButton.wasPressedThisFrame, mouse != null && mouse.leftButton.wasReleasedThisFrame);
        }

        /// <summary>누적 피해·DPS·에너지·동시 개체 지표와 자동 발사·배속·적응 상태 문구를 표시한다.</summary>
        private void RenderStatus()
        {
            RuneSimulation dock = _run.Dock;
            _view.SetMetrics(GameData.L("ui.damage") + " " + dock.TotalDamage.ToString("0") + "   " + GameData.L("ui.dps") + " " + dock.RollingDps.ToString("0.0")
                + "   " + GameData.L("ui.energy") + " " + dock.EnergySpent.ToString("0") + "   " + GameData.L("ui.peak") + " " + dock.PeakSpellEntities);
            _view.SetStatus(GameData.L("ui.autofire") + " " + GameData.L(_run.AutoFire ? "ui.on" : "ui.off") + "  ·  " + _run.Speed.ToString("0.#") + "×  ·  "
                + GameData.L("ui.adaptation") + " " + GameData.L(dock.Adaptation.Enabled ? "ui.on" : "ui.off") + "\n" + AdaptationText.Format(dock, false));
        }
    }
}
