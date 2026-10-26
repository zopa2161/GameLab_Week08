using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>
    /// behavior 노드의 등급·속성 슬롯·모디 슬롯 파라미터를 검사하고 실행 가능한 형태로 바꾼다. 형태는 행동 정의의 고정 형태를 쓴다.
    /// 각 behavior 노드는 컴파일 내부 표현인 Inline Magic 노드(같은 ID·좌표)로 교체되고, 장착된 모디파이어는
    /// 노드별 합성 Modifier 노드 목록으로 돌려진다(그래프에는 넣지 않는다). behavior 외 노드와 엣지는 그대로 둔다.
    /// </summary>
    public static class SpellChainNormalizer
    {
        /// <summary>
        /// behavior 노드를 Inline Magic으로 교체한 컴파일용 그래프를 반환한다. 교체 대상이 없으면 원본을 그대로 반환한다.
        /// 등급·슬롯·속성·모디 오류를 errors에 추가하고, behavior 노드 ID가 키인 합성 Modifier 노드 목록을 slotModifiers로 돌려준다.
        /// </summary>
        public static SpellGraph Normalize(SpellGraph graph, RuneCatalog runes,
            List<CompileIssue> errors, List<CompileIssue> warnings,
            out Dictionary<string, List<GraphNode>> slotModifiers)
        {
            slotModifiers = new Dictionary<string, List<GraphNode>>(StringComparer.Ordinal);
            if (graph?.Nodes == null || graph.Edges == null) return graph;
            var replacements = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
            int errorCount = errors.Count;
            foreach (GraphNode node in graph.Nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.Id) || !runes.TryGet(node.RuneId, out RuneDefinition rune)) continue;
                if (rune.Category != SpellGrammar.CATEGORY_BEHAVIOR || rune.IsDisabled) continue;
                if (!NormalizeBehaviorNode(node, rune, runes, errors, slotModifiers, out GraphNode inline)) continue;
                replacements[node.Id] = inline;
            }
            if (replacements.Count == 0 || errors.Count > errorCount) return graph;
            var nodes = new List<GraphNode>(graph.Nodes.Count);
            foreach (GraphNode node in graph.Nodes)
            {
                if (node == null || !replacements.TryGetValue(node.Id, out GraphNode inline))
                {
                    nodes.Add(node);
                    continue;
                }
                nodes.Add(inline);
            }
            return graph.CopyWith(nodes, graph.Edges);
        }

        /// <summary>
        /// behavior 노드 하나의 등급·속성·모디 슬롯을 검사하고 행동의 고정 형태를 쓴 Inline Magic 노드로 만든다.
        /// 장착 모디는 합성 Modifier 노드 목록으로 slotModifiers에 추가한다. 등급 오류로 진행이 불가하면 false를 반환한다.
        /// </summary>
        private static bool NormalizeBehaviorNode(GraphNode node, RuneDefinition rune, RuneCatalog runes,
            List<CompileIssue> errors, Dictionary<string, List<GraphNode>> slotModifiers, out GraphNode inline)
        {
            inline = null;
            string grade = node.GetText(SpellGrammar.MODIFIER_GRADE_PARAM, SpellGrammar.DEFAULT_BEHAVIOR_GRADE);
            if (!runes.TryGetBehaviorSlots(grade, out int elementSlots, out int modifierSlots))
            {
                errors.Add(new CompileIssue("E9", node.Id, SpellGrammar.MODIFIER_GRADE_PARAM));
                return false;
            }
            CheckSlotLimit(node, SpellGrammar.ELEMENT_SLOT_PREFIX, SpellGrammar.MAX_ELEMENT_SLOTS, elementSlots, errors);
            CheckSlotLimit(node, SpellGrammar.MODIFIER_SLOT_PREFIX, SpellGrammar.MAX_MODIFIER_SLOTS, modifierSlots, errors);

            ElementDefinition element = ResolveElement(node, elementSlots, runes, errors);
            CollectModifiers(node, modifierSlots, runes, slotModifiers, errors);

            inline = new GraphNode(node.Id, SpellGrammar.INLINE_RUNE, node.X, node.Y);
            inline.SetText("magicType", rune.FixedShape);
            inline.SetText(SpellGrammar.ELEMENT_PARAM, element != null ? element.InternalValue : null);
            inline.SetText("form", SpellGrammar.FormOf(node.RuneId));
            return true;
        }

        /// <summary>등급 한도를 넘는 슬롯 키에 비어 있지 않은 값이 있으면 E23을 추가한다.</summary>
        private static void CheckSlotLimit(GraphNode node, string prefix, int maxSlots, int limit, List<CompileIssue> errors)
        {
            for (int index = 0; index < maxSlots; index++)
            {
                if (index < limit) continue;
                string value = node.GetText(prefix + index, null);
                if (string.IsNullOrEmpty(value)) continue;
                errors.Add(new CompileIssue("E23", node.Id));
                return;
            }
        }

        /// <summary>
        /// 한도 안의 속성 슬롯을 인덱스 순서로 검사하고 첫 유효 속성을 반환한다(확정 R1: 나머지 슬롯은 장착만 처리).
        /// 존재하지 않거나 비활성이거나 전투 속성이 아닌 값은 E24를 낸다. 장착된 속성이 없으면 무속성이다.
        /// </summary>
        private static ElementDefinition ResolveElement(GraphNode node, int elementSlots, RuneCatalog runes, List<CompileIssue> errors)
        {
            ElementDefinition element = null;
            for (int index = 0; index < elementSlots; index++)
            {
                string value = node.GetText(SpellGrammar.ElementSlotKey(index), null);
                if (string.IsNullOrEmpty(value)) continue;
                if (!runes.TryGetElement(value, out ElementDefinition candidate)
                    || candidate.Category != SpellGrammar.ELEMENT_CATEGORY_ELEMENTAL
                    || !runes.TryGet(candidate.RuneId, out RuneDefinition elementRune)
                    || elementRune.IsDisabled)
                {
                    errors.Add(new CompileIssue("E24", node.Id, value));
                    continue;
                }
                if (element == null) element = candidate;
            }
            if (element == null) runes.TryGetElement(SpellGrammar.ELEMENT_NEUTRAL, out element);
            return element;
        }

        /// <summary>
        /// 한도 안의 모디 슬롯 값을 검사해 통과한 항목을 합성 Modifier 노드(모디 룬 + 등급 파라미터) 목록으로 모은다.
        /// 룬 없음·카테고리 불일치·등급 미존재·중복은 E9, 행동과 호환되지 않으면 E22를 낸다.
        /// </summary>
        private static void CollectModifiers(GraphNode node, int modifierSlots, RuneCatalog runes,
            Dictionary<string, List<GraphNode>> slotModifiers, List<CompileIssue> errors)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < modifierSlots; index++)
            {
                string slotKey = SpellGrammar.ModifierSlotKey(index);
                string value = node.GetText(slotKey, null);
                if (string.IsNullOrEmpty(value)) continue;
                if (!SpellGrammar.TryParseStockKey(value, out string modifierId, out string modifierGrade)
                    || !runes.TryGet(modifierId, out RuneDefinition modifierRune)
                    || modifierRune.Category != SpellGrammar.CATEGORY_MODIFIER
                    || !runes.ModifierGrades.IsGradeAvailable(modifierId, modifierGrade))
                {
                    errors.Add(new CompileIssue("E9", node.Id, slotKey));
                    continue;
                }
                if (!seen.Add(modifierId))
                {
                    errors.Add(new CompileIssue("E9", node.Id, slotKey));
                    continue;
                }
                if (!runes.IsModifierCompatible(modifierId, node.RuneId))
                {
                    errors.Add(new CompileIssue("E22", node.Id));
                    continue;
                }
                if (!slotModifiers.TryGetValue(node.Id, out List<GraphNode> list))
                {
                    list = new List<GraphNode>();
                    slotModifiers.Add(node.Id, list);
                }
                GraphNode modifierNode = new GraphNode(node.Id + "#" + slotKey, modifierId, 0f, 0f);
                modifierNode.SetText(SpellGrammar.MODIFIER_GRADE_PARAM, modifierGrade);
                list.Add(modifierNode);
            }
        }
    }
}
