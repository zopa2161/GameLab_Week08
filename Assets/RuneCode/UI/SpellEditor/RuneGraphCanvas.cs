using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 마법 그래프 편집 View다. 포인터 입력(연결·노드 드래그·화면 이동·영역 선택·확대·연결선 삭제·슬롯 장착과 해제)을 해석해 편집 세션에 전달하고,
    /// 그리기는 GraphMeshPainter, 라벨은 GraphNodeLabels, 좌표는 GraphViewport·GraphNodeLayout, 배치는 GraphPlacement에 맡긴다.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RuneGraphCanvas : MaskableGraphic,
        IPointerDownHandler, IPointerUpHandler, IPointerMoveHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        private const float HIGHLIGHT_DURATION = 0.2f;
        private const float NODE_DRAG_THRESHOLD = 1f;

        [Header("연결 판정")]
        [SerializeField, Min(0f)] private float _connectionSnapPadding = 24f;

        private readonly GraphPointerState _state = new GraphPointerState();
        private readonly List<string> _expiredHighlights = new List<string>();

        private ISpellEditor _editor;
        private Action<GraphNode> _selectionChanged;
        private Action<GraphNode, string> _hoverChanged;
        private Action<Vector2> _quickPlacement;
        private Action<string, string> _slotPicker;
        private GraphViewport _viewport;
        private GraphNodeLayout _layout;
        private GraphMeshPainter _painter;
        private GraphNodeLabels _labels;
        private GraphMeshLayer _overlayLayer;
        private GraphPlacement _placement;
        private GraphNode _hoveredNode;
        private string _hoveredSlot;
        private bool _isHoverValid;

        /// <summary>선택된 노드 ID 집합이다.</summary>
        public IReadOnlyCollection<string> SelectedNodes => _state.Selected;

        /// <summary>행동 노드 하나만 선택되어 있으면 그 노드 ID를, 아니면 null을 반환한다. 팔레트 클릭 장착 대상이다.</summary>
        public string SelectedBehaviorNodeId
        {
            get
            {
                if (!HasGraph || _state.Selected.Count != 1) return null;
                foreach (string id in _state.Selected)
                {
                    GraphNode node = _layout.FindNode(id);
                    if (node != null && GraphNodeLayout.HasSlots(GameData.Runes.Get(node.RuneId))) return id;
                }
                return null;
            }
        }

        private bool HasGraph => _editor != null && _editor.Graph != null;

        /// <summary>
        /// 편집 세션, 글꼴과 선택·포인터 진입·빠른 배치·슬롯 선택(노드 ID, 슬롯 키) 콜백을 연결하고 그래프를 표시한다.
        /// </summary>
        public void Initialize(ISpellEditor editor, TMP_FontAsset font, Action<GraphNode> selectionChanged,
            Action<GraphNode, string> hoverChanged, Action<Vector2> quickPlacement, Action<string, string> slotPicker)
        {
            _editor = editor;
            _selectionChanged = selectionChanged;
            _hoverChanged = hoverChanged;
            _quickPlacement = quickPlacement;
            _slotPicker = slotPicker;
            _viewport = new GraphViewport(rectTransform);
            _layout = new GraphNodeLayout(_viewport, () => _editor.Graph);
            _painter = new GraphMeshPainter(_viewport, _layout, _state,
                Resources.Load<GraphVisualStyle>("RuneCode/UI/Elements/GraphVisualStyle"), _editor.IsRuneUnlocked);
            _labels = new GraphNodeLabels(rectTransform, font, _viewport, _layout, _painter);
            _overlayLayer = GraphMeshLayer.Create(rectTransform, "GraphInteractionOverlay");
            _overlayLayer.Initialize(_painter);
            _placement = new GraphPlacement(() => _editor, _viewport);
            raycastTarget = true;
            ResetView();
        }

        /// <summary>그래프 전체가 보이도록 화면 위치와 배율을 맞추고 표시를 갱신한다.</summary>
        public void ResetView()
        {
            if (HasGraph && _layout.TryGetGraphBounds(out Vector2 minimum, out Vector2 maximum)) _viewport.Fit(minimum, maximum);
            RefreshGraph();
        }

        /// <summary>노드 라벨 내용·배치와 메시를 갱신하고 사라진 노드의 선택을 해제한다.</summary>
        public void RefreshGraph()
        {
            if (!HasGraph) return;
            HashSet<string> existing = _labels.Refresh(_editor.Graph, _editor.Library);
            _state.Selected.RemoveWhere(id => !existing.Contains(id));
            _isHoverValid = false;
            LayoutLabels();
        }

        /// <summary>지정 노드를 실행 하이라이트 상태로 잠시 표시한다.</summary>
        public void Highlight(string nodeId)
        {
            _state.ExecutedUntil[nodeId] = Time.unscaledTime + HIGHLIGHT_DURATION;
            RefreshMeshes();
        }

        /// <summary>지정 노드가 화면 가운데에 보이도록 화면 위치를 맞추고 선택한다.</summary>
        public void FocusNode(string nodeId)
        {
            GraphNode node = _layout.FindNode(nodeId);
            if (node == null) return;
            _viewport.CenterOn(new Vector2(node.X + GraphNodeLayout.NODE_WIDTH / 2, node.Y));
            _state.Selected.Clear();
            _state.Selected.Add(nodeId);
            _selectionChanged(node);
            RefreshGraph();
        }

        /// <summary>선택한 노드와 그 연결을 삭제한다. 삭제할 수 없는 시전 노드는 편집 세션이 남긴다.</summary>
        public void DeleteSelection()
        {
            foreach (string id in new List<string>(_state.Selected)) _editor.RemoveNode(id);
            _state.Selected.Clear();
            _selectionChanged(null);
            RefreshGraph();
        }

        /// <summary>화면 좌표가 편집 영역 안에 있으면 true와 함께 배치할 그래프 좌표를 반환한다.</summary>
        public bool TryGetGraphPoint(Vector2 screen, out Vector2 graphPoint)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, null, out Vector2 point);
            graphPoint = _viewport.GraphPoint(point);
            return rectTransform.rect.Contains(point);
        }

        /// <summary>현재 보이는 캔버스 안에서 새 룬을 놓을 그래프 좌표를 반환한다.</summary>
        public Vector2 SuggestPlacement(string runeId) => _placement.Suggest(runeId);

        /// <summary>그래프 좌표에 행동 노드를 지정 등급으로 배치한다.</summary>
        public void PlaceRune(string runeId, Vector2 position, string grade = null) => _placement.Place(runeId, position, grade);

        /// <summary>
        /// 화면 좌표에 속성·모디를 놓는다. 같은 종류의 잠기지 않은 슬롯 위면 그 슬롯에(교체 포함), 행동 노드 본문 위면 첫 빈 슬롯에 장착한다.
        /// 장착하지 못하면 false를 반환한다.
        /// </summary>
        public bool DropSlotItem(string runeId, string grade, Vector2 screen)
        {
            if (!HasGraph || !GameData.Runes.TryGet(runeId, out RuneDefinition rune)) return false;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, null, out Vector2 point);
            if (!rectTransform.rect.Contains(point)) return false;
            bool isElement = rune.Category == SpellGrammar.CATEGORY_ELEMENT;
            if (_layout.FindSlot(point, out GraphNode slotNode, out string slotKey)
                && SpellGrammar.TryParseSlotKey(slotKey, out bool isElementSlot, out _) && isElementSlot == isElement
                && !GraphNodeLayout.IsSlotLocked(slotNode, slotKey))
                return _editor.Equip(slotNode.Id, slotKey, runeId, grade);
            GraphNode node = _layout.FindNodeAt(point);
            if (node == null || !GraphNodeLayout.HasSlots(GameData.Runes.Get(node.RuneId))) return false;
            if (!_editor.TryFindEquipSlot(node.Id, runeId, grade, out string emptySlot))
            {
                _editor.ReportStatus("editor.slotBlocked");
                return false;
            }
            return _editor.Equip(node.Id, emptySlot, runeId, grade);
        }

        void Update()
        {
            if (_state.ExecutedUntil.Count == 0) return;
            _expiredHighlights.Clear();
            foreach (var pair in _state.ExecutedUntil)
            {
                if (pair.Value <= Time.unscaledTime) _expiredHighlights.Add(pair.Key);
            }
            if (_expiredHighlights.Count == 0) return;
            foreach (string id in _expiredHighlights) _state.ExecutedUntil.Remove(id);
            RefreshMeshes();
        }

        /// <summary>배경과 연결선의 기본 메시를 구성한다. 노드와 조작 표시는 자식 레이어가 그린다.</summary>
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            if (_painter == null)
            {
                mesh.Clear();
                return;
            }
            _painter.Paint(mesh, HasGraph ? _editor.Graph : null);
        }

        /// <summary>포인터 위치에서 연결, 노드 선택·드래그, 화면 이동, 연결선 삭제 또는 영역 선택을 시작한다.</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            _state.Pointer = Local(eventData);
            _state.DragStart = _state.Pointer;
            _state.PanStart = _viewport.Pan;
            if (LocalTelemetry.IsUsabilityTestActive)
            {
                LocalTelemetry.RecordUsabilityEvent("editor_click", "editor", new Dictionary<string, object>
                {
                    ["input_id"] = Guid.NewGuid().ToString("N"),
                    ["button"] = eventData.button.ToString(),
                    ["screen_position"] = new[] { eventData.position.x, eventData.position.y },
                    ["canvas_position"] = new[] { _state.Pointer.x, _state.Pointer.y },
                });
            }

            if (eventData.button == PointerEventData.InputButton.Middle)
            {
                _state.IsPanning = true;
                return;
            }
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                if (_layout.FindSlot(_state.Pointer, out GraphNode clearNode, out string clearKey) && _editor.ClearSlot(clearNode.Id, clearKey)) return;
                if (TryRemoveEdgeAt(_state.Pointer)) RefreshGraph();
                else _state.IsPanning = true;
                return;
            }
            if (_layout.FindPort(_state.Pointer, out GraphNode portNode, out PortDefinition port))
            {
                BeginConnection(portNode, port);
                RefreshMeshes();
                return;
            }

            if (TryHandleSlotClick(_state.Pointer)) return;

            GraphNode clicked = _layout.FindNodeAt(_state.Pointer);
            if (clicked != null) BeginNodeDrag(clicked, IsShiftPressed());
            else if (eventData.clickCount >= 2)
            {
                _quickPlacement(_viewport.GraphPoint(_state.Pointer));
                return;
            }
            else BeginBoxSelection(IsShiftPressed());
            RefreshMeshes();
        }

        /// <summary>포인터 이동에 맞춰 화면 위치, 선택 노드 위치 또는 연결 미리보기·영역 선택을 갱신한다.</summary>
        public void OnDrag(PointerEventData eventData)
        {
            _state.Pointer = Local(eventData);
            if (_state.IsPanning) _viewport.SetPan(_state.PanStart + _state.Pointer - _state.DragStart);
            if (_state.IsNodeDragging) MoveDraggedNodes();
            if (_state.IsPanning || _state.IsNodeDragging) LayoutLabels();
            else RefreshMeshes();
        }

        /// <summary>연결 시도와 영역 선택을 확정하고, 노드를 실제로 옮겼으면 편집 세션에 변경을 알린다.</summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            _state.Pointer = Local(eventData);
            TryCompleteConnection();
            if (_state.IsBoxSelecting) SelectNodesInBox();
            if (_state.IsNodeDragging && Vector2.Distance(_state.Pointer, _state.DragStart) > NODE_DRAG_THRESHOLD)
            {
                _editor.MarkChanged();
                if (LocalTelemetry.IsUsabilityTestActive)
                    LocalTelemetry.RecordUsabilityEvent("editor_action_result", "editor", new Dictionary<string, object>
                    {
                        ["action"] = "move_nodes",
                        ["input_method"] = "drag",
                        ["result"] = "success",
                        ["node_ids"] = new List<string>(_state.Selected),
                    });
            }
            _labels.SetFrontNode(null);
            _state.ResetPointer();
            RefreshGraph();
        }

        /// <summary>
        /// 슬롯 셀 좌클릭을 처리한다. 잠기지 않은 속성·모디 셀은 슬롯 선택을 연다.
        /// 처리했으면 true, 잠긴 셀·슬롯 밖이면 노드 선택으로 넘기도록 false를 반환한다.
        /// </summary>
        private bool TryHandleSlotClick(Vector2 point)
        {
            if (!_layout.FindSlot(point, out GraphNode node, out string slotKey)) return false;
            if (GraphNodeLayout.IsSlotLocked(node, slotKey)) return false;
            _slotPicker(node.Id, slotKey);
            return true;
        }

        /// <summary>그래프 드래그를 수신하도록 Unity 이벤트 시스템에 알린다.</summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
        }

        /// <summary>포인터 해제와 같은 그래프 드래그 종료 절차를 수행한다.</summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            OnPointerUp(eventData);
        }

        /// <summary>마우스 위치를 중심으로 그래프를 확대·축소한다.</summary>
        public void OnScroll(PointerEventData eventData)
        {
            _viewport.ZoomAt(Local(eventData), eventData.scrollDelta.y > 0);
            LayoutLabels();
        }

        /// <summary>포인터 아래 노드가 바뀐 경우에만 툴팁용 노드를 알린다.</summary>
        public void OnPointerMove(PointerEventData eventData)
        {
            if (!HasGraph) return;
            Vector2 point = Local(eventData);
            GraphNode hovered = _layout.FindNodeAt(point);
            string slotKey = null;
            if (_layout.FindSlot(point, out GraphNode slotNode, out string foundKey))
            {
                hovered = slotNode;
                slotKey = foundKey;
            }
            if (_isHoverValid && hovered == _hoveredNode && slotKey == _hoveredSlot) return;
            _hoveredSlot = slotKey;
            _hoveredNode = hovered;
            _isHoverValid = true;
            _hoverChanged(hovered, slotKey);
        }

        /// <summary>그래프를 벗어나면 호버 상태와 안내를 초기화한다.</summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            _isHoverValid = false;
            _hoverChanged?.Invoke(null, null);
        }

        /// <summary>화면 이동·확대에 맞춰 라벨을 배치하고 메시 재구성을 예약한다.</summary>
        private void LayoutLabels()
        {
            if (!HasGraph) return;
            _labels.Layout(_editor.Graph);
            _overlayLayer.transform.SetAsLastSibling();
            RefreshMeshes();
        }

        /// <summary>배경·연결선·노드 본체·최상단 조작 표시의 메시 갱신을 함께 예약한다.</summary>
        private void RefreshMeshes()
        {
            SetVerticesDirty();
            _labels.RefreshMeshes();
            _overlayLayer.SetVerticesDirty();
        }

        /// <summary>포인터 근처 연결선을 찾아 삭제하고 삭제 여부를 반환한다.</summary>
        private bool TryRemoveEdgeAt(Vector2 point)
        {
            GraphEdge edge = _layout.FindEdgeAt(point);
            if (edge == null) return false;
            _editor.RemoveEdge(edge.Id);
            return true;
        }

        /// <summary>지정 포트를 연결 시작점으로 저장하고 다른 포트의 연결 가능 여부를 계산해 둔다.</summary>
        private void BeginConnection(GraphNode node, PortDefinition port)
        {
            _state.SourceNode = node.Id;
            _state.SourcePort = port.Id;
            _state.IsSourceOutput = port.Direction == SpellGrammar.DIRECTION_OUT;
            CacheConnectablePorts();
        }

        /// <summary>
        /// 연결 시작 포트 기준으로 모든 노드 포트의 연결 가능 여부를 노드 ID별 포트 순번 배열로 저장한다.
        /// 연결 드래그 동안 그래프가 바뀌지 않으므로 메시 재구성마다 규칙 검사를 반복하지 않는다.
        /// </summary>
        private void CacheConnectablePorts()
        {
            _state.ConnectablePorts.Clear();
            string oppositeDirection = _state.IsSourceOutput ? SpellGrammar.DIRECTION_IN : SpellGrammar.DIRECTION_OUT;
            foreach (GraphNode node in _editor.Graph.Nodes)
            {
                IReadOnlyList<PortDefinition> ports = GameData.Runes.Get(node.RuneId).Ports;
                var connectable = new bool[ports.Count];
                for (int i = 0; i < ports.Count; i++)
                {
                    if (ports[i].Direction != oppositeDirection) continue;
                    connectable[i] = _state.IsSourceOutput
                        ? _editor.CanConnectPorts(_state.SourceNode, _state.SourcePort, node.Id, ports[i].Id, true)
                        : _editor.CanConnectPorts(node.Id, ports[i].Id, _state.SourceNode, _state.SourcePort, true);
                }
                _state.ConnectablePorts[node.Id] = connectable;
            }
        }

        /// <summary>포인터 근처의 유효 포트를 찾아 방향에 맞춰 연결하고 점유된 단일 포트의 연결을 교체한다.</summary>
        private void TryCompleteConnection()
        {
            if (!_state.IsConnecting) return;
            if (!rectTransform.rect.Contains(_state.Pointer) || !TryFindConnectionTarget(out GraphNode node, out PortDefinition port)) return;
            if (_state.IsSourceOutput) _editor.Connect(_state.SourceNode, _state.SourcePort, node.Id, port.Id, true);
            else _editor.Connect(node.Id, port.Id, _state.SourceNode, _state.SourcePort, true);
        }

        /// <summary>정확히 가리킨 포트를 우선하며 노드 본체와 주변 여백에서는 가장 가까운 연결 가능 포트를 반환한다.</summary>
        private bool TryFindConnectionTarget(out GraphNode foundNode, out PortDefinition foundPort)
        {
            if (_layout.FindPort(_state.Pointer, out foundNode, out foundPort) && foundNode.Id != _state.SourceNode)
            {
                return _state.IsSourceOutput
                    ? _editor.CanConnectPorts(_state.SourceNode, _state.SourcePort, foundNode.Id, foundPort.Id, true)
                    : _editor.CanConnectPorts(foundNode.Id, foundPort.Id, _state.SourceNode, _state.SourcePort, true);
            }

            foundNode = null;
            foundPort = null;
            float closestNodeDistance = float.PositiveInfinity;
            float closestPortDistance = float.PositiveInfinity;
            IReadOnlyList<GraphNode> nodes = _editor.Graph.Nodes;
            for (int n = nodes.Count - 1; n >= 0; n--)
            {
                GraphNode node = nodes[n];
                if (node.Id == _state.SourceNode || !_state.ConnectablePorts.TryGetValue(node.Id, out bool[] connectable)) continue;
                RuneDefinition rune = GameData.Runes.Get(node.RuneId);
                Rect rect = _layout.NodeRect(node, rune);
                Vector2 nearest = new Vector2(Mathf.Clamp(_state.Pointer.x, rect.xMin, rect.xMax),
                    Mathf.Clamp(_state.Pointer.y, rect.yMin, rect.yMax));
                float nodeDistance = (_state.Pointer - nearest).sqrMagnitude;
                if (nodeDistance > _connectionSnapPadding * _connectionSnapPadding || nodeDistance > closestNodeDistance) continue;
                for (int p = 0; p < rune.Ports.Count; p++)
                {
                    PortDefinition port = rune.Ports[p];
                    if (!connectable[p] || !_layout.IsPortShown(node, port)) continue;
                    float portDistance = (_state.Pointer - _layout.PortPosition(rect, node, rune, port.Id)).sqrMagnitude;
                    if (nodeDistance == closestNodeDistance && portDistance >= closestPortDistance) continue;
                    closestNodeDistance = nodeDistance;
                    closestPortDistance = portDistance;
                    foundNode = node;
                    foundPort = port;
                }
            }
            return foundNode != null;
        }

        /// <summary>클릭한 노드로 선택을 갱신하고 선택된 노드들의 드래그 시작 좌표를 저장한다.</summary>
        private void BeginNodeDrag(GraphNode clicked, bool isShift)
        {
            // Shift 없이 선택 밖 노드를 누르면 단일 선택, Shift를 누르면 선택을 토글한다.
            HashSet<string> selected = _state.Selected;
            if (!isShift && !selected.Contains(clicked.Id)) selected.Clear();
            if (isShift && selected.Contains(clicked.Id)) selected.Remove(clicked.Id);
            else selected.Add(clicked.Id);
            _selectionChanged(clicked);

            _state.IsNodeDragging = true;
            _state.DragOrigins.Clear();
            foreach (string id in selected)
            {
                GraphNode node = _layout.FindNode(id);
                if (node != null) _state.DragOrigins[id] = new Vector2(node.X, node.Y);
            }
            _labels.SetFrontNode(_state.DragOrigins.ContainsKey(clicked.Id) ? clicked.Id : null);
            LayoutLabels();
            _editor.BeginEdit();
        }

        /// <summary>드래그 시작 이후 포인터 이동량만큼 선택된 노드들을 그래프 좌표에서 옮긴다.</summary>
        private void MoveDraggedNodes()
        {
            Vector2 difference = (_state.Pointer - _state.DragStart) / _viewport.Zoom;
            foreach (var pair in _state.DragOrigins) _layout.FindNode(pair.Key)?.Move(pair.Value.x + difference.x, pair.Value.y - difference.y);
        }

        /// <summary>Shift가 없으면 기존 선택을 해제하고 영역 선택을 시작한다.</summary>
        private void BeginBoxSelection(bool isShift)
        {
            if (!isShift) _state.Selected.Clear();
            _state.IsBoxSelecting = true;
            _selectionChanged(null);
        }

        /// <summary>영역 선택 사각형과 겹치는 노드를 선택에 추가한다.</summary>
        private void SelectNodesInBox()
        {
            Rect area = _state.SelectionRect;
            foreach (GraphNode candidate in _editor.Graph.Nodes)
            {
                if (area.Overlaps(_layout.NodeRect(candidate, GameData.Runes.Get(candidate.RuneId)))) _state.Selected.Add(candidate.Id);
            }
        }

        /// <summary>화면 포인터를 그래프 RectTransform의 로컬 좌표로 변환한다.</summary>
        private Vector2 Local(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 position);
            return position;
        }

        /// <summary>왼쪽 또는 오른쪽 Shift 키가 눌려 있는지 반환한다.</summary>
        private static bool IsShiftPressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }
    }
}
