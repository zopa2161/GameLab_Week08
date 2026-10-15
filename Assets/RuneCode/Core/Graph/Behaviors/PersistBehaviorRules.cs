namespace RuneCode
{
    /// <summary>잔류 행동 규칙이다. 예고 뒤 생성 위치에 남아 주기마다 피해를 주며 유도 수식이 있으면 대상을 따라간다.</summary>
    public sealed class PersistBehaviorRules : SpellBehaviorRules
    {
        public override string RuneId => "behavior.persist";
        public override string FormValue => "remain";
        public override string Kind => SpellGrammar.FORM_ZONE;
        public override SpellDisplayType DisplayType => SpellDisplayType.LastingArea;
        public override bool UsesHoming => true;
    }
}
