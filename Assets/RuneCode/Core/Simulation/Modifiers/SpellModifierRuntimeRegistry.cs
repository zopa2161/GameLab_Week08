using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>
    /// 동작 모드 실행 등록 목록이다. 새 동작 모드는 RUNTIMES에 한 줄 추가하고, 규칙은 SpellModifierRuleRegistry에 같은 RuneId로 등록한다.
    /// </summary>
    internal static class SpellModifierRuntimeRegistry
    {
        private static readonly SpellModifierRuntime[] RUNTIMES =
        {
            new MultiModifierRuntime(),
            new HomingModifierRuntime(),
            new PierceModifierRuntime(),
            new OrbitModifierRuntime(),
            new SequenceModifierRuntime()
        };
        private static readonly SpellModifierRuntime[] NONE = Array.Empty<SpellModifierRuntime>();

        /// <summary>등록된 모든 동작 모드 실행이다.</summary>
        public static IReadOnlyList<SpellModifierRuntime> All => RUNTIMES;

        /// <summary>모드 룬 ID에 등록된 실행 동작을 찾는다. 없으면 false를 반환한다.</summary>
        public static bool TryGet(string runeId, out SpellModifierRuntime runtime)
        {
            foreach (SpellModifierRuntime candidate in RUNTIMES)
            {
                if (candidate.RuneId != runeId) continue;
                runtime = candidate;
                return true;
            }
            runtime = null;
            return false;
        }

        /// <summary>
        /// 동작 모드 규칙 목록(등록 순서)에 맞는 실행 동작 배열을 같은 순번으로 반환한다. 개체는 이 순번으로 규칙과 실행을 짝짓는다.
        /// 실행 동작이 없는 규칙은 로드 검증(GameData.ValidateModifierRegistries)에서 막히므로 여기서는 예외를 던진다.
        /// 장착된 동작 모드가 없으면 공유 빈 배열을 반환해 할당하지 않는다.
        /// </summary>
        public static SpellModifierRuntime[] Resolve(IReadOnlyList<SpellModifierRules> rules)
        {
            if (rules == null || rules.Count == 0) return NONE;
            var runtimes = new SpellModifierRuntime[rules.Count];
            for (int index = 0; index < rules.Count; index++)
            {
                if (!TryGet(rules[index].RuneId, out runtimes[index]))
                    throw new InvalidOperationException("동작 모드 실행이 등록되지 않았습니다: " + rules[index].RuneId);
            }
            return runtimes;
        }
    }
}
