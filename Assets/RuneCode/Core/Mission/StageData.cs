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

        [Header("3막 체력 곡선")]
        [SerializeField] private double[] _actHpStartMultipliers = { 1, 2.5, 6 };
        [SerializeField] private double[] _actHpEndMultipliers = { 2, 5, 12 };
        [SerializeField, Min(0.01f)] private double _hpCurveExponent = 1.2;
        public double HpStageScale => _hpStageScale;
        public double DamageStageScale => _damageStageScale;

        /// <summary>스테이지 번호로 체력 배율을 계산해 반환한다. 입력 stage는 1부터 시작한다.</summary>
        public double GetHpMultiplier(int stage) => 1 + (stage - 1) * _hpStageScale;

        /// <summary>스테이지 번호로 피해 배율을 계산해 반환한다. 입력 stage는 1부터 시작한다.</summary>
        public double GetDamageMultiplier(int stage) => 1 + (stage - 1) * _damageStageScale;

        /// <summary>1부터 시작하는 막·라운드 번호를 곡선 범위로 제한해 해당 라운드의 기본 체력 배율을 반환한다.</summary>
        public double GetActHpMultiplier(int stage, int round)
        {
            int index = Math.Max(0, Math.Min(StageCatalog.PROGRESSION_STAGE_COUNT - 1, stage - 1));
            double progress = Math.Max(0, Math.Min(StageCatalog.PROGRESSION_ROUND_COUNT - 1, round - 1))
                / (double)(StageCatalog.PROGRESSION_ROUND_COUNT - 1);
            return EvaluateHpCurve(_actHpStartMultipliers[index], _actHpEndMultipliers[index], _hpCurveExponent, progress);
        }

        /// <summary>시작·종료 배율 사이를 지수 보간하고 곡선 지수로 진행률을 조절해 체력 배율을 반환한다.</summary>
        public static double EvaluateHpCurve(double start, double end, double exponent, double progress)
        {
            double curvedProgress = Math.Pow(Math.Max(0, Math.Min(1, progress)), exponent);
            return Math.Exp(Math.Log(start) * (1 - curvedProgress) + Math.Log(end) * curvedProgress);
        }

        /// <summary>막별 시작·종료 배율과 곡선 지수가 유한한 양수이며 진행 중 체력이 감소하지 않는지 검사한다.</summary>
        public void ValidateHpCurve()
        {
            if (_actHpStartMultipliers == null || _actHpEndMultipliers == null
                || _actHpStartMultipliers.Length != StageCatalog.PROGRESSION_STAGE_COUNT
                || _actHpEndMultipliers.Length != StageCatalog.PROGRESSION_STAGE_COUNT)
                throw new FormatException("체력 곡선은 3개 막의 시작·종료 배율이 필요합니다.");
            if (double.IsNaN(_hpCurveExponent) || double.IsInfinity(_hpCurveExponent) || _hpCurveExponent <= 0)
                throw new FormatException("체력 곡선 지수는 0보다 큰 유한값이어야 합니다.");
            for (int index = 0; index < _actHpStartMultipliers.Length; index++)
            {
                double start = _actHpStartMultipliers[index];
                double end = _actHpEndMultipliers[index];
                if (double.IsNaN(start) || double.IsInfinity(start) || start <= 0
                    || double.IsNaN(end) || double.IsInfinity(end) || end < start
                    || (index > 0 && start < _actHpEndMultipliers[index - 1]))
                    throw new FormatException((index + 1) + "막 체력 배율은 양수이며 이전 라운드·막보다 작아질 수 없습니다.");
            }
        }
    }

    [Serializable]
    public sealed class StageSpawnSettings
    {
        [Header("스폰 설정")]
        [SerializeField] private int _ringSlots;
        [SerializeField] private double _ringMargin;
        [SerializeField] private double _jitterScale;
        [SerializeField] private double _reinforcementGap;
        [SerializeField] private double _warningSeconds;
        [SerializeField] private double _minPlayerDistance;
        [SerializeField] private double _maxPlayerDistance;
        [SerializeField] private double _spawnGraceSeconds;
        [SerializeField, Min(0.01f)] private double _batchIntervalSeconds = 2;
        public double BatchIntervalSeconds => _batchIntervalSeconds;
        public int RingSlots => _ringSlots;
        public double RingMargin => _ringMargin;
        public double JitterScale => _jitterScale;
        public double ReinforcementGap => _reinforcementGap;

        /// <summary>맵형 스폰의 예고 표시부터 실제 생성까지의 시간(초)이다.</summary>
        public double WarningSeconds => _warningSeconds;

        /// <summary>맵형 스폰 위치가 플레이어와 떨어져야 하는 최소 거리다.</summary>
        public double MinPlayerDistance => _minPlayerDistance;

        /// <summary>맵형 스폰 위치가 플레이어와 떨어질 수 있는 최대 거리다. 0이면 제한하지 않는다.</summary>
        public double MaxPlayerDistance => _maxPlayerDistance;

        /// <summary>맵형 스폰으로 생성된 적이 접촉 피해를 주지 않는 생성 직후 시간(초)이다.</summary>
        public double SpawnGraceSeconds => _spawnGraceSeconds;
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

    public readonly struct FormationSettings
    {
        private readonly string _shape;
        private readonly string _angle;
        private readonly double _spacing;
        private readonly double _interval;
        private readonly int _slots;
        private readonly double _radiusScale;

        /// <summary>모양, 각도, 간격, 주기, 슬롯 수, 스폰 반경 배율을 받아 읽기 전용 진형 설정을 만든다.</summary>
        public FormationSettings(string shape, string angle, double spacing, double interval, int slots, double radiusScale)
        {
            _shape = shape; _angle = angle; _spacing = spacing; _interval = interval; _slots = slots; _radiusScale = radiusScale;
        }

        public string Shape => _shape;
        public string Angle => _angle;
        public double Spacing => _spacing;
        public double Interval => _interval;
        public int Slots => _slots;

        /// <summary>기본 스폰 반경에 곱할 배율이다. 0이면 데이터에 기재하지 않은 것으로 보고 1을 반환한다.</summary>
        public double RadiusScale => _radiusScale > 0 ? _radiusScale : 1;
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
        [SerializeField] private double _radiusScale;
        public string Id => _id;
        public string Shape => _shape;
        public string Angle => _angle;
        public double Spacing => _spacing;
        public double Interval => _interval;
        public int Slots => _slots;
        public double RadiusScale => _radiusScale;

        /// <summary>직렬화 진형 정의를 런타임용 읽기 전용 설정으로 변환해 반환한다.</summary>
        public FormationSettings ToSettings()
        { return new FormationSettings(_shape, _angle, _spacing, _interval, _slots, _radiusScale); }
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

        /// <summary>빈 문자열과 0인 값은 source의 원본 값을 쓰고 나머지는 이 재정의 값으로 덮어쓴 새 설정을 반환한다. shape와 스폰 반경 배율은 항상 source를 따른다.</summary>
        public FormationSettings ApplyTo(FormationSettings source)
        {
            string angle = string.IsNullOrEmpty(_angle) ? source.Angle : _angle;
            double spacing = _spacing == 0 ? source.Spacing : _spacing;
            double interval = _interval == 0 ? source.Interval : _interval;
            int slots = _slots == 0 ? source.Slots : _slots;
            return new FormationSettings(source.Shape, angle, spacing, interval, slots, source.RadiusScale);
        }
    }

    [Serializable]
    public sealed class WaveUnit
    {
        [Header("웨이브 유닛")]
        [SerializeField] private string _enemyId;
        [SerializeField] private int _count;
        [SerializeField] private bool _isElite;
        public string EnemyId => _enemyId;
        public int Count => _count;

        /// <summary>이 유닛을 스테이지 엘리트 정의로 생성할지 반환한다.</summary>
        public bool IsElite => _isElite;

        /// <summary>지정 보스 한 기만 포함하는 비엘리트 런타임 편성 항목을 반환한다.</summary>
        internal static WaveUnit CreateBoss(string enemyId) => new WaveUnit { _enemyId = enemyId, _count = 1 };
    }

    [Serializable]
    public sealed class WaveDefinition
    {
        [Header("웨이브")]
        [SerializeField] private FormationOverride _formation;
        [SerializeField] private WaveUnit[] _units;
        public FormationOverride Formation => _formation;
        public IReadOnlyList<WaveUnit> Units => _units;

        /// <summary>원본 진형을 유지하고 편성만 지정 보스 한 기로 바꾼 독립 디버그 웨이브를 반환한다.</summary>
        internal WaveDefinition WithBoss(string enemyId)
            => new WaveDefinition { _formation = this._formation, _units = new[] { WaveUnit.CreateBoss(enemyId) } };
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
        [SerializeField] private double _dropChancePercent;
        public string EnemyId => _enemyId;
        // 배율 0은 데이터에 기재하지 않은 것으로 보고 1을 쓴다. 이후 강화 특징도 같은 방식으로 필드를 확장한다.
        public double HpMultiplier => _hpMultiplier > 0 ? _hpMultiplier : 1;
        public double RewardMultiplier => _rewardMultiplier > 0 ? _rewardMultiplier : 1;
        public double SpeedMultiplier => _speedMultiplier > 0 ? _speedMultiplier : 1;
        /// <summary>엘리트의 크기 배율이다. 데이터에 기재하지 않으면 1을 반환한다.</summary>
        public double RadiusMultiplier => _radiusMultiplier > 0 ? _radiusMultiplier : 1;

        /// <summary>엘리트 처치 시 추첨할 드롭 테이블 ID다. 데이터에 기재하지 않으면 드롭이 없다.</summary>
        public string DropTable => _dropTable;

        /// <summary>엘리트 처치 시 드롭 테이블을 굴릴 확률(%)이다. 데이터에 기재하지 않으면(0) 100을 반환한다.</summary>
        public double DropChancePercent => _dropChancePercent > 0 ? _dropChancePercent : 100;
        internal double RawDropChancePercent => _dropChancePercent;
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
    public sealed class ProgressionRoundDefinition
    {
        [Header("라운드 편성")]
        [SerializeField] private WaveDefinition[] _waves;
        [SerializeField] private string _rewardDropTable;
        public IReadOnlyList<WaveDefinition> Waves => _waves ?? Array.Empty<WaveDefinition>();

        /// <summary>라운드 종료 시 3개 중 1개 선택 보상을 추첨할 drops.json 테이블 ID다. 마지막 라운드에서는 스테이지 종료 보상 테이블이다.</summary>
        public string RewardDropTable => _rewardDropTable;
    }

    [Serializable]
    public sealed class ProgressionStageDefinition
    {
        [Header("연속 스테이지")]
        [SerializeField] private int _stage;
        [SerializeField] private ProgressionRoundDefinition[] _rounds;
        [SerializeField] private EliteSpawnDefinition[] _eliteTypes;
        public int Stage => _stage;
        public IReadOnlyList<ProgressionRoundDefinition> Rounds => _rounds ?? Array.Empty<ProgressionRoundDefinition>();
        public IReadOnlyList<EliteSpawnDefinition> EliteTypes => _eliteTypes ?? Array.Empty<EliteSpawnDefinition>();

        /// <summary>적 ID가 일치하는 엘리트 정의를 반환하고 없으면 false를 반환한다.</summary>
        public bool TryGetElite(string enemyId, out EliteSpawnDefinition elite)
        {
            elite = null;
            foreach (EliteSpawnDefinition candidate in EliteTypes)
                if (candidate.EnemyId == enemyId) { elite = candidate; return true; }
            return false;
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

        /// <summary>JSON 진형 설정을 읽어 배열 구성, ID 중복, 모양·각도·값 범위(스폰 반경 배율 포함)와 유한값을 검증한다.</summary>
        public static FormationCatalog FromJson(string json)
        {
            var catalog = JsonUtility.FromJson<FormationCatalog>(json);
            if (catalog == null || catalog._formations == null || catalog._formations.Length == 0) throw new FormatException("진형 설정이 비어 있습니다.");
            var ids = new HashSet<string>();
            foreach (FormationDefinition formation in catalog._formations)
            {
                if (string.IsNullOrEmpty(formation.Id) || !ids.Add(formation.Id)) throw new FormatException("진형 ID가 비었거나 중복입니다: " + formation.Id);
                if (!SpawnFormations.IsKnownShape(formation.Shape)) throw new FormatException("구현되지 않은 진형 모양입니다: " + formation.Shape + " (진형 " + formation.Id + ")");
                if (formation.Angle != "random" && formation.Angle != "ahead" && formation.Angle != "fixed")
                    throw new FormatException("진형 각도는 random, ahead 또는 fixed여야 합니다: " + formation.Id);
                if (!IsFinite(formation.Spacing) || !IsFinite(formation.Interval) || !IsFinite(formation.RadiusScale))
                    throw new FormatException("진형 값은 유한해야 합니다: " + formation.Id);
                if (formation.Spacing < 0 || formation.Interval < 0 || formation.Slots < 0 || formation.RadiusScale < 0)
                    throw new FormatException("진형 값 범위 오류입니다: " + formation.Id);
                if (formation.Slots > 0 && !SpawnFormations.IsValidSlotCount(formation.Shape, formation.Slots))
                    throw new FormatException("반원 진형 슬롯 수는 " + SpawnFormations.HALF_RING_SLOT_STEP + "의 배수여야 합니다: " + formation.Id);
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
        public const int PROGRESSION_STAGE_COUNT = 3;
        public const int PROGRESSION_ROUND_COUNT = 6;
        public const int PROGRESSION_WAVE_COUNT = 3;
        public const int MINI_BOSS_ROUND = 3;
        public const int ROUND_REWARD_CHOICE_COUNT = 3;
        internal const int STAGE_CYCLE_START = 21;
        internal const int STAGE_CYCLE_FIRST_STAGE = 11;
        internal const int STAGE_CYCLE_LAST_STAGE = 20;
        internal const int ENDLESS_STAGE_START = 21;
        internal const int ENDLESS_HP_BASE_STAGE = 10;
        internal const double ENDLESS_HP_SCALE_MULTIPLIER = 1.5;
        internal const double ENDLESS_HP_BASE_MULTIPLIER = 2;

        [Header("스테이지 루트")]
        [SerializeField] private StageMapSettings _map;
        [SerializeField] private StageGrowth _growth;
        [SerializeField] private StageSpawnSettings _spawn;
        [SerializeField] private double _stationaryAdvanceSpeed;
        [SerializeField] private EnemyReward[] _rewards;
        [SerializeField] private ProgressionStageDefinition[] _progressionStages;
        public StageMapSettings Map => _map;
        public StageGrowth Growth => _growth;
        public StageSpawnSettings Spawn => _spawn;
        public double StationaryAdvanceSpeed => _stationaryAdvanceSpeed;
        public IReadOnlyList<EnemyReward> Rewards => _rewards;
        public IReadOnlyList<ProgressionStageDefinition> ProgressionStages => _progressionStages;

        /// <summary>연속 미션의 1부터 시작하는 stage 번호에 대응하는 편성을 반환한다.</summary>
        public ProgressionStageDefinition GetProgressionStage(int stage)
        {
            if (stage < 1 || stage > _progressionStages.Length) throw new ArgumentOutOfRangeException(nameof(stage));
            return _progressionStages[stage - 1];
        }

        /// <summary>지정 스테이지·라운드가 연결한 선택 보상 테이블의 단일 행동·모드 분류를 반환한다. 혼합 분류는 데이터 오류다.</summary>
        public string GetRoundRewardCategory(int stage, int round, DropCatalog drops)
        {
            string tableId = GetProgressionStage(stage).Rounds[round - 1].RewardDropTable;
            string category = null;
            foreach (DropCategoryChance chance in drops.Get(tableId).CategoryChances)
            {
                if (chance.Percent <= 0) continue;
                if (category != null || (chance.Category != DropCatalog.TYPE_MODIFIER && chance.Category != DropCatalog.TYPE_BEHAVIOR))
                    throw new FormatException("고정 등급 선택 보상은 행동 또는 모드 한 분류만 지원합니다: " + tableId);
                category = chance.Category;
            }
            if (category == null) throw new FormatException("라운드 선택 보상 분류가 없습니다: " + tableId);
            return category;
        }

        /// <summary>
        /// 스테이지 번호가 쓸 데이터의 스테이지 번호를 반환한다. 20스테이지까지는 그대로, 21스테이지 이후는 11~20 하나로 순환 배치한다.
        /// 21→11, 22→12, … 30→20, 31→11 순서다. 등급표 조회가 이 순환 규칙을 쓴다.
        /// </summary>
        public static int ResolveDataStage(int stage)
        {
            if (stage < STAGE_CYCLE_START) return stage;
            int cycleLength = STAGE_CYCLE_LAST_STAGE - STAGE_CYCLE_FIRST_STAGE + 1;
            return STAGE_CYCLE_FIRST_STAGE + (stage - STAGE_CYCLE_START) % cycleLength;
        }

        /// <summary>
        /// 스테이지 체력 배율을 반환한다. 10스테이지까지는 성장표의 스테이지당 증가율을 쓰고,
        /// 11스테이지부터는 무한 모드 기준 배율(ENDLESS_HP_BASE_MULTIPLIER)에 같은 증가율의 1.5배를 더한다.
        /// </summary>
        public double GetHpMultiplier(int stage)
        {
            if (stage < ENDLESS_STAGE_START) return Growth.GetHpMultiplier(stage);
            return ENDLESS_HP_BASE_MULTIPLIER
                + (stage - ENDLESS_HP_BASE_STAGE) * Growth.HpStageScale * ENDLESS_HP_SCALE_MULTIPLIER;
        }

        /// <summary>적 ID의 보상을 반환하고 보상표에 없는 적은 fallback 값을 돌려준다.</summary>
        public int GetReward(string enemyId, int fallback)
        {
            for (int i = 0; i < _rewards.Length; i++)
                if (_rewards[i].EnemyId == enemyId) return _rewards[i].Reward;
            return fallback;
        }

        /// <summary>JSON 스테이지 설정을 읽어 루트 설정, 적 보상표와 연속 미션의 3×6×3 편성, 적·진형 참조 및 라운드 보상 드롭 테이블을 검증한다.</summary>
        public static StageCatalog FromJson(string json, EnemyCatalog enemies, FormationCatalog formations, DropCatalog drops,
            ModifierGradeTable modifierGrades, RuneCatalog runes, BossCatalog bosses)
        {
            var catalog = JsonUtility.FromJson<StageCatalog>(json);
            if (catalog == null || catalog._map == null || catalog._growth == null || catalog._spawn == null
                || catalog._rewards == null)
                throw new FormatException("스테이지 루트 설정이 비어 있습니다.");
            ValidateRoot(catalog);
            ValidateRewards(catalog._rewards, enemies);
            ValidateProgression(catalog, enemies, formations, drops, modifierGrades, runes, bosses);
            int modifierCount = 0;
            int behaviorCount = 0;
            for (int stage = 1; stage <= PROGRESSION_STAGE_COUNT; stage++)
            {
                for (int round = 1; round <= PROGRESSION_ROUND_COUNT; round++)
                {
                    if (stage == PROGRESSION_STAGE_COUNT && round == PROGRESSION_ROUND_COUNT) continue;
                    if (catalog.GetRoundRewardCategory(stage, round, drops) == DropCatalog.TYPE_MODIFIER) modifierCount++;
                    else behaviorCount++;
                }
            }
            if (modifierCount != PROGRESSION_STAGE_COUNT * (PROGRESSION_ROUND_COUNT - 2)
                || behaviorCount != PROGRESSION_STAGE_COUNT * 2 - 1)
                throw new FormatException("라운드 선택 보상 횟수가 고정 등급 목록과 다릅니다.");
            return catalog;
        }

        /// <summary>연속 미션 편성·참조와 3라운드 미니 보스·마지막 라운드 보스의 위치 및 수량을 검증한다.</summary>
        private static void ValidateProgression(StageCatalog catalog, EnemyCatalog enemies, FormationCatalog formations, DropCatalog drops,
            ModifierGradeTable modifierGrades, RuneCatalog runes, BossCatalog bosses)
        {
            if (catalog._progressionStages == null || catalog._progressionStages.Length != PROGRESSION_STAGE_COUNT)
                throw new FormatException("연속 미션은 스테이지가 정확히 3개여야 합니다.");
            for (int stageIndex = 0; stageIndex < PROGRESSION_STAGE_COUNT; stageIndex++)
            {
                ProgressionStageDefinition stage = catalog._progressionStages[stageIndex];
                if (stage == null || stage.Stage != stageIndex + 1 || stage.Rounds.Count != PROGRESSION_ROUND_COUNT)
                    throw new FormatException("연속 미션 스테이지 번호 또는 라운드 수 오류: " + (stageIndex + 1));
                for (int roundIndex = 0; roundIndex < PROGRESSION_ROUND_COUNT; roundIndex++)
                {
                    ProgressionRoundDefinition round = stage.Rounds[roundIndex];
                    if (round == null || round.Waves.Count != PROGRESSION_WAVE_COUNT)
                        throw new FormatException("연속 미션 라운드의 웨이브 수 오류: " + stage.Stage + "/" + (roundIndex + 1));
                    ValidateRewardDropTable(round.RewardDropTable, drops, runes, modifierGrades, "스테이지 " + stage.Stage + ", 라운드 " + (roundIndex + 1));
                    for (int waveIndex = 0; waveIndex < PROGRESSION_WAVE_COUNT; waveIndex++)
                    {
                        WaveDefinition wave = round.Waves[waveIndex];
                        if (wave == null)
                            throw new FormatException("연속 미션 웨이브가 비어 있습니다: " + stage.Stage + "/" + (roundIndex + 1) + "/" + (waveIndex + 1));
                        ValidateWave(wave, stage.Stage, enemies, formations);
                        CheckFormationSlots(wave.Formation.ApplyTo(formations.Get(wave.Formation.Id).ToSettings()), catalog._spawn.RingSlots, stage.Stage);
                        bool isBossWave = roundIndex == PROGRESSION_ROUND_COUNT - 1 && waveIndex == PROGRESSION_WAVE_COUNT - 1;
                        bool isMiniBossWave = roundIndex + 1 == MINI_BOSS_ROUND && waveIndex == PROGRESSION_WAVE_COUNT - 1;
                        int bossCount = 0;
                        int miniBossCount = 0;
                        foreach (WaveUnit unit in wave.Units)
                        {
                            if (unit.IsElite)
                            {
                                if (bosses.TryGet(unit.EnemyId, out _) || unit.EnemyId.StartsWith("boss.", StringComparison.Ordinal))
                                    throw new FormatException("보스는 엘리트 유닛이 될 수 없습니다: " + unit.EnemyId + " (스테이지 " + stage.Stage + ")");
                                if (!stage.TryGetElite(unit.EnemyId, out _))
                                    throw new FormatException("엘리트 유닛의 스테이지 엘리트 정의가 없습니다: " + unit.EnemyId + " (스테이지 " + stage.Stage + ")");
                            }
                            if (!bosses.TryGet(unit.EnemyId, out BossDefinition boss))
                            {
                                if (unit.EnemyId.StartsWith("boss.", StringComparison.Ordinal)) bosses.Get(unit.EnemyId);
                                continue;
                            }
                            if (boss.IsMiniBoss) miniBossCount += unit.Count;
                            else bossCount += unit.Count;
                        }
                        if (bossCount != (isBossWave ? 1 : 0))
                            throw new FormatException("보스는 각 스테이지의 마지막 웨이브에 1기만 있어야 합니다: " + stage.Stage + "/" + (roundIndex + 1) + "/" + (waveIndex + 1));
                        if (miniBossCount != (isMiniBossWave ? 1 : 0))
                            throw new FormatException("미니 보스는 각 스테이지 3라운드 마지막 웨이브에 1기만 있어야 합니다: " + stage.Stage + "/" + (roundIndex + 1) + "/" + (waveIndex + 1));
                    }
                }
                var eliteIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (EliteSpawnDefinition elite in stage.EliteTypes)
                {
                    if (!eliteIds.Add(elite.EnemyId))
                        throw new FormatException("연속 미션 엘리트 적 ID가 중복입니다: " + elite.EnemyId + " (스테이지 " + stage.Stage + ")");
                    ValidateEliteDefinition(elite, enemies, bosses, drops, "스테이지 " + stage.Stage);
                }
            }
        }

        /// <summary>
        /// 라운드 선택 보상 테이블이 등록돼 있고 속성 확률이 없으며, 행동·모드의 모든 지원 등급마다 후보가 선택지 수 이상인지 확인한다.
        /// location은 오류 메시지의 위치 표기다.
        /// </summary>
        private static void ValidateRewardDropTable(string tableId, DropCatalog drops, RuneCatalog runes, ModifierGradeTable modifierGrades, string location)
        {
            if (string.IsNullOrEmpty(tableId) || !drops.Has(tableId))
                throw new FormatException("라운드 보상 드롭 테이블이 없거나 등록되지 않았습니다: " + tableId + " (" + location + ")");
            DropTableDefinition table = drops.Get(tableId);
            foreach (DropCategoryChance chance in table.CategoryChances)
            {
                if (chance.Percent <= 0) continue;
                if (chance.Category == DropCatalog.TYPE_ELEMENT)
                    throw new FormatException("라운드 보상 드롭 테이블에는 속성 확률을 둘 수 없습니다: " + tableId + " (" + location + ")");
                foreach (string grade in DropCatalog.GetGradeOrder(chance.Category, runes, modifierGrades))
                    if (RuneRewardRoller.CollectCandidates(table, chance.Category, grade, runes, modifierGrades, null).Count < ROUND_REWARD_CHOICE_COUNT)
                        throw new FormatException("라운드 보상 드롭 테이블은 모든 지원 등급마다 후보가 " + ROUND_REWARD_CHOICE_COUNT + "개 이상이어야 합니다: "
                            + chance.Category + " " + grade + " (" + tableId + ", " + location + ")");
            }
        }

        /// <summary>루트의 맵 칸수, 성장 배율, 스폰 설정(맵형 스폰 예고·거리·유예 포함), 정지 전진 속도 범위와 유한값을 확인한다.</summary>
        private static void ValidateRoot(StageCatalog catalog)
        {
            catalog._growth.ValidateHpCurve();
            if (catalog._map.RoomColumns < 1 || catalog._map.RoomRows < 1 || catalog._map.RoomsX < 1 || catalog._map.RoomsY < 1)
                throw new FormatException("맵 칸수는 1 이상이어야 합니다.");
            if (!IsFinite(catalog._growth.HpStageScale) || !IsFinite(catalog._growth.DamageStageScale)
                || catalog._growth.HpStageScale < 0 || catalog._growth.DamageStageScale < 0)
                throw new FormatException("성장 배율은 0 이상의 유한값이어야 합니다.");
            if (catalog._spawn.RingSlots < 1 || !IsFinite(catalog._spawn.RingMargin) || catalog._spawn.RingMargin < 0
                || !IsFinite(catalog._spawn.JitterScale) || catalog._spawn.JitterScale < 0
                || !IsFinite(catalog._spawn.ReinforcementGap) || catalog._spawn.ReinforcementGap < 0)
                throw new FormatException("스폰 설정 값이 유효하지 않습니다.");
            StageSpawnSettings spawn = catalog._spawn;
            if (!IsFinite(spawn.BatchIntervalSeconds) || spawn.BatchIntervalSeconds <= 0)
                throw new FormatException("무리 스폰 간격은 0보다 큰 유한한 초 단위 값이어야 합니다.");
            if (!IsFinite(spawn.WarningSeconds) || spawn.WarningSeconds < 0 || !IsFinite(spawn.SpawnGraceSeconds) || spawn.SpawnGraceSeconds < 0
                || !IsFinite(spawn.MinPlayerDistance) || spawn.MinPlayerDistance < 0
                || !IsFinite(spawn.MaxPlayerDistance) || spawn.MaxPlayerDistance < 0
                || (spawn.MaxPlayerDistance > 0 && spawn.MaxPlayerDistance < spawn.MinPlayerDistance))
                throw new FormatException("맵형 스폰 설정은 0 이상의 유한값이고 최대 거리는 0 또는 최소 거리 이상이어야 합니다.");
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

        /// <summary>연속 미션 웨이브의 유닛·진형 재정의 값과 적·진형 참조를 검증한다.</summary>
        private static void ValidateWave(WaveDefinition wave, int stageNumber, EnemyCatalog enemies, FormationCatalog formations)
        {
            FormationOverride formation = wave.Formation;
            if (formation == null || !formations.Has(formation.Id)) throw new FormatException("웨이브 진형 참조 오류입니다: 스테이지 " + stageNumber);
            if (!string.IsNullOrEmpty(formation.Angle) && formation.Angle != "random" && formation.Angle != "ahead" && formation.Angle != "fixed")
                throw new FormatException("웨이브 진형 각도는 random, ahead 또는 fixed여야 합니다: 스테이지 " + stageNumber);
            if (!IsFinite(formation.Spacing) || formation.Spacing < 0 || !IsFinite(formation.Interval) || formation.Interval < 0 || formation.Slots < 0)
                throw new FormatException("웨이브 진형 값 오류입니다: 스테이지 " + stageNumber);
            if (wave.Units == null || wave.Units.Count == 0) throw new FormatException("웨이브에 유닛이 없습니다: 스테이지 " + stageNumber);
            foreach (WaveUnit unit in wave.Units)
            {
                if (unit == null || string.IsNullOrEmpty(unit.EnemyId)) throw new FormatException("웨이브 적 ID가 비었습니다: 스테이지 " + stageNumber);
                enemies.Get(unit.EnemyId);
                if (unit.Count < 1) throw new FormatException("웨이브 유닛 수는 1 이상이어야 합니다: " + unit.EnemyId + " (스테이지 " + stageNumber + ")");
            }
        }

        /// <summary>진형 설정의 슬롯 수(0이면 ringSlots)가 모양에 맞지 않으면 데이터 오류를 발생시킨다.</summary>
        private static void CheckFormationSlots(FormationSettings settings, int ringSlots, int stageNumber)
        {
            int slots = settings.Slots > 0 ? settings.Slots : ringSlots;
            if (!SpawnFormations.IsValidSlotCount(settings.Shape, slots))
                throw new FormatException("반원 진형 슬롯 수는 " + SpawnFormations.HALF_RING_SLOT_STEP + "의 배수여야 합니다: 스테이지 " + stageNumber);
        }

        /// <summary>엘리트 정의 하나의 대상 적, 보스 종족 제외, 배율, 드롭 확률 범위와 드롭 테이블 참조를 확인한다. location은 오류 메시지의 위치 표기다.</summary>
        private static void ValidateEliteDefinition(EliteSpawnDefinition elite, EnemyCatalog enemies, BossCatalog bosses, DropCatalog drops, string location)
        {
            enemies.Get(elite.EnemyId); // 없는 적 ID면 여기서 오류가 난다.
            if (bosses.TryGet(elite.EnemyId, out _) || elite.EnemyId.StartsWith("boss.")) throw new FormatException("보스는 엘리트 대상이 아닙니다: " + elite.EnemyId + " (" + location + ")");
            if (!IsFinite(elite.RawHpMultiplier) || !IsFinite(elite.RawRewardMultiplier) || !IsFinite(elite.RawSpeedMultiplier) || !IsFinite(elite.RawRadiusMultiplier)
                || elite.RawHpMultiplier < 0 || elite.RawRewardMultiplier < 0 || elite.RawSpeedMultiplier < 0 || elite.RawRadiusMultiplier < 0)
                throw new FormatException("엘리트 배율은 0 이상의 유한값이어야 합니다: " + elite.EnemyId + " (" + location + ")");
            if (double.IsNaN(elite.RawDropChancePercent) || elite.RawDropChancePercent < 0 || elite.RawDropChancePercent > 100)
                throw new FormatException("엘리트 드롭 확률은 0 이상 100 이하여야 합니다: " + elite.EnemyId + " (" + location + ")");
            if (!string.IsNullOrEmpty(elite.DropTable) && !drops.Has(elite.DropTable))
                throw new FormatException("등록되지 않은 엘리트 드롭 테이블입니다: " + elite.DropTable + " (" + elite.EnemyId + ", " + location + ")");
        }

        /// <summary>값이 NaN이나 무한대가 아닌지 반환한다.</summary>
        private static bool IsFinite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
