namespace RuneCode
{
    /// <summary>공유 빔 채널의 연속 유지 시간에 따라 이번 적중 피해에 과충전 배율을 적용한다.</summary>
    internal sealed class BeamEmpowerModifierRuntime : SpellModifierRuntime
    {
        public override string RuneId => BeamEmpowerModifierRules.RUNE_ID;

        /// <summary>빔의 입력 피해에 현재 충전 진행률의 피해 배율을 곱한다. 소멸 상태는 바꾸지 않는다.</summary>
        public override void OnHit(RuneSimulation simulation, SimulationSpellEntity entity, object state, SimulationEnemy enemy,
            ref double damage, ref bool isConsumed)
        {
            damage *= entity.BeamDamageMultiplier;
        }
    }
}
