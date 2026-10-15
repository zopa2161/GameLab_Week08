using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RuneCode
{
    /// <summary>얇은 빔의 홀드 수명, 곡선 경로, 지속 피해와 두 번 적중마다 발생하는 이벤트를 처리한다.</summary>
    internal sealed class BeamBehaviorRuntime : SpellBehaviorRuntime
    {
        private const double DEGREES_TO_RADIANS = Math.PI / 180;
        private const double PATH_STEP = 12;
        private const int HITS_PER_EVENT = 2;

        public override string Kind => BeamBehaviorRules.BEAM_KIND;
        public override bool IsDamageOverTime => true;

        /// <summary>빔별 경로·적중 횟수·피해 예약을 보관할 상태를 생성한다.</summary>
        public override object CreateState(SimulationSpellEntity entity) => new BeamState();

        /// <summary>발사와 같은 등급별 퍼짐으로 다중 빔의 고정 시작 방향을 배치한다.</summary>
        public override void PlaceSpawn(RuneSimulation simulation, SpellAction action, SpellStats stats, int index,
            bool isFromEvent, SimulationPlayer caster, ref SimVector position, ref SimVector direction)
        {
            if (action.Count > 1)
                direction = direction.Rotated((index / (double)(action.Count - 1) * 2 - 1) * stats.SpreadAngle * DEGREES_TO_RADIANS);
        }

        /// <summary>생성 틱에 경로를 만들고 최초 피해를 한 번 판정한다.</summary>
        public override void OnSpawned(RuneSimulation simulation, SimulationSpellEntity entity) => Advance(simulation, entity);

        /// <summary>공유 채널의 결제·종료를 갱신하고 고정 방향에서 경로와 예약된 지속 피해를 처리한다.</summary>
        public override void Advance(RuneSimulation simulation, SimulationSpellEntity entity)
        {
            var state = (BeamState)entity.BehaviorState;
            if (state.LastAdvanceTick == simulation.Tick) return;
            state.SetAdvanceTick(simulation.Tick);
            state.Channel.Advance(simulation);
            if (state.Channel.IsStopped) { entity.HasExpired = true; return; }
            if (state.Channel.FollowsPlayer)
                entity.Move(simulation.Player.Position + entity.Direction *
                    (simulation.PlayerRadius + simulation.Balance.Sim.WandOffset), entity.Direction);
            BuildPath(simulation, entity, state);
            if (simulation.Tick < state.NextDamageTick) return;
            state.ScheduleDamage(simulation.Tick + Math.Max(1, (int)Math.Round(entity.Stats.TickInterval * RuneSimulation.TICK_RATE)));
            HitPath(simulation, entity, state);
        }

        /// <summary>구간 종료는 틱 기반 채널이 처리하므로 공용 나이 판정으로 빔을 조기 제거하지 않는다.</summary>
        public override double GetLifetime(SimulationSpellEntity entity) => double.PositiveInfinity;

        /// <summary>맵 경계·벽·유도를 반영한 중심선 경로를 빔 상태에 재사용해 저장한다.</summary>
        private static void BuildPath(RuneSimulation simulation, SimulationSpellEntity entity, BeamState state)
        {
            state.ClearPath();
            SimVector point = entity.Position;
            SimVector direction = entity.Direction;
            double halfWidth = entity.Stats.BoxWidth * 0.5;
            state.AddPoint(point);
            if (!simulation.Map.CanOccupy(point, halfWidth)) return;
            double diagonal = Math.Sqrt(simulation.Map.Width * simulation.Map.Width + simulation.Map.Height * simulation.Map.Height);
            bool isHoming = entity.Stats.HomingRange > 0 && entity.Stats.HomingTurn > 0;
            double pathSpeed = GameData.Runes.Get("form.beam").Stats.Speed;
            double remaining = isHoming ? entity.Stats.BoxLength : diagonal;
            SimulationEnemy target = null;
            while (remaining > RuneSimulation.COLLISION_EPSILON)
            {
                double step = Math.Min(PATH_STEP, remaining);
                if (isHoming)
                {
                    if (target == null) target = FindTarget(simulation, point, direction, entity.Stats.HomingRange, state);
                    if (target != null)
                    {
                        SimVector offset = target.Position - point;
                        if (offset.Length <= Math.Max(step, target.Radius + halfWidth)
                            || SimVector.Dot(offset, direction) < 0)
                        {
                            state.PassTarget(target.Id);
                            target = null;
                        }
                        else
                        {
                            double current = Math.Atan2(direction.Y, direction.X);
                            double wanted = Math.Atan2(offset.Y, offset.X);
                            double delta = Math.Atan2(Math.Sin(wanted - current), Math.Cos(wanted - current));
                            double turn = entity.Stats.HomingTurn * DEGREES_TO_RADIANS * step / pathSpeed;
                            direction = direction.Rotated(Math.Max(-turn, Math.Min(turn, delta)));
                        }
                    }
                }
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

        /// <summary>진행 방향 앞의 탐색 범위에서 아직 지나지 않은 가장 가까운 적을 ID 동률 규칙으로 고른다.</summary>
        private static SimulationEnemy FindTarget(RuneSimulation simulation, SimVector point, SimVector direction,
            double range, BeamState state)
        {
            SimulationEnemy selected = null;
            double bestDistance = range * range;
            foreach (SimulationEnemy enemy in simulation.Enemies)
            {
                if (!enemy.IsAlive || state.HasPassedTarget(enemy.Id)) continue;
                SimVector offset = enemy.Position - point;
                double distance = offset.LengthSquared;
                if (SimVector.Dot(offset, direction) < 0 || distance > bestDistance) continue;
                if (selected != null && distance == bestDistance && enemy.Id > selected.Id) continue;
                selected = enemy;
                bestDistance = distance;
            }
            return selected;
        }

        /// <summary>경로에 겹친 적을 경로 거리·ID 순으로 한 번씩 타격하고 대상별 두 번째 피해마다 onHit을 발생시킨다.</summary>
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
                            start + (end - start) * fraction);
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
                simulation.Hit(entity, enemy, entity.Stats.Damage, true);
                if (enemy.Hp >= previousHp) continue;
                entity.HitTimes[enemy.Id] = simulation.Time;
                if (state.RecordHit(enemy.Id) % HITS_PER_EVENT == 0)
                    simulation.RaiseHitEvent(entity, target.Contact, enemy);
            }
        }

        /// <summary>채널 상태와 대상별 누적 적중 수를 고정 순서·불변 형식으로 결정성 해시에 기록한다.</summary>
        public override void WriteState(SimulationSpellEntity entity, StringBuilder state)
        {
            var beam = (BeamState)entity.BehaviorState;
            beam.Channel.WriteState(state);
            state.Append("|beam:").Append(beam.NextDamageTick).Append(':').Append(beam.LastAdvanceTick);
            beam.WriteHits(state);
        }

        /// <summary>한 번의 행동으로 만들어진 다중 빔이 공유하는 홀드·지속 결제와 루트 실행 수명이다.</summary>
        internal sealed class BeamChannel
        {
            private readonly int _slot;
            private readonly int _pressSerial;
            private readonly int _startedTick;
            private readonly int _minimumTicks;
            private readonly int _maximumTicks;
            private readonly int _paymentTicks;
            private readonly ResourceCostSet _paymentCosts;
            private readonly Action _complete;
            private readonly bool _canExtend;
            private readonly bool _followsPlayer;
            private int _endTick;
            private int _paidUntilTick;
            private int _lastTick = -1;
            private bool _hasReleased;
            private bool _isStopped;

            public bool FollowsPlayer => _followsPlayer;
            public bool IsStopped => _isStopped;

            /// <summary>최소 구간을 선결제한 빔 그룹의 수명과 추가 피해 주기별 비용을 초기화한다.</summary>
            internal BeamChannel(int tick, SpellStats stats, int slot, int pressSerial, bool canExtend,
                bool followsPlayer, ResourceCostSet minimumCosts, Action complete)
            {
                _slot = slot; _pressSerial = pressSerial; _startedTick = tick;
                _minimumTicks = Math.Max(1, (int)Math.Round(stats.MinimumLifetime * RuneSimulation.TICK_RATE));
                _maximumTicks = Math.Max(_minimumTicks, (int)Math.Round(stats.Lifetime * RuneSimulation.TICK_RATE));
                _paymentTicks = Math.Max(1, (int)Math.Round(stats.TickInterval * RuneSimulation.TICK_RATE));
                _paymentCosts = minimumCosts.Multiply(_paymentTicks / (double)_minimumTicks);
                _canExtend = canExtend; _followsPlayer = followsPlayer; _complete = complete;
                _endTick = tick + _minimumTicks; _paidUntilTick = _endTick;
            }

            /// <summary>한 틱에 한 번 홀드 해제·최대 시간·지속 비용을 검사하고 종료 시 루트 추적기를 해제한다.</summary>
            internal void Advance(RuneSimulation simulation)
            {
                if (_isStopped || _lastTick == simulation.Tick) return;
                _lastTick = simulation.Tick;
                if (!_canExtend || !simulation.IsBeamHeld(_slot, _pressSerial)) _hasReleased = true;
                int age = simulation.Tick - _startedTick;
                if (age >= _maximumTicks || (age >= _minimumTicks && _hasReleased)) { Stop(); return; }
                if (simulation.Tick >= _endTick) _endTick = Math.Min(_startedTick + _maximumTicks, _endTick + _minimumTicks);
                if (simulation.Tick < _paidUntilTick) return;
                if (!simulation.TrySpendBeamCosts(_paymentCosts)) { Stop(); return; }
                _paidUntilTick += _paymentTicks;
            }

            /// <summary>추가 이벤트 없이 채널을 종료하고 확보한 실행 추적 참조를 한 번만 해제한다.</summary>
            internal void Stop()
            {
                if (_isStopped) return;
                _isStopped = true;
                _complete?.Invoke();
            }

            /// <summary>공유 홀드·예약·결제 상태를 결정성 해시에 기록한다.</summary>
            internal void WriteState(StringBuilder state)
                => state.Append("|channel:").Append(_slot).Append(':').Append(_pressSerial).Append(':')
                    .Append(_startedTick).Append(':').Append(_endTick).Append(':').Append(_paidUntilTick).Append(':')
                    .Append(_lastTick).Append(':').Append(_hasReleased).Append(':').Append(_isStopped)
                    .Append(':').Append(_canExtend).Append(':').Append(_followsPlayer);
        }

        /// <summary>빔 개체별 재사용 경로·피해 예약·대상 적중 이력이다.</summary>
        internal sealed class BeamState
        {
            private readonly List<SimVector> _path = new List<SimVector>();
            private readonly List<BeamTarget> _targets = new List<BeamTarget>();
            private readonly HashSet<int> _passedTargets = new HashSet<int>();
            private readonly SortedDictionary<int, int> _hitCounts = new SortedDictionary<int, int>();
            private BeamChannel _channel;
            private int _lastAdvanceTick = -1;
            private int _nextDamageTick;

            public IReadOnlyList<SimVector> Path => _path;
            internal IReadOnlyList<BeamTarget> Targets => _targets;
            internal BeamChannel Channel => _channel;
            internal int LastAdvanceTick => _lastAdvanceTick;
            internal int NextDamageTick => _nextDamageTick;

            /// <summary>생성한 개체를 동일 행동의 공유 채널에 연결한다.</summary>
            internal void Attach(BeamChannel channel) => _channel = channel;
            /// <summary>중복 판정을 방지할 마지막 갱신 틱을 기록한다.</summary>
            internal void SetAdvanceTick(int tick) => _lastAdvanceTick = tick;
            /// <summary>다음 피해 판정 틱을 예약한다.</summary>
            internal void ScheduleDamage(int tick) => _nextDamageTick = tick;
            /// <summary>이전 중심선과 경로 탐색 이력을 비운다.</summary>
            internal void ClearPath() { _path.Clear(); _passedTargets.Clear(); }
            /// <summary>공유 표시·판정 중심선에 점을 추가한다.</summary>
            internal void AddPoint(SimVector point) => _path.Add(point);
            /// <summary>경로가 지난 유도 대상을 기록한다.</summary>
            internal void PassTarget(int id) => _passedTargets.Add(id);
            /// <summary>유도 대상이 이미 경로에서 지났는지 반환한다.</summary>
            internal bool HasPassedTarget(int id) => _passedTargets.Contains(id);
            /// <summary>이전 피해 후보 목록을 비운다.</summary>
            internal void ClearTargets() => _targets.Clear();
            /// <summary>중심선에 겹친 적과 최초 접촉 거리를 피해 후보에 추가한다.</summary>
            internal void AddTarget(SimulationEnemy enemy, double distance, SimVector contact)
                => _targets.Add(new BeamTarget(enemy, distance, contact));
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
            internal SimulationEnemy Enemy => _enemy;
            internal double Distance => _distance;
            internal SimVector Contact => _contact;

            /// <summary>경로상의 적·거리·접점을 피해 처리용으로 보관한다.</summary>
            internal BeamTarget(SimulationEnemy enemy, double distance, SimVector contact)
            { _enemy = enemy; _distance = distance; _contact = contact; }
        }
    }
}
