namespace RuneCode
{
    /// <summary>
    /// 다중 모드 규칙이다. 등급표의 개수 값으로 생성 개수를 정하는 배치 모드이며, 피해 ×0.7과 퍼짐 각도는 수치로 SpellStats가 처리한다.
    /// 지팡이 앞에서 생성되는 행동(발사·빔)은 등급표의 발사 전용 개수 값을 쓴다.
    /// </summary>
    public sealed class MultiModifierRules : SpellModifierRules
    {
        public const string RUNE_ID = "mod.multi";

        public override string RuneId => RUNE_ID;
        public override SpellModifierCategory Category => SpellModifierCategory.Placement;

        /// <summary>
        /// 등급표의 개수 값(행동에 맞는 값)이 있으면 그 값으로, 없으면 모드 기본 개수로 생성 개수를 바꾼다.
        /// </summary>
        public override int ModifyCount(int count, SpellBehaviorRules behavior, string grade, RuneStats modifierStats,
            ModifierGradeTable modifierGrades)
        {
            bool usesLaunchValues = behavior != null && behavior.SpawnOrigin == SpellSpawnOrigin.Wand;
            if (modifierGrades != null && modifierGrades.TryGetCountValue(RUNE_ID, grade, usesLaunchValues, out int gradeCount)) return gradeCount;
            return modifierStats.Count;
        }
    }
}
