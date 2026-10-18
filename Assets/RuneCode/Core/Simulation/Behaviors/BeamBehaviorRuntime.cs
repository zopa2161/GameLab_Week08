using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RuneCode
{
    /// <summary>얇은 빔의 홀드 수명, 곡선 경로, 지속 피해와 대상별 첫 타격 및 이후 두 타격마다 발생하는 이벤트를 처리한다.</summary>
    internal sealed class BeamBehaviorRuntime : SpellBehaviorRuntime
    {
        private const double PATH_STEP = 12;
        private const double ORBIT_PATH_STEP = 16;
        private const int ORBIT_MIN_SEGMENTS = 32;
        private const int ORBIT_MAX_SEGMENTS = 64;
        private const double ORBIT_RADIUS_STEP = 0.04;
        private const int HITS_PER_EVENT = 2;

        public override string Kind => BeamBehaviorRules.BEAM_KIND;
        public override bool IsDamageOverTime => true;

        /// <summary>빔별 경로·적중 횟수·피해 예약을 보관할 상태를 생성한다.</summary>
        public override object CreateState(SimulationSpellEntity entity) => new BeamState();

        /// <summary>
        /// 생성 틱에 시전자를 따라가는 빔이면 생성 방향의 조준 대비 각도를 저장하고, 발사 지점을 정한 뒤(이동 단계) 경로를 만들어 최초 피해를 한 번 판정한다.
        /// </summary>
        public override void OnSpawned(RuneSimulation simulation, SimulationSpellEntity entity)
        {
            var state = (BeamState)entity.BehaviorState;
            if (state.Channel.FollowsPlayer)
            {
                // 다중·호출 퍼짐이 반영된 생성 방향을 조준 기준 각도로 보관한다. 공전 접선이 덮어쓰기 전에 한 번만 계산한다.
                SimVector aim = simulation.Player.AimDirection;
                SimVector direction = entity.Direction;
                state.SetAimOffset(Math.Atan2(aim.X * direction.Y - aim.Y * direction.X, SimVector.Dot(aim, direction)));
            }
            MoveAlongPath(simulation, entity);
            Judge(simulation, entity);
        }

        /// <summary>
        /// 직접 시전해 시전자를 따라가는 빔이면 방향을 현재 조준에 생성 시 조준 대비 각도를 더한 값으로 바꾸고
        /// 발사 지점을 그 방향의 지팡이 끝으로 옮긴다. 이동 모드가 이동을 맡으면 호출되지 않는다.
        /// </summary>
        public override void MoveAlongPath(RuneSimulation simulation, SimulationSpellEntity entity)
        {
            var state = (BeamState)entity.BehaviorState;
            if (!state.Channel.FollowsPlayer) return;
            SimVector direction = simulation.Player.AimDirection.Rotated(state.AimOffset);
            entity.Move(simulation.Player.Position + direction *
                (simulation.PlayerRadius + simulation.Balance.Sim.WandOffset), direction);
        }

        /// <summary>
        /// 공유 채널의 결제·종료를 갱신하고 발사 지점에서 경로와 예약된 지속 피해를 처리한다. 한 틱에 한 번만 처리한다.
        /// </summary>
        public override void Judge(RuneSimulation simulation, SimulationSpellEntity entity)
        {
            var state = (BeamState)entity.BehaviorState;
            if (state.LastAdvanceTick == simulation.Tick) return;
            state.SetAdvanceTick(simulation.Tick);
            state.Channel.Advance(simulation);
            if (state.Channel.IsStopped) { entity.HasExpired = true; return; }
            BuildPath(simulation, entity, state);
            if (simulation.Tick < state.NextDamageTick) return;
            if (state.NextDamageTick == 0) simulation.RecordBeamFlash(entity, state.Path);
            state.ScheduleDamage(simulation.Tick + Math.Max(1, (int)Math.Round(entity.Stats.TickInterval * RuneSimulation.TICK_RATE)));
            HitPath(simulation, entity, state);
        }

        /// <summary>구간 종료는 틱 기반 채널이 처리하므로 공용 나이 판정으로 빔을 조기 제거하지 않는다.</summary>
        public override double GetLifetime(SimulationSpellEntity entity) => double.PositiveInfinity;

        /// <summary>
        /// 맵 경계·벽을 반영한 중심선 경로를 빔 상태에 재사용해 저장한다. 각 칸의 방향은 장착된 이동 모드(유도 등)가 경로 개입으로 고친다.
        /// 공전은 현재 공전 중심의 원 한 바퀴(유도가 있으면 고른 대상 쪽으로 휜 원), 그 외에는 유도 수치가 있으면 빔 길이, 없으면 맵 대각선을 경로 길이로 쓴다.
        /// </summary>
        private static void BuildPath(RuneSimulation simulation, SimulationSpellEntity entity, BeamState state)
        {
            state.ClearPath();
            entity.BeginPath(simulation);
            if (entity.HasModifierPath && entity.Stats.OrbitRadius > 0)
            {
                BuildOrbitPath(simulation, entity, state);
                return;
            }
            SimVector point = entity.Position;
            SimVector direction = entity.Direction;
            double halfWidth = entity.Stats.BoxWidth * 0.5;
            state.AddPoint(point);
            if (!simulation.Map.CanOccupy(point, halfWidth)) return;
            double diagonal = Math.Sqrt(simulation.Map.Width * simulation.Map.Width + simulation.Map.Height * simulation.Map.Height);
            bool isHoming = entity.Stats.HomingRange > 0 && entity.Stats.HomingTurn > 0;
            double pathSpeed = GameData.Runes.Get("form.beam").Stats.Speed;
            double remaining = isHoming ? entity.Stats.BoxLength : diagonal;
            while (remaining > RuneSimulation.COLLISION_EPSILON)
            {
                double step = Math.Min(PATH_STEP, remaining);
                entity.SteerPathStep(simulation, point, ref direction, step, pathSpeed, halfWidth);
                SimVector next = point + direction * step;
                double wall = simulation.FirstWallFraction(point, next, halfWidth);
                if (!double.IsPositiveInfinity(wall))
                {
                    state.AddPoint(point + (next - point) * Math.Max(0, wall - RuneSimulation.COLLISION_EPSILON));
                    break;
                }
                state.AddPoint(next);
                point = next;
                remaining -= step;
            }
        }

        /// <summary>
        /// 현재 공전 중심의 원을 반경에 비례한 등각 표본(ORBIT_MIN_SEGMENTS~ORBIT_MAX_SEGMENTS개)으로 만들고, 유도가 고른 대상 각도에도
        /// 표본을 끼워 넣어 휜 원이 대상 위치를 정확히 지나게 한다. 벽에 닿으면 그 앞에서 멈추고, 벽이 없으면 마지막 점을 첫 점과 정확히 연결한다.
        /// 다중 빔은 순번마다 반경을 조금씩 달리해 겹치지 않게 한다.
        /// </summary>
        private static void BuildOrbitPath(RuneSimulation simulation, SimulationSpellEntity entity, BeamState state)
        {
            double radius = GetOrbitPathRadius(entity);
            int segments = Math.Max(ORBIT_MIN_SEGMENTS, Math.Min(ORBIT_MAX_SEGMENTS, (int)Math.Ceiling(2 * Math.PI * radius / ORBIT_PATH_STEP)));
            double angleStep = 2 * Math.PI / segments;
            SimVector radial = (entity.Position - OrbitModifierRuntime.GetCenter(entity)).Normalized();
            double halfWidth = entity.Stats.BoxWidth * 0.5;
            List<HomingModifierRuntime.OrbitBend> bends = state.Bends;
            double bendWidth = HomingModifierRuntime.SelectOrbitBends(simulation, entity, radial, radius, bends);
            SimVector first = HomingModifierRuntime.GetOrbitPathPoint(entity, radial, radius, 0, bends, bendWidth);
            SimVector point = first;
            state.AddPoint(first);
            if (!simulation.Map.CanOccupy(first, halfWidth)) return;
            int bendIndex = 0;
            for (int index = 1; index <= segments; index++)
            {
                double sampleAngle = index * angleStep;
                // 이번 표본 전에 놓인 대상 각도를 먼저 넣는다. 첫 점이나 이번 표본과 겹치는 대상 각도는 그 점이 대신한다.
                while (bendIndex < bends.Count && bends[bendIndex].Angle <= sampleAngle + RuneSimulation.COLLISION_EPSILON)
                {
                    double bendAngle = bends[bendIndex++].Angle;
                    if (bendAngle <= RuneSimulation.COLLISION_EPSILON || bendAngle >= sampleAngle - RuneSimulation.COLLISION_EPSILON) continue;
                    if (!TryAddOrbitPoint(simulation, state, ref point,
                        HomingModifierRuntime.GetOrbitPathPoint(entity, radial, radius, bendAngle, bends, bendWidth), halfWidth)) return;
                }
                SimVector next = index == segments ? first : HomingModifierRuntime.GetOrbitPathPoint(entity, radial, radius, sampleAngle, bends, bendWidth);
                if (!TryAddOrbitPoint(simulation, state, ref point, next, halfWidth)) return;
            }
        }

        /// <summary>
        /// 이 빔의 공전 경로 반경을 반환한다. 다중 빔이면 순번 각도에서 복원한 순번 i(0~개수-1)로 공전 반경 × (1 + ORBIT_RADIUS_STEP × (i - (개수-1)/2))를,
        /// 단일 빔이면 공전 반경을 그대로 쓴다.
        /// </summary>
        private static double GetOrbitPathRadius(SimulationSpellEntity entity)
        {
            double radius = entity.Stats.OrbitRadius;
            int count = entity.Action.Count;
            if (count <= 1) return radius;
            int index = (int)Math.Round(entity.Angle * count / (2 * Math.PI)) % count;
            return radius * (1 + ORBIT_RADIUS_STEP * (index - (count - 1) * 0.5));
        }

        /// <summary>point에서 next까지 벽을 검사해 막히지 않으면 next를 경로에 추가하고 point를 옮긴 뒤 true를 반환한다. 막히면 벽 앞 점을 추가하고 false를 반환한다.</summary>
        private static bool TryAddOrbitPoint(RuneSimulation simulation, BeamState state, ref SimVector point, SimVector next, double halfWidth)
        {
            double wall = simulation.FirstWallFraction(point, next, halfWidth);
            if (!double.IsPositiveInfinity(wall))
            {
                state.AddPoint(point + (next - point) * Math.Max(0, wall - RuneSimulation.COLLISION_EPSILON));
                return false;
            }
            state.AddPoint(next);
            point = next;
            return true;
        }

        /// <summary>경로에 겹친 적을 경로 거리·ID 순으로 한 번씩 타격하고 실제 피해를 준 대상별 1·3·5번째 타격에 OnHit을 발생시킨다.</summary>
        private static void HitPath(RuneSimulation simulation, SimulationSpellEntity entity, BeamState state)
        {
            state.ClearTargets();
            foreach (SimulationEnemy enemy in simulation.Enemies)
            {
                if (!enemy.IsAlive) continue;
                double distance = 0;
                for (int index = 1; index < state.Path.Count; index++)
                {
                    SimVector start = state.Path[index - 1], end = state.Path[index];
                    if (SpellGeometry.SegmentDistance(start, end, enemy.Position) <= entity.Stats.BoxWidth * 0.5 + enemy.Radius)
                    {
                        double fraction = SpellGeometry.CircleContactFraction(start, end, enemy.Position, entity.Stats.BoxWidth * 0.5 + enemy.Radius);
                        if (double.IsPositiveInfinity(fraction))
                        {
                            SimVector segment = end - start;
                            fraction = segment.LengthSquared > RuneSimulation.COLLISION_EPSILON
                                ? Math.Max(0, Math.Min(1, SimVector.Dot(enemy.Position - start, segment) / segment.LengthSquared)) : 0;
                        }
                        state.AddTarget(enemy, distance + (end - start).Length * fraction,
                            start + (end - start) * fraction, end - start);
                        break;
                    }
                    distance += (end - start).Length;
                }
            }
            state.SortTargets();
            foreach (BeamTarget target in state.Targets)
            {
                SimulationEnemy enemy = target.Enemy;
                if (!enemy.IsAlive) continue;
                double previousHp = enemy.Hp;
                simulation.Hit(entity, enemy, entity.Stats.Damage, true, target.Direction);
                if (enemy.Hp >= previousHp) continue;
                entity.HitTimes[enemy.Id] = simulation.Time;
                if ((state.RecordHit(enemy.Id) - 1) % HITS_PER_EVENT == 0)
                    simulation.RaiseHitEvent(entity, target.Contact, enemy);
            }
        }

        /// <summary>채널 상태, 조준 대비 각도와 대상별 누적 적중 수를 고정 순서·불변 형식으로 결정성 해시에 기록한다.</summary>
        public override void WriteState(SimulationSpellEntity entity, StringBuilder state)
        {
            var beam = (BeamState)entity.BehaviorState;
            beam.Channel.WriteState(state);
            state.Append("|beam:").Append(beam.NextDamageTick).Append(':').Append(beam.LastAdvanceTick)
                .Append(':').Append(beam.AimOffset.ToString("R", CultureInfo.InvariantCulture));
            beam.WriteHits(state);
        }

        /// <summary>한 행동의 다중 빔이 공유하는 결제 완료 구간이다. 직접 공격 빔은 슬롯 경계에서 갱신하고 나머지는 초기 구간만 유지한다.</summary>
        internal sealed class BeamChannel
        {
            private readonly int _slot;
            private readonly int _startedTick;
            private readonly SpellAction _action;
            private readonly ResourceCostSet _cycleCosts;
            private readonly Action _complete;
            private readonly bool _usesSlotCycle;
            private readonly bool _followsPlayer;
            private int _paidUntilTick;
            private int _lastTick = -1;
            private bool _isStopped;

            public bool FollowsPlayer => _followsPlayer;
            public bool IsStopped => _isStopped;
            public int Slot => _slot;
            public SpellAction Action => _action;
            public bool UsesSlotCycle => _usesSlotCycle;
            public ResourceCostSet CycleCosts => _cycleCosts;
            public int PaidUntilTick => _paidUntilTick;

            /// <summary>최초 결제 비용과 슬롯 주기 종료 틱을 저장한다. 비슬롯 빔의 수명은 기존 최소 유지 시간으로 정한다.</summary>
            internal BeamChannel(int tick, SpellStats stats, int slot, bool followsPlayer, SpellAction action,
                bool usesSlotCycle, int cycleEndTick, ResourceCostSet cycleCosts, Action complete)
            {
                _slot = slot; _startedTick = tick; _action = action;
                _usesSlotCycle = usesSlotCycle; _followsPlayer = followsPlayer; _complete = complete;
                _cycleCosts = cycleCosts;
                int minimumTicks = Math.Max(1, (int)Math.Round(stats.MinimumLifetime * RuneSimulation.TICK_RATE));
                _paidUntilTick = usesSlotCycle ? cycleEndTick : tick + minimumTicks;
            }

            /// <summary>최초 공격은 보장하고 결제한 구간 끝에 도달하면 종료한다. 홀드·결제는 슬롯 시전 경계에서 먼저 처리한다.</summary>
            internal void Advance(RuneSimulation simulation)
            {
                if (_isStopped || _lastTick == simulation.Tick) return;
                _lastTick = simulation.Tick;
                int age = simulation.Tick - _startedTick;
                if (age == 0) return;
                if (simulation.Tick >= _paidUntilTick) Stop();
            }

            /// <summary>구간 비용 지불 성공 후 종료 틱만 갱신하며 경로·적중 횟수·다음 공격 시각은 유지한다.</summary>
            internal void Renew(int cycleEndTick) => _paidUntilTick = cycleEndTick;

            /// <summary>추가 이벤트 없이 채널을 종료하고 확보한 실행 추적 참조를 한 번만 해제한다.</summary>
            internal void Stop()
            {
                if (_isStopped) return;
                _isStopped = true;
                _complete?.Invoke();
            }

            /// <summary>공유 홀드·예약·결제 상태를 결정성 해시에 기록한다.</summary>
            internal void WriteState(StringBuilder state)
            {
                state.Append("|channel:").Append(_slot).Append(':').Append(_action.NodeId).Append(':')
                    .Append(_startedTick).Append(':').Append(_paidUntilTick).Append(':')
                    .Append(_lastTick).Append(':').Append(_isStopped)
                    .Append(':').Append(_usesSlotCycle).Append(':').Append(_followsPlayer);
                foreach (ResourceAmount amount in _cycleCosts.Amounts)
                    state.Append(':').Append(amount.Resource).Append('=').Append(amount.Amount.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        /// <summary>빔 개체별 재사용 경로·공전 휨 대상·피해 예약·조준 대비 각도·대상 적중 이력이다.</summary>
        internal sealed class BeamState
        {
            private readonly List<SimVector> _path = new List<SimVector>();
            private readonly List<HomingModifierRuntime.OrbitBend> _bends = new List<HomingModifierRuntime.OrbitBend>();
            private readonly List<BeamTarget> _targets = new List<BeamTarget>();
            private readonly SortedDictionary<int, int> _hitCounts = new SortedDictionary<int, int>();
            private BeamChannel _channel;
            private int _lastAdvanceTick = -1;
            private int _nextDamageTick;
            private double _aimOffset;

            public IReadOnlyList<SimVector> Path => _path;
            /// <summary>공전 경로를 휘게 할 이번 틱 대상 목록이다. 경로를 만들 때마다 유도 모드가 다시 채운다.</summary>
            internal List<HomingModifierRuntime.OrbitBend> Bends => _bends;
            internal IReadOnlyList<BeamTarget> Targets => _targets;
            internal BeamChannel Channel => _channel;
            internal int LastAdvanceTick => _lastAdvanceTick;
            internal int NextDamageTick => _nextDamageTick;
            /// <summary>시전자를 따라가는 빔의 생성 방향이 조준 방향에서 벗어난 각도(라디안, 반시계 양수)다.</summary>
            internal double AimOffset => _aimOffset;

            /// <summary>생성한 개체를 동일 행동의 공유 채널에 연결한다.</summary>
            internal void Attach(BeamChannel channel) => _channel = channel;
            /// <summary>중복 판정을 방지할 마지막 갱신 틱을 기록한다.</summary>
            internal void SetAdvanceTick(int tick) => _lastAdvanceTick = tick;
            /// <summary>다음 피해 판정 틱을 예약한다.</summary>
            internal void ScheduleDamage(int tick) => _nextDamageTick = tick;
            /// <summary>생성 방향의 조준 대비 각도를 기록한다.</summary>
            internal void SetAimOffset(double offset) => _aimOffset = offset;
            /// <summary>이전 중심선을 비운다. 경로 유도의 지나친 대상 기록은 유도 모드가 BeginPath에서 비운다.</summary>
            internal void ClearPath() => _path.Clear();
            /// <summary>공유 표시·판정 중심선에 점을 추가한다.</summary>
            internal void AddPoint(SimVector point) => _path.Add(point);
            /// <summary>이전 피해 후보 목록을 비운다.</summary>
            internal void ClearTargets() => _targets.Clear();
            /// <summary>중심선에 겹친 적의 최초 접촉 거리·접점·구간 진행 방향을 피해 후보에 추가한다.</summary>
            internal void AddTarget(SimulationEnemy enemy, double distance, SimVector contact, SimVector direction)
                => _targets.Add(new BeamTarget(enemy, distance, contact, direction));
            /// <summary>경로 거리와 적 ID 순서로 피해 후보를 정렬한다.</summary>
            internal void SortTargets() => _targets.Sort(CompareTargets);
            /// <summary>후보의 경로 거리를 비교하고 동률이면 적 ID로 순서를 정한다.</summary>
            private static int CompareTargets(BeamTarget left, BeamTarget right)
            {
                int order = left.Distance.CompareTo(right.Distance);
                return order != 0 ? order : left.Enemy.Id.CompareTo(right.Enemy.Id);
            }
            /// <summary>대상의 실제 피해 횟수를 증가시키고 누적 횟수를 반환한다.</summary>
            internal int RecordHit(int id)
            {
                _hitCounts.TryGetValue(id, out int count);
                _hitCounts[id] = ++count;
                return count;
            }
            /// <summary>정렬된 대상별 적중 이력을 상태 해시에 추가한다.</summary>
            internal void WriteHits(StringBuilder state)
            {
                foreach (KeyValuePair<int, int> pair in _hitCounts)
                    state.Append('|').Append(pair.Key).Append(':').Append(pair.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>피해 후보 한 개의 적과 중심선 최초 접촉 위치다.</summary>
        internal readonly struct BeamTarget
        {
            private readonly SimulationEnemy _enemy;
            private readonly double _distance;
            private readonly SimVector _contact;
            private readonly SimVector _direction;
            internal SimulationEnemy Enemy => _enemy;
            internal double Distance => _distance;
            internal SimVector Contact => _contact;
            internal SimVector Direction => _direction;

            /// <summary>경로상의 적·거리·접점·구간 진행 방향을 피해와 밀어내기 처리용으로 보관한다.</summary>
            internal BeamTarget(SimulationEnemy enemy, double distance, SimVector contact, SimVector direction)
            { _enemy = enemy; _distance = distance; _contact = contact; _direction = direction; }
        }
    }
}
