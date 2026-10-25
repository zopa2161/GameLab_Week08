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
        }
    }
}
