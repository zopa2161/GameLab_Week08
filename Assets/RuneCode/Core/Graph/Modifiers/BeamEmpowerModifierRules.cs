using System;

namespace RuneCode
{
    /// <summary>빔을 유지한 시간에 따라 피해·마나 비용·폭을 강화하는 과충전 모드 규칙이다.</summary>
    public sealed class BeamEmpowerModifierRules : SpellModifierRules
    {
        public const string RUNE_ID = "mod.beamEmpower";
        public const double CHANNEL_SECONDS = 2;
        public const double MAX_CHANNEL_SECONDS = 5;

        public override string RuneId => RUNE_ID;
        public override SpellModifierCategory Category => SpellModifierCategory.Hit;

        /// <summary>빔 행동에만 과충전 모드를 허용한다.</summary>
        public override bool CanAttachTo(SpellBehaviorRules behavior)
            => behavior != null && behavior.RuneId == "behavior.beam";

        /// <summary>연속 채널링 시간을 받아 강화 시작부터 최대 충전까지의 선형 진행률을 0~1로 반환한다.</summary>
        public static double GetChargeProgress(double channelSeconds)
            => Math.Max(0, Math.Min(1, (channelSeconds - CHANNEL_SECONDS) / (MAX_CHANNEL_SECONDS - CHANNEL_SECONDS)));
    }
}
