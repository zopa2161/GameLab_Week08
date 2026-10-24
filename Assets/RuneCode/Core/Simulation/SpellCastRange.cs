using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>컴파일된 마법의 형태와 흐름에서 자동 시전 거리를 계산한다. CLI 자동 실행과 측정 실행기가 같은 원본 값을 쓴다.</summary>
    public static class SpellCastRange
    {
        // 사거리를 계산할 수 없는 마법의 자동 시전 거리. 기존 오른쪽 유입 x좌표와 같은 값이다.
        private const double FALLBACK_CAST_RANGE = 1180;

        /// <summary>컴파일된 형태와 흐름의 최대 도달 범위에 지팡이 발사 위치를 더해 자동 시전 거리를 반환한다.</summary>
        public static double GetAutomaticCastRange(CompiledSpell spell)
        {
            var range = GetActionRange(spell.Root);
            return (range > 0 ? range : FALLBACK_CAST_RANGE) + GameData.Balance.Sim.WandOffset;
        }

        /// <summary>후속 실행에서 볼트·영역·순간이동의 최대 도달 거리를 계산한다.</summary>
        private static double GetActionRange(IReadOnlyList<SpellAction> actions)
        {
            var range = 0.0;
            foreach (var action in actions)
            {
                var candidate = 0.0;
                if (action.Kind == "spawn")
                {
                    var stats = action.Stats;
                    candidate = action.GetReach(stats) + (action.Form == SlashBehaviorRules.KIND ? 0 : stats.Radius);
                }
                else candidate = GetActionRange(action.Next);
                range = Math.Max(range, candidate);
            }
            return range;
        }
    }
}
