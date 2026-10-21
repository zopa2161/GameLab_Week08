namespace RuneCode
{
    /// <summary>
    /// 시뮬레이션 생성 설정(시드·모드·스테이지, 트리 능력치, 스크랩·처치 보너스)과 시뮬레이션이 읽는 카탈로그 참조를 묶는다.
    /// FromGameData로 전역 게임 데이터를 읽어 만든다.
    /// </summary>
    public sealed class SimulationSetup
    {
        /// <summary>공유 난수 시드다.</summary>
        public int Seed { get; private set; }

        /// <summary>미션 전투이면 true, 시험 도크이면 false다.</summary>
        public bool IsMission { get; private set; }

        public bool IsProgressionMission { get; private set; }

        /// <summary>플레이어 최대 체력이다. 0이면 밸런스 기본값을 쓴다.</summary>
        public double MaxHp { get; private set; }

        /// <summary>플레이어 최대 전력이다. 0이면 밸런스 기본값을 쓴다.</summary>
        public double MaxEnergy { get; private set; }

        /// <summary>미션 스테이지 번호다.</summary>
        public int StageNumber { get; private set; }

        /// <summary>초당 전력 재생량이다. 0이면 밸런스 기본값을 쓴다.</summary>
        public double EnergyRegen { get; private set; }

        /// <summary>전투 공통 피해 배율이다.</summary>
        public double DamageMultiplier { get; private set; }

        /// <summary>플레이어 이동 속도 배율이다.</summary>
        public double MoveSpeedMultiplier { get; private set; }

        /// <summary>스크랩 획득 배율이다.</summary>
        public double ScrapGainMultiplier { get; private set; }

        /// <summary>스크랩 줍기 반경 배율이다.</summary>
        public double ScrapPickupRangeMultiplier { get; private set; }

        /// <summary>처치 전력 회복에 더하는 고정 보너스다.</summary>
        public double KillEnergyBonus { get; private set; }

        /// <summary>스크랩 획득 시 체력 회복 비율이다.</summary>
        public double ScrapHealRatio { get; private set; }

        /// <summary>이어할 연속 미션 스테이지 번호다. 0이면 새 연속 미션이다.</summary>
        public int ProgressionStage { get; private set; }

        /// <summary>이어할 연속 미션 라운드 번호다. 0이면 새 연속 미션이다.</summary>
        public int ProgressionRound { get; private set; }

        /// <summary>이어하기 시작 체력이다. 0 이하이면 최대 체력으로 시작한다.</summary>
        public double ProgressionHp { get; private set; }

        /// <summary>전투 규칙 수치다.</summary>
        public BalanceData Balance { get; private set; }

        /// <summary>적 정의 카탈로그다.</summary>
        public EnemyCatalog Enemies { get; private set; }

        /// <summary>섹터 지형·타일 설정이다.</summary>
        public SectorDefinition Sector { get; private set; }

        /// <summary>보스 패턴 카탈로그다.</summary>
        public BossCatalog Bosses { get; private set; }

        /// <summary>스폰 진형 카탈로그다.</summary>
        public FormationCatalog Formations { get; private set; }

        /// <summary>드롭 테이블 카탈로그다.</summary>
        public DropCatalog Drops { get; private set; }

        /// <summary>엘리트 Modifier 등급표다.</summary>
        public ModifierGradeTable ModifierGrades { get; private set; }

        /// <summary>스테이지 편성 카탈로그다.</summary>
        public StageCatalog Stages { get; private set; }

        /// <summary>룬 카탈로그다. 기본 발사 사거리, 넉백 기준 피해, 전격 연쇄 수치를 조회한다.</summary>
        public RuneCatalog Runes { get; private set; }

        /// <summary>FromGameData에서만 만든다.</summary>
        private SimulationSetup() { }

        /// <summary>
        /// 전역 게임 데이터를 한 번 읽고 시드, 스테이지와 트리 능력치·처치 전력 보너스·스크랩 회복 비율, 카탈로그로 설정을 만든다.
        /// 매개변수와 기본값은 기존 시뮬레이션 생성자와 같다. 생략한 배율은 1, 보너스는 0을 사용한다.
        /// </summary>
        public static SimulationSetup FromGameData(int seed = 1, bool isMission = false, double maxHp = 0, double maxEnergy = 0, int stageNumber = 1, double energyRegen = 0,
            double damageMultiplier = 1, double moveSpeedMultiplier = 1, double scrapGainMultiplier = 1,
            double scrapPickupRangeMultiplier = 1, double killEnergyBonus = 0, double scrapHealRatio = 0, bool isProgressionMission = false,
            int progressionStage = 0, int progressionRound = 0, double progressionHp = 0)
        {
            GameData.Load();
            if (isProgressionMission && !isMission) throw new System.ArgumentException("연속 스테이지는 미션에서만 사용할 수 있습니다.", nameof(isProgressionMission));
            if (!isProgressionMission && (progressionStage != 0 || progressionRound != 0 || progressionHp != 0))
                throw new System.ArgumentException("연속 진행 시작 값은 연속 스테이지에서만 사용할 수 있습니다.", nameof(progressionStage));
            return new SimulationSetup
            {
                Seed = seed, IsMission = isMission, IsProgressionMission = isProgressionMission, MaxHp = maxHp, MaxEnergy = maxEnergy, StageNumber = stageNumber, EnergyRegen = energyRegen,
                DamageMultiplier = damageMultiplier, MoveSpeedMultiplier = moveSpeedMultiplier, ScrapGainMultiplier = scrapGainMultiplier,
                ScrapPickupRangeMultiplier = scrapPickupRangeMultiplier, KillEnergyBonus = killEnergyBonus, ScrapHealRatio = scrapHealRatio,
                ProgressionStage = progressionStage, ProgressionRound = progressionRound, ProgressionHp = progressionHp,
                Balance = GameData.Balance, Enemies = GameData.Enemies, Sector = GameData.Sector, Bosses = GameData.Bosses, Formations = GameData.Formations,
                Drops = GameData.Drops, ModifierGrades = GameData.ModifierGrades, Stages = GameData.Stages, Runes = GameData.Runes
            };
        }
    }
}
