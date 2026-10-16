namespace RuneCode
{
    /// <summary>
    /// 관통 모드 규칙이다. 적중할 때마다 피해 감소와 소멸 시점을 바꾸는 적중 모드이며, 최대 OnHit 수에 관통 수를 더한다.
    /// 관통 수·관통 감소율(등급별 값)은 수치로 SpellStats가 처리한다.
    /// </summary>
    public sealed class PierceModifierRules : SpellModifierRules
    {
        public const string RUNE_ID = "mod.pierce";

        public override string RuneId => RUNE_ID;
        public override SpellModifierCategory Category => SpellModifierCategory.Hit;

        /// <summary>적중마다 소멸하는 이동형 행동(발사)에만 장착할 수 있다.</summary>
        public override bool CanAttachTo(SpellBehaviorRules behavior) => behavior.MotionKind == SpellMotionKind.Moving;

        /// <summary>관통 수만큼 최대 OnHit 수를 늘린다. 관통이 무제한이면 상한이 없으므로 false를 반환한다.</summary>
        public override bool TryModifyMaxHitEvents(SpellStats stats, ref int count)
        {
            if (stats.Pierce >= SpellGrammar.UNLIMITED_PIERCE_COUNT) return false;
            count += stats.Pierce;
            return true;
        }
    }
}
