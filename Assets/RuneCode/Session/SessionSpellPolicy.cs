using System.Collections.Generic;
using System.Linq;

namespace RuneCode
{
    /// <summary>
    /// 세션의 세이브와 성장 값으로 마법 편집 규칙(보관함 한도, 해금, 룬 소지량, 튜토리얼, 상태 문구)을 적용한다.
    /// </summary>
    public sealed class SessionSpellPolicy : ISpellEditPolicy
    {
        private readonly RuneCodeSession _session;

        public int MaxLibrary => GameData.Balance.Economy.MaxLibrary;

        /// <summary>규칙을 적용할 세션 참조를 받는다.</summary>
        public SessionSpellPolicy(RuneCodeSession session)
        {
            _session = session;
        }

        /// <summary>현재 해금 룬, 해금 검사가 필요 없는 모디·속성·행동 룬, 통합 소지량과 행동 하나 고정·속성 비용 감소 강화 효과로 컴파일 문맥을 만든다. 미구매 시작 노드 룬은 해금 목록에서 뺀다.</summary>
        public SpellCompileContext GetCompileContext()
        {
            IEnumerable<string> unlockedRunes = _session.Save.UnlockedRunes.Concat(GameData.Runes.StartRunes)
                .Concat(GameData.Runes.All.Where(rune => !rune.IsDisabled
                    && (rune.Category == SpellGrammar.CATEGORY_MODIFIER || rune.Category == SpellGrammar.CATEGORY_ELEMENT
                        || rune.Category == SpellGrammar.CATEGORY_BEHAVIOR)).Select(rune => rune.Id))
                .Where(IsCoreRuneUnlocked);
            return new SpellCompileContext(unlockedRunes, _session.MaxEnergy, _session.Save.Library,
                _session.Save.CreateStockMap(), isSingleBehavior: _session.HasUpgradeEffect(UpgradeEffectType.SingleBehavior),
                elementCostReduction: _session.GetUpgradeTreeEffectTotal(UpgradeEffectType.ElementCostReduction));
        }

        /// <summary>룬의 해금 여부를 반환한다. 비활성 룬은 false이고, 모디·속성·행동은 소지량 검사로 배치를 제한하므로 true다.</summary>
        public bool IsRuneUnlocked(string runeId)
        {
            if (!GameData.Runes.TryGet(runeId, out RuneDefinition rune)) return false;
            if (rune.IsDisabled) return false;
            if (rune.Category == SpellGrammar.CATEGORY_CORE) return IsCoreRuneUnlocked(runeId);
            if (rune.Category == SpellGrammar.CATEGORY_INTERNAL
                || rune.Category == SpellGrammar.CATEGORY_MODIFIER || rune.Category == SpellGrammar.CATEGORY_ELEMENT
                || rune.Category == SpellGrammar.CATEGORY_BEHAVIOR) return true;
            return _session.Save.UnlockedRunes.Contains(runeId);
        }

        /// <summary>추가 시작 노드 룬은 대응 강화 트리 효과를 구매한 경우에만 true이고, core.cast와 다른 룬은 true를 반환한다.</summary>
        private bool IsCoreRuneUnlocked(string runeId)
        {
            if (runeId == SpellGrammar.CORE_RUNE) return true;
            if (runeId == SpellGrammar.DASH_END_CORE_RUNE) return _session.HasUpgradeEffect(UpgradeEffectType.DashEndStart);
            if (runeId == SpellGrammar.STATUS_STACK_CORE_RUNE) return _session.HasUpgradeEffect(UpgradeEffectType.StatusStackStart);
            if (runeId == SpellGrammar.RIGHT_CLICK_CORE_RUNE) return _session.HasUpgradeEffect(UpgradeEffectType.RightClickStart);
            return true;
        }

        /// <summary>
        /// 현재 그래프를 후보로 바꿨을 때 수가 늘어나는 소지 키마다 후보의 배치 수가 프리셋 소지량 이하인지 반환한다.
        /// 프리셋별로 소지량을 관리하므로 다른 마법에 배치된 수는 합산하지 않는다. 이미 초과한 키라도 늘리지 않는 편집(다른 노드 배치 등)은 막지 않는다.
        /// </summary>
        public bool IsWithinModifierStock(SpellGraph current, SpellGraph candidate)
        {
            if (candidate == null) return false;
            Dictionary<string, int> stock = _session.Save.CreateStockMap();
            Dictionary<string, int> before = GraphCompiler.CountStockUsage(current, GameData.Runes);
            foreach (KeyValuePair<string, int> pair in GraphCompiler.CountStockUsage(candidate, GameData.Runes))
            {
                before.TryGetValue(pair.Key, out int previous);
                if (pair.Value <= previous) continue;
                stock.TryGetValue(pair.Key, out int owned);
                if (pair.Value > owned) return false;
            }
            return true;
        }

        /// <summary>편집 중인 프리셋 그래프에서 지정 (룬 ID, 등급)의 배치 수(used)와 소지량(owned)을 구한다. 프리셋별 소지량 관리로 다른 마법 배치는 세지 않는다.</summary>
        public bool TryGetModifierUsage(string runeId, string grade, SpellGraph editing, out int used, out int owned)
        {
            used = 0;
            owned = 0;
            if (!GameData.Runes.TryGet(runeId, out RuneDefinition rune) || rune.IsDisabled) return false;
            if (rune.Category == SpellGrammar.CATEGORY_MODIFIER)
            {
                if (!GameData.ModifierGrades.IsGradeAvailable(runeId, grade)) return false;
            }
            else if (rune.Category == SpellGrammar.CATEGORY_ELEMENT)
            {
                if (!string.IsNullOrEmpty(grade) || !GameData.Runes.TryGetRuneElement(runeId, out ElementDefinition element)
                    || element.Category != SpellGrammar.ELEMENT_CATEGORY_ELEMENTAL) return false;
            }
            else if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR)
            {
                if (string.IsNullOrEmpty(grade) || !GameData.Runes.BehaviorGradeOrder.Contains(grade)) return false;
            }
            else return false;
            string key = SpellGrammar.ModifierStockKey(runeId, grade);
            GraphCompiler.CountStockUsage(editing, GameData.Runes).TryGetValue(key, out used);
            owned = _session.Save.GetStock(runeId, grade);
            return true;
        }

        /// <summary>보관함 전체 마법에 배치된 수를 제외한 미사용 수량(unused)과 소지량(owned)을 구한다. 합성 재료 계산에 쓴다.</summary>
        public bool TryGetUnusedStock(string runeId, string grade, SpellGraph editing, out int unused, out int owned)
        {
            unused = 0;
            if (!TryGetModifierUsage(runeId, grade, editing, out _, out owned)) return false;
            string key = SpellGrammar.ModifierStockKey(runeId, grade);
            unused = owned - CountPlacedInLibrary(key, editing);
            return true;
        }

        /// <summary>보관함 전체에서 지정 소지 키의 배치 수를 센다. 편집 중 마법은 저장 사본 대신 편집 그래프 기준으로 센다.</summary>
        private int CountPlacedInLibrary(string key, SpellGraph editing)
        {
            var count = 0;
            string editingId = editing?.Id;
            foreach (SpellGraph graph in _session.Save.Library)
            {
                if (graph == null || (editingId != null && graph.Id == editingId)) continue;
                if (GraphCompiler.CountStockUsage(graph, GameData.Runes).TryGetValue(key, out int placed)) count += placed;
            }
            GraphCompiler.CountStockUsage(editing, GameData.Runes).TryGetValue(key, out int editingPlaced);
            return count + editingPlaced;
        }

        /// <summary>룬 배치 후 진행 반응을 처리한다. 튜토리얼은 제외됐다.</summary>
        public void OnRunePlaced(string runeId)
        {
        }

        /// <summary>속성 선택 후 진행 반응을 처리한다. 튜토리얼은 제외됐다.</summary>
        public void OnElementSelected(string elementId)
        {
        }

        /// <summary>편집 거부·오류 키를 세션 상태 문구로 전달하고 알림음을 재생한다.</summary>
        public void ReportStatus(string key)
        {
            _session.SetStatus(key);
            SoundManager.Instance.PlayDisable();
        }

        /// <summary>가져오기·저장 실패 문구를 세션 상태 문구로 전달하고 알림음을 재생한다.</summary>
        public void ReportStatusText(string text)
        {
            _session.SetStatusText(text);
            SoundManager.Instance.PlayDisable();
        }
    }
}
