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

        public override string RuneId => HomingModifierRules.RUNE_ID;

        /// <summary>경로 모드 위 추적 대상과, 경로형 행동이 경로 한 번을 그리는 동안 쓰는 유도 대상·지나친 대상 기록을 만든다.</summary>
        public override object CreateState(SimulationSpellEntity entity) => new HomingState();

        /// <summary>
        /// 이동형은 회전 후 직진, 정지형은 대상 쪽 이동으로 대상이 있을 때만 이동을 맡는다. 경로 모드(공전)가 있으면 경로를 떠나 직선 추적한다.
        /// 경로형은 맡지 않는다.
        /// </summary>
        public override bool TryMove(RuneSimulation simulation, SimulationSpellEntity entity, object state)
        {
            SpellBehaviorRules rules = entity.Action.Rules;
            if (rules == null || rules.MotionKind == SpellMotionKind.Path) return false;
            if (entity.HasModifierPath)
                return TryChaseOnPath(simulation, entity, (HomingState)state, rules.MotionKind == SpellMotionKind.Moving);
            if (rules.MotionKind == SpellMotionKind.Moving) return TrySteerMoving(simulation, entity);
            if (rules.MotionKind == SpellMotionKind.Stationary) return TryChaseStationary(simulation, entity);
            return false;
        }

        /// <summary>
        /// 유도 범위 안 가장 가까운 미적중 적이 있으면 진행 방향을 초당 회전각을 넘지 않게 돌리고 이동 속도만큼 직진한 뒤 true를 반환한다.
        /// 대상이 없으면 행동의 기본 직진에 맡기도록 false를 반환한다.
        /// </summary>
        private static bool TrySteerMoving(RuneSimulation simulation, SimulationSpellEntity entity)
        {
            SpellStats stats = entity.Stats;
            if (stats.HomingTurn <= 0) return false;
            SimulationEnemy target = simulation.NearestEnemy(entity.Position, stats.HomingRange, entity.HitTimes);
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
        private static bool TryChaseStationary(RuneSimulation simulation, SimulationSpellEntity entity)
        {
            if (entity.IsWarning || entity.Stats.HomingRange <= 0) return false;
            SimulationEnemy target = simulation.NearestEnemy(entity.Position, entity.Stats.HomingRange, RuneSimulation.NoExcludedTargets);
            if (target == null) return false;
            SimVector offset = target.Position - entity.Position;
            double step = Math.Min(entity.Stats.Speed * RuneSimulation.STEP_SECONDS, offset.Length);
            if (step <= RuneSimulation.COLLISION_EPSILON) return true;
            entity.Move(simulation.Map.Move(entity.Position, offset.Normalized() * step, entity.Radius), entity.Direction);
            return true;
        }

        /// <summary>
        /// 경로 모드 위에서 추적 대상 쪽으로 직선 이동하고 true를 반환한다. 발사는 이동 속도로 직진하며 맞힌 대상은 놓고 다시 고르지 않는다.
        /// 잔류는 예고가 끝난 뒤 이동 속도로(벽 통과 없음) 대상 위까지 가서 머문다. 대상이 죽거나 유도 범위를 벗어나면 놓는다.
        /// 새 대상은 경로 모드 진입이 끝났을 때만 고르며, 대상이 없으면 false를 반환해 경로 모드에 이동을 맡긴다.
        /// </summary>
        private static bool TryChaseOnPath(RuneSimulation simulation, SimulationSpellEntity entity, HomingState homing, bool isMoving)
        {
            SpellStats stats = entity.Stats;
            if (stats.HomingRange <= 0 || entity.IsWarning)
            {
                homing.ChaseTargetId = 0;
                return false;
            }
            SimulationEnemy target = FindAliveEnemy(simulation, homing.ChaseTargetId);
            if (target != null && ((isMoving && entity.HitTimes.ContainsKey(target.Id))
                || (target.Position - entity.Position).Length > stats.HomingRange)) target = null;
            if (target == null)
            {
                homing.ChaseTargetId = 0;
                if (!entity.CanLeaveModifierPath()) return false;
                target = simulation.NearestEnemy(entity.Position, stats.HomingRange,
                    isMoving ? entity.HitTimes : RuneSimulation.NoExcludedTargets);
                if (target == null) return false;
                homing.ChaseTargetId = target.Id;
            }
            SimVector offset = target.Position - entity.Position;
            if (isMoving)
            {
                SimVector heading = offset.LengthSquared > RuneSimulation.COLLISION_EPSILON ? offset.Normalized() : entity.Direction;
                entity.Move(entity.Position + heading * (stats.Speed * RuneSimulation.STEP_SECONDS), heading);
                return true;
            }
            double step = Math.Min(stats.Speed * RuneSimulation.STEP_SECONDS, offset.Length);
            if (step <= RuneSimulation.COLLISION_EPSILON) return true;
            entity.Move(simulation.Map.Move(entity.Position, offset.Normalized() * step, entity.Radius), entity.Direction);
            return true;
        }

        /// <summary>ID가 enemyId인 살아 있는 적을 반환한다. 없거나 enemyId가 0이면 null이다.</summary>
        private static SimulationEnemy FindAliveEnemy(RuneSimulation simulation, int enemyId)
        {
            if (enemyId == 0) return null;
            foreach (SimulationEnemy enemy in simulation.Enemies)
                if (enemy.Id == enemyId && enemy.IsAlive) return enemy;
            return null;
        }

        /// <summary>새 경로를 그리기 시작하면 유도 대상과 지나친 대상 기록을 비운다.</summary>
        public override void BeginPath(RuneSimulation simulation, SimulationSpellEntity entity, object state)
            => ((HomingState)state).Clear();

        /// <summary>
        /// 경로 한 칸의 방향을 진행 방향 앞의 대상 쪽으로 돌리고 true를 반환한다. 대상에 충분히 가까워지거나 대상이 뒤로 가면
        /// 지나친 대상으로 기록하고 놓는다. 대상이 없으면 false를 반환해 낮은 우선순위 이동 모드(공전)에 그 칸을 맡긴다.
        /// 회전량은 회전각 × (칸 길이 ÷ 경로 속도)다.
        /// </summary>
        public override bool SteerPathStep(RuneSimulation simulation, SimulationSpellEntity entity, object state,
            SimVector point, ref SimVector direction, double step, double pathSpeed, double halfWidth)
        {
            SpellStats stats = entity.Stats;
            if (stats.HomingRange <= 0 || stats.HomingTurn <= 0) return false;
            var path = (HomingState)state;
            if (path.Target == null) path.Target = FindPathTarget(simulation, point, direction, stats.HomingRange, path);
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

        /// <summary>진행 방향 앞의 유도 범위 안에서 이번 경로에서 지나치지 않은 가장 가까운 적을 고른다. 거리가 같으면 ID가 작은 적이다.</summary>
        private static SimulationEnemy FindPathTarget(RuneSimulation simulation, SimVector point, SimVector direction,
            double range, HomingState path)
        {
            SimulationEnemy selected = null;
            double bestDistance = range * range;
            foreach (SimulationEnemy enemy in simulation.Enemies)
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

        /// <summary>경로 모드 위 추적 대상 ID를 결정성 해시에 기록한다. 경로 유도 기록은 매 경로마다 비우므로 기록하지 않는다.</summary>
        public override void WriteState(SimulationSpellEntity entity, object state, StringBuilder buffer)
            => buffer.Append("|homing:").Append(((HomingState)state).ChaseTargetId);

        /// <summary>경로 모드(공전) 위에서 직선 추적하는 대상 ID와, 경로 한 번을 그리는 동안의 유도 대상·지나친 대상 기록이다.</summary>
        private sealed class HomingState
        {
            private readonly HashSet<int> _passedTargets = new HashSet<int>();

            /// <summary>경로 모드 위에서 추적 중인 적 ID다. 0이면 추적하지 않는다.</summary>
            public int ChaseTargetId { get; set; }

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
    }
}
