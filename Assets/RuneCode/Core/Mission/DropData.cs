using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace RuneCode
{
    /// <summary>드롭 추첨 결과 한 항목이다. fragments는 Fragments 수량을, modifier는 RuneId·Grade·Count를 쓴다. element는 등급이 빈 문자열이다. eliteModifier 항목의 등급·종류와 등급이 빈 behavior 항목의 등급은 시뮬레이션이 스테이지 등급표로 정한다.</summary>
    public readonly struct DropRoll
    {
        private readonly string _type;
        private readonly string _runeId;
        private readonly string _grade;
        private readonly int _count;
        private readonly int _fragments;
        public string Type => _type;
        public string RuneId => _runeId;

        /// <summary>지정 항목의 등급이다. Modifier는 비어 있으면 그 Modifier에서 값이 있는 가장 낮은 등급을, behavior는 비어 있으면 스테이지 행동 등급 확률을, element는 항상 빈 문자열을 쓴다.</summary>
        public string Grade => _grade;
        public int Count => _count;
        public int Fragments => _fragments;

        /// <summary>항목 형식과 추첨으로 정해진 값으로 결과를 만든다.</summary>
        internal DropRoll(string type, string runeId, string grade, int count, int fragments)
        { _type = type; _runeId = runeId; _grade = grade; _count = count; _fragments = fragments; }
    }

    [Serializable]
    public sealed class DropEntry
    {
        [Header("드롭 항목")]
        [SerializeField] private string _type;
        [SerializeField] private string _id;
        [SerializeField] private string _grade;
        [SerializeField] private int _count;
        [SerializeField] private int _min;
        [SerializeField] private int _max;
        [SerializeField] private double _weight;
        public string Type => _type;
        public string Id => _id;
        public string Grade => _grade;
        public int Count => _count;
        public int Min => _min;
        public int Max => _max;
        public double Weight => _weight;
    }

    [Serializable]
    public sealed class DropTableDefinition
    {
        [Header("드롭 테이블")]
        [SerializeField] private string _id;
        [SerializeField] private int _rolls;
        [SerializeField] private DropEntry[] _guaranteed;
        [SerializeField] private DropEntry[] _entries;
        public string Id => _id;
        public int Rolls => _rolls;
        internal DropEntry[] Guaranteed => _guaranteed ?? Array.Empty<DropEntry>();
        internal DropEntry[] Entries => _entries ?? Array.Empty<DropEntry>();

        /// <summary>보장 항목을 모두 내고 rolls 횟수만큼 가중 추첨한 결과 목록을 반환한다. random은 0 이상 1 미만의 결정적 난수 함수다.</summary>
        public List<DropRoll> Roll(Func<double> random)
        {
            var rolls = new List<DropRoll>();
            foreach (DropEntry entry in Guaranteed) AddRoll(rolls, entry, random);
            for (int i = 0; i < _rolls; i++) AddRoll(rolls, PickWeightedEntry(random), random);
            return rolls;
        }

        /// <summary>가중치 합에 대한 난수 비율로 항목 하나를 고르고, 부동소수 오차로 고르지 못하면 마지막 항목을 쓴다.</summary>
        private DropEntry PickWeightedEntry(Func<double> random)
        {
            double totalWeight = 0;
            foreach (DropEntry entry in Entries) totalWeight += entry.Weight;
            double choice = random() * totalWeight;
            DropEntry last = Entries[Entries.Length - 1];
            foreach (DropEntry entry in Entries)
            {
                choice -= entry.Weight;
                if (choice <= 0) return entry;
            }
            return last;
        }

        /// <summary>항목 형식에 맞춰 결과를 만든다. fragments는 min 이상 max 이하 수량을 정하고 none은 결과를 만들지 않는다. Modifier 등급은 시뮬레이션이 정하고 element 등급은 빈 문자열로 정규화한다.</summary>
        private static void AddRoll(List<DropRoll> rolls, DropEntry entry, Func<double> random)
        {
            if (entry.Type == DropCatalog.TYPE_NONE) return;
            if (entry.Type == DropCatalog.TYPE_FRAGMENTS)
            { rolls.Add(new DropRoll(DropCatalog.TYPE_FRAGMENTS, null, null, 0, entry.Min + (int)(random() * (entry.Max - entry.Min + 1)))); return; }
            rolls.Add(new DropRoll(entry.Type, entry.Id, entry.Type == DropCatalog.TYPE_ELEMENT ? "" : entry.Grade, entry.Count, 0));
        }
    }

    [Serializable]
    public sealed class NormalEnemyDropDefinition
    {
        [Header("일반 적 드롭")]
        [SerializeField] private string _dropTable;
        [SerializeField] private double _chancePercent;
        public string DropTable => _dropTable;
        public double ChancePercent => _chancePercent;

        /// <summary>일반 적 드롭이 설정되어 있는지 반환한다. 테이블 ID가 비어 있거나 확률이 0 이하면 false다.</summary>
        public bool IsEnabled => !string.IsNullOrEmpty(_dropTable) && _chancePercent > 0;
    }

    [Serializable]
    public sealed class DropCatalog
    {
        public const string TYPE_FRAGMENTS = "fragments";
        public const string TYPE_MODIFIER = "modifier";
        public const string TYPE_ELEMENT = "element";
        public const string TYPE_BEHAVIOR = "behavior";
        public const string TYPE_NONE = "none";

        /// <summary>Modifier 항목의 특수 ID다. 특정 룬 대신 쓰면 엘리트 Modifier 등급표로 등급·종족을 정한다.</summary>
        public const string ELITE_MODIFIER_ENTRY_ID = "eliteModifier";
        public const string RANDOM_ELEMENT_ENTRY_ID = "randomElement";
        public const string RANDOM_BEHAVIOR_ENTRY_ID = "randomBehavior";

        [Header("드롭 테이블 목록")]
        [SerializeField] private DropTableDefinition[] _dropTables;
        public IReadOnlyList<DropTableDefinition> Tables => _dropTables;

        [Header("일반 적 드롭")]
        [SerializeField] private NormalEnemyDropDefinition _normalEnemyDrop;

        /// <summary>엘리트·보스가 아닌 적이 처치될 때 굴리는 드롭 설정이다. 필드가 없으면 null이다.</summary>
        public NormalEnemyDropDefinition NormalEnemyDrop => _normalEnemyDrop;

        /// <summary>드롭 테이블 ID의 존재 여부를 반환한다.</summary>
        public bool Has(string id)
        { for (int i = 0; i < _dropTables.Length; i++) if (_dropTables[i].Id == id) return true; return false; }

        /// <summary>드롭 테이블 ID에 해당하는 정의를 반환하고 누락된 ID에는 데이터 오류를 발생시킨다.</summary>
        public DropTableDefinition Get(string id)
        { for (int i = 0; i < _dropTables.Length; i++) if (_dropTables[i].Id == id) return _dropTables[i]; throw new FormatException("등록되지 않은 드롭 테이블: " + id); }

        /// <summary>JSON 드롭 설정을 읽고 모든 테이블의 검증 오류를 모아 한 번에 보고한다. Modifier·element·behavior 룬의 존재와 카테고리·비활성 여부는 GameData.Runes로 확인한다.</summary>
        public static DropCatalog FromJson(string json)
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
                if (table.Rolls < 0) errors.Add("drops.json:" + tableId + ":_rolls 추첨 횟수는 0 이상이어야 합니다.");
                if (table.Rolls > 0 && table.Entries.Length == 0) errors.Add("drops.json:" + tableId + ":_entries 추첨 횟수가 있으면 항목이 1개 이상 필요합니다.");
                ValidateEntries(table.Guaranteed, tableId, "_guaranteed", errors, false);
                ValidateEntries(table.Entries, tableId, "_entries", errors, true);
            }
            ValidateNormalEnemyDrop(catalog, ids, errors);
            if (errors.Count > 0) throw new FormatException("drops.json 검증 오류:\n" + string.Join("\n", errors));
            return catalog;
        }

        /// <summary>일반 적 드롭 설정의 테이블 ID가 등록된 ID인지, 확률이 0 이상 100 이하인지 검사하고 오류를 목록에 모은다. 테이블 ID가 비어 있으면 일반 적 드롭이 없다.</summary>
        private static void ValidateNormalEnemyDrop(DropCatalog catalog, HashSet<string> tableIds, List<string> errors)
        {
            NormalEnemyDropDefinition drop = catalog._normalEnemyDrop;
            if (drop == null || string.IsNullOrEmpty(drop.DropTable)) return;
            if (!tableIds.Contains(drop.DropTable)) errors.Add("drops.json:_normalEnemyDrop._dropTable 등록되지 않은 드롭 테이블입니다: " + drop.DropTable);
            if (double.IsNaN(drop.ChancePercent) || drop.ChancePercent < 0 || drop.ChancePercent > 100)
                errors.Add("drops.json:_normalEnemyDrop._chancePercent 확률은 0 이상 100 이하여야 합니다.");
        }

        /// <summary>테이블의 항목 배열을 검사하고 오류를 목록에 모은다. 가중치는 추첨 항목에서만 요구한다.</summary>
        private static void ValidateEntries(DropEntry[] entries, string tableId, string field, List<string> errors, bool checkWeight)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                DropEntry entry = entries[i];
                string path = "drops.json:" + tableId + ":" + field + "[" + i + "]";
                if (entry.Type != TYPE_FRAGMENTS && entry.Type != TYPE_MODIFIER && entry.Type != TYPE_ELEMENT
                    && entry.Type != TYPE_BEHAVIOR && entry.Type != TYPE_NONE)
                { errors.Add(path + "._type 항목 형식은 " + TYPE_FRAGMENTS + ", " + TYPE_MODIFIER + ", " + TYPE_ELEMENT + ", "
                    + TYPE_BEHAVIOR + ", " + TYPE_NONE + " 중 하나여야 합니다: " + entry.Type); continue; }
                if (checkWeight && (double.IsNaN(entry.Weight) || double.IsInfinity(entry.Weight) || entry.Weight <= 0))
                    errors.Add(path + "._weight 가중치는 0보다 큰 유한값이어야 합니다.");
                if (entry.Type == TYPE_MODIFIER)
                {
                    // eliteModifier 항목은 등급표 조회로 등급·종족을 정하므로 룬 ID 검사를 생략한다.
                    if (entry.Id != ELITE_MODIFIER_ENTRY_ID
                        && (!GameData.Runes.TryGet(entry.Id, out RuneDefinition rune) || rune.Category != SpellGrammar.CATEGORY_MODIFIER))
                        errors.Add(path + "._id Modifier 룬이 없거나 Modifier가 아닙니다: " + entry.Id + " (특수 ID " + ELITE_MODIFIER_ENTRY_ID + "도 쓸 수 있습니다)");
                    if (entry.Count < 1) errors.Add(path + "._count 수량은 1 이상이어야 합니다.");
                    if (entry.Id != ELITE_MODIFIER_ENTRY_ID && !string.IsNullOrEmpty(entry.Grade) && !GameData.ModifierGrades.IsGradeAvailable(entry.Id, entry.Grade))
                        errors.Add(path + "._grade 이 Modifier에 없는 등급입니다: " + entry.Id + " " + entry.Grade);
                }
                else if (entry.Type == TYPE_ELEMENT)
                {
                    if (entry.Id != RANDOM_ELEMENT_ENTRY_ID && (!GameData.Runes.TryGet(entry.Id, out RuneDefinition rune)
                        || rune.Category != SpellGrammar.CATEGORY_ELEMENT || rune.IsDisabled))
                        errors.Add(path + "._id 비활성이 아니고 element 카테고리인 룬이 없습니다: " + entry.Id);
                    if (!string.IsNullOrEmpty(entry.Grade)) errors.Add(path + "._grade 속성 항목에는 등급을 넣지 않습니다: " + entry.Id + " " + entry.Grade);
                    if (entry.Count < 1) errors.Add(path + "._count 수량은 1 이상이어야 합니다.");
                }
                else if (entry.Type == TYPE_BEHAVIOR)
                {
                    if (entry.Id != RANDOM_BEHAVIOR_ENTRY_ID && (!GameData.Runes.TryGet(entry.Id, out RuneDefinition rune)
                        || rune.Category != SpellGrammar.CATEGORY_BEHAVIOR || rune.IsDisabled))
                        errors.Add(path + "._id 비활성이 아니고 behavior 카테고리인 룬이 없습니다: " + entry.Id);
                    // 등급이 비어 있으면 시뮬레이션이 스테이지 행동 등급 확률로 정한다.
                    if (!string.IsNullOrEmpty(entry.Grade) && !GameData.Runes.BehaviorGradeOrder.Contains(entry.Grade))
                        errors.Add(path + "._grade 행동 등급표에 없는 등급입니다: " + entry.Id + " " + entry.Grade);
                    if (entry.Count < 1) errors.Add(path + "._count 수량은 1 이상이어야 합니다.");
                }
                else if (entry.Type == TYPE_FRAGMENTS)
                {
                    if (entry.Min < 0 || entry.Max < entry.Min) errors.Add(path + "._min/_max 조각 수량은 0 <= min <= max여야 합니다.");
                }
            }
        }
    }
}
