using System.Collections.Generic;

using UnityEngine;

using TMPro;

namespace RuneCode
{
    /// <summary>
    /// 그래프 노드마다 상단 이름·지표·핵심 설정 요약·포트 이름과 행동 노드 슬롯 내용을 담은 TMP 라벨을 만들고 배치한다.
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
        private readonly GraphNodeMetricLabels _metricLabels;
        private readonly Dictionary<string, TextMeshProUGUI> _labels = new Dictionary<string, TextMeshProUGUI>();
        private readonly Dictionary<string, List<TextMeshProUGUI>> _portLabels = new Dictionary<string, List<TextMeshProUGUI>>();
        private readonly Dictionary<string, List<TextMeshProUGUI>> _slotLabels = new Dictionary<string, List<TextMeshProUGUI>>();
        private readonly Dictionary<string, TextMeshProUGUI> _gradeLabels = new Dictionary<string, TextMeshProUGUI>();
        private readonly GraphMeshPainter _painter;
        private readonly Dictionary<string, GraphMeshLayer> _nodeLayers = new Dictionary<string, GraphMeshLayer>();
        private readonly HashSet<string> _existingNodes = new HashSet<string>();
        private readonly List<string> _staleIds = new List<string>();
        private string _frontNodeId;

        /// <summary>라벨을 둘 캔버스, 글꼴, 뷰포트와 노드 배치로 라벨 관리자를 만든다.</summary>
        public GraphNodeLabels(RectTransform parent, TMP_FontAsset font, GraphViewport viewport, GraphNodeLayout layout, GraphMeshPainter painter)
        {
            _parent = parent;
            _font = font;
            _viewport = viewport;
            _layout = layout;
            _painter = painter;
            _metricLabels = new GraphNodeMetricLabels(parent, font, viewport);
        }

        /// <summary>
        /// 그래프 노드마다 현재 컴파일 결과를 반영한 라벨을 갱신하고 사라진 노드의 라벨을 지운다.
        /// 남아 있는 노드 ID 집합을 반환한다(선택 정리용).
        /// </summary>
        public HashSet<string> Refresh(SpellGraph graph, IEnumerable<SpellGraph> library, CompiledSpell compiledSpell)
        {
            _existingNodes.Clear();
            _metricLabels.Refresh(graph, library, compiledSpell);
            foreach (GraphNode node in graph.Nodes)
            {
                _existingNodes.Add(node.Id);
                TextMeshProUGUI label = GetOrCreate(node.Id);
                _nodeLayers[node.Id].Initialize(_painter, node);
                RuneDefinition rune = GameData.Runes.Get(node.RuneId);
                string nameText = rune.Category == SpellGrammar.CATEGORY_BEHAVIOR
                    ? GraphNodeLabelText.BuildNameText(node) : GraphNodeLabelText.BuildLegacyText(node, library);
                if (label.text != nameText) label.text = nameText;
                RefreshPortLabels(node);
                RefreshSlotLabels(node);
                RefreshGradeLabel(node, rune);
            }
            RemoveStale();
            return _existingNodes;
        }

        /// <summary>화면 이동·확대 배율에 맞춰 이름·지표·포트와 슬롯 라벨을 배치한다.</summary>
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
                RectTransform titleRect = label.rectTransform;
                titleRect.anchoredPosition = new Vector2(rect.x + LABEL_PADDING_X * zoom,
                    rect.yMax - _parent.rect.yMax - LABEL_PADDING_TOP * zoom);
                titleRect.sizeDelta = new Vector2(LABEL_WIDTH, HEADER_HEIGHT);
                titleRect.localScale = scale;
                if (GraphNodeLayout.HasSlots(rune)) _metricLabels.LayoutNode(node.Id, rect);
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
                LayoutGradeLabel(node, rune, scale);
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

        /// <summary>지정된 노드의 본체, 이름·지표, 포트와 슬롯 라벨을 형제 순서의 맨 앞으로 옮긴다.</summary>
        private void BringFrontNodeToTop()
        {
            if (string.IsNullOrEmpty(_frontNodeId)) return;
            if (!_nodeLayers.TryGetValue(_frontNodeId, out GraphMeshLayer nodeLayer)) return;
            if (!_labels.TryGetValue(_frontNodeId, out TextMeshProUGUI label)) return;
            if (!_portLabels.TryGetValue(_frontNodeId, out List<TextMeshProUGUI> ports)) return;

            nodeLayer.transform.SetAsLastSibling();
            label.transform.SetAsLastSibling();
            _metricLabels.BringToFront(_frontNodeId);
            foreach (TextMeshProUGUI portLabel in ports) portLabel.transform.SetAsLastSibling();
            foreach (TextMeshProUGUI slotLabel in _slotLabels[_frontNodeId]) slotLabel.transform.SetAsLastSibling();
            if (_gradeLabels.TryGetValue(_frontNodeId, out TextMeshProUGUI gradeLabel)) gradeLabel.transform.SetAsLastSibling();
        }

        /// <summary>노드 ID의 이름·지표 라벨을 반환하며 없으면 글꼴과 정렬을 지정해 만든다.</summary>
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
                string text = GraphNodeLabelText.BuildPortText(port);
                if (label.text != text) label.text = text;
            }
            for (int index = rows * 2; index < labels.Count; index++) labels[index].gameObject.SetActive(false);
        }

        /// <summary>행동 노드의 속성·모디 슬롯마다 라벨을 재사용해 룬 이름 또는 빈 상태 문구를 지정한다. 행동 노드가 아니면 숨긴다.</summary>
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
                string text = GraphNodeLabelText.SlotText(node, GraphNodeLayout.SlotKeys[index]);
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

        /// <summary>행동 노드의 등급 변경 버튼 라벨을 없으면 만들고 문구를 지정한다. 행동 노드가 아니면 만들지 않는다.</summary>
        private void RefreshGradeLabel(GraphNode node, RuneDefinition rune)
        {
            if (!GraphNodeLayout.HasSlots(rune)) return;
            if (!_gradeLabels.TryGetValue(node.Id, out TextMeshProUGUI label))
            {
                label = CreateLabel(SLOT_FONT_SIZE, true);
                label.horizontalAlignment = HorizontalAlignmentOptions.Center;
                _gradeLabels.Add(node.Id, label);
            }
            string text = GameData.L("editor.changeGrade");
            if (label.text != text) label.text = text;
        }

        /// <summary>등급 변경 버튼 라벨을 버튼 사각형 중앙에 배율에 맞춰 배치한다.</summary>
        private void LayoutGradeLabel(GraphNode node, RuneDefinition rune, Vector3 scale)
        {
            if (!_gradeLabels.TryGetValue(node.Id, out TextMeshProUGUI label)) return;
            bool isShown = _layout.TryGetGradeButtonRect(node, rune, out Rect button);
            label.gameObject.SetActive(isShown);
            if (!isShown) return;
            float zoom = _viewport.Zoom;
            RectTransform buttonRect = label.rectTransform;
            buttonRect.anchoredPosition = new Vector2(button.x, button.center.y - _parent.rect.yMax);
            buttonRect.sizeDelta = new Vector2(button.width / zoom, button.height / zoom);
            buttonRect.localScale = scale;
            buttonRect.SetAsLastSibling();
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
                if (!_gradeLabels.TryGetValue(id, out TextMeshProUGUI gradeLabel)) continue;
                Object.Destroy(gradeLabel.gameObject);
                _gradeLabels.Remove(id);
            }
        }

    }
}
