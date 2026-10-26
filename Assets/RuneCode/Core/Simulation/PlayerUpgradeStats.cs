namespace RuneCode
{
    /// <summary>
    /// 강화 트리에서 합산한 플레이어 능력치와 효과 플래그를 시뮬레이션에 전달하는 묶음이다.
    /// 생략한 값은 강화가 없는 상태(배율 1, 추가량 0, 플래그 꺼짐)이며 최대 체력·전력·재생량 0은 밸런스 기본값을 뜻한다.
    /// </summary>
    public sealed class PlayerUpgradeStats
    {
        /// <summary>강화가 없는 기본 묶음이다.</summary>
        public static readonly PlayerUpgradeStats None = new PlayerUpgradeStats();

        /// <summary>플레이어 최대 체력이다. 0이면 밸런스 기본값을 쓴다.</summary>
        public double MaxHp { get; }

        /// <summary>플레이어 최대 전력이다. 0이면 밸런스 기본값을 쓴다.</summary>
        public double MaxEnergy { get; }

        /// <summary>초당 전력 재생량이다. 0이면 밸런스 기본값을 쓴다.</summary>
        public double EnergyRegen { get; }

        /// <summary>전투 공통 피해 배율이다.</summary>
        public double DamageMultiplier { get; }

        /// <summary>플레이어 이동 속도 배율이다.</summary>
        public double MoveSpeedMultiplier { get; }

        /// <summary>스크랩 획득 배율이다.</summary>
        public double ScrapGainMultiplier { get; }

        /// <summary>스크랩 줍기 반경 배율이다.</summary>
        public double ScrapPickupRangeMultiplier { get; }

        /// <summary>스크랩 획득 시 체력 회복 비율이다.</summary>
        public double ScrapHealRatio { get; }

        /// <summary>처치 전력 회복에 더하는 고정 보너스다.</summary>
        public double KillEnergyBonus { get; }

        /// <summary>fire·ice·arc 속성 피해에 더하는 비율이다.</summary>
        public double ElementDamageBonus { get; }

        /// <summary>적 상태 이상 지속시간에 더하는 비율이다.</summary>
        public double StatusDurationBonus { get; }

        /// <summary>대시 쿨타임 감소 비율이다.</summary>
        public double DashCooldownReduction { get; }

        /// <summary>대시 무적에 더하는 초다.</summary>
        public double DashInvulnerabilityBonus { get; }

        /// <summary>적 직접 피격을 무시할 확률(0~1)이다.</summary>
        public double EvasionChance { get; }

        /// <summary>근거리 적중 피해에 더하는 비율이다.</summary>
        public double NearDamageBonus { get; }

        /// <summary>원거리 적중 피해에 더하는 비율이다.</summary>
        public double FarDamageBonus { get; }

        /// <summary>최대 전력에서 시전한 주문 피해에 더하는 비율이다.</summary>
        public double FullManaDamageBonus { get; }

        /// <summary>대시 직후 시전한 주문 피해에 더하는 비율이다.</summary>
        public double PostDashDamageBonus { get; }

        /// <summary>정지 중 전력 재생량에 더하는 비율이다.</summary>
        public double StationaryRegenBonus { get; }

        /// <summary>대시를 순간이동으로 바꾸는지 나타낸다.</summary>
        public bool HasDashTeleport { get; }

        /// <summary>대시 중 충돌 시 무적과 전력 회복을 받는지 나타낸다.</summary>
        public bool HasDashCollisionGuard { get; }

        /// <summary>플레이어 주위 지속 장판을 켜는지 나타낸다.</summary>
        public bool HasPlayerAura { get; }

        /// <summary>전격 강화(EMP 부여)를 켜는지 나타낸다.</summary>
        public bool HasArcEnhance { get; }

        /// <summary>냉기 강화(최대 스택 빙결)를 켜는지 나타낸다.</summary>
        public bool HasIceEnhance { get; }

        /// <summary>화염 강화(화상 중첩 폭발)를 켜는지 나타낸다.</summary>
        public bool HasFireEnhance { get; }

        /// <summary>합산한 강화 능력치와 효과 플래그로 묶음을 만든다. 생략한 값은 강화가 없는 상태다.</summary>
        public PlayerUpgradeStats(double maxHp = 0, double maxEnergy = 0, double energyRegen = 0,
            double damageMultiplier = 1, double moveSpeedMultiplier = 1, double scrapGainMultiplier = 1,
            double scrapPickupRangeMultiplier = 1, double scrapHealRatio = 0, double killEnergyBonus = 0,
            double elementDamageBonus = 0, double statusDurationBonus = 0, double dashCooldownReduction = 0,
            double dashInvulnerabilityBonus = 0, double evasionChance = 0, double nearDamageBonus = 0,
            double farDamageBonus = 0, double fullManaDamageBonus = 0, double postDashDamageBonus = 0,
            double stationaryRegenBonus = 0, bool hasDashTeleport = false, bool hasDashCollisionGuard = false,
            bool hasPlayerAura = false, bool hasArcEnhance = false, bool hasIceEnhance = false, bool hasFireEnhance = false)
        {
            MaxHp = maxHp;
            MaxEnergy = maxEnergy;
            EnergyRegen = energyRegen;
            DamageMultiplier = damageMultiplier;
            MoveSpeedMultiplier = moveSpeedMultiplier;
            ScrapGainMultiplier = scrapGainMultiplier;
            ScrapPickupRangeMultiplier = scrapPickupRangeMultiplier;
            ScrapHealRatio = scrapHealRatio;
            KillEnergyBonus = killEnergyBonus;
            ElementDamageBonus = elementDamageBonus;
            StatusDurationBonus = statusDurationBonus;
            DashCooldownReduction = dashCooldownReduction;
            DashInvulnerabilityBonus = dashInvulnerabilityBonus;
            EvasionChance = evasionChance;
            NearDamageBonus = nearDamageBonus;
            FarDamageBonus = farDamageBonus;
            FullManaDamageBonus = fullManaDamageBonus;
            PostDashDamageBonus = postDashDamageBonus;
            StationaryRegenBonus = stationaryRegenBonus;
            HasDashTeleport = hasDashTeleport;
            HasDashCollisionGuard = hasDashCollisionGuard;
            HasPlayerAura = hasPlayerAura;
            HasArcEnhance = hasArcEnhance;
            HasIceEnhance = hasIceEnhance;
            HasFireEnhance = hasFireEnhance;
        }
    }
}
