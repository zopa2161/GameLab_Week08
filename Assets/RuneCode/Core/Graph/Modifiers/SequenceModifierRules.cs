namespace RuneCode
{
    /// <summary>
    /// 연속 모드 규칙이다. 등급표의 회차 수 값(없으면 모드 수치)으로 실행 회차를 정하는 배치 모드이며, 피해 ×0.8과 회차 간격은 수치로 SpellStats와 SpellAction이 처리한다.
    /// 회차 사이의 재예약과 실행은 RuneSimulation이 맡는다.
    /// </summary>
    public sealed class SequenceModifierRules : SpellModifierRules
    {
        public const string RUNE_ID = "mod.sequence";

        public override string RuneId => RUNE_ID;
        public override SpellModifierCategory Category => SpellModifierCategory.Placement;

        /// <summary>발사·폭발·참격 행동에 장착할 수 있다.</summary>
        public override bool CanAttachTo(SpellBehaviorRules behavior)
            => behavior != null && (behavior.RuneId == "behavior.launch" || behavior.RuneId == "behavior.burst"
                || behavior.RuneId == SlashBehaviorRules.RUNE_ID);

        /// <summary>
        /// 등급표의 회차 수 값이 있으면 그 값으로, 없으면 모드 수치의 회차 수(0 이하면 1)로 실행 회차를 바꾼다.
        /// </summary>
        public override int ModifyWaveCount(int waveCount, SpellBehaviorRules behavior, string grade, RuneStats modifierStats,
            ModifierGradeTable modifierGrades)
        {
            if (modifierGrades != null && modifierGrades.TryGetWaveCount(RUNE_ID, grade, out int gradeCount)) return gradeCount;
            return modifierStats.WaveCount > 0 ? modifierStats.WaveCount : 1;
        }
    }
}
