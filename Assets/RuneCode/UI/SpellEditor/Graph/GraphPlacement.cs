using System;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 새 행동 노드의 배치 위치 제안과 배치를 맡는다. 속성·모디는 노드 슬롯에 장착하므로 자동 연결 대상이 없다.
    /// </summary>
    internal sealed class GraphPlacement
    {
        private const float NODE_PLACEMENT_GAP = 20f;
        private const float PLACEMENT_MARGIN = 12f;
        private const float PLACEMENT_BOTTOM_MARGIN = 90f;

        private readonly Func<ISpellEditor> _editor;
        private readonly GraphViewport _viewport;

        /// <summary>편집 세션 조회 함수와 뷰포트로 배치 도우미를 만든다.</summary>
        public GraphPlacement(Func<ISpellEditor> editor, GraphViewport viewport)
        {
            _editor = editor;
            _viewport = viewport;
        }

        /// <summary>현재 보이는 영역 안에서 새 노드를 놓을 그래프 좌표를 반환한다. 마지막 노드 오른쪽을 우선하고 없으면 화면 중앙이다.</summary>
        public Vector2 Suggest(string runeId)
        {
            ISpellEditor editor = _editor();
            Vector2 position = _viewport.GraphPoint(_viewport.Bounds.center);
            int count = editor.Graph.Nodes.Count;
            if (count > 0)
            {
                GraphNode last = editor.Graph.Nodes[count - 1];
                position = new Vector2(last.X + GraphNodeLayout.NODE_WIDTH + NODE_PLACEMENT_GAP, last.Y);
            }

            // 제안 위치가 화면 밖이면 보이는 영역 안쪽으로 당긴다.
            Rect bounds = _viewport.Bounds;
            float zoom = _viewport.Zoom;
            Vector2 local = _viewport.ToLocal(position);
            local.x = Mathf.Clamp(local.x, bounds.xMin + PLACEMENT_MARGIN, bounds.xMax - (GraphNodeLayout.NODE_WIDTH + PLACEMENT_MARGIN) * zoom);
            local.y = Mathf.Clamp(local.y, bounds.yMin + PLACEMENT_BOTTOM_MARGIN * zoom, bounds.yMax - PLACEMENT_MARGIN);
            return _viewport.GraphPoint(local);
        }

        /// <summary>그래프 좌표에 룬을 지정 등급으로 배치한다. 배치 가능 여부는 편집 세션이 판정한다.</summary>
        public void Place(string runeId, Vector2 position, string grade = null)
        {
            _editor().AddRune(runeId, position.x, position.y, grade);
        }
    }
}
