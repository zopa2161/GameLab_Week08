using System;
using System.Collections.Generic;
using System.Text;

namespace RuneCode
{
    /// <summary>
    /// 슬롯 시전·Trigger 시전, 루트 분기와 예약 실행, 틱당 행동 예산, 자원 결제, 마법 개체 생성과 빔 정리를 담당한다.
    /// 예약 목록과 행동 예산은 재진입 실행(OnHit·OnExpire)이 같은 인스턴스를 공유한다.
    /// </summary>
    internal sealed class SpellExecutor
    {
        private readonly SimulationWorld _world;
        private readonly SimulationFeedback _feedback;
        private readonly SimulationStats _stats;
        private readonly BeamChannelController _beam;
        private readonly SpellRuntimeContext _runtime;
        private readonly List<ScheduledExecution> _scheduled = new List<ScheduledExecution>();
        private readonly CompiledSpell[] _loadout = new CompiledSpell[SpellGrammar.MAX_START_NODES];
        private readonly List<string> _unlockedElements = new List<string> { "fire" };
        private SimVector? _aimPoint;
        private int _actionBudgetTick = -1;
        private int _actionsThisTick;
        private int _droppedExecutions;

        /// <summary>장착된 마법 목록이다. 같은 배열 인스턴스를 읽기 전용으로 노출한다.</summary>
        internal IReadOnlyList<CompiledSpell> Loadout => _loadout;

        /// <summary>이번 틱 입력의 포인터 위치다. Step 시작 시 설정한다.</summary>
        internal SimVector? AimPoint { get => _aimPoint; set => _aimPoint = value; }

        /// <summary>행동 예산을 마지막으로 초기화한 틱이다. 상태 해시용이다.</summary>
        internal int ActionBudgetTick => _actionBudgetTick;

        /// <summary>현재 틱에 실행한 행동 수다. 상태 해시용이다.</summary>
        internal int ActionsThisTick => _actionsThisTick;

        /// <summary>예약·행동 상한으로 버린 실행 수다.</summary>
        internal int DroppedExecutions => _droppedExecutions;

        /// <summary>월드·표시 버퍼·통계·빔 컨트롤러와 행동·모드 런타임에 넘길 런타임 컨텍스트로 만든다.</summary>
        internal SpellExecutor(SimulationWorld world, SimulationFeedback feedback, SimulationStats stats, BeamChannelController beam, SpellRuntimeContext runtime)
        {
            _world = world;
            _feedback = feedback;
            _stats = stats;
            _beam = beam;
            _runtime = runtime;
        }

        /// <summary>공격 입력이나 빔 예약 클릭이 있는 슬롯을 0, 1, 2 순서로 공격 Trigger 시전한다.</summary>
        internal void TryCastAttackSlots(SimulationInput input)
        {
            if (input.CastA || _beam.IsQueued(0)) TryCastTriggered(_loadout[0], SpellGrammar.TRIGGER_ON_ATTACK, 0);
            if (input.CastB || _beam.IsQueued(1)) TryCastTriggered(_loadout[1], SpellGrammar.TRIGGER_ON_ATTACK, 1);
            if (input.CastC || _beam.IsQueued(2)) TryCastTriggered(_loadout[2], SpellGrammar.TRIGGER_ON_ATTACK, 2);
        }

        /// <summary>시험 도크 초기화 시 행동 예산과 버린 실행 수를 되돌린다.</summary>
        internal void ResetForBench()
        {
            _actionBudgetTick = -1; _actionsThisTick = 0; _droppedExecutions = 0;
        }

        /// <summary>전투 취소 시 예약 실행을 추적 해제 없이 비운다.</summary>
        internal void ClearScheduled() => _scheduled.Clear();

        /// <summary>예약 실행 목록을 상태 해시 문자열에 기존 형식으로 기록한다.</summary>
        internal void WriteScheduledState(StringBuilder state)
        {
            foreach (ScheduledExecution pending in _scheduled)
            {
                state.Append(pending.Tick);
                if (pending.IsBranchStart)
                    state.Append("|branch:").Append(pending.Action.NodeId);
                else if (pending.Wave > 0)
                    state.Append("|wave:").Append(pending.Action.NodeId).Append(':').Append(pending.Wave);
                else state.Append("|finish:").Append(pending.Action.NodeId);
                SimHash.AppendNumber(state, pending.Context.Origin.X); SimHash.AppendNumber(state, pending.Context.Origin.Y);
                SimHash.AppendNumber(state, pending.Context.Direction.X); SimHash.AppendNumber(state, pending.Context.Direction.Y);
                SimHash.AppendNumber(state, pending.Context.DirectionOffset);
                state.Append(pending.Context.Target?.Id ?? 0).Append('|').Append(pending.Context.FromEvent).Append('|').Append(pending.Context.NoiseElement);
                SimHash.AppendNumber(state, pending.Context.CostMultiplier);
                SimHash.AppendNumber(state, pending.Context.CastDamageMultiplier);
                if (pending.Context.IsManaPrepaid) state.Append("|manaPrepaid");
                if (pending.Context.SuppressesTriggers) state.Append("|noTriggers");
                state.Append(pending.Context.SourceEntityId).Append('|');
                if (pending.Tracker != null)
                {
                    state.Append("|flow:").Append(pending.Tracker.RootCast?.Slot ?? -1).Append(':').Append(pending.Tracker.Pending);
                    state.Append(':').Append(pending.Tracker.RootCast?.CycleEndTick ?? -1);
                    pending.Tracker.RootCast?.WriteTargetState(state);
                }
                pending.Context.Modifiers.AppendState(state);
                if (pending.Context.Target != null && !pending.Context.Target.IsAlive) pending.Context.Target.WriteState(state);
            }
        }

        /// <summary>장착 마법 서명과 해금 원소를 상태 해시 문자열에 기존 형식으로 기록한다.</summary>
        internal void WriteLoadoutState(StringBuilder state)
        {
            foreach (CompiledSpell spell in _loadout) state.Append('|').Append(spell?.Signature);
            foreach (string element in _unlockedElements) state.Append('|').Append(element);
        }

        /// <summary>받은 컴파일 마법 목록을 슬롯 0부터 순서대로 장착하고 남는 슬롯은 비운다. 미션은 시작 노드별 목록을, 시험 도크는 기존 슬롯 목록을 받는다.</summary>
        internal void SetLoadout(IReadOnlyList<CompiledSpell> spells)
        {
            for (int i = 0; i < _loadout.Length; i++)
            {
                CompiledSpell next = i < spells.Count ? spells[i] : null;
                if (!ReferenceEquals(_loadout[i], next)) CancelBeamChannels();
                _loadout[i] = next;
            }
        }

        /// <summary>해금한 속성 태그를 노이즈의 무작위 후보로 설정하고 기본 화염 후보를 보존한다.</summary>
        internal void SetUnlockedElements(IReadOnlyList<string> elements)
        {
            _unlockedElements.Clear();
            for (int i = 0; i < elements.Count; i++)
            { string tag = elements[i].StartsWith("elem.", StringComparison.Ordinal) ? elements[i].Substring(5) : elements[i]; if ((tag == "fire" || tag == "ice" || tag == "arc") && !_unlockedElements.Contains(tag)) _unlockedElements.Add(tag); }
            if (_unlockedElements.Count == 0) _unlockedElements.Add("fire");
        }

        /// <summary>
        /// 슬롯 쿨다운을 시작하고 루트 실행을 등록한다. 루트 문맥에는 비용 결제 전 시점의 최대 전력·대시 직후 조건으로 합산한 시전 피해 배율을 넣는다.
        /// 직접 공격 빔은 구간 비용을 선결제하며 연장은 현재 슬롯 입력으로 판단한다.
        /// </summary>
        internal bool TryCast(CompiledSpell spell, int slot = 0, bool allowBeamHold = false) => BeginRootCast(spell, slot, null);

        /// <summary>
        /// 상태 이상 중첩 트리거처럼 적을 원점·대상으로 삼는 시전 경로다. 적마다 슬롯 잠금과 분리된 실행 추적기를 사용하고
        /// 같은 틱 실행량은 공통 행동 예산으로 제한한다. 대상이 없거나 이미 죽었으면 시전하지 않는다.
        /// </summary>
        internal void TryCastTriggerAtEnemy(string trigger, SimulationEnemy target)
        {
            if (target == null || !target.IsAlive) return;
            for (int slot = 0; slot < _loadout.Length; slot++)
            {
                CompiledSpell spell = _loadout[slot];
                if (spell == null || spell.Trigger != trigger) continue;
                BeginRootCast(spell, slot, target);
            }
        }

        /// <summary>전투 단계에서 지정 Trigger의 실행 가능 여부를 반환한다. 상태 중첩은 적별 이벤트여서 슬롯 쿨다운·실행 잠금을 공유하지 않는다.</summary>
        internal bool IsTriggerReady(string trigger)
        {
            if (_world.Stage != MissionStage.Bench && _world.Stage != MissionStage.Combat && _world.Stage != MissionStage.RoundBreak) return false;
            for (int slot = 0; slot < _loadout.Length; slot++)
            {
                CompiledSpell spell = _loadout[slot];
                if (spell != null && spell.Trigger == trigger
                    && (trigger == SpellGrammar.TRIGGER_ON_STATUS_STACK
                        || _world.Player.Cooldowns[slot] <= 0.000001 && !_world.Player.IsSpellRunning(slot))) return true;
            }
            return false;
        }

        /// <summary>루트 시전의 공통 흐름이다. target이 있으면 적을 원점·대상으로 하고 없으면 플레이어 조준으로 시전한다.</summary>
        private bool BeginRootCast(CompiledSpell spell, int slot, SimulationEnemy target)
        {
            if (spell == null || slot < 0 || slot >= _loadout.Length || (_world.Stage != MissionStage.Bench && _world.Stage != MissionStage.Combat && _world.Stage != MissionStage.RoundBreak)) return false;
            bool isEnemyCast = target != null;
            if (!isEnemyCast && (_world.Player.Cooldowns[slot] > 0.000001 || _world.Player.IsSpellRunning(slot))) return false;
            double executionSeconds = spell.GetEstimatedExecutionSeconds(_world.Balance.Sim.ExecutionTimeScale);
            int cycleTicks = spell.GetSlotCycleTicks(_world.Balance.Sim.ExecutionTimeScale);
            double cooldownSeconds = spell.Trigger == SpellGrammar.TRIGGER_ON_ATTACK && BeamChannelController.HasDirectBeam(spell)
                ? cycleTicks / (double)RuneSimulation.TICK_RATE : spell.Cooldown + executionSeconds;
            if (!isEnemyCast && !_world.Player.TryBeginSpell(slot, executionSeconds, cooldownSeconds)) return false;
            RecordNode(spell.CoreNodeId);
            if (!isEnemyCast) _beam.ClearQueued(slot);
            SimVector delta = _aimPoint.HasValue ? _aimPoint.Value - _world.Player.Position : SimVector.Zero;
            SimVector targetDirection = delta.LengthSquared > 0.000001 ? delta.Normalized() : _world.Player.AimDirection;
            // 적 대상 시전은 적 위치를 원점으로 하고 플레이어 시전은 지팡이 끝을 원점으로 한다.
            SimVector origin = isEnemyCast ? target.Position
                : _world.Player.Position + _world.Player.AimDirection * (_world.Sector.PlayerRadius + _world.Balance.Sim.WandOffset);
            SimVector direction = isEnemyCast ? (target.Position - _world.Player.Position).Normalized() : _world.Player.AimDirection;
            var rootCast = new RootSpellExecution(slot, spell, cycleTicks, _world.Tick + cycleTicks,
                _world.Player.Position, isEnemyCast ? direction : targetDirection,
                isEnemyCast ? (SimVector?)null : _aimPoint ?? _world.Player.Position + targetDirection * _runtime.GetAreaPlacementRange());
            // 비용 결제 전 시점의 최대 전력과 대시 직후 조건 보너스를 합산해 시전 피해 배율을 정한다.
            double castDamageMultiplier = 1d
                + (_world.Player.Energy >= _world.Player.MaxEnergy - 0.000001 ? _world.Upgrades.FullManaDamageBonus : 0d)
                + (_world.Time - _world.Player.LastDashStartTime <= _world.Balance.Player.PostDashBuffSeconds ? _world.Upgrades.PostDashDamageBonus : 0d);
            double costMultiplier = spell.Trigger == SpellGrammar.TRIGGER_ON_DASH_END ? 0d : 1d;
            bool isStatusStackCast = spell.Trigger == SpellGrammar.TRIGGER_ON_STATUS_STACK;
            // 적에게서 발생한 트리거는 후속 배치에서도 적 위치를 유지한다.
            var context = new SpellContext(origin, direction, target, isEnemyCast, null,
                caster: _world.Player, randomState: new SpellRandomState(), castDamageMultiplier: castDamageMultiplier, costMultiplier: costMultiplier,
                isManaPrepaid: isStatusStackCast, suppressesTriggers: isStatusStackCast);
            var tracker = isEnemyCast ? new ExecutionTracker(null, null)
                : new ExecutionTracker(rootCast, () => CompleteRootSpell(rootCast));
            ExecuteRootBranches(spell.Root, context, tracker);
            if (!isEnemyCast) RecordRootCastPayment(rootCast);
            return true;
        }

        /// <summary>선택 Trigger와 일치하는 슬롯 마법을 시전하고 성공 여부를 반환한다.</summary>
        private bool TryCastTriggered(CompiledSpell spell, string trigger, int slot)
        {
            if (spell == null || spell.Trigger != trigger) return false;
            return TryCast(spell, slot, trigger == SpellGrammar.TRIGGER_ON_ATTACK);
        }

        /// <summary>루트 노드와 예약 분기가 끝나면 실행 잠금만 해제하고 시전 시 시작한 슬롯 쿨다운은 유지한다.</summary>
        private void CompleteRootSpell(RootSpellExecution rootCast)
        {
            _world.Player.FinishSpell(rootCast.Slot);
        }

        /// <summary>전투 중단 시 예약 노드 실행을 지우고 진행 중 시전 상태를 환급 없이 취소한다.</summary>
        internal void CancelPendingSpellExecutions()
        {
            CancelBeamChannels();
            _scheduled.Clear();
            _world.Player.CancelSpellExecutions();
        }

        /// <summary>지정 Trigger를 가진 슬롯을 순서대로 시전해 입력 이벤트 실행을 결정적으로 유지한다.</summary>
        internal void TryCastTrigger(string trigger)
        {
            for (int slot = 0; slot < _loadout.Length; slot++) TryCastTriggered(_loadout[slot], trigger, slot);
        }

        /// <summary>현재 틱에 도달한 노드 완료와 폭발 회차 예약을 등록 순서대로 적용한다.</summary>
        internal void RunScheduled()
        {
            for (int i = 0; i < _scheduled.Count;)
            {
                ScheduledExecution pending = _scheduled[i];
                if (pending.Tick > _world.Tick) { i++; continue; }
                _scheduled.RemoveAt(i);
                if (pending.IsBranchStart)
                {
                    Execute(new[] { pending.Action }, pending.Context, pending.Tracker);
                    pending.Tracker.Release();
                }
                else if (pending.Wave > 0) RunSequenceWave(pending);
                else CompleteAction(pending.Action, pending.Context, pending.Tracker);
            }
        }

        /// <summary>폭발 회차 예약을 위치·조준 재계산과 함께 비용 없이 실행하고 완료 추적을 해제한다.</summary>
        private void RunSequenceWave(ScheduledExecution pending)
        {
            pending.Tracker.BeginDispatch();
            try
            {
                SpellContext context = GetEffectContext(pending.Context, pending.Tracker);
                SpawnForm(pending.Action, context, pending.Tracker, pending.Wave);
            }
            finally
            {
                pending.Tracker.Release();
                pending.Tracker.EndDispatch();
            }
        }

        /// <summary>직접 빔은 주기 경계에서 결제·연장하고 다른 루트 분기는 기존 0.05초 간격으로 시작한다.</summary>
        private void ExecuteRootBranches(IReadOnlyList<SpellAction> actions, SpellContext context, ExecutionTracker tracker)
        {
            tracker.BeginDispatch();
            try
            {
                int intervalTicks = GetExecutionDelayTicks(CompiledSpell.ROOT_BRANCH_INTERVAL_SECONDS);
                for (int index = 1; index < actions.Count; index++)
                {
                    if (tracker.RootCast != null && tracker.RootCast.IsSlotCycleBeam(actions[index]))
                        continue;
                    if (_scheduled.Count >= _world.Balance.Limits.MaxScheduledExecutions)
                    {
                        _droppedExecutions++;
                        continue;
                    }
                    tracker.Acquire();
                    _scheduled.Add(new ScheduledExecution(_world.Tick + intervalTicks * index, actions[index], context, tracker, true));
                }
                if (actions.Count > 0)
                {
                    if (tracker.RootCast != null && tracker.RootCast.IsSlotCycleBeam(actions[0]))
                        ExecuteBeamCycle(actions[0], context, tracker, 0);
                    else Execute(new[] { actions[0] }, context, tracker);
                }
                for (int index = 1; index < actions.Count; index++)
                    if (tracker.RootCast != null && tracker.RootCast.IsSlotCycleBeam(actions[index]))
                        ExecuteBeamCycle(actions[index], context, tracker, intervalTicks * index);
            }
            finally
            {
                tracker.EndDispatch();
            }
        }

        /// <summary>직접 빔은 원본 구간 비용에 현재 충전 마나 배율을 적용해 갱신한다. 최초 비용은 원래대로 결제하고 생성 예약한다.</summary>
        private void ExecuteBeamCycle(SpellAction action, SpellContext context, ExecutionTracker tracker, int branchDelayTicks)
        {
            if (_actionBudgetTick != _world.Tick) { _actionBudgetTick = _world.Tick; _actionsThisTick = 0; }
            if (_actionsThisTick >= _world.Balance.Limits.MaxActionsPerTick)
            {
                _droppedExecutions++;
                return;
            }
            _actionsThisTick++;
            RootSpellExecution root = tracker.RootCast;
            BeamBehaviorRuntime.BeamChannel active = _beam.FindActiveCycleChannel(action, root.Slot);
            int delayTicks = branchDelayTicks + GetExecutionDelayTicks(GetExecutionSeconds(action, context));
            if (active == null && delayTicks > 0 && _scheduled.Count >= _world.Balance.Limits.MaxScheduledExecutions)
            {
                _droppedExecutions++;
                return;
            }
            ResourceCostSet costs = active != null ? active.CycleCosts
                : root.Spell.GetBeamCycleCosts(action, _world.Balance.Sim.ExecutionTimeScale,
                    context.Modifiers, context.CostMultiplier, context.IsManaPrepaid);
            if (active != null)
                costs = costs.WithAmount("mana", costs.GetAmount("mana") * active.GetManaMultiplier(_world.Tick));
            if (!_beam.TrySpendBeamCosts(costs))
            {
                active?.Stop();
                RecordRootFirstActionPayment(tracker, action, isPaid: false);
                return;
            }
            RecordRootFirstActionPayment(tracker, action, isPaid: true);
            RecordNode(action.NodeId);
            if (active != null)
            {
                active.Renew(root.CycleEndTick);
                foreach (string nodeId in action.AttachedNodeIds) RecordNode(nodeId);
                return;
            }
            root.SetBeamCycleCosts(action, costs);
            tracker.Acquire();
            if (delayTicks == 0) CompleteAction(action, context, tracker);
            else _scheduled.Add(new ScheduledExecution(_world.Tick + delayTicks, action, context, tracker));
        }

        /// <summary>이벤트로 시작한 후속 분기를 슬롯과 분리된 실행 추적기로 시작한다.</summary>
        internal void Execute(IReadOnlyList<SpellAction> actions, SpellContext context)
        {
            Execute(actions, context, new ExecutionTracker(null, null));
        }

        /// <summary>실행 명령의 병렬 분기를 시작하고 노드 시간 종료 예약을 추적기에 연결한다.</summary>
        private void Execute(IReadOnlyList<SpellAction> actions, SpellContext context, ExecutionTracker tracker)
        {
            tracker.BeginDispatch();
            if (_actionBudgetTick != _world.Tick) { _actionBudgetTick = _world.Tick; _actionsThisTick = 0; }
            try
            {
                for (int i = 0; i < actions.Count; i++)
                {
                    if (_actionsThisTick >= _world.Balance.Limits.MaxActionsPerTick)
                    {
                        _droppedExecutions += actions.Count - i;
                        break;
                    }
                    _actionsThisTick++;
                    SpellAction action = actions[i];
                    double seconds = GetExecutionSeconds(action, context);
                    int durationTicks = GetExecutionDelayTicks(seconds);
                    if (durationTicks > 0 && _scheduled.Count >= _world.Balance.Limits.MaxScheduledExecutions)
                    {
                        _droppedExecutions++;
                        continue;
                    }
                    if (!TrySpendFor(action, context))
                    {
                        RecordRootFirstActionPayment(tracker, action, isPaid: false);
                        continue;
                    }
                    RecordRootFirstActionPayment(tracker, action, isPaid: true);
                    RecordNode(action.NodeId);
                    tracker.Acquire();
                    if (durationTicks == 0) CompleteAction(action, context, tracker);
                    else _scheduled.Add(new ScheduledExecution(_world.Tick + durationTicks, action, context, tracker));
                }
            }
            finally
            {
                tracker.EndDispatch();
            }
        }

        /// <summary>지정 노드가 시작된 시점의 배율을 적용한 실행 시간을 반환한다.</summary>
        private double GetExecutionSeconds(SpellAction action, SpellContext context)
        {
            double seconds = action.GetExecutionSeconds(context.Modifiers);
            return seconds * _world.Balance.Sim.ExecutionTimeScale;
        }

        /// <summary>노드 실행 시간을 60Hz 완료 틱으로 올림하고 0이면 같은 틱 실행을 반환한다.</summary>
        private static int GetExecutionDelayTicks(double seconds)
        {
            if (seconds <= 0) return 0;
            double ticks = Math.Ceiling(seconds * RuneSimulation.TICK_RATE - 0.000000001);
            return (int)Math.Min(int.MaxValue / 4, Math.Max(1, ticks));
        }

        /// <summary>노드가 시작할 때 자원을 차감하고 실행 시간이 끝난 시점에 효과를 적용한다.</summary>
        private void CompleteAction(SpellAction action, SpellContext context, ExecutionTracker tracker)
        {
            tracker.BeginDispatch();
            try
            {
                context = GetEffectContext(context, tracker);
                switch (action.Kind)
                {
                    case "spawn": SpawnForm(action, context, tracker); break;
                }
            }
            finally
            {
                tracker.Release();
                tracker.EndDispatch();
            }
        }

        /// <summary>루트 시전은 효과 적용 시점의 지팡이 끝과 조준을 쓰고 적 대상 시전과 이벤트 체인은 시전 시점 문맥을 보존한다.</summary>
        private SpellContext GetEffectContext(SpellContext context, ExecutionTracker tracker)
        {
            if (context.FromEvent || tracker.RootCast == null || context.Target != null) return context;
            SimVector aim = _world.Player.AimDirection;
            SimVector origin = _world.Player.Position + aim * (_world.Sector.PlayerRadius + _world.Balance.Sim.WandOffset);
            return context.WithOriginAndDirection(origin, aim.Rotated(context.DirectionOffset));
        }

        /// <summary>첫 Noise 효과가 적용되는 순간 원소를 결정하고 같은 루트의 다른 Noise 노드와 공유한다.</summary>
        private string ChooseNoiseElement(SpellContext context)
        {
            if (context.NoiseElement != null) return context.NoiseElement;
            int index = Math.Min(_unlockedElements.Count - 1, (int)(_world.Random.Next() * _unlockedElements.Count));
            string element = _unlockedElements[index];
            context.RandomState.SetNoiseElement(element);
            return element;
        }

        /// <summary>
        /// 루트 시전 시도와 첫 행동 자원 지불 결과를 통계에 기록한다. 지불 지점 전에 틱당·예약 상한으로 버려진 시전은 시도에만 포함한다.
        /// 첫 실패 틱은 이 지점의 _world.Tick으로 확정한다.
        /// </summary>
        private void RecordRootCastPayment(RootSpellExecution rootCast)
        {
            _stats.RootCastAttempts++;
            if (rootCast.FirstActionPayment > 0) _stats.RootCastSuccesses++;
            else if (rootCast.FirstActionPayment < 0)
            {
                _stats.RootCastFailures++;
                if (_stats.FirstRootCastFailureTick < 0) _stats.FirstRootCastFailureTick = _world.Tick;
            }
        }

        /// <summary>루트 첫 행동의 자원 지불 결과를 루트 실행 플래그에 기록한다. 이벤트 후속과 루트의 예약 분기는 첫 행동이 아니므로 제외된다.</summary>
        private static void RecordRootFirstActionPayment(ExecutionTracker tracker, SpellAction action, bool isPaid)
        {
            RootSpellExecution root = tracker.RootCast;
            if (root != null && root.IsFirstRootAction(action)) root.RecordFirstActionPayment(isPaid);
        }

        /// <summary>
        /// 최초 행동의 onHit 전력 선결제와 나머지 노드 자원 비용을 함께 차감하고 성공 여부를 반환한다. 선결제 후속은 전력을 다시 내지 않는다.
        /// 비용이 0이면 항상 성공한다.
        /// </summary>
        private bool TrySpendFor(SpellAction action, SpellContext context)
        {
            ResourceCostSet costs = action.GetExecutionCosts(context.Modifiers, context.CostMultiplier,
                context.IsManaPrepaid);
            if (costs.Amounts.Count == 0) return true;
            if (!_world.Player.TrySpend(costs)) return false;
            foreach (ResourceAmount amount in costs.Amounts)
            {
                _stats.AddResourceCost(amount);
            }
            return true;
        }

        /// <summary>
        /// 행동 실행 동작에 따라 개체 수만큼 배치·예열 시간을 정해 마법 개체를 생성하고 생성 직후 처리를 실행한다.
        /// 배치는 배치 모드(행동 배치 전) → 행동 PlaceSpawn → 배치 모드(행동 배치 후) 순서다. 이벤트 앵커와 노이즈 속성을 반영한다.
        /// wave가 0이면 첫 회차로 부착 노드를 기록하고 폭발에 장착한 다중의 남은 회차를 예약하며, 0보다 크면 기록·재예약 없이 개체만 다시 만든다.
        /// </summary>
        private void SpawnForm(SpellAction action, SpellContext context, ExecutionTracker tracker, int wave = 0)
        {
            if (wave == 0)
            {
                foreach (string attachedNodeId in action.AttachedNodeIds) RecordNode(attachedNodeId);
                ScheduleWaves(action, context, tracker);
            }
            string element = action.Noise ? ChooseNoiseElement(context) : action.Element;
            SpellStats stats = action.Stats.Apply(context.Modifiers);
            SpellBehaviorRuntime behavior = SpellBehaviorRuntimeRegistry.Get(action.Form);
            SpellModifierRuntime[] modifiers = SpellModifierRuntimeRegistry.Resolve(action.ModifierRules);
            BeamBehaviorRuntime.BeamChannel channel = null;
            if (action.Form == BeamBehaviorRules.BEAM_KIND && action.Count > 0 && _world.SpellEntities.Count < _world.Balance.Limits.MaxLiveSpellEntities)
            {
                RootSpellExecution root = tracker.RootCast;
                bool isDirect = !context.FromEvent && root != null && root.IsDirectBeam(action);
                bool isSlotCycle = isDirect && root.IsSlotCycleBeam(action);
                if (isDirect && !isSlotCycle) tracker.Acquire();
                channel = new BeamBehaviorRuntime.BeamChannel(_world.Tick, stats, root?.Slot ?? -1,
                    isDirect, action, isSlotCycle, root?.CycleEndTick ?? 0,
                    isSlotCycle ? root.GetBeamCycleCosts(action) : ResourceCostSet.Empty,
                    isDirect && !isSlotCycle ? (Action)(() => _beam.CompleteBeamChannel(tracker)) : null);
                _beam.AddChannel(channel);
            }
            for (int i = 0; i < action.Count; i++)
            {
                if (_world.SpellEntities.Count >= _world.Balance.Limits.MaxLiveSpellEntities) return;
                SimVector direction = context.Direction;
                SimVector position = context.Origin;
                if (action.Form == SlashBehaviorRules.KIND && !context.FromEvent) direction = context.Caster.AimDirection;
                foreach (SpellModifierRuntime modifier in modifiers) modifier.BeforePlaceSpawn(_runtime, action, stats, i, ref position, ref direction);
                behavior.PlaceSpawn(_runtime, action, stats, i, context.FromEvent, context.Caster, ref position, ref direction);
                foreach (SpellModifierRuntime modifier in modifiers) modifier.AfterPlaceSpawn(_runtime, action, stats, i, ref position, ref direction);
                RootSpellExecution targetCast = tracker.RootCast;
                if (!context.FromEvent && targetCast != null && targetCast.AimPoint.HasValue
                    && RuneSimulation.IsAreaPreviewAction(action) && targetCast.IsDirectAction(action))
                {
                    position = AreaTargeting.GetAreaTargetPosition(_runtime, action, targetCast.AimPoint.Value, i, targetCast.PlayerPosition);
                    direction = targetCast.AimDirection;
                }
                // 개체 순번 각도다. 공전 모드가 궤도 자리를 등각으로 나누는 데 쓴다.
                double angle = i * 2 * Math.PI / action.Count;
                double warmup = behavior.GetWarmupSeconds(stats);
                var entity = new SimulationSpellEntity(_world.AllocateEntityId(), action, stats, element, position, direction,
                    context.FromEvent, context.Origin, angle, context.NoiseElement, _world.Balance.Sim.SpellVisualSeconds, warmup,
                    context.Modifiers, context.CostMultiplier, context.Caster, context.CastDamageMultiplier,
                    action.Form == SlashBehaviorRules.KIND ? _world.Upgrades.HasFlyingSlash
                        : action.Form == SpellGrammar.FORM_BOLT ? _world.Upgrades.HasReturningLaunch
                        : action.Form == SpellGrammar.FORM_BURST && _world.Upgrades.HasReturningBurst,
                    suppressesTriggers: context.SuppressesTriggers);
                if (entity.BehaviorState is SlashBehaviorRuntime.SlashState slash) slash.SetAnchorTarget(context.Target);
                _stats.RecordActionSpawn(action, context);
                _world.SpellEntities.Add(entity); _stats.PeakSpellEntities = Math.Max(_stats.PeakSpellEntities, _world.SpellEntities.Count);
                if (action.Form == SpellGrammar.FORM_BOLT) _stats.ShotLaunchEventCount++;
                else if (action.Form == SpellGrammar.FORM_BURST) _stats.ShotBurstEventCount++;
                else if (action.Form == SpellGrammar.FORM_ZONE) _stats.ShotPersistEventCount++;
                else if (action.Form == SlashBehaviorRules.KIND) _stats.ShotSlashEventCount++;
                if (channel != null) ((BeamBehaviorRuntime.BeamState)entity.BehaviorState).Attach(channel);
                behavior.OnSpawned(_runtime, entity);
            }
        }

        /// <summary>
        /// 폭발에 장착한 다중이 붙은 spawn의 남은 회차를 회차 간격 뒤로 예약한다. 빔에는 적용하지 않으며,
        /// 예약 상한을 넘은 회차는 추적을 걸지 않고 버리고 dropped 실행 수만 센다.
        /// </summary>
        private void ScheduleWaves(SpellAction action, SpellContext context, ExecutionTracker tracker)
        {
            if (action.WaveCount <= 1 || action.Form == BeamBehaviorRules.BEAM_KIND) return;
            int waveTicks = Math.Max(1, GetExecutionDelayTicks(action.WaveInterval * _world.Balance.Sim.ExecutionTimeScale));
            for (int wave = 1; wave < action.WaveCount; wave++)
            {
                if (_scheduled.Count >= _world.Balance.Limits.MaxScheduledExecutions)
                {
                    _droppedExecutions++;
                    continue;
                }
                tracker.Acquire();
                _scheduled.Add(new ScheduledExecution(_world.Tick + waveTicks * wave, action, context, tracker, wave));
            }
        }

        /// <summary>도크·장착·전투가 바뀔 때 빔 실행 추적과 입력 권한을 이벤트 없이 정리한다.</summary>
        internal void CancelBeamChannels()
        {
            _beam.StopAndClearChannels();
            for (int index = _scheduled.Count - 1; index >= 0; index--)
            {
                ScheduledExecution pending = _scheduled[index];
                if (pending.Action?.Form != BeamBehaviorRules.BEAM_KIND) continue;
                _scheduled.RemoveAt(index);
                pending.Tracker.Release();
            }
            for (int index = _world.SpellEntities.Count - 1; index >= 0; index--)
                if (_world.SpellEntities[index].Kind == BeamBehaviorRules.BEAM_KIND) _world.SpellEntities.RemoveAt(index);
            _beam.CancelBeamInput();
        }

        /// <summary>노드 실행 틱을 기록하고 누적 실행 수를 증가시킨다.</summary>
        private void RecordNode(string nodeId) { _feedback.AddNodeEvent(nodeId); _stats.NodeExecutionCount++; }
    }
}
