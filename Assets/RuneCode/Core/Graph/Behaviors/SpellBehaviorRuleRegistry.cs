using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>
    /// 행동 규칙 등록 목록이다. 새 행동은 RULES에 한 줄 추가한다. 실행 동작은 SpellBehaviorRuntimeRegistry에 같은 Kind로 등록한다.
    /// </summary>
    public static class SpellBehaviorRuleRegistry
    {
        private static readonly SpellBehaviorRules[] RULES =
        {
            new LaunchBehaviorRules(),
            new LegacyBurstBehaviorRules(),
            new PersistBehaviorRules(),
            new BeamBehaviorRules(),
            new ExplosionBehaviorRules()
        };

        /// <summary>등록된 모든 행동 규칙이다.</summary>
        public static IReadOnlyList<SpellBehaviorRules> All => RULES;

        /// <summary>행동 룬 ID(behavior.launch 등)로 규칙을 찾는다. 없으면 false를 반환한다.</summary>
        public static bool TryGetByRuneId(string runeId, out SpellBehaviorRules rules)
        {
            foreach (SpellBehaviorRules candidate in RULES)
            {
                if (candidate.RuneId != runeId) continue;
                rules = candidate;
                return true;
            }
            rules = null;
            return false;
        }

        /// <summary>Inline Magic form 값(launch 등)으로 규칙을 찾는다. 없으면 false를 반환한다.</summary>
        public static bool TryGetByFormValue(string formValue, out SpellBehaviorRules rules)
        {
            foreach (SpellBehaviorRules candidate in RULES)
            {
                if (candidate.FormValue != formValue) continue;
                rules = candidate;
                return true;
            }
            rules = null;
            return false;
        }

        /// <summary>런타임 종류(bolt 등)로 규칙을 찾는다. 없으면 false를 반환한다.</summary>
        public static bool TryGetByKind(string kind, out SpellBehaviorRules rules)
        {
            foreach (SpellBehaviorRules candidate in RULES)
            {
                if (candidate.Kind != kind) continue;
                rules = candidate;
                return true;
            }
            rules = null;
            return false;
        }

        /// <summary>런타임 종류의 표시 방식을 반환한다. 등록되지 않은 종류는 발사체로 그린다.</summary>
        public static SpellDisplayType GetDisplayType(string kind)
            => TryGetByKind(kind, out SpellBehaviorRules rules) ? rules.DisplayType : SpellDisplayType.Projectile;
    }
}
