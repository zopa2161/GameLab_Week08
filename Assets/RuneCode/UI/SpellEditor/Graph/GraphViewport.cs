using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 그래프 편집 화면의 이동·확대 상태와 좌표 변환이다. 그래프 좌표(노드 X·Y, 아래로 증가)와
    /// 캔버스 로컬 좌표(좌상단 기준, 위로 증가) 사이를 변환한다.
    /// </summary>
    internal sealed class GraphViewport
    {
        private const float MIN_ZOOM = 0.45f;
        private const float MAX_ZOOM = 1.5f;
        private const float MAX_FIT_ZOOM = 1f;
        private const float ZOOM_IN_FACTOR = 1.1f;
        private const float ZOOM_OUT_FACTOR = 0.9f;
        private const float FIT_MARGIN = 24f;

        private readonly RectTransform _rect;

        private Vector2 _pan = new Vector2(30, -35);
        private float _zoom = 1;

        /// <summary>현재 확대 배율이다.</summary>
        public float Zoom => _zoom;

        /// <summary>현재 화면 이동량(캔버스 좌상단 기준)이다.</summary>
        public Vector2 Pan => _pan;

        /// <summary>캔버스 로컬 사각형이다.</summary>
        public Rect Bounds => _rect.rect;

        /// <summary>캔버스 로컬 좌표의 좌상단이다.</summary>
        public Vector2 TopLeft => new Vector2(_rect.rect.xMin, _rect.rect.yMax);

        /// <summary>그래프 캔버스의 RectTransform으로 뷰포트를 만든다.</summary>
        public GraphViewport(RectTransform rect)
        {
            _rect = rect;
        }

        /// <summary>그래프 좌표에 확대와 화면 이동을 적용한 캔버스 로컬 좌표를 반환한다.</summary>
        public Vector2 ToLocal(Vector2 graphPoint)
        {
            return TopLeft + _pan + new Vector2(graphPoint.x, -graphPoint.y) * _zoom;
        }

        /// <summary>캔버스 로컬 좌표에서 확대와 화면 이동을 제거한 그래프 좌표를 반환한다. ToLocal의 역변환이다.</summary>
        public Vector2 GraphPoint(Vector2 local)
        {
            Vector2 position = (local - TopLeft - _pan) / _zoom;
            return new Vector2(position.x, -position.y);
        }

        /// <summary>화면 이동량을 지정한다.</summary>
        public void SetPan(Vector2 pan) => _pan = pan;

        /// <summary>캔버스 로컬 지점을 중심으로 한 단계 확대·축소한다. 그 지점 아래의 그래프 위치는 화면에서 그대로 유지된다.</summary>
        public void ZoomAt(Vector2 local, bool isZoomIn, float minimumZoom = MIN_ZOOM)
        {
            Vector2 point = local - TopLeft;
            float nextZoom = Mathf.Clamp(_zoom * (isZoomIn ? ZOOM_IN_FACTOR : ZOOM_OUT_FACTOR), minimumZoom, MAX_ZOOM);
            _pan = point - (point - _pan) * (nextZoom / _zoom);
            _zoom = nextZoom;
        }

        /// <summary>그래프 좌표 범위(최소·최대)가 여백을 두고 화면에 모두 보이도록 배율과 화면 이동을 맞춘다.</summary>
        public void Fit(Vector2 minimum, Vector2 maximum, float minimumZoom = MIN_ZOOM)
        {
            Rect bounds = _rect.rect;
            float zoomX = (bounds.width - FIT_MARGIN * 2) / Mathf.Max(1, maximum.x - minimum.x);
            float zoomY = (bounds.height - FIT_MARGIN * 2) / Mathf.Max(1, maximum.y - minimum.y);
            _zoom = Mathf.Clamp(Mathf.Min(zoomX, zoomY), minimumZoom, MAX_FIT_ZOOM);
            _pan = new Vector2(FIT_MARGIN - minimum.x * _zoom, -FIT_MARGIN + minimum.y * _zoom);
        }

        /// <summary>그래프 좌표 지점(가로 중심, 세로 위쪽)이 화면 가운데에 오도록 화면 이동을 맞춘다.</summary>
        public void CenterOn(Vector2 graphPoint)
        {
            Rect bounds = _rect.rect;
            _pan = new Vector2(bounds.width / 2 - graphPoint.x * _zoom, -bounds.height / 2 + graphPoint.y * _zoom);
        }
    }
}
