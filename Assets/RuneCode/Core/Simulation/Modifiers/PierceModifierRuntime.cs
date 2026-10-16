using System;

namespace RuneCode
{
    /// <summary>
    /// 관통 모드의 적중 동작이다. 적중마다 직전 관통 피해에 유지 배율을 곱하고, 관통 수를 넘을 때까지 소멸하지 않게 한다.
    /// </summary>
    internal sealed class PierceModifierRuntime : SpellModifierRuntime
    {
        public override string RuneId => PierceModifierRules.RUNE_ID;

        /// <summary>앞선 적중 수만큼 피해 유지 배율을 누적 곱해 최소 1의 피해를 계산하고 관통 수를 넘는 적중에서만 소멸시킨다.</summary>
        public override void OnHit(RuneSimulation simulation, SimulationSpellEntity entity, object state, SimulationEnemy enemy,
            ref double damage, ref bool isConsumed)
        {
            SpellStats stats = entity.Stats;
            damage = Math.Max(1d, damage * Math.Pow(1d - stats.PierceLoss, entity.Hits));
            isConsumed = entity.Hits + 1 > stats.Pierce;
        }
    }
}
