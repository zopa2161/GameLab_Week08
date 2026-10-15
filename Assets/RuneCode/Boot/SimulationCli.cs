using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using UnityEngine;

namespace RuneCode
{
    public static class SimulationCli
    {
        // 사거리를 계산할 수 없는 마법의 자동 시전 거리. 기존 오른쪽 유입 x좌표와 같은 값이다.
        private const double FALLBACK_CAST_RANGE = 1180;

        /// <summary>명령행의 --sim 모드에서 지정 마법·시나리오를 실행하여 JSON 파일을 기록하고 종료한다.</summary>
        public static bool TryRunCommandLine()
        {
            var arguments = Environment.GetCommandLineArgs();
            if (!arguments.Contains("--sim")) return false;
            var output = Argument(arguments, "--output", Path.Combine(Application.persistentDataPath, "runecode.sim.json"));
            try
            {
                var spell = Argument(arguments, "--spell", "energybolt");
                var scenario = Argument(arguments, "--scenario", "dummy_line");
                var ticks = int.Parse(Argument(arguments, "--ticks", "600"), CultureInfo.InvariantCulture);
                var seed = int.Parse(Argument(arguments, "--seed", "1"), CultureInfo.InvariantCulture);
                var stage = int.Parse(Argument(arguments, "--stage", "1"), CultureInfo.InvariantCulture);
                var duration = double.Parse(Argument(arguments, "--duration", "0"), CultureInfo.InvariantCulture);
                if (duration != 0) UnityEngine.Debug.LogWarning("--duration은 더 이상 쓰이지 않습니다. 일반 스테이지는 목표 처치 수 달성, 보스 스테이지는 보스 처치 또는 제한시간으로 끝납니다.");
                var report = Run(spell, scenario, ticks, seed, stage);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                File.WriteAllText(output, report);
                UnityEngine.Debug.Log(report);
                Application.Quit(0);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError(exception.Message);
                Application.Quit(1);
            }
            return true;
        }

        /// <summary>전달한 마법과 시드로 고정 tick 시뮬레이션을 실행하여 피해·DPS·실행 횟수 JSON을 반환한다.</summary>
        public static string Run(string spellPath, string scenario, int ticks, int seed, int stage = 1)
        {
            GameData.Load();
            if (ticks < 1 || ticks > 1000000) throw new ArgumentOutOfRangeException(nameof(ticks));
            SpellGraph graph;
            if (File.Exists(spellPath)) graph = ShareCodec.Deserialize(File.ReadAllText(spellPath));
            else
            {
                var resource = Resources.Load<TextAsset>("RuneCode/spells/" + Path.GetFileNameWithoutExtension(spellPath));
                if (resource == null) throw new ArgumentException("마법 파일이나 Resources 템플릿이 없습니다.", nameof(spellPath));
                graph = ShareCodec.Deserialize(resource.text);
            }
            var economy = GameData.Balance.Economy;
            if (stage < 1 || stage > 1000000)
                throw new ArgumentOutOfRangeException(nameof(stage), "스테이지가 유효하지 않습니다.");
            var isTimedBattle = scenario == "incremental";
            var capacity = economy.BaseCapacity;
            var maxEnergy = GameData.Balance.Player.MaxEnergy;
            var compiled = GraphCompiler.Compile(graph, GameData.Runes, GameData.Balance.Grammar, GameData.Runes.All.Select(rune => rune.Id), capacity, maxEnergy);
            if (!compiled.Ok) throw new ArgumentException(string.Join("\n", compiled.Errors.Select(CompileIssueText.Format)));
            var simulation = new RuneSimulation(seed, isTimedBattle, GameData.Balance.Player.MaxHp, maxEnergy, stage,
                GameData.Balance.Player.EnergyRegen, carrier: CarrierSpawnSettings.LoadDefinition());
            if (!isTimedBattle) { simulation.ResetBench(scenario); simulation.SetAdaptationEnabled(scenario == "adapt_loop"); }
            simulation.SetUnlockedElements(new[] { "raw", "fire", "ice", "arc" });
            simulation.SetLoadout(new[] { compiled.Spell });
            var castRange = GetAutomaticCastRange(compiled.Spell);
            var timer = Stopwatch.StartNew();
            for (var tick = 0; tick < ticks && !simulation.Completed && !simulation.IsFailed; tick++)
                simulation.Step(isTimedBattle ? CreateAutomaticInput(simulation, castRange) : new SimulationInput(SimVector.Zero, new SimVector(1, 0), true));
            timer.Stop();
            return JsonUtility.ToJson(new SimulationReport(simulation, simulation.Tick, seed, scenario, compiled.Spell, timer.Elapsed.TotalMilliseconds), true);
        }

        /// <summary>살아 있는 가장 가까운 적을 조준하고 사거리 안에서 단일 마법을 시전하는 자동 전투 입력을 반환한다.</summary>
        public static SimulationInput CreateAutomaticInput(RuneSimulation simulation, double castRange)
        {
            SimulationEnemy nearest = null;
            var distanceSquared = double.MaxValue;
            foreach (var enemy in simulation.Enemies)
            {
                if (!enemy.IsAlive) continue;
                var candidateDistance = (enemy.Position - simulation.Player.Position).LengthSquared;
                if (candidateDistance >= distanceSquared) continue;
                nearest = enemy; distanceSquared = candidateDistance;
            }
            var aim = nearest == null ? simulation.Player.AimDirection : nearest.Position - simulation.Player.Position;
            var reach = castRange + (nearest == null ? 0 : nearest.Radius);
            // 실행 중·쿨다운이면 TryCast가 시전을 거부하므로 여기서 미리 거르지 않는다(같은 틱에 쿨다운이 끝나는 경우를 놓치지 않기 위함).
            return new SimulationInput(SimVector.Zero, aim, nearest != null && distanceSquared <= reach * reach);
        }

        /// <summary>컴파일된 형태와 흐름의 최대 도달 범위에 지팡이 발사 위치를 더해 자동 시전 거리를 반환한다.</summary>
        public static double GetAutomaticCastRange(CompiledSpell spell)
        {
            var range = GetActionRange(spell.Root);
            return (range > 0 ? range : FALLBACK_CAST_RANGE) + GameData.Balance.Sim.WandOffset;
        }

        /// <summary>중첩 분기와 후속 실행에서 볼트·영역·궤도·순간이동의 최대 도달 거리를 계산한다.</summary>
        private static double GetActionRange(IReadOnlyList<SpellAction> actions)
        {
            var range = 0.0;
            foreach (var action in actions)
            {
                var candidate = 0.0;
                if (action.Kind == "spawn")
                {
                    var stats = action.Stats;
                    candidate = action.Form == "orbit" ? stats.OrbitRadius + stats.Radius
                        : (action.Rules != null ? action.Rules.GetReach(stats) : stats.Offset) + stats.Radius;
                }
                else if (action.Kind == "blink") candidate = action.Distance + GetActionRange(action.Next);
                else if (action.Kind == "repeat") candidate = Math.Max(GetActionRange(action.Body), GetActionRange(action.OnComplete));
                else if (action.Kind == "delay") candidate = GetActionRange(action.Then);
                else if (action.Kind == "if") candidate = Math.Max(GetActionRange(action.Then), GetActionRange(action.Else));
                else candidate = GetActionRange(action.Next);
                range = Math.Max(range, candidate);
            }
            return range;
        }

        /// <summary>옵션 이름 다음의 인수를 반환하며 없으면 기본값을 반환한다.</summary>
        private static string Argument(string[] arguments, string name, string fallback)
        {
            var index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : fallback;
        }
    }

    [Serializable]
    public sealed class SimulationReport
    {
        [Header("시뮬레이션 내보내기")]
        [SerializeField] private int _ticks;
        [SerializeField] private int _seed;
        [SerializeField] private string _scenario;
        [SerializeField] private string _signature;
        [SerializeField] private double _maxEnergy;
        [SerializeField] private double _totalDamage;
        [SerializeField] private double _dps;
        [SerializeField] private double _energySpent;
        [SerializeField] private int _peakSpellEntities;
        [SerializeField] private int _nodeExecutionCount;
        [SerializeField] private double _elapsedMilliseconds;
        [SerializeField] private string _stateHash;
        [SerializeField] private int _stage;
        [SerializeField] private double _timeLimit;
        [SerializeField] private double _remainingTime;
        [SerializeField] private int _killTarget;
        [SerializeField] private int _targetKills;
        [SerializeField] private string _result;
        [SerializeField] private int _kills;
        [SerializeField] private int _earnedRam;
        [SerializeField] private int _settledRam;
        [SerializeField] private int _clearReward;
        [SerializeField] private int _spawnedEnemies;
        [SerializeField] private int _droppedExecutions;
        [SerializeField] private double _energyRegen;

        /// <summary>실행한 시뮬레이션과 설정값에서 공유 가능한 메트릭 스냅샷을 만든다.</summary>
        public SimulationReport(RuneSimulation simulation, int ticks, int seed, string scenario, CompiledSpell spell, double elapsedMilliseconds)
        {
            _ticks = ticks; _seed = seed; _scenario = scenario; _signature = spell.Signature;
            _maxEnergy = simulation.Player.MaxEnergy;
            _totalDamage = simulation.TotalDamage; _dps = simulation.TotalDamage / (ticks / (double)GameData.Balance.Sim.TickRate);
            _energySpent = simulation.EnergySpent; _peakSpellEntities = simulation.PeakSpellEntities;
            _nodeExecutionCount = simulation.NodeExecutionCount; _elapsedMilliseconds = elapsedMilliseconds;
            _stateHash = simulation.StateHash();
            _stage = simulation.StageNumber; _timeLimit = simulation.TimeLimit; _remainingTime = simulation.RemainingTime;
            _killTarget = simulation.KillTarget; _targetKills = simulation.TargetKills;
            _result = simulation.Stage.ToString(); _kills = simulation.KillCount; _earnedRam = simulation.EarnedFragments;
            _settledRam = simulation.Completed || simulation.IsFailed ? simulation.SettlementFragments : 0;
            _clearReward = simulation.ClearReward;
            _spawnedEnemies = simulation.SpawnedEnemies; _droppedExecutions = simulation.DroppedExecutions; _energyRegen = simulation.EnergyRegen;
        }
    }
}
