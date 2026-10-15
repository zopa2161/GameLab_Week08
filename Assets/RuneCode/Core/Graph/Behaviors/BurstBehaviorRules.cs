namespace RuneCode
{
    /// <summary>폭발 행동 규칙이다. 생성 위치에서 범위가 한 번 확장하며 대상별로 한 번 적중한다.</summary>
    public sealed class BurstBehaviorRules : SpellBehaviorRules
    {
        public override string RuneId => "behavior.burst";
        public override string FormValue => "explosion";
        public override string Kind => SpellGrammar.FORM_BURST;
        public override SpellDisplayType DisplayType => SpellDisplayType.InstantArea;
    }
}
