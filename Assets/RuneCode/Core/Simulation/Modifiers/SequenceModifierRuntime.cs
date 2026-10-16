namespace RuneCode
{
    /// <summary>
    /// 연속 모드의 실행 등록이다. 배치·이동·적중에 개입하지 않아 RuneId만 반환하며,
    /// 회차 사이의 재예약과 실행은 RuneSimulation이 맡는다.
    /// </summary>
    internal sealed class SequenceModifierRuntime : SpellModifierRuntime
    {
        public override string RuneId => SequenceModifierRules.RUNE_ID;
    }
}
