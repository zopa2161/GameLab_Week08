using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>
    /// 행동·모드 런타임이 쓰는 시뮬레이션 상태 읽기와 판정·기록 창구다. 런타임은 RuneSimulation 대신 이 객체만 받는다.
    /// 전투 판정기·마법 개체 시스템은 생성 순서상 나중에 Connect로 연결한다.
    /// </summary>
    internal sealed class SpellRuntimeContext
    {
        /// <summary>제외 대상이 없는 최근접 적 탐색에 넘기는 빈 목록이다. 유도 모드가 쓴다.</summary>
        internal static readonly Dictionary<int, double> NoExcludedTargets = new Dictionary<int, double>();

        private readonly SimulationWorld _world;
        private readonly SimulationFeedback _feedback;
        private CombatResolver _combat;
        private SpellEntitySystem _entities;

        /// <summary>현재 틱 번호다.</summary>
        internal int Tick => _world.Tick;

        /// <summary>현재 시뮬레이션 시각(초)이다.</summary>
        internal double Time => _world.Time;

        /// <summary>활성 적 목록이다.</summary>
        internal IReadOnlyList<SimulationEnemy> Enemies => _world.Enemies;

        /// <summary>전투 맵이다.</summary>
        internal MissionMap Map => _world.Map;

        /// <summary>플레이어 상태다.</summary>
        internal SimulationPlayer Player => _world.Player;

        /// <summary>전투 규칙 수치다. 행동 실행 동작이 다중 배치 간격 등을 읽는다.</summary>
        internal BalanceData Balance => _world.Balance;

        /// <summary>플레이어 충돌 반경이다.</summary>
        internal double PlayerRadius => _world.Sector.PlayerRadius;

        /// <summary>월드와 빔 표시 기록을 남길 표시 버퍼로 만든다.</summary>
        internal SpellRuntimeContext(SimulationWorld world, SimulationFeedback feedback)
        {
            _world = world;
            _feedback = feedback;
        }

        /// <summary>판정 위임 대상인 전투 판정기와 마법 개체 시스템을 연결한다. 시뮬레이션 생성자에서 한 번 호출한다.</summary>
        internal void Connect(CombatResolver combat, SpellEntitySystem entities)
        {
            _combat = combat;
            _entities = entities;
        }

        /// <summary>폭발·잔류가 함께 사용하는 플레이어 중심 최대 배치 반경을 반환한다.</summary>
        internal double GetAreaPlacementRange() => _world.Balance.Sim.AreaPlacementRange;

        /// <summary>범위 마법의 대상별 주기 피해와 OnHit을 처리한다.</summary>
        internal void HitArea(SimulationSpellEntity entity, bool isBurst) => _combat.HitArea(entity, isBurst);

        /// <summary>스펠 피해·상태·전격 연쇄와 밀어내기를 적용한다.</summary>
        internal void Hit(SimulationSpellEntity entity, SimulationEnemy enemy, double damage, bool isDot, SimVector pushDirection)
            => _combat.Hit(entity, enemy, damage, isDot, pushDirection);

        /// <summary>개체의 OnHit 이벤트를 지정 위치·대상으로 실행한다.</summary>
        internal void RaiseHitEvent(SimulationSpellEntity entity, SimVector position, SimulationEnemy target)
            => _combat.RaiseHitEvent(entity, position, target);

        /// <summary>발사체의 보호막 피해와 명중·소멸 이벤트를 처리한다.</summary>
        internal void RaiseShieldHitEvent(SimulationSpellEntity entity, SimVector contact, SimulationEnemy shieldOwner)
            => _combat.RaiseShieldHitEvent(entity, contact, shieldOwner);

        /// <summary>첫 판정 빔의 경로를 표시 전용으로 복사하고 최근 기록 수를 제한한다.</summary>
        internal void RecordBeamFlash(SimulationSpellEntity entity, IReadOnlyList<SimVector> path)
            => _feedback.RecordBeamFlash(entity, path);

        /// <summary>원형 개체의 직선 이동에서 첫 벽 접촉 비율을 반환한다.</summary>
        internal double FirstWallFraction(SimVector start, SimVector end, double radius) => _entities.FirstWallFraction(start, end, radius);

        /// <summary>이미 적중한 적을 제외하고 탐지 반경 내 가장 가까운 생존 적을 반환한다.</summary>
        internal SimulationEnemy NearestEnemy(SimVector position, double range, Dictionary<int, double> excluded)
            => _entities.NearestEnemy(position, range, excluded);
    }
}
