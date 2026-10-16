namespace RuneCode
{
    /// <summary>레거시 폭발 행동 규칙이다. 생성 위치에서 범위가 한 번 확장하며 대상별로 한 번 적중한다.</summary>
    public sealed class LegacyBurstBehaviorRules : SpellBehaviorRules
    {
        public override string RuneId => "behavior.legacy_burst";
        public override string FormValue => "legacy_explosion";
        public override string Kind => SpellGrammar.FORM_LEGACY_BURST;
        public override SpellDisplayType DisplayType => SpellDisplayType.InstantArea;
        public override SpellSpawnOrigin SpawnOrigin => SpellSpawnOrigin.Ground;
        public override SpellMotionKind MotionKind => SpellMotionKind.Stationary;
    }
}
