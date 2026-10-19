using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>룬 합성의 재료 수, 승급 순서 및 보상에 공통으로 쓰는 활성 룬·지원 등급 조건을 제공한다.</summary>
    public static class RuneSynthesisRules
    {
        public const int MATERIAL_COUNT = 3;
        public const int SCRAP_COST = 200;
        private static readonly string[] GRADES = { "C", "B", "A", "S" };

        public static IReadOnlyList<string> GradeOrder { get; } = Array.AsReadOnly(GRADES);

        /// <summary>재료의 합성 결과 등급을 반환한다. S는 S를 유지하고 알 수 없는 등급이면 null이다.</summary>
        public static string NextGrade(string grade)
        {
            int index = Array.IndexOf(GRADES, grade);
            return index >= 0 ? GRADES[Math.Min(index + 1, GRADES.Length - 1)] : null;
        }

        /// <summary>활성 행동·모드의 지원 등급 또는 무등급 전투 속성인지 반환한다. 일반·회복·보호 속성은 제외한다.</summary>
        public static bool IsEligible(RuneDefinition rune, string grade)
        {
            if (rune == null || rune.IsDisabled) return false;
            if (rune.Category == SpellGrammar.CATEGORY_ELEMENT)
                return string.IsNullOrEmpty(grade) && GameData.Runes.TryGetRuneElement(rune.Id, out ElementDefinition element)
                    && element.Category == SpellGrammar.ELEMENT_CATEGORY_ELEMENTAL && element.Id != SpellGrammar.ELEMENT_NEUTRAL;
            if (Array.IndexOf(GRADES, grade) < 0) return false;
            if (rune.Category == SpellGrammar.CATEGORY_MODIFIER)
                return GameData.ModifierGrades.IsGradeAvailable(rune.Id, grade);
            if (rune.Category != SpellGrammar.CATEGORY_BEHAVIOR) return false;
            foreach (string available in GameData.Runes.BehaviorGradeOrder)
                if (available == grade) return true;
            return false;
        }

        /// <summary>지정 분류·결과 등급에 실제로 존재하는 활성 룬 ID를 중복 없이 반환한다.</summary>
        public static List<string> GetResults(string category, string grade)
        {
            var results = new List<string>();
            foreach (RuneDefinition rune in GameData.Runes.All)
                if (rune.Category == category && IsEligible(rune, grade) && !results.Contains(rune.Id)) results.Add(rune.Id);
            return results;
        }
    }
}
