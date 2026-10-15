namespace RuneCode
{
    /// <summary>발사 행동 규칙이다. 진행 방향으로 날아가는 발사체이며 관통·유도·발사 전용 다중 값을 쓴다.</summary>
    public sealed class LaunchBehaviorRules : SpellBehaviorRules
    {
        public override string RuneId => "behavior.launch";
        public override string FormValue => "launch";
        public override string Kind => SpellGrammar.FORM_BOLT;
        public override bool UsesPierce => true;
        public override bool UsesHoming => true;
        public override bool UsesLaunchGradeValues => true;
        public override bool IsTravelling => true;
        public override bool HasExclusiveHitAndExpire => true;

        /// <summary>비행 거리를 유지하도록 사거리 배율을 곱하고 속도 배율로 나눈 뒤 지속 배율을 곱한 수명을 반환한다.</summary>
        public override float GetLifetime(float lifetime, float rangeMultiplier, float speedMultiplier, float durationMultiplier)
            => lifetime * rangeMultiplier / speedMultiplier * durationMultiplier;

        /// <summary>무제한이 아닌 관통이면 관통 허용 수 + 1(마지막은 적 또는 벽)을 최대 OnHit 횟수로 반환한다.</summary>
        public override bool TryGetMaxHitEvents(SpellStats stats, out int count)
        {
            bool isBounded = stats.Pierce < SpellGrammar.UNLIMITED_PIERCE_COUNT;
            count = isBounded ? 1 + stats.Pierce : 0;
            return isBounded;
        }

        /// <summary>속도 × 수명의 비행 거리를 반환한다.</summary>
        public override float GetReach(SpellStats stats) => stats.Speed * stats.Lifetime;
    }
}
