using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RuneCode
{
    internal readonly struct SpawnOrder
    {
        private readonly int _tick;
        private readonly int _group;
        private readonly int _index;
        private readonly int _count;
        private readonly string _enemyId;
        public int Tick => _tick;
        public int Group => _group;
        public int Index => _index;
        public int Count => _count;
        public string EnemyId => _enemyId;
        public bool IsStream => _enemyId == null;

        /// <summary>
        /// 스폰 예정 틱, 묶음(0 = 기본 흐름, 1부터 웨이브), 묶음 안 순서와 기체 수,
        /// 적 ID(null이면 기본 흐름 후보에서 고름)를 보관한다.
        /// </summary>
        public SpawnOrder(int tick, int group, int index, int count, string enemyId)
        { _tick = tick; _group = group; _index = index; _count = count; _enemyId = enemyId; }
    }

    /// <summary>
    /// 기본 흐름 커서와 웨이브별 회차 커서로 스폰을 진행한다. 시작 시 예약 목록을 만들지 않고
    /// TryPeek가 현재 틱까지 도달한 다음 스폰을 순서대로 내주므로 기본 흐름은 무기한 이어진다.
    /// 위치 계산과 적 생성은 시뮬레이션이 한다.
    /// </summary>
    internal sealed class SpawnDirector
    {
        private const int STREAM_GROUP = 0;

        private readonly StreamDefinition _stream;
        private readonly int _stageNumber;
        private readonly int _tickRate;
        private readonly WaveDefinition[] _waves;
        private readonly FormationSettings[] _settings;
        private readonly int[] _waveStartTicks;
        private readonly int[] _waveUnitIndices;
        private readonly bool[] _isWaveDone;
        private readonly bool[] _isStarted;
        private readonly double[] _startAngles;
        private readonly int[] _anchors;
        private readonly SimVector[] _anchorPoints;
        private int _nextStreamTick;
        private int _streamUnitIndex;

        /// <summary>
        /// 스테이지 정의의 기본 흐름과 웨이브 진형 설정을 읽고 웨이브의 첫 회차 시작 틱을 시작 시각으로 정한다.
        /// 같은 틱에서는 기본 흐름이 먼저, 웨이브는 데이터 순서를 따른다.
        /// </summary>
        public SpawnDirector(StageDefinition stage, int stageNumber, FormationCatalog formations, int tickRate)
        {
            _stream = stage.Stream;
            _stageNumber = stageNumber;
            _tickRate = tickRate;
            IReadOnlyList<WaveDefinition> waveList = stage.Waves;
            int groupCount = waveList.Count + 1;
            _waves = new WaveDefinition[waveList.Count];
            _settings = new FormationSettings[groupCount];
            _waveStartTicks = new int[waveList.Count];
            _waveUnitIndices = new int[waveList.Count];
            _isWaveDone = new bool[waveList.Count];
            _isStarted = new bool[groupCount];
            _startAngles = new double[groupCount];
            _anchors = new int[groupCount];
            _anchorPoints = new SimVector[groupCount];
            _settings[STREAM_GROUP] = formations.Get(_stream.Formation).ToSettings();
            for (int wave = 0; wave < waveList.Count; wave++)
            {
                WaveDefinition definition = waveList[wave];
                _waves[wave] = definition;
                _settings[wave + 1] = definition.Formation.ApplyTo(formations.Get(definition.Formation.Id).ToSettings());
                _waveStartTicks[wave] = (int)Math.Round(definition.At * tickRate);
            }
        }

        /// <summary>현재 웨이브의 유닛만 시작 틱부터 한 번 배출하는 스폰 진행기를 만든다.</summary>
        public SpawnDirector(WaveDefinition wave, FormationCatalog formations, int tickRate, int startTick)
        {
            _tickRate = tickRate;
            _waves = new[] { wave };
            _settings = new FormationSettings[2];
            _settings[1] = wave.Formation.ApplyTo(formations.Get(wave.Formation.Id).ToSettings());
            _waveStartTicks = new[] { startTick };
            _waveUnitIndices = new int[1];
            _isWaveDone = new bool[1];
            _isStarted = new bool[2];
            _startAngles = new double[2];
            _anchors = new int[2];
            _anchorPoints = new SimVector[2];
        }

        internal bool IsFinished => _stream == null && _isWaveDone[0];

        /// <summary>단일 유한 웨이브에서 아직 배출하지 않은 편성 수를 반환한다. 기존 스트림 모드에는 적용하지 않는다.</summary>
        internal int RemainingWaveSpawns => _stream == null ? _waves[0].UnitCount - _waveUnitIndices[0] : 0;

        /// <summary>현재 틱까지 도달한 다음 스폰이 있으면 꺼내지 않고 반환한다.</summary>
        public bool TryPeek(int tick, out SpawnOrder order) => TryFindNext(tick, out order, out _);

        /// <summary>
        /// TryPeek로 확인한 스폰을 처리 완료로 표시한다. 기본 흐름은 묶음 크기만큼 같은 틱에 낸 뒤 진형 시작 상태를 해제하고,
        /// 밀린 스폰을 몰아서 내지 않게 실제 스폰 틱 기준으로 다음 묶음 간격을 다시 계산한다. 웨이브 회차가 끝나면 반복 웨이브는
        /// 반복 간격만큼 뒤의 다음 회차와 진형 시작 상태를 해제하며, 1회 웨이브는 종료로 표시한다.
        /// </summary>
        public void Consume(int tick)
        {
            if (!TryFindNext(tick, out _, out int waveIndex)) return;
            if (waveIndex < 0)
            {
                if (++_streamUnitIndex < _stream.GroupSize) return;
                _streamUnitIndex = 0;
                _isStarted[STREAM_GROUP] = false;
                int current = Math.Max(_nextStreamTick, tick);
                _nextStreamTick = current + _stream.GetIntervalTicks(_stageNumber, current, _tickRate);
                return;
            }
            WaveDefinition wave = _waves[waveIndex];
            if (++_waveUnitIndices[waveIndex] < wave.UnitCount) return;
            if (wave.IsRepeating)
            {
                _waveStartTicks[waveIndex] += Math.Max(1, (int)Math.Round(wave.RepeatInterval * _tickRate));
                _waveUnitIndices[waveIndex] = 0;
                _isStarted[waveIndex + 1] = false;
            }
            else _isWaveDone[waveIndex] = true;
        }

        /// <summary>묶음의 진형 설정(웨이브 덮어쓰기 적용 후)을 반환한다.</summary>
        public FormationSettings GetSettings(int group) => _settings[group];

        /// <summary>묶음의 시작 각도와 기준 슬롯이 정해졌는지 반환한다.</summary>
        public bool IsStarted(int group) => _isStarted[group];

        /// <summary>묶음이 처음 스폰할 때 정한 슬롯 시작 각도와 기준 슬롯을 기록한다.</summary>
        public void Start(int group, double startAngle, int anchor) { _isStarted[group] = true; _startAngles[group] = startAngle; _anchors[group] = anchor; }

        /// <summary>맵형 무리 진형 묶음이 처음 스폰할 때 정한 무리 중심 맵 좌표를 기록한다. 슬롯 각도와 기준 슬롯은 쓰지 않는다.</summary>
        public void StartAt(int group, SimVector anchorPoint) { Start(group, 0, -1); _anchorPoints[group] = anchorPoint; }

        /// <summary>묶음의 무리 중심 맵 좌표를 반환한다. StartAt으로 시작한 묶음에서만 의미가 있다.</summary>
        public SimVector GetAnchorPoint(int group) => _anchorPoints[group];

        /// <summary>묶음의 슬롯 시작 각도(라디안)를 반환한다.</summary>
        public double GetStartAngle(int group) => _startAngles[group];

        /// <summary>묶음의 기준 슬롯을 반환한다. 기준 슬롯을 쓰지 않으면 -1이다.</summary>
        public int GetAnchor(int group) => _anchors[group];

        /// <summary>기본 흐름 커서와 묶음 순번, 웨이브별 회차 시작·순번·종료 여부와 시작된 묶음의 각도·기준 슬롯·무리 중심을 결정성 해시 버퍼에 기록한다.</summary>
        public void WriteState(StringBuilder state)
        {
            state.Append(_nextStreamTick).Append(':').Append(_streamUnitIndex).Append('|');
            for (int wave = 0; wave < _waves.Length; wave++)
                state.Append(_waveStartTicks[wave]).Append(':').Append(_waveUnitIndices[wave]).Append(':').Append(_isWaveDone[wave]).Append('|');
            for (int group = 0; group < _isStarted.Length; group++)
                if (_isStarted[group])
                    state.Append(group).Append(':').Append(_startAngles[group].ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(_anchors[group])
                        .Append(':').Append(_anchorPoints[group].X.ToString("R", CultureInfo.InvariantCulture))
                        .Append(':').Append(_anchorPoints[group].Y.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        /// <summary>
        /// 현재 틱까지 도달한 다음 스폰 후보를 찾는다. 같은 틱이면 기본 흐름을 먼저,
        /// 웨이브는 데이터 순서대로 고르며, 찾은 후보의 묶음 번호를 waveIndex로 구분한다(-1이면 기본 흐름).
        /// </summary>
        private bool TryFindNext(int tick, out SpawnOrder order, out int waveIndex)
        {
            waveIndex = -1;
            bool hasStream = _stream != null && _nextStreamTick <= tick;
            int bestTick = hasStream ? _nextStreamTick : int.MaxValue;
            for (int wave = 0; wave < _waves.Length; wave++)
            {
                if (_isWaveDone[wave]) continue;
                int candidateTick = _waveStartTicks[wave] + (int)Math.Round(_waveUnitIndices[wave] * _settings[wave + 1].Interval * _tickRate);
                if (candidateTick > tick || candidateTick >= bestTick) continue;
                bestTick = candidateTick; waveIndex = wave;
            }
            if (!hasStream && waveIndex < 0) { order = default; return false; }
            if (waveIndex < 0) order = new SpawnOrder(_nextStreamTick, STREAM_GROUP, _streamUnitIndex, _stream.GroupSize, null);
            else
            {
                WaveDefinition wave = _waves[waveIndex];
                int unitIndex = _waveUnitIndices[waveIndex];
                order = new SpawnOrder(bestTick, waveIndex + 1, unitIndex, wave.UnitCount, GetWaveUnitId(wave, unitIndex));
            }
            return true;
        }

        /// <summary>회차 안 유닛 순번의 적 ID를 웨이브 편성에서 찾아 반환한다. 편성 수와 UnitCount가 같으므로 끝에 도달하지 않는다.</summary>
        private static string GetWaveUnitId(WaveDefinition wave, int unitIndex)
        {
            int remaining = unitIndex;
            foreach (WaveUnit unit in wave.Units)
            {
                if (remaining < unit.Count) return unit.EnemyId;
                remaining -= unit.Count;
            }
            return wave.Units[wave.Units.Count - 1].EnemyId;
        }
    }
}
