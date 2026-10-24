using System;

namespace RuneCode
{
    /// <summary>
    /// 다중 모드의 배치 동작이다. 행동의 생성 위치 유형에 따라 여러 개체를 나눈다.
    /// 지팡이 앞(발사·빔)은 퍼짐 각도 안에서 방향을 같은 간격으로 나누고, 참격은 조준 방향부터 원을 생성 개수로 등분한다.
    /// 폭발은 지팡이 앞에서 생성되고 생성 개수가 1이라 배치에 개입하지 않는다. 지면 분기는 지면 생성 행동용으로만 남아 있으며, 잔류는 다중을 장착할 수 없다.
    /// 궤도(공전 모드)는 공전 모드가 궤도 자리를 등각으로 나누므로 바꾸지 않는다.
    /// </summary>
    internal sealed class MultiModifierRuntime : SpellModifierRuntime
    {
        private const double DEGREES_TO_RADIANS = Math.PI / 180;

        public override string RuneId => MultiModifierRules.RUNE_ID;

        /// <summary>지팡이 앞 행동의 방향을 index로 분산한다. 참격은 조준 방향부터 원을 개수로 등분하고 나머지는 좌우 퍼짐 각도를 사용한다.</summary>
        public override void BeforePlaceSpawn(SpellRuntimeContext context, SpellAction action, SpellStats stats, int index,
            ref SimVector position, ref SimVector direction)
        {
            if (action.Count <= 1 || action.SpawnOrigin != SpellSpawnOrigin.Wand) return;
            if (action.Form == SlashBehaviorRules.KIND)
            {
                direction = direction.Rotated(index * 2 * Math.PI / action.Count);
                return;
            }
            direction = direction.Rotated((index / (double)(action.Count - 1) * 2 - 1) * stats.SpreadAngle * DEGREES_TO_RADIANS);
        }

        /// <summary>지면에 생성되는 행동이면 생성 지점 주위의 원 위에 index에 따라 같은 간격으로 놓는다.</summary>
        public override void AfterPlaceSpawn(SpellRuntimeContext context, SpellAction action, SpellStats stats, int index,
            ref SimVector position, ref SimVector direction)
        {
            if (action.Count <= 1 || action.SpawnOrigin != SpellSpawnOrigin.Ground) return;
            position += new SimVector(Math.Cos(index * 2 * Math.PI / action.Count), Math.Sin(index * 2 * Math.PI / action.Count))
                * context.Balance.Sim.MultiOffset;
        }
    }
}
