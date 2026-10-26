using System;
using System.Collections.Generic;
using System.Text;

namespace RuneCode
{
    /// <summary>
    /// 유도 모드의 이동 동작이다. 행동의 이동 성격에 따라 적용한다.
    /// 이동형(발사)은 유도 범위 안 가장 가까운 미적중 적 쪽으로 진행 방향을 회전각만큼 돌린 뒤 직진하고,
    /// 정지형(잔류)은 예고가 끝난 뒤 가장 가까운 적 쪽으로 이동 속도만큼 옮기며,
    /// 경로형(빔)은 경로를 그리는 각 칸에서 진행 방향 앞의 대상 쪽으로 방향을 돌린다.
    /// 회전량은 모두 회전각 × 한 걸음 시간이다.
    /// 경로 모드(공전) 위에서는 진입을 마친 뒤 유도 범위 안 대상에게 직선으로 날아가고, 발사는 맞힌 뒤, 모두 대상이 죽거나
    /// 범위를 벗어나면 놓아 경로 모드에 이동을 돌려준다.
    /// </summary>
    internal sealed class HomingModifierRuntime : SpellModifierRuntime
    {
        private const double DEGREES_TO_RADIANS = Math.PI / 180;
        private const double FULL_TURN = 2 * Math.PI;
        private const double ORBIT_RING_SCALE = 0.3;
        private const double ORBIT_BEND_WIDTH_SCALE = 1.5;
        private const double ORBIT_MIN_BEND_ANGLE = 20 * DEGREES_TO_RADIANS;
        private const double ORBIT_MAX_BEND_ANGLE = 60 * DEGREES_TO_RADIANS;
        private const double ORBIT_BEND_PACKING = 1.5;
        private const double ORBIT_PREFERENCE_SCALE = 0.5;

        public override string RuneId => HomingModifierRules.RUNE_ID;

        /// <summary>경로 모드 위 추적 대상과, 경로형 행동이 경로 한 번을 그리는 동안 쓰는 유도 대상·지나친 대상 기록을 만든다.</summary>
        public override object CreateState(SimulationSpellEntity entity) => new HomingState();

        /// <summary>
        /// 이동형은 회전 후 직진, 정지형은 대상 쪽 이동으로 대상이 있을 때만 이동을 맡는다. 경로 모드(공전)가 있으면 경로를 떠나 직선 추적한다.
        /// 경로형은 맡지 않는다.
        /// </summary>
        public override bool TryMove(SpellRuntimeContext context, SimulationSpellEntity entity, object state)
        {
            SpellBehaviorRules rules = entity.Action.Rules;
            if (entity.IsSlash) return false;
            if (rules == null || rules.MotionKind == SpellMotionKind.Path) return false;
            bool isMoving = rules.MotionKind == SpellMotionKind.Moving;
            if (entity.HasModifierPath)
                return TryChaseOnPath(context, entity, (HomingState)state, isMoving);
            if (isMoving) return TrySteerMoving(context, entity);
            if (rules.MotionKind == SpellMotionKind.Stationary) return TryChaseStationary(context, entity);
            return false;
        }

        /// <summary>
        /// 유도 범위 안 가장 가까운 미적중 적이 있으면 진행 방향을 초당 회전각을 넘지 않게 돌리고 이동 속도만큼 직진한 뒤 true를 반환한다.
        /// 대상이 없으면 행동의 기본 직진에 맡기도록 false를 반환한다.
        /// </summary>
        private static bool TrySteerMoving(SpellRuntimeContext context, SimulationSpellEntity entity)
        {
            SpellStats stats = entity.Stats;
            if (stats.HomingTurn <= 0) return false;
            SimulationEnemy target = context.NearestEnemy(entity.Position, stats.HomingRange, entity.HitTimes);
            if (target == null) return false;
            SimVector direction = entity.Direction;
            double current = Math.Atan2(direction.Y, direction.X); SimVector toTarget = target.Position - entity.Position;
            double wanted = Math.Atan2(toTarget.Y, toTarget.X); double delta = Math.Atan2(Math.Sin(wanted - current), Math.Cos(wanted - current));
            double turn = stats.HomingTurn * DEGREES_TO_RADIANS * RuneSimulation.STEP_SECONDS;
            direction = direction.Rotated(Math.Max(-turn, Math.Min(turn, delta)));
            entity.Move(entity.Position + direction * (stats.Speed * RuneSimulation.STEP_SECONDS), direction);
            return true;
        }

        /// <summary>
        /// 예고가 끝났고 유도 범위 안에 적이 있으면 그 적 쪽으로 이동 속도만큼(닿으면 멈춤, 벽 통과 없음) 옮기고 true를 반환한다.
        /// 모양의 방향은 유지한다. 예고 중이거나 대상이 없으면 false를 반환한다.
        /// </summary>
        private static bool TryChaseStationary(SpellRuntimeContext context, SimulationSpellEntity entity)
        {
            if (entity.IsWarning || entity.Stats.HomingRange <= 0) return false;
            SimulationEnemy target = context.NearestEnemy(entity.Position, entity.Stats.HomingRange, SpellRuntimeContext.NoExcludedTargets);
            if (target == null) return false;
            SimVector offset = target.Position - entity.Position;
            double step = Math.Min(entity.Stats.Speed * RuneSimulation.STEP_SECONDS, offset.Length);
            if (step <= RuneSimulation.COLLISION_EPSILON) return true;
            entity.Move(context.Map.Move(entity.Position, offset.Normalized() * step, entity.Radius), entity.Direction);
            return true;
        }

        /// <summary>
        /// 경로 모드 위에서 추적 대상 쪽으로 직선 이동하고 true를 반환한다. 발사는 이동 속도로 직진하며 맞힌 대상은 놓고 다시 고르지 않는다.
        /// 잔류는 예고가 끝난 뒤 이동 속도로(벽 통과 없음) 대상 위까지 가서 머문다. 대상이 죽거나 유도 범위를 벗어나면 놓는다.
        /// 새 대상은 경로 모드 진입이 끝났을 때만 고르고 행동 속도와 공전 이탈 속도 중 큰 값을 추적 속도로 저장한다.
        /// 대상이 없으면 false를 반환해 경로 모드에 이동을 맡긴다.
        /// </summary>
        private static bool TryChaseOnPath(SpellRuntimeContext context, SimulationSpellEntity entity, HomingState homing, bool isMoving)
        {
            SpellStats stats = entity.Stats;
            if (stats.HomingRange <= 0 || entity.IsWarning)
            {
                homing.ChaseTargetId = 0;
                return false;
            }
            SimulationEnemy target = FindAliveEnemy(context, homing.ChaseTargetId);
            if (target != null && ((isMoving && entity.HitTimes.ContainsKey(target.Id))
                || (target.Position - entity.Position).Length > stats.HomingRange)) target = null;
            if (target == null)
            {
                homing.ChaseTargetId = 0;
                if (!entity.CanLeaveModifierPath()) return false;
                target = context.NearestEnemy(entity.Position, stats.HomingRange,
                    isMoving ? entity.HitTimes : SpellRuntimeContext.NoExcludedTargets);
                if (target == null) return false;
                homing.StartChase(target.Id, Math.Max(stats.Speed, OrbitModifierRuntime.GetExitSpeed(entity)));
            }
            SimVector offset = target.Position - entity.Position;
            if (isMoving)
            {
                SimVector heading = offset.LengthSquared > RuneSimulation.COLLISION_EPSILON ? offset.Normalized() : entity.Direction;
                entity.Move(entity.Position + heading * (homing.ChaseSpeed * RuneSimulation.STEP_SECONDS), heading);
                return true;
            }
            double step = Math.Min(homing.ChaseSpeed * RuneSimulation.STEP_SECONDS, offset.Length);
            if (step <= RuneSimulation.COLLISION_EPSILON) return true;
            entity.Move(context.Map.Move(entity.Position, offset.Normalized() * step, entity.Radius), entity.Direction);
            return true;
        }

        /// <summary>ID가 enemyId인 살아 있는 적을 반환한다. 없거나 enemyId가 0이면 null이다.</summary>
        private static SimulationEnemy FindAliveEnemy(SpellRuntimeContext context, int enemyId)
        {
            if (enemyId == 0) return null;
            foreach (SimulationEnemy enemy in context.Enemies)
                if (enemy.Id == enemyId && enemy.IsAlive) return enemy;
            return null;
        }

        /// <summary>새 경로를 그리기 시작하면 유도 대상과 지나친 대상 기록을 비운다.</summary>
        public override void BeginPath(SpellRuntimeContext context, SimulationSpellEntity entity, object state)
            => ((HomingState)state).Clear();

        /// <summary>
        /// 경로 한 칸의 방향을 진행 방향 앞의 대상 쪽으로 돌리고 true를 반환한다. 대상에 충분히 가까워지거나 대상이 뒤로 가면
        /// 지나친 대상으로 기록하고 놓는다. 대상이 없으면 false를 반환해 낮은 우선순위 이동 모드(공전)에 그 칸을 맡긴다.
        /// 회전량은 회전각 × (칸 길이 ÷ 경로 속도)다.
        /// </summary>
        public override bool SteerPathStep(SpellRuntimeContext context, SimulationSpellEntity entity, object state,
            SimVector point, ref SimVector direction, double step, double pathSpeed, double halfWidth)
        {
            SpellStats stats = entity.Stats;
            if (stats.HomingRange <= 0 || stats.HomingTurn <= 0) return false;
            var path = (HomingState)state;
            if (path.Target == null) path.Target = FindPathTarget(context, point, direction, stats.HomingRange, path);
            if (path.Target == null) return false;
            SimVector offset = path.Target.Position - point;
            if (offset.Length <= Math.Max(step, path.Target.Radius + halfWidth) || SimVector.Dot(offset, direction) < 0)
            {
                path.Pass(path.Target.Id);
                path.Target = null;
                return false;
            }
            double current = Math.Atan2(direction.Y, direction.X);
            double wanted = Math.Atan2(offset.Y, offset.X);
            double delta = Math.Atan2(Math.Sin(wanted - current), Math.Cos(wanted - current));
            double turn = stats.HomingTurn * DEGREES_TO_RADIANS * step / pathSpeed;
            direction = direction.Rotated(Math.Max(-turn, Math.Min(turn, delta)));
            return true;
        }

        /// <summary>
        /// 공전 빔 경로를 휘게 할 대상을 골라 bends에 시작 반경 방향 기준 각도 순으로 채우고 휘는 반폭(라디안)을 반환한다.
        /// radius는 이 빔의 공전 경로 반경이다. 공전 원에서 반경 방향으로 유도 범위 × ORBIT_RING_SCALE 이내인 적 중 우선 점수가 낮은 순(동률은 ID 순)으로
        /// 최대 대상 수만큼 고른다. 우선 점수는 원과의 거리이며, 다중 빔이면 빔·적 ID 해시로 만든 고정 선호도를 더해 빔마다 다른 적을 고르게 한다.
        /// 이미 고른 대상과 각도 차이가 반폭보다 작은 적은 건너뛴다. 반폭은 링 폭 기반 값을 20°~60°로 제한한 뒤 360° ÷ (1.5 × 대상 수) 이하로 줄인다.
        /// 유도 수치가 없으면 bends를 비우고 0을 반환한다.
        /// </summary>
        internal static double SelectOrbitBends(SpellRuntimeContext context, SimulationSpellEntity entity, SimVector startRadial,
            double radius, List<OrbitBend> bends)
        {
            bends.Clear();
            SpellStats stats = entity.Stats;
            if (stats.HomingRange <= 0 || stats.HomingTurn <= 0 || radius <= 0) return 0;
            double ringWidth = stats.HomingRange * ORBIT_RING_SCALE;
            int count = Math.Max(1, stats.OrbitTargetCount);
            // 선호도는 시간과 무관한 해시라 빔이 살아 있는 동안 고르는 적이 바뀌지 않아 경로가 튀지 않는다.
            double preference = entity.Action.Count > 1 ? ringWidth * ORBIT_PREFERENCE_SCALE : 0;
            // 대상은 원에 가까운 순으로 고르므로 사이에 빈틈이 남는다. 대상 수만큼 채워지도록 반폭을 360° ÷ (1.5 × 대상 수) 이하로 줄인다.
            double bendWidth = Math.Min(FULL_TURN / (ORBIT_BEND_PACKING * count),
                Math.Max(ORBIT_MIN_BEND_ANGLE, Math.Min(ORBIT_MAX_BEND_ANGLE, ORBIT_BEND_WIDTH_SCALE * ringWidth / radius)));
            double startAngle = Math.Atan2(startRadial.Y, startRadial.X);
            SimVector center = OrbitModifierRuntime.GetCenter(entity);
            for (int pick = 0; pick < count; pick++)
            {
                SimulationEnemy selected = null;
                double bestScore = 0, selectedAngle = 0, selectedDistance = 0;
                foreach (SimulationEnemy enemy in context.Enemies)
                {
                    if (!enemy.IsAlive) continue;
                    SimVector offset = enemy.Position - center;
                    double distance = offset.Length;
                    double gap = Math.Abs(distance - radius);
                    if (distance <= RuneSimulation.COLLISION_EPSILON || gap > ringWidth) continue;
                    double score = gap + (preference > 0 ? preference * GetPreferenceNoise(entity.Id, enemy.Id) : 0);
                    if (selected != null && (score > bestScore || (score == bestScore && enemy.Id > selected.Id))) continue;
                    double angle = NormalizeAngle(Math.Atan2(offset.Y, offset.X) - startAngle);
                    if (!IsSeparated(bends, angle, bendWidth)) continue;
                    selected = enemy;
                    bestScore = score;
                    selectedAngle = angle;
                    selectedDistance = distance;
                }
                if (selected == null) break;
                bends.Add(new OrbitBend(selectedAngle, selectedDistance - radius));
            }
            SortByAngle(bends);
            return bendWidth;
        }

        /// <summary>
        /// 시작 반경 방향에서 angle(라디안)만큼 돈 공전 경로 위의 점을 반환한다. 반경은 이 빔의 공전 경로 반경 radius에 각 대상의 반경 차이를
        /// 반폭 안에서 0.5·(1 + cos(π·각도차/반폭)) 가중치로 더한 값이며, 가중치 합이 1을 넘으면 합으로 나눈다.
        /// 대상 각도에서는 가중치가 1이라 대상 위치를 정확히 지난다.
        /// </summary>
        internal static SimVector GetOrbitPathPoint(SimulationSpellEntity entity, SimVector startRadial, double radius, double angle,
            IReadOnlyList<OrbitBend> bends, double bendWidth)
        {
            double weightSum = 0, heightSum = 0;
            for (int index = 0; index < bends.Count; index++)
            {
                double distance = AngleDistance(angle, bends[index].Angle);
                if (distance >= bendWidth) continue;
                double weight = 0.5 * (1 + Math.Cos(Math.PI * distance / bendWidth));
                weightSum += weight;
                heightSum += weight * bends[index].Height;
            }
            double pathRadius = radius + heightSum / Math.Max(1, weightSum);
            return OrbitModifierRuntime.GetCenter(entity) + startRadial.Rotated(angle) * pathRadius;
        }

        /// <summary>빔 ID와 적 ID로 시간과 무관한 고정 선호도 -1~1을 만든다. 같은 입력이면 항상 같은 값이다.</summary>
        private static double GetPreferenceNoise(int beamId, int enemyId)
        {
            unchecked
            {
                uint hash = (uint)beamId * 0x9E3779B1u ^ (uint)enemyId * 0x85EBCA77u;
                hash ^= hash >> 15;
                hash *= 0x2C1B3C6Du;
                hash ^= hash >> 12;
                hash *= 0x297A2D39u;
                hash ^= hash >> 15;
                return hash / (double)uint.MaxValue * 2 - 1;
            }
        }

        /// <summary>각도를 [0, 2π) 범위로 맞춘다.</summary>
        private static double NormalizeAngle(double angle)
        {
            angle %= FULL_TURN;
            if (angle < 0) angle += FULL_TURN;
            return angle >= FULL_TURN ? 0 : angle;
        }

        /// <summary>두 각도의 원형 차이(0~π 라디안)를 반환한다.</summary>
        private static double AngleDistance(double left, double right)
        {
            double distance = Math.Abs(left - right) % FULL_TURN;
            return distance > Math.PI ? FULL_TURN - distance : distance;
        }

        /// <summary>angle이 이미 고른 모든 대상과 반폭 이상 떨어져 있는지 반환한다.</summary>
        private static bool IsSeparated(List<OrbitBend> bends, double angle, double bendWidth)
        {
            foreach (OrbitBend bend in bends)
                if (AngleDistance(angle, bend.Angle) < bendWidth) return false;
            return true;
        }

        /// <summary>대상 수가 적으므로 삽입 정렬로 bends를 각도 오름차순으로 정렬한다.</summary>
        private static void SortByAngle(List<OrbitBend> bends)
        {
            for (int index = 1; index < bends.Count; index++)
            {
                OrbitBend current = bends[index];
                int target = index - 1;
                while (target >= 0 && bends[target].Angle > current.Angle)
                {
                    bends[target + 1] = bends[target];
                    target--;
                }
                bends[target + 1] = current;
            }
        }

        /// <summary>진행 방향 앞의 유도 범위 안에서 이번 경로에서 지나치지 않은 가장 가까운 적을 고른다. 거리가 같으면 ID가 작은 적이다.</summary>
        private static SimulationEnemy FindPathTarget(SpellRuntimeContext context, SimVector point, SimVector direction,
            double range, HomingState path)
        {
            SimulationEnemy selected = null;
            double bestDistance = range * range;
            foreach (SimulationEnemy enemy in context.Enemies)
            {
                if (!enemy.IsAlive || path.HasPassed(enemy.Id)) continue;
                SimVector offset = enemy.Position - point;
                double distance = offset.LengthSquared;
                if (SimVector.Dot(offset, direction) < 0 || distance > bestDistance) continue;
                if (selected != null && distance == bestDistance && enemy.Id > selected.Id) continue;
                selected = enemy;
                bestDistance = distance;
            }
            return selected;
        }

        /// <summary>경로 모드의 추적 대상 ID·추적 속도를 결정성 해시에 기록한다. 경로 유도 기록은 매 경로마다 비우므로 기록하지 않는다.</summary>
        public override void WriteState(SimulationSpellEntity entity, object state, StringBuilder buffer)
        {
            var homing = (HomingState)state;
            buffer.Append(FormattableString.Invariant($"|homing:{homing.ChaseTargetId}:{homing.ChaseSpeed:R}"));
        }

        /// <summary>경로 모드(공전) 위에서 직선 추적하는 대상 ID와, 경로 한 번을 그리는 동안의 유도 대상·지나친 대상 기록이다.</summary>
        private sealed class HomingState
        {
            private readonly HashSet<int> _passedTargets = new HashSet<int>();

            private double _chaseSpeed;

            /// <summary>경로 모드 위에서 추적 중인 적 ID다. 0이면 추적하지 않는다.</summary>
            public int ChaseTargetId { get; set; }

            /// <summary>현재 추적 대상을 선택할 때 공전에서 넘겨받은 이동 속도를 반환한다.</summary>
            public double ChaseSpeed => _chaseSpeed;

            /// <summary>새 추적 대상 ID와 공전에서 넘겨받은 이동 속도를 저장한다.</summary>
            public void StartChase(int targetId, double speed)
            {
                ChaseTargetId = targetId;
                _chaseSpeed = speed;
            }

            /// <summary>지금 따라가는 경로 유도 대상이다. 없으면 null이다.</summary>
            public SimulationEnemy Target { get; set; }

            /// <summary>대상과 지나친 대상 기록을 비운다.</summary>
            public void Clear()
            {
                Target = null;
                _passedTargets.Clear();
            }

            /// <summary>경로가 지나친 대상을 기록한다.</summary>
            public void Pass(int enemyId) => _passedTargets.Add(enemyId);

            /// <summary>이번 경로에서 이미 지나친 대상인지 반환한다.</summary>
            public bool HasPassed(int enemyId) => _passedTargets.Contains(enemyId);
        }

        /// <summary>공전 빔 경로를 휘게 할 대상 한 명의 시작 반경 방향 기준 각도(라디안)와 공전 반경 대비 반경 차이(px)다.</summary>
        internal readonly struct OrbitBend
        {
            private readonly double _angle;
            private readonly double _height;
            internal double Angle => _angle;
            internal double Height => _height;

            /// <summary>대상 각도와 반경 차이를 보관한다.</summary>
            internal OrbitBend(double angle, double height) { _angle = angle; _height = height; }
        }
    }
}
