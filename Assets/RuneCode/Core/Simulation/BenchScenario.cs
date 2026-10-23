using System;

namespace RuneCode
{
    /// <summary>시험 도크 시나리오 이름 검사, 시나리오별 적 배치와 adapt_loop 재생성을 담당한다.</summary>
    internal sealed class BenchScenario
    {
        internal const string MEASURE_SINGLE = "measure_single";
        internal const string MEASURE_SWARM = "measure_swarm";
        private const double MEASURE_GRID_GAP = 40;
        private const int MEASURE_SWARM_DEPTH_COUNT = 3;
        private const int MEASURE_SWARM_WIDTH_COUNT = 5;
        // 측정 적이 죽지 않을 만큼 크면서 피해 차감이 체력 값에 반영되는 정밀도를 유지하는 체력. float 최대값은 피해를 빼도 값이 변하지 않아 체력 감소를 조건으로 하는 후속 행동이 발동하지 않는다.
        private const double MEASURE_ENEMY_HP = 1e12;

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
            if (scenario != "dummy_single" && scenario != "dummy_line" && scenario != "dummy_swarm" && scenario != "aegis" && scenario != "adapt_loop"
                && scenario != MEASURE_SINGLE && scenario != MEASURE_SWARM) throw new ArgumentException("알 수 없는 시험 시나리오입니다.", nameof(scenario));
            _scenario = scenario;
        }

        /// <summary>현재 시나리오의 적을 배치한다. dummyCount가 0이면 일렬 배치는 기본 수를 사용한다. 측정 시나리오는 measurementDistance를 적 거리로 쓰고 나머지 시나리오는 무시한다.</summary>
        internal void PlaceEnemies(int dummyCount, double measurementDistance = 0)
        {
            if (_scenario == MEASURE_SINGLE || _scenario == MEASURE_SWARM) { PlaceMeasurementEnemies(measurementDistance); return; }
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

        /// <summary>측정 시나리오의 무반격 적을 배치한다. measure_single은 지정 거리에 1마리, measure_swarm은 지정 거리를 격자 중심으로 3열 × 5행 15마리를 둔다.</summary>
        private void PlaceMeasurementEnemies(double measurementDistance)
        {
            if (measurementDistance <= 0) throw new ArgumentOutOfRangeException(nameof(measurementDistance), "측정 시나리오는 0보다 큰 측정 거리가 필요합니다.");
            SimulationBalance bench = _world.Balance.Sim;
            SimVector center = new SimVector(bench.BenchPlayerX + measurementDistance, bench.BenchPlayerY);
            if (_scenario == MEASURE_SINGLE)
            {
                _spawn.SpawnEnemy("enemy.scout", center, true, MEASURE_ENEMY_HP);
                return;
            }
            // 깊이(x축)는 -40, 0, +40, 폭(y축)은 -80부터 +80까지 40 간격으로 놓는다.
            for (int depth = 0; depth < MEASURE_SWARM_DEPTH_COUNT; depth++)
                for (int width = 0; width < MEASURE_SWARM_WIDTH_COUNT; width++)
                    _spawn.SpawnEnemy("enemy.scout", new SimVector(center.X + (depth - 1) * MEASURE_GRID_GAP, center.Y + (width - 2) * MEASURE_GRID_GAP), true, MEASURE_ENEMY_HP);
        }

        /// <summary>adapt_loop 시나리오에서 적이 모두 사라졌으면 기본 위치에 더미를 다시 만든다.</summary>
        internal void RespawnAdaptDummy()
        {
            if (_scenario == "adapt_loop" && _world.Enemies.Count == 0) _spawn.SpawnBenchDummy(new SimVector(_world.Balance.Sim.BenchDummyX, _world.Balance.Sim.BenchDummyY), _world.Balance.Sim.BenchHp);
        }
    }
}
