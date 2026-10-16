namespace RuneCode
{
    /// <summary>발사와 같은 이동·수명·적중 규칙을 사용하며 시간에 따라 커지는 호 투사체를 정의한다.</summary>
    public sealed class RadiateBehaviorRules : LaunchBehaviorRules
    {
        public const string RADIATE_KIND = "radiate";

        public override string RuneId => "behavior.radiate";
        public override string FormValue => RADIATE_KIND;
        public override string Kind => RADIATE_KIND;
        public override SpellDisplayType DisplayType => SpellDisplayType.ArcProjectile;
    }
}
