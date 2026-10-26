using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>처치·클리어·속성 조합에 공통으로 쓰는 활성 룬 및 스테이지 등급 추첨 규칙이다.</summary>
    public static class RuneRewardRoller
    {
        /// <summary>보스면 직전 일반 스테이지 번호를, 그 외에는 요청한 번호를 반환한다. 21스테이지 이후는 순환된 데이터 스테이지로 보스 여부를 판정한다. 보스 선택 보상에는 사용하지 않는다.</summary>
        public static int GetRewardStage(int stage)
        {
            int dataStage = StageCatalog.ResolveDataStage(stage);
            ModifierStageDropTable table = GameData.ModifierGrades.GetStageDropTable(dataStage);
            if (table == null || table.StageType != "boss") return stage;
            int previous = 0;
            foreach (ModifierStageDropTable candidate in GameData.ModifierGrades.StageDropTables)
                if (candidate.StageType == "normal" && candidate.Stage < dataStage && candidate.Stage > previous) previous = candidate.Stage;
            if (previous == 0) throw new InvalidOperationException("보스 보상에 적용할 이전 일반 스테이지가 없습니다: " + stage);
            return previous;
        }

        /// <summary>테이블에서 분류·공통 등급을 한 번 추첨하고, 해당 분류·등급의 서로 다른 룬을 최대 count개 반환한다. 남은 후보가 없으면 추첨을 마친다.</summary>
        public static List<DropRoll> DrawChoices(DropTableDefinition table, int stage, int count, IEliteModifierDropSource grades, Func<double> random)
        {
            return DrawChoices(table, stage, count, grades, random, out _, out _);
        }

        /// <summary>분류와 공통 등급을 추첨해 반환하고 해당 조건의 서로 다른 보상 후보를 최대 count개 만든다.</summary>
        public static List<DropRoll> DrawChoices(DropTableDefinition table, int stage, int count, IEliteModifierDropSource grades,
            Func<double> random, out string category, out string grade)
        {
            category = null;
            grade = null;
            if (count <= 0) return new List<DropRoll>();
            category = RollCategory(table.CategoryChances, random);
            grade = category == SpellGrammar.CATEGORY_ELEMENT ? "" : RollGrade(GetGradeChances(table, category, stage, grades), random);
            return DrawChoices(table, category, grade, count, random);
        }

        /// <summary>같은 분류·등급에서 직전 후보 밖의 룬을 먼저 가중 추첨하고 부족한 자리만 직전 후보로 채운다. 대체 후보가 없으면 빈 목록을 반환한다.</summary>
        public static List<DropRoll> DrawRerolledChoices(DropTableDefinition table, string category, string grade,
            IReadOnlyList<string> previousIds, Func<double> random)
        {
            var choices = new List<DropRoll>();
            var excludedIds = new HashSet<string>(previousIds, StringComparer.Ordinal);
            var chosenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int slot = 0; slot < previousIds.Count; slot++)
            {
                if (!TryDrawCandidate(table, category, grade, excludedIds, random, out DropRoll roll, true)) break;
                choices.Add(roll);
                chosenIds.Add(roll.RuneId);
                excludedIds.Add(roll.RuneId);
            }
            if (choices.Count == 0) return choices;
            while (choices.Count < previousIds.Count)
            {
                if (!TryDrawCandidate(table, category, grade, chosenIds, random, out DropRoll roll, true)) break;
                choices.Add(roll);
                chosenIds.Add(roll.RuneId);
            }
            return choices;
        }

        /// <summary>이전 저장의 후보 등급 또는 속성 보상 항목에서 공통 보상 등급을 복원한다. 속성 등급이 모호하면 null을 반환한다.</summary>
        public static string ResolveChoiceGrade(DropTableDefinition table, IReadOnlyList<string> ids, IReadOnlyList<string> grades)
        {
            foreach (string grade in grades)
                if (!string.IsNullOrEmpty(grade)) return grade;
            string rewardGrade = null;
            foreach (string id in ids)
                foreach (DropEntry entry in table.Entries)
                {
                    if (entry.Id != id || string.IsNullOrEmpty(entry.RewardGrade)) continue;
                    if (rewardGrade != null && rewardGrade != entry.RewardGrade) return null;
                    rewardGrade = entry.RewardGrade;
                }
            return rewardGrade;
        }

        /// <summary>분류·보상 등급으로 서로 다른 후보를 최대 count개 반환한다. 모드 선택 보상에는 등급 조건이 맞는 무등급 속성도 참여한다.</summary>
        public static List<DropRoll> DrawChoices(DropTableDefinition table, string category, string grade, int count, Func<double> random)
        {
            var choices = new List<DropRoll>();
            var chosenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int slot = 0; slot < count; slot++)
            {
                if (!TryDrawCandidate(table, category, grade, chosenIds, random, out DropRoll roll, true)) break;
                choices.Add(roll);
                chosenIds.Add(roll.RuneId);
            }
            return choices;
        }

        /// <summary>테이블에서 룬 1개를 추첨해 반환한다. 굴린 분류·등급을 지원하는 후보가 없으면 데이터 오류로 보고한다.</summary>
        public static DropRoll DrawRune(DropTableDefinition table, int stage, IEliteModifierDropSource grades, Func<double> random)
        {
            if (!TryDrawCandidate(table, stage, grades, null, random, out DropRoll roll))
                throw new InvalidOperationException("룬 보상 테이블에 굴린 분류·등급을 지원하는 후보가 없습니다: " + table.Id);
            return roll;
        }

        /// <summary>
        /// 테이블의 분류 확률로 분류를, 분류의 등급 확률(없으면 스테이지 등급표)로 등급을 굴린 뒤 그 등급을 지원하고 excludedIds에 없는 후보 중 가중치로 룬 1개를 고른다.
        /// 후보가 없으면 false를 반환한다.
        /// </summary>
        private static bool TryDrawCandidate(DropTableDefinition table, int stage, IEliteModifierDropSource grades, ICollection<string> excludedIds,
            Func<double> random, out DropRoll roll)
        {
            string category = RollCategory(table.CategoryChances, random);
            string grade = category == SpellGrammar.CATEGORY_ELEMENT ? "" : RollGrade(GetGradeChances(table, category, stage, grades), random);
            return TryDrawCandidate(table, category, grade, excludedIds, random, out roll);
        }

        /// <summary>분류·등급과 제외 ID로 가중치 추첨한다. 선택 보상이면 조건부 속성을 포함하며 속성의 실제 등급은 비워 반환한다.</summary>
        private static bool TryDrawCandidate(DropTableDefinition table, string category, string grade, ICollection<string> excludedIds,
            Func<double> random, out DropRoll roll, bool includesRewardElements = false)
        {
            roll = default;
            List<KeyValuePair<string, double>> candidates = includesRewardElements
                ? CollectChoiceCandidates(table, category, grade, GameData.Runes, GameData.ModifierGrades, excludedIds)
                : CollectCandidates(table, category, grade, GameData.Runes, GameData.ModifierGrades, excludedIds);
            double totalWeight = 0;
            foreach (KeyValuePair<string, double> candidate in candidates) totalWeight += candidate.Value;
            if (totalWeight <= 0) return false;
            double choice = random() * totalWeight;
            string picked = candidates[candidates.Count - 1].Key;
            foreach (KeyValuePair<string, double> candidate in candidates)
            {
                choice -= candidate.Value;
                if (choice > 0) continue;
                picked = candidate.Key;
                break;
            }
            string pickedCategory = GameData.Runes.Get(picked).Category;
            roll = new DropRoll(pickedCategory, picked, pickedCategory == SpellGrammar.CATEGORY_ELEMENT ? "" : grade, 1, 0);
            return true;
        }

        /// <summary>선택 보상 후보를 수집한다. 모드 보상이면 보상 등급 조건에 맞고 제외 ID에 없는 유효한 무등급 속성 후보도 추가한다.</summary>
        public static List<KeyValuePair<string, double>> CollectChoiceCandidates(DropTableDefinition table, string category, string grade,
            RuneCatalog runes, ModifierGradeTable modifierGrades, ICollection<string> excludedIds)
        {
            List<KeyValuePair<string, double>> candidates = CollectCandidates(table, category, grade, runes, modifierGrades, excludedIds);
            if (category != SpellGrammar.CATEGORY_MODIFIER) return candidates;
            foreach (DropEntry entry in table.Entries)
                if (entry.Type == SpellGrammar.CATEGORY_ELEMENT && entry.RewardGrade == grade
                    && IsCandidateAvailable(runes, modifierGrades, entry.Id, "", excludedIds))
                    candidates.Add(new KeyValuePair<string, double>(entry.Id, entry.Weight));
            return candidates;
        }

        /// <summary>분류의 등급 확률을 반환한다. 테이블에 그 분류의 등급 확률이 없으면 스테이지 행동·Modifier 등급표를 쓴다.</summary>
        private static IReadOnlyList<EliteModifierGradeChance> GetGradeChances(DropTableDefinition table, string category, int stage, IEliteModifierDropSource grades)
        {
            if (!table.HasGradeChances(category))
                return category == SpellGrammar.CATEGORY_BEHAVIOR
                    ? grades.GetBehaviorGradeChances(GetRewardStage(stage))
                    : grades.GetGradeChances(GetRewardStage(stage));
            var chances = new List<EliteModifierGradeChance>();
            foreach (DropGradeChance chance in table.GradeChances)
                if (chance.Category == category) chances.Add(new EliteModifierGradeChance(chance.Grade, chance.Percent));
            return chances;
        }

        /// <summary>
        /// 테이블에서 분류·등급을 지원하고 excludedIds에 없는 후보를 (룬 ID, 가중치) 목록으로 반환한다.
        /// 그 분류의 후보 항목이 없으면 runes의 그 분류 활성 룬 전체를 가중치 1로 쓴다.
        /// </summary>
        public static List<KeyValuePair<string, double>> CollectCandidates(DropTableDefinition table, string category, string grade,
            RuneCatalog runes, ModifierGradeTable modifierGrades, ICollection<string> excludedIds)
        {
            var candidates = new List<KeyValuePair<string, double>>();
            bool hasEntries = false;
            foreach (DropEntry entry in table.Entries)
            {
                if (entry.Type != category) continue;
                hasEntries = true;
                if (!string.IsNullOrEmpty(entry.RewardGrade)) continue;
                if (IsCandidateAvailable(runes, modifierGrades, entry.Id, grade, excludedIds))
                    candidates.Add(new KeyValuePair<string, double>(entry.Id, entry.Weight));
            }
            if (hasEntries) return candidates;
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (RuneDefinition rune in runes.All)
                if (rune.Category == category && seenIds.Add(rune.Id) && IsCandidateAvailable(runes, modifierGrades, rune.Id, grade, excludedIds))
                    candidates.Add(new KeyValuePair<string, double>(rune.Id, 1));
            return candidates;
        }

        /// <summary>후보 룬이 excludedIds에 없고 보상 추첨에서 숨겨지지 않았으며 지정 등급을 지원하는지 반환한다.</summary>
        private static bool IsCandidateAvailable(RuneCatalog runes, ModifierGradeTable modifierGrades, string runeId, string grade, ICollection<string> excludedIds)
            => (excludedIds == null || !excludedIds.Contains(runeId))
                && runes.TryGet(runeId, out RuneDefinition rune) && !rune.IsHiddenFromRewards
                && IsCandidateGradeAvailable(runes, modifierGrades, runeId, grade);

        /// <summary>후보 룬과 등급을 카탈로그로 확인해 활성 행동의 슬롯 등급, Modifier 효과 등급 또는 무등급 전투 속성(일반 속성 제외)이 지원되는지 반환한다.</summary>
        public static bool IsCandidateGradeAvailable(RuneCatalog runes, ModifierGradeTable modifierGrades, string runeId, string grade)
        {
            if (!runes.TryGet(runeId, out RuneDefinition rune) || rune.IsDisabled) return false;
            if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR)
                return runes.TryGetBehaviorSlots(grade, out _, out _);
            if (rune.Category == SpellGrammar.CATEGORY_ELEMENT)
                return string.IsNullOrEmpty(grade) && runes.TryGetRuneElement(runeId, out ElementDefinition element)
                    && element.Category == SpellGrammar.ELEMENT_CATEGORY_ELEMENTAL && element.Id != SpellGrammar.ELEMENT_NEUTRAL;
            return rune.Category == SpellGrammar.CATEGORY_MODIFIER && modifierGrades.IsGradeAvailable(runeId, grade);
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

        /// <summary>양수 확률만 합산해 분류 하나를 뽑는다. 확률표가 없으면 데이터 오류로 보고한다.</summary>
        private static string RollCategory(IReadOnlyList<DropCategoryChance> chances, Func<double> random)
        {
            int total = 0;
            foreach (DropCategoryChance chance in chances) if (chance.Percent > 0) total += chance.Percent;
            if (total == 0) throw new InvalidOperationException("룬 보상 분류 확률표가 없습니다.");
            double roll = random() * total;
            string last = null;
            foreach (DropCategoryChance chance in chances)
            {
                if (chance.Percent <= 0) continue;
                last = chance.Category;
                roll -= chance.Percent;
                if (roll < 0) return chance.Category;
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
