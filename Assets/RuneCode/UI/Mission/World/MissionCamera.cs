using System;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 미션 전용 정사영 카메라다. HUD 캔버스(Expand 배율)와 같은 배율로 시뮬레이션 1px을 논리 UI 1px에 맞추고,
    /// 플레이어를 화면 중앙에 두고 따라가며 맵 경계에 닿으면 축별로 멈춘다. 세로는 맵 끝이 상·하단 HUD 바 안쪽에 오도록 멈춘다.
    /// 화면 흔들림과 화면 좌표 → 시뮬레이션 좌표 변환도 맡는다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class MissionCamera : MonoBehaviour
    {
        private const float CAMERA_DEPTH = -10f;
        private const float SHAKE_PIXELS = 3f;
        private const float SHAKE_FREQUENCY_X = 170f;
        private const float SHAKE_FREQUENCY_Y = 190f;

        [Header("화면")]
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1280, 720);
        [SerializeField] private float _topHudHeight = 74f;
        [SerializeField] private float _bottomHudHeight = 70f;

        private Camera _camera;
        private Func<RuneSimulation> _simulation;
        private float _shakeUntil;

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        /// <summary>따라갈 시뮬레이션 조회 함수를 연결한다.</summary>
        public void Initialize(Func<RuneSimulation> simulation)
        {
            _simulation = simulation;
        }

        /// <summary>월드 위치에서 화면 상단 밖까지 상승 연출을 그리는 데 필요한 높이를 픽셀 단위로 반환한다.</summary>
        public float GetAscensionHeight(Vector3 worldPosition)
        {
            float screenTop = _camera.transform.position.y + _camera.orthographicSize;
            return Mathf.Max(0f, screenTop - worldPosition.y) * MissionWorldSpace.PIXELS_PER_UNIT + 128f;
        }

        /// <summary>지정 시간(초) 동안 카메라를 흔든다. 이미 흔들리는 중이면 더 긴 쪽을 유지한다.</summary>
        public void Shake(float seconds)
        {
            _shakeUntil = Mathf.Max(_shakeUntil, Time.unscaledTime + seconds);
        }

        void LateUpdate()
        {
            RuneSimulation sim = _simulation?.Invoke();
            if (sim == null) return;
            // 캔버스 Expand 배율과 같게 해 논리 화면 1280×720이 항상 보이고 남는 쪽으로 더 넓게 보인다.
            float uiScale = Mathf.Min(_camera.pixelWidth / _referenceResolution.x, _camera.pixelHeight / _referenceResolution.y);
            _camera.orthographicSize = _camera.pixelHeight / uiScale * 0.5f / MissionWorldSpace.PIXELS_PER_UNIT;
            float halfWidth = _camera.orthographicSize * _camera.aspect;
            Vector2 target = MissionWorldSpace.ToWorld(sim.Player.Position, sim.MapCenter);
            float x = ClampAxis(target.x, MissionWorldSpace.ToWorldLength(sim.Map.Width * 0.5), halfWidth, halfWidth);
            float halfPage = _referenceResolution.y * 0.5f;
            float y = ClampAxis(target.y, MissionWorldSpace.ToWorldLength(sim.Map.Height * 0.5),
                MissionWorldSpace.ToWorldLength(halfPage - _bottomHudHeight), MissionWorldSpace.ToWorldLength(halfPage - _topHudHeight));
            if (_shakeUntil > Time.unscaledTime)
            {
                float time = Time.unscaledTime;
                x += Mathf.Sin(time * SHAKE_FREQUENCY_X) * SHAKE_PIXELS / MissionWorldSpace.PIXELS_PER_UNIT;
                y += Mathf.Cos(time * SHAKE_FREQUENCY_Y) * SHAKE_PIXELS / MissionWorldSpace.PIXELS_PER_UNIT;
            }
            transform.position = new Vector3(x, y, CAMERA_DEPTH);
        }

        /// <summary>
        /// 화면 좌표를 현재 카메라 기준 시뮬레이션 좌표로 바꾼다. 포인터가 게임 화면 안에 있으면 true를 반환한다.
        /// 시뮬레이션이 없으면 false를 반환한다.
        /// </summary>
        public bool TryGetSimPoint(Vector2 screen, out SimVector point)
        {
            point = SimVector.Zero;
            RuneSimulation sim = _simulation?.Invoke();
            if (sim == null) return false;
            Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -CAMERA_DEPTH));
            point = MissionWorldSpace.ToSim(world, sim.MapCenter);
            return screen.x >= 0 && screen.y >= 0 && screen.x <= Screen.width && screen.y <= Screen.height;
        }

        /// <summary>
        /// 맵 중앙이 0인 축에서 카메라 중심을 제한한다. 음수 쪽 맵 끝은 중심에서 lowerView, 양수 쪽 맵 끝은 upperView 거리까지만 다가온다.
        /// 맵이 그 범위보다 작으면 두 경계의 가운데에 둔다.
        /// </summary>
        private static float ClampAxis(float target, float mapHalf, float lowerView, float upperView)
        {
            float min = -mapHalf + lowerView;
            float max = mapHalf - upperView;
            return min > max ? (min + max) * 0.5f : Mathf.Clamp(target, min, max);
        }
    }
}
