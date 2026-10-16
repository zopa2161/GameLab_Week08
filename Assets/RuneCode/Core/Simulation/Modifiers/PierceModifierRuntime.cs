using System;

namespace RuneCode
{
    /// <summary>
    /// 관통 모드의 적중 동작이다. 앞서 맞힌 수만큼 피해를 관통 감소율로 줄이고, 관통 수를 넘을 때까지 소멸하지 않게 한다.
    /// </summary>
    internal sealed class PierceModifierRuntime : SpellModifierRuntime
    {
        public override string RuneId => PierceModifierRules.RUNE_ID;

        /// <summary>피해에 (1 − 앞선 적중 수 × 관통 감소율, 최소 0)을 곱하고, 이번 적중 뒤 적중 수가 관통 수를 넘을 때만 소멸시킨다.</summary>
        public override void OnHit(RuneSimulation simulation, SimulationSpellEntity entity, object state, SimulationEnemy enemy,
            ref double damage, ref bool isConsumed)
        {
            SpellStats stats = entity.Stats;
            damage *= Math.Max(0, 1 - entity.Hits * stats.PierceLoss);
            isConsumed = entity.Hits + 1 > stats.Pierce;
        }
    }
}
