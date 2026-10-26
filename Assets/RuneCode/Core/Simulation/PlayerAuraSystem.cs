using System;
using System.Text;

namespace RuneCode
{
    /// <summary>
    /// 강화 트리의 플레이어 주위 지속 장판을 마법 시스템과 별개로 처리한다.
    /// 피해 판정은 상태 이상 시스템과 같이 시뮬레이션 위임(ApplyDamage)으로 부른다.
    /// </summary>
    internal sealed class PlayerAuraSystem
    {
        private readonly SimulationWorld _world;
        private readonly RuneSimulation _simulation;

        private double _nextTickTime;

        /// <summary>공유 월드와 피해 판정을 호출할 시뮬레이션으로 장판 시스템을 만든다.</summary>
        internal PlayerAuraSystem(SimulationWorld world, RuneSimulation simulation)
        {
            _world = world;
            _simulation = simulation;
        }

        /// <summary>장판 강화가 켜져 있으면 한 틱만큼 장판을 진행한다. 꺼져 있으면 아무것도 하지 않는다.</summary>
        internal void Advance()
        {
            if (!_world.Upgrades.HasPlayerAura) return;
            // 화상 틱과 같이 판정 시각 비교에 여유를 둔다. 틱은 스텝마다 한 번씩만 진행하므로 단순 판정으로 충분하다.
            if (_world.Time + 0.000001 < _nextTickTime) return;
            CombatBalance combat = _world.Balance.Combat;
            _nextTickTime = _world.Time + combat.AuraTickInterval;
            foreach (SimulationEnemy enemy in _world.Enemies)
            {
                if (!enemy.IsAlive) continue;
                if (SimVector.Distance(_world.Player.Position, enemy.Position) > combat.AuraRadius + enemy.Radius) continue;
                _simulation.ApplyDamage(enemy, combat.AuraTickDamage, "raw", "raw", false, true, SimVector.Zero, false);
            }
        }

        /// <summary>시험 도크 초기화로 시뮬레이션 시각이 0으로 돌아갈 때 다음 틱 시각도 처음 상태로 되돌린다.</summary>
        internal void ResetForBench() => _nextTickTime = 0;

        /// <summary>장판의 다음 틱 시각을 결정성 해시 버퍼에 기록한다. 장판이 꺼져 있으면 항상 0이므로 해시 비교에 영향이 없다.</summary>
        internal void WriteState(StringBuilder state)
        {
            state.Append(FormattableString.Invariant($"|auraNext:{_nextTickTime:R}"));
        }
    }
}
