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

        /// <summary>미션 스테이지 번호다.</summary>
        public int StageNumber { get; private set; }

        /// <summary>강화 트리 능력치와 효과 플래그다. 최대 체력·전력·재생량 0은 밸런스 기본값을 뜻한다.</summary>
        public PlayerUpgradeStats UpgradeStats { get; private set; }

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
        /// 전역 게임 데이터를 한 번 읽고 시드, 스테이지, 강화 능력치 묶음과 카탈로그로 설정을 만든다.
        /// upgradeStats를 생략하면 강화가 없는 기본 묶음을 쓴다.
        /// </summary>
        public static SimulationSetup FromGameData(int seed = 1, bool isMission = false, int stageNumber = 1,
            PlayerUpgradeStats upgradeStats = null, bool isProgressionMission = false,
            int progressionStage = 0, int progressionRound = 0, double progressionHp = 0)
        {
            GameData.Load();
            if (isProgressionMission != isMission) throw new System.ArgumentException("미션은 연속 스테이지로만 실행하며 연속 스테이지는 미션에서만 사용할 수 있습니다.", nameof(isProgressionMission));
            if (!isProgressionMission && (progressionStage != 0 || progressionRound != 0 || progressionHp != 0))
                throw new System.ArgumentException("연속 진행 시작 값은 연속 스테이지에서만 사용할 수 있습니다.", nameof(progressionStage));
            return new SimulationSetup
            {
                Seed = seed, IsMission = isMission, IsProgressionMission = isProgressionMission, StageNumber = stageNumber,
                UpgradeStats = upgradeStats ?? PlayerUpgradeStats.None,
                ProgressionStage = progressionStage, ProgressionRound = progressionRound, ProgressionHp = progressionHp,
                Balance = GameData.Balance, Enemies = GameData.Enemies, Sector = GameData.Sector, Bosses = GameData.Bosses, Formations = GameData.Formations,
                Drops = GameData.Drops, ModifierGrades = GameData.ModifierGrades, Stages = GameData.Stages, Runes = GameData.Runes
            };
        }
    }
}
