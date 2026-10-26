using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace RuneCode
{
    /// <summary>드롭 추첨 결과 한 항목이다. Type은 룬 분류, RuneId·Grade·Count는 지급할 룬이다. element는 등급이 빈 문자열이다.</summary>
    public readonly struct DropRoll
    {
        private readonly string _type;
        private readonly string _runeId;
        private readonly string _grade;
        private readonly int _count;
        private readonly int _fragments;
        public string Type => _type;
        public string RuneId => _runeId;

        /// <summary>추첨된 등급이다. element는 항상 빈 문자열이다.</summary>
        public string Grade => _grade;
        public int Count => _count;
        public int Fragments => _fragments;

        /// <summary>항목 형식과 추첨으로 정해진 값으로 결과를 만든다.</summary>
        internal DropRoll(string type, string runeId, string grade, int count, int fragments)
        { _type = type; _runeId = runeId; _grade = grade; _count = count; _fragments = fragments; }
    }

    /// <summary>드롭 테이블의 분류(행동·강화·속성) 확률 한 항목이다. 테이블 안의 Percent 합은 100이다.</summary>
    [Serializable]
    public struct DropCategoryChance
    {
        [Header("분류 확률")]
        [SerializeField] private string _category;
        [SerializeField] private int _percent;
        public string Category => _category;
        public int Percent => _percent;
    }

    /// <summary>드롭 테이블의 분류별 등급 확률 한 항목이다. 같은 분류의 Percent 합은 100이다.</summary>
    [Serializable]
    public struct DropGradeChance
    {
        [Header("등급 확률")]
        [SerializeField] private string _category;
        [SerializeField] private string _grade;
        [SerializeField] private int _percent;
        public string Category => _category;
        public string Grade => _grade;
        public int Percent => _percent;
    }

    /// <summary>드롭 후보 룬과 가중치다. RewardGrade가 있는 속성은 해당 등급의 모드 선택 보상에만 참여한다.</summary>
    [Serializable]
    public sealed class DropEntry
    {
        [Header("후보 룬")]
        [SerializeField] private string _type;
        [SerializeField] private string _id;
        [SerializeField] private double _weight;
        [SerializeField] private string _rewardGrade;
        public string Type => _type;
        public string Id => _id;
        public double Weight => _weight;
        public string RewardGrade => _rewardGrade;
    }

    /// <summary>
    /// 분류 확률로 행동·강화·속성을 고르고, 분류의 등급 확률로 등급을 고른 뒤, 그 등급을 지원하는 후보 중 가중치로 룬 1개를 고르는 드롭 테이블이다.
    /// 등급 확률이 없는 분류는 스테이지 등급표를 따르고, 후보가 없는 분류는 그 분류의 활성 룬 전체를 같은 가중치로 쓴다.
    /// </summary>
    [Serializable]
    public sealed class DropTableDefinition
    {
        [Header("드롭 테이블")]
        [SerializeField] private string _id;
        [SerializeField] private DropCategoryChance[] _categoryChances;
        [SerializeField] private DropGradeChance[] _gradeChances;
        [SerializeField] private DropEntry[] _entries;
        public string Id => _id;
        public IReadOnlyList<DropCategoryChance> CategoryChances => _categoryChances ?? Array.Empty<DropCategoryChance>();
        public IReadOnlyList<DropGradeChance> GradeChances => _gradeChances ?? Array.Empty<DropGradeChance>();
        public IReadOnlyList<DropEntry> Entries => _entries ?? Array.Empty<DropEntry>();

        /// <summary>분류에 이 테이블의 등급 확률이 있는지 반환한다. 없으면 스테이지 등급표를 쓴다.</summary>
        public bool HasGradeChances(string category)
        {
            foreach (DropGradeChance chance in GradeChances) if (chance.Category == category) return true;
            return false;
        }

        /// <summary>
        /// 분류에서 나올 수 있는 등급 목록을 반환한다. 속성은 빈 등급 하나, 등급 확률이 있으면 확률이 0보다 큰 등급,
        /// 없으면 스테이지 등급표 중 하나라도 확률이 0보다 큰 등급이다. runes·modifierGrades는 등급 순서와 스테이지 등급표 조회에 쓴다.
        /// </summary>
        public List<string> GetPossibleGrades(string category, RuneCatalog runes, ModifierGradeTable modifierGrades)
        {
            var grades = new List<string>();
            if (category == DropCatalog.TYPE_ELEMENT) { grades.Add(""); return grades; }
            if (HasGradeChances(category))
            {
                foreach (DropGradeChance chance in GradeChances)
                    if (chance.Category == category && chance.Percent > 0 && !grades.Contains(chance.Grade)) grades.Add(chance.Grade);
                return grades;
            }
            IReadOnlyList<string> order = DropCatalog.GetGradeOrder(category, runes, modifierGrades);
            if (order == null) return grades;
            foreach (string grade in order)
            {
                foreach (ModifierStageDropTable stage in modifierGrades.StageDropTables)
                {
                    IReadOnlyDictionary<string, int> percents = category == DropCatalog.TYPE_BEHAVIOR
                        ? stage.BehaviorGradeChancesPercent : stage.EliteModifierGradeChancesPercent;
                    if (percents == null || !percents.TryGetValue(grade, out int percent) || percent <= 0) continue;
                    grades.Add(grade);
                    break;
                }
            }
            return grades;
        }
    }

    [Serializable]
    public sealed class StageDropChance
    {
        [Header("스테이지 구간 확률")]
        [SerializeField] private int _fromStage;
        [SerializeField] private int _toStage;
        [SerializeField] private double _chancePercent;
        public int FromStage => _fromStage;
        public int ToStage => _toStage;
        public double ChancePercent => _chancePercent;

        /// <summary>스테이지 번호가 이 구간 안인지 반환한다.</summary>
        public bool Contains(int stage) => stage >= _fromStage && stage <= _toStage;
    }

    [Serializable]
    public sealed class NormalEnemyDropDefinition
    {
        [Header("일반 적 드롭")]
        [SerializeField] private string _dropTable;
        [SerializeField] private double _chancePercent;
        [SerializeField] private StageDropChance[] _stageChances;
        public string DropTable => _dropTable;
        public double ChancePercent => _chancePercent;
        internal StageDropChance[] StageChances => _stageChances ?? Array.Empty<StageDropChance>();

        /// <summary>스테이지의 일반 적 드롭 확률을 반환한다. 처음 일치하는 스테이지 구간 확률을 쓰고 없으면 기본 확률을 쓴다.</summary>
        public double GetChancePercent(int stage)
        {
            foreach (StageDropChance chance in StageChances)
                if (chance.Contains(stage)) return chance.ChancePercent;
            return _chancePercent;
        }

        /// <summary>스테이지에서 일반 적 드롭이 가능한지 반환한다. 테이블 ID가 비어 있거나 그 스테이지 확률이 0 이하면 false다.</summary>
        public bool IsEnabled(int stage) => !string.IsNullOrEmpty(_dropTable) && GetChancePercent(stage) > 0;
    }

    /// <summary>라운드 보상에서 체력 회복 카드의 등장 확률과 등급별 회복 비율 설정이다.</summary>
    [Serializable]
    public sealed class RoundHealChoiceDefinition
    {
        [Header("라운드 회복 선택지")]
        [SerializeField] private double _chancePercent;
        [SerializeField] private double _baseRatio;
        [SerializeField] private double _ratioPerGrade;
        public double ChancePercent => _chancePercent;
        public double BaseRatio => _baseRatio;
        public double RatioPerGrade => _ratioPerGrade;

        /// <summary>등급 인덱스(0이 첫 등급)의 회복 비율을 반환한다. 기본 비율에 단계 비율을 인덱스만큼 더한다.</summary>
        public double GetRatio(int gradeIndex) => _baseRatio + _ratioPerGrade * Math.Max(0, gradeIndex);
    }

    [Serializable]
    public sealed class DropCatalog
    {
        public const string TYPE_MODIFIER = "modifier";
        public const string TYPE_ELEMENT = "element";
        public const string TYPE_BEHAVIOR = "behavior";
        public const int TOTAL_PERCENT = 100;

        /// <summary>라운드 보상 선택지의 체력 회복 카드 ID다.</summary>
        public const string HEAL_CHOICE_ID = "reward.heal";

        /// <summary>드롭 테이블이 다루는 룬 분류를 행동·강화·속성 순서로 반환한다.</summary>
        public static IReadOnlyList<string> Categories { get; } = Array.AsReadOnly(new[] { TYPE_BEHAVIOR, TYPE_MODIFIER, TYPE_ELEMENT });

        [Header("드롭 테이블 목록")]
        [SerializeField] private DropTableDefinition[] _dropTables;
        public IReadOnlyList<DropTableDefinition> Tables => _dropTables;

        [Header("일반 적 드롭")]
        [SerializeField] private NormalEnemyDropDefinition _normalEnemyDrop;

        /// <summary>엘리트·보스가 아닌 적이 처치될 때 굴리는 드롭 설정이다. 필드가 없으면 null이다.</summary>
        public NormalEnemyDropDefinition NormalEnemyDrop => _normalEnemyDrop;

        [Header("라운드 회복 선택지")]
        [SerializeField] private RoundHealChoiceDefinition _roundHealChoice;

        /// <summary>라운드 보상의 체력 회복 카드 설정이다. 필드가 없으면 null이다.</summary>
        public RoundHealChoiceDefinition RoundHealChoice => _roundHealChoice;

        /// <summary>분류·등급의 라운드 회복 비율을 반환한다. 등급 순서에 없는 등급은 첫 등급 비율을 쓰고 설정이 없으면 0을 반환한다.</summary>
        public double GetRoundHealRatio(string category, string grade, RuneCatalog runes, ModifierGradeTable modifierGrades)
        {
            if (_roundHealChoice == null) return 0;
            IReadOnlyList<string> order = GetGradeOrder(category, runes, modifierGrades);
            int index = 0;
            if (order != null)
                for (int i = 0; i < order.Count; i++)
                    if (order[i] == grade) { index = i; break; }
            return _roundHealChoice.GetRatio(index);
        }

        /// <summary>드롭 테이블 ID의 존재 여부를 반환한다.</summary>
        public bool Has(string id)
        { for (int i = 0; i < _dropTables.Length; i++) if (_dropTables[i].Id == id) return true; return false; }

        /// <summary>드롭 테이블 ID에 해당하는 정의를 반환하고 누락된 ID에는 데이터 오류를 발생시킨다.</summary>
        public DropTableDefinition Get(string id)
        { for (int i = 0; i < _dropTables.Length; i++) if (_dropTables[i].Id == id) return _dropTables[i]; throw new FormatException("등록되지 않은 드롭 테이블: " + id); }

        /// <summary>분류의 등급 순서를 반환한다. 강화는 Modifier 등급표, 행동은 행동 등급표 순서이고 속성·알 수 없는 분류는 null이다.</summary>
        public static IReadOnlyList<string> GetGradeOrder(string category, RuneCatalog runes, ModifierGradeTable modifierGrades)
        {
            if (category == TYPE_MODIFIER) return modifierGrades.GradeOrder;
            if (category == TYPE_BEHAVIOR) return runes.BehaviorGradeOrder;
            return null;
        }

        /// <summary>JSON 드롭 설정을 읽고 모든 테이블의 검증 오류를 모아 한 번에 보고한다. 룬의 존재·분류·비활성 여부와 등급은 runes와 modifierGrades로 확인한다.</summary>
        public static DropCatalog FromJson(string json, RuneCatalog runes, ModifierGradeTable modifierGrades)
        {
            var catalog = JsonUtility.FromJson<DropCatalog>(json);
            if (catalog == null || catalog._dropTables == null || catalog._dropTables.Length == 0)
                throw new FormatException("drops.json: 드롭 테이블이 비어 있습니다.");
            var errors = new List<string>();
            var ids = new HashSet<string>();
            foreach (DropTableDefinition table in catalog._dropTables)
            {
                string tableId = !string.IsNullOrEmpty(table.Id) ? table.Id : "(ID 없음)";
                if (string.IsNullOrEmpty(table.Id)) errors.Add("drops.json:" + tableId + ":_id 테이블 ID가 비어 있습니다.");
                else if (!ids.Add(table.Id)) errors.Add("drops.json:" + tableId + ":_id 테이블 ID가 중복됩니다.");
                string path = "drops.json:" + tableId;
                ValidateCategoryChances(table, path, errors);
                ValidateGradeChances(table, path, errors, runes, modifierGrades);
                ValidateEntries(table, path, errors, runes, modifierGrades);
                ValidateCandidates(table, path, errors, runes, modifierGrades);
            }
            ValidateNormalEnemyDrop(catalog, ids, errors);
            ValidateRoundHealChoice(catalog, errors, runes, modifierGrades);
            if (errors.Count > 0) throw new FormatException("drops.json 검증 오류:\n" + string.Join("\n", errors));
            return catalog;
        }

        /// <summary>일반 적 드롭 설정의 테이블 ID가 등록된 ID인지, 기본·스테이지 구간 확률이 0 이상 100 이하인지, 구간이 1 이상이고 시작이 끝 이하인지 검사하고 오류를 목록에 모은다. 테이블 ID가 비어 있으면 일반 적 드롭이 없다.</summary>
        private static void ValidateNormalEnemyDrop(DropCatalog catalog, HashSet<string> tableIds, List<string> errors)
        {
            NormalEnemyDropDefinition drop = catalog._normalEnemyDrop;
            if (drop == null || string.IsNullOrEmpty(drop.DropTable)) return;
            if (!tableIds.Contains(drop.DropTable)) errors.Add("drops.json:_normalEnemyDrop._dropTable 등록되지 않은 드롭 테이블입니다: " + drop.DropTable);
            if (!IsValidPercent(drop.ChancePercent))
                errors.Add("drops.json:_normalEnemyDrop._chancePercent 확률은 0 이상 100 이하여야 합니다.");
            for (int i = 0; i < drop.StageChances.Length; i++)
            {
                StageDropChance chance = drop.StageChances[i];
                string path = "drops.json:_normalEnemyDrop._stageChances[" + i + "]";
                if (chance.FromStage < 1 || chance.ToStage < chance.FromStage) errors.Add(path + " 스테이지 구간은 1 <= from <= to여야 합니다.");
                if (!IsValidPercent(chance.ChancePercent)) errors.Add(path + "._chancePercent 확률은 0 이상 100 이하여야 합니다.");
            }
        }

        /// <summary>분류 확률이 알려진 분류이고 중복이 없으며 0 이상 100 이하이고 합이 100인지 검사해 오류를 목록에 모은다.</summary>
        private static void ValidateCategoryChances(DropTableDefinition table, string path, List<string> errors)
        {
            var categories = new HashSet<string>(StringComparer.Ordinal);
            int total = 0;
            foreach (DropCategoryChance chance in table.CategoryChances)
            {
                if (!Categories.Contains(chance.Category) || !categories.Add(chance.Category) || chance.Percent < 0 || chance.Percent > TOTAL_PERCENT)
                    errors.Add(path + ":_categoryChances 분류가 없거나 중복이거나 확률이 0~100 밖입니다: " + chance.Category);
                total += chance.Percent;
            }
            if (total != TOTAL_PERCENT) errors.Add(path + ":_categoryChances 행동·강화·속성 확률의 합은 100이어야 합니다. 현재 " + total);
        }

        /// <summary>등급 확률이 행동·강화 분류의 등급표에 있는 중복 없는 등급이고 0 이상 100 이하이며 분류별 합이 100인지 검사해 오류를 목록에 모은다.</summary>
        private static void ValidateGradeChances(DropTableDefinition table, string path, List<string> errors, RuneCatalog runes, ModifierGradeTable modifierGrades)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (DropGradeChance chance in table.GradeChances)
            {
                IReadOnlyList<string> order = GetGradeOrder(chance.Category, runes, modifierGrades);
                if (order == null || !order.Contains(chance.Grade) || !keys.Add(chance.Category + "/" + chance.Grade)
                    || chance.Percent < 0 || chance.Percent > TOTAL_PERCENT)
                    errors.Add(path + ":_gradeChances 분류·등급이 없거나 중복이거나 확률이 0~100 밖입니다: " + chance.Category + " " + chance.Grade);
                if (chance.Category == null) continue;
                totals[chance.Category] = (totals.TryGetValue(chance.Category, out int total) ? total : 0) + chance.Percent;
            }
            foreach (KeyValuePair<string, int> total in totals)
                if (total.Value != TOTAL_PERCENT) errors.Add(path + ":_gradeChances " + total.Key + " 등급 확률의 합은 100이어야 합니다. 현재 " + total.Value);
        }

        /// <summary>후보 룬이 항목 분류와 같은 분류의 활성 룬(속성은 전투 속성)이고 중복이 없으며 가중치가 0보다 큰 유한값인지 검사해 오류를 목록에 모은다.</summary>
        private static void ValidateEntries(DropTableDefinition table, string path, List<string> errors, RuneCatalog runes, ModifierGradeTable modifierGrades)
        {
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < table.Entries.Count; i++)
            {
                DropEntry entry = table.Entries[i];
                string entryPath = path + ":_entries[" + i + "]";
                if (!Categories.Contains(entry.Type))
                { errors.Add(entryPath + "._type 분류는 " + string.Join(", ", Categories) + " 중 하나여야 합니다: " + entry.Type); continue; }
                if (!runes.TryGet(entry.Id, out RuneDefinition rune) || rune.Category != entry.Type || rune.IsDisabled)
                    errors.Add(entryPath + "._id 비활성이 아니고 " + entry.Type + " 분류인 룬이 없습니다: " + entry.Id);
                else if (entry.Type == TYPE_ELEMENT && !RuneRewardRoller.IsCandidateGradeAvailable(runes, modifierGrades, entry.Id, ""))
                    errors.Add(entryPath + "._id 보상으로 줄 수 있는 전투 속성 룬이 아닙니다: " + entry.Id);
                if (!string.IsNullOrEmpty(entry.RewardGrade)
                    && (entry.Type != TYPE_ELEMENT || !modifierGrades.GradeOrder.Contains(entry.RewardGrade)
                        || !table.CategoryChances.Any(chance => chance.Category == TYPE_MODIFIER && chance.Percent > 0)))
                    errors.Add(entryPath + "._rewardGrade 모드 보상 분류가 있는 테이블의 속성에만 C·B·A·S 보상 등급을 지정할 수 있습니다.");
                if (!entryIds.Add(entry.Id ?? "")) errors.Add(entryPath + "._id 후보 룬이 중복됩니다: " + entry.Id);
                if (double.IsNaN(entry.Weight) || double.IsInfinity(entry.Weight) || entry.Weight <= 0)
                    errors.Add(entryPath + "._weight 가중치는 0보다 큰 유한값이어야 합니다.");
            }
        }

        /// <summary>확률이 0보다 큰 분류마다 나올 수 있는 모든 등급에 그 등급을 지원하는 후보가 1개 이상 있는지 검사해 오류를 목록에 모은다.</summary>
        private static void ValidateCandidates(DropTableDefinition table, string path, List<string> errors, RuneCatalog runes, ModifierGradeTable modifierGrades)
        {
            foreach (DropCategoryChance chance in table.CategoryChances)
            {
                if (chance.Percent <= 0 || !Categories.Contains(chance.Category)) continue;
                List<string> grades = table.GetPossibleGrades(chance.Category, runes, modifierGrades);
                if (grades.Count == 0) errors.Add(path + " " + chance.Category + " 분류에서 나올 수 있는 등급이 없습니다.");
                foreach (string grade in grades)
                    if (RuneRewardRoller.CollectCandidates(table, chance.Category, grade, runes, modifierGrades, null).Count == 0)
                        errors.Add(path + " " + chance.Category + " " + grade + " 등급을 지원하는 후보가 없습니다.");
            }
        }

        /// <summary>라운드 회복 선택지 설정의 확률과 등급별 비율을 검사해 오류를 목록에 모은다. 확률이 0보다 클 때만 비율 값을 요구한다.</summary>
        private static void ValidateRoundHealChoice(DropCatalog catalog, List<string> errors, RuneCatalog runes, ModifierGradeTable modifierGrades)
        {
            RoundHealChoiceDefinition choice = catalog._roundHealChoice;
            if (choice == null)
            { errors.Add("drops.json:_roundHealChoice 라운드 회복 선택지 설정이 없습니다."); return; }
            if (!IsValidPercent(choice.ChancePercent))
                errors.Add("drops.json:_roundHealChoice._chancePercent 확률은 0 이상 100 이하여야 합니다.");
            if (choice.ChancePercent <= 0) return;
            if (double.IsNaN(choice.BaseRatio) || choice.BaseRatio <= 0)
                errors.Add("drops.json:_roundHealChoice._baseRatio 확률이 0보다 크면 기본 비율은 0보다 커야 합니다.");
            if (double.IsNaN(choice.RatioPerGrade) || choice.RatioPerGrade < 0)
                errors.Add("drops.json:_roundHealChoice._ratioPerGrade 단계 비율은 0 이상이어야 합니다.");
            int behaviorIndex = GetGradeOrder(TYPE_BEHAVIOR, runes, modifierGrades).Count - 1;
            int modifierIndex = GetGradeOrder(TYPE_MODIFIER, runes, modifierGrades).Count - 1;
            if (choice.GetRatio(Math.Max(behaviorIndex, modifierIndex)) > 1)
                errors.Add("drops.json:_roundHealChoice 최대 등급 회복 비율은 1 이하여야 합니다.");
        }

        /// <summary>확률 값이 0 이상 100 이하인지 반환한다.</summary>
        private static bool IsValidPercent(double percent) => !double.IsNaN(percent) && percent >= 0 && percent <= 100;
    }
}
