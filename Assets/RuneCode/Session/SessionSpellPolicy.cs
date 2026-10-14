using System.Collections.Generic;
using System.Linq;

namespace RuneCode
{
    /// <summary>
    /// 세션의 세이브와 성장 값으로 마법 편집 규칙(보관함 한도, 해금, RAM 용량, 룬 소지량, 튜토리얼, 상태 문구)을 적용한다.
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

        /// <summary>현재 해금 룬, 해금 검사가 필요 없는 모디·속성·행동 룬, 성장 값, 통합 소지량으로 컴파일 문맥을 만든다.</summary>
        public SpellCompileContext GetCompileContext()
        {
            IEnumerable<string> unlockedRunes = _session.Save.UnlockedRunes.Concat(GameData.Runes.StartRunes)
                .Concat(GameData.Runes.All.Where(rune => !rune.IsDisabled
                    && (rune.Category == SpellGrammar.CATEGORY_MODIFIER || rune.Category == SpellGrammar.CATEGORY_ELEMENT
                        || rune.Category == SpellGrammar.CATEGORY_BEHAVIOR)).Select(rune => rune.Id));
            return new SpellCompileContext(unlockedRunes, _session.Capacity, _session.MaxEnergy, _session.Save.Library,
                _session.Save.CreateStockMap());
        }

        /// <summary>룬의 해금 여부를 반환한다. 비활성 룬은 false이고, 모디·속성·행동은 소지량 검사로 배치를 제한하므로 true다.</summary>
        public bool IsRuneUnlocked(string runeId)
        {
            if (!GameData.Runes.TryGet(runeId, out RuneDefinition rune)) return false;
            if (rune.IsDisabled) return false;
            if (rune.Category == SpellGrammar.CATEGORY_CORE || rune.Category == SpellGrammar.CATEGORY_INTERNAL
                || rune.Category == SpellGrammar.CATEGORY_MODIFIER || rune.Category == SpellGrammar.CATEGORY_ELEMENT
                || rune.Category == SpellGrammar.CATEGORY_BEHAVIOR) return true;
            return _session.Save.UnlockedRunes.Contains(runeId);
        }

        /// <summary>후보 그래프를 장착 슬롯에 반영했을 때 공유·비공유 RAM 규칙을 지키는지 반환한다.</summary>
        public bool IsWithinRam(SpellGraph candidate)
        {
            if (candidate == null) return false;
            PlayerSave save = _session.Save;
            var equippedCopies = 0;
            for (var slot = 0; slot < save.SlotCount; slot++)
                if (save.Loadout[slot] == candidate.Id) equippedCopies++;
            if (equippedCopies == 0) return true;
            if (GameData.Balance.Ram.Mode == "shared") return CalculateEquippedRam(candidate) <= _session.Capacity;
            return GetGraphRam(candidate) <= _session.Capacity;
        }

        /// <summary>
        /// 현재 그래프를 후보로 바꿨을 때 수가 늘어나는 소지 키마다, 후보와 보관함의 다른 마법에 배치된 수의 합이 소지량 이하인지 반환한다.
        /// 이미 초과한 키라도 늘리지 않는 편집(다른 노드 배치 등)은 막지 않는다.
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
                if (pair.Value + CountInOtherSpells(pair.Key, candidate.Id) > owned) return false;
            }
            return true;
        }

        /// <summary>편집 그래프를 포함한 보관함 전체에서 지정 (룬 ID, 등급)의 배치 수(used)와 소지량(owned)을 구한다.</summary>
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
            used += CountInOtherSpells(key, editing?.Id);
            owned = _session.Save.GetStock(runeId, grade);
            return true;
        }

        /// <summary>보관함에서 지정 ID를 제외한 마법들에 배치된 소지 키별 수를 반환한다.</summary>
        private int CountInOtherSpells(string key, string excludedSpellId)
        {
            var count = 0;
            foreach (SpellGraph graph in _session.Save.Library)
            {
                if (graph == null || graph.Id == excludedSpellId) continue;
                if (GraphCompiler.CountStockUsage(graph, GameData.Runes).TryGetValue(key, out int placed)) count += placed;
            }
            return count;
        }

        /// <summary>룬 배치 후 진행 반응을 처리한다. 튜토리얼은 제외됐다.</summary>
        public void OnRunePlaced(string runeId)
        {
        }

        /// <summary>속성 선택 후 진행 반응을 처리한다. 튜토리얼은 제외됐다.</summary>
        public void OnElementSelected(string elementId)
        {
        }

        /// <summary>문자열 키를 세션 상태 문구로 전달한다.</summary>
        public void ReportStatus(string key)
        {
            _session.SetStatus(key);
        }

        /// <summary>이미 번역된 문구를 세션 상태 문구로 전달한다.</summary>
        public void ReportStatusText(string text)
        {
            _session.SetStatusText(text);
        }

        /// <summary>그래프에서 엣지에 연결된 룬의 RAM만 합산한다.</summary>
        private int GetGraphRam(SpellGraph graph)
        {
            if (graph == null) return 0;
            var ram = 0;
            foreach (var node in graph.Nodes)
                if (graph.IsNodeConnected(node.Id)) ram += GameData.Runes.NodeRam(node);
            return ram;
        }

        /// <summary>후보 그래프를 자신을 참조하는 장착 슬롯에 반영한 전체 RAM 사용량을 반환한다.</summary>
        private int CalculateEquippedRam(SpellGraph candidate)
        {
            var ram = 0;
            PlayerSave save = _session.Save;
            for (var slot = 0; slot < save.SlotCount; slot++)
            {
                var id = save.Loadout[slot];
                ram += GetGraphRam(candidate != null && id == candidate.Id ? candidate : save.FindGraph(id));
            }
            return ram;
        }
    }
}
