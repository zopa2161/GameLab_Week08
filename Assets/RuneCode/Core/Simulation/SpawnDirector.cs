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
    /// 웨이브 하나의 유닛을 시작 틱부터 진형 주기 간격으로 한 번씩 배출한다. 시작 시 예약 목록을 만들지 않고
    /// TryPeek가 현재 틱까지 도달한 다음 스폰을 순서대로 내준다. 위치 계산과 적 생성은 시뮬레이션이 한다.
    /// </summary>
    internal sealed class SpawnDirector
    {
        private readonly int _tickRate;
        private readonly WaveDefinition _wave;
        private readonly FormationSettings _settings;
        private readonly int _startTick;
        private int _unitIndex;
        private bool _isStarted;
        private double _startAngle;
        private int _anchor;
        private SimVector _anchorPoint;

        /// <summary>웨이브의 진형 재정의를 적용한 설정을 읽고 startTick부터 유닛을 배출하는 스폰 진행기를 만든다.</summary>
        public SpawnDirector(WaveDefinition wave, FormationCatalog formations, int tickRate, int startTick)
        {
            _tickRate = tickRate;
            _wave = wave;
            _settings = wave.Formation.ApplyTo(formations.Get(wave.Formation.Id).ToSettings());
            _startTick = startTick;
        }

        internal bool IsFinished => _unitIndex >= _wave.UnitCount;

        /// <summary>웨이브에서 아직 배출하지 않은 편성 수를 반환한다.</summary>
        internal int RemainingWaveSpawns => _wave.UnitCount - _unitIndex;

        /// <summary>현재 틱까지 도달한 다음 스폰이 있으면 꺼내지 않고 반환한다.</summary>
        public bool TryPeek(int tick, out SpawnOrder order)
        {
            int nextTick = _startTick + (int)Math.Round(_unitIndex * _settings.Interval * _tickRate);
            if (IsFinished || nextTick > tick) { order = default; return false; }
            WaveUnit unit = GetWaveUnit(_unitIndex);
            order = new SpawnOrder(nextTick, _unitIndex, _wave.UnitCount, unit.EnemyId, unit.IsElite);
            return true;
        }

        /// <summary>TryPeek로 확인한 스폰을 처리 완료로 표시한다.</summary>
        public void Consume(int tick)
        {
            if (TryPeek(tick, out _)) _unitIndex++;
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
            if (!_isStarted) return;
            state.Append(_startAngle.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(_anchor)
                .Append(':').Append(_anchorPoint.X.ToString("R", CultureInfo.InvariantCulture))
                .Append(':').Append(_anchorPoint.Y.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        /// <summary>웨이브 안 유닛 순번의 웨이브 유닛을 편성에서 찾아 반환한다. 편성 수와 UnitCount가 같으므로 끝에 도달하지 않는다.</summary>
        private WaveUnit GetWaveUnit(int unitIndex)
        {
            int remaining = unitIndex;
            foreach (WaveUnit unit in _wave.Units)
            {
                if (remaining < unit.Count) return unit;
                remaining -= unit.Count;
            }
            return _wave.Units[_wave.Units.Count - 1];
        }
    }
}
