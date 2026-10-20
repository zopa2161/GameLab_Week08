using System.Collections.Generic;
using System.Text;

namespace RuneCode
{
    /// <summary>슬롯과 컴파일 마법을 루트 체인 완료 시점까지 연결한다.</summary>
    internal sealed class RootSpellExecution
    {
        private readonly int _slot;
        private readonly IReadOnlyList<SpellAction> _actions;
        private readonly CompiledSpell _spell;
        private readonly int _cycleTicks;
        private readonly int _cycleEndTick;
        private readonly Dictionary<SpellAction, ResourceCostSet> _beamCycleCosts = new Dictionary<SpellAction, ResourceCostSet>();
        private readonly SimVector _playerPosition;
        private readonly SimVector _aimDirection;
        private readonly SimVector? _aimPoint;
        public int Slot => _slot;
        public CompiledSpell Spell => _spell;
        public int CycleEndTick => _cycleEndTick;
        public SimVector PlayerPosition => _playerPosition;
        public SimVector AimDirection => _aimDirection;
        public SimVector? AimPoint => _aimPoint;

        /// <summary>슬롯·실행 루트·공통 주기와 종료 틱, 시전 시작 위치·조준 방향·포인터 좌표를 보관한다.</summary>
        public RootSpellExecution(int slot, CompiledSpell spell, int cycleTicks, int cycleEndTick,
            SimVector playerPosition, SimVector aimDirection, SimVector? aimPoint)
        {
            _slot = slot; _actions = spell.Root; _spell = spell;
            _cycleTicks = cycleTicks; _cycleEndTick = cycleEndTick;
            _playerPosition = playerPosition; _aimDirection = aimDirection; _aimPoint = aimPoint;
        }

        /// <summary>명령이 슬롯 주기로 결제·연장할 직접 공격 빔인지 반환한다.</summary>
        public bool IsSlotCycleBeam(SpellAction action) => _spell.IsSlotCycleBeam(action);

        /// <summary>최초 생성 예약에 사용할 빔의 결제 완료 구간 비용을 저장한다.</summary>
        public void SetBeamCycleCosts(SpellAction action, ResourceCostSet costs) => _beamCycleCosts[action] = costs;

        /// <summary>최초 결제한 빔 구간 비용을 생성 시 채널에 전달한다.</summary>
        public ResourceCostSet GetBeamCycleCosts(SpellAction action) => _beamCycleCosts[action];

        /// <summary>명령이 시작 노드에 직접 연결된 실행 대상인지 반환한다.</summary>
        public bool IsDirectAction(SpellAction action)
        {
            foreach (SpellAction direct in _actions)
                if (ReferenceEquals(direct, action)) return true;
            return false;
        }

        /// <summary>예약된 범위 마법의 생성 위치·방향을 결정하는 시전 시점 상태를 해시에 기록한다.</summary>
        public void WriteTargetState(StringBuilder state)
        {
            state.Append('|').Append(_aimPoint.HasValue);
            state.Append(':').Append(_cycleTicks).Append(':').Append(_cycleEndTick);
            SimHash.AppendNumber(state, _playerPosition.X); SimHash.AppendNumber(state, _playerPosition.Y);
            SimHash.AppendNumber(state, _aimDirection.X); SimHash.AppendNumber(state, _aimDirection.Y);
            if (_aimPoint.HasValue) { SimHash.AppendNumber(state, _aimPoint.Value.X); SimHash.AppendNumber(state, _aimPoint.Value.Y); }
        }

        /// <summary>행동 인스턴스가 시작 조건에 직접 연결된 빔인지 검사한다.</summary>
        public bool IsDirectBeam(SpellAction action)
        {
            if (action.Form != BeamBehaviorRules.BEAM_KIND) return false;
            return IsDirectAction(action);
        }
    }
}
