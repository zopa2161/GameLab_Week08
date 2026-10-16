using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>
    /// 동작 모드 규칙 등록 목록이다. 새 동작 모드는 RULES에 한 줄 추가하고, 실행 동작은 SpellModifierRuntimeRegistry에 같은 RuneId로 등록한다.
    /// 등록 순서가 같은 우선순위 이동 모드의 실행 순서와 개체의 모드 상태 순서가 된다. 수치 모드는 등록하지 않는다.
    /// </summary>
    public static class SpellModifierRuleRegistry
    {
        private static readonly SpellModifierRules[] RULES =
        {
            new MultiModifierRules(),
            new HomingModifierRules(),
            new PierceModifierRules(),
            new OrbitModifierRules()
        };
        private static readonly SpellModifierRules[] NONE = Array.Empty<SpellModifierRules>();

        /// <summary>등록된 모든 동작 모드 규칙이다.</summary>
        public static IReadOnlyList<SpellModifierRules> All => RULES;

        /// <summary>모드 룬 ID로 규칙을 찾는다. 등록되지 않은 모드(수치 모드 등)면 false를 반환한다.</summary>
        public static bool TryGetByRuneId(string runeId, out SpellModifierRules rules)
        {
            foreach (SpellModifierRules candidate in RULES)
            {
                if (candidate.RuneId != runeId) continue;
                rules = candidate;
                return true;
            }
            rules = null;
            return false;
        }

        /// <summary>
        /// 모드 룬 ID 목록에서 등록된 동작 모드 규칙만 등록 순서로 골라 반환한다. 하나도 없으면 빈 목록을 반환한다.
        /// 장착 슬롯 순서와 관계없이 결과 순서가 같다.
        /// </summary>
        public static IReadOnlyList<SpellModifierRules> SelectInRegistryOrder(IReadOnlyList<string> modifierIds)
        {
            List<SpellModifierRules> selected = null;
            foreach (SpellModifierRules candidate in RULES)
            {
                bool isAttached = false;
                for (int index = 0; index < modifierIds.Count; index++)
                    if (modifierIds[index] == candidate.RuneId) isAttached = true;
                if (!isAttached) continue;
                selected ??= new List<SpellModifierRules>();
                selected.Add(candidate);
            }
            return selected ?? (IReadOnlyList<SpellModifierRules>)NONE;
        }
    }
}
