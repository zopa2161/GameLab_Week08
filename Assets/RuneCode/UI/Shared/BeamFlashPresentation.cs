using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>전투 화면과 도크에서 최초 빔 경로를 읽어 비전투 시간 동안 잠깐 유지하는 표시 상태다.</summary>
    internal sealed class BeamFlashPresentation
    {
        private const float DISPLAY_SECONDS = 0.08f;
        private const int MAX_CAPTURE_AGE_TICKS = RuneSimulation.TICK_RATE / 2;

        private readonly List<SimulationBeamFlash> _flashes = new List<SimulationBeamFlash>();
        private readonly List<float> _expiresAt = new List<float>();
        private RuneSimulation _observedSimulation;
        private int _lastEntityId;
        private int _lastTick;
        private int _observedGeneration;

        public IReadOnlyList<SimulationBeamFlash> Flashes => _flashes;

        /// <summary>새 시뮬레이션·도크 초기화·종료 시 표시 기록을 비우고 최근 첫 빔을 수집하며 변경 여부를 반환한다.</summary>
        public bool Update(RuneSimulation simulation, float now)
        {
            bool changed = false;
            if (_observedSimulation != simulation || simulation.Tick < _lastTick
                || _observedGeneration != simulation.BeamFlashGeneration)
            {
                _observedSimulation = simulation;
                _observedGeneration = simulation.BeamFlashGeneration;
                _lastEntityId = 0;
                changed = _flashes.Count > 0;
                _flashes.Clear();
                _expiresAt.Clear();
            }
            _lastTick = simulation.Tick;
            for (int index = _flashes.Count - 1; index >= 0; index--)
            {
                if (now < _expiresAt[index] && !simulation.Completed && !simulation.IsFailed) continue;
                _flashes.RemoveAt(index);
                _expiresAt.RemoveAt(index);
                changed = true;
            }
            if (simulation.Completed || simulation.IsFailed) return changed;
            foreach (SimulationBeamFlash flash in simulation.BeamFlashes)
            {
                if (flash.EntityId <= _lastEntityId) continue;
                _lastEntityId = flash.EntityId;
                if (simulation.Tick - flash.Tick > MAX_CAPTURE_AGE_TICKS) continue;
                _flashes.Add(flash);
                _expiresAt.Add(now + DISPLAY_SECONDS);
                changed = true;
            }
            return changed;
        }

        /// <summary>동일 빔이 실제 개체 목록에 남아 있는지 반환해 정상 갱신 중 중복 표시를 피한다.</summary>
        public static bool HasLiveBeam(RuneSimulation simulation, int entityId)
        {
            foreach (SimulationSpellEntity entity in simulation.SpellEntities)
                if (entity.Id == entityId) return true;
            return false;
        }
    }
}
