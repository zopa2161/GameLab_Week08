namespace RuneCode
{
    /// <summary>발사의 속도·수명 수식을 사용하며 적마다 한 번 타격하고 계속 확장하는 원형 파동을 정의한다.</summary>
    public sealed class RadiateBehaviorRules : LaunchBehaviorRules
    {
        public const string RADIATE_KIND = "radiate";

        public override string RuneId => "behavior.radiate";
        public override string FormValue => RADIATE_KIND;
        public override string Kind => RADIATE_KIND;
        public override SpellDisplayType DisplayType => SpellDisplayType.ArcProjectile;

        /// <summary>대상 수에 따라 적중 이벤트가 증가하므로 개체당 상한이 없음을 반환한다.</summary>
        public override bool TryGetMaxHitEvents(SpellStats stats, out int count)
        {
            count = 0;
            return false;
        }
    }
}
