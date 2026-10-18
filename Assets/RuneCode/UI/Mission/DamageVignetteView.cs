using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 화면 둘레에 피해 비네트를 그리는 View다. 피해를 입으면 가장자리의 암막 밴드·붉은 밴드·밝은 붉은 선을 점등한 뒤 페이드아웃하고,
    /// 체력이 낮으면 약한 비네트를 계속 표시한다. 점등 시점과 강도 판단은 MissionPresenter가 한다.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DamageVignetteView : UnityEngine.UI.MaskableGraphic
    {
        private const float INTENSITY_EPSILON = 0.001f;
        private const float COLOR_EPSILON = 0.004f;
        private const float SHADOW_FALLOFF_POWER = 1.5f;
        private const int BAND_STRIPS = 10;

        [Header("암막 밴드")]
        [SerializeField] private Color _shadowColor = new Color(0.01f, 0.02f, 0.04f);
        [SerializeField, Min(0f)] private float _shadowThickness = 48f;
        [SerializeField, Range(0f, 1f)] private float _shadowAlpha = 0.55f;

        [Header("붉은 밴드")]
        [SerializeField] private Color _bandColor = new Color(0.75f, 0.06f, 0.12f);
        [SerializeField, Min(0f)] private float _bandThickness = 40f;
        [SerializeField, Range(0f, 1f)] private float _bandAlpha = 0.45f;

        [Header("밝은 선")]
        [SerializeField] private Color _lineColor = new Color(1f, 0.30f, 0.34f);
        [SerializeField, Min(0f)] private float _lineThickness = 3f;
        [SerializeField, Range(0f, 1f)] private float _lineAlpha = 0.95f;

        [Header("연출 시간")]
        [SerializeField, Min(0.01f)] private float _holdSeconds = 0.06f;
        [SerializeField, Min(0.01f)] private float _fadeSeconds = 0.45f;

        private float _flashIntensity;
        private float _holdUntil;
        private float _lowHealthLevel;
        private float _lowHealthTarget;

        /// <summary>피해 비네트를 strength(0~1) 강도로 점등한다. 이미 켜져 있으면 더 밝은 강도를 유지하고 홀드를 새로 시작한다.</summary>
        public void Flash(float strength)
        {
            _flashIntensity = Mathf.Max(_flashIntensity, Mathf.Clamp01(strength));
            _holdUntil = Time.unscaledTime + _holdSeconds;
            enabled = true;
            SetVerticesDirty();
        }

        /// <summary>저체력 지속 비네트의 목표 강도를 설정한다. 0을 넣으면 페이드아웃된다.</summary>
        public void SetLowHealth(float level)
        {
            _lowHealthTarget = Mathf.Clamp01(level);
            // 비활성 상태에서는 Update가 멈추므로 표시할 강도가 생기면 직접 켠다.
            if (_lowHealthTarget > 0f) enabled = true;
        }

        /// <summary>부모 화면 전체를 덮고 HUD 뒤에 그려지는 비네트 레이어를 만들어 반환한다. 포인터 입력은 통과시킨다.</summary>
        public static DamageVignetteView Create(RectTransform parent)
        {
            var layerObject = new GameObject("DamageVignette", typeof(RectTransform), typeof(CanvasRenderer), typeof(DamageVignetteView));
            var vignette = layerObject.GetComponent<DamageVignetteView>();
            RectTransform rect = vignette.rectTransform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsFirstSibling();
            vignette.raycastTarget = false;
            vignette.enabled = false;
            return vignette;
        }

        void Update()
        {
            // 홀드가 끝난 뒤 실제 경과 시간으로 페이드아웃한다. 히트스톱과 일시정지 중에도 사라지게 한다.
            if (Time.unscaledTime >= _holdUntil) _flashIntensity = Mathf.MoveTowards(_flashIntensity, 0f, Time.unscaledDeltaTime / _fadeSeconds);
            _lowHealthLevel = Mathf.MoveTowards(_lowHealthLevel, _lowHealthTarget, Time.unscaledDeltaTime / _fadeSeconds);
            if (_flashIntensity < INTENSITY_EPSILON && _lowHealthLevel < INTENSITY_EPSILON && _lowHealthTarget < INTENSITY_EPSILON)
            {
                _flashIntensity = 0f;
                _lowHealthLevel = 0f;
                enabled = false;
                return;
            }
            SetVerticesDirty();
        }

        /// <summary>현재 강도에 맞춰 가장자리부터 암막 밴드, 붉은 밴드, 밝은 선 순으로 테두리 메시를 만든다.</summary>
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            float intensity = Mathf.Max(_flashIntensity, _lowHealthLevel);
            if (intensity < INTENSITY_EPSILON) return;
            Rect area = rectTransform.rect;
            float shadowStep = _shadowThickness / BAND_STRIPS;
            float bandStep = _bandThickness / BAND_STRIPS;
            for (int strip = 0; strip < BAND_STRIPS; strip++)
            {
                float depth = (strip + 0.5f) / BAND_STRIPS;
                AddFrame(mesh, area, strip * shadowStep, (strip + 1) * shadowStep,
                    WithAlpha(_shadowColor, _shadowAlpha * intensity * Mathf.Pow(1f - depth, SHADOW_FALLOFF_POWER)));
                AddFrame(mesh, area, strip * bandStep, (strip + 1) * bandStep,
                    WithAlpha(_bandColor, _bandAlpha * intensity * (1f - depth)));
            }
            AddFrame(mesh, area, 0f, _lineThickness, WithAlpha(_lineColor, _lineAlpha * intensity));
        }

        /// <summary>지정 바깥·안쪽 여백 사이의 테두리 링을 겹침 없는 상·하·좌·우 네 사각형으로 추가한다.</summary>
        private static void AddFrame(UnityEngine.UI.VertexHelper mesh, Rect area, float insetOuter, float insetInner, Color color)
        {
            if (color.a < COLOR_EPSILON || insetInner <= insetOuter) return;
            float thickness = insetInner - insetOuter;
            float innerHeight = area.height - insetInner * 2f;
            RuneMesh.Rect(mesh, new Rect(area.xMin, area.yMax - insetInner, area.width, thickness), color);
            RuneMesh.Rect(mesh, new Rect(area.xMin, area.yMin + insetOuter, area.width, thickness), color);
            RuneMesh.Rect(mesh, new Rect(area.xMin + insetOuter, area.yMin + insetInner, thickness, innerHeight), color);
            RuneMesh.Rect(mesh, new Rect(area.xMax - insetInner, area.yMin + insetInner, thickness, innerHeight), color);
        }

        /// <summary>색의 알파만 바꾼 새 색을 반환한다.</summary>
        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }
    }
}
