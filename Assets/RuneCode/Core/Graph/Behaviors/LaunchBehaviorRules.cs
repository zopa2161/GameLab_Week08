        namespace RuneCode
{
    /// <summary>발사 행동 규칙이다. 지팡이 앞에서 진행 방향으로 날아가는 이동형 발사체이며 첫 적중에 소멸한다.</summary>
    public sealed class LaunchBehaviorRules : SpellBehaviorRules
    {
        public override string RuneId => "behavior.launch";
        public override string FormValue => "launch";
        public override string Kind => SpellGrammar.FORM_BOLT;
        public override bool HasExclusiveHitAndExpire => true;

        /// <summary>비행 거리를 유지하도록 사거리 배율을 곱하고 속도 배율로 나눈 뒤 지속 배율을 곱한 수명을 반환한다.</summary>
        public override float GetLifetime(float lifetime, float rangeMultiplier, float speedMultiplier, float durationMultiplier)
            => lifetime * rangeMultiplier / speedMultiplier * durationMultiplier;

        /// <summary>발사체는 첫 적중(적 또는 벽)에 소멸하므로 최대 OnHit 1회다. 관통은 관통 모드가 이 수에 더한다.</summary>
        public override bool TryGetMaxHitEvents(SpellStats stats, out int count)
        {
            count = 1;
            return true;
        }

        /// <summary>속도 × 수명의 비행 거리를 반환한다.</summary>
        public override float GetReach(SpellStats stats) => stats.Speed * stats.Lifetime;
    }
}
