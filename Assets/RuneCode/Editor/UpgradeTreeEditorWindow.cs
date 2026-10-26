using System;
using System.Collections.Generic;

using UnityEngine;

using UnityEditor;

namespace RuneCode.Editor
{
    /// <summary>강화 트리 SO의 노드를 격자에서 생성·편집·배치·연결·삭제하는 창이다. 선 연결은 클릭 순서로 방향을 정한다.</summary>
    public sealed class UpgradeTreeEditorWindow : EditorWindow
    {
        private const float CELL_PIXELS = 90;
        private const float NODE_INSET = 8;
        private const float EDGE_THICKNESS = 3;
        private const float EDGE_HIT_DISTANCE = 6;
        private const float PANEL_WIDTH = 340;
        private const int MAX_UPGRADE_LEVEL = 99;
        private const string MAINBOARD_ID = "upgrade.mainboard";
        private const string NEW_NODE_ID = "upgrade.new";

        private static readonly Color GRID_BACKGROUND = new Color(0.82f, 0.82f, 0.84f);
        private static readonly Color GRID_LINE = new Color(0f, 0f, 0f, 0.10f);
        private static readonly Color CONNECTION_LINE = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        private static readonly Color SELECTION_RING = new Color(0.10f, 0.10f, 0.10f);
        private static readonly Color EDGE_START_RING = new Color(0.95f, 0.65f, 0.10f);
        private static readonly Color DROP_VALID = new Color(0.20f, 0.80f, 0.25f, 0.40f);
        private static readonly Color DROP_INVALID = new Color(0.90f, 0.25f, 0.20f, 0.40f);

        private UpgradeTreeDefinition _definition;
        private Vector2 _scroll;
        private Vector2 _panelScroll;
        private Rect _gridViewRect;
        private bool _connectionMode;
        private string _selectedNodeId;
        private string _selectedEdgeParentId;
        private string _selectedEdgeChildId;
        private string _edgeStartNodeId;
        private string _dragNodeId;
        private Vector2Int _dragCell;
        private bool _isDragValid;
        private string _loadError;
        private string _validateError;
        private string _transientMessage;
        private GUIStyle _nodeLabelStyle;

        /// <summary>화면에서 선택한 엣지의 부모·자식 ID 짝이다.</summary>
        private readonly struct EdgeHit
        {
            public readonly string ParentId;
            public readonly string ChildId;

            public EdgeHit(string parentId, string childId)
            {
                ParentId = parentId;
                ChildId = childId;
            }
        }

        /// <summary>강화 트리 편집기 창을 연다.</summary>
        [MenuItem("Rune Code/Upgrade Tree Editor")]
        public static void Open()
        {
            GetWindow<UpgradeTreeEditorWindow>("Upgrade Tree Editor");
        }

        void OnEnable()
        {
            if (_definition == null)
                _definition = Resources.Load<UpgradeTreeDefinition>("RuneCode/RunUpgradeTree");
            titleContent = new GUIContent("Upgrade Tree");
            Undo.undoRedoPerformed += RefreshState;
            RefreshState();
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= RefreshState;
        }

        void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();
            if (_definition == null)
            {
                EditorGUILayout.HelpBox("강화 트리 자산을 선택하세요.", MessageType.Info);
                return;
            }
            DrawStatus();
            EditorGUILayout.BeginHorizontal();
            DrawGridCanvas();
            DrawSidePanel();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>대상 자산이 바뀌면 게임 데이터 로드와 자산 검증을 다시 수행하고 만료된 선택을 정리한다.</summary>
        private void RefreshState()
        {
            _loadError = null;
            _validateError = null;
            if (_selectedNodeId != null && FindPlacedNode(_selectedNodeId) == null) _selectedNodeId = null;
            if (_edgeStartNodeId != null && FindPlacedNode(_edgeStartNodeId) == null) _edgeStartNodeId = null;
            if (_dragNodeId != null && FindPlacedNode(_dragNodeId) == null) _dragNodeId = null;
            if (_selectedEdgeChildId != null)
            {
                UpgradeTreeNodeDefinition child = FindPlacedNode(_selectedEdgeChildId);
                if (child == null || FindPlacedNode(_selectedEdgeParentId) == null || !HasPrerequisite(child, _selectedEdgeParentId))
                {
                    _selectedEdgeParentId = null;
                    _selectedEdgeChildId = null;
                }
            }
            if (_definition == null) return;
            if (!GameData.IsLoaded)
            {
                try { GameData.Load(); }
                catch (Exception exception) { _loadError = exception.Message; }
            }
            if (GameData.IsLoaded)
                _definition.Validate(out _validateError);
            Repaint();
        }

        /// <summary>대상 자산 선택란, 연결 모드 전환, 새 노드 버튼과 데이터 로드 오류를 표시한다.</summary>
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _definition = (UpgradeTreeDefinition)EditorGUILayout.ObjectField("트리 자산", _definition, typeof(UpgradeTreeDefinition), false);
            if (EditorGUI.EndChangeCheck()) RefreshState();
            bool connectionMode = GUILayout.Toggle(_connectionMode, "연결 모드", GUILayout.Width(90));
            if (connectionMode != _connectionMode)
            {
                _connectionMode = connectionMode;
                _edgeStartNodeId = null;
                _dragNodeId = null;
            }
            using (new EditorGUI.DisabledScope(_definition == null))
            {
                if (GUILayout.Button("새 노드", GUILayout.Width(70)))
                    CreateNode(FindFreeCell());
            }
            EditorGUILayout.EndHorizontal();

            if (_loadError != null)
                EditorGUILayout.HelpBox("게임 데이터 로드 실패: " + _loadError + "\n라벨은 키로 표시되고 룬 검증은 생략됩니다.", MessageType.Warning);
        }

        /// <summary>연결 모드 안내, 임시 안내 메시지와 자산 검증 결과를 표시한다.</summary>
        private void DrawStatus()
        {
            if (_connectionMode)
                EditorGUILayout.HelpBox("연결 모드: 첫 클릭 노드가 부모, 두 번째 클릭 노드가 자식이 됩니다.", MessageType.Info);
            if (_transientMessage != null)
                EditorGUILayout.HelpBox(_transientMessage, MessageType.Info);
            if (_validateError != null)
                EditorGUILayout.HelpBox(_validateError, MessageType.Error);
            else
                EditorGUILayout.HelpBox("검증 통과", MessageType.None);
        }

        /// <summary>배치 노드의 격자, 선행 관계 선, 노드 사각형을 그리고 마우스 조작을 처리한다.</summary>
        private void DrawGridCanvas()
        {
            ComputeGridBounds(out int minCol, out int maxCol, out int minRow, out int maxRow);
            float width = (maxCol - minCol + 1) * CELL_PIXELS;
            float height = (maxRow - minRow + 1) * CELL_PIXELS;

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            Rect content = GUILayoutUtility.GetRect(width + CELL_PIXELS, height + CELL_PIXELS, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(content, GRID_BACKGROUND);
            Vector2 origin = new Vector2(content.x + CELL_PIXELS * 0.5f, content.y + CELL_PIXELS * 0.5f);

            DrawGridLines(origin, minCol, maxCol, minRow, maxRow);
            DrawConnections(origin, minCol, minRow);
            DrawNodes(origin, minCol, minRow);
            HandleGridEvents(origin, minCol, minRow);

            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) _gridViewRect = GUILayoutUtility.GetLastRect();
        }

        /// <summary>배치 노드 좌표를 펼쳐 격자의 열·행 범위를 구한다.</summary>
        private void ComputeGridBounds(out int minCol, out int maxCol, out int minRow, out int maxRow)
        {
            minCol = 0; maxCol = 8; minRow = 0; maxRow = 7;
            if (_definition == null) return;
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
            {
                if (node == null) continue;
                minCol = Mathf.Min(minCol, node.LayoutCell.x);
                maxCol = Mathf.Max(maxCol, node.LayoutCell.x);
                minRow = Mathf.Min(minRow, node.LayoutCell.y);
                maxRow = Mathf.Max(maxRow, node.LayoutCell.y);
            }
        }

        /// <summary>격자 셀 경계선을 그린다.</summary>
        private void DrawGridLines(Vector2 origin, int minCol, int maxCol, int minRow, int maxRow)
        {
            Handles.color = GRID_LINE;
            for (int col = minCol; col <= maxCol + 1; col++)
            {
                float x = origin.x + (col - minCol) * CELL_PIXELS;
                Handles.DrawLine(new Vector2(x, origin.y), new Vector2(x, origin.y + (maxRow - minRow + 1) * CELL_PIXELS));
            }
            for (int row = minRow; row <= maxRow + 1; row++)
            {
                float y = origin.y + (row - minRow) * CELL_PIXELS;
                Handles.DrawLine(new Vector2(origin.x, y), new Vector2(origin.x + (maxCol - minCol + 1) * CELL_PIXELS, y));
            }
        }

        /// <summary>배치 노드의 선행 관계를 부모 셀 중심에서 자식 셀 중심으로 그린다.</summary>
        private void DrawConnections(Vector2 origin, int minCol, int minRow)
        {
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
            {
                if (node == null || node.Prerequisites.Count == 0) continue;
                foreach (UpgradeTreePrerequisite prerequisite in node.Prerequisites)
                {
                    UpgradeTreeNodeDefinition parent = prerequisite == null ? null : _definition.FindNode(prerequisite.NodeId);
                    if (parent == null) continue;
                    Vector2 start = CellCenter(origin, parent.LayoutCell, minCol, minRow);
                    Vector2 end = CellCenter(origin, node.LayoutCell, minCol, minRow);
                    Handles.DrawBezier(start, end, start, end, CONNECTION_LINE, Texture2D.whiteTexture, EDGE_THICKNESS);
                }
            }
        }

        /// <summary>배치 노드 사각형, 배타 세트 테두리, 라벨, 드래그 고스트를 그린다.</summary>
        private void DrawNodes(Vector2 origin, int minCol, int minRow)
        {
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
            {
                if (node == null) continue;
                Rect rect = NodeRect(origin, node.LayoutCell, minCol, minRow);
                if (!string.IsNullOrEmpty(node.ExclusiveGroupId))
                    EditorGUI.DrawRect(new Rect(rect.x - 6, rect.y - 6, rect.width + 12, rect.height + 12), GetGroupColor(node.ExclusiveGroupId));
                if (_selectedNodeId == node.Id)
                    EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), SELECTION_RING);
                if (_edgeStartNodeId == node.Id)
                    EditorGUI.DrawRect(new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6), EDGE_START_RING);
                EditorGUI.DrawRect(rect, GetEffectColor(node.EffectType));
                string title = string.IsNullOrEmpty(node.TitleKey) ? node.Id : GameData.L(node.TitleKey);
                string tooltip = node.Id + "\n셀 (" + node.LayoutCell.x + ", " + node.LayoutCell.y + ")";
                if (!string.IsNullOrEmpty(node.ExclusiveGroupId))
                    tooltip += "\n배타 세트: " + node.ExclusiveGroupId;
                GUI.Label(rect, new GUIContent(title, tooltip), _nodeLabelStyle);
            }
            UpgradeTreeNodeDefinition dragNode = _dragNodeId == null ? null : FindPlacedNode(_dragNodeId);
            if (dragNode != null && _dragCell != dragNode.LayoutCell)
            {
                Rect ghost = NodeRect(origin, _dragCell, minCol, minRow);
                EditorGUI.DrawRect(ghost, _isDragValid ? DROP_VALID : DROP_INVALID);
                GUI.Label(ghost, new GUIContent(GameData.L(dragNode.TitleKey)), _nodeLabelStyle);
            }
        }

        /// <summary>연결 모드의 선 연결, 일반 모드의 선택·이동, 우클릭 메뉴를 처리한다. 격자 영역 밖의 클릭은 무시한다.</summary>
        private void HandleGridEvents(Vector2 origin, int minCol, int minRow)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown)
            {
                Rect visible = new Rect(_scroll, _gridViewRect.size);
                if (_gridViewRect.size != Vector2.zero && !visible.Contains(current.mousePosition)) return;
                _transientMessage = null;
                UpgradeTreeNodeDefinition hit = FindNodeAt(origin, current.mousePosition, minCol, minRow);
                if (current.button == 1)
                {
                    if (_edgeStartNodeId != null) CancelPendingModes();
                    else if (hit == null) ShowCellMenu(CellAt(origin, current.mousePosition, minCol, minRow));
                    current.Use();
                    return;
                }
                if (current.button != 0) return;
                if (_connectionMode)
                {
                    HandleConnectionClick(hit);
                    current.Use();
                    return;
                }
                if (hit == null)
                {
                    EdgeHit? edgeHit = FindEdgeAt(origin, current.mousePosition, minCol, minRow);
                    if (edgeHit != null)
                    {
                        _selectedEdgeParentId = edgeHit.Value.ParentId;
                        _selectedEdgeChildId = edgeHit.Value.ChildId;
                        _selectedNodeId = null;
                        GUI.FocusControl(null);
                        Repaint();
                        current.Use();
                        return;
                    }
                }
                _dragNodeId = hit == null ? null : hit.Id;
                _isDragValid = false;
                if (hit != null) _dragCell = hit.LayoutCell;
                _selectedEdgeParentId = null;
                _selectedEdgeChildId = null;
                string hitId = hit == null ? null : hit.Id;
                if (_selectedNodeId != hitId)
                {
                    _selectedNodeId = hitId;
                    GUI.FocusControl(null);
                    Repaint();
                }
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 && _dragNodeId != null)
            {
                UpgradeTreeNodeDefinition dragNode = FindPlacedNode(_dragNodeId);
                if (dragNode == null) return;
                _dragCell = CellAt(origin, current.mousePosition, minCol, minRow);
                _isDragValid = _dragCell != dragNode.LayoutCell && !IsCellOccupied(_dragCell, dragNode);
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp && current.button == 0 && _dragNodeId != null)
            {
                UpgradeTreeNodeDefinition dragNode = FindPlacedNode(_dragNodeId);
                if (dragNode != null && _isDragValid) WriteLayoutCell(dragNode, _dragCell);
                _dragNodeId = null;
                Repaint();
                current.Use();
            }
        }

        /// <summary>빈 셀 우클릭 시 그 셀에 새 노드를 만드는 메뉴를 연다.</summary>
        private void ShowCellMenu(Vector2Int cell)
        {
            if (IsCellOccupied(cell, null)) return;
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("여기에 새 노드"), false, () => CreateNode(cell));
            menu.ShowAsContext();
        }

        /// <summary>연결 모드에서 노드 클릭으로 첫 클릭을 부모, 두 번째 클릭을 자식으로 지정한다.</summary>
        private void HandleConnectionClick(UpgradeTreeNodeDefinition hit)
        {
            if (hit == null || _edgeStartNodeId == hit.Id)
            {
                _edgeStartNodeId = null;
                Repaint();
                return;
            }
            if (_edgeStartNodeId == null)
            {
                _edgeStartNodeId = hit.Id;
                Repaint();
                return;
            }
            string startId = _edgeStartNodeId;
            _edgeStartNodeId = null;
            TryCreateEdge(startId, hit.Id);
        }

        /// <summary>첫 클릭 노드를 부모, 두 번째 클릭 노드를 자식으로 하는 선행 관계를 만든다. 메인보드 자식, 같은 선, 순환은 메시지로 거절한다.</summary>
        private void TryCreateEdge(string parentId, string childId)
        {
            if (childId == MAINBOARD_ID)
            {
                SetTransientMessage("메인보드는 다른 노드의 자식이 될 수 없습니다.");
                return;
            }
            if (HasPrerequisite(FindPlacedNode(childId), parentId))
            {
                SetTransientMessage("두 노드 사이에 같은 선이 이미 있습니다.");
                return;
            }
            if (WouldCreateCycle(parentId, childId))
            {
                SetTransientMessage("이 연결은 선행 관계에 순환을 만듭니다.");
                return;
            }
            WriteEdge(parentId, childId);
        }

        /// <summary>새 부모에서 선행 조건 방향으로 거슬러 올라가 자식에 닿으면 순환이 생기는지 검사한다. 방문 집합으로 이미 본 노드는 다시 확인하지 않는다.</summary>
        private bool WouldCreateCycle(string parentId, string childId)
        {
            HashSet<string> visited = new HashSet<string> { parentId };
            Queue<string> pending = new Queue<string>();
            pending.Enqueue(parentId);
            while (pending.Count > 0)
            {
                string currentId = pending.Dequeue();
                if (currentId == childId) return true;
                UpgradeTreeNodeDefinition current = FindPlacedNode(currentId);
                if (current == null) continue;
                foreach (UpgradeTreePrerequisite prerequisite in current.Prerequisites)
                {
                    if (prerequisite == null || !visited.Add(prerequisite.NodeId)) continue;
                    pending.Enqueue(prerequisite.NodeId);
                }
            }
            return false;
        }

        /// <summary>다음 클릭까지 표시되는 안내 메시지를 설정한다.</summary>
        private void SetTransientMessage(string message)
        {
            _transientMessage = message;
            Repaint();
        }

        /// <summary>연결 시작을 취소한다.</summary>
        private void CancelPendingModes()
        {
            if (_edgeStartNodeId == null) return;
            _edgeStartNodeId = null;
            Repaint();
        }

        /// <summary>지정 셀에 기본값의 새 노드를 추가하고 선택한다. 다른 노드의 값은 복사하지 않는다.</summary>
        private void CreateNode(Vector2Int cell)
        {
            if (_definition == null) return;
            string newId = GenerateUniqueNodeId(NEW_NODE_ID);
            Undo.RecordObject(_definition, "Create Upgrade Tree Node");
            SerializedObject serialized = new SerializedObject(_definition);
            serialized.Update();
            SerializedProperty nodes = serialized.FindProperty("_nodes");
            nodes.InsertArrayElementAtIndex(nodes.arraySize);
            // 삽입한 요소는 마지막 노드의 복제본이므로 모든 필드를 기본값으로 덮어쓴다.
            SerializedProperty node = nodes.GetArrayElementAtIndex(nodes.arraySize - 1);
            node.FindPropertyRelative("_id").stringValue = newId;
            node.FindPropertyRelative("_requiredStage").intValue = 1;
            node.FindPropertyRelative("_titleKey").stringValue = "";
            node.FindPropertyRelative("_descriptionKey").stringValue = "";
            node.FindPropertyRelative("_icon").objectReferenceValue = null;
            node.FindPropertyRelative("_layoutCell").vector2IntValue = cell;
            node.FindPropertyRelative("_effectType").intValue = (int)UpgradeEffectType.EnergyRegen;
            node.FindPropertyRelative("_runeId").stringValue = "";
            node.FindPropertyRelative("_exclusiveGroupId").stringValue = "";
            node.FindPropertyRelative("_initialLevel").intValue = 0;
            node.FindPropertyRelative("_prerequisites").arraySize = 0;
            SerializedProperty levels = node.FindPropertyRelative("_levels");
            levels.arraySize = 1;
            levels.GetArrayElementAtIndex(0).FindPropertyRelative("_cost").intValue = 0;
            levels.GetArrayElementAtIndex(0).FindPropertyRelative("_amount").floatValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(_definition);
            _selectedNodeId = newId;
            _selectedEdgeParentId = null;
            _selectedEdgeChildId = null;
            GUI.FocusControl(null);
            RefreshState();
            SetTransientMessage("새 노드를 만들었습니다: " + newId + " — 값을 채우고 연결 모드로 부모를 지정하세요.");
        }

        /// <summary>기준 ID가 쓰이지 않으면 그대로, 쓰이면 숫자 접미사를 붙여 배치 노드와 겹치지 않는 ID를 반환한다.</summary>
        private string GenerateUniqueNodeId(string baseId)
        {
            string candidate = baseId;
            for (int suffix = 2; FindPlacedNode(candidate) != null; suffix++)
                candidate = baseId + suffix;
            return candidate;
        }

        /// <summary>격자에서 비어 있는 첫 셀을 찾는다. 현재 범위가 꽉 차면 마지막 행 다음을 반환한다.</summary>
        private Vector2Int FindFreeCell()
        {
            ComputeGridBounds(out int minCol, out int maxCol, out int minRow, out int maxRow);
            for (int row = minRow; row <= maxRow; row++)
            {
                for (int col = minCol; col <= maxCol; col++)
                {
                    Vector2Int cell = new Vector2Int(col, row);
                    if (!IsCellOccupied(cell, null)) return cell;
                }
            }
            return new Vector2Int(minCol, maxRow + 1);
        }

        /// <summary>오른쪽 패널에 선택한 선의 정보·삭제 또는 선택한 노드의 편집 필드를 표시한다.</summary>
        private void DrawSidePanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(PANEL_WIDTH));
            _panelScroll = EditorGUILayout.BeginScrollView(_panelScroll);
            if (_selectedEdgeChildId != null) DrawEdgePanel();
            else DrawNodePanel();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>선택한 선의 부모·자식과 삭제 버튼을 표시한다.</summary>
        private void DrawEdgePanel()
        {
            EditorGUILayout.LabelField("선택한 선", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_selectedEdgeParentId + " → " + _selectedEdgeChildId, MessageType.None);
            if (GUILayout.Button("선 삭제", GUILayout.Width(120)))
                RemoveEdge(_selectedEdgeChildId, _selectedEdgeParentId);
        }

        /// <summary>선택한 노드의 값을 SerializedObject로 편집하고, 변경이 있으면 Undo를 기록한 뒤 검증을 갱신한다.</summary>
        private void DrawNodePanel()
        {
            int index = FindNodeIndex(_selectedNodeId);
            if (index < 0)
            {
                EditorGUILayout.LabelField("격자의 노드를 클릭하거나 새 노드를 만드세요.", EditorStyles.wordWrappedLabel);
                return;
            }
            UpgradeTreeNodeDefinition node = _definition.Nodes[index];
            SerializedObject serialized = new SerializedObject(_definition);
            serialized.Update();
            SerializedProperty nodes = serialized.FindProperty("_nodes");
            SerializedProperty property = nodes.GetArrayElementAtIndex(index);

            EditorGUILayout.LabelField("노드 편집", EditorStyles.boldLabel);
            DrawIdField(nodes, property);
            DrawTextKeyField(property.FindPropertyRelative("_titleKey"), "이름 키");
            DrawTextKeyField(property.FindPropertyRelative("_descriptionKey"), "설명 키");
            EditorGUILayout.PropertyField(property.FindPropertyRelative("_icon"), new GUIContent("아이콘"));

            EditorGUILayout.Space();
            SerializedProperty effectType = property.FindPropertyRelative("_effectType");
            SerializedProperty runeId = property.FindPropertyRelative("_runeId");
            SerializedProperty initialLevel = property.FindPropertyRelative("_initialLevel");
            SerializedProperty levels = property.FindPropertyRelative("_levels");
            EditorGUILayout.PropertyField(effectType, new GUIContent("효과"));
            bool isRuneUnlock = effectType.intValue == (int)UpgradeEffectType.RuneUnlock;
            if (isRuneUnlock)
            {
                DrawRuneField(runeId, node.Id);
                if (levels.arraySize != 1) levels.arraySize = 1;
                if (initialLevel.intValue != 0) initialLevel.intValue = 0;
            }
            else if (!string.IsNullOrEmpty(runeId.stringValue))
            {
                runeId.stringValue = "";
            }
            DrawLevels(levels, isRuneUnlock);
            using (new EditorGUI.DisabledScope(isRuneUnlock))
                initialLevel.intValue = EditorGUILayout.IntSlider("시작 레벨", initialLevel.intValue, 0, levels.arraySize);
            if (initialLevel.intValue > levels.arraySize) initialLevel.intValue = levels.arraySize;

            DrawExclusiveGroupField(property, node);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("셀", "(" + node.LayoutCell.x + ", " + node.LayoutCell.y + ") — 격자에서 드래그");

            if (serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(_definition);
                RefreshState();
            }

            DrawConnectionRows(node);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("자산을 Project에서 보기"))
                EditorGUIUtility.PingObject(_definition);
            if (GUILayout.Button("노드 삭제"))
                DeleteNode(node.Id);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 노드 ID를 입력받아 비어 있거나 중복되면 거절하고, 유효하면 ID와 이를 참조하는 자식의 선행 ID를 함께 바꾼다.
        /// 메인보드 ID는 검증 규칙에 고정되어 있어 변경할 수 없다.
        /// </summary>
        private void DrawIdField(SerializedProperty nodes, SerializedProperty property)
        {
            SerializedProperty idProperty = property.FindPropertyRelative("_id");
            string oldId = idProperty.stringValue;
            string input;
            using (new EditorGUI.DisabledScope(oldId == MAINBOARD_ID))
                input = EditorGUILayout.DelayedTextField("ID", oldId);
            EditorGUILayout.LabelField("ID를 바꾸면 기존 저장의 이 노드 레벨이 이어지지 않습니다.", EditorStyles.wordWrappedMiniLabel);
            if (input == oldId) return;
            string newId = input.Trim();
            if (newId == oldId) return;
            if (string.IsNullOrEmpty(newId) || FindPlacedNode(newId) != null)
            {
                SetTransientMessage("ID가 비어 있거나 이미 사용 중입니다: " + newId);
                return;
            }
            idProperty.stringValue = newId;
            for (int index = 0; index < nodes.arraySize; index++)
            {
                SerializedProperty prerequisites = nodes.GetArrayElementAtIndex(index).FindPropertyRelative("_prerequisites");
                for (int i = 0; i < prerequisites.arraySize; i++)
                {
                    SerializedProperty parentId = prerequisites.GetArrayElementAtIndex(i).FindPropertyRelative("_nodeId");
                    if (parentId.stringValue == oldId) parentId.stringValue = newId;
                }
            }
            _selectedNodeId = newId;
        }

        /// <summary>현지화 키 입력란과 한국어 미리보기를 표시하고 키가 비었거나 문구 테이블에 없으면 경고한다.</summary>
        private static void DrawTextKeyField(SerializedProperty property, string label)
        {
            EditorGUILayout.PropertyField(property, new GUIContent(label));
            if (!GameData.IsLoaded) return;
            string key = property.stringValue;
            string text = GameData.L(key);
            if (string.IsNullOrEmpty(key) || text == key)
                EditorGUILayout.HelpBox("키가 비어 있거나 strings.ko.json에 없습니다.", MessageType.Warning);
            else
                EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>트리 해금 대상 룬 중 다른 노드가 쓰지 않는 룬을 드롭다운으로 표시해 룬 ID를 고른다. 데이터 로드 실패 시 직접 입력한다.</summary>
        private void DrawRuneField(SerializedProperty runeId, string nodeId)
        {
            if (!GameData.IsLoaded)
            {
                EditorGUILayout.PropertyField(runeId, new GUIContent("룬 ID"));
                return;
            }
            string current = runeId.stringValue;
            List<string> options = new List<string> { "" };
            List<string> labels = new List<string> { "(선택 안 함)" };
            foreach (RuneDefinition rune in GameData.Runes.All)
            {
                if (!IsTreeUnlockRune(rune) || (rune.Id != current && IsRuneUsedByOtherNode(rune.Id, nodeId))) continue;
                options.Add(rune.Id);
                labels.Add(rune.Name + " (" + rune.Id + ")");
            }
            int selected = options.IndexOf(current);
            if (selected < 0)
            {
                options.Add(current);
                labels.Add(current + " (사용 불가)");
                selected = options.Count - 1;
            }
            int next = EditorGUILayout.Popup("룬", selected, labels.ToArray());
            if (next != selected) runeId.stringValue = options[next];
        }

        /// <summary>룬 해금 노드가 참조할 수 있는 tree·bench 해금 플레이어 룬인지 반환한다.</summary>
        private static bool IsTreeUnlockRune(RuneDefinition rune)
        {
            return rune.Category != SpellGrammar.CATEGORY_CORE && rune.Category != SpellGrammar.CATEGORY_INTERNAL
                && (rune.UnlockType == "tree" || rune.UnlockType == "bench");
        }

        /// <summary>지정 노드 외의 룬 해금 노드가 같은 룬을 쓰는지 검사한다.</summary>
        private bool IsRuneUsedByOtherNode(string runeId, string nodeId)
        {
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
                if (node != null && node.Id != nodeId && node.EffectType == UpgradeEffectType.RuneUnlock && node.RuneId == runeId)
                    return true;
            return false;
        }

        /// <summary>레벨별 비용·효과량 입력 행과 레벨 추가·삭제 버튼을 표시한다. 룬 해금 노드는 레벨 수를 바꿀 수 없다.</summary>
        private static void DrawLevels(SerializedProperty levels, bool isRuneUnlock)
        {
            EditorGUILayout.LabelField("레벨 (비용 / 효과량)", EditorStyles.miniBoldLabel);
            int removeIndex = -1;
            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                SerializedProperty cost = level.FindPropertyRelative("_cost");
                SerializedProperty amount = level.FindPropertyRelative("_amount");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Lv " + (i + 1), GUILayout.Width(44));
                cost.intValue = Mathf.Max(0, EditorGUILayout.IntField(cost.intValue));
                amount.floatValue = Mathf.Max(0, EditorGUILayout.FloatField(amount.floatValue));
                using (new EditorGUI.DisabledScope(isRuneUnlock || levels.arraySize <= 1))
                {
                    if (GUILayout.Button("-", GUILayout.Width(22))) removeIndex = i;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (removeIndex >= 0) levels.DeleteArrayElementAtIndex(removeIndex);
            using (new EditorGUI.DisabledScope(isRuneUnlock || levels.arraySize >= MAX_UPGRADE_LEVEL))
            {
                if (GUILayout.Button("레벨 추가")) levels.arraySize++;
            }
        }

        /// <summary>배타 세트 그룹 ID 입력란과 안내 문구, 같은 그룹의 다른 노드 ID 목록을 표시한다. 입력값의 앞뒤 공백은 잘라 저장한다.</summary>
        private void DrawExclusiveGroupField(SerializedProperty property, UpgradeTreeNodeDefinition node)
        {
            SerializedProperty groupProperty = property.FindPropertyRelative("_exclusiveGroupId");
            string input = EditorGUILayout.DelayedTextField("배타 세트", groupProperty.stringValue);
            if (input != groupProperty.stringValue)
                groupProperty.stringValue = input.Trim();
            EditorGUILayout.LabelField("같은 값을 가진 노드끼리 하나만 구매할 수 있습니다. 같은 부모 하나를 가져야 합니다.", EditorStyles.wordWrappedMiniLabel);
            string groupId = groupProperty.stringValue;
            if (string.IsNullOrEmpty(groupId)) return;
            List<string> siblingIds = new List<string>();
            foreach (UpgradeTreeNodeDefinition other in _definition.Nodes)
            {
                if (other == null || other == node || other.ExclusiveGroupId != groupId) continue;
                siblingIds.Add(other.Id);
            }
            if (siblingIds.Count > 0)
                EditorGUILayout.LabelField("같은 세트: " + string.Join(", ", siblingIds), EditorStyles.miniLabel);
        }

        /// <summary>선택한 노드의 부모·자식 연결을 행으로 표시하고, 끊기 버튼을 누르면 해당 선행 관계를 제거한다.</summary>
        private void DrawConnectionRows(UpgradeTreeNodeDefinition node)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("연결", EditorStyles.miniBoldLabel);
            string removeChildId = null;
            string removeParentId = null;
            bool hasConnection = false;
            foreach (UpgradeTreePrerequisite prerequisite in node.Prerequisites)
            {
                if (prerequisite == null) continue;
                hasConnection = true;
                if (DrawConnectionRow("부모", prerequisite.NodeId))
                {
                    removeChildId = node.Id;
                    removeParentId = prerequisite.NodeId;
                }
            }
            foreach (UpgradeTreeNodeDefinition child in _definition.Nodes)
            {
                if (child == null || child == node || !HasPrerequisite(child, node.Id)) continue;
                hasConnection = true;
                if (DrawConnectionRow("자식", child.Id))
                {
                    removeChildId = child.Id;
                    removeParentId = node.Id;
                }
            }
            if (!hasConnection)
                EditorGUILayout.LabelField("연결 없음 — 연결 모드에서 지정", EditorStyles.wordWrappedMiniLabel);
            // 목록 순회가 끝난 뒤 제거해 순회 중 컬렉션 변경을 피한다.
            if (removeChildId != null) RemoveEdge(removeChildId, removeParentId);
        }

        /// <summary>연결 한 줄을 역할·노드 ID·끊기 버튼으로 표시하고 버튼 클릭 여부를 반환한다.</summary>
        private static bool DrawConnectionRow(string role, string nodeId)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(role, nodeId);
            bool isClicked = GUILayout.Button("끊기", GUILayout.Width(44));
            EditorGUILayout.EndHorizontal();
            return isClicked;
        }

        /// <summary>화면 지점에 놓인 배치 노드를 반환한다.</summary>
        private UpgradeTreeNodeDefinition FindNodeAt(Vector2 origin, Vector2 point, int minCol, int minRow)
        {
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
                if (node != null && NodeRect(origin, node.LayoutCell, minCol, minRow).Contains(point))
                    return node;
            return null;
        }

        /// <summary>화면 지점에 가장 가까운 엣지를 반환하고 임계 거리 밖이면 null을 반환한다.</summary>
        private EdgeHit? FindEdgeAt(Vector2 origin, Vector2 point, int minCol, int minRow)
        {
            EdgeHit? nearest = null;
            float nearestDistance = EDGE_HIT_DISTANCE;
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
            {
                if (node == null || node.Prerequisites.Count == 0) continue;
                foreach (UpgradeTreePrerequisite prerequisite in node.Prerequisites)
                {
                    if (prerequisite == null) continue;
                    UpgradeTreeNodeDefinition parent = _definition.FindNode(prerequisite.NodeId);
                    if (parent == null) continue;
                    Vector2 start = CellCenter(origin, parent.LayoutCell, minCol, minRow);
                    Vector2 end = CellCenter(origin, node.LayoutCell, minCol, minRow);
                    float distance = DistanceToSegment(point, start, end);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = new EdgeHit(prerequisite.NodeId, node.Id);
                    }
                }
            }
            return nearest;
        }

        /// <summary>점과 선분 사이의 거리를 반환한다.</summary>
        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 line = end - start;
            float lengthSquared = line.sqrMagnitude;
            if (lengthSquared < 0.001f) return Vector2.Distance(point, start);
            float t = Mathf.Clamp01(Vector2.Dot(point - start, line) / lengthSquared);
            return Vector2.Distance(point, start + line * t);
        }

        /// <summary>화면 지점이 속한 격자 셀을 반환한다.</summary>
        private static Vector2Int CellAt(Vector2 origin, Vector2 point, int minCol, int minRow)
        {
            return new Vector2Int(
                Mathf.FloorToInt((point.x - origin.x) / CELL_PIXELS) + minCol,
                Mathf.FloorToInt((point.y - origin.y) / CELL_PIXELS) + minRow);
        }

        /// <summary>다른 배치 노드가 해당 셀을 이미 쓰고 있는지 검사한다.</summary>
        private bool IsCellOccupied(Vector2Int cell, UpgradeTreeNodeDefinition ignore)
        {
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
                if (node != null && node != ignore && node.LayoutCell == cell) return true;
            return false;
        }

        /// <summary>노드가 지정 부모의 선행 조건을 갖고 있는지 검사한다.</summary>
        private static bool HasPrerequisite(UpgradeTreeNodeDefinition node, string parentId)
        {
            foreach (UpgradeTreePrerequisite prerequisite in node.Prerequisites)
                if (prerequisite != null && prerequisite.NodeId == parentId) return true;
            return false;
        }

        /// <summary>노드 ID로 배치 노드를 찾고 없으면 null을 반환한다.</summary>
        private UpgradeTreeNodeDefinition FindPlacedNode(string nodeId)
        {
            if (nodeId == null || _definition == null) return null;
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
                if (node != null && node.Id == nodeId) return node;
            return null;
        }

        /// <summary>노드 ID로 SO 배열 인덱스를 찾고 없으면 -1을 반환한다.</summary>
        private int FindNodeIndex(string nodeId)
        {
            if (nodeId == null) return -1;
            for (int index = 0; index < _definition.Nodes.Count; index++)
            {
                UpgradeTreeNodeDefinition node = _definition.Nodes[index];
                if (node != null && node.Id == nodeId) return index;
            }
            return -1;
        }

        /// <summary>노드의 격자 셀을 SerializedObject 경로로 바꾸고 Undo를 등록한 뒤 검증을 갱신한다.</summary>
        private void WriteLayoutCell(UpgradeTreeNodeDefinition node, Vector2Int cell)
        {
            int index = FindNodeIndex(node.Id);
            if (index < 0) return;
            Undo.RecordObject(_definition, "Move Upgrade Tree Node");
            SerializedObject serialized = new SerializedObject(_definition);
            serialized.Update();
            SerializedProperty cellProperty = serialized.FindProperty("_nodes")
                .GetArrayElementAtIndex(index)
                .FindPropertyRelative("_layoutCell");
            cellProperty.FindPropertyRelative("x").intValue = cell.x;
            cellProperty.FindPropertyRelative("y").intValue = cell.y;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(_definition);
            RefreshState();
        }

        /// <summary>자식 노드에 부모 선행 조건을 추가하고 Undo를 등록한 뒤 검증을 갱신한다.</summary>
        private void WriteEdge(string parentId, string childId)
        {
            int index = FindNodeIndex(childId);
            if (index < 0) return;
            Undo.RecordObject(_definition, "Connect Upgrade Tree Nodes");
            SerializedObject serialized = new SerializedObject(_definition);
            serialized.Update();
            SerializedProperty prerequisites = serialized.FindProperty("_nodes")
                .GetArrayElementAtIndex(index)
                .FindPropertyRelative("_prerequisites");
            prerequisites.arraySize++;
            SerializedProperty entry = prerequisites.GetArrayElementAtIndex(prerequisites.arraySize - 1);
            entry.FindPropertyRelative("_nodeId").stringValue = parentId;
            entry.FindPropertyRelative("_requiredLevel").intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(_definition);
            RefreshState();
        }

        /// <summary>자식 노드에서 지정 부모의 선행 조건을 제거하고 Undo를 등록한 뒤 검증을 갱신한다.</summary>
        private void RemoveEdge(string childId, string parentId)
        {
            int index = FindNodeIndex(childId);
            if (index < 0) return;
            Undo.RecordObject(_definition, "Remove Upgrade Tree Edge");
            SerializedObject serialized = new SerializedObject(_definition);
            serialized.Update();
            SerializedProperty prerequisites = serialized.FindProperty("_nodes")
                .GetArrayElementAtIndex(index)
                .FindPropertyRelative("_prerequisites");
            for (int i = prerequisites.arraySize - 1; i >= 0; i--)
            {
                if (prerequisites.GetArrayElementAtIndex(i).FindPropertyRelative("_nodeId").stringValue == parentId)
                    prerequisites.DeleteArrayElementAtIndex(i);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(_definition);
            RefreshState();
        }

        /// <summary>확인 후 리프 노드를 트리에서 삭제하고 Undo를 등록한다. 메인보드와 자식 보유 노드는 차단한다.</summary>
        private void DeleteNode(string nodeId)
        {
            if (nodeId == MAINBOARD_ID)
            {
                EditorUtility.DisplayDialog("노드 삭제 차단", "메인보드는 트리의 유일한 시작 노드라 삭제할 수 없습니다.", "확인");
                return;
            }
            int children = 0;
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
                if (node != null && node.Id != nodeId && HasPrerequisite(node, nodeId)) children++;
            if (children > 0)
            {
                EditorUtility.DisplayDialog("노드 삭제 차단",
                    "이 노드를 부모로 참조하는 자식이 " + children + "개 있습니다. 먼저 자식 노드를 삭제하거나 선을 끊으세요.", "확인");
                return;
            }
            if (!EditorUtility.DisplayDialog("노드 삭제",
                    nodeId + " 노드를 삭제합니다. 기존 저장 데이터의 이 노드 레벨은 더 이상 읽히지 않습니다.", "삭제", "취소"))
                return;
            int index = FindNodeIndex(nodeId);
            if (index < 0) return;
            Undo.RecordObject(_definition, "Delete Upgrade Tree Node");
            SerializedObject serialized = new SerializedObject(_definition);
            serialized.Update();
            serialized.FindProperty("_nodes").DeleteArrayElementAtIndex(index);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(_definition);
            _selectedNodeId = null;
            RefreshState();
        }

        /// <summary>격자 셀의 화면 중심 좌표를 반환한다.</summary>
        private static Vector2 CellCenter(Vector2 origin, Vector2Int cell, int minCol, int minRow)
        {
            return origin + new Vector2((cell.x - minCol + 0.5f) * CELL_PIXELS, (cell.y - minRow + 0.5f) * CELL_PIXELS);
        }

        /// <summary>격자 셀 안의 노드 사각형을 반환한다.</summary>
        private static Rect NodeRect(Vector2 origin, Vector2Int cell, int minCol, int minRow)
        {
            Vector2 position = origin + new Vector2((cell.x - minCol) * CELL_PIXELS, (cell.y - minRow) * CELL_PIXELS);
            return new Rect(position.x + NODE_INSET, position.y + NODE_INSET,
                CELL_PIXELS - NODE_INSET * 2, CELL_PIXELS - NODE_INSET * 2);
        }

        /// <summary>그룹 ID 문자열의 결정적 해시로 배타 세트 테두리색을 반환한다. 같은 그룹은 항상 같은 색이 된다.</summary>
        private static Color GetGroupColor(string groupId)
        {
            int hash = 0;
            foreach (char character in groupId)
                hash = hash * 31 + character;
            return Color.HSVToRGB(((hash & int.MaxValue) % 360) / 360f, 0.85f, 1f);
        }

        /// <summary>효과 종류마다 구분되는 노드 배경색을 반환한다.</summary>
        private static Color GetEffectColor(UpgradeEffectType type)
        {
            switch (type)
            {
                case UpgradeEffectType.EnergyRegen: return new Color(0.20f, 0.45f, 0.65f);
                case UpgradeEffectType.MaxEnergy: return new Color(0.85f, 0.75f, 0.25f);
                case UpgradeEffectType.RamCapacity: return new Color(0.45f, 0.65f, 0.30f);
                case UpgradeEffectType.CastSpeed: return new Color(0.30f, 0.75f, 0.70f);
                case UpgradeEffectType.Damage: return new Color(0.75f, 0.35f, 0.30f);
                case UpgradeEffectType.ScrapGain: return new Color(0.80f, 0.60f, 0.35f);
                case UpgradeEffectType.MaxHp: return new Color(0.70f, 0.30f, 0.45f);
                case UpgradeEffectType.MoveSpeed: return new Color(0.50f, 0.50f, 0.75f);
                case UpgradeEffectType.RuneUnlock: return new Color(0.60f, 0.40f, 0.70f);
                case UpgradeEffectType.ScrapPickupRange: return new Color(0.60f, 0.55f, 0.35f);
                default: return new Color(0.45f, 0.45f, 0.45f);
            }
        }

        /// <summary>노드 라벨에 쓰는 스타일을 한 번만 만든다.</summary>
        private void EnsureStyles()
        {
            if (_nodeLabelStyle != null) return;
            _nodeLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                margin = new RectOffset(4, 4, 4, 4)
            };
            _nodeLabelStyle.normal.textColor = Color.white;
        }
    }
}
