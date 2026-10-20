using System.Collections.Generic;
using System.Text;

using UnityEngine;

namespace RuneCode
{
    internal static class GraphNodeLabelText
    {
        private static readonly string _behaviorColorHex = ColorUtility.ToHtmlStringRGB(RuneMesh.CategoryColor(SpellGrammar.CATEGORY_BEHAVIOR));

        /// <summary>룬 이름과 Modifier 또는 행동 등급을 제목 라벨 문구로 반환한다.</summary>
        internal static string BuildNameText(GraphNode node)
        {
            RuneDefinition rune = GameData.Runes.Get(node.RuneId);
            string name = rune.Name;
            if (rune.Category == SpellGrammar.CATEGORY_MODIFIER)
            {
                string grade = SpellGrammar.GetModifierGrade(rune, node, GameData.ModifierGrades);
                name = ModifierGradeText.FormatName(name, grade);
            }
            else if (GraphNodeLayout.HasSlots(rune))
            {
                name = ModifierGradeText.FormatName(name, node.GetText(SpellGrammar.MODIFIER_GRADE_PARAM, SpellGrammar.DEFAULT_BEHAVIOR_GRADE));
            }
            return "<b>" + name + "</b>";
        }

        /// <summary>이름·RAM·핵심 설정 요약을 기존 비행동 노드 라벨 문구로 반환한다.</summary>
        internal static string BuildLegacyText(GraphNode node, IEnumerable<SpellGraph> library)
        {
            var builder = new StringBuilder();
            builder.Append(BuildNameText(node)).Append("  ")
                .Append(GameData.Runes.NodeRam(node)).Append(" ").Append(GameData.L("ui.ram"));
            string summary = Summary(node);
            if (!string.IsNullOrEmpty(summary)) builder.Append("\n<size=12>").Append(summary).Append("</size>");
            return builder.ToString();
        }

        /// <summary>행동 노드의 컴파일된 사용 전력과 핵심 설정 요약을 왼쪽 지표 라벨로 반환한다.</summary>
        internal static string BuildBehaviorDetailsText(GraphNode node, IEnumerable<SpellGraph> library, CompiledSpell compiledSpell)
        {
            var builder = new StringBuilder();
            SpellAction action = compiledSpell?.FindAction(node.Id);
            string energy = action == null ? "—" : action.NodeEnergy.ToString("0.#");
            builder.Append("<b><color=#").Append(_behaviorColorHex).Append(">")
                .Append(energy).Append(" ").Append(GameData.L("ui.energy")).Append("</color></b>");
            string summary = Summary(node);
            if (!string.IsNullOrEmpty(summary)) builder.Append("\n<size=12>").Append(summary).Append("</size>");
            return builder.ToString();
        }

        /// <summary>노드가 차지하는 RAM 수치와 라벨을 반환한다.</summary>
        internal static string BuildRamText(GraphNode node)
        {
            return GameData.Runes.NodeRam(node) + " " + GameData.L("ui.ram");
        }

        /// <summary>포트 이름을 반환하며 없는 포트는 빈 문자열, 지원 중단 포트는 취소선을 붙인 이름으로 반환한다.</summary>
        internal static string BuildPortText(PortDefinition port)
        {
            if (port == null) return string.Empty;
            string label = port.Id == SpellGrammar.BODY_PORT ? "shape/method" : port.Id;
            return port.IsDeprecated ? "<s>" + label + "</s>" : label;
        }

        /// <summary>슬롯 셀 문구를 반환한다. 빈 속성·모디 슬롯은 비어 있음 문구, 잠긴 칸은 빈 문자열을 반환한다.</summary>
        internal static string SlotText(GraphNode node, string slotKey)
        {
            if (GraphNodeLayout.IsSlotLocked(node, slotKey)) return string.Empty;
            if (!SpellGrammar.TryParseSlotKey(slotKey, out bool isElement, out _)) return string.Empty;
            string value = node.GetText(slotKey, null);
            if (string.IsNullOrEmpty(value)) return GameData.L("editor.slotEmpty");
            if (isElement)
            {
                return GameData.Runes.TryGetElement(value, out ElementDefinition element)
                    && GameData.Runes.TryGet(element.RuneId, out RuneDefinition elementRune) ? elementRune.Name : value;
            }
            return SpellGrammar.TryParseStockKey(value, out string modifierId, out string grade)
                && GameData.Runes.TryGet(modifierId, out RuneDefinition modifierRune)
                ? ModifierGradeText.FormatName(modifierRune.Name, grade) : value;
        }

        /// <summary>Trigger 노드의 시작 조건 값을 반환하며 해당 없으면 null을 반환한다.</summary>
        private static string Summary(GraphNode node)
        {
            if (node.RuneId != SpellGrammar.CORE_RUNE) return null;
            string trigger = node.GetText(SpellGrammar.TRIGGER_PARAM, SpellGrammar.TRIGGER_ON_ATTACK);
            return LocalizedValue("trigger." + trigger, trigger);
        }

        /// <summary>현지화 값을 찾고 번역이 없으면 지정 원문을 반환한다.</summary>
        private static string LocalizedValue(string key, string fallback)
        {
            string value = GameData.L(key);
            return value == key ? fallback : value;
        }
    }
}
