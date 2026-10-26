using UnityEngine;

using UnityEngine.EventSystems;

namespace RuneCode
{
    public sealed class UpgradeTreeCanvasInput : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        private const float MIN_ZOOM = 0.30f;
        private const float MAX_ZOOM = 1.65f;
        private const float ZOOM_STEP = 1.12f;

        [Header("트리 캔버스")]
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private RectTransform _content;

        private bool _isPanning;
        private Vector2 _lastPointerPosition;

        /// <summary>우클릭 또는 휠 클릭 드래그가 시작되면 마지막 포인터 위치를 저장해 이동을 준비한다.</summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            _isPanning = eventData.button == PointerEventData.InputButton.Right
                || eventData.button == PointerEventData.InputButton.Middle;
            _lastPointerPosition = eventData.position;
        }

        /// <summary>우클릭 또는 휠 클릭 드래그 거리를 뷰포트 좌표로 변환해 트리 전체를 이동한다.</summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (!_isPanning || !TryGetViewportPosition(eventData.position, eventData.pressEventCamera, out Vector2 currentPosition) ||
                !TryGetViewportPosition(_lastPointerPosition, eventData.pressEventCamera, out Vector2 previousPosition)) return;
            _content.anchoredPosition += currentPosition - previousPosition;
            _lastPointerPosition = eventData.position;
        }

        /// <summary>드래그가 끝나면 이동 상태를 해제한다.</summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            _isPanning = false;
        }

        /// <summary>포인터 아래의 트리 지점을 고정한 채 마우스 휠로 화면 배율을 바꾼다.</summary>
        public void OnScroll(PointerEventData eventData)
        {
            float wheelSteps = Mathf.Clamp(eventData.scrollDelta.y, -3, 3);
            if (Mathf.Approximately(wheelSteps, 0) ||
                !TryGetViewportPosition(eventData.position, eventData.pressEventCamera, out Vector2 pointerPosition)) return;
            float currentZoom = _content.localScale.x;
            float nextZoom = Mathf.Clamp(currentZoom * Mathf.Pow(ZOOM_STEP, wheelSteps), MIN_ZOOM, MAX_ZOOM);
            if (Mathf.Approximately(currentZoom, nextZoom)) return;
            Vector2 contentPoint = (pointerPosition - _content.anchoredPosition) / currentZoom;
            _content.localScale = Vector3.one * nextZoom;
            _content.anchoredPosition = pointerPosition - contentPoint * nextZoom;
        }

        /// <summary>Screen Space Overlay 포인터 좌표를 뷰포트 로컬 좌표로 변환한다.</summary>
        private bool TryGetViewportPosition(Vector2 screenPosition, Camera eventCamera, out Vector2 localPosition)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, screenPosition, eventCamera, out localPosition);
        }
    }
}
