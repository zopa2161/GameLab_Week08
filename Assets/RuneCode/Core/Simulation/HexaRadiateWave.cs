using System;
using System.Text;

namespace RuneCode
{
    /// <summary>Hexa 방사 패턴의 90° 부채꼴 확장 파동 상태다. 통과 판정과 표시가 같은 구역 기하를 공유한다.</summary>
    public sealed class HexaRadiateWave
    {
        private const double DIRECTION_EPSILON = 0.000001;

        private readonly SimVector _center;
        private readonly SimVector _direction;
        private readonly double _startedAt;
        private readonly double _startRadius;
        private readonly double _maxRadius;
        private double _age;
        private bool _hasHit;
        public SimVector Center => _center;
        public SimVector Direction => _direction;
        public double StartRadius => _startRadius;
        public double MaxRadius => _maxRadius;
        public double Age => _age;

        /// <summary>Hexa 중심·구역 중심 방향·발사 시각·시작 반경과 소멸 반경으로 파동 상태를 만든다.</summary>
        internal HexaRadiateWave(SimVector center, SimVector direction, double time, double startRadius, double maxRadius)
        { _center = center; _direction = direction; _startedAt = time; _startRadius = startRadius; _maxRadius = maxRadius; }

        /// <summary>현재 경과로 시작 반경에서 RADIATE_SPEED만큼 퍼진 반경을 반환한다.</summary>
        public double GetRadius() => _startRadius + HexaDefinition.RADIATE_SPEED * _age;

        /// <summary>
        /// 이전·현재 반경 사이의 띠와 구역 중심 방향 ±45° 각도로 플레이어 통과를 판정해 파동당 한 번 피해를 주고,
        /// 반경이 구역의 맵 경계상 가장 먼 거리에 도달하면 소멸 여부로 true를 반환한다.
        /// </summary>
        internal bool Advance(SimulationWorld world, double damageMultiplier, Action<double, string> hurt)
        {
            double previousRadius = GetRadius();
            _age = Math.Max(0, world.Time - _startedAt);
            double currentRadius = GetRadius();
            SimVector offset = world.Player.Position - _center;
            double distance = offset.Length;
            double thickness = world.Sector.PlayerRadius + HexaDefinition.RADIATE_WIDTH * 0.5;
            if (!_hasHit && distance + thickness >= previousRadius && distance - thickness <= currentRadius
                && IsWithinSector(offset))
            { _hasHit = true; hurt(HexaDefinition.RADIATE_DAMAGE * damageMultiplier, "radiate"); }
            return currentRadius + RuneSimulation.COLLISION_EPSILON >= _maxRadius;
        }

        /// <summary>파동 중심·방향·시작 시각·경과·반경 경계와 피격 여부를 결정성 버퍼에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        { state.Append(FormattableString.Invariant($"|radiate:{_center.X:R}:{_center.Y:R}:{_direction.X:R}:{_direction.Y:R}:{_startedAt:R}:{_age:R}:{_startRadius:R}:{_maxRadius:R}:{_hasHit}")); }

        /// <summary>구역 순서(0~3)에 해당하는 중심 방향 +X, +Y, -X, -Y를 반환한다.</summary>
        internal static SimVector GetSectorDirection(int index)
        {
            switch (index % HexaDefinition.RADIATE_SECTOR_COUNT)
            {
                case 0: return new SimVector(1, 0);
                case 1: return new SimVector(0, 1);
                case 2: return new SimVector(-1, 0);
                default: return new SimVector(0, -1);
            }
        }

        /// <summary>구역 경계 두 직선이 맵 사각형(0..Width, 0..Height)과 만나는 점과 그 사이 맵 모서리를 중심부터 경계 순서로 연결한 다각형 꼭짓점을 반환한다. 판정과 경고 표시가 같은 기하를 쓴다.</summary>
        public static SimVector[] GetSectorPolygon(SimVector center, SimVector direction, double mapWidth, double mapHeight)
        {
            double halfRadians = HexaDefinition.RADIATE_SECTOR_HALF_DEGREES * RuneSimulation.DEGREES_TO_RADIANS;
            SimVector firstPoint = GetFarthestBoundaryHit(center, direction.Rotated(halfRadians), mapWidth, mapHeight) ?? center;
            SimVector secondPoint = GetFarthestBoundaryHit(center, direction.Rotated(-halfRadians), mapWidth, mapHeight) ?? center;
            SimVector[] corners = new SimVector[4];
            double[] angles = new double[4];
            int count = 0;
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                {
                    SimVector corner = new SimVector(x * mapWidth, y * mapHeight);
                    double angle = GetSectorAngleOffset(center, direction, corner);
                    if (angle < -halfRadians - DIRECTION_EPSILON || angle > halfRadians + DIRECTION_EPSILON) continue;
                    InsertCorner(corners, angles, ref count, corner, angle);
                }
            SimVector[] polygon = new SimVector[3 + count];
            polygon[0] = center;
            polygon[1] = firstPoint;
            for (int index = 0; index < count; index++) polygon[2 + index] = corners[index];
            polygon[polygon.Length - 1] = secondPoint;
            return polygon;
        }

        /// <summary>구역 경계 직선과 맵 사각형의 교점과 그 사이 맵 모서리 중 중심에서 가장 먼 거리를 반환한다.</summary>
        internal static double GetSectorFarthestDistance(SimVector center, SimVector direction, double mapWidth, double mapHeight)
        {
            SimVector[] polygon = GetSectorPolygon(center, direction, mapWidth, mapHeight);
            double farthest = 0;
            for (int index = 1; index < polygon.Length; index++)
                farthest = Math.Max(farthest, SimVector.Distance(center, polygon[index]));
            return farthest;
        }

        /// <summary>중심에서 본 오프셋이 구역 중심 방향 ±45° 이내인지 반환한다. 중심 자체는 항상 구역 안으로 본다.</summary>
        private bool IsWithinSector(SimVector offset)
        {
            if (offset.LengthSquared <= RuneSimulation.COLLISION_EPSILON) return true;
            double halfRadians = HexaDefinition.RADIATE_SECTOR_HALF_DEGREES * RuneSimulation.DEGREES_TO_RADIANS;
            return Math.Abs(GetSectorAngleOffset(_center, _direction, _center + offset)) <= halfRadians;
        }

        /// <summary>중심에서 점을 본 방향과 구역 중심 방향 사이의 부호 있는 각도(라디안)를 반환한다.</summary>
        private static double GetSectorAngleOffset(SimVector center, SimVector direction, SimVector point)
        {
            SimVector offset = point - center;
            if (offset.LengthSquared <= DIRECTION_EPSILON) return 0;
            SimVector normalized = offset.Normalized();
            return Math.Atan2(direction.X * normalized.Y - direction.Y * normalized.X, SimVector.Dot(normalized, direction));
        }

        /// <summary>중심에서 방향으로 뻗는 광선이 맵 사각형의 변과 만나는 점 중 가장 먼 점을 반환하고 만나지 않으면 null을 반환한다.</summary>
        private static SimVector? GetFarthestBoundaryHit(SimVector center, SimVector direction, double mapWidth, double mapHeight)
        {
            double bestFraction = -1;
            bestFraction = Math.Max(bestFraction, GetEdgeFraction(center.X, direction.X, mapWidth, center.Y, direction.Y, mapHeight));
            bestFraction = Math.Max(bestFraction, GetEdgeFraction(center.X, direction.X, 0, center.Y, direction.Y, mapHeight));
            bestFraction = Math.Max(bestFraction, GetEdgeFraction(center.Y, direction.Y, mapHeight, center.X, direction.X, mapWidth));
            bestFraction = Math.Max(bestFraction, GetEdgeFraction(center.Y, direction.Y, 0, center.X, direction.X, mapWidth));
            return bestFraction < 0 ? (SimVector?)null : center + direction * bestFraction;
        }

        /// <summary>한 좌표가 지정 경계값에 닿는 양의 진행비를 반환하고, 방향 성분이 0이거나 도달점의 다른 좌표가 맵 밖이면 -1을 반환한다.</summary>
        private static double GetEdgeFraction(double along, double alongDirection, double edge, double across, double acrossDirection, double acrossMax)
        {
            if (Math.Abs(alongDirection) <= DIRECTION_EPSILON) return -1;
            double fraction = (edge - along) / alongDirection;
            if (fraction < 0) return -1;
            double acrossAtHit = across + acrossDirection * fraction;
            if (acrossAtHit < 0 || acrossAtHit > acrossMax) return -1;
            return fraction;
        }

        /// <summary>맵 모서리를 구역 중심 방향 기준 각도 오프셋이 큰 순서(첫 경계 → 두 번째 경계)로 삽입 정렬해 경계점 순서를 만든다.</summary>
        private static void InsertCorner(SimVector[] corners, double[] angles, ref int count, SimVector corner, double angle)
        {
            int index = count;
            while (index > 0 && angles[index - 1] < angle)
            {
                corners[index] = corners[index - 1];
                angles[index] = angles[index - 1];
                index--;
            }
            corners[index] = corner;
            angles[index] = angle;
            count++;
        }
    }
}
