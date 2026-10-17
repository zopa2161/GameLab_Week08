using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>처치·클리어·속성 조합에 공통으로 쓰는 활성 룬 및 스테이지 등급 추첨 규칙이다.</summary>
    public static class RuneRewardRoller
    {
        /// <summary>보스면 직전 일반 스테이지 번호를, 그 외에는 요청한 번호를 반환한다. 보스 선택 보상에는 사용하지 않는다.</summary>
        public static int GetRewardStage(int stage)
        {
            ModifierStageDropTable table = GameData.ModifierGrades.GetStageDropTable(stage);
            if (table == null || table.StageType != "boss") return stage;
            int previous = 0;
            foreach (ModifierStageDropTable candidate in GameData.ModifierGrades.StageDropTables)
                if (candidate.StageType == "normal" && candidate.Stage < stage && candidate.Stage > previous) previous = candidate.Stage;
            if (previous == 0) throw new InvalidOperationException("보스 보상에 적용할 이전 일반 스테이지가 없습니다: " + stage);
            return previous;
        }

        /// <summary>설정된 단일 드롭에서 분류를 뽑고 실제 룬·등급을 결정해 정확히 1개를 반환한다.</summary>
        public static DropRoll DrawRune(DropTableDefinition table, int stage, IEliteModifierDropSource grades, Func<double> random)
        {
            List<DropRoll> rolls = table.Roll(random);
            if (rolls.Count != 1 || rolls[0].Count != 1)
                throw new InvalidOperationException("룬 보상 테이블은 정확히 룬 1개를 제공해야 합니다: " + table.Id);
            DropRoll roll = rolls[0];
            if (roll.RuneId == DropCatalog.RANDOM_ELEMENT_ENTRY_ID)
                return DrawCategory(SpellGrammar.CATEGORY_ELEMENT, stage, grades, random);
            if (roll.RuneId == DropCatalog.RANDOM_BEHAVIOR_ENTRY_ID)
                return DrawCategory(SpellGrammar.CATEGORY_BEHAVIOR, stage, grades, random);
            if (roll.RuneId == DropCatalog.ELITE_MODIFIER_ENTRY_ID)
                return DrawCategory(SpellGrammar.CATEGORY_MODIFIER, stage, grades, random);
            if (!GameData.Runes.TryGet(roll.RuneId, out RuneDefinition rune) || rune.IsDisabled)
                throw new InvalidOperationException("룬 보상 후보가 유효하지 않습니다: " + roll.RuneId);
            string grade = roll.Grade ?? "";
            if (rune.Category == SpellGrammar.CATEGORY_MODIFIER && string.IsNullOrEmpty(grade))
                grade = GameData.ModifierGrades.GetLowestAvailableGrade(rune.Id);
            else if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR && string.IsNullOrEmpty(grade))
                grade = RollGrade(grades.GetBehaviorGradeChances(GetRewardStage(stage)), random);
            if (!RuneSynthesisRules.IsEligible(rune, grade)) throw new InvalidOperationException("룬 보상 등급이 유효하지 않습니다: " + rune.Id);
            return new DropRoll(roll.Type, rune.Id, grade, 1, 0);
        }

        /// <summary>분류별 활성 룬 1개를 추첨한다. 행동은 종류 후 등급, 모드는 등급 후 유효 종류를 고르며 속성은 무등급이다.</summary>
        public static DropRoll DrawCategory(string category, int stage, IEliteModifierDropSource grades, Func<double> random)
        {
            int rewardStage = GetRewardStage(stage);
            string grade;
            string runeId;
            if (category == SpellGrammar.CATEGORY_ELEMENT)
            {
                grade = "";
                runeId = PickRune(category, grade, random);
            }
            else if (category == SpellGrammar.CATEGORY_BEHAVIOR)
            {
                runeId = PickRune(category, SpellGrammar.DEFAULT_BEHAVIOR_GRADE, random);
                grade = RollGrade(grades.GetBehaviorGradeChances(rewardStage), random);
            }
            else if (category == SpellGrammar.CATEGORY_MODIFIER)
            {
                grade = RollGrade(grades.GetGradeChances(rewardStage), random);
                runeId = PickRune(category, grade, random);
            }
            else throw new ArgumentException("룬 보상 분류가 유효하지 않습니다.", nameof(category));
            return new DropRoll(category, runeId, grade, 1, 0);
        }

        /// <summary>양수 확률만 합산해 등급 하나를 뽑는다. 확률표가 없으면 데이터 오류로 보고한다.</summary>
        private static string RollGrade(IReadOnlyList<EliteModifierGradeChance> chances, Func<double> random)
        {
            int total = 0;
            if (chances != null)
                foreach (EliteModifierGradeChance chance in chances) if (chance.Percent > 0) total += chance.Percent;
            if (total == 0) throw new InvalidOperationException("룬 보상 등급 확률표가 없습니다.");
            double roll = random() * total;
            string last = null;
            foreach (EliteModifierGradeChance chance in chances)
            {
                if (chance.Percent <= 0) continue;
                last = chance.Grade;
                roll -= chance.Percent;
                if (roll < 0) return chance.Grade;
            }
            return last;
        }

        /// <summary>지정 분류·등급에 실제로 존재하는 활성 룬 중 종류별 균등 추첨으로 ID 하나를 반환한다.</summary>
        public static string PickRune(string category, string grade, Func<double> random)
        {
            List<string> candidates = RuneSynthesisRules.GetResults(category, grade);
            if (candidates.Count == 0) throw new InvalidOperationException("룬 보상 후보가 없습니다: " + category + " " + grade);
            return candidates[Math.Min(candidates.Count - 1, (int)(random() * candidates.Count))];
        }
    }
}
