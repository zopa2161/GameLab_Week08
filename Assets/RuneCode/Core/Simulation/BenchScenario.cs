using System;

namespace RuneCode
{
    /// <summary>시험 도크 시나리오 이름 검사, 시나리오별 적 배치와 adapt_loop 재생성을 담당한다.</summary>
    internal sealed class BenchScenario
    {
        private readonly SimulationWorld _world;
        private readonly SpawnSystem _spawn;
        private string _scenario;

        /// <summary>현재 시험 시나리오 이름이다. 미션이면 null이다.</summary>
        internal string Scenario => _scenario;

        /// <summary>월드와 적 생성에 쓸 스폰 시스템 참조로 만든다.</summary>
        internal BenchScenario(SimulationWorld world, SpawnSystem spawn)
        {
            _world = world;
            _spawn = spawn;
        }

        /// <summary>시나리오 이름을 검사하고 기록한다. 알 수 없는 이름이면 상태를 바꾸지 않고 ArgumentException을 던진다.</summary>
        internal void Begin(string scenario)
        {
            if (scenario != "dummy_single" && scenario != "dummy_line" && scenario != "dummy_swarm" && scenario != "aegis" && scenario != "adapt_loop") throw new ArgumentException("알 수 없는 시험 시나리오입니다.", nameof(scenario));
            _scenario = scenario;
        }

        /// <summary>현재 시나리오의 적을 배치한다. dummyCount가 0이면 일렬 배치는 기본 수를 사용한다.</summary>
        internal void PlaceEnemies(int dummyCount)
        {
            SimulationBalance bench = _world.Balance.Sim;
            if (_scenario == "dummy_line")
            {
                int count = dummyCount == 0 ? bench.BenchLineCount : dummyCount;
                for (int i = count - 1; i >= 0; i--)
                    _spawn.SpawnBenchDummy(new SimVector(bench.BenchLineStartX + i * bench.BenchLineGap, bench.BenchDummyY), bench.BenchHp);
            }
            else if (_scenario == "dummy_swarm")
                for (int i = 0; i < bench.BenchSwarmColumns * bench.BenchSwarmRows; i++) _spawn.SpawnEnemy("enemy.scout", new SimVector(bench.BenchSwarmX + (i % bench.BenchSwarmColumns) * bench.BenchSwarmGapX, bench.BenchSwarmY + (i / bench.BenchSwarmColumns) * bench.BenchSwarmGapY), false, bench.BenchHp);
            else if (_scenario == "aegis") _spawn.SpawnEnemy("enemy.aegis", new SimVector(bench.BenchDummyX, bench.BenchDummyY), true, bench.BenchHp);
            else _spawn.SpawnBenchDummy(new SimVector(bench.BenchDummyX, bench.BenchDummyY), bench.BenchHp);
        }

        /// <summary>adapt_loop 시나리오에서 적이 모두 사라졌으면 기본 위치에 더미를 다시 만든다.</summary>
        internal void RespawnAdaptDummy()
        {
            if (_scenario == "adapt_loop" && _world.Enemies.Count == 0) _spawn.SpawnBenchDummy(new SimVector(_world.Balance.Sim.BenchDummyX, _world.Balance.Sim.BenchDummyY), _world.Balance.Sim.BenchHp);
        }
    }
}
