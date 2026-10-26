using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 그래프 노드의 화면 크기·포트 위치 계산과 화면 좌표의 노드·포트·연결선 찾기를 맡는다.
    /// 지원 중단 포트는 기존 연결이 있을 때만 표시 포트로 친다.
    /// </summary>
    internal sealed class GraphNodeLayout
    {
        public const float NODE_WIDTH = 160f;
        public const float SLOT_IMAGE_SCALE = 0.5f;
        public const float NODE_BASE_HEIGHT = 51f;
        public const float PUZZLE_DEPTH = 12f;
        public const float PUZZLE_HALF_HEIGHT = 10f;

        /// <summary>실행 입력과 사건·제어 출력 중 퍼즐 결합부로 표시할 활성 포트인지 반환한다.</summary>
        public static bool IsPuzzlePort(PortDefinition port)
        {
            return port != null && !port.IsDeprecated && port.Kind == SpellGrammar.EXEC_KIND
                && (port.Direction == SpellGrammar.DIRECTION_IN || port.Direction == SpellGrammar.DIRECTION_OUT);
        }

        private const float PORT_SPACING = 23f;
        private const float PORT_TOP_OFFSET = 52f;
        private const float PORT_HIT_RADIUS = 9f;
        private const float MIN_PORT_HIT_RADIUS = 8f;
        private const float EDGE_HIT_DISTANCE = 8f;
        private const float SLOT_ROW_HEIGHT = 28f;
        private const float SLOT_GAP = 3f;
        private const float SLOT_MARGIN = 7f;
        private const float SLOT_PADDING = 6f;
        private const int MODIFIER_SLOT_COLUMNS = 2;
        private const int MODIFIER_SLOT_ROWS = SpellGrammar.MAX_MODIFIER_SLOTS / MODIFIER_SLOT_COLUMNS;
        private const string GHOST_ID_PREFIX = "ghost:";

        // 행동 노드 슬롯 키를 속성 슬롯과 모디 슬롯 순서로 나열한다.
        private static readonly string[] SLOT_KEYS = BuildSlotKeys();

        private readonly GraphViewport _viewport;
        private readonly Func<SpellGraph> _graph;
        private readonly Dictionary<string, GraphNode> _nodeLookup = new Dictionary<string, GraphNode>();
        private readonly List<GraphNode> _ghosts = new List<GraphNode>();
        private readonly Dictionary<string, GraphNode> _ghostBySource = new Dictionary<string, GraphNode>();

        /// <summary>행동 하나 고정 강화의 자기 복제를 보여 주는 표시 전용 노드 목록이다. 그래프에 없으므로 선택·연결 판정에서 제외된다.</summary>
        public IReadOnlyList<GraphNode> Ghosts => _ghosts;

        /// <summary>뷰포트와 현재 그래프 조회 함수로 배치 계산기를 만든다.</summary>
        public GraphNodeLayout(GraphViewport viewport, Func<SpellGraph> graph)
        {
            _viewport = viewport;
            _graph = graph;
        }

        /// <summary>활성 실행 연결을 따라 후속 묶음을 배치한다. resetRoots가 true이면 독립 묶음도 세로로 정렬하며 위치 변경 여부를 반환한다.</summary>
        public bool ArrangeBlockGroups(bool resetRoots = false)
        {
            SpellGraph graph = _graph();
            var incoming = new HashSet<string>();
            foreach (GraphEdge edge in graph.Edges)
                if (IsBlockEdge(edge)) incoming.Add(edge.ToNode);
            var visited = new HashSet<string>();
            bool changed = false;
            float nextRootY = 55;
            foreach (GraphNode root in graph.Nodes)
            {
                if (incoming.Contains(root.Id)) continue;
                if (resetRoots) changed |= MoveBlock(root, 35, nextRootY);
                float bottom = ArrangeBlock(root, visited, ref changed);
                nextRootY = bottom + 40;
            }
            return changed;
        }

        /// <summary>
        /// isEnabled가 true면 시작 노드의 실행 출력에 행동 하나만 이어진 경우 그 행동과 같은 구성의 표시 전용 복제 노드를 만들어 onHit 출력 옆에 붙인다.
        /// false면 복제 노드를 모두 비운다.
        /// </summary>
        public void RebuildGhosts(bool isEnabled)
        {
            _ghosts.Clear();
            _ghostBySource.Clear();
            if (!isEnabled) return;
            SpellGraph graph = _graph();
            foreach (GraphNode start in graph.Nodes)
            {
                if (!GameData.Runes.TryGet(start.RuneId, out RuneDefinition startRune) || startRune.Category != SpellGrammar.CATEGORY_CORE) continue;
                GraphNode behavior = null;
                int behaviorCount = 0;
                foreach (GraphEdge edge in graph.Edges)
                {
                    if (edge.FromNode != start.Id || edge.FromPort != SpellGrammar.EXEC_PORT) continue;
                    behaviorCount++;
                    behavior = graph.FindNode(edge.ToNode);
                }
                if (behaviorCount != 1 || behavior == null || _ghostBySource.ContainsKey(behavior.Id)
                    || !GameData.Runes.TryGet(behavior.RuneId, out RuneDefinition rune) || rune.Category != SpellGrammar.CATEGORY_BEHAVIOR) continue;
                GraphNode ghost = behavior.Clone(GHOST_ID_PREFIX + behavior.Id);
                _ghostBySource.Add(behavior.Id, ghost);
                _ghosts.Add(ghost);
            }
            PlaceGhosts();
        }

        /// <summary>복제 노드를 원본 행동의 현재 위치 기준 onHit 출력 옆으로 옮긴다. 원본을 끌어 옮기는 중에도 따라가도록 배치 때마다 호출한다.</summary>
        public void PlaceGhosts()
        {
            foreach (KeyValuePair<string, GraphNode> pair in _ghostBySource)
            {
                GraphNode source = FindNode(pair.Key);
                if (source == null) continue;
                Vector2 position = GhostPosition(source, pair.Value);
                pair.Value.Move(position.x, position.y);
            }
        }

        /// <summary>원본 행동의 onHit 출력과 복제 노드의 실행 입력 높이를 맞춘 복제 노드의 그래프 좌표를 반환한다.</summary>
        private Vector2 GhostPosition(GraphNode source, GraphNode ghost)
        {
            RuneDefinition rune = GameData.Runes.Get(source.RuneId);
            Rect sourceRect = NodeRect(source, rune);
            Rect ghostRect = NodeRect(ghost, rune);
            float outputY = (sourceRect.yMax - PortPosition(sourceRect, source, rune, SpellGrammar.ON_HIT_PORT).y) / _viewport.Zoom;
            float inputY = (ghostRect.yMax - PortPosition(ghostRect, ghost, rune, SpellGrammar.EXEC_PORT).y) / _viewport.Zoom;
            return new Vector2(source.X + NODE_WIDTH, source.Y + outputY - inputY);
        }

        /// <summary>유효한 실행 출력 엣지인지 반환한다. 지원 중단·효과 연결은 블록 묶음 배치에서 제외한다.</summary>
        public bool IsBlockEdge(GraphEdge edge)
        {
            GraphNode source = _graph().FindNode(edge.FromNode);
            if (source == null) return false;
            PortDefinition port = GameData.Runes.Get(source.RuneId).FindPort(edge.FromPort, SpellGrammar.DIRECTION_OUT);
            return port != null && !port.IsDeprecated && port.Kind == SpellGrammar.EXEC_KIND;
        }

        /// <summary>한 블록의 출력 순서와 분기별 전체 높이로 후속을 겹치지 않게 배치하고 가장 아래 그래프 좌표를 반환한다.</summary>
        private float ArrangeBlock(GraphNode node, HashSet<string> visited, ref bool changed)
        {
            RuneDefinition rune = GameData.Runes.Get(node.RuneId);
            float bottom = node.Y + NodeHeight(node, rune);
            if (!visited.Add(node.Id)) return bottom;
            var outgoing = new List<GraphEdge>();
            foreach (GraphEdge edge in _graph().Edges)
                if (edge.FromNode == node.Id && IsBlockEdge(edge)) outgoing.Add(edge);
            outgoing.Sort((left, right) =>
            {
                int order = IndexOfPort(rune, left.FromPort).CompareTo(IndexOfPort(rune, right.FromPort));
                return order != 0 ? order : left.Order.CompareTo(right.Order);
            });
            float branchBottom = float.MinValue;
            // 복제 노드가 onHit의 첫 자리를 차지하므로 실제 후속 블록은 그 아래에 놓는다.
            if (_ghostBySource.TryGetValue(node.Id, out GraphNode ghost))
            {
                Vector2 ghostPosition = GhostPosition(node, ghost);
                ghost.Move(ghostPosition.x, ghostPosition.y);
                branchBottom = ghost.Y + NodeHeight(ghost, rune) + 24;
                bottom = Mathf.Max(bottom, branchBottom - 24);
            }
            foreach (GraphEdge edge in outgoing)
            {
                GraphNode child = _graph().FindNode(edge.ToNode);
                if (child == null || visited.Contains(child.Id)) continue;
                RuneDefinition childRune = GameData.Runes.Get(child.RuneId);
                Rect parentRect = NodeRect(node, rune);
                Rect childRect = NodeRect(child, childRune);
                float outputY = (parentRect.yMax - PortPosition(parentRect, node, rune, edge.FromPort).y) / _viewport.Zoom;
                float inputY = (childRect.yMax - PortPosition(childRect, child, childRune, edge.ToPort).y) / _viewport.Zoom;
                bool isNested = edge.FromPort == SpellGrammar.BODY_PORT || edge.FromPort == "then" || edge.FromPort == "else";
                float childY = isNested ? bottom + 20 : node.Y + outputY - inputY;
                childY = Mathf.Max(childY, branchBottom);
                changed |= MoveBlock(child, node.X + (isNested ? 32 : NODE_WIDTH), childY);
                branchBottom = ArrangeBlock(child, visited, ref changed) + 24;
                bottom = Mathf.Max(bottom, branchBottom - 24);
            }
            return bottom;
        }

        /// <summary>좌표 차이가 작은 부동소수점 흔들림이면 유지하고, 그 외에는 블록 위치를 바꿔 변경 여부를 반환한다.</summary>
        private static bool MoveBlock(GraphNode node, float x, float y)
        {
            if (Mathf.Abs(node.X - x) < 0.01f && Mathf.Abs(node.Y - y) < 0.01f) return false;
            node.Move(x, y);
            return true;
        }

        /// <summary>제어 블록과 내부 실행·조건 분기의 캔버스 사각형을 반환한다. 완료 후 후속은 감싸는 프레임에서 제외한다.</summary>
        public Rect BlockGroupBounds(string nodeId)
        {
            var nestedRoots = new List<string>();
            foreach (GraphEdge edge in _graph().Edges)
                if (edge.FromNode == nodeId && (edge.FromPort == SpellGrammar.BODY_PORT
                    || edge.FromPort == "then" || edge.FromPort == "else") && IsBlockEdge(edge)) nestedRoots.Add(edge.ToNode);
            HashSet<string> group = _graph().GetBlockGroup(nestedRoots, GameData.Runes);
            group.Add(nodeId);
            Vector2 minimum = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 maximum = new Vector2(float.MinValue, float.MinValue);
            foreach (string id in group)
            {
                GraphNode node = _graph().FindNode(id);
                Rect bounds = NodeRect(node, GameData.Runes.Get(node.RuneId));
                minimum = Vector2.Min(minimum, bounds.min);
                maximum = Vector2.Max(maximum, bounds.max);
            }
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        /// <summary>현재 그래프 노드를 ID로 찾는 조회 테이블을 다시 채운다. 연결선 위치 조회 전에 호출한다.</summary>
        public void RebuildLookup()
        {
            _nodeLookup.Clear();
            foreach (GraphNode node in _graph().Nodes) _nodeLookup[node.Id] = node;
        }

        /// <summary>행동 노드 슬롯 키 목록(속성 슬롯, 모디 슬롯 순)이다.</summary>
        public static IReadOnlyList<string> SlotKeys => SLOT_KEYS;

        /// <summary>노드의 표시 포트 수로 계산한 배율 적용 전 노드 높이를 반환한다. 행동 노드는 슬롯 영역 높이를 더한다.</summary>
        public float NodeHeight(GraphNode node, RuneDefinition rune)
        {
            float height = NODE_BASE_HEIGHT + PortRowCount(node, rune) * PORT_SPACING;
            return HasSlots(rune) ? height + SlotSectionHeight(node) : height;
        }

        /// <summary>룬이 속성·모디 슬롯을 가진 행동 노드인지 반환한다.</summary>
        public static bool HasSlots(RuneDefinition rune)
        {
            return rune != null && !rune.IsDisabled && rune.Category == SpellGrammar.CATEGORY_BEHAVIOR;
        }

        /// <summary>
        /// 슬롯 순번이 행동 노드 등급의 속성·모디 슬롯 수 이상이면 잠긴 슬롯으로 보고 true를 반환한다.
        /// 슬롯 키가 아니면 true를 반환한다.
        /// </summary>
        public static bool IsSlotLocked(GraphNode node, string slotKey)
        {
            if (!SpellGrammar.TryParseSlotKey(slotKey, out bool isElement, out int index)) return true;
            string grade = node.GetText(SpellGrammar.MODIFIER_GRADE_PARAM, SpellGrammar.DEFAULT_BEHAVIOR_GRADE);
            if (!GameData.Runes.TryGetBehaviorSlots(grade, out int elementSlots, out int modifierSlots)) return true;
            return index >= (isElement ? elementSlots : modifierSlots);
        }

        /// <summary>
        /// 행동 노드 사각형 아래쪽 슬롯 영역에서 element·modifier 슬롯 키에 해당하는 셀 사각형을 구한다.
        /// 행동 노드가 아니거나 슬롯 키가 아니면 false를 반환한다.
        /// </summary>
        public bool TryGetSlotRect(GraphNode node, RuneDefinition rune, string slotKey, out Rect rect)
        {
            rect = default;
            if (!HasSlots(rune)) return false;
            if (!SpellGrammar.TryParseSlotKey(slotKey, out bool isElement, out int index)) return false;

            int elementSlots = ElementSlotCount(node);
            int row;
            int column;
            int columns;
            if (isElement)
            {
                if (index >= elementSlots) return false;
                row = index;
                column = 0;
                columns = 1;
            }
            else
            {
                row = elementSlots + index / MODIFIER_SLOT_COLUMNS;
                column = index % MODIFIER_SLOT_COLUMNS;
                columns = MODIFIER_SLOT_COLUMNS;
            }

            rect = CellRect(node, rune, row, column, columns);
            return true;
        }

        /// <summary>캔버스 로컬 위치에 있는 가장 앞쪽 행동 노드의 슬롯 셀을 찾아 노드와 슬롯 키를 반환한다.</summary>
        public bool FindSlot(Vector2 point, out GraphNode foundNode, out string foundSlotKey)
        {
            IReadOnlyList<GraphNode> nodes = _graph().Nodes;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                RuneDefinition rune = GameData.Runes.Get(nodes[i].RuneId);
                if (!HasSlots(rune) || !NodeRect(nodes[i], rune).Contains(point)) continue;
                foreach (string slotKey in SLOT_KEYS)
                {
                    if (!TryGetSlotRect(nodes[i], rune, slotKey, out Rect rect) || !rect.Contains(point)) continue;
                    foundNode = nodes[i];
                    foundSlotKey = slotKey;
                    return true;
                }
                break;
            }
            foundNode = null;
            foundSlotKey = null;
            return false;
        }

        /// <summary>입력·출력 표시 포트 중 많은 쪽의 개수를 노드의 포트 행 수로 반환한다.</summary>
        public int PortRowCount(GraphNode node, RuneDefinition rune)
        {
            return Math.Max(CountPorts(node, rune, SpellGrammar.DIRECTION_IN), CountPorts(node, rune, SpellGrammar.DIRECTION_OUT));
        }

        /// <summary>표시 포트 수와 배율을 반영한 캔버스 로컬 노드 사각형을 반환한다.</summary>
        public Rect NodeRect(GraphNode node, RuneDefinition rune)
        {
            float height = NodeHeight(node, rune) * _viewport.Zoom;
            Vector2 topLeft = _viewport.ToLocal(new Vector2(node.X, node.Y));
            return new Rect(topLeft.x, topLeft.y - height, NODE_WIDTH * _viewport.Zoom, height);
        }

        /// <summary>그래프 좌표 기준 모든 노드의 최소·최대 범위를 구한다. 노드가 없으면 false를 반환한다.</summary>
        public bool TryGetGraphBounds(out Vector2 minimum, out Vector2 maximum)
        {
            minimum = new Vector2(float.MaxValue, float.MaxValue);
            maximum = new Vector2(float.MinValue, float.MinValue);
            IReadOnlyList<GraphNode> nodes = _graph().Nodes;
            foreach (GraphNode node in nodes)
            {
                float height = NodeHeight(node, GameData.Runes.Get(node.RuneId));
                minimum = Vector2.Min(minimum, new Vector2(node.X, node.Y));
                maximum = Vector2.Max(maximum, new Vector2(node.X + NODE_WIDTH, node.Y + height));
            }
            return nodes.Count > 0;
        }

        /// <summary>노드 사각형에서 포트 ID에 해당하는 포트 중심 좌표를 반환하며 포트가 없으면 노드 중심을 반환한다.</summary>
        public Vector2 PortPosition(Rect rect, GraphNode node, RuneDefinition rune, string portId)
        {
            int index = IndexOfPort(rune, portId);
            if (index < 0) return rect.center;

            // 같은 방향의 표시 포트 중 몇 번째인지가 세로 행 위치가 된다.
            PortDefinition port = rune.Ports[index];
            int row = 0;
            for (int i = 0; i < index; i++)
            {
                if (rune.Ports[i].Direction == port.Direction && IsPortShown(node, rune.Ports[i])) row++;
            }
            float x = port.Direction == SpellGrammar.DIRECTION_IN ? rect.xMin : rect.xMax;
            float y = rect.yMax - (PORT_TOP_OFFSET + row * PORT_SPACING) * _viewport.Zoom;
            return new Vector2(x, y);
        }

        /// <summary>조회 테이블에서 노드를 찾아 포트 중심 좌표를 반환하며 노드가 없으면 원점을 반환한다. RebuildLookup 이후 호출한다.</summary>
        public Vector2 LookupPortPosition(string nodeId, string portId)
        {
            if (!_nodeLookup.TryGetValue(nodeId, out GraphNode node)) return Vector2.zero;
            RuneDefinition rune = GameData.Runes.Get(node.RuneId);
            return PortPosition(NodeRect(node, rune), node, rune, portId);
        }

        /// <summary>노드에 표시할 포트인지 반환한다. 지원 중단 포트는 기존 연결이 남아 있을 때만 표시한다.</summary>
        public bool IsPortShown(GraphNode node, PortDefinition port)
        {
            if (!port.IsDeprecated) return true;
            foreach (GraphEdge edge in _graph().Edges)
            {
                if (edge.FromNode == node.Id && edge.FromPort == port.Id) return true;
            }
            return false;
        }

        /// <summary>연결의 출발 포트가 지원 중단 포트(실행되지 않는 기존 연결)인지 반환한다. RebuildLookup 이후 호출한다.</summary>
        public bool IsDeprecatedEdge(GraphEdge edge)
        {
            return _nodeLookup.TryGetValue(edge.FromNode, out GraphNode node)
                && GameData.Runes.Get(node.RuneId).FindPort(edge.FromPort, SpellGrammar.DIRECTION_OUT)?.IsDeprecated == true;
        }

        /// <summary>지정 방향의 표시 포트 중 순번에 해당하는 포트를 반환하며 없으면 null을 반환한다.</summary>
        public PortDefinition PortAt(GraphNode node, RuneDefinition rune, string direction, int row)
        {
            int current = 0;
            foreach (PortDefinition port in rune.Ports)
            {
                if (port.Direction != direction || !IsPortShown(node, port)) continue;
                if (current == row) return port;
                current++;
            }
            return null;
        }

        /// <summary>그래프 노드 ID와 일치하는 노드를 반환하며 없으면 null을 반환한다.</summary>
        public GraphNode FindNode(string id)
        {
            foreach (GraphNode node in _graph().Nodes)
            {
                if (node.Id == id) return node;
            }
            return null;
        }

        /// <summary>캔버스 로컬 위치에 있는 가장 앞쪽(나중에 그린) 노드를 반환하며 없으면 null을 반환한다.</summary>
        public GraphNode FindNodeAt(Vector2 point)
        {
            IReadOnlyList<GraphNode> nodes = _graph().Nodes;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                if (NodeRect(nodes[i], GameData.Runes.Get(nodes[i].RuneId)).Contains(point)) return nodes[i];
            }
            return null;
        }

        /// <summary>캔버스 로컬 위치에서 판정 반경 안의 첫 표시 포트를 찾아 노드와 포트 정의를 반환한다.</summary>
        public bool FindPort(Vector2 point, out GraphNode foundNode, out PortDefinition foundPort)
        {
            float radius = Mathf.Max(MIN_PORT_HIT_RADIUS, PORT_HIT_RADIUS * _viewport.Zoom);
            foreach (GraphNode node in _graph().Nodes)
            {
                RuneDefinition rune = GameData.Runes.Get(node.RuneId);
                Rect rect = NodeRect(node, rune);
                foreach (PortDefinition port in rune.Ports)
                {
                    float hitRadius = IsPuzzlePort(port) ? Mathf.Max(radius, PUZZLE_DEPTH * _viewport.Zoom + MIN_PORT_HIT_RADIUS) : radius;
                    if (IsPortShown(node, port) && Vector2.Distance(point, PortPosition(rect, node, rune, port.Id)) < hitRadius)
                    {
                        foundNode = node;
                        foundPort = port;
                        return true;
                    }
                }
            }
            foundNode = null;
            foundPort = null;
            return false;
        }

        /// <summary>캔버스 로컬 위치에서 판정 거리 안의 첫 연결선을 반환하며 없으면 null을 반환한다.</summary>
        public GraphEdge FindEdgeAt(Vector2 point)
        {
            RebuildLookup();
            foreach (GraphEdge edge in _graph().Edges)
            {
                Vector2 from = LookupPortPosition(edge.FromNode, edge.FromPort);
                Vector2 to = LookupPortPosition(edge.ToNode, edge.ToPort);

                // 포인터를 선분에 투영한 가장 가까운 점과의 거리로 판정한다.
                Vector2 span = to - from;
                float t = Mathf.Clamp01(Vector2.Dot(point - from, span) / Mathf.Max(1, span.sqrMagnitude));
                if (Vector2.Distance(point, from + span * t) < EDGE_HIT_DISTANCE) return edge;
            }
            return null;
        }

        /// <summary>포트 ID와 일치하는 첫 포트의 순번을 반환하며 없으면 -1을 반환한다.</summary>
        private static int IndexOfPort(RuneDefinition rune, string portId)
        {
            for (int i = 0; i < rune.Ports.Count; i++)
            {
                if (rune.Ports[i].Id == portId) return i;
            }
            return -1;
        }

        /// <summary>
        /// 슬롯 영역 위쪽부터 행 순번과 열 순번에 해당하는 셀 사각형을 캔버스 로컬 좌표로 반환한다.
        /// columns는 그 행을 나눌 열 수다.
        /// </summary>
        private Rect CellRect(GraphNode node, RuneDefinition rune, int row, int column, int columns)
        {
            float zoom = _viewport.Zoom;
            Rect nodeRect = NodeRect(node, rune);
            float sectionTop = nodeRect.yMin + SlotSectionHeight(node) * zoom;
            float rowTop = sectionTop - (SLOT_PADDING * 0.5f + row * SLOT_ROW_HEIGHT) * zoom;
            float cellHeight = (SLOT_ROW_HEIGHT - SLOT_GAP) * zoom;
            float innerWidth = nodeRect.width - SLOT_MARGIN * 2f * zoom;
            float cellWidth = innerWidth / columns;
            return new Rect(nodeRect.x + SLOT_MARGIN * zoom + column * cellWidth, rowTop - cellHeight,
                cellWidth - SLOT_GAP * zoom, cellHeight);
        }

        /// <summary>행동 등급의 속성 행과 Modifier 여섯 칸의 세 행을 포함한 슬롯 영역 높이를 반환한다.</summary>
        private float SlotSectionHeight(GraphNode node)
            => (ElementSlotCount(node) + MODIFIER_SLOT_ROWS) * SLOT_ROW_HEIGHT + SLOT_PADDING;

        /// <summary>최대 속성 슬롯과 Modifier 슬롯에 해당하는 셀 키 배열을 만든다.</summary>
        private static string[] BuildSlotKeys()
        {
            var keys = new List<string>();
            for (int i = 0; i < SpellGrammar.MAX_ELEMENT_SLOTS; i++) keys.Add(SpellGrammar.ElementSlotKey(i));
            for (int i = 0; i < SpellGrammar.MAX_MODIFIER_SLOTS; i++) keys.Add(SpellGrammar.ModifierSlotKey(i));
            return keys.ToArray();
        }

        /// <summary>노드 등급으로 사용할 수 있는 속성 슬롯 행 수를 반환하고 등급을 찾지 못하면 최대 행 수를 반환한다.</summary>
        private static int ElementSlotCount(GraphNode node)
        {
            string grade = node.GetText(SpellGrammar.MODIFIER_GRADE_PARAM, SpellGrammar.DEFAULT_BEHAVIOR_GRADE);
            return GameData.Runes.TryGetBehaviorSlots(grade, out int elementSlots, out _)
                ? elementSlots : SpellGrammar.MAX_ELEMENT_SLOTS;
        }

        /// <summary>지정 방향에 속한 노드의 표시 포트 개수를 반환한다.</summary>
        private int CountPorts(GraphNode node, RuneDefinition rune, string direction)
        {
            int count = 0;
            foreach (PortDefinition port in rune.Ports)
            {
                if (port.Direction == direction && IsPortShown(node, port)) count++;
            }
            return count;
        }
    }
}
