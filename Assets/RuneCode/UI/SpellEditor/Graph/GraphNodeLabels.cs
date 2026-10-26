using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 그래프 노드마다 상단 이름·지표·핵심 설정 요약·포트 이름과 행동 노드 슬롯 이미지·내용 라벨을 만들고 배치한다.
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
        private const float GHOST_ALPHA = 0.35f;

        private static readonly Color SLOT_LOCKED_COLOR = new Color(0.05f, 0.05f, 0.06f);

        private readonly RectTransform _parent;
        private readonly TMP_FontAsset _font;
        private readonly GraphViewport _viewport;
        private readonly GraphNodeLayout _layout;
        private readonly GraphNodeMetricLabels _metricLabels;
        private readonly GraphVisualStyle _style;
        private readonly GraphPointerState _state;
        private readonly Dictionary<string, TextMeshProUGUI> _labels = new Dictionary<string, TextMeshProUGUI>();
        private readonly Dictionary<string, List<TextMeshProUGUI>> _portLabels = new Dictionary<string, List<TextMeshProUGUI>>();
        private readonly Dictionary<string, List<TextMeshProUGUI>> _slotLabels = new Dictionary<string, List<TextMeshProUGUI>>();
        private readonly Dictionary<string, List<SlotView>> _slotViews = new Dictionary<string, List<SlotView>>();
        private readonly Dictionary<string, GraphNode> _nodes = new Dictionary<string, GraphNode>();
        private readonly GraphMeshPainter _painter;
        private readonly Dictionary<string, GraphMeshLayer> _nodeLayers = new Dictionary<string, GraphMeshLayer>();
        private readonly HashSet<string> _existingNodes = new HashSet<string>();
        private readonly List<string> _staleIds = new List<string>();
        private string _frontNodeId;

        /// <summary>라벨을 둘 캔버스, 글꼴, 뷰포트, 노드 배치와 슬롯 표시 설정·상태로 라벨 관리자를 만든다.</summary>
        public GraphNodeLabels(RectTransform parent, TMP_FontAsset font, GraphViewport viewport, GraphNodeLayout layout,
            GraphMeshPainter painter, GraphVisualStyle style, GraphPointerState state)
        {
            _parent = parent;
            _font = font;
            _viewport = viewport;
            _layout = layout;
            _painter = painter;
            _style = style;
            _state = state;
            _metricLabels = new GraphNodeMetricLabels(parent, font, viewport);
        }

        /// <summary>
        /// 그래프 노드와 배치의 표시 전용 복제 노드마다 현재 컴파일 결과를 반영한 라벨을 갱신하고 사라진 노드의 라벨을 지운다.
        /// 복제 노드는 본체와 라벨을 반투명으로 표시한다. 남아 있는 노드 ID 집합을 반환한다(선택 정리용).
        /// </summary>
        public HashSet<string> Refresh(SpellGraph graph, IEnumerable<SpellGraph> library, IReadOnlyList<CompiledSpell> entries)
        {
            _existingNodes.Clear();
            _metricLabels.Refresh(graph, library, entries);
            foreach (GraphNode node in graph.Nodes) RefreshNode(node, library, 1f);
            foreach (GraphNode ghost in _layout.Ghosts) RefreshNode(ghost, library, GHOST_ALPHA);
            RemoveStale();
            return _existingNodes;
        }

        /// <summary>노드 하나의 본체 레이어와 이름·포트 라벨, 슬롯 이미지·라벨을 갱신하고 본체와 라벨·슬롯의 불투명도를 alpha로 지정한다.</summary>
        private void RefreshNode(GraphNode node, IEnumerable<SpellGraph> library, float alpha)
        {
            _existingNodes.Add(node.Id);
            _nodes[node.Id] = node;
            TextMeshProUGUI label = GetOrCreate(node.Id);
            _nodeLayers[node.Id].Initialize(_painter, node);
            _nodeLayers[node.Id].canvasRenderer.SetAlpha(alpha);
            RuneDefinition rune = GameData.Runes.Get(node.RuneId);
            string nameText = rune.Category == SpellGrammar.CATEGORY_BEHAVIOR
                ? GraphNodeLabelText.BuildNameText(node) : GraphNodeLabelText.BuildLegacyText(node, library);
            if (label.text != nameText) label.text = nameText;
            label.alpha = alpha;
            RefreshPortLabels(node);
            RefreshSlotViews(node);
            RefreshSlotLabels(node);
            foreach (TextMeshProUGUI portLabel in _portLabels[node.Id]) portLabel.alpha = alpha;
            foreach (TextMeshProUGUI slotLabel in _slotLabels[node.Id]) slotLabel.alpha = alpha;
            // 슬롯 이미지는 노드 레이어의 자식이지만 CanvasRenderer 알파는 상속되지 않아 따로 지정한다.
            foreach (SlotView view in _slotViews[node.Id])
            {
                if (view == null) continue;
                view.Image.canvasRenderer.SetAlpha(alpha);
                if (view.OutlineImage != null) view.OutlineImage.canvasRenderer.SetAlpha(alpha);
            }
        }

        /// <summary>화면 이동·확대 배율에 맞춰 이름·지표·포트와 슬롯 라벨을 배치한다. 표시 전용 복제 노드는 실제 노드 뒤에 배치하고 지표 라벨은 두지 않는다.</summary>
        public void Layout(SpellGraph graph)
        {
            float zoom = _viewport.Zoom;
            Vector3 scale = new Vector3(zoom, zoom, 1);
            for (int nodeIndex = 0; nodeIndex < graph.Nodes.Count; nodeIndex++) LayoutNode(graph.Nodes[nodeIndex], scale, true);
            foreach (GraphNode ghost in _layout.Ghosts) LayoutNode(ghost, scale, false);
            BringFrontNodeToTop();
        }

        /// <summary>노드 하나의 본체 레이어와 이름·지표·포트 라벨, 슬롯 이미지·라벨을 배율에 맞춰 배치한다. hasMetrics가 false면 지표 라벨을 건너뛴다.</summary>
        private void LayoutNode(GraphNode node, Vector3 scale, bool hasMetrics)
        {
            if (!_labels.TryGetValue(node.Id, out TextMeshProUGUI label)) return;
            float zoom = _viewport.Zoom;
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
            if (hasMetrics && GraphNodeLayout.HasSlots(rune)) _metricLabels.LayoutNode(node.Id, rect);
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
            LayoutSlotViews(node, rune, scale);
            LayoutSlotLabels(node, rune, scale);
        }

        /// <summary>드래그 중인 노드 ID를 저장해 다음 레이아웃에서도 해당 노드를 가장 앞에 배치한다.</summary>
        public void SetFrontNode(string nodeId)
        {
            _frontNodeId = nodeId;
        }

        /// <summary>선택·실행·연결 표시가 변했을 때 모든 노드 본체의 메시 갱신을 예약하고 슬롯 색·장착 가능 표시를 갱신한다.</summary>
        public void RefreshMeshes()
        {
            foreach (GraphMeshLayer layer in _nodeLayers.Values) layer.SetVerticesDirty();
            foreach (var pair in _slotViews)
            {
                if (_nodes.TryGetValue(pair.Key, out GraphNode node))
                    RefreshSlotColors(node);
            }
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

        /// <summary>행동 노드의 속성·모디 슬롯마다 표시 프리팹 이미지를 확보하고 표시할 셀이 있는 칸만 활성화한다.</summary>
        private void RefreshSlotViews(GraphNode node)
        {
            RuneDefinition rune = GameData.Runes.Get(node.RuneId);
            List<SlotView> views = GetOrCreateSlotViews(node.Id);
            int count = GraphNodeLayout.HasSlots(rune) ? GraphNodeLayout.SlotKeys.Count : 0;
            for (int index = 0; index < count; index++)
            {
                string slotKey = GraphNodeLayout.SlotKeys[index];
                SlotView view = EnsureSlotView(node, views, index, slotKey);
                bool isShown = view != null && _layout.TryGetSlotRect(node, rune, slotKey, out _);
                if (view != null)
                {
                    view.Image.gameObject.SetActive(isShown);
                    if (view.OutlineImage != null) view.OutlineImage.gameObject.SetActive(isShown);
                }
            }
            for (int index = count; index < views.Count; index++)
            {
                if (views[index] == null) continue;
                views[index].Image.gameObject.SetActive(false);
                if (views[index].OutlineImage != null) views[index].OutlineImage.gameObject.SetActive(false);
            }
            RefreshSlotColors(node);
        }

        /// <summary>노드의 슬롯 이미지 목록을 반환하고 없으면 빈 목록을 만들어 등록한다.</summary>
        private List<SlotView> GetOrCreateSlotViews(string nodeId)
        {
            if (_slotViews.TryGetValue(nodeId, out List<SlotView> views)) return views;
            views = new List<SlotView>();
            _slotViews.Add(nodeId, views);
            return views;
        }

        /// <summary>
        /// 지정 순번의 슬롯 이미지를 반환하고 없으면 슬롯 키의 종류에 맞는 프리팹을 노드 레이어 아래에 만든다.
        /// 프리팹이 연결되지 않았으면 null을 반환한다.
        /// </summary>
        private SlotView EnsureSlotView(GraphNode node, List<SlotView> views, int index, string slotKey)
        {
            if (index < views.Count) return views[index];
            bool isElement = SpellGrammar.TryParseSlotKey(slotKey, out bool elementSlot, out _) && elementSlot;
            Image prefab = isElement ? _style.ElementSlotPrefab : _style.ModifierSlotPrefab;
            if (prefab == null)
            {
                views.Add(null);
                return null;
            }
            Image image = Object.Instantiate(prefab, _nodeLayers[node.Id].rectTransform, false);
            image.name = slotKey;
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, .5f);
            Outline originalOutline = image.GetComponent<Outline>();
            if (originalOutline != null) originalOutline.enabled = false;
            Image outlineImage = null;
            if (_style.SlotOutlineSprite != null)
            {
                var outlineRoot = new GameObject("SlotOutline", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                RectTransform outlineRect = (RectTransform)outlineRoot.transform;
                outlineRect.SetParent(rect.parent, false);
                outlineRect.anchorMin = outlineRect.anchorMax = new Vector2(0f, 1f);
                outlineRect.pivot = new Vector2(0.5f, 0.5f);
                outlineRect.SetSiblingIndex(rect.GetSiblingIndex());
                outlineImage = outlineRoot.GetComponent<Image>();
                outlineImage.sprite = _style.SlotOutlineSprite;
                outlineImage.type = Image.Type.Sliced;
                outlineImage.pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier;
                outlineImage.color = Color.white;
                outlineImage.raycastTarget = false;
                outlineImage.enabled = false;
            }
            var view = new SlotView(image, outlineImage);
            views.Add(view);
            return view;
        }

        /// <summary>슬롯의 잠금색을 갱신하고 장착 가능한 칸의 뒤쪽 흰색 테두리만 표시한다.</summary>
        private void RefreshSlotColors(GraphNode node)
        {
            if (!_slotViews.TryGetValue(node.Id, out List<SlotView> views)) return;
            _state.EquippableSlots.TryGetValue(node.Id, out HashSet<string> equippable);
            for (int index = 0; index < views.Count && index < GraphNodeLayout.SlotKeys.Count; index++)
            {
                SlotView view = views[index];
                if (view == null) continue;
                string slotKey = GraphNodeLayout.SlotKeys[index];
                bool isLocked = GraphNodeLayout.IsSlotLocked(node, slotKey);
                view.Image.color = isLocked ? SLOT_LOCKED_COLOR : Color.white;
                if (view.OutlineImage != null)
                    view.OutlineImage.enabled = !isLocked && equippable != null && equippable.Contains(slotKey);
            }
        }

        /// <summary>슬롯 이미지를 각 셀 사각형에 배율에 맞춰 배치한다. 표시하지 않는 칸은 건너뛴다.</summary>
        private void LayoutSlotViews(GraphNode node, RuneDefinition rune, Vector3 scale)
        {
            if (!_slotViews.TryGetValue(node.Id, out List<SlotView> views)) return;
            float zoom = _viewport.Zoom;
            for (int index = 0; index < views.Count && index < GraphNodeLayout.SlotKeys.Count; index++)
            {
                SlotView view = views[index];
                if (view == null || !view.Image.gameObject.activeSelf) continue;
                if (!_layout.TryGetSlotRect(node, rune, GraphNodeLayout.SlotKeys[index], out Rect cell)) continue;
                RectTransform rect = view.Image.rectTransform;
                rect.anchoredPosition = new Vector2(cell.x, cell.center.y - _parent.rect.yMax);
                rect.sizeDelta = new Vector2(cell.width / zoom, cell.height / zoom) / GraphNodeLayout.SLOT_IMAGE_SCALE;
                rect.localScale = scale * GraphNodeLayout.SLOT_IMAGE_SCALE;
                if (view.OutlineImage != null)
                {
                    RectTransform outlineRect = view.OutlineImage.rectTransform;
                    outlineRect.anchoredPosition = new Vector2(cell.center.x, cell.center.y - _parent.rect.yMax);
                    outlineRect.sizeDelta = (cell.size / zoom + Vector2.one * (_style.SlotOutlineWidth * 2f))
                        / GraphNodeLayout.SLOT_IMAGE_SCALE;
                    outlineRect.localScale = rect.localScale;
                }
            }
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
                _slotViews.Remove(id);
                _nodes.Remove(id);
            }
        }

        private sealed class SlotView
        {
            public Image Image { get; }
            public Image OutlineImage { get; }

            /// <summary>슬롯 본체와 그 뒤에 배치할 흰색 테두리 이미지를 보관한다.</summary>
            public SlotView(Image image, Image outlineImage)
            {
                Image = image;
                OutlineImage = outlineImage;
            }
        }
    }
}
