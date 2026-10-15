namespace RuneCode
{
    /// <summary>빔의 고정 폭, 유도·발사 퍼짐, 유지 시간과 표시 유형을 정의한다.</summary>
    public sealed class BeamBehaviorRules : SpellBehaviorRules
    {
        public const string BEAM_KIND = "beam";

        public override string RuneId => "behavior.beam";
        public override string FormValue => BEAM_KIND;
        public override string Kind => BEAM_KIND;
        public override SpellDisplayType DisplayType => SpellDisplayType.Beam;
        public override bool UsesHoming => true;
        public override bool UsesLaunchGradeValues => true;

        /// <summary>빔의 최대 수명은 속도·사거리·지속 수식과 관계없이 데이터의 고정값을 사용한다.</summary>
        public override float GetLifetime(float lifetime, float rangeMultiplier, float speedMultiplier, float durationMultiplier)
            => lifetime;

        /// <summary>빔은 맵 끝까지 도달하므로 자동 시전의 사거리 제한을 제거한다.</summary>
        public override float GetReach(SpellStats stats) => float.MaxValue;
    }
}
