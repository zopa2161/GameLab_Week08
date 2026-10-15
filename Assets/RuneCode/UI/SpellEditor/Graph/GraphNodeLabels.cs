using System.Collections.Generic;
using System.Text;

using UnityEngine;

using TMPro;

namespace RuneCode
{
    /// <summary>
    /// 그래프 노드마다 이름·RAM·핵심 설정 요약·포트 이름과 행동 노드 슬롯 내용을 담은 TMP 라벨을 만들고 배치한다.
    /// 글꼴 크기는 고정하고 Transform 스케일로 확대해 rich text 크기·위치 태그가 노드와 같은 비율을 유지한다.
    /// </summary>
    internal sealed class GraphNodeLabels
    {
        private const float LABEL_FONT_SIZE = 15f;
        private const float LABEL_PADDING_X = 24f;
        private const float LABEL_PADDING_RIGHT = 14f;
        private const float LABEL_PADDING_TOP = 9f;
        private const float HEADER_HEIGHT = 40f;
        private const float PORT_FONT_SIZE = 12f;
        private const float PORT_LABEL_HEIGHT = 16f;
        private const float SLOT_FONT_SIZE = 10f;
        private const float LABEL_WIDTH = GraphNodeLayout.NODE_WIDTH - LABEL_PADDING_X - LABEL_PADDING_RIGHT;

        private readonly RectTransform _parent;
        private readonly TMP_FontAsset _font;
        private readonly GraphViewport _viewport;
        private readonly GraphNodeLayout _layout;
        private readonly Dictionary<string, TextMeshProUGUI> _labels = new Dictionary<string, TextMeshProUGUI>();
        private readonly Dictionary<string, List<TextMeshProUGUI>> _portLabels = new Dictionary<string, List<TextMeshProUGUI>>();
        private readonly Dictionary<string, List<TextMeshProUGUI>> _slotLabels = new Dictionary<string, List<TextMeshProUGUI>>();
        private readonly GraphMeshPainter _painter;
        private readonly Dictionary<string, GraphMeshLayer> _nodeLayers = new Dictionary<string, GraphMeshLayer>();
        private readonly HashSet<string> _existingNodes = new HashSet<string>();
        private readonly List<string> _staleIds = new List<string>();
        private readonly StringBuilder _builder = new StringBuilder();
        private string _frontNodeId;

        /// <summary>라벨을 둘 캔버스, 글꼴, 뷰포트와 노드 배치로 라벨 관리자를 만든다.</summary>
        public GraphNodeLabels(RectTransform parent, TMP_FontAsset font, GraphViewport viewport, GraphNodeLayout layout, GraphMeshPainter painter)
        {
            _parent = parent;
            _font = font;
            _viewport = viewport;
            _layout = layout;
            _painter = painter;
        }

        /// <summary>
        /// 그래프 노드마다 라벨 내용을 갱신하고 사라진 노드의 라벨을 지운다. 프리셋 호출 요약은 보관함에서 이름을 찾는다.
        /// 남아 있는 노드 ID 집합을 반환한다(선택 정리용).
        /// </summary>
        public HashSet<string> Refresh(SpellGraph graph, IEnumerable<SpellGraph> library)
        {
            _existingNodes.Clear();
            foreach (GraphNode node in graph.Nodes)
            {
                _existingNodes.Add(node.Id);
                TextMeshProUGUI label = GetOrCreate(node.Id);
                _nodeLayers[node.Id].Initialize(_painter, node);
                string text = BuildText(node, library);
                if (label.text != text) label.text = text;
                RefreshPortLabels(node);
                RefreshSlotLabels(node);
            }
            RemoveStale();
            return _existingNodes;
        }

        /// <summary>화면 이동·확대 배율에 맞춰 라벨을 배치하고 노드 본체·제목·포트를 노드 순서대로 겹쳐 그린다.</summary>
        public void Layout(SpellGraph graph)
        {
            float zoom = _viewport.Zoom;
            Vector3 scale = new Vector3(zoom, zoom, 1);
            for (int nodeIndex = 0; nodeIndex < graph.Nodes.Count; nodeIndex++)
            {
                GraphNode node = graph.Nodes[nodeIndex];
                if (!_labels.TryGetValue(node.Id, out TextMeshProUGUI label)) continue;
                RuneDefinition rune = GameData.Runes.Get(node.RuneId);
                Rect rect = _layout.NodeRect(node, rune);
                _nodeLayers[node.Id].transform.SetAsLastSibling();
                label.gameObject.SetActive(true);
                label.transform.SetAsLastSibling();
                RectTransform textRect = label.rectTransform;
                textRect.anchoredPosition = new Vector2(rect.x + LABEL_PADDING_X * zoom, rect.yMax - _parent.rect.yMax - LABEL_PADDING_TOP * zoom);
                textRect.sizeDelta = new Vector2(LABEL_WIDTH, HEADER_HEIGHT);
                textRect.localScale = scale;
                List<TextMeshProUGUI> ports = _portLabels[node.Id];
                int rows = _layout.PortRowCount(node, rune);
                for (int index = 0; index < rows * 2; index++)
                {
                    bool isOutput = index % 2 != 0;
                    PortDefinition port = _layout.PortAt(node, rune,
                        isOutput ? SpellGrammar.DIRECTION_OUT : SpellGrammar.DIRECTION_IN, index / 2);
                    RectTransform portRect = ports[index].rectTransform;
                    portRect.gameObject.SetActive(port != null);
                    if (port == null) continue;
                    Vector2 point = _layout.PortPosition(rect, node, rune, port.Id);
                    portRect.anchoredPosition = new Vector2(rect.x + LABEL_PADDING_X * zoom, point.y - _parent.rect.yMax);
                    portRect.sizeDelta = new Vector2(LABEL_WIDTH, PORT_LABEL_HEIGHT);
                    portRect.localScale = scale;
                    portRect.SetAsLastSibling();
                }
                LayoutSlotLabels(node, rune, scale);
            }
            BringFrontNodeToTop();
        }

        /// <summary>드래그 중인 노드 ID를 저장해 다음 레이아웃에서도 해당 노드를 가장 앞에 배치한다.</summary>
        public void SetFrontNode(string nodeId)
        {
            _frontNodeId = nodeId;
        }

        /// <summary>선택·실행·연결 표시가 변했을 때 모든 노드 본체의 메시 갱신을 예약한다.</summary>
        public void RefreshMeshes()
        {
            foreach (GraphMeshLayer layer in _nodeLayers.Values) layer.SetVerticesDirty();
        }

        /// <summary>지정된 노드의 본체, 제목, 포트 라벨을 형제 순서의 맨 앞으로 옮긴다.</summary>
        private void BringFrontNodeToTop()
        {
            if (string.IsNullOrEmpty(_frontNodeId)) return;
            if (!_nodeLayers.TryGetValue(_frontNodeId, out GraphMeshLayer nodeLayer)) return;
            if (!_labels.TryGetValue(_frontNodeId, out TextMeshProUGUI label)) return;
            if (!_portLabels.TryGetValue(_frontNodeId, out List<TextMeshProUGUI> ports)) return;

            nodeLayer.transform.SetAsLastSibling();
            label.transform.SetAsLastSibling();
            foreach (TextMeshProUGUI portLabel in ports) portLabel.transform.SetAsLastSibling();
            foreach (TextMeshProUGUI slotLabel in _slotLabels[_frontNodeId]) slotLabel.transform.SetAsLastSibling();
        }

        /// <summary>노드 ID의 라벨을 반환하며 없으면 고정 글꼴 크기와 좌상단 기준점으로 새로 만든다.</summary>
        private TextMeshProUGUI GetOrCreate(string nodeId)
        {
            if (_labels.TryGetValue(nodeId, out TextMeshProUGUI label)) return label;
            _nodeLayers.Add(nodeId, GraphMeshLayer.Create(_parent, "GraphNodeVisual"));
            label = CreateLabel(LABEL_FONT_SIZE, false);
            _labels.Add(nodeId, label);
            _portLabels.Add(nodeId, new List<TextMeshProUGUI>());
            _slotLabels.Add(nodeId, new List<TextMeshProUGUI>());
            return label;
        }

        /// <summary>프리팹으로 라벨을 만들고 글꼴 크기·세로 정렬·좌상단 기준점과 텍스트 잘림을 지정한다.</summary>
        private TextMeshProUGUI CreateLabel(float fontSize, bool isPort)
        {
            TextMeshProUGUI label = Object.Instantiate(Resources.Load<TextMeshProUGUI>("RuneCode/UI/Elements/GraphNodeLabel"), _parent, false);
            label.font = _font;
            label.fontSize = fontSize;
            label.raycastTarget = false;
            label.richText = true;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.horizontalAlignment = HorizontalAlignmentOptions.Left;
            label.verticalAlignment = isPort ? VerticalAlignmentOptions.Middle : VerticalAlignmentOptions.Top;
            RectTransform textRect = label.rectTransform;
            textRect.anchorMin = textRect.anchorMax = new Vector2(0, 1);
            textRect.pivot = new Vector2(0, isPort ? .5f : 1);
            return label;
        }

        /// <summary>행마다 입력·출력 라벨을 따로 재사용해 이름과 좌우 정렬을 지정하고 없는 포트는 숨긴다.</summary>
        private void RefreshPortLabels(GraphNode node)
        {
            RuneDefinition rune = GameData.Runes.Get(node.RuneId);
            List<TextMeshProUGUI> labels = _portLabels[node.Id];
            int rows = _layout.PortRowCount(node, rune);
            for (int index = 0; index < rows * 2; index++)
            {
                bool isOutput = index % 2 != 0;
                if (index == labels.Count)
                {
                    TextMeshProUGUI portLabel = CreateLabel(PORT_FONT_SIZE, true);
                    portLabel.horizontalAlignment = isOutput ? HorizontalAlignmentOptions.Right : HorizontalAlignmentOptions.Left;
                    labels.Add(portLabel);
                }
                TextMeshProUGUI label = labels[index];
                PortDefinition port = _layout.PortAt(node, rune,
                    isOutput ? SpellGrammar.DIRECTION_OUT : SpellGrammar.DIRECTION_IN, index / 2);
                label.gameObject.SetActive(port != null);
                string text = BuildPortText(port);
                if (label.text != text) label.text = text;
            }
            for (int index = rows * 2; index < labels.Count; index++) labels[index].gameObject.SetActive(false);
        }

        /// <summary>행동 노드의 슬롯 셀마다 라벨을 재사용해 형태·속성·모디 이름 또는 빈 상태 문구를 지정한다. 행동 노드가 아니면 숨긴다.</summary>
        private void RefreshSlotLabels(GraphNode node)
        {
            RuneDefinition rune = GameData.Runes.Get(node.RuneId);
            List<TextMeshProUGUI> labels = _slotLabels[node.Id];
            int count = GraphNodeLayout.HasSlots(rune) ? GraphNodeLayout.SlotKeys.Count : 0;
            for (int index = 0; index < count; index++)
            {
                if (index == labels.Count)
                {
                    TextMeshProUGUI slotLabel = CreateLabel(SLOT_FONT_SIZE, true);
                    slotLabel.horizontalAlignment = HorizontalAlignmentOptions.Center;
                    labels.Add(slotLabel);
                }
                string text = SlotText(node, GraphNodeLayout.SlotKeys[index]);
                if (labels[index].text != text) labels[index].text = text;
            }
            for (int index = count; index < labels.Count; index++) labels[index].gameObject.SetActive(false);
        }

        /// <summary>슬롯 라벨을 각 셀 사각형 중앙에 배율에 맞춰 배치한다. 잠긴 칸은 숨긴다.</summary>
        private void LayoutSlotLabels(GraphNode node, RuneDefinition rune, Vector3 scale)
        {
            List<TextMeshProUGUI> labels = _slotLabels[node.Id];
            if (!GraphNodeLayout.HasSlots(rune)) return;
            float zoom = _viewport.Zoom;
            for (int index = 0; index < labels.Count && index < GraphNodeLayout.SlotKeys.Count; index++)
            {
                TextMeshProUGUI label = labels[index];
                bool hasCell = _layout.TryGetSlotRect(node, rune, GraphNodeLayout.SlotKeys[index], out Rect cell);
                bool isShown = hasCell && !string.IsNullOrEmpty(label.text);
                label.gameObject.SetActive(isShown);
                if (!isShown) continue;
                RectTransform slotRect = label.rectTransform;
                slotRect.anchoredPosition = new Vector2(cell.x, cell.center.y - _parent.rect.yMax);
                slotRect.sizeDelta = new Vector2(cell.width / zoom, cell.height / zoom);
                slotRect.localScale = scale;
                slotRect.SetAsLastSibling();
            }
        }

        /// <summary>슬롯 셀 문구를 반환한다. 빈 속성·모디 슬롯은 비어 있음 문구, 잠긴 칸은 빈 문자열을 반환한다.</summary>
        private static string SlotText(GraphNode node, string slotKey)
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

        /// <summary>그래프에서 사라진 노드의 라벨 오브젝트를 파괴하고 목록에서 뺀다.</summary>
        private void RemoveStale()
        {
            _staleIds.Clear();
            foreach (var pair in _labels)
            {
                if (!_existingNodes.Contains(pair.Key)) _staleIds.Add(pair.Key);
            }
            foreach (string id in _staleIds)
            {
                Object.Destroy(_labels[id].gameObject);
                _labels.Remove(id);
                Object.Destroy(_nodeLayers[id].gameObject);
                _nodeLayers.Remove(id);
                foreach (TextMeshProUGUI label in _portLabels[id]) Object.Destroy(label.gameObject);
                _portLabels.Remove(id);
                foreach (TextMeshProUGUI label in _slotLabels[id]) Object.Destroy(label.gameObject);
                _slotLabels.Remove(id);
            }
        }

        /// <summary>
        /// 룬 이름·Modifier 또는 행동 등급·RAM과 핵심 설정 요약을 담은 제목 라벨을 반환한다. 포트 이름·슬롯 내용은 별도 라벨로 표시한다.
        /// </summary>
        private string BuildText(GraphNode node, IEnumerable<SpellGraph> library)
        {
            RuneDefinition rune = GameData.Runes.Get(node.RuneId);
            _builder.Clear();
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
            _builder.Append("<b>").Append(name).Append("</b>  ").Append(GameData.Runes.NodeRam(node)).Append(" RAM");
            string summary = Summary(node, rune, library);
            if (!string.IsNullOrEmpty(summary)) _builder.Append("\n<size=12>").Append(summary).Append("</size>");
            return _builder.ToString();
        }

        /// <summary>포트 이름을 반환하며 없는 포트는 빈 문자열, 지원 중단 포트는 취소선을 붙인 이름으로 반환한다.</summary>
        private static string BuildPortText(PortDefinition port)
        {
            if (port == null) return string.Empty;
            string label = port.Id == SpellGrammar.BODY_PORT ? "shape/method" : port.Id;
            return port.IsDeprecated ? "<s>" + label + "</s>" : label;
        }

        /// <summary>Trigger·Apply·프리셋 호출의 핵심 값(시작 조건, 속성, 호출 대상)을 반환하며 해당 없으면 null을 반환한다. Shape 속성은 붙은 속성 노드로 보인다.</summary>
        private static string Summary(GraphNode node, RuneDefinition rune, IEnumerable<SpellGraph> library)
        {
            if (node.RuneId == SpellGrammar.CORE_RUNE)
            {
                string trigger = node.GetText(SpellGrammar.TRIGGER_PARAM, SpellGrammar.TRIGGER_ON_ATTACK);
                return LocalizedValue("trigger." + trigger, trigger);
            }
            if (rune.Id == SpellGrammar.APPLY_RUNE)
            {
                string element = node.GetText(SpellGrammar.ELEMENT_PARAM, "healing");
                return LocalizedValue("element." + element, element);
            }
            if (node.RuneId == SpellGrammar.CALL_RUNE)
            {
                string spellId = node.GetText("spellId");
                foreach (SpellGraph graph in library)
                {
                    if (graph.Id == spellId) return graph.Name;
                }
                return string.IsNullOrEmpty(spellId) ? GameData.L("ui.selectSpell") : GameData.L("ui.missingSpell");
            }
            return null;
        }

        /// <summary>현지화 값을 찾고 번역이 없으면 지정 원문을 반환한다.</summary>
        private static string LocalizedValue(string key, string fallback)
        {
            string value = GameData.L(key);
            return value == key ? fallback : value;
        }
    }
}
