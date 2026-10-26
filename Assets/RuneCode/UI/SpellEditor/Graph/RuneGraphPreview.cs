using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>보관된 빌드를 편집기와 같은 메시·라벨로 표시하고 화면 이동·확대만 허용한다.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RuneGraphPreview : MaskableGraphic, IBeginDragHandler, IDragHandler, IScrollHandler
    {
        private const float MIN_PREVIEW_ZOOM = 0.02f;

        [Header("그래프 표시")]
        [SerializeField] private GraphVisualStyle _style;

        private SpellGraph _graph;
        private GraphViewport _viewport;
        private GraphNodeLayout _layout;
        private GraphMeshPainter _painter;
        private GraphNodeLabels _labels;
        private Vector2 _dragStart;
        private Vector2 _panStart;

        /// <summary>독립된 그래프·라이브러리·컴파일 결과와 글꼴을 연결하고 전체 빌드를 표시한다.</summary>
        public void SetContent(SpellGraph graph, IReadOnlyList<SpellGraph> library, CompiledSpell spell, TMP_FontAsset font)
        {
            _graph = graph;
            if (_viewport == null)
            {
                _viewport = new GraphViewport(rectTransform);
                // 저장된 그래프의 두 번째 속성 슬롯도 보이도록 확장 보너스 1로 계산한다.
                _layout = new GraphNodeLayout(_viewport, () => _graph, () => 1);
                var state = new GraphPointerState();
                _painter = new GraphMeshPainter(_viewport, _layout, state, _style, IsRuneVisible);
                _labels = new GraphNodeLabels(rectTransform, font, _viewport, _layout, _painter, _style, state);
            }
            _labels.Refresh(graph, library, spell == null ? null : new[] { spell });
            raycastTarget = true;
            ResetView();
        }

        /// <summary>모든 노드와 연결선이 여백 안에 들어오도록 배율·위치를 초기화한다.</summary>
        public void ResetView()
        {
            if (_graph == null) return;
            if (_layout.TryGetGraphBounds(out Vector2 minimum, out Vector2 maximum))
            {
                _viewport.Fit(minimum, maximum, MIN_PREVIEW_ZOOM);
                _viewport.CenterOn((minimum + maximum) / 2);
            }
            RefreshView();
        }

        /// <summary>결과에 보관한 룬은 현재 세이브의 초기화·해금 상태와 관계없이 표시한다.</summary>
        private static bool IsRuneVisible(string runeId) => true;

        /// <summary>포인터 드래그 시작 좌표와 현재 화면 이동량을 기억한다.</summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_viewport == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out _dragStart);
            _panStart = _viewport.Pan;
        }

        /// <summary>포인터 이동량만큼 보기 위치만 옮기며 원본 노드 좌표는 변경하지 않는다.</summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (_viewport == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 point);
            _viewport.SetPan(_panStart + point - _dragStart);
            RefreshView();
        }

        /// <summary>포인터 위치를 중심으로 휠 방향에 따라 빌드 보기를 확대·축소한다.</summary>
        public void OnScroll(PointerEventData eventData)
        {
            if (_viewport == null || eventData.scrollDelta.y == 0) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.enterEventCamera, out Vector2 point);
            _viewport.ZoomAt(point, eventData.scrollDelta.y > 0, MIN_PREVIEW_ZOOM);
            RefreshView();
        }

        /// <summary>현재 화면 위치와 배율에 맞춰 라벨·노드·연결선 메시를 갱신한다.</summary>
        private void RefreshView()
        {
            _labels.Layout(_graph);
            _labels.RefreshMeshes();
            SetVerticesDirty();
        }

        /// <summary>보기 영역 크기가 달라지면 전체 빌드가 보이도록 다시 맞춘다.</summary>
        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (_viewport != null) ResetView();
        }

        /// <summary>편집기의 공통 그리기 도구로 배경과 연결선을 채운다.</summary>
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            if (_painter == null) mesh.Clear();
            else _painter.Paint(mesh, _graph);
        }
    }
}
