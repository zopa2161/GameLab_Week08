namespace RuneCode
{
    /// <summary>잔류 행동 규칙이다. 예고 뒤 지면 생성 위치에 남아 주기마다 피해를 주는 정지형이다.</summary>
    public sealed class PersistBehaviorRules : SpellBehaviorRules
    {
        public override string RuneId => "behavior.persist";
        public override string FormValue => "remain";
        public override string Kind => SpellGrammar.FORM_ZONE;
        public override SpellDisplayType DisplayType => SpellDisplayType.LastingArea;
        public override SpellSpawnOrigin SpawnOrigin => SpellSpawnOrigin.Ground;
        public override SpellMotionKind MotionKind => SpellMotionKind.Stationary;
    }
}
