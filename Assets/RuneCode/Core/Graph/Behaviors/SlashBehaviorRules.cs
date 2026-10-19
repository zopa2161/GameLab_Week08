namespace RuneCode
{
    /// <summary>시전 중심을 따라 칼날이 회전하는 참격의 수명·사거리·표시 규칙이다.</summary>
    public sealed class SlashBehaviorRules : SpellBehaviorRules
    {
        public const string RUNE_ID = "behavior.slash";
        public const string KIND = "slash";

        public override string RuneId => RUNE_ID;
        public override string FormValue => KIND;
        public override string Kind => KIND;
        public override SpellDisplayType DisplayType => SpellDisplayType.Slash;
        public override SpellMotionKind MotionKind => SpellMotionKind.Stationary;

        /// <summary>전체 회전각을 유지하면서 속도 배율에 반비례하는 공격 시간을 반환한다.</summary>
        public override float GetLifetime(float lifetime, float rangeMultiplier, float speedMultiplier, float durationMultiplier)
            => lifetime / speedMultiplier;

        /// <summary>회전 중심부터 칼날 끝까지의 길이를 도달 거리로 반환한다.</summary>
        public override float GetReach(SpellStats stats) => stats.Radius;
    }
}
