using System;

namespace RuneCode
{
    /// <summary>발사의 이동·공유 관통·이벤트를 재사용하며 속이 빈 호 전체의 벽·보호막·적 충돌을 판정한다.</summary>
    internal sealed class RadiateBehaviorRuntime : LaunchBehaviorRuntime
    {
        private const double WALL_SWEEP_PRECISION = 2;
        private const int WALL_REFINEMENT_STEPS = 12;

        public override string Kind => RadiateBehaviorRules.RADIATE_KIND;

        /// <summary>방사는 생성 지점을 호 정면의 중앙점으로 사용하므로 별도 사각형 위치 보정을 하지 않는다.</summary>
        public override void PlaceSpawn(RuneSimulation simulation, SpellAction action, SpellStats stats, int index,
            bool isFromEvent, SimulationPlayer caster, ref SimVector position, ref SimVector direction)
        {
        }

        /// <summary>반경 증가와 방향 회전을 포함한 호의 이동에서 대상 원과 최초 접촉하는 비율을 반환한다.</summary>
        protected override double GetContactFraction(SimulationSpellEntity entity, SimVector previous,
            SimVector next, SimVector direction, SimVector target, double targetRadius)
            => SpellGeometry.ArcContactFraction(entity, previous, next, target, targetRadius);

        /// <summary>적의 중심 대신 접촉 시점의 호 중심선 위 실제 위치를 적중 사건 위치로 사용한다.</summary>
        protected override SimVector GetContactPoint(SimulationSpellEntity entity, SimVector previous,
            SimVector next, SimVector direction, double fraction, SimVector target)
            => SpellGeometry.ClosestArcPoint(entity, previous, next, fraction, target);

        /// <summary>정면 중앙점이 보호막 안에서 생성된 경우를 제외하고 호 전체의 보호막 접촉을 검사한다.</summary>
        protected override bool CanHitShield(SimulationSpellEntity entity, SimVector previous, SimVector next, SimulationEnemy shield)
            => (previous - shield.Position).Length >= shield.Definition.ShieldRadius - RuneSimulation.COLLISION_EPSILON;

        /// <summary>호의 실제 장애물 접촉 위치를 반환해 후속 행동이 투사체 정면이 아닌 적중한 곳에서 생성되게 한다.</summary>
        protected override SimVector GetObstacleContactPoint(RuneSimulation simulation, SimulationSpellEntity entity,
            SimVector previous, SimVector next, SimVector direction, double fraction, SimulationEnemy shield)
        {
            if (shield != null) return SpellGeometry.ClosestArcPoint(entity, previous, next, fraction, shield.Position);
            TryGetWallContact(simulation, entity, previous, next, fraction, WALL_SWEEP_PRECISION * 0.5, out SimVector contact);
            return contact;
        }

        /// <summary>호 전체의 이동·회전·크기 증가를 분할해 첫 벽 접촉 시점을 보수적으로 찾는다.</summary>
        protected override double GetWallContactFraction(RuneSimulation simulation, SimulationSpellEntity entity,
            SimVector previous, SimVector next, SimVector direction)
        {
            double radius = entity.Stats.Speed * (entity.Age + RuneSimulation.STEP_SECONDS);
            double bound = 2 * radius * Math.Sin(entity.ArcAngle * Math.PI / 720) + entity.Radius;
            if (double.IsPositiveInfinity(simulation.FirstWallFraction(previous, next, bound))) return double.PositiveInfinity;
            double motion = SpellGeometry.ArcMotionBound(entity, previous, next);
            int samples = Math.Max(1, (int)Math.Ceiling(motion / WALL_SWEEP_PRECISION));
            double padding = motion / samples * 0.5;
            double low = 0;
            if (TryGetWallContact(simulation, entity, previous, next, 0, padding, out _)) return 0;
            for (int index = 1; index <= samples; index++)
            {
                double high = index / (double)samples;
                if (!TryGetWallContact(simulation, entity, previous, next, high, padding, out _)) { low = high; continue; }
                for (int iteration = 0; iteration < WALL_REFINEMENT_STEPS; iteration++)
                {
                    double middle = (low + high) * 0.5;
                    if (TryGetWallContact(simulation, entity, previous, next, middle, padding, out _)) high = middle;
                    else low = middle;
                }
                return high;
            }
            return double.PositiveInfinity;
        }

        /// <summary>지정 시점의 호를 짧은 원형 두께 선분으로 나누어 벽과 접촉하는지 반환한다.</summary>
        private static bool TryGetWallContact(RuneSimulation simulation, SimulationSpellEntity entity,
            SimVector previous, SimVector next, double fraction, double padding, out SimVector contact)
        {
            double radius = entity.Stats.Speed * (entity.Age + RuneSimulation.STEP_SECONDS * fraction);
            int segments = Math.Max(1, (int)Math.Ceiling(radius * entity.ArcAngle * Math.PI / 180
                / Math.Max(1, Math.Min(entity.Radius, simulation.Map.TileSize * 0.25))));
            SimVector start = SpellGeometry.ArcPoint(entity, previous, next, fraction, 0);
            contact = start;
            double halfWidth = entity.Radius + padding
                + radius * (1 - Math.Cos(entity.ArcAngle * Math.PI / 180 / segments * 0.5));
            if (!simulation.Map.CanOccupy(start, halfWidth)) return true;
            for (int index = 1; index <= segments; index++)
            {
                SimVector end = SpellGeometry.ArcPoint(entity, previous, next, fraction, index / (double)segments);
                double wall = simulation.FirstWallFraction(start, end, halfWidth);
                if (!double.IsPositiveInfinity(wall)) { contact = start + (end - start) * wall; return true; }
                start = end;
            }
            return false;
        }
    }
}
