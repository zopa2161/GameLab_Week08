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
        public const float NODE_BASE_HEIGHT = 51f;

        private const float PORT_SPACING = 23f;
        private const float PORT_TOP_OFFSET = 52f;
        private const float PORT_HIT_RADIUS = 9f;
        private const float MIN_PORT_HIT_RADIUS = 8f;
        private const float EDGE_HIT_DISTANCE = 8f;
        private const float SLOT_ROW_HEIGHT = 26f;
        private const float SLOT_GAP = 3f;
        private const float SLOT_MARGIN = 6f;
        private const float SLOT_PADDING = 6f;
        private const int MODIFIER_SLOT_COLUMNS = 2;
        private const int MODIFIER_SLOT_ROWS = SpellGrammar.MAX_MODIFIER_SLOTS / MODIFIER_SLOT_COLUMNS;
        private const int GRADE_BUTTON_ROWS = 1;

        // 행동 노드 슬롯 키를 속성 슬롯과 모디 슬롯 순서로 나열한다.
        private static readonly string[] SLOT_KEYS = BuildSlotKeys();

        private readonly GraphViewport _viewport;
        private readonly Func<SpellGraph> _graph;
        private readonly bool _isReadOnly;
        private readonly Func<string, bool> _isElementTriggerUnlocked;
        private readonly Dictionary<string, GraphNode> _nodeLookup = new Dictionary<string, GraphNode>();

        /// <summary>뷰포트와 현재 그래프 조회 함수로 배치 계산기를 만든다.</summary>
        public GraphNodeLayout(GraphViewport viewport, Func<SpellGraph> graph, bool isReadOnly = false,
            Func<string, bool> isElementTriggerUnlocked = null)
        {
            _viewport = viewport;
            _graph = graph;
            _isReadOnly = isReadOnly;
            _isElementTriggerUnlocked = isElementTriggerUnlocked;
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

        /// <summary>행동 노드 슬롯 영역 맨 아래 행의 등급 변경 버튼 사각형을 구한다. 행동 노드가 아니면 false를 반환한다.</summary>
        public bool TryGetGradeButtonRect(GraphNode node, RuneDefinition rune, out Rect rect)
        {
            rect = default;
            if (_isReadOnly || !HasSlots(rune)) return false;
            rect = CellRect(node, rune, ElementSlotCount(node) + MODIFIER_SLOT_ROWS, 0, 1);
            return true;
        }

        /// <summary>캔버스 로컬 위치에 있는 가장 앞쪽 노드가 행동 노드이고 위치가 등급 변경 버튼 안이면 그 노드를 반환한다.</summary>
        public bool FindGradeButton(Vector2 point, out GraphNode foundNode)
        {
            foundNode = null;
            IReadOnlyList<GraphNode> nodes = _graph().Nodes;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                RuneDefinition rune = GameData.Runes.Get(nodes[i].RuneId);
                if (!NodeRect(nodes[i], rune).Contains(point)) continue;
                if (!TryGetGradeButtonRect(nodes[i], rune, out Rect rect) || !rect.Contains(point)) return false;
                foundNode = nodes[i];
                return true;
            }
            return false;
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
            if (!port.IsDeprecated && (!SpellGrammar.IsElementTriggerPort(port.Id)
                || (_isElementTriggerUnlocked?.Invoke(port.Id) ?? true))) return true;
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
                    if (IsPortShown(node, port) && Vector2.Distance(point, PortPosition(rect, node, rune, port.Id)) < radius)
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

        /// <summary>행동 등급의 속성 행, Modifier 여섯 칸의 세 행과 등급 변경 버튼 행을 포함한 슬롯 영역 높이를 반환한다.</summary>
        private float SlotSectionHeight(GraphNode node)
            => (ElementSlotCount(node) + MODIFIER_SLOT_ROWS + (_isReadOnly ? 0 : GRADE_BUTTON_ROWS)) * SLOT_ROW_HEIGHT + SLOT_PADDING;

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
