using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>화면 표시 전용 버퍼를 소유하는 기록 시스템이다. 피해 판정과 상태 해시 계산에는 쓰이지 않는다.</summary>
    internal sealed class SimulationFeedback
    {
        private const int MAX_BEAM_FLASHES = 128;
        private readonly SimulationWorld _world;
        private readonly List<DamageNumber> _damageNumbers = new List<DamageNumber>();
        private readonly List<HealPulse> _healPulses = new List<HealPulse>();
        private readonly List<ArcLink> _arcLinks = new List<ArcLink>();
        private readonly List<SimulationBeamFlash> _beamFlashes = new List<SimulationBeamFlash>();
        private readonly List<NodeExecutionEvent> _nodeEvents = new List<NodeExecutionEvent>();
        private readonly List<SpawnWarning> _spawnWarnings = new List<SpawnWarning>();
        private int _beamFlashGeneration;

        /// <summary>피해 숫자 표시 목록이다. 같은 리스트 인스턴스를 반환한다.</summary>
        internal IReadOnlyList<DamageNumber> DamageNumbers => _damageNumbers;
        /// <summary>체력 회복 표시 목록이다. 같은 리스트 인스턴스를 반환한다.</summary>
        internal IReadOnlyList<HealPulse> HealPulses => _healPulses;
        /// <summary>전격 연쇄 구간 표시 목록이다. 같은 리스트 인스턴스를 반환한다.</summary>
        internal IReadOnlyList<ArcLink> ArcLinks => _arcLinks;
        /// <summary>짧은 빔의 최초 경로 표시 목록이다. 같은 리스트 인스턴스를 반환한다.</summary>
        internal IReadOnlyList<SimulationBeamFlash> BeamFlashes => _beamFlashes;
        /// <summary>노드 실행 이벤트 표시 목록이다. 같은 리스트 인스턴스를 반환한다.</summary>
        internal IReadOnlyList<NodeExecutionEvent> NodeEvents => _nodeEvents;
        /// <summary>맵형 스폰 예고 표시 목록이다. 예고 갱신이 인덱스로 읽고 덮어쓰므로 같은 리스트 인스턴스를 그대로 노출한다.</summary>
        internal List<SpawnWarning> SpawnWarnings => _spawnWarnings;
        /// <summary>도크 초기화·전투 취소 후 이전 표시 기록을 구분하는 세대 번호다.</summary>
        internal int BeamFlashGeneration => _beamFlashGeneration;

        /// <summary>틱·밸런스 조회에 쓸 공유 월드를 받아 표시 기록 시스템을 만든다.</summary>
        internal SimulationFeedback(SimulationWorld world) { _world = world; }

        /// <summary>피해 숫자 표시를 목록 끝에 추가한다.</summary>
        internal void AddDamageNumber(SimVector position, double amount, string element)
            => _damageNumbers.Add(new DamageNumber(position, amount, element, _world.Tick));

        /// <summary>체력 회복 표시를 목록 끝에 추가한다.</summary>
        internal void AddHealPulse(SimVector position)
            => _healPulses.Add(new HealPulse(position, _world.Tick));

        /// <summary>전격 연쇄 구간 표시를 목록 끝에 추가한다.</summary>
        internal void AddArcLink(SimVector from, SimVector to)
            => _arcLinks.Add(new ArcLink(from, to, _world.Tick));

        /// <summary>노드 실행 이벤트 표시를 목록 끝에 추가한다.</summary>
        internal void AddNodeEvent(string nodeId)
            => _nodeEvents.Add(new NodeExecutionEvent(nodeId, _world.Tick));

        /// <summary>첫 판정 빔의 경로를 표시 전용으로 복사하고 최근 기록 수를 제한한다.</summary>
        internal void RecordBeamFlash(SimulationSpellEntity entity, IReadOnlyList<SimVector> path)
        {
            if (path.Count < 2) return;
            if (_beamFlashes.Count >= MAX_BEAM_FLASHES) _beamFlashes.RemoveAt(0);
            _beamFlashes.Add(new SimulationBeamFlash(entity, path, _world.Tick));
        }

        /// <summary>세대 번호를 올리고 빔 경로 표시를 모두 비운다. 전투 취소가 이전 빔 기록을 구분할 때 쓴다.</summary>
        internal void CancelBeamFlashes()
        {
            _beamFlashGeneration++;
            _beamFlashes.Clear();
        }

        /// <summary>빔 경로 표시만 비운다. 세대 번호는 그대로 둔다.</summary>
        internal void ClearBeamFlashes() => _beamFlashes.Clear();

        /// <summary>시험 도크 초기화에 맞춰 피해 숫자·체력 회복·전격 연쇄·노드 이벤트 표시를 비운다.</summary>
        internal void ClearForBench()
        {
            _damageNumbers.Clear();
            _healPulses.Clear();
            _arcLinks.Clear();
            _nodeEvents.Clear();
        }

        /// <summary>맵형 스폰 예고 표시를 모두 비운다.</summary>
        internal void ClearSpawnWarnings() => _spawnWarnings.Clear();

        /// <summary>표시 수명이 지난 피해 숫자, 체력 회복 표시, 전격 연쇄 구간, 노드 이벤트를 제거한다.</summary>
        internal void Trim()
        {
            for (int i = _damageNumbers.Count - 1; i >= 0; i--) if (_world.Tick - _damageNumbers[i].Tick > _world.Balance.Sim.DamageNumberSeconds * RuneSimulation.TICK_RATE) _damageNumbers.RemoveAt(i);
            for (int i = _healPulses.Count - 1; i >= 0; i--) if (_world.Tick - _healPulses[i].Tick > _world.Balance.Sim.DamageNumberSeconds * RuneSimulation.TICK_RATE) _healPulses.RemoveAt(i);
            for (int i = _arcLinks.Count - 1; i >= 0; i--) if (_world.Tick - _arcLinks[i].Tick > RuneSimulation.ARC_LINK_TICKS) _arcLinks.RemoveAt(i);
            for (int i = _nodeEvents.Count - 1; i >= 0; i--) if (_world.Tick - _nodeEvents[i].Tick > RuneSimulation.TICK_RATE) _nodeEvents.RemoveAt(i);
        }
    }
}
