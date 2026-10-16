using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    [Serializable]
    public sealed class LocalizedEntry
    {
        [Header("현지화 항목")]
        [SerializeField] private string _key;
        [SerializeField] private string _value;
        public string Key => _key;
        public string Value => _value;
    }

    [Serializable]
    public sealed class LocalizationData
    {
        [Header("한국어 문자열")]
        [SerializeField] private LocalizedEntry[] _entries;
        public IReadOnlyList<LocalizedEntry> Entries => _entries;
    }

    public static class GameData
    {
        private const string RUNE_TABLE = "tables/runes";
        private const string MODIFIER_GRADE_TABLE = "tables/elite_modifier_drops";

        private static RuneCatalog _runes;
        private static ModifierGradeTable _modifierGrades;
        private static BalanceData _balance;
        private static IReadOnlyList<SpellGraph> _spells;
        private static Dictionary<string, string> _strings;
        public static bool IsLoaded => _runes != null;
        public static RuneCatalog Runes => _runes;
        public static ModifierGradeTable ModifierGrades => _modifierGrades;
        public static BalanceData Balance => _balance;
        public static IReadOnlyList<SpellGraph> Spells => _spells;

        /// <summary>Resources의 룬 테이블(runes.json)과 밸런스·현지화·시작 마법 JSON을 한 번 읽고 스키마를 검증한다.</summary>
        public static void Load()
        {
            if (IsLoaded) return;
            LocalizationData strings = JsonUtility.FromJson<LocalizationData>(ReadResource("strings.ko"));
            if (strings?.Entries == null) throw new FormatException("한국어 문자열 데이터가 없습니다.");
            _strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (LocalizedEntry entry in strings.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Key) || !_strings.TryAdd(entry.Key, entry.Value))
                    throw new FormatException("중복되거나 잘못된 한국어 문자열 키입니다.");
            }
            RuneTableData runeTable = JsonUtility.FromJson<RuneTableData>(ReadResource(RUNE_TABLE));
            RuneCatalog runes = RuneCatalog.FromTable(runeTable);
            ValidateBehaviorRegistries(runes);
            ValidateModifierRegistries(runes);
            ModifierGradeTable modifierGrades = ModifierGradeTable.FromJson(ReadResource(MODIFIER_GRADE_TABLE), runes);
            runes.AttachModifierGrades(modifierGrades);
            BalanceData balance = BalanceData.FromJson(ReadResource("balance"));
            List<string> allRuneIds = new List<string>(runes.All.Count);
            foreach (RuneDefinition rune in runes.All) allRuneIds.Add(rune.Id);
            List<SpellGraph> spells = new List<SpellGraph>
            {
                ShareCodec.Deserialize(ReadResource("spells/energybolt"), modifierGrades),
                ShareCodec.Deserialize(ReadResource("spells/triplemissile"), modifierGrades)
            };
            foreach (SpellGraph graph in spells)
            {
                CompileResult result = GraphCompiler.Compile(graph, runes, balance.Grammar, allRuneIds, int.MaxValue);
                if (!result.Ok) throw new FormatException("시작 마법이 유효하지 않습니다: " + graph.Name);
            }
            _balance = balance;
            _modifierGrades = modifierGrades;
            _spells = spells;
            _runes = runes;
        }

        /// <summary>
        /// 등록된 행동 규칙마다 행동 룬·수치 룬이 룬 테이블에 있고 같은 종류의 실행 동작이 등록되어 있는지 검사한다.
        /// 하나라도 어긋나면 FormatException을 던진다.
        /// </summary>
        private static void ValidateBehaviorRegistries(RuneCatalog runes)
        {
            foreach (RuneDefinition rune in runes.All)
            {
                if (rune.IsDisabled || rune.Category != SpellGrammar.CATEGORY_BEHAVIOR) continue;
                if (!SpellBehaviorRuleRegistry.TryGetByRuneId(rune.Id, out _))
                    throw new FormatException("활성 행동의 규칙이 등록되지 않았습니다: " + rune.Id);
            }
            foreach (SpellBehaviorRules rules in SpellBehaviorRuleRegistry.All)
            {
                if (!runes.TryGet(rules.RuneId, out _) || !runes.TryGet(rules.StatsRuneId, out _))
                    throw new FormatException("행동 규칙의 룬 또는 수치 룬이 없습니다: " + rules.RuneId);
                if (!SpellBehaviorRuntimeRegistry.IsRegistered(rules.Kind))
                    throw new FormatException("행동 규칙에 맞는 실행 동작이 등록되지 않았습니다: " + rules.Kind);
            }
            RuneStats beam = runes.Get("form.beam").Stats;
            if (beam.MinimumLifetime <= 0 || beam.TickInterval <= 0 || beam.BeamLength <= 0 || beam.Speed <= 0)
                throw new FormatException("빔의 초기 결제 구간, 피해 간격, 곡선 길이와 경로 속도는 양수여야 합니다.");
            RuneStats burst = runes.Get("form.burst").Stats;
            if (burst.Speed <= 0 || burst.Lifetime <= 0 || burst.Radius <= 0 || burst.ConeAngle != 360)
                throw new FormatException("폭발의 속도·수명·두께 반경은 양수, 파동 각도는 360이어야 합니다.");
        }

        /// <summary>
        /// 등록된 동작 모드 규칙마다 모드 룬이 룬 테이블의 modifier 카테고리로 있고 같은 ID의 실행 동작이 등록되어 있는지,
        /// 등록된 실행 동작마다 규칙이 있는지 검사한다. 하나라도 어긋나면 FormatException을 던진다.
        /// </summary>
        private static void ValidateModifierRegistries(RuneCatalog runes)
        {
            foreach (SpellModifierRules rules in SpellModifierRuleRegistry.All)
            {
                if (!runes.TryGet(rules.RuneId, out RuneDefinition rune) || rune.Category != SpellGrammar.CATEGORY_MODIFIER)
                    throw new FormatException("동작 모드 규칙의 모드 룬이 없습니다: " + rules.RuneId);
                if (!SpellModifierRuntimeRegistry.TryGet(rules.RuneId, out _))
                    throw new FormatException("동작 모드 규칙에 맞는 실행 동작이 등록되지 않았습니다: " + rules.RuneId);
            }
            foreach (SpellModifierRuntime runtime in SpellModifierRuntimeRegistry.All)
                if (!SpellModifierRuleRegistry.TryGetByRuneId(runtime.RuneId, out _))
                    throw new FormatException("동작 모드 실행에 맞는 규칙이 등록되지 않았습니다: " + runtime.RuneId);
        }

        /// <summary>시작 마법 템플릿 중 ID가 일치하는 그래프를 반환하며 없으면 null을 반환한다.</summary>
        public static SpellGraph FindSpell(string id)
        {
            foreach (SpellGraph graph in _spells)
                if (graph.Id == id) return graph;
            return null;
        }

        /// <summary>현지화 키의 한국어 문구를 반환하고 누락된 키는 그대로 표시한다.</summary>
        public static string L(string key)
        {
            if (_strings != null && _strings.TryGetValue(key, out string value)) return value;
            return key;
        }

        /// <summary>RuneCode Resources 경로의 필수 텍스트 자산을 읽는다.</summary>
        private static string ReadResource(string path)
        {
            TextAsset asset = Resources.Load<TextAsset>("RuneCode/" + path);
            if (asset == null) throw new FormatException("필수 데이터 자산이 없습니다: " + path);
            return asset.text;
        }
    }
}
