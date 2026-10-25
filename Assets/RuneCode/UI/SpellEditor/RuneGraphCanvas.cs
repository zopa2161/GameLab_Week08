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
        [SerializeField, Min(0f)] private float _puzzleSnapDistance = 42f;

        private readonly GraphPointerState _state = new GraphPointerState();
        private readonly List<string> _expiredHighlights = new List<string>();

        private ISpellEditor _editor;
        private TMP_FontAsset _font;
        private Action<GraphNode> _selectionChanged;
        private Action<GraphNode, string> _hoverChanged;
        private Action<Vector2> _quickPlacement;
        private Func<RuneDefinition, string, RunePalettePreview> _slotPreviewFactory;
        private GraphViewport _viewport;
        private GraphNodeLayout _layout;
        private GraphMeshPainter _painter;
        private GraphNodeLabels _labels;
        private GraphMeshLayer _overlayLayer;
        private GraphPlacement _placement;
        private RectTransform _paletteDropTarget;
        private GraphNode _hoveredNode;
        private string _hoveredSlot;
        private bool _isHoverValid;

        private RunePalettePreview _pickedModulePreview;
        private string _pickedRuneId;
        private string _pickedGrade;
        private string _pickedSourceNodeId;
        private string _pickedSourceSlotKey;
        private bool _isPickupPressActive;
        private bool _isSlotPickupDragged;
        private int _moduleInputFrame = -1;
        private bool _hasDetachedDrag;
        private bool _isBehaviorDrag;

        public bool HasPickedModule => _pickedModulePreview != null;

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
        /// 편집 세션, 글꼴과 선택·포인터 진입·빠른 배치 콜백을 연결하고 그래프를 표시한다.
        /// </summary>
        public void Initialize(ISpellEditor editor, TMP_FontAsset font, Action<GraphNode> selectionChanged,
            Action<GraphNode, string> hoverChanged, Action<Vector2> quickPlacement)
        {
            _editor = editor;
            _font = font;
            _selectionChanged = selectionChanged;
            _hoverChanged = hoverChanged;
            _quickPlacement = quickPlacement;
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

        /// <summary>좌측 룬 팔레트의 화면 영역을 지정한다. 이 영역에 노드나 장착 룬을 놓으면 팔레트로 반환한다.</summary>
        internal void SetPaletteDropTarget(RectTransform palette)
        {
            _paletteDropTarget = palette;
        }

        /// <summary>슬롯에서 집어 든 룬의 드래그 미리보기를 만드는 팩토리를 지정한다. 지정하지 않으면 슬롯 한 칸 미리보기를 쓴다.</summary>
        internal void SetSlotPreviewFactory(Func<RuneDefinition, string, RunePalettePreview> factory)
        {
            _slotPreviewFactory = factory;
        }

        /// <summary>그래프 전체가 보이도록 화면 위치와 배율을 맞추고 표시를 갱신한다.</summary>
        public void ResetView()
        {
            CancelPickedModule();
            _isBehaviorDrag = false;
            if (HasGraph && _layout.TryGetGraphBounds(out Vector2 minimum, out Vector2 maximum)) _viewport.Fit(minimum, maximum);
            RefreshGraph();
        }

        /// <summary>노드 라벨 내용·배치와 메시를 갱신하고 사라진 노드의 선택을 해제한다.</summary>
        public void RefreshGraph()
        {
            if (!HasGraph) return;
            if (!_hasDetachedDrag && _editor.IsEditable && _layout.ArrangeBlockGroups()) _editor.MarkBlockLayoutChanged();
            HashSet<string> existing = _labels.Refresh(_editor.Graph, _editor.Library, _editor.CompileResult.Spell);
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
            _state.Selected.UnionWith(_editor.Graph.GetBlockGroup(new[] { nodeId }, GameData.Runes));
            _selectionChanged(node);
            RefreshGraph();
        }

        /// <summary>선택한 노드와 그 연결을 삭제한다. 삭제할 수 없는 시전 노드는 편집 세션이 남긴다.</summary>
        public void DeleteSelection()
        {
            HashSet<string> group = _editor.Graph.GetBlockGroup(_state.Selected, GameData.Runes);
            _editor.BeginBlockEdit();
            foreach (string id in group) _editor.RemoveNode(id);
            _editor.EndBlockEdit();
            _state.Selected.Clear();
            _selectionChanged(null);
            RefreshGraph();
        }

        /// <summary>복사한 후속 묶음을 붙여넣고 새 블록들을 선택하며 자동 배치까지 되돌리기 한 단계로 묶는다.</summary>
        public void PasteBlockGroup()
        {
            var previous = new HashSet<string>();
            foreach (GraphNode node in _editor.Graph.Nodes) previous.Add(node.Id);
            _editor.BeginBlockEdit();
            try
            {
                _editor.PasteNodes();
                var added = new HashSet<string>();
                foreach (GraphNode node in _editor.Graph.Nodes)
                    if (!previous.Contains(node.Id)) added.Add(node.Id);
                if (added.Count == 0) return;
                _state.Selected.Clear();
                _state.Selected.UnionWith(added);
                RefreshGraph();
            }
            finally { _editor.EndBlockEdit(); }
        }

        /// <summary>화면 좌표가 편집 영역 안에 있으면 true와 함께 배치할 그래프 좌표를 반환한다.</summary>
        public bool TryGetGraphPoint(Vector2 screen, out Vector2 graphPoint)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, null, out Vector2 point);
            graphPoint = _viewport.GraphPoint(point);
            return rectTransform.rect.Contains(point);
        }

        /// <summary>화면 좌표가 표시 중인 좌측 룬 팔레트 영역 안이면 true를 반환한다.</summary>
        private bool IsPointerOverPalette(Vector2 screenPoint)
        {
            return _paletteDropTarget != null && _paletteDropTarget.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(_paletteDropTarget, screenPoint, null);
        }

        /// <summary>현재 보이는 캔버스 안에서 새 룬을 놓을 그래프 좌표를 반환한다.</summary>
        public Vector2 SuggestPlacement(string runeId) => _placement.Suggest(runeId);

        /// <summary>그래프 좌표에 행동 노드를 지정 등급으로 배치한다.</summary>
        public void PlaceRune(string runeId, Vector2 position, string grade = null)
        {
            _editor.BeginBlockEdit();
            try
            {
                int previousCount = _editor.Graph.Nodes.Count;
                _placement.Place(runeId, position, grade);
                if (_editor.Graph.Nodes.Count <= previousCount) return;
                GraphNode added = _editor.Graph.Nodes[_editor.Graph.Nodes.Count - 1];
                string[] movingIds = { added.Id };
                UpdatePuzzleSnap(movingIds);
                CommitPuzzleSnap(movingIds);
                _state.ClearSnap();
                RefreshGraph();
            }
            finally { _editor.EndBlockEdit(); }
        }

        /// <summary>독립된 실행 묶음과 내부 분기를 정렬하고 전체 배치를 되돌리기 한 단계로 기록한다.</summary>
        public void ArrangeBlocks()
        {
            if (!_editor.IsEditable) return;
            _editor.BeginBlockEdit();
            if (_layout.ArrangeBlockGroups(true)) _editor.MarkChanged();
            _editor.EndBlockEdit();
            RefreshGraph();
        }

        /// <summary>실제 편집 데이터와 분리된 팔레트 미리보기를 현재 글꼴과 그래프 배율로 생성한다.</summary>
        internal RunePalettePreview CreatePalettePreview(RuneDefinition rune, string grade)
        {
            if (!HasGraph || canvas == null) return null;
            return new RunePalettePreview(canvas.rootCanvas, rectTransform, _viewport.Zoom, rune, grade,
                _font, _editor.Library, _editor.IsRuneUnlocked);
        }

        /// <summary>팔레트 행을 복제해 포인터를 따라다니는 미리보기를 생성한다.</summary>
        internal RunePalettePreview CreateRowPreview(UiRow source)
        {
            if (canvas == null) return null;
            return RunePalettePreview.CreateRow(canvas.rootCanvas, source);
        }

        /// <summary>팔레트 블록의 화면 위치와 등급으로 표시 전용 노드를 만들고 앞쪽 우선 연결 후보를 갱신한다. 실제 그래프는 변경하지 않는다.</summary>
        internal void UpdatePaletteSnap(string runeId, string grade, Vector2 screen, Vector2 previewScreen)
        {
            _state.ClearSnap();
            if (HasGraph && _editor.IsEditable && TryGetGraphPoint(screen, out _))
            {
                TryGetGraphPoint(previewScreen, out Vector2 point);
                var moving = new GraphNode("palette-snap-preview", runeId, point.x, point.y);
                moving.SetText(SpellGrammar.MODIFIER_GRADE_PARAM,
                    string.IsNullOrEmpty(grade) ? SpellGrammar.DEFAULT_BEHAVIOR_GRADE : grade);
                float closest = _puzzleSnapDistance * _viewport.Zoom;
                closest *= closest;
                foreach (GraphNode stationary in _editor.Graph.Nodes)
                    ConsiderPuzzleSnap(stationary, moving, false, ref closest, moving);
                if (_state.SnapFromNode == null)
                    foreach (GraphNode stationary in _editor.Graph.Nodes)
                        ConsiderPuzzleSnap(moving, stationary, true, ref closest, moving);
            }
            RefreshMeshes();
        }

        /// <summary>팔레트 드래그 종료 시 연결 후보와 테두리 표시를 제거한다.</summary>
        internal void ClearPaletteSnap()
        {
            _state.ClearSnap();
            if (HasGraph) RefreshMeshes();
        }

        /// <summary>지정 룬·등급을 들고 있는 동안 장착 가능한 슬롯을 계산하고 표시 메시를 갱신한다.</summary>
        internal void ShowEquippableSlots(string runeId, string grade)
        {
            _state.EquippableSlots.Clear();
            if (HasGraph && GameData.Runes.TryGet(runeId, out _))
            {
                foreach (GraphNode node in _editor.Graph.Nodes)
                {
                    if (!GraphNodeLayout.HasSlots(GameData.Runes.Get(node.RuneId))) continue;
                    HashSet<string> slots = null;
                    foreach (string slotKey in GraphNodeLayout.SlotKeys)
                    {
                        if (!_editor.CanEquipToSlot(node.Id, slotKey, runeId, grade)) continue;
                        slots ??= new HashSet<string>();
                        slots.Add(slotKey);
                    }
                    if (slots != null) _state.EquippableSlots.Add(node.Id, slots);
                }
            }
            RefreshMeshes();
        }

        /// <summary>장착 가능한 슬롯 표시를 지우고 표시 메시를 갱신한다.</summary>
        internal void ClearEquippableSlots()
        {
            if (_state.EquippableSlots.Count == 0) return;
            _state.EquippableSlots.Clear();
            RefreshMeshes();
        }

        /// <summary>팔레트에서 클릭한 속성·모디를 행 복제 미리보기로 커서에 붙이고 다음 그래프 클릭 해제 시 장착하도록 준비한다.</summary>
        public void PickUpModule(string runeId, string grade, UiRow source)
        {
            CancelPickedModule();
            if (!HasGraph || !_editor.IsEditable || Mouse.current == null || source == null
                || !GameData.Runes.TryGet(runeId, out RuneDefinition rune)) return;
            if (rune.Category != SpellGrammar.CATEGORY_ELEMENT && rune.Category != SpellGrammar.CATEGORY_MODIFIER) return;
            _pickedModulePreview = CreateRowPreview(source);
            _pickedRuneId = runeId;
            _pickedGrade = grade;
            _pickedModulePreview?.Move(Mouse.current.position.ReadValue());
            ShowEquippableSlots(runeId, grade);
            _moduleInputFrame = Time.frameCount;
            _state.ResetPointer();
        }

        /// <summary>커서에 붙인 모듈 미리보기와 장착·이동 대기를 제거하고 취소한 항목이 있으면 true를 반환한다.</summary>
        public bool CancelPickedModule()
        {
            ClearEquippableSlots();
            if (!HasPickedModule) return false;
            _pickedModulePreview.Dispose();
            _pickedModulePreview = null;
            _pickedRuneId = null;
            _pickedGrade = null;
            _pickedSourceNodeId = null;
            _pickedSourceSlotKey = null;
            _isPickupPressActive = false;
            _isSlotPickupDragged = false;
            _moduleInputFrame = Time.frameCount;
            return true;
        }

        /// <summary>커서 모듈을 포인터 위치로 옮기고 우클릭·편집 영역 밖 클릭·편집 불가 상태에서 장착 대기를 취소한다.</summary>
        private void UpdatePickedModule()
        {
            if (!HasPickedModule) return;
            Mouse mouse = Mouse.current;
            if (mouse == null || !_editor.IsEditable)
            {
                CancelPickedModule();
                return;
            }
            Vector2 screen = mouse.position.ReadValue();
            _pickedModulePreview.Move(screen);
            if (_moduleInputFrame == Time.frameCount) return;
            if (mouse.rightButton.wasPressedThisFrame
                || (mouse.leftButton.wasPressedThisFrame && !TryGetGraphPoint(screen, out _))) CancelPickedModule();
        }

        protected override void OnDisable()
        {
            CancelPickedModule();
            _editor?.EndBlockEdit();
            _hasDetachedDrag = false;
            _isBehaviorDrag = false;
            _state.ResetPointer();
            base.OnDisable();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                CancelPickedModule();
                _editor?.EndBlockEdit();
                _hasDetachedDrag = false;
                _isBehaviorDrag = false;
                _state.ResetPointer();
            }
        }

        /// <summary>
        /// 화면 좌표에 속성·모디를 놓는다. 슬롯 위면 해당 슬롯의 장착 가능 여부를 검사하고, 행동 노드 본문 위면 첫 빈 슬롯에 장착한다.
        /// 장착 성공 시 선택 효과음을 재생하고 true를 반환하며, 장착하지 못하면 false를 반환한다.
        /// </summary>
        public bool DropSlotItem(string runeId, string grade, Vector2 screen)
        {
            if (!HasGraph || !GameData.Runes.TryGet(runeId, out _)) return false;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, null, out Vector2 point);
            if (!rectTransform.rect.Contains(point)) return false;
            if (_layout.FindSlot(point, out GraphNode slotNode, out string slotKey)
                && SpellGrammar.TryParseSlotKey(slotKey, out _, out _))
            {
                bool isSlotEquipped = _editor.Equip(slotNode.Id, slotKey, runeId, grade);
                if (isSlotEquipped) SoundManager.Instance.PlaySelectSlotItem();
                return isSlotEquipped;
            }
            GraphNode node = _layout.FindNodeAt(point);
            if (node == null || !GraphNodeLayout.HasSlots(GameData.Runes.Get(node.RuneId))) return false;
            if (!_editor.TryFindEquipSlot(node.Id, runeId, grade, out string emptySlot, true)) return false;
            bool isEquipped = _editor.Equip(node.Id, emptySlot, runeId, grade);
            if (isEquipped) SoundManager.Instance.PlaySelectSlotItem();
            return isEquipped;
        }

        /// <summary>
        /// 지정 슬롯에 장착된 속성·모디를 커서에 붙인다. 이후 다른 슬롯에 놓으면 옮겨 장착하고,
        /// 그래프 빈 공간에 놓으면 원래 슬롯을 비워 팔레트로 돌려보낸다.
        /// 슬롯이 비어 있거나 잠겼거나 편집할 수 없으면 false를 반환한다.
        /// </summary>
        private bool TryPickUpSlot(GraphNode node, string slotKey)
        {
            if (!HasGraph || !_editor.IsEditable || Mouse.current == null) return false;
            string value = node.GetText(slotKey, null);
            if (string.IsNullOrEmpty(value) || !SpellGrammar.TryParseSlotKey(slotKey, out bool isElement, out _)) return false;
            string runeId;
            string grade = null;
            if (isElement)
            {
                if (!GameData.Runes.TryGetElement(value, out ElementDefinition element)) return false;
                runeId = element.RuneId;
            }
            else if (!SpellGrammar.TryParseStockKey(value, out runeId, out grade)) return false;
            if (!GameData.Runes.TryGet(runeId, out RuneDefinition rune)) return false;

            CancelPickedModule();
            _pickedModulePreview = _slotPreviewFactory != null ? _slotPreviewFactory(rune, grade) : CreatePalettePreview(rune, grade);
            if (_pickedModulePreview == null) return false;
            _pickedRuneId = runeId;
            _pickedGrade = grade;
            _pickedSourceNodeId = node.Id;
            _pickedSourceSlotKey = slotKey;
            _isPickupPressActive = true;
            _pickedModulePreview.Move(Mouse.current.position.ReadValue());
            ShowEquippableSlots(runeId, grade);
            _moduleInputFrame = Time.frameCount;
            _state.ResetPointer();
            return true;
        }

        /// <summary>
        /// 슬롯에서 집어 든 룬을 화면 좌표에 놓는다. 좌측 룬 팔레트나 그래프 빈 공간이면 원래 슬롯을 비우고,
        /// 다른 슬롯이면 옮겨 장착한다. 팔레트를 제외한 그래프 밖이나 장착할 수 없는 위치에서는 원래 상태를 유지한다.
        /// </summary>
        private void DropPickedSlotItem(string sourceNodeId, string sourceSlotKey, string runeId, string grade, Vector2 screen)
        {
            if (IsPointerOverPalette(screen))
            {
                _editor.ClearSlot(sourceNodeId, sourceSlotKey);
                return;
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, null, out Vector2 point);
            if (!rectTransform.rect.Contains(point)) return;
            if (_layout.FindSlot(point, out GraphNode targetNode, out string targetKey)
                && SpellGrammar.TryParseSlotKey(targetKey, out _, out _))
            {
                if (targetNode.Id == sourceNodeId && targetKey == sourceSlotKey) return;
                MoveSlotItem(sourceNodeId, sourceSlotKey, targetNode.Id, targetKey, runeId, grade);
                return;
            }
            GraphNode node = _layout.FindNodeAt(point);
            if (node == null)
            {
                _editor.ClearSlot(sourceNodeId, sourceSlotKey);
                return;
            }
            if (!GraphNodeLayout.HasSlots(GameData.Runes.Get(node.RuneId))
                || !_editor.TryFindEquipSlot(node.Id, runeId, grade, out string emptySlot)
                || (node.Id == sourceNodeId && emptySlot == sourceSlotKey)) return;
            MoveSlotItem(sourceNodeId, sourceSlotKey, node.Id, emptySlot, runeId, grade);
        }

        /// <summary>
        /// 원래 슬롯을 비우고 대상 슬롯에 같은 룬을 장착한다. 장착에 실패하면 원래 슬롯을 복구하며,
        /// 두 변경은 되돌리기 한 단계로 묶는다. 장착에 성공하면 true를 반환한다.
        /// </summary>
        private bool MoveSlotItem(string sourceNodeId, string sourceSlotKey, string targetNodeId, string targetSlotKey,
            string runeId, string grade)
        {
            _editor.BeginBlockEdit();
            try
            {
                if (!_editor.ClearSlot(sourceNodeId, sourceSlotKey)) return false;
                if (_editor.Equip(targetNodeId, targetSlotKey, runeId, grade))
                {
                    SoundManager.Instance.PlaySelectSlotItem();
                    return true;
                }
                _editor.Equip(sourceNodeId, sourceSlotKey, runeId, grade);
                return false;
            }
            finally { _editor.EndBlockEdit(); }
        }

        void Update()
        {
            UpdatePickedModule();
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

        /// <summary>포인터 위치에서 블록 선택·드래그, 화면 이동 또는 영역 선택을 시작한다.</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (HasPickedModule || _moduleInputFrame == Time.frameCount) return;
            _state.Pointer = Local(eventData);
            _state.DragStart = _state.Pointer;
            _state.PanStart = _viewport.Pan;
            if (LocalTelemetry.IsUsabilityTestActive)
            {
                GraphNode clickedNode = _layout.FindNodeAt(_state.Pointer);
                string targetKind = clickedNode == null ? "background" : "node";
                string slotKey = null;
                if (_layout.FindPort(_state.Pointer, out GraphNode clickedPortNode, out _))
                {
                    clickedNode = clickedPortNode;
                    targetKind = "port";
                }
                else if (_layout.FindSlot(_state.Pointer, out GraphNode clickedSlotNode, out slotKey))
                {
                    clickedNode = clickedSlotNode;
                    targetKind = "slot";
                }
                else if (clickedNode == null && _layout.FindEdgeAt(_state.Pointer) != null)
                {
                    targetKind = "edge";
                }
                LocalTelemetry.RecordUsabilityEvent("editor_click", "editor", new Dictionary<string, object>
                {
                    ["input_id"] = Guid.NewGuid().ToString("N"),
                    ["target"] = "RuneGraphCanvas",
                    ["target_kind"] = targetKind,
                    ["node_id"] = clickedNode?.Id,
                    ["rune_id"] = clickedNode?.RuneId,
                    ["node_category"] = clickedNode == null ? null : GameData.Runes.Get(clickedNode.RuneId).Category,
                    ["slot_key"] = slotKey,
                    ["slot_value"] = slotKey == null ? null : clickedNode.GetText(slotKey),
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
                _state.IsPanning = true;
                return;
            }

            if (TryHandleSlotClick(_state.Pointer)) return;

            GraphNode clicked = _layout.FindNodeAt(_state.Pointer);
            if (clicked == null && _layout.FindPort(_state.Pointer, out GraphNode portNode, out PortDefinition port)
                && GraphNodeLayout.IsPuzzlePort(port)) clicked = portNode;
            if (clicked != null) BeginNodeDrag(clicked, IsShiftPressed());
            else if (eventData.clickCount >= 2)
            {
                _quickPlacement(_viewport.GraphPoint(_state.Pointer));
                return;
            }
            else BeginBoxSelection(IsShiftPressed());
            RefreshMeshes();
        }

        /// <summary>포인터 이동에 맞춰 화면 위치, 선택 노드 위치 또는 영역 선택을 갱신한다. 집어 든 룬을 끌면 드롭으로 처리한다.</summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (HasPickedModule)
            {
                if (_isPickupPressActive) _isSlotPickupDragged = true;
                return;
            }
            if (_moduleInputFrame == Time.frameCount) return;
            _state.Pointer = Local(eventData);
            if (_state.IsPanning) _viewport.SetPan(_state.PanStart + _state.Pointer - _state.DragStart);
            if (_state.IsNodeDragging) MoveDraggedNodes();
            if (_state.IsPanning || _state.IsNodeDragging) LayoutLabels();
            else RefreshMeshes();
        }

        /// <summary>
        /// 팔레트에 놓은 행동 노드 묶음을 삭제하고, 그 외에는 퍼즐 결합과 영역 선택·이동을 확정한다.
        /// 커서에 붙인 룬은 클릭으로 집은 경우 그대로 유지하고, 드래그로 집었거나 다음 클릭이면 놓는다.
        /// </summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (_moduleInputFrame == Time.frameCount)
            {
                // 집기와 같은 프레임에 버튼을 놓은 경우에도 클릭으로 집은 상태를 유지한다.
                _isPickupPressActive = false;
                _isSlotPickupDragged = false;
                return;
            }
            if (HasPickedModule)
            {
                if (_isPickupPressActive)
                {
                    bool isDragged = _isSlotPickupDragged;
                    _isPickupPressActive = false;
                    _isSlotPickupDragged = false;
                    if (!isDragged) return;
                }
                string runeId = _pickedRuneId;
                string grade = _pickedGrade;
                string sourceNodeId = _pickedSourceNodeId;
                string sourceSlotKey = _pickedSourceSlotKey;
                CancelPickedModule();
                if (eventData.button != PointerEventData.InputButton.Left) return;
                if (!string.IsNullOrEmpty(sourceSlotKey)) DropPickedSlotItem(sourceNodeId, sourceSlotKey, runeId, grade, eventData.position);
                else DropSlotItem(runeId, grade, eventData.position);
                return;
            }
            _state.Pointer = Local(eventData);
            if (_state.IsBoxSelecting) SelectNodesInBox();
            if (_state.IsNodeDragging && _editor.IsEditable
                && (_hasDetachedDrag || Vector2.Distance(_state.Pointer, _state.DragStart) > NODE_DRAG_THRESHOLD))
            {
                if (!_hasDetachedDrag) MoveDraggedNodes();
                if (_isBehaviorDrag && IsPointerOverPalette(eventData.position))
                {
                    HashSet<string> group = _editor.Graph.GetBlockGroup(_state.DragOrigins.Keys, GameData.Runes);
                    foreach (string id in group) _editor.RemoveNode(id);
                    _state.Selected.Clear();
                    _selectionChanged(null);
                    _editor.EndBlockEdit();
                    _labels.SetFrontNode(null);
                    _hasDetachedDrag = false;
                    _isBehaviorDrag = false;
                    _state.ResetPointer();
                    RefreshGraph();
                    return;
                }
                UpdatePuzzleSnap(_state.DragOrigins.Keys);
                CommitPuzzleSnap(_state.DragOrigins.Keys);
                _layout.ArrangeBlockGroups();
                _editor.MarkChanged();
                _editor.EndBlockEdit();
                if (LocalTelemetry.IsUsabilityTestActive)
                    LocalTelemetry.RecordUsabilityEvent("editor_action_result", "editor", new Dictionary<string, object>
                    {
                        ["action"] = "move_nodes",
                        ["input_method"] = "drag",
                        ["result"] = "success",
                        ["node_ids"] = new List<string>(_state.Selected),
                    });
            }
            _editor.EndBlockEdit();
            _labels.SetFrontNode(null);
            _hasDetachedDrag = false;
            _isBehaviorDrag = false;
            _state.ResetPointer();
            RefreshGraph();
        }

        /// <summary>
        /// 슬롯 셀 좌클릭을 처리한다. 장착된 속성·모디가 있으면 커서에 붙이고 true를 반환한다.
        /// 처리하지 못한 잠긴 셀·빈 슬롯·슬롯 밖이면 노드 선택으로 넘기도록 false를 반환한다.
        /// </summary>
        private bool TryHandleSlotClick(Vector2 point)
        {
            if (!_layout.FindSlot(point, out GraphNode node, out string slotKey)) return false;
            if (GraphNodeLayout.IsSlotLocked(node, slotKey)) return false;
            return TryPickUpSlot(node, slotKey);
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
            if (HasPickedModule) return;
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

        /// <summary>클릭한 노드로 선택을 갱신하고 선택된 노드들의 드래그 시작 좌표를 저장한다.</summary>
        private void BeginNodeDrag(GraphNode clicked, bool isShift)
        {
            HashSet<string> selected = _state.Selected;
            HashSet<string> group = _editor.Graph.GetBlockGroup(new[] { clicked.Id }, GameData.Runes);
            if (!isShift) selected.Clear();
            if (isShift && selected.Contains(clicked.Id)) selected.ExceptWith(group);
            else selected.UnionWith(group);
            selected.UnionWith(_editor.Graph.GetBlockGroup(selected, GameData.Runes));
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
            _hasDetachedDrag = false;
            _isBehaviorDrag = GameData.Runes.Get(clicked.RuneId).Category == SpellGrammar.CATEGORY_BEHAVIOR;
        }

        /// <summary>드래그 시작 이후 포인터 이동량만큼 선택된 노드들을 그래프 좌표에서 옮긴다.</summary>
        private void MoveDraggedNodes()
        {
            if (!_editor.IsEditable || Vector2.Distance(_state.Pointer, _state.DragStart) <= NODE_DRAG_THRESHOLD) return;
            if (!_hasDetachedDrag)
            {
                _editor.BeginBlockEdit();
                _hasDetachedDrag = true;
                _editor.DetachBlockGroup(_state.DragOrigins.Keys);
            }
            Vector2 difference = (_state.Pointer - _state.DragStart) / _viewport.Zoom;
            foreach (var pair in _state.DragOrigins) _editor.Graph.FindNode(pair.Key)?.Move(pair.Value.x + difference.x, pair.Value.y - difference.y);
            _editor.MarkChanged();
            UpdatePuzzleSnap(_state.DragOrigins.Keys);
        }

        /// <summary>이동 묶음 앞쪽의 유효 결합을 우선하고 없으면 뒤쪽에서 가장 가까운 결합을 선택한다.</summary>
        private void UpdatePuzzleSnap(ICollection<string> movingIds)
        {
            _state.ClearSnap();
            if (!_editor.IsEditable) return;
            float closest = _puzzleSnapDistance * _viewport.Zoom;
            closest *= closest;
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (string id in movingIds)
                {
                    GraphNode moving = _editor.Graph.FindNode(id);
                    if (moving == null) continue;
                    foreach (GraphNode stationary in _editor.Graph.Nodes)
                    {
                        if (movingIds.Contains(stationary.Id)) continue;
                        bool isHead = true;
                        foreach (GraphEdge edge in _editor.Graph.Edges)
                            if (edge.ToNode == moving.Id && movingIds.Contains(edge.FromNode) && _layout.IsBlockEdge(edge)) { isHead = false; break; }
                        if (pass == 0 && isHead) ConsiderPuzzleSnap(stationary, moving, false, ref closest);
                        if (pass == 1) ConsiderPuzzleSnap(moving, stationary, true, ref closest);
                    }
                }
                if (_state.SnapFromNode != null) break;
            }
        }

        /// <summary>출력·입력 포트 거리와 기존 연결·문법을 검사하고 더 가까운 결합 후보와 이동 보정량을 저장한다.</summary>
        private void ConsiderPuzzleSnap(GraphNode from, GraphNode to, bool isOutputMoving, ref float closest, GraphNode previewNode = null)
        {
            RuneDefinition fromRune = GameData.Runes.Get(from.RuneId);
            RuneDefinition toRune = GameData.Runes.Get(to.RuneId);
            PortDefinition input = toRune.FindPort(SpellGrammar.EXEC_PORT, SpellGrammar.DIRECTION_IN);
            if (!GraphNodeLayout.IsPuzzlePort(input)) return;
            foreach (PortDefinition output in fromRune.Ports)
            {
                if (output.Direction != SpellGrammar.DIRECTION_OUT || !GraphNodeLayout.IsPuzzlePort(output)) continue;
                Vector2 fromPoint = _layout.PortPosition(_layout.NodeRect(from, fromRune), from, fromRune, output.Id);
                Vector2 toPoint = _layout.PortPosition(_layout.NodeRect(to, toRune), to, toRune, input.Id);
                Vector2 offset = fromPoint - toPoint;
                float distance = offset.sqrMagnitude;
                if (distance >= closest) continue;
                bool isConnected = false;
                bool isOccupied = false;
                string insertNode = null;
                foreach (GraphEdge edge in _editor.Graph.Edges)
                {
                    bool isSameOutput = edge.FromNode == from.Id && edge.FromPort == output.Id;
                    bool isSameInput = edge.ToNode == to.Id && edge.ToPort == input.Id;
                    if (isSameOutput && isSameInput) isConnected = true;
                    else if (isOutputMoving && (isSameOutput || isSameInput)) isOccupied = true;
                    if (isSameOutput && !isSameInput) insertNode = edge.ToNode;
                }
                if (isOccupied || (!isConnected && !_editor.CanDockBlocks(from.Id, output.Id, to.Id, previewNode))) continue;
                closest = distance;
                _state.SnapFromNode = from.Id;
                _state.SnapFromPort = output.Id;
                _state.SnapToNode = to.Id;
                _state.SnapInsertNode = insertNode;
                _state.SnapHighlightNode = isOutputMoving ? to.Id : from.Id;
                _state.SnapOffset = (isOutputMoving ? -offset : offset) / _viewport.Zoom;
                _state.IsSnapConnected = isConnected;
            }
        }

        /// <summary>선택 후보의 포트가 맞닿도록 이동 집합을 정렬하고 기존 연결 규칙으로 연결한다. 거부되면 보정 이동만 복구한다.</summary>
        private void CommitPuzzleSnap(ICollection<string> movingIds)
        {
            if (_state.SnapFromNode == null) return;
            Vector2 offset = _state.SnapOffset;
            foreach (string id in movingIds)
            {
                GraphNode node = _editor.Graph.FindNode(id);
                node.Move(node.X + offset.x, node.Y - offset.y);
            }
            if (_state.IsSnapConnected)
            {
                _editor.MarkChanged();
                return;
            }
            if (_editor.DockBlocks(_state.SnapFromNode, _state.SnapFromPort, _state.SnapToNode)) return;
            foreach (string id in movingIds)
            {
                GraphNode node = _editor.Graph.FindNode(id);
                node.Move(node.X - offset.x, node.Y + offset.y);
            }
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
            _state.Selected.UnionWith(_editor.Graph.GetBlockGroup(_state.Selected, GameData.Runes));
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
