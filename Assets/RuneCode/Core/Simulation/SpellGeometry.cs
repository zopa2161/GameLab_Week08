using System;

namespace RuneCode
{
    /// <summary>
    /// 마법 개체의 모양(원·사각형·부채꼴)과 대상 원의 겹침, 이동 경로 접촉 비율, 접촉 위치를 계산하는 순수 기하 함수 모음이다.
    /// 맵·적 목록에 의존하지 않으며 행동 실행 클래스(SpellBehaviorRuntime)와 RuneSimulation이 함께 쓴다.
    /// </summary>
    internal static class SpellGeometry
    {
        private const double DEGREES_TO_RADIANS = Math.PI / 180;
        private const double EPSILON = 0.000001;
        private const int SHAPE_CONTACT_REFINEMENT_STEPS = 16;

        /// <summary>폭발의 확장 또는 진화 수축 구간에서 원형 띠와 대상 원이 처음 접촉하는 틱 내 비율을 반환한다.</summary>
        internal static double BurstContactFraction(SimulationSpellEntity entity, SimVector start,
            SimVector target, double targetRadius)
        {
            double thickness = entity.Radius + targetRadius;
            double radius = entity.ExplosionRadius;
            SimVector center = entity.HasBehaviorEvolution ? entity.SpawnPosition : start - entity.PreviousDirection * radius;
            double distance = (target - center).Length;
            double growth = entity.Stats.Speed * RuneSimulation.STEP_SECONDS;
            if (entity.HasBehaviorEvolution && entity.IsReturning)
            {
                double nextRadius = entity.Stats.Speed * entity.GetExplosionTravelTime(entity.Age + RuneSimulation.STEP_SECONDS);
                if (distance - thickness > radius + EPSILON || distance + thickness < nextRadius - EPSILON)
                    return double.PositiveInfinity;
                return growth <= EPSILON ? 0 : Math.Max(0, Math.Min(1, (radius - distance - thickness) / growth));
            }
            if (distance + thickness < radius - EPSILON || distance - thickness > radius + growth + EPSILON)
                return double.PositiveInfinity;
            if (growth <= EPSILON) return 0;
            return Math.Max(0, Math.Min(1, (distance - thickness - radius) / growth));
        }

        /// <summary>발사체 모양의 경로가 대상 원에 처음 닿는 비율을 반환하며 미충돌이면 양의 무한대를 반환한다.</summary>

        internal static double SpellContactFraction(SimulationSpellEntity entity, SimVector start, SimVector end, SimVector direction, SimVector target, double targetRadius)

        {

            if (!entity.IsBox && !entity.IsCone) return CircleContactFraction(start, end, target, entity.Radius + targetRadius);

            if (!IntersectsSpellSweep(entity, start, end, direction, target, targetRadius)) return double.PositiveInfinity;

            if (IntersectsSpellSweep(entity, start, start, direction, target, targetRadius)) return 0;

            double low = 0, high = 1;

            for (int i = 0; i < SHAPE_CONTACT_REFINEMENT_STEPS; i++)

            {

                double middle = (low + high) * 0.5;

                if (IntersectsSpellSweep(entity, start, start + (end - start) * middle, direction, target, targetRadius)) high = middle;

                else low = middle;

            }

            return high;

        }



        /// <summary>사각·부채꼴 발사체의 기존 이동 경로 판정으로 대상 원과의 충돌 여부를 반환한다.</summary>

        internal static bool IntersectsSpellSweep(SimulationSpellEntity entity, SimVector start, SimVector end, SimVector direction, SimVector target, double radius)

            => entity.IsCone ? IntersectsConeSweep(start, end, direction, entity.Radius, entity.ConeAngle, target, radius)

                : IntersectsBoxSweep(start, end, direction, entity.Stats.BoxWidth * 0.5, entity.Stats.BoxLength * 0.5, target, radius);



        /// <summary>선분이 대상 원에 처음 진입하는 비율을 해석적으로 계산하며 미충돌이면 양의 무한대를 반환한다.</summary>

        internal static double CircleContactFraction(SimVector start, SimVector end, SimVector target, double radius)

        {

            SimVector relative = start - target, delta = end - start;

            double c = relative.LengthSquared - radius * radius;

            if (c <= 0) return 0;

            double a = delta.LengthSquared;

            if (a <= EPSILON) return double.PositiveInfinity;

            double b = SimVector.Dot(relative, delta), discriminant = b * b - a * c;

            if (discriminant < 0) return double.PositiveInfinity;

            double fraction = (-b - Math.Sqrt(discriminant)) / a;

            return fraction >= 0 && fraction <= 1 ? fraction : double.PositiveInfinity;

        }



        /// <summary>

        /// 진행 방향으로 펼친 부채꼴 발사체(꼭짓점은 중심에서 반경만큼 뒤, 부채꼴 반경은 지름)가 이번 틱 이동 중 대상 원과 겹치는지 반환한다.

        /// 이동 거리를 반경 간격으로 나눈 위치마다 판정해 빠른 발사체가 대상을 건너뛰지 않게 한다.

        /// </summary>

        internal static bool IntersectsConeSweep(SimVector start, SimVector end, SimVector direction, double radius, double coneAngle,

            SimVector target, double targetRadius)

        {

            int samples = Math.Max(1, (int)Math.Ceiling((end - start).Length / Math.Max(radius, 0.000001)));

            for (int i = 1; i <= samples; i++)

            {

                SimVector center = start + (end - start) * (i / (double)samples);

                if (IntersectsSector(SimulationSpellEntity.GetConeApex(center, direction, radius), direction, radius * 2, coneAngle, target, targetRadius))

                    return true;

            }

            return false;

        }



        /// <summary>꼭짓점에서 방향으로 반경·전체 각도(도)만큼 펼친 부채꼴과 대상 원의 겹침 여부를 반환한다. 대상 반경만큼 거리와 각도에 여유를 준다.</summary>

        internal static bool IntersectsSector(SimVector apex, SimVector direction, double reach, double coneAngle, SimVector target, double targetRadius)

        {

            SimVector relative = target - apex;

            double distance = relative.Length;

            if (distance > reach + targetRadius) return false;

            if (distance <= targetRadius) return true;

            double cosine = Math.Max(-1, Math.Min(1, SimVector.Dot(relative / distance, direction.Normalized())));

            double tolerance = Math.Asin(Math.Min(1, targetRadius / distance));

            return Math.Acos(cosine) <= coneAngle * 0.5 * DEGREES_TO_RADIANS + tolerance;

        }



        /// <summary>진행 방향으로 길이, 수직으로 폭을 가진 이동 사각형이 이번 틱 이동 중 대상 원과 겹치는지 반환한다.</summary>

        internal static bool IntersectsBoxSweep(SimVector start, SimVector end, SimVector direction,

            double halfWidth, double halfLength, SimVector target, double targetRadius)

        {

            SimVector movement = end - start;

            double length = movement.Length;

            SimVector forward = length > 0.000001 ? movement / length : direction.Normalized();

            SimVector side = new SimVector(-forward.Y, forward.X);

            SimVector relative = target - start;

            double moveAlong = SimVector.Dot(movement, forward);

            double moveAcross = SimVector.Dot(movement, side);

            double along = SimVector.Dot(relative, forward);

            double across = SimVector.Dot(relative, side);

            double alongExtent = halfLength + targetRadius;

            double acrossExtent = halfWidth + targetRadius;

            return along >= Math.Min(0, moveAlong) - alongExtent && along <= Math.Max(0, moveAlong) + alongExtent

                && across >= Math.Min(0, moveAcross) - acrossExtent && across <= Math.Max(0, moveAcross) + acrossExtent;

        }



        /// <summary>

        /// 진행 방향으로 현재 길이, 수직으로 폭을 가진 Beam 범위(BoxCenter 중심)와 대상 원의 겹침 여부를 반환한다.

        /// Burst·Persist는 뒤쪽 면이 개체 위치에 있고, 공전은 개체 위치가 중심이다.

        /// </summary>

        private static bool IntersectsAreaBox(SimulationSpellEntity entity, SimVector target, double targetRadius)

        {

            SimVector forward = entity.Direction.Normalized();

            SimVector side = new SimVector(-forward.Y, forward.X);

            SimVector relative = target - entity.BoxCenter;

            return Math.Abs(SimVector.Dot(relative, forward)) <= entity.BoxLength * 0.5 + targetRadius

                && Math.Abs(SimVector.Dot(relative, side)) <= entity.BoxWidth * 0.5 + targetRadius;

        }



        /// <summary>범위 마법(원·사각형·부채꼴)이 대상 원과 겹치는지 반환한다.</summary>

        internal static bool IsInsideArea(SimulationSpellEntity entity, SimVector target, double targetRadius)

        {

            if (entity.IsBox) return IntersectsAreaBox(entity, target, targetRadius);

            if (entity.IsCone) return IntersectsCone(entity, target, targetRadius);

            return SimVector.Distance(entity.Position, target) <= entity.Radius + targetRadius;

        }



        /// <summary>

        /// 꼭짓점(개체 위치)에서 진행 방향으로 반경·전체 각도만큼 펼친 부채꼴과 대상 원의 겹침 여부를 반환한다.

        /// 대상 반경만큼 거리와 각도에 여유를 준다.

        /// </summary>

        internal static bool IntersectsCone(SimulationSpellEntity entity, SimVector target, double targetRadius)

            => IntersectsSector(entity.Position, entity.Direction, entity.Radius, entity.ConeAngle, target, targetRadius);



        /// <summary>

        /// 범위 중심에서 대상 쪽으로 현재 반경만큼(대상이 더 가까우면 대상 위치까지) 간 접촉 위치를 반환한다.

        /// Beam 범위는 현재 길이의 중심선 위에서 대상에 가장 가까운 위치다.

        /// </summary>

        internal static SimVector GetContactPoint(SimulationSpellEntity entity, SimVector target)

        {

            if (entity.IsBox)

            {

                SimVector forward = entity.Direction.Normalized();

                SimVector back = entity.BoxCenter - forward * (entity.BoxLength * 0.5);

                return back + forward * Math.Max(0, Math.Min(entity.BoxLength, SimVector.Dot(target - back, forward)));

            }

            SimVector offset = target - entity.Position;

            double distance = offset.Length;

            return distance <= 0.000001 ? entity.Position : entity.Position + offset / distance * Math.Min(entity.Radius, distance);

        }



        /// <summary>점과 선분 사이의 최단 거리를 반환하여 빠른 투사체의 연속 충돌을 판정한다.</summary>

        internal static double SegmentDistance(SimVector start, SimVector end, SimVector point)

        { SimVector segment = end - start; double progress = segment.LengthSquared < 0.000001 ? 0 : Math.Max(0, Math.Min(1, SimVector.Dot(point - start, segment) / segment.LengthSquared)); return SimVector.Distance(start + segment * progress, point); }
    }
}
