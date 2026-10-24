namespace RuneCode
{
    /// <summary>
    /// 다중 모드 규칙이다. 발사·빔·참격은 등급표의 개수 값으로 생성 개수를 정하고, 폭발은 생성 개수를 늘리지 않고 등급표의 회차 수로 반복한다.
    /// 피해 배율·퍼짐·회차 간격은 룬 수치로 SpellStats와 SpellAction이 처리하며, 잔류에는 장착할 수 없다.
    /// </summary>
    public sealed class MultiModifierRules : SpellModifierRules
    {
        public const string RUNE_ID = "mod.multi";

        public override string RuneId => RUNE_ID;
        public override SpellModifierCategory Category => SpellModifierCategory.Placement;

        /// <summary>잔류를 제외한 모든 행동에 장착할 수 있다. runes.json의 _modifierTargets 검사는 이와 별도로 계속 적용된다.</summary>
        public override bool CanAttachTo(SpellBehaviorRules behavior)
            => behavior != null && behavior.DisplayType != SpellDisplayType.LastingArea;

        /// <summary>
        /// 폭발이면 생성 개수를 바꾸지 않고 그대로 반환한다. 그 외에는 등급표의 개수 값(지팡이 앞 행동은 발사 전용 값)이 있으면 그 값으로,
        /// 없으면 모드 기본 개수로 생성 개수를 바꾼다.
        /// </summary>
        public override int ModifyCount(int count, SpellBehaviorRules behavior, string grade, RuneStats modifierStats,
            ModifierGradeTable modifierGrades)
        {
            // 폭발은 개수를 늘리지 않고 회차로 반복한다. 폭발도 지팡이 앞 생성이라 이 분기를 먼저 둔다.
            if (behavior != null && behavior.DisplayType == SpellDisplayType.ExpandingRing) return count;
            bool usesLaunchValues = behavior != null && behavior.SpawnOrigin == SpellSpawnOrigin.Wand;
            if (modifierGrades != null && modifierGrades.TryGetCountValue(RUNE_ID, grade, usesLaunchValues, out int gradeCount)) return gradeCount;
            return modifierStats.Count;
        }

        /// <summary>
        /// 폭발에 장착했을 때만 등급표의 회차 수 값(없으면 모드 수치의 회차 수, 0 이하면 1)으로 실행 회차를 바꾼다.
        /// 그 외 행동은 회차 수를 바꾸지 않는다.
        /// </summary>
        public override int ModifyWaveCount(int waveCount, SpellBehaviorRules behavior, string grade, RuneStats modifierStats,
            ModifierGradeTable modifierGrades)
        {
            if (behavior == null || behavior.DisplayType != SpellDisplayType.ExpandingRing) return waveCount;
            if (modifierGrades != null && modifierGrades.TryGetWaveGradeCount(RUNE_ID, grade, out int gradeWaveCount)) return gradeWaveCount;
            return modifierStats.WaveCount > 0 ? modifierStats.WaveCount : 1;
        }
    }
}
