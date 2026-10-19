using System;

using UnityEngine;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 그래프 편집 화면의 메시를 그린다. 배경 격자, 연결선·화살표, 노드 본체·포트, 연결 미리보기선, 영역 선택 사각형 순서다.
    /// 선택·하이라이트·연결 드래그 상태는 GraphPointerState에서 읽는다.
    /// </summary>
    internal sealed class GraphMeshPainter
    {
        private const int BACKGROUND_COLOR = 0;
        private const int GRID_COLOR = 1;
        private const int CHAIN_COLOR = 2;
        private const int MODIFIER_EDGE_COLOR = 3;
        private const int EXEC_EDGE_COLOR = 4;
        private const int EDGE_ARROW_COLOR = 5;
        private const int NODE_BORDER_COLOR = 6;
        private const int NODE_FILL_COLOR = 7;
        private const int EXECUTED_BORDER_COLOR = 8;
        private const int EXECUTED_FILL_COLOR = 9;
        private const int MODIFIER_PORT_COLOR = 10;
        private const int EXEC_PORT_COLOR = 11;
        private const int CONNECTABLE_PORT_COLOR = 12;
        private const int BLOCKED_PORT_COLOR = 13;
        private const int DEPRECATED_COLOR = 14;
        private const int SELECTION_FILL_COLOR = 15;
        private const int PREVIEW_COLOR = 17;
        private const int SELECTION_BORDER_COLOR = 18;

        private const float NODE_BORDER = 2f;
        private const float NODE_ACCENT_HEIGHT = 8f;
        private const float ICON_OFFSET_X = 14f;
        private const float ICON_OFFSET_Y = 18f;
        private const float ICON_RADIUS = 3f;
        private const float PORT_RADIUS = 5f;
        private const float GRID_STEP = 28f;
        private const float GRID_DOT_SIZE = 1.5f;
        private const float EDGE_WIDTH = 2.4f;
        private const float EDGE_ARROW_SIZE = 4.5f;
        private const float EDGE_ARROW_POSITION = 0.68f;
        private const float SELECTION_LINE_WIDTH = 1f;
        private const float SLOT_CATEGORY_TINT = 0.45f;

        private static readonly Color LOCKED_NODE_COLOR = new Color(1f, 0.25f, 0.25f);
        private static readonly Color LOCKED_NODE_FILL_COLOR = new Color(0.24f, 0.07f, 0.08f);
        private static readonly Color SLOT_LOCKED_COLOR = new Color(0.05f, 0.05f, 0.06f);

        private readonly GraphVisualStyle _style;
        private readonly GraphViewport _viewport;
        private readonly GraphNodeLayout _layout;
        private readonly GraphPointerState _state;
        private readonly Func<string, bool> _isRuneUnlocked;

        /// <summary>뷰포트·노드 배치·편집 상태와 룬 해금 판정으로 그래프 그리기 도구를 만든다.</summary>
        public GraphMeshPainter(GraphViewport viewport, GraphNodeLayout layout, GraphPointerState state, GraphVisualStyle style,
            Func<string, bool> isRuneUnlocked)
        {
            _style = style;
            _viewport = viewport;
            _layout = layout;
            _state = state;
            _isRuneUnlocked = isRuneUnlocked;
        }

        /// <summary>배경과 연결선을 기본 레이어 메시에 그린다.</summary>
        public void Paint(VertexHelper mesh, SpellGraph graph)
        {
            mesh.Clear();
            DrawBackground(mesh);
            if (graph == null) return;
            _layout.RebuildLookup();
            DrawEdges(mesh, graph);
        }

        /// <summary>지정 노드의 본체와 포트를 독립 레이어 메시에 그린다.</summary>
        public void PaintNode(VertexHelper mesh, GraphNode node)
        {
            mesh.Clear();
            DrawNode(mesh, node);
        }

        /// <summary>연결 미리보기와 영역 선택을 최상단 레이어 메시에 그린다.</summary>
        public void PaintOverlay(VertexHelper mesh)
        {
            mesh.Clear();
            DrawConnectionPreview(mesh);
            DrawBoxSelection(mesh);
        }

        /// <summary>배경과 화면 이동·배율에 맞춘 격자 점을 그린다.</summary>
        private void DrawBackground(VertexHelper mesh)
        {
            Rect bounds = _viewport.Bounds;
            RuneMesh.Rect(mesh, bounds, _style.GetColor(BACKGROUND_COLOR));
            float step = GRID_STEP * _viewport.Zoom;
            for (float x = bounds.xMin + _viewport.Pan.x % step; x < bounds.xMax; x += step)
            {
                for (float y = bounds.yMax + _viewport.Pan.y % step; y > bounds.yMin; y -= step)
                    RuneMesh.Rect(mesh, new Rect(x, y, GRID_DOT_SIZE, GRID_DOT_SIZE), _style.GetColor(GRID_COLOR));
            }
        }

        /// <summary>모든 연결선과 방향 화살표를 그린다. 지원 중단 연결은 회색이다.</summary>
        private void DrawEdges(VertexHelper mesh, SpellGraph graph)
        {
            foreach (GraphEdge edge in graph.Edges)
            {
                Vector2 from = _layout.LookupPortPosition(edge.FromNode, edge.FromPort);
                Vector2 to = _layout.LookupPortPosition(edge.ToNode, edge.ToPort);
                RuneMesh.Line(mesh, from, to, EDGE_WIDTH, _layout.IsDeprecatedEdge(edge) ? _style.GetColor(DEPRECATED_COLOR) : EdgeColor(edge));
                Vector2 direction = (to - from).normalized;
                float angle = Mathf.Atan2(direction.y, direction.x);
                RuneMesh.Polygon(mesh, Vector2.Lerp(from, to, EDGE_ARROW_POSITION), EDGE_ARROW_SIZE, _style.GetColor(EDGE_ARROW_COLOR), 3, angle);
            }
        }

        /// <summary>노드 테두리·본체·카테고리 강조 막대·아이콘과 포트를 그린다.</summary>
        private void DrawNode(VertexHelper mesh, GraphNode node)
        {
            float zoom = _viewport.Zoom;
            RuneDefinition rune = GameData.Runes.Get(node.RuneId);
            bool isRuneUnlocked = _isRuneUnlocked(node.RuneId);
            Color accent = isRuneUnlocked ? _style.GetCategoryColor(rune.Category) : LOCKED_NODE_COLOR;
            Rect rect = _layout.NodeRect(node, rune);
            bool isExecuted = _state.ExecutedUntil.TryGetValue(node.Id, out float until) && until > Time.unscaledTime;

            Rect border = new Rect(rect.x - NODE_BORDER, rect.y - NODE_BORDER, rect.width + NODE_BORDER * 2, rect.height + NODE_BORDER * 2);
            RuneMesh.Rect(mesh, border, NodeBorderColor(node, accent, isExecuted, isRuneUnlocked));
            Color fill = isRuneUnlocked
                ? isExecuted ? _style.GetColor(EXECUTED_FILL_COLOR) : _style.GetColor(NODE_FILL_COLOR)
                : LOCKED_NODE_FILL_COLOR;
            RuneMesh.Rect(mesh, rect, fill);
            RuneMesh.Rect(mesh, new Rect(rect.x, rect.yMax - NODE_ACCENT_HEIGHT * zoom, rect.width, NODE_ACCENT_HEIGHT * zoom), accent);
            Vector2 iconCenter = new Vector2(rect.x + ICON_OFFSET_X * zoom, rect.yMax - ICON_OFFSET_Y * zoom);
            RuneMesh.Polygon(mesh, iconCenter, ICON_RADIUS * zoom, accent, CategoryIconSides(rune.Category));
            DrawPorts(mesh, node, rune, rect);
            DrawSlots(mesh, node, rune, fill);
            if (_layout.TryGetGradeButtonRect(node, rune, out Rect gradeButton))
                RuneMesh.Rect(mesh, gradeButton, Color.Lerp(fill, accent, SLOT_CATEGORY_TINT));
        }

        /// <summary>행동 노드의 속성·모디 슬롯 셀을 카테고리 강조색과 잠금색으로 그린다.</summary>
        private void DrawSlots(VertexHelper mesh, GraphNode node, RuneDefinition rune, Color fill)
        {
            if (!GraphNodeLayout.HasSlots(rune)) return;
            foreach (string slotKey in GraphNodeLayout.SlotKeys)
            {
                if (!_layout.TryGetSlotRect(node, rune, slotKey, out Rect cell)) continue;
                Color color;
                if (GraphNodeLayout.IsSlotLocked(node, slotKey)) color = SLOT_LOCKED_COLOR;
                else
                {
                    bool isElement = SpellGrammar.TryParseSlotKey(slotKey, out bool elementSlot, out _) && elementSlot;
                    Color categoryColor = RuneMesh.CategoryColor(isElement ? SpellGrammar.CATEGORY_ELEMENT : SpellGrammar.CATEGORY_MODIFIER);
                    color = Color.Lerp(fill, categoryColor, SLOT_CATEGORY_TINT);
                }
                RuneMesh.Rect(mesh, cell, color);
            }
        }

        /// <summary>노드의 표시 포트를 종류별 모양으로 그리며 연결 드래그 중에는 연결 가능 여부 색으로 표시한다. 지원 중단 포트는 회색이다.</summary>
        private void DrawPorts(VertexHelper mesh, GraphNode node, RuneDefinition rune, Rect rect)
        {
            _state.ConnectablePorts.TryGetValue(node.Id, out bool[] connectable);
            for (int i = 0; i < rune.Ports.Count; i++)
            {
                PortDefinition port = rune.Ports[i];
                if (!_layout.IsPortShown(node, port)) continue;
                Color color = port.IsDeprecated ? _style.GetColor(DEPRECATED_COLOR) : PortBaseColor(port.Kind);
                if (_state.IsConnecting) color = connectable != null && connectable[i] ? _style.GetColor(CONNECTABLE_PORT_COLOR) : _style.GetColor(BLOCKED_PORT_COLOR);
                Vector2 point = _layout.PortPosition(rect, node, rune, port.Id);
                RuneMesh.Polygon(mesh, point, PORT_RADIUS * _viewport.Zoom, color, PortSides(port.Kind));
            }
        }

        /// <summary>연결 드래그 중이면 시작 포트에서 포인터까지 미리보기 선을 그린다.</summary>
        private void DrawConnectionPreview(VertexHelper mesh)
        {
            if (!_state.IsConnecting) return;
            RuneMesh.Line(mesh, _layout.LookupPortPosition(_state.SourceNode, _state.SourcePort), _state.Pointer, EDGE_WIDTH, _style.GetColor(PREVIEW_COLOR));
        }

        /// <summary>영역 선택 중이면 선택 사각형과 위·아래 경계선을 그린다.</summary>
        private void DrawBoxSelection(VertexHelper mesh)
        {
            if (!_state.IsBoxSelecting) return;
            Rect selection = _state.SelectionRect;
            RuneMesh.Rect(mesh, selection, _style.GetColor(SELECTION_FILL_COLOR));
            RuneMesh.Line(mesh, new Vector2(selection.xMin, selection.yMin), new Vector2(selection.xMax, selection.yMin), SELECTION_LINE_WIDTH, _style.GetColor(SELECTION_BORDER_COLOR));
            RuneMesh.Line(mesh, new Vector2(selection.xMin, selection.yMax), new Vector2(selection.xMax, selection.yMax), SELECTION_LINE_WIDTH, _style.GetColor(SELECTION_BORDER_COLOR));
        }

        /// <summary>미해금 표시를 실행 하이라이트와 선택 표시보다 우선해 노드 테두리 색을 반환한다.</summary>
        private Color NodeBorderColor(GraphNode node, Color accent, bool isExecuted, bool isRuneUnlocked)
        {
            if (!isRuneUnlocked) return LOCKED_NODE_COLOR;
            if (isExecuted) return _style.GetColor(EXECUTED_BORDER_COLOR);
            return _state.Selected.Contains(node.Id) ? accent : _style.GetColor(NODE_BORDER_COLOR);
        }

        /// <summary>효과 연결, 체인 연결, 실행 연결 순으로 연결선 색을 반환한다.</summary>
        private Color EdgeColor(GraphEdge edge)
        {
            if (edge.FromPort == SpellGrammar.MODIFIER_PORT) return _style.GetColor(MODIFIER_EDGE_COLOR);
            if (edge.ToPort == SpellGrammar.CHAIN_IN) return _style.GetColor(CHAIN_COLOR);
            return _style.GetColor(EXEC_EDGE_COLOR);
        }

        /// <summary>룬 카테고리별 노드 아이콘 다각형의 변 개수를 반환한다.</summary>
        private static int CategoryIconSides(string category)
        {
            return category switch
            {
                SpellGrammar.CATEGORY_BEHAVIOR => 4,
                SpellGrammar.CATEGORY_ELEMENT => 3,
                SpellGrammar.CATEGORY_SHAPE => 5,
                SpellGrammar.CATEGORY_MODIFIER => 6,
                _ => 20,
            };
        }

        /// <summary>포트 종류별 기본 색을 반환한다.</summary>
        private Color PortBaseColor(string kind)
        {
            return kind switch
            {
                SpellGrammar.MODIFIER_KIND => _style.GetColor(MODIFIER_PORT_COLOR),
                SpellGrammar.CHAIN_KIND => _style.GetColor(CHAIN_COLOR),
                SpellGrammar.ELEMENT_KIND => RuneMesh.CategoryColor(SpellGrammar.CATEGORY_ELEMENT),
                _ => _style.GetColor(EXEC_PORT_COLOR),
            };
        }

        /// <summary>포트 종류별 다각형 변 개수를 반환한다.</summary>
        private static int PortSides(string kind)
        {
            return kind switch
            {
                SpellGrammar.MODIFIER_KIND => 4,
                SpellGrammar.CHAIN_KIND => 6,
                SpellGrammar.ELEMENT_KIND => 3,
                _ => 16,
            };
        }
    }
}
