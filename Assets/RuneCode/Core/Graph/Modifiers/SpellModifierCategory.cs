namespace RuneCode
{
    /// <summary>
    /// 동작 모드가 개입하는 시점에 따른 분류다. 배치는 생성, 이동은 매 틱 이동, 적중은 적중 처리에 개입한다.
    /// 수치만 바꾸는 모드(증폭·확장·속도·지속시간)는 클래스로 만들지 않으므로 분류가 없다.
    /// </summary>
    public enum SpellModifierCategory
    {
        Placement,
        Motion,
        Hit
    }
}
