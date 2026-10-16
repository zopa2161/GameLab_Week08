namespace RuneCode
{
    /// <summary>빔의 경로형 이동 성격, 유지 시간, 사거리와 표시 유형을 정의한다.</summary>
    public sealed class BeamBehaviorRules : SpellBehaviorRules
    {
        public const string BEAM_KIND = "beam";

        public override string RuneId => "behavior.beam";
        public override string FormValue => BEAM_KIND;
        public override string Kind => BEAM_KIND;
        public override SpellDisplayType DisplayType => SpellDisplayType.Beam;
        public override SpellMotionKind MotionKind => SpellMotionKind.Path;

        /// <summary>빔의 기준 수명 수치는 다른 수식으로 바꾸지 않는다. 직접 시전 홀드의 실제 종료는 런타임 채널이 입력·자원으로 정한다.</summary>
        public override float GetLifetime(float lifetime, float rangeMultiplier, float speedMultiplier, float durationMultiplier)
            => lifetime;

        /// <summary>빔은 맵 끝까지 도달하므로 자동 시전의 사거리 제한을 제거한다.</summary>
        public override float GetReach(SpellStats stats) => float.MaxValue;
    }
}
