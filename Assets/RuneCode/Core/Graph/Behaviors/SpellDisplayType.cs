namespace RuneCode
{
    /// <summary>마법 개체를 화면에 그리는 방식이다. 행동 규칙(SpellBehaviorRules)이 정하고 표시 코드는 이 값으로만 분기한다.</summary>
    public enum SpellDisplayType
    {
        Projectile,
        InstantArea,
        LastingArea
    }
}
