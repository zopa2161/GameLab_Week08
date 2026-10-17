using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    [Serializable]
    public sealed class StageMapSettings
    {
        [Header("맵 구성")]
        [SerializeField] private int _roomColumns;
        [SerializeField] private int _roomRows;
        [SerializeField] private int _roomsX;
        [SerializeField] private int _roomsY;
        public int RoomColumns => _roomColumns;
        public int RoomRows => _roomRows;
        public int RoomsX => _roomsX;
        public int RoomsY => _roomsY;
        public int Columns => RoomColumns * RoomsX;
        public int Rows => RoomRows * RoomsY;
    }

    [Serializable]
    public sealed class StageGrowth
    {
        [Header("스테이지 성장")]
        [SerializeField] private double _hpStageScale;
        [SerializeField] private double _damageStageScale;
        [SerializeField] private double _clearRewardStageScale;
        [SerializeField] private double _killTargetStageScale;
        public double HpStageScale => _hpStageScale;
        public double DamageStageScale => _damageStageScale;
        public double ClearRewardStageScale => _clearRewardStageScale;
        public double KillTargetStageScale => _killTargetStageScale;

        /// <summary>스테이지 번호로 체력 배율을 계산해 반환한다. 입력 stage는 1부터 시작한다.</summary>
        public double GetHpMultiplier(int stage) => 1 + (stage - 1) * _hpStageScale;

        /// <summary>스테이지 번호로 피해 배율을 계산해 반환한다. 입력 stage는 1부터 시작한다.</summary>
        public double GetDamageMultiplier(int stage) => 1 + (stage - 1) * _damageStageScale;

        /// <summary>스테이지 기본 클리어 보상에 스테이지 번호 성장 계수를 적용한 보상을 반환한다.</summary>
        public int GetClearReward(int baseReward, int stage) => (int)Math.Round(baseReward * (1 + (stage - 1) * _clearRewardStageScale), MidpointRounding.AwayFromZero);

        /// <summary>스테이지 기본 목표 처치 수에 스테이지 번호 성장 계수를 적용한 목표 처치 수를 반환한다.</summary>
        public int GetKillTarget(int baseTarget, int stage) => (int)Math.Round(baseTarget * (1 + (stage - 1) * _killTargetStageScale), MidpointRounding.AwayFromZero);
    }

    [Serializable]
    public sealed class StageSpawnSettings
    {
        [Header("스폰 설정")]
        [SerializeField] private int _ringSlots;
        [SerializeField] private double _ringMargin;
        [SerializeField] private double _jitterScale;
        [SerializeField] private double _reinforcementGap;
        public int RingSlots => _ringSlots;
        public double RingMargin => _ringMargin;
        public double JitterScale => _jitterScale;
        public double ReinforcementGap => _reinforcementGap;
    }

    [Serializable]
    public sealed class EnemyReward
    {
        [Header("적 보상")]
        [SerializeField] private string _enemyId;
        [SerializeField] private int _reward;
        public string EnemyId => _enemyId;
        public int Reward => _reward;
    }

    [Serializable]
    public sealed class StreamCandidate
    {
        [Header("유입 후보")]
        [SerializeField] private string _enemyId;
        [SerializeField] private int _firstStage;
        [SerializeField] private double _weight;
        public string EnemyId => _enemyId;
        public int FirstStage => _firstStage;
        public double Weight => _weight;
    }

    [Serializable]
    public sealed class StreamDefinition
    {
        [Header("적 유입")]
        [SerializeField] private StreamCandidate[] _candidates;
        [SerializeField] private double _baseInterval;
        [SerializeField] private double _minInterval;
        [SerializeField] private double _stageScale;
        [SerializeField] private double _intraRamp;
        [SerializeField] private double _rampSeconds;
        [SerializeField] private string _formation;
        public IReadOnlyList<StreamCandidate> Candidates => _candidates;
        public double BaseInterval => _baseInterval;
        public double MinInterval => _minInterval;
        public double StageScale => _stageScale;
        public double IntraRamp => _intraRamp;
        public double RampSeconds => _rampSeconds;
        public string Formation => _formation;

        /// <summary>스테이지 배율과 스테이지 내 경과 시간에 따른 가속을 반영해 다음 스폰까지의 틱 수를 반환한다. tick은 현재 틱 번호, tickRate는 초당 틱 수다.</summary>
        public int GetIntervalTicks(int stage, int tick, int tickRate)
        {
            double time = tick * (1.0 / tickRate);
            double progress = Math.Max(0, Math.Min(1, time / _rampSeconds));
            double seconds = _baseInterval / (1 + (stage - 1) * _stageScale) * (1 - progress * _intraRamp);
            return Math.Max(1, (int)Math.Round(Math.Max(_minInterval, seconds) * tickRate));
        }
    }

    public readonly struct FormationSettings
    {
        private readonly string _shape;
        private readonly string _angle;
        private readonly double _spacing;
        private readonly double _interval;
        private readonly int _slots;

        /// <summary>모양, 각도, 간격, 주기, 슬롯 수를 받아 읽기 전용 진형 설정을 만든다.</summary>
        public FormationSettings(string shape, string angle, double spacing, double interval, int slots)
        {
            _shape = shape; _angle = angle; _spacing = spacing; _interval = interval; _slots = slots;
        }

        public string Shape => _shape;
        public string Angle => _angle;
        public double Spacing => _spacing;
        public double Interval => _interval;
        public int Slots => _slots;
    }

    [Serializable]
    public sealed class FormationDefinition
    {
        [Header("진형")]
        [SerializeField] private string _id;
        [SerializeField] private string _shape;
        [SerializeField] private string _angle;
        [SerializeField] private double _spacing;
        [SerializeField] private double _interval;
        [SerializeField] private int _slots;
        public string Id => _id;
        public string Shape => _shape;
        public string Angle => _angle;
        public double Spacing => _spacing;
        public double Interval => _interval;
        public int Slots => _slots;

        /// <summary>직렬화 진형 정의를 런타임용 읽기 전용 설정으로 변환해 반환한다.</summary>
        public FormationSettings ToSettings()
        { return new FormationSettings(_shape, _angle, _spacing, _interval, _slots); }
    }

    [Serializable]
    public sealed class FormationOverride
    {
        [Header("진형 재정의")]
        [SerializeField] private string _id;
        [SerializeField] private string _angle;
        [SerializeField] private double _spacing;
        [SerializeField] private double _interval;
        [SerializeField] private int _slots;
        public string Id => _id;
        public string Angle => _angle;
        public double Spacing => _spacing;
        public double Interval => _interval;
        public int Slots => _slots;

        /// <summary>빈 문자열과 0인 값은 source의 원본 값을 쓰고 나머지는 이 재정의 값으로 덮어쓴 새 설정을 반환한다. shape는 항상 source를 따른다.</summary>
        public FormationSettings ApplyTo(FormationSettings source)
        {
            string angle = string.IsNullOrEmpty(_angle) ? source.Angle : _angle;
            double spacing = _spacing == 0 ? source.Spacing : _spacing;
            double interval = _interval == 0 ? source.Interval : _interval;
            int slots = _slots == 0 ? source.Slots : _slots;
            return new FormationSettings(source.Shape, angle, spacing, interval, slots);
        }
    }

    [Serializable]
    public sealed class WaveUnit
    {
        [Header("웨이브 유닛")]
        [SerializeField] private string _enemyId;
        [SerializeField] private int _count;
        public string EnemyId => _enemyId;
        public int Count => _count;
    }

    [Serializable]
    public sealed class WaveDefinition
    {
        [Header("웨이브")]
        [SerializeField] private double _at;
        [SerializeField] private double _repeatInterval;
        [SerializeField] private FormationOverride _formation;
        [SerializeField] private WaveUnit[] _units;
        public double At => _at;
        public double RepeatInterval => _repeatInterval;
        public bool IsRepeating => _repeatInterval > 0;
        public FormationOverride Formation => _formation;
        public IReadOnlyList<WaveUnit> Units => _units;
        public int UnitCount
        { get { int total = 0; for (int i = 0; i < _units.Length; i++) total += _units[i].Count; return total; } }
    }

    [Serializable]
    public sealed class EliteSpawnDefinition
    {
        [Header("엘리트 스폰")]
        [SerializeField] private string _enemyId;
        [SerializeField] private double _hpMultiplier;
        [SerializeField] private double _rewardMultiplier;
        [SerializeField] private double _speedMultiplier;
        [SerializeField] private double _radiusMultiplier;
        [SerializeField] private string _dropTable;
        public string EnemyId => _enemyId;
        // 배율 0은 데이터에 기재하지 않은 것으로 보고 1을 쓴다. 이후 강화 특징도 같은 방식으로 필드를 확장한다.
        public double HpMultiplier => _hpMultiplier > 0 ? _hpMultiplier : 1;
        public double RewardMultiplier => _rewardMultiplier > 0 ? _rewardMultiplier : 1;
        public double SpeedMultiplier => _speedMultiplier > 0 ? _speedMultiplier : 1;
        /// <summary>엘리트의 크기 배율이다. 데이터에 기재하지 않으면 1을 반환한다.</summary>
        public double RadiusMultiplier => _radiusMultiplier > 0 ? _radiusMultiplier : 1;

        /// <summary>엘리트 처치 시 추첨할 드롭 테이블 ID다. 데이터에 기재하지 않으면 드롭이 없다.</summary>
        public string DropTable => _dropTable;
        internal double RawHpMultiplier => _hpMultiplier;
        internal double RawRewardMultiplier => _rewardMultiplier;
        internal double RawSpeedMultiplier => _speedMultiplier;
        internal double RawRadiusMultiplier => _radiusMultiplier;

        /// <summary>엘리트 종족과 체력·보상·속도·반경 배율 및 드롭 테이블 ID로 디버그 스폰용 실행 상태를 만든다.</summary>
        internal EliteSpawnDefinition(string enemyId, double hpMultiplier, double rewardMultiplier, double speedMultiplier,
            double radiusMultiplier, string dropTable = null)
        {
            _enemyId = enemyId;
            _hpMultiplier = hpMultiplier;
            _rewardMultiplier = rewardMultiplier;
            _speedMultiplier = speedMultiplier;
            _radiusMultiplier = radiusMultiplier;
            _dropTable = dropTable;
        }
    }

    [Serializable]
    public sealed class ModifierRewardDefinition
    {
        [Header("Modifier 보상")]
        [SerializeField] private string _runeId;
        [SerializeField] private string _grade;
        [SerializeField] private int _count;
        public string RuneId => _runeId;

        /// <summary>지급할 등급(C·B·A·S)이다. 비어 있으면 지급 시 그 Modifier에서 값이 있는 가장 낮은 등급을 쓴다.</summary>
        public string Grade => _grade;
        public int Count => _count;

        /// <summary>드롭 추첨으로 정한 룬 ID·등급·수량을 결과 화면과 정산에서 사용할 보상으로 만든다.</summary>
        public ModifierRewardDefinition(string runeId, string grade, int count)
        {
            _runeId = runeId;
            _grade = grade;
            _count = count;
        }
    }

    [Serializable]
    public sealed class StageDefinition
    {
        [Header("스테이지")]
        [SerializeField] private int _fromStage;
        [SerializeField] private int _toStage;
        [SerializeField] private string _kind;
        [SerializeField] private double _timeLimit;
        [SerializeField] private int _clearReward;
        [SerializeField] private int _killTarget;
        [SerializeField] private StreamDefinition _stream;
        [SerializeField] private WaveDefinition[] _waves;
        [SerializeField] private int _eliteEvery;
        [SerializeField] private EliteSpawnDefinition[] _eliteTypes;
        [SerializeField] private ModifierRewardDefinition[] _modifierRewards;
        public int FromStage => _fromStage;
        public int ToStage => _toStage;
        public string Kind => _kind;
        public double TimeLimit => _timeLimit;
        public int ClearReward => _clearReward;
        public int KillTarget => _killTarget;
        public StreamDefinition Stream => _stream;
        public IReadOnlyList<WaveDefinition> Waves => _waves ?? Array.Empty<WaveDefinition>();
        public int EliteEvery => _eliteEvery;
        public IReadOnlyList<EliteSpawnDefinition> EliteTypes => _eliteTypes ?? Array.Empty<EliteSpawnDefinition>();

        /// <summary>이 항목의 스테이지를 처음 클리어할 때 주는 Modifier 소지량 보상이다. 없으면 빈 목록이다.</summary>
        public IReadOnlyList<ModifierRewardDefinition> ModifierRewards => _modifierRewards ?? Array.Empty<ModifierRewardDefinition>();
        public bool IsBoss => _kind == "boss";
        public bool IsSingle => _toStage == _fromStage;

        /// <summary>스테이지 번호가 이 항목 범위 안인지 반환한다. toStage가 0이면 상한이 없다.</summary>
        public bool Contains(int stage) => stage >= _fromStage && (_toStage == 0 || stage <= _toStage);

        /// <summary>기본 흐름 스폰 순번이 엘리트 주기의 배수이면 순회할 엘리트 정의를 반환하고 아니면 false를 반환한다.</summary>
        public bool TryGetElite(int streamSpawnIndex, out EliteSpawnDefinition elite)
        {
            elite = null;
            if (_eliteEvery <= 0 || streamSpawnIndex % _eliteEvery != 0) return false;
            IReadOnlyList<EliteSpawnDefinition> types = EliteTypes;
            elite = types[(streamSpawnIndex / _eliteEvery - 1) % types.Count];
            return true;
        }
    }

    [Serializable]
    public sealed class FormationCatalog
    {
        [Header("진형 목록")]
        [SerializeField] private FormationDefinition[] _formations;
        public IReadOnlyList<FormationDefinition> Formations => _formations;

        /// <summary>진형 ID에 해당하는 정의를 반환하고 누락된 ID에는 데이터 오류를 발생시킨다.</summary>
        public FormationDefinition Get(string id)
        { for (int i = 0; i < _formations.Length; i++) if (_formations[i].Id == id) return _formations[i]; throw new FormatException("등록되지 않은 진형: " + id); }

        /// <summary>진형 ID의 존재 여부를 반환한다.</summary>
        public bool Has(string id)
        { for (int i = 0; i < _formations.Length; i++) if (_formations[i].Id == id) return true; return false; }

        /// <summary>JSON 진형 설정을 읽어 배열 구성, ID 중복, 모양·각도·값 범위와 유한값을 검증한다.</summary>
        public static FormationCatalog FromJson(string json)
        {
            var catalog = JsonUtility.FromJson<FormationCatalog>(json);
            if (catalog == null || catalog._formations == null || catalog._formations.Length == 0) throw new FormatException("진형 설정이 비어 있습니다.");
            var ids = new HashSet<string>();
            foreach (FormationDefinition formation in catalog._formations)
            {
                if (string.IsNullOrEmpty(formation.Id) || !ids.Add(formation.Id)) throw new FormatException("진형 ID가 비었거나 중복입니다: " + formation.Id);
                if (!SpawnFormations.IsKnownShape(formation.Shape)) throw new FormatException("구현되지 않은 진형 모양입니다: " + formation.Shape + " (진형 " + formation.Id + ")");
                if (formation.Angle != "random" && formation.Angle != "ahead") throw new FormatException("진형 각도는 random 또는 ahead여야 합니다: " + formation.Id);
                if (!IsFinite(formation.Spacing) || !IsFinite(formation.Interval)) throw new FormatException("진형 값은 유한해야 합니다: " + formation.Id);
                if (formation.Spacing < 0 || formation.Interval < 0 || formation.Slots < 0) throw new FormatException("진형 값 범위 오류입니다: " + formation.Id);
            }
            return catalog;
        }

        /// <summary>값이 NaN이나 무한대가 아닌지 반환한다.</summary>
        private static bool IsFinite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    [Serializable]
    public sealed class StageCatalog
    {
        internal const int ENDLESS_STAGE_START = 11;
        internal const int ENDLESS_CYCLE_FIRST_STAGE = 6;
        internal const int ENDLESS_CYCLE_LAST_STAGE = 10;
        internal const double ENDLESS_HP_SCALE_MULTIPLIER = 1.5;
        internal const double ENDLESS_HP_BASE_MULTIPLIER = 5;
        internal const int ENDLESS_KILL_TARGET = 50;

        [Header("스테이지 루트")]
        [SerializeField] private StageMapSettings _map;
        [SerializeField] private StageGrowth _growth;
        [SerializeField] private StageSpawnSettings _spawn;
        [SerializeField] private double _stationaryAdvanceSpeed;
        [SerializeField] private EnemyReward[] _rewards;
        [SerializeField] private StageDefinition[] _stages;
        public StageMapSettings Map => _map;
        public StageGrowth Growth => _growth;
        public StageSpawnSettings Spawn => _spawn;
        public double StationaryAdvanceSpeed => _stationaryAdvanceSpeed;
        public IReadOnlyList<EnemyReward> Rewards => _rewards;
        public IReadOnlyList<StageDefinition> Stages => _stages;

        /// <summary>
        /// 스테이지 번호의 정의를 반환한다. 단일 항목을 먼저 찾고 없으면 첫 범위 항목을 사용하며 둘 다 없으면 데이터 오류를 발생시킨다.
        /// 11스테이지부터는 무한 모드로 6~10스테이지 정의를 순환해서 재사용한다.
        /// </summary>
        public StageDefinition Get(int stage)
        {
            if (stage >= ENDLESS_STAGE_START) stage = ResolveEndlessStage(stage);
            for (int i = 0; i < _stages.Length; i++)
                if (_stages[i].IsSingle && _stages[i].FromStage == stage) return _stages[i];
            for (int i = 0; i < _stages.Length; i++)
                if (_stages[i].Contains(stage)) return _stages[i];
            throw new FormatException("스테이지 정의가 없습니다: " + stage);
        }

        /// <summary>무한 모드 스테이지 번호를 6~10스테이지 정의 하나로 순환 배치한다. 11→6, 12→7, … 15→10, 16→6 순서다.</summary>
        private static int ResolveEndlessStage(int stage)
        {
            int cycleLength = ENDLESS_CYCLE_LAST_STAGE - ENDLESS_CYCLE_FIRST_STAGE + 1;
            return ENDLESS_CYCLE_FIRST_STAGE + (stage - ENDLESS_STAGE_START) % cycleLength;
        }

        /// <summary>
        /// 스테이지 체력 배율을 반환한다. 10스테이지까지는 성장표의 스테이지당 증가율을 쓰고,
        /// 11스테이지부터는 무한 모드 기준 배율(ENDLESS_HP_BASE_MULTIPLIER)에 같은 증가율의 1.5배를 더한다.
        /// </summary>
        public double GetHpMultiplier(int stage)
        {
            if (stage < ENDLESS_STAGE_START) return Growth.GetHpMultiplier(stage);
            return ENDLESS_HP_BASE_MULTIPLIER
                + (stage - ENDLESS_CYCLE_LAST_STAGE) * Growth.HpStageScale * ENDLESS_HP_SCALE_MULTIPLIER;
        }

        /// <summary>
        /// 스테이지 목표 처치 수를 반환한다. 보스 스테이지는 0이고, 11스테이지부터는 50으로 고정하며,
        /// 그 외에는 단일 항목은 데이터 값을, 범위 항목은 스테이지 성장 배율을 적용한 값을 쓴다.
        /// </summary>
        public int GetKillTarget(StageDefinition stage, int stageNumber)
        {
            if (stage.IsBoss) return 0;
            if (stageNumber >= ENDLESS_STAGE_START) return ENDLESS_KILL_TARGET;
            return stage.IsSingle ? stage.KillTarget : Growth.GetKillTarget(stage.KillTarget, stageNumber);
        }

        /// <summary>적 ID의 보상을 반환하고 보상표에 없는 적은 fallback 값을 돌려준다.</summary>
        public int GetReward(string enemyId, int fallback)
        {
            for (int i = 0; i < _rewards.Length; i++)
                if (_rewards[i].EnemyId == enemyId) return _rewards[i].Reward;
            return fallback;
        }

        /// <summary>JSON 스테이지 설정을 읽어 맵, 성장, 스폰, 보상, 스테이지 범위, 스트림, 웨이브, 엘리트 주기와 드롭 테이블 참조를 검증한다.</summary>
        public static StageCatalog FromJson(string json, EnemyCatalog enemies, FormationCatalog formations, DropCatalog drops)
        {
            var catalog = JsonUtility.FromJson<StageCatalog>(json);
            if (catalog == null || catalog._map == null || catalog._growth == null || catalog._spawn == null
                || catalog._rewards == null || catalog._stages == null || catalog._stages.Length == 0)
                throw new FormatException("스테이지 루트 설정이 비어 있습니다.");
            ValidateRoot(catalog);
            ValidateRewards(catalog._rewards, enemies);
            var singleStages = new HashSet<int>();
            foreach (StageDefinition stage in catalog._stages) ValidateStage(stage, enemies, formations, drops, singleStages);
            catalog.Get(1); // 1스테이지를 담는 항목이 반드시 있어야 한다.
            return catalog;
        }

        /// <summary>루트의 맵 칸수, 성장 배율, 스폰 설정, 정지 전진 속도 범위와 유한값을 확인한다.</summary>
        private static void ValidateRoot(StageCatalog catalog)
        {
            if (catalog._map.RoomColumns < 1 || catalog._map.RoomRows < 1 || catalog._map.RoomsX < 1 || catalog._map.RoomsY < 1)
                throw new FormatException("맵 칸수는 1 이상이어야 합니다.");
            if (!IsFinite(catalog._growth.HpStageScale) || !IsFinite(catalog._growth.DamageStageScale)
                || catalog._growth.HpStageScale < 0 || catalog._growth.DamageStageScale < 0
                || !IsFinite(catalog._growth.ClearRewardStageScale) || catalog._growth.ClearRewardStageScale < 0
                || !IsFinite(catalog._growth.KillTargetStageScale) || catalog._growth.KillTargetStageScale < 0)
                throw new FormatException("성장 배율은 0 이상의 유한값이어야 합니다.");
            if (catalog._spawn.RingSlots < 1 || !IsFinite(catalog._spawn.RingMargin) || catalog._spawn.RingMargin < 0
                || !IsFinite(catalog._spawn.JitterScale) || catalog._spawn.JitterScale < 0
                || !IsFinite(catalog._spawn.ReinforcementGap) || catalog._spawn.ReinforcementGap < 0)
                throw new FormatException("스폰 설정 값이 유효하지 않습니다.");
            if (!IsFinite(catalog._stationaryAdvanceSpeed) || catalog._stationaryAdvanceSpeed < 0)
                throw new FormatException("정지 전진 속도는 0 이상의 유한값이어야 합니다.");
        }

        /// <summary>보상표의 적 존재, 보상 범위, ID 중복을 확인한다.</summary>
        private static void ValidateRewards(EnemyReward[] rewards, EnemyCatalog enemies)
        {
            var ids = new HashSet<string>();
            foreach (EnemyReward reward in rewards)
            {
                enemies.Get(reward.EnemyId); // 없는 적 ID면 여기서 오류가 난다.
                if (!ids.Add(reward.EnemyId) || reward.Reward < 0) throw new FormatException("적 보상 설정 오류입니다: " + reward.EnemyId);
            }
        }

        /// <summary>스테이지 항목의 번호 범위, 종류, 제한시간, 클리어 보상, 목표 처치 수, 단일 항목 중복과 스트림·웨이브를 확인한다.</summary>
        private static void ValidateStage(StageDefinition stage, EnemyCatalog enemies, FormationCatalog formations, DropCatalog drops, HashSet<int> singleStages)
        {
            if (stage.FromStage < 1) throw new FormatException("시작 스테이지는 1 이상이어야 합니다: 스테이지 " + stage.FromStage);
            if (stage.ToStage != 0 && stage.ToStage < stage.FromStage) throw new FormatException("종료 스테이지가 시작보다 앞섭니다: 스테이지 " + stage.FromStage);
            if (stage.Kind != "normal" && stage.Kind != "boss") throw new FormatException("스테이지 종류는 normal 또는 boss여야 합니다: " + stage.Kind + " (스테이지 " + stage.FromStage + ")");
            if (!IsFinite(stage.TimeLimit)) throw new FormatException("제한시간은 유한값이어야 합니다: 스테이지 " + stage.FromStage);
            if (stage.IsBoss ? stage.TimeLimit <= 0 : stage.TimeLimit != 0)
                throw new FormatException("보스 제한시간은 0보다 크고 일반 제한시간은 0이어야 합니다: 스테이지 " + stage.FromStage);
            if (stage.ClearReward < 0) throw new FormatException("클리어 보상은 0 이상이어야 합니다: 스테이지 " + stage.FromStage);
            if (stage.IsBoss ? stage.KillTarget != 0 : stage.KillTarget < 1)
                throw new FormatException("일반 스테이지 목표 처치 수는 1 이상, 보스 스테이지는 0이어야 합니다: 스테이지 " + stage.FromStage);
            if (stage.IsSingle && !singleStages.Add(stage.FromStage)) throw new FormatException("같은 번호의 단일 스테이지 항목이 중복됩니다: 스테이지 " + stage.FromStage);
            ValidateStream(stage, enemies, formations);
            ValidateWaves(stage, enemies, formations);
            ValidateElites(stage, enemies, drops);
            ValidateModifierRewards(stage);
        }

        /// <summary>스트림의 후보 적, 시작 스테이지, 가중치, 간격 공식, 가속 구간, 진형 참조를 확인한다.</summary>
        private static void ValidateStream(StageDefinition stage, EnemyCatalog enemies, FormationCatalog formations)
        {
            StreamDefinition stream = stage.Stream;
            if (stream == null || stream.Candidates == null || stream.Candidates.Count == 0)
                throw new FormatException("스트림 후보가 비어 있습니다: 스테이지 " + stage.FromStage);
            bool hasStartCandidate = false;
            foreach (StreamCandidate candidate in stream.Candidates)
            {
                enemies.Get(candidate.EnemyId); // 없는 적 ID면 여기서 오류가 난다.
                if (candidate.FirstStage < 1 || !IsFinite(candidate.Weight) || candidate.Weight <= 0)
                    throw new FormatException("스트림 후보 값 오류입니다: " + candidate.EnemyId + " (스테이지 " + stage.FromStage + ")");
                if (candidate.FirstStage <= stage.FromStage) hasStartCandidate = true;
            }
            if (!hasStartCandidate) throw new FormatException("시작 스테이지에서 스폰 가능한 스트림 후보가 없습니다: 스테이지 " + stage.FromStage);
            if (!IsFinite(stream.BaseInterval) || !IsFinite(stream.MinInterval) || !IsFinite(stream.StageScale)
                || !IsFinite(stream.IntraRamp) || !IsFinite(stream.RampSeconds))
                throw new FormatException("스트림 값은 유한해야 합니다: 스테이지 " + stage.FromStage);
            if (stream.BaseInterval <= 0 || stream.MinInterval <= 0 || stream.MinInterval > stream.BaseInterval
                || stream.StageScale < 0 || stream.IntraRamp < 0 || stream.IntraRamp >= 1 || stream.RampSeconds <= 0)
                throw new FormatException("스트림 간격 설정 오류입니다: 스테이지 " + stage.FromStage);
            if (!formations.Has(stream.Formation)) throw new FormatException("등록되지 않은 스트림 진형입니다: " + stream.Formation);
        }

        /// <summary>웨이브의 시각, 반복 간격, 진형 재정의 참조와 값, 유닛 편성을 확인한다. waves가 없으면 확인을 생략한다.</summary>
        private static void ValidateWaves(StageDefinition stage, EnemyCatalog enemies, FormationCatalog formations)
        {
            foreach (WaveDefinition wave in stage.Waves)
            {
                if (!IsFinite(wave.At) || wave.At < 0) throw new FormatException("웨이브 시각은 0 이상의 유한값이어야 합니다: 스테이지 " + stage.FromStage);
                FormationOverride formation = wave.Formation;
                if (formation == null || !formations.Has(formation.Id)) throw new FormatException("웨이브 진형 참조 오류입니다: 스테이지 " + stage.FromStage);
                if (!string.IsNullOrEmpty(formation.Angle) && formation.Angle != "random" && formation.Angle != "ahead")
                    throw new FormatException("웨이브 진형 각도는 random 또는 ahead여야 합니다: 스테이지 " + stage.FromStage);
                if (!IsFinite(formation.Spacing) || formation.Spacing < 0 || !IsFinite(formation.Interval) || formation.Interval < 0 || formation.Slots < 0)
                    throw new FormatException("웨이브 진형 값 오류입니다: 스테이지 " + stage.FromStage);
                if (wave.Units == null || wave.Units.Count == 0) throw new FormatException("웨이브에 유닛이 없습니다: 스테이지 " + stage.FromStage);
                bool hasBossUnit = false;
                foreach (WaveUnit unit in wave.Units)
                {
                    enemies.Get(unit.EnemyId); // 없는 적 ID면 여기서 오류가 난다.
                    hasBossUnit |= unit.EnemyId.StartsWith("boss.");
                    if (unit.Count < 1) throw new FormatException("웨이브 유닛 수는 1 이상이어야 합니다: " + unit.EnemyId + " (스테이지 " + stage.FromStage + ")");
                }
                if (!IsFinite(wave.RepeatInterval) || wave.RepeatInterval < 0)
                    throw new FormatException("웨이브 반복 간격은 0 이상의 유한값이어야 합니다: 스테이지 " + stage.FromStage);
                if (!wave.IsRepeating) continue;
                if (hasBossUnit) throw new FormatException("보스가 들어 있는 웨이브는 반복할 수 없습니다: 스테이지 " + stage.FromStage);
                double interval = wave.Formation.ApplyTo(formations.Get(wave.Formation.Id).ToSettings()).Interval;
                if (wave.RepeatInterval <= (wave.UnitCount - 1) * interval)
                    throw new FormatException("반복 간격은 한 회차의 유닛 배출 시간보다 길어야 합니다: 스테이지 " + stage.FromStage);
            }
        }

        /// <summary>Modifier 보상의 룬 ID가 비어 있지 않고 수량이 1 이상인지 확인한다. 룬 존재 여부는 지급 시 확인한다.</summary>
        private static void ValidateModifierRewards(StageDefinition stage)
        {
            foreach (ModifierRewardDefinition reward in stage.ModifierRewards)
            {
                if (reward == null || string.IsNullOrEmpty(reward.RuneId) || reward.Count < 1)
                    throw new FormatException("Modifier 보상은 룬 ID와 1 이상의 수량이 필요합니다 (스테이지 " + stage.FromStage + ")");
            }
        }

        /// <summary>엘리트 주기와 순회 타입의 대상 적, 배율, 보스 종족 제외, 드롭 테이블 참조를 확인한다. 주기가 0이면 타입 확인을 생략한다.</summary>
        private static void ValidateElites(StageDefinition stage, EnemyCatalog enemies, DropCatalog drops)
        {
            if (stage.EliteEvery < 0) throw new FormatException("엘리트 주기는 0 이상이어야 합니다: 스테이지 " + stage.FromStage);
            if (stage.EliteEvery > 0 && stage.EliteTypes.Count == 0)
                throw new FormatException("엘리트 주기가 있으면 순회 타입이 1개 이상 필요합니다: 스테이지 " + stage.FromStage);
            foreach (EliteSpawnDefinition elite in stage.EliteTypes)
            {
                enemies.Get(elite.EnemyId); // 없는 적 ID면 여기서 오류가 난다.
                if (elite.EnemyId.StartsWith("boss.")) throw new FormatException("보스는 엘리트 대상이 아닙니다: " + elite.EnemyId + " (스테이지 " + stage.FromStage + ")");
                if (!IsFinite(elite.RawHpMultiplier) || !IsFinite(elite.RawRewardMultiplier) || !IsFinite(elite.RawSpeedMultiplier) || !IsFinite(elite.RawRadiusMultiplier)
                    || elite.RawHpMultiplier < 0 || elite.RawRewardMultiplier < 0 || elite.RawSpeedMultiplier < 0 || elite.RawRadiusMultiplier < 0)
                    throw new FormatException("엘리트 배율은 0 이상의 유한값이어야 합니다: " + elite.EnemyId + " (스테이지 " + stage.FromStage + ")");
                if (!string.IsNullOrEmpty(elite.DropTable) && !drops.Has(elite.DropTable))
                    throw new FormatException("등록되지 않은 엘리트 드롭 테이블입니다: " + elite.DropTable + " (" + elite.EnemyId + ", 스테이지 " + stage.FromStage + ")");
            }
        }

        /// <summary>값이 NaN이나 무한대가 아닌지 반환한다.</summary>
        private static bool IsFinite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
