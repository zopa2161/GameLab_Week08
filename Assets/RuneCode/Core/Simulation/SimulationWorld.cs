using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>시뮬레이션 전체가 공유하는 카탈로그 참조, 전투 설정과 런타임 상태를 소유한다.</summary>
    internal sealed class SimulationWorld
    {
        private int _nextEntityId;

        /// <summary>전투 규칙 수치다. 행동 실행·스폰·피해 판정 전체가 참조한다.</summary>
        internal BalanceData Balance { get; }

        /// <summary>적 정의 카탈로그다. 스폰과 판정이 적 정의를 조회한다.</summary>
        internal EnemyCatalog EnemyCatalog { get; }

        /// <summary>섹터 지형·타일 설정이다. 맵 생성과 충돌 반경 계산에 쓴다.</summary>
        internal SectorDefinition Sector { get; }

        /// <summary>보스 패턴 카탈로그다. 스폰 시 적 ID로 보스 정의를 조회한다.</summary>
        internal BossCatalog Bosses { get; }

        /// <summary>스폰 진형 카탈로그다. 진형 스폰이 모양 정의를 조회한다.</summary>
        internal FormationCatalog Formations { get; }

        /// <summary>드롭 테이블 카탈로그다. 처치 드롭과 클리어 보상이 조회한다.</summary>
        internal DropCatalog Drops { get; }

        /// <summary>엘리트 Modifier 등급표 조회 창구다.</summary>
        internal IEliteModifierDropSource EliteDrops { get; }

        /// <summary>스테이지 편성 카탈로그다. 미션 스폰과 배율 조회에 쓴다.</summary>
        internal StageCatalog Stages { get; }

        /// <summary>캐리어 자식 생성 설정이다. 캐리어 적의 스폰 규칙이 참조한다.</summary>
        internal CarrierDefinition Carrier { get; }

        /// <summary>미션 모드 여부다. 시험 도크와 미션의 규칙 분기에 쓴다.</summary>
        internal bool IsMission { get; }

        /// <summary>현재 미션 스테이지 번호다. 1 미만 값은 1로 보정한다.</summary>
        internal int StageNumber { get; }

        /// <summary>플레이어 최대 체력이다. maxHp가 0이면 밸런스 기본값을 쓴다.</summary>
        internal double MaxHp { get; }

        /// <summary>플레이어 최대 전력이다. maxEnergy가 0이면 밸런스 기본값을 쓴다.</summary>
        internal double MaxEnergy { get; }

        /// <summary>초당 전력 재생량이다. energyRegen이 0이면 밸런스 기본값을 쓴다.</summary>
        internal double EnergyRegen { get; }

        /// <summary>전투 공통 피해 배율이다.</summary>
        internal double DamageMultiplier { get; }

        /// <summary>플레이어 이동 속도 배율이다.</summary>
        internal double MoveSpeedMultiplier { get; }

        /// <summary>시뮬레이션 공유 난수 발생기다. 모든 난수 호출이 같은 인스턴스를 쓴다.</summary>
        internal SimRandom Random { get; }

        /// <summary>적응 학습망이다. 피해 감쇠와 학습 상태를 관리한다.</summary>
        internal AdaptationNet Adaptation { get; }

        /// <summary>활성 적 목록이다. 스폰·판정·처치 정리가 같은 인스턴스를 공유한다.</summary>
        internal List<SimulationEnemy> Enemies { get; } = new List<SimulationEnemy>();

        /// <summary>활성 마법 개체 목록이다. 진행·판정·취소가 같은 인스턴스를 공유한다.</summary>
        internal List<SimulationSpellEntity> SpellEntities { get; } = new List<SimulationSpellEntity>();

        /// <summary>활성 적 탄환 목록이다. 발사·진행·피격 판정이 같은 인스턴스를 공유한다.</summary>
        internal List<SimulationProjectile> EnemyProjectiles { get; } = new List<SimulationProjectile>();

        /// <summary>플레이어 상태다. 도크·미션 시작 시 교체된다.</summary>
        internal SimulationPlayer Player { get; set; }

        /// <summary>전투 맵이다. 도크·미션 시작 시 교체된다.</summary>
        internal MissionMap Map { get; set; }

        /// <summary>현재 전투 진행 단계다.</summary>
        internal MissionStage Stage { get; set; }

        /// <summary>현재 스테이지 정의다. 미션 시작 시 설정된다.</summary>
        internal StageDefinition StageDefinition { get; set; }

        /// <summary>시뮬레이션 틱 카운터다. Step마다 증가한다.</summary>
        internal int Tick { get; set; }

        /// <summary>디버그 무적 활성 여부다.</summary>
        internal bool DebugInvulnerable { get; set; }

        /// <summary>마지막 이동 입력 방향이다.</summary>
        internal SimVector MoveDirection { get; set; }

        /// <summary>이동 입력으로 방향을 확정했는지 나타낸다.</summary>
        internal bool HasMoveDirection { get; set; }

        /// <summary>다음에 발급할 개체 ID다. 상태 해시 기록에 쓴다.</summary>
        internal int NextEntityId => _nextEntityId;

        /// <summary>
        /// 카탈로그 참조와 전투 설정, 공유 난수·적응망으로 월드를 만든다. 체력·전력·재생량이 0이면 밸런스 기본값을 쓰고 스테이지 번호는 1 미만을 허용하지 않는다.
        /// </summary>
        internal SimulationWorld(BalanceData balance, EnemyCatalog enemyCatalog, SectorDefinition sector, BossCatalog bosses,
            FormationCatalog formations, DropCatalog drops, IEliteModifierDropSource eliteDrops, StageCatalog stages,
            CarrierDefinition carrier, bool isMission, double maxHp, double maxEnergy, int stageNumber, double energyRegen,
            double damageMultiplier, double moveSpeedMultiplier, SimRandom random, AdaptationNet adaptation)
        {
            Balance = balance;
            EnemyCatalog = enemyCatalog;
            Sector = sector;
            Bosses = bosses;
            Formations = formations;
            Drops = drops;
            EliteDrops = eliteDrops;
            Stages = stages;
            Carrier = carrier;
            IsMission = isMission;
            MaxHp = maxHp > 0 ? maxHp : Balance.Player.Hp;
            MaxEnergy = maxEnergy > 0 ? maxEnergy : Balance.Player.Energy;
            StageNumber = Math.Max(1, stageNumber);
            EnergyRegen = energyRegen > 0 ? energyRegen : Balance.Player.EnergyRegen;
            DamageMultiplier = damageMultiplier;
            MoveSpeedMultiplier = moveSpeedMultiplier;
            Random = random;
            Adaptation = adaptation;
        }

        /// <summary>다음 개체 ID를 하나 발급하고 카운터를 증가시켜 반환한다.</summary>
        internal int AllocateEntityId() => ++_nextEntityId;

        /// <summary>개체 ID 카운터를 0으로 되돌린다. 도크 초기화에 쓴다.</summary>
        internal void ResetEntityIds() { _nextEntityId = 0; }
    }
}
