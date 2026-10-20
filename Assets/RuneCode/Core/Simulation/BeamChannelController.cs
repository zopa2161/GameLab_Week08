using System.Collections.Generic;
using System.Text;

namespace RuneCode
{
    /// <summary>슬롯별 빔 홀드·예약 클릭 입력과 진행 중인 빔 채널 목록, 빔 구간 비용 결제를 관리한다.</summary>
    internal sealed class BeamChannelController
    {
        private readonly SimulationWorld _world;
        private readonly SimulationStats _stats;
        private readonly bool[] _castHeld = new bool[3];
        private readonly bool[] _beamQueued = new bool[3];
        private readonly List<BeamBehaviorRuntime.BeamChannel> _beamChannels = new List<BeamBehaviorRuntime.BeamChannel>();

        /// <summary>월드와 자원 소비 통계 참조로 만든다.</summary>
        internal BeamChannelController(SimulationWorld world, SimulationStats stats)
        {
            _world = world;
            _stats = stats;
        }

        /// <summary>공격 코어에 직접 연결된 빔이 있는지 검사해 슬롯 주기 길이를 틱으로 맞출 마법을 구분한다.</summary>
        internal static bool HasDirectBeam(CompiledSpell spell)
        {
            foreach (SpellAction action in spell.Root)
                if (action.Form == BeamBehaviorRules.BEAM_KIND) return true;
            return false;
        }

        /// <summary>슬롯별 현재 홀드와 짧은 클릭을 장착 목록 loadout 기준으로 기록한다. 해제·재클릭은 결제한 빔 구간을 취소하지 않는다.</summary>
        internal void ReadBeamInput(SimulationInput input, IReadOnlyList<CompiledSpell> loadout)
        {
            for (int slot = 0; slot < _castHeld.Length; slot++)
            {
                bool held = slot == 0 ? input.CastA : slot == 1 ? input.CastB : input.CastC;
                bool released = _castHeld[slot] && !held || slot == 0 && input.CastReleased;
                bool pressed = held && !_castHeld[slot] || slot == 0 && input.CastPressed;
                if (released)
                {
                    _beamQueued[slot] = false;
                }
                if (pressed)
                {
                    if (loadout[slot] != null && loadout[slot].Trigger == SpellGrammar.TRIGGER_ON_ATTACK
                        && HasDirectBeam(loadout[slot]))
                        _beamQueued[slot] = true;
                }
                _castHeld[slot] = held;
            }
        }

        /// <summary>슬롯에 예약 클릭된 빔 시전이 있는지 반환한다.</summary>
        internal bool IsQueued(int slot) => _beamQueued[slot];

        /// <summary>시전을 시작한 슬롯의 예약 클릭을 지운다.</summary>
        internal void ClearQueued(int slot) => _beamQueued[slot] = false;

        /// <summary>지정 슬롯에 아직 종료되지 않은 직접 시전 빔이 있는지 반환한다.</summary>
        internal bool HasActiveBeam(int slot)
        {
            foreach (BeamBehaviorRuntime.BeamChannel channel in _beamChannels)
                if (channel.Slot == slot && channel.FollowsPlayer && !channel.IsStopped) return true;
            return false;
        }

        /// <summary>직접 시전 여부와 슬롯에 관계없이 아직 종료되지 않은 빔 채널이 있는지 반환한다.</summary>
        internal bool HasAnyActiveBeam()
        {
            foreach (BeamBehaviorRuntime.BeamChannel channel in _beamChannels)
                if (!channel.IsStopped) return true;
            return false;
        }

        /// <summary>슬롯 slot에서 같은 행동 action의 결제가 현재 틱까지 유효한 슬롯 주기 채널을 등록 순서로 찾아 반환한다. 없으면 null이다.</summary>
        internal BeamBehaviorRuntime.BeamChannel FindActiveCycleChannel(SpellAction action, int slot)
        {
            foreach (BeamBehaviorRuntime.BeamChannel channel in _beamChannels)
            {
                if (!channel.IsStopped && channel.UsesSlotCycle && channel.Slot == slot
                    && ReferenceEquals(channel.Action, action) && channel.PaidUntilTick >= _world.Tick)
                {
                    return channel;
                }
            }
            return null;
        }

        /// <summary>새로 생성한 빔 채널을 목록 끝에 추가한다.</summary>
        internal void AddChannel(BeamBehaviorRuntime.BeamChannel channel) => _beamChannels.Add(channel);

        /// <summary>직접 빔 종료 시 추가 쿨다운 없이 채널의 실행 추적 참조를 해제한다.</summary>
        internal void CompleteBeamChannel(ExecutionTracker tracker)
        {
            tracker.Release();
        }

        /// <summary>정지된 빔 채널을 역순으로 제거한다. 마법 개체 진행 직후 호출한다.</summary>
        internal void RemoveStoppedChannels()
        {
            for (int index = _beamChannels.Count - 1; index >= 0; index--)
                if (_beamChannels[index].IsStopped) _beamChannels.RemoveAt(index);
        }

        /// <summary>모든 빔 채널을 이벤트 없이 정지하고 목록을 비운다.</summary>
        internal void StopAndClearChannels()
        {
            foreach (BeamBehaviorRuntime.BeamChannel channel in _beamChannels) channel.Stop();
            _beamChannels.Clear();
        }

        /// <summary>입력 중단 시 기존 빔 홀드와 예약 클릭을 취소하며 다른 행동의 입력 규칙은 유지한다.</summary>
        internal void CancelBeamInput()
        {
            for (int slot = 0; slot < _castHeld.Length; slot++)
            {
                _castHeld[slot] = false;
                _beamQueued[slot] = false;
            }
        }

        /// <summary>빔의 모든 비용 자원을 원자적으로 차감하고 기존 누적 소비 통계에 기록한다.</summary>
        internal bool TrySpendBeamCosts(ResourceCostSet costs)
        {
            if (!_world.Player.TrySpend(costs)) return false;
            foreach (ResourceAmount amount in costs.Amounts)
            {
                _stats.AddResourceCost(amount);
            }
            return true;
        }

        /// <summary>슬롯별 홀드·예약 입력을 상태 해시 문자열에 기존 형식으로 기록한다.</summary>
        internal void WriteInputState(StringBuilder state)
        {
            state.Append("beamInput:");
            for (int slot = 0; slot < _castHeld.Length; slot++)
                state.Append('|').Append(_castHeld[slot]).Append(':').Append(_beamQueued[slot]);
        }
    }
}
