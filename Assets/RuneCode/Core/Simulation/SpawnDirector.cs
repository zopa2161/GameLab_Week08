using System;
using System.Globalization;
using System.Text;

namespace RuneCode
{
    internal readonly struct SpawnOrder
    {
        private readonly int _tick;
        private readonly int _index;
        private readonly int _count;
        private readonly string _enemyId;
        private readonly bool _isElite;
        public int Tick => _tick;
        public int Index => _index;
        public int Count => _count;
        public string EnemyId => _enemyId;

        /// <summary>이 스폰을 웨이브가 속한 스테이지의 엘리트 정의로 생성할지 반환한다.</summary>
        public bool IsElite => _isElite;

        /// <summary>스폰 예정 틱, 웨이브 안 순서와 기체 수, 적 ID와 엘리트 여부를 보관한다.</summary>
        public SpawnOrder(int tick, int index, int count, string enemyId, bool isElite)
        { _tick = tick; _index = index; _count = count; _enemyId = enemyId; _isElite = isElite; }
    }

    /// <summary>
    /// 배율로 늘어난 웨이브를 원본 편성 크기의 무리로 나누고, 무리 간격과 무리 안 진형 주기에 맞춰 배출한다. 시작 시 예약 목록을 만들지 않고
    /// TryPeek가 현재 틱까지 도달한 다음 스폰을 순서대로 내준다. 위치 계산과 적 생성은 시뮬레이션이 한다.
    /// </summary>
    internal sealed class SpawnDirector
    {
        private readonly int _tickRate;
        private readonly WaveDefinition _wave;
        private readonly FormationSettings _settings;
        private readonly int _startTick;
        private readonly int[] _unitCounts;
        private readonly int _unitCount;
        private readonly int _batchGapTicks;
        private int _unitIndex;
        private int _batchIndex;
        private int _batchUnitIndex;
        private int _batchStartTick;
        private bool _isStarted;
        private double _startAngle;
        private int _anchor;
        private SimVector _anchorPoint;

        /// <summary>웨이브의 진형·일반 적 수 배율·무리 간격으로 진행기를 만든다. 보스 카탈로그에 등록된 적은 첫 무리에서 원래 수만 배출한다.</summary>
        public SpawnDirector(WaveDefinition wave, FormationCatalog formations, int tickRate, int startTick,
            double countMultiplier = 1, BossCatalog bosses = null, double batchIntervalSeconds = 0)
        {
            _tickRate = tickRate;
            _wave = wave;
            _settings = wave.Formation.ApplyTo(formations.Get(wave.Formation.Id).ToSettings());
            _startTick = startTick;
            _batchStartTick = startTick;
            _batchGapTicks = Math.Max(0, (int)Math.Ceiling(batchIntervalSeconds * tickRate));
            _unitCounts = new int[wave.Units.Count];
            for (int index = 0; index < wave.Units.Count; index++)
            {
                WaveUnit unit = wave.Units[index];
                bool isBoss = bosses != null && bosses.TryGet(unit.EnemyId, out _);
                int count = isBoss ? unit.Count : checked((int)Math.Max(1,
                    Math.Round(unit.Count * countMultiplier, MidpointRounding.AwayFromZero)));
                _unitCounts[index] = count;
                _unitCount = checked(_unitCount + count);
            }
        }

        internal bool IsFinished => _unitIndex >= _unitCount;

        /// <summary>웨이브에서 아직 배출하지 않은 편성 수를 반환한다.</summary>
        internal int RemainingWaveSpawns => _unitCount - _unitIndex;

        /// <summary>현재 틱까지 도달한 다음 스폰이 있으면 꺼내지 않고 반환한다.</summary>
        public bool TryPeek(int tick, out SpawnOrder order)
        {
            int nextTick = _batchStartTick + (int)Math.Round(_batchUnitIndex * _settings.Interval * _tickRate);
            if (IsFinished || nextTick > tick) { order = default; return false; }
            WaveUnit unit = GetWaveUnit(_batchUnitIndex);
            order = new SpawnOrder(nextTick, _batchUnitIndex, GetBatchUnitCount(), unit.EnemyId, unit.IsElite);
            return true;
        }

        /// <summary>배출한 스폰을 집계하고, 새 무리의 배치를 초기화하거나 무리 완료 시 실제 배출 틱부터 다음 무리 간격을 예약한다.</summary>
        public void Consume(int tick)
        {
            if (!TryPeek(tick, out SpawnOrder order)) return;
            if (_batchUnitIndex == 0) _isStarted = false;
            _unitIndex++;
            _batchUnitIndex++;
            if (_batchUnitIndex < order.Count) return;
            _batchIndex++;
            _batchUnitIndex = 0;
            _batchStartTick = tick + _batchGapTicks;
        }

        /// <summary>웨이브의 진형 설정(재정의 적용 후)을 반환한다.</summary>
        public FormationSettings Settings => _settings;

        /// <summary>웨이브의 시작 각도와 기준 슬롯이 정해졌는지 반환한다.</summary>
        public bool IsStarted => _isStarted;

        /// <summary>웨이브가 처음 스폰할 때 정한 슬롯 시작 각도와 기준 슬롯을 기록한다.</summary>
        public void Start(double startAngle, int anchor) { _isStarted = true; _startAngle = startAngle; _anchor = anchor; }

        /// <summary>맵형 무리 진형 웨이브가 처음 스폰할 때 정한 무리 중심 맵 좌표를 기록한다. 슬롯 각도와 기준 슬롯은 쓰지 않는다.</summary>
        public void StartAt(SimVector anchorPoint) { Start(0, -1); _anchorPoint = anchorPoint; }

        /// <summary>웨이브의 무리 중심 맵 좌표를 반환한다. StartAt으로 시작한 웨이브에서만 의미가 있다.</summary>
        public SimVector AnchorPoint => _anchorPoint;

        /// <summary>웨이브의 슬롯 시작 각도(라디안)를 반환한다.</summary>
        public double StartAngle => _startAngle;

        /// <summary>웨이브의 기준 슬롯을 반환한다. 기준 슬롯을 쓰지 않으면 -1이다.</summary>
        public int Anchor => _anchor;

        /// <summary>웨이브 시작 틱·배출 순번과 시작된 웨이브의 각도·기준 슬롯·무리 중심을 결정성 해시 버퍼에 기록한다.</summary>
        public void WriteState(StringBuilder state)
        {
            state.Append(_startTick).Append(':').Append(_unitIndex).Append('|');
            state.Append("batch:").Append(_batchIndex).Append(':').Append(_batchUnitIndex)
                .Append(':').Append(_batchStartTick).Append(':').Append(_batchGapTicks).Append('|');
            foreach (int count in _unitCounts) state.Append("spawnCount:").Append(count).Append('|');
            if (!_isStarted) return;
            state.Append(_startAngle.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(_anchor)
                .Append(':').Append(_anchorPoint.X.ToString("R", CultureInfo.InvariantCulture))
                .Append(':').Append(_anchorPoint.Y.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        /// <summary>확대된 편성 수에서 배출 순번이 속한 원본 웨이브 유닛을 찾아 반환한다.</summary>
        private WaveUnit GetWaveUnit(int unitIndex)
        {
            int remaining = unitIndex;
            for (int index = 0; index < _wave.Units.Count; index++)
            {
                int count = GetUnitBatchCount(index);
                if (remaining < count) return _wave.Units[index];
                remaining -= count;
            }
            return _wave.Units[_wave.Units.Count - 1];
        }

        /// <summary>현재 무리에서 특정 원본 유닛이 배출할 수를 반환한다. 원본 편성 수만큼씩 나누고 마지막 무리에는 나머지만 배출한다.</summary>
        private int GetUnitBatchCount(int index)
        {
            int originalCount = _wave.Units[index].Count;
            long remaining = _unitCounts[index] - (long)_batchIndex * originalCount;
            return (int)Math.Max(0, Math.Min(originalCount, remaining));
        }

        /// <summary>현재 무리에서 배출할 일반·엘리트·보스의 총수를 반환한다.</summary>
        private int GetBatchUnitCount()
        {
            int total = 0;
            for (int index = 0; index < _unitCounts.Length; index++) total += GetUnitBatchCount(index);
            return total;
        }
    }
}
