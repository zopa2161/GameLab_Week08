using System.Collections.Generic;

using UnityEngine;

using TMPro;

namespace RuneCode
{
    internal sealed class GraphNodeMetricLabels
    {
        private const float DETAILS_FONT_SIZE = 15f;
        private const float DETAILS_CHARACTER_SPACING = -13f;
        private const float LEFT_PADDING = 8f;
        private const float DETAILS_TOP_OFFSET = 26f;
        private const float LABEL_HEIGHT = 40f;
        private const float LABEL_WIDTH = GraphNodeLayout.NODE_WIDTH - LEFT_PADDING * 2f;

        private readonly RectTransform _parent;
        private readonly TMP_FontAsset _font;
        private readonly GraphViewport _viewport;
        private readonly Dictionary<string, TextMeshProUGUI> _details = new Dictionary<string, TextMeshProUGUI>();
        private readonly HashSet<string> _activeBehaviors = new HashSet<string>();
        private readonly List<string> _staleIds = new List<string>();

        /// <summary>캔버스, 글꼴, 뷰포트로 행동 전력 라벨 관리자를 만든다.</summary>
        public GraphNodeMetricLabels(RectTransform parent, TMP_FontAsset font, GraphViewport viewport)
        {
            _parent = parent;
            _font = font;
            _viewport = viewport;
        }

        /// <summary>행동 노드의 전력 문구를 갱신하고 사라지거나 바뀐 노드의 라벨을 제거한다.</summary>
        public void Refresh(SpellGraph graph, IEnumerable<SpellGraph> library, IReadOnlyList<CompiledSpell> entries)
        {
            _activeBehaviors.Clear();
            foreach (GraphNode node in graph.Nodes)
            {
                if (!GraphNodeLayout.HasSlots(GameData.Runes.Get(node.RuneId))) continue;
                _activeBehaviors.Add(node.Id);
                TextMeshProUGUI details = GetOrCreate(_details, node.Id, DETAILS_FONT_SIZE);
                details.characterSpacing = DETAILS_CHARACTER_SPACING;
                string detailsText = GraphNodeLabelText.BuildBehaviorDetailsText(node, library, entries);
                if (details.text != detailsText) details.text = detailsText;
            }
            RemoveStale();
        }

        /// <summary>행동 노드 이름 바로 아래 오른쪽에 사용 전력을 배치한다.</summary>
        public void LayoutNode(string nodeId, Rect nodeRect)
        {
            float zoom = _viewport.Zoom;
            Vector3 scale = new Vector3(zoom, zoom, 1);
            LayoutDetailsLabel(_details[nodeId], nodeRect, scale);
        }

        /// <summary>지정한 노드에 전력 라벨이 있으면 가장 앞에 배치한다. 행동 노드가 아니면 생략한다.</summary>
        public void BringToFront(string nodeId)
        {
            if (_details.TryGetValue(nodeId, out TextMeshProUGUI details)) details.transform.SetAsLastSibling();
        }

        /// <summary>지정 노드의 지표를 같은 좌표계를 쓰는 드래그 레이어 또는 원래 부모로 옮긴다.</summary>
        public void SetNodeParent(string nodeId, RectTransform parent)
        {
            if (_details.TryGetValue(nodeId, out TextMeshProUGUI details) && details.transform.parent != parent)
                details.transform.SetParent(parent, false);
        }

        /// <summary>사용 전력을 노드 이름 바로 아래 오른쪽 끝에 맞춰 배치한다.</summary>
        private void LayoutDetailsLabel(TextMeshProUGUI label, Rect nodeRect, Vector3 scale)
        {
            label.gameObject.SetActive(true);
            RectTransform rect = label.rectTransform;
            float zoom = _viewport.Zoom;
            rect.anchoredPosition = new Vector2(nodeRect.x + LEFT_PADDING * zoom,
                nodeRect.yMax - _parent.rect.yMax - DETAILS_TOP_OFFSET * zoom);
            rect.sizeDelta = new Vector2(LABEL_WIDTH, LABEL_HEIGHT);
            rect.localScale = scale;
            rect.SetAsLastSibling();
        }

        /// <summary>노드별 오른쪽 정렬 TMP 라벨을 반환하고 없으면 공용 글꼴로 만든다.</summary>
        private TextMeshProUGUI GetOrCreate(Dictionary<string, TextMeshProUGUI> labels, string nodeId, float fontSize)
        {
            if (labels.TryGetValue(nodeId, out TextMeshProUGUI label)) return label;
            label = Object.Instantiate(Resources.Load<TextMeshProUGUI>("RuneCode/UI/Elements/GraphNodeLabel"), _parent, false);
            label.font = _font;
            label.fontSize = fontSize;
            label.raycastTarget = false;
            label.richText = true;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.horizontalAlignment = HorizontalAlignmentOptions.Right;
            label.verticalAlignment = VerticalAlignmentOptions.Top;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0, 1);
            label.rectTransform.pivot = new Vector2(0, 1);
            labels.Add(nodeId, label);
            return label;
        }

        /// <summary>현재 그래프에 없는 행동 노드의 전력 TMP 라벨을 파괴한다.</summary>
        private void RemoveStale()
        {
            _staleIds.Clear();
            foreach (string nodeId in _details.Keys)
                if (!_activeBehaviors.Contains(nodeId)) _staleIds.Add(nodeId);
            foreach (string nodeId in _staleIds)
            {
                Object.Destroy(_details[nodeId].gameObject);
                _details.Remove(nodeId);
            }
        }
    }
}
