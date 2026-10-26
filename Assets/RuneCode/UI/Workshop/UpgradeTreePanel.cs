using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 강화 트리 탭 View다. SO 배치 노드에서 노드·연결선을 생성해 데이터에 연결하고, 노드 클릭·포인터 진입을 알리며
    /// 요약·노드 상태·연결선 상태·정보 패널을 표시한다. 상태 계산과 구매는 UpgradeTreePresenter가 한다.
    /// </summary>
    public sealed class UpgradeTreePanel : MonoBehaviour
    {
        private const float CONTENT_MARGIN = 42;
        private const float CELL_SIZE = 150;
        private const float EXCLUSIVE_INTERMEDIATE_SCALE = 1.5f;
        private const float EXCLUSIVE_FINAL_SCALE = 1.5f;
        private static readonly Vector2 GRID_ORIGIN = new Vector2(400, -50);

        [Header("트리 데이터")]
        [SerializeField] private UpgradeTreeDefinition _definition;

        [Header("생성 프리팹")]
        [SerializeField] private UpgradeTreeNodeView _nodePrefab;
        [SerializeField] private UpgradeTreeConnectionView _connectionPrefab;

        [Header("트리 화면")]
        [SerializeField] private TextMeshProUGUI _summary;
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private RectTransform _content;
        [SerializeField] private RectTransform _hoverPanel;
        [SerializeField] private TextMeshProUGUI _hoverTitle;
        [SerializeField] private TextMeshProUGUI _hoverDescription;
        [SerializeField] private TextMeshProUGUI _hoverLevel;
        [SerializeField] private TextMeshProUGUI _hoverIncrease;
        [SerializeField] private TextMeshProUGUI _hoverCost;

        [Header("마우스 조작 안내")]
        [SerializeField] private Sprite _mouseWheelIcon;
        [SerializeField] private Sprite _mouseLeftIcon;
        [SerializeField] private Sprite _mouseRightIcon;

        [Header("배타 세트 표시")]
        [SerializeField] private Color _exclusiveGroupColor = new Color(0.45f, 0.75f, 1f, 0.12f);
        [SerializeField] private Color _exclusiveGroupOutlineColor = new Color(0.45f, 0.75f, 1f, 0.55f);
        [SerializeField, Min(0)] private float _exclusiveGroupPadding = 14;

        private readonly Dictionary<string, UpgradeTreeNodeView> _nodeViews = new Dictionary<string, UpgradeTreeNodeView>();
        private UpgradeTreeConnectionView[] _connections = Array.Empty<UpgradeTreeConnectionView>();
        private readonly List<GameObject> _exclusiveGroupPlates = new List<GameObject>();
        private readonly Vector3[] _tooltipCorners = new Vector3[4];
        private string _tooltipNodeId;
        private RectTransform _mouseControls;

        /// <summary>표시 중인 트리 자산이다.</summary>
        public UpgradeTreeDefinition Definition => _definition;

        /// <summary>노드를 눌렀을 때 노드 ID와 함께 알린다.</summary>
        public event Action<string> NodeClicked;
        public event Action<string> NodeRefundRequested;

        /// <summary>포인터가 노드에 들어오거나(true) 나갈 때(false) 노드 ID와 함께 알린다.</summary>
        public event Action<string, bool> NodeHovered;

        /// <summary>SO 배치 노드에서 노드·연결선 뷰를 생성해 데이터에 연결하고 화면과 연결선을 준비한다.</summary>
        public void Build()
        {
            EnsureMouseControls();
            _nodeViews.Clear();
            _hoverPanel.gameObject.SetActive(false);
            ClearGeneratedViews();
            HashSet<string> referencedNodeIds = CollectReferencedNodeIds();
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
                if (node != null) CreateNodeView(node, referencedNodeIds);
            CreateConnectionViews();
            ResetContent();
            CreateExclusiveGroupPlates();
            foreach (UpgradeTreeConnectionView connection in _connections) connection.RefreshLayout();
        }

        /// <summary>트리 뷰포트 우측 하단에 강화 트리 마우스 조작 안내를 패널당 한 번만 만들고 이후 재진입에서는 재사용한다.</summary>
        private void EnsureMouseControls()
        {
            if (_mouseControls != null) return;
            TMP_FontAsset font = _summary.font;
            _mouseControls = UiFactory.CreateMouseControls(_viewport, font, _mouseWheelIcon, _mouseLeftIcon,
                _mouseRightIcon, GameData.L("ui.mouseControls.upgrade"), GameData.L("ui.mouseControls.refund"));
        }

        /// <summary>이전 Build에서 생성한 노드·연결선 뷰를 제거해 재진입에 대비한다.</summary>
        private void ClearGeneratedViews()
        {
            foreach (GameObject plate in _exclusiveGroupPlates) Destroy(plate);
            _exclusiveGroupPlates.Clear();
            for (int index = _content.childCount - 1; index >= 0; index--)
            {
                Transform child = _content.GetChild(index);
                if (child.GetComponent<UpgradeTreeNodeView>() != null || child.GetComponent<UpgradeTreeConnectionView>() != null)
                    Destroy(child.gameObject);
            }
        }

        /// <summary>배치 노드 하나의 뷰를 생성하고 데이터 ID와 격자 셀 좌표를 설정한 뒤 바인딩한다. 배타 세트 노드는 뒤에 선행되는 노드가 있으면 중간·없으면 마지막 분기로 키워드를 구분한다.</summary>
        private void CreateNodeView(UpgradeTreeNodeDefinition node, HashSet<string> referencedNodeIds)
        {
            UpgradeTreeNodeView view = Instantiate(_nodePrefab, _content);
            view.gameObject.name = node.Id;
            view.ConfigureNodeId(node.Id);
            RectTransform rect = (RectTransform)view.transform;
            rect.anchoredPosition = CellToAnchoredPosition(node.LayoutCell);
            if (!string.IsNullOrWhiteSpace(node.ExclusiveGroupId))
            {
                bool isFinal = !referencedNodeIds.Contains(node.Id);
                float scale = isFinal ? EXCLUSIVE_FINAL_SCALE : EXCLUSIVE_INTERMEDIATE_SCALE;
                view.ConfigureExclusiveNode(isFinal);
                // 좌상단 기준점이라 늘어난 변만큼 위치를 옮겨 노드 중심을 원래 셀에 둔다.
                rect.localScale = Vector3.one * scale;
                rect.anchoredPosition += new Vector2(-rect.rect.width, rect.rect.height) * ((scale - 1) * 0.5f);
            }
            BindNode(view);
        }

        /// <summary>다른 노드의 선행 조건으로 인용된 노드 ID를 모은다. 배타 세트 노드가 뒤에 노드를 두는지 판단하는 데만 쓴다.</summary>
        private HashSet<string> CollectReferencedNodeIds()
        {
            HashSet<string> referencedNodeIds = new HashSet<string>();
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
            {
                if (node == null) continue;
                foreach (UpgradeTreePrerequisite prerequisite in node.Prerequisites)
                    if (prerequisite != null) referencedNodeIds.Add(prerequisite.NodeId);
            }
            return referencedNodeIds;
        }

        /// <summary>배타 세트마다 소속 노드 사각형을 여백만큼 감싸는 반투명 배경판을 만들어 연결선 뒤에 둔다. ResetContent 이후 좌상단 기준 콘텐츠 좌표를 사용한다.</summary>
        private void CreateExclusiveGroupPlates()
        {
            Dictionary<string, Rect> groupBounds = new Dictionary<string, Rect>();
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.ExclusiveGroupId) ||
                    !_nodeViews.TryGetValue(node.Id, out UpgradeTreeNodeView view)) continue;
                Rect bounds = GetNodeBounds(view);
                if (groupBounds.TryGetValue(node.ExclusiveGroupId, out Rect current))
                    bounds = Rect.MinMaxRect(Mathf.Min(current.xMin, bounds.xMin), Mathf.Min(current.yMin, bounds.yMin),
                        Mathf.Max(current.xMax, bounds.xMax), Mathf.Max(current.yMax, bounds.yMax));
                groupBounds[node.ExclusiveGroupId] = bounds;
            }
            foreach (KeyValuePair<string, Rect> pair in groupBounds)
            {
                GameObject plate = new GameObject("ExclusiveGroup " + pair.Key, typeof(RectTransform), typeof(Image), typeof(Outline));
                RectTransform rect = (RectTransform)plate.transform;
                rect.SetParent(_content, false);
                rect.SetAsFirstSibling();
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = pair.Value.center;
                rect.sizeDelta = pair.Value.size + Vector2.one * (_exclusiveGroupPadding * 2);
                Image image = plate.GetComponent<Image>();
                image.color = _exclusiveGroupColor;
                image.raycastTarget = false;
                Outline outline = plate.GetComponent<Outline>();
                outline.effectColor = _exclusiveGroupOutlineColor;
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                _exclusiveGroupPlates.Add(plate);
            }
        }

        /// <summary>배치 노드의 선행 조건마다 연결선 뷰를 생성해 부모·자식 노드로 연결하고, 노드 뒤에 그려지도록 형제 순서를 맨 앞으로 보낸다.</summary>
        private void CreateConnectionViews()
        {
            List<UpgradeTreeConnectionView> connections = new List<UpgradeTreeConnectionView>();
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
            {
                if (node == null || node.Prerequisites.Count == 0 ||
                    !_nodeViews.TryGetValue(node.Id, out UpgradeTreeNodeView targetView)) continue;
                foreach (UpgradeTreePrerequisite prerequisite in node.Prerequisites)
                {
                    if (prerequisite == null ||
                        !_nodeViews.TryGetValue(prerequisite.NodeId, out UpgradeTreeNodeView sourceView)) continue;
                    UpgradeTreeConnectionView connection = Instantiate(_connectionPrefab, _content);
                    connection.gameObject.name = sourceView.NodeId + " To " + targetView.NodeId;
                    connection.transform.SetAsFirstSibling();
                    connection.Configure(sourceView, targetView);
                    connections.Add(connection);
                }
            }
            _connections = connections.ToArray();
        }

        /// <summary>SO의 격자 셀을 콘텐츠 로컬 좌표로 변환한다.</summary>
        private static Vector2 CellToAnchoredPosition(Vector2Int cell)
        {
            return GRID_ORIGIN + new Vector2(cell.x * CELL_SIZE, -cell.y * CELL_SIZE);
        }

        /// <summary>상단 요약 문구를 표시한다.</summary>
        public void SetSummary(string text) => _summary.text = text;

        /// <summary>노드 효과·룬 분류와 레벨 채움·다음 구매 비용·열림·완료·구매 가능 상태를 표시한다.</summary>
        public void SetNodeState(string nodeId, UpgradeEffectType effectType, string runeCategory, Sprite icon, int level, int maxLevel, int nextCost, bool isAvailable, bool isComplete, bool canPurchase, string runeId = null)
        {
            if (_nodeViews.TryGetValue(nodeId, out UpgradeTreeNodeView view)) view.SetContent(effectType, runeCategory, icon, level, maxLevel, nextCost, isAvailable, isComplete, canPurchase, runeId);
        }

        /// <summary>노드 ID별 학습 여부를 받아 연결된 양쪽 노드를 모두 배웠을 때만 연결선을 강조한다.</summary>
        public void SetConnectionStates(Func<string, bool> isLearned)
        {
            foreach (UpgradeTreeConnectionView connection in _connections)
                connection.SetLearned(isLearned(connection.SourceNodeId) && isLearned(connection.TargetNodeId));
        }

        /// <summary>노드 위 정보 상자에 이름·설명·강화 레벨·다음 상승량·비용을 표시한다.</summary>
        public void ShowTooltip(string nodeId, string title, string description, string level, string increase, string cost)
        {
            _tooltipNodeId = nodeId;
            _hoverPanel.gameObject.SetActive(true);
            _hoverTitle.text = title;
            _hoverDescription.text = description;
            _hoverLevel.gameObject.SetActive(!string.IsNullOrEmpty(level));
            _hoverLevel.text = level;
            _hoverIncrease.text = increase;
            _hoverCost.text = cost;
            PositionTooltip();
        }

        /// <summary>정보 패널을 닫는다.</summary>
        public void HideTooltip()
        {
            _tooltipNodeId = null;
            _hoverPanel.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_hoverPanel.gameObject.activeSelf) PositionTooltip();
        }

        /// <summary>확대·이동 후에도 툴팁을 포인터가 놓인 노드의 위쪽에 유지한다.</summary>
        private void PositionTooltip()
        {
            if (!_nodeViews.TryGetValue(_tooltipNodeId, out UpgradeTreeNodeView view)) return;
            RectTransform nodeRect = (RectTransform)view.transform;
            RectTransform parent = (RectTransform)_hoverPanel.parent;
            nodeRect.GetWorldCorners(_tooltipCorners);
            Vector3 left = parent.InverseTransformPoint(_tooltipCorners[1]);
            Vector3 right = parent.InverseTransformPoint(_tooltipCorners[2]);
            float halfWidth = _hoverPanel.rect.width * 0.5f;
            float x = Mathf.Clamp((left.x + right.x) * 0.5f, halfWidth + 8, parent.rect.width - halfWidth - 8);
            _hoverPanel.anchoredPosition = new Vector2(x, Mathf.Max(left.y, right.y) + 8);
        }

        /// <summary>콘텐츠를 좌상단 기준·최소 줌으로 되돌리고 전체 노드 영역을 화면 중앙에 배치하며, 모든 노드가 들어가는 콘텐츠 크기로 맞춘다.</summary>
        private void ResetContent()
        {
            _content.anchorMin = _content.anchorMax = new Vector2(0, 1);
            _content.pivot = new Vector2(0, 1);
            float initialZoom = UpgradeTreeCanvasInput.MinimumZoom;
            _content.localScale = Vector3.one * initialZoom;
            float rightEdge = 0;
            float bottomEdge = 0;
            Vector2 minimum = Vector2.positiveInfinity;
            Vector2 maximum = Vector2.negativeInfinity;
            foreach (UpgradeTreeNodeView view in _nodeViews.Values)
            {
                Rect bounds = GetNodeBounds(view);
                minimum = Vector2.Min(minimum, bounds.min);
                maximum = Vector2.Max(maximum, bounds.max);
                rightEdge = Mathf.Max(rightEdge, bounds.xMax);
                bottomEdge = Mathf.Max(bottomEdge, -bounds.yMin);
            }
            Vector2 viewportCenter = new Vector2(_viewport.rect.width * 0.5f, -_viewport.rect.height * 0.5f);
            _content.anchoredPosition = _nodeViews.Count > 0
                ? viewportCenter - (minimum + maximum) * (0.5f * initialZoom)
                : Vector2.zero;
            _content.sizeDelta = new Vector2(Mathf.Max(_viewport.rect.width, rightEdge + CONTENT_MARGIN),
                Mathf.Max(_viewport.rect.height, bottomEdge + CONTENT_MARGIN));
        }

        /// <summary>배치한 노드의 사각형을 이동·확대 전 콘텐츠 로컬 좌표로 반환한다.</summary>
        private Rect GetNodeBounds(UpgradeTreeNodeView view)
        {
            RectTransform rect = (RectTransform)view.transform;
            rect.GetWorldCorners(_tooltipCorners);
            Vector2 minimum = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 maximum = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 corner in _tooltipCorners)
            {
                Vector2 local = _content.InverseTransformPoint(corner);
                minimum = Vector2.Min(minimum, local);
                maximum = Vector2.Max(maximum, local);
            }
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        /// <summary>프리팹 노드 ID의 데이터 존재·중복을 확인하고 클릭·호버 이벤트를 연결한다. 노드 위치는 변경하지 않는다.</summary>
        private void BindNode(UpgradeTreeNodeView view)
        {
            string nodeId = view.NodeId;
            if (string.IsNullOrEmpty(nodeId) || _definition.FindNode(nodeId) == null || _nodeViews.ContainsKey(nodeId))
            {
                Debug.LogError("[UpgradeTree] 배치 노드 ID가 비어 있거나 SO에 없거나 중복됩니다: " + nodeId, view);
                return;
            }
            view.SetPurchaseAction(() => NodeClicked?.Invoke(nodeId));
            view.SetRefundAction(() => NodeRefundRequested?.Invoke(nodeId));
            view.SetHoverAction(isHovered => NodeHovered?.Invoke(nodeId, isHovered));
            _nodeViews.Add(nodeId, view);
        }
    }
}
