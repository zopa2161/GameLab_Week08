using System;
using System.Collections.Generic;
using System.Text;

namespace RuneCode
{
    /// <summary>중심을 추종하며 오른쪽에서 왼쪽으로 회전하는 칼날의 구간 판정과 대상별 단발 피해를 처리한다.</summary>
    internal sealed class SlashBehaviorRuntime : SpellBehaviorRuntime
    {
        private const double DEGREES_TO_RADIANS = Math.PI / 180;
        private const int MAX_SWEEP_SEGMENTS = 128;

        public override string Kind => SlashBehaviorRules.KIND;
        public override bool TracksFirstEvent => true;

        /// <summary>개체의 생성 방향과 직접·후속 시전 구분으로 회전 중심 및 재사용 판정 상태를 만든다.</summary>
        public override object CreateState(SimulationSpellEntity entity) => new SlashState(entity);

        /// <summary>왕복 참격은 원래 베기와 되돌아 베기의 시간을 합산하고 일반 참격은 기존 수명을 유지한다.</summary>
        public override double GetLifetime(SimulationSpellEntity entity)
            => entity.Stats.Lifetime * (entity.HasBehaviorEvolution ? 2 : 1);

        /// <summary>직접 시전이면 다중 퍼짐 방향을 보존하고 위치만 시전자 중심으로 맞춘다.</summary>
        public override void PlaceSpawn(SpellRuntimeContext context, SpellAction action, SpellStats stats, int index,
            bool isFromEvent, SimulationPlayer caster, ref SimVector position, ref SimVector direction)
        {
            if (!isFromEvent) position = caster.Position;
        }

        /// <summary>생성 즉시 기준 중심에서 첫 활성 구간을 판정한다.</summary>
        public override void OnSpawned(SpellRuntimeContext context, SimulationSpellEntity entity)
        {
            MoveAlongPath(context, entity);
            Judge(context, entity);
        }

        /// <summary>일반·왕복 참격 모두 시전자 또는 발생 대상을 추종하며 현재 진행률의 칼날 방향을 적용한다.</summary>
        public override void MoveAlongPath(SpellRuntimeContext context, SimulationSpellEntity entity)
        {
            var state = (SlashState)entity.BehaviorState;
            entity.Move(state.GetCenter(entity.Caster), state.GetDirection(state.Progress, entity.SlashSweepAngle));
        }

        /// <summary>칼날이 지나간 면을 판정해 적마다 편도 한 번 타격하고, 왕복 참격은 귀환 구간에서 한 번 더 타격한다.</summary>
        public override void Judge(SpellRuntimeContext context, SimulationSpellEntity entity)
        {
            var state = (SlashState)entity.BehaviorState;
            if (state.LastTick == context.Tick || entity.Age >= entity.Lifetime) return;
            double progress = Math.Min(1, (entity.Age + RuneSimulation.STEP_SECONDS) / entity.Lifetime);
            BuildSweep(context, entity, state, progress);
            state.Commit(entity.Position, progress, context.Tick);
            SimVector direction = state.GetDirection(progress, entity.SlashSweepAngle);
            entity.Move(state.Center, direction);
            SimVector pushDirection = new SimVector(direction.Y, -direction.X);
            IReadOnlyList<SimulationEnemy> enemies = context.Enemies;
            for (int index = 0; index < enemies.Count; index++)
            {
                SimulationEnemy enemy = enemies[index];
                if (!enemy.IsAlive || entity.HitTimes.ContainsKey(enemy.Id)
                    || !IntersectsSweep(state.Samples, enemy.Position, enemy.Radius + entity.BoxWidth * .5)) continue;
                double previousHp = enemy.Hp;
                context.Hit(entity, enemy, entity.Stats.Damage, false, pushDirection);
                if (enemy.Hp >= previousHp) continue;
                entity.HitTimes[enemy.Id] = context.Time;
                entity.Hits++;
                SimVector contact = GetContact(state.Samples, enemy.Position);
                context.RaiseHitEvent(entity, contact, enemy);
            }
        }

        /// <summary>이전 중심·각도부터 칼날을 샘플링한다. 왕복 참격은 원래 각도를 되짚으며 칼날 끝을 벽에서 제한한다.</summary>
        private static void BuildSweep(SpellRuntimeContext context, SimulationSpellEntity entity, SlashState state, double progress)
        {
            double distance = (entity.Position - state.Center).Length
                + Math.Abs(progress - state.Progress) * (entity.HasBehaviorEvolution ? 2 : 1) * entity.SlashSweepAngle * DEGREES_TO_RADIANS * entity.Radius;
            int segments = Math.Max(1, Math.Min(MAX_SWEEP_SEGMENTS,
                (int)Math.Ceiling(distance / Math.Max(2, entity.BoxWidth * .5))));
            state.ClearSamples();
            for (int index = 0; index <= segments; index++)
            {
                double fraction = index / (double)segments;
                SimVector center = state.Center + (entity.Position - state.Center) * fraction;
                double sampleProgress = state.Progress + (progress - state.Progress) * fraction;
                SimVector direction = state.GetDirection(sampleProgress, entity.SlashSweepAngle);
                SimVector start = center;
                SimVector tip = center + direction * entity.Radius;
                double wall = context.FirstWallFraction(center, tip, entity.BoxWidth * .5);
                if (!double.IsPositiveInfinity(wall)) tip = center + (tip - center) * Math.Max(0, wall - RuneSimulation.COLLISION_EPSILON);
                state.AddSample(start, tip);
            }
        }

        /// <summary>적의 원형 범위가 인접 칼날 사이의 사각형이나 두께를 포함한 외곽선과 겹치는지 반환한다.</summary>
        private static bool IntersectsSweep(IReadOnlyList<BladeSample> samples, SimVector point, double radius)
        {
            for (int index = 1; index < samples.Count; index++)
            {
                BladeSample a = samples[index - 1], b = samples[index];
                if (SpellGeometry.SegmentDistance(a.Center, a.Tip, point) <= radius
                    || SpellGeometry.SegmentDistance(b.Center, b.Tip, point) <= radius
                    || SpellGeometry.SegmentDistance(a.Center, b.Center, point) <= radius
                    || SpellGeometry.SegmentDistance(a.Tip, b.Tip, point) <= radius
                    || IsInsideTriangle(point, a.Center, a.Tip, b.Tip)
                    || IsInsideTriangle(point, a.Center, b.Tip, b.Center)) return true;
            }
            return false;
        }

        /// <summary>삼각형이 퇴화하지 않았을 때 점이 삼각형 내부에 있는지 외적 부호로 판정한다.</summary>
        private static bool IsInsideTriangle(SimVector point, SimVector a, SimVector b, SimVector c)
        {
            if (Math.Abs(Cross(b - a, c - a)) < RuneSimulation.COLLISION_EPSILON) return false;
            double first = Cross(b - a, point - a);
            double second = Cross(c - b, point - b);
            double third = Cross(a - c, point - c);
            return first >= 0 && second >= 0 && third >= 0 || first <= 0 && second <= 0 && third <= 0;
        }

        /// <summary>두 평면 벡터의 외적 크기를 반환한다.</summary>
        private static double Cross(SimVector a, SimVector b) => a.X * b.Y - a.Y * b.X;

        /// <summary>이번 회전 구간의 칼날 중 대상 중심에 가장 가까운 접촉점을 반환한다.</summary>
        private static SimVector GetContact(IReadOnlyList<BladeSample> samples, SimVector point)
        {
            SimVector closest = samples[0].Center;
            double minimum = double.PositiveInfinity;
            for (int index = 0; index < samples.Count; index++)
            {
                BladeSample sample = samples[index];
                SimVector span = sample.Tip - sample.Center;
                double fraction = span.LengthSquared > RuneSimulation.COLLISION_EPSILON
                    ? Math.Max(0, Math.Min(1, SimVector.Dot(point - sample.Center, span) / span.LengthSquared)) : 0;
                SimVector contact = sample.Center + span * fraction;
                double distance = (point - contact).LengthSquared;
                if (distance >= minimum) continue;
                minimum = distance;
                closest = contact;
            }
            return closest;
        }

        /// <summary>시전 기준점이 부채꼴 전체 범위의 꼭짓점이다.</summary>
        public override SimVector GetConeApex(SimulationSpellEntity entity) => entity.Position;

        /// <summary>칼날 길이를 부채꼴 도달 반경으로 반환한다.</summary>
        public override double GetConeReach(SimulationSpellEntity entity) => entity.Radius;

        /// <summary>중심·발생 대상·기준 방향·회전 진행을 결정성 해시에 기록한다.</summary>
        public override void WriteState(SimulationSpellEntity entity, StringBuilder state)
            => ((SlashState)entity.BehaviorState).WriteState(state);

        internal readonly struct BladeSample
        {
            private readonly SimVector _center;
            private readonly SimVector _tip;
            public SimVector Center => _center;
            public SimVector Tip => _tip;

            /// <summary>구간 판정에 사용할 칼날의 중심과 끝점을 저장한다.</summary>
            public BladeSample(SimVector center, SimVector tip) { _center = center; _tip = tip; }
        }

        internal sealed class SlashState
        {
            private readonly SimVector _baseDirection;
            private readonly bool _returns;
            private readonly bool _followsCaster;
            private readonly List<BladeSample> _samples = new List<BladeSample>();
            private readonly List<SimVector> _path = new List<SimVector>(2);
            private SimulationEnemy _anchorTarget;
            private SimVector _center;
            private double _progress;
            private int _lastTick = -1;
            public SimVector Center => _center;
            public double Progress => _progress;
            public int LastTick => _lastTick;
            public IReadOnlyList<BladeSample> Samples => _samples;
            public IReadOnlyList<SimVector> Path => _path;

            /// <summary>개체에서 생성 방향과 중심을 읽고 직접 시전의 시전자 추종 여부를 고정한다.</summary>
            public SlashState(SimulationSpellEntity entity)
            {
                _baseDirection = entity.Direction.Normalized();
                _returns = entity.HasBehaviorEvolution;
                _followsCaster = !entity.FromEvent;
                _center = entity.Position;
            }

            /// <summary>후속 참격의 발생 대상을 연결하고 사망 여부와 관계없이 발생 당시 적 위치를 기준점으로 보관한다.</summary>
            public void SetAnchorTarget(SimulationEnemy target)
            {
                if (_followsCaster) return;
                _anchorTarget = target;
                if (target != null) _center = target.Position;
            }

            /// <summary>직접 시전자는 현재 위치, 후속 대상은 생존 중 현재 위치, 그 외에는 마지막 중심을 반환한다.</summary>
            public SimVector GetCenter(SimulationPlayer caster)
                => _followsCaster ? caster.Position : _anchorTarget != null && _anchorTarget.IsAlive ? _anchorTarget.Position : _center;

            /// <summary>진행률과 각도로 칼날 방향을 반환한다. 왕복 참격은 수명 후반에 원래 각도를 되짚는다.</summary>
            public SimVector GetDirection(double progress, double angle)
            {
                double swing = _returns ? (progress <= .5 ? progress * 2 : (1 - progress) * 2) : progress;
                return _baseDirection.Rotated((.5 - swing) * angle * DEGREES_TO_RADIANS);
            }

            /// <summary>이전 틱의 임시 판정 샘플을 지워 목록을 재사용한다.</summary>
            public void ClearSamples() => _samples.Clear();

            /// <summary>중심과 벽으로 제한한 칼날 끝점을 이번 회전 구간에 추가한다.</summary>
            public void AddSample(SimVector center, SimVector tip) => _samples.Add(new BladeSample(center, tip));

            /// <summary>판정한 중심·진행·틱을 저장하고 마지막 칼날을 표시용 중심선으로 갱신한다.</summary>
            public void Commit(SimVector center, double progress, int tick)
            {
                _center = center;
                _progress = progress;
                _lastTick = tick;
                BladeSample blade = _samples[_samples.Count - 1];
                _path.Clear();
                _path.Add(blade.Center);
                _path.Add(blade.Tip);
            }

            /// <summary>이후 실행을 결정하는 추종 대상·회전 기준과 마지막 판정 상태를 기록한다.</summary>
            public void WriteState(StringBuilder state)
                => state.Append(FormattableString.Invariant($"|slash:{_followsCaster}:{_anchorTarget?.Id ?? 0}:{_center.X:R}:{_center.Y:R}:{_baseDirection.X:R}:{_baseDirection.Y:R}:{_progress:R}:{_lastTick}:{_returns}"));
        }
    }
}
