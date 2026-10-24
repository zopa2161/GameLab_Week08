using UnityEngine;

using TMPro;

namespace RuneCode
{
    /// <summary>라운드 대기 포털을 원 둘레의 짧은 막대 고리와 가운데 마름모 코어로 표시한다. 고리는 천천히 회전하고 코어는 맥동한다.</summary>
    public sealed class PortalView : MonoBehaviour
    {
        private const int SEGMENT_COUNT = 12;
        private const float SEGMENT_LENGTH_RATIO = 0.45f;
        private const float MIN_SEGMENT_PIXELS = 6f;
        private const float SEGMENT_THICKNESS_PIXELS = 4f;
        private const float CORE_SIZE_RATIO = 0.55f;
        private const float CORE_PULSE_DEPTH = 0.18f;
        private const float CORE_PULSE_SPEED = 2.6f;
        private const float CORE_ALPHA_MIN = 0.5f;
        private const float RING_SPIN_DEGREES_PER_SECOND = 25f;
        private const float INACTIVE_SPEED_RATIO = 0.4f;
        private const float INACTIVE_ALPHA = 0.3f;
        private const string LABEL_TEXT = "다음 라운드로";
        private const float LABEL_FONT_SIZE = 7f;
        private const float LABEL_GAP_PIXELS = 14f;

        private readonly SpriteRenderer[] _segments = new SpriteRenderer[SEGMENT_COUNT];
        private Transform _ringPivot;
        private SpriteRenderer _core;
        private TextMeshPro _label;
        private Color _color;

        /// <summary>맵용 정사각 스프라이트로 고리 막대들과 마름모 코어를, TMP 기본 글꼴로 고리 위 안내 문구를 만든다. 색과 정렬 순서는 월드 뷰에서 받는다.</summary>
        public void Initialize(Transform parent, Sprite squareSprite, Material spriteMaterial, Color color, int order)
        {
            transform.SetParent(parent, false);
            _color = color;
            _ringPivot = new GameObject("RingPivot").transform;
            _ringPivot.SetParent(transform, false);
            for (int index = 0; index < SEGMENT_COUNT; index++)
                _segments[index] = CreateSprite("Segment", _ringPivot, squareSprite, spriteMaterial, order);
            _core = CreateSprite("Core", transform, squareSprite, spriteMaterial, order);
            _core.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            _label = new GameObject("Label", typeof(TextMeshPro)).GetComponent<TextMeshPro>();
            _label.transform.SetParent(transform, false);
            _label.text = LABEL_TEXT;
            _label.fontSize = LABEL_FONT_SIZE;
            _label.rectTransform.pivot = new Vector2(0.5f, 0f);
            _label.alignment = TextAlignmentOptions.Bottom;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.sortingOrder = order;
        }

        /// <summary>포털 중심과 반경(맵 픽셀)에 맞춰 고리·안내 문구 위치와 크기를 갱신하고 피벗 회전과 코어 맥동을 unscaledTime 기준으로 진행한다. 진입 불가 상태면 고리와 문구의 알파를 낮추고 회전·맥동을 느리게 한다.</summary>
        public void Apply(SimVector position, double radius, bool isActive, SimVector origin, float unscaledTime)
        {
            transform.localPosition = MissionWorldSpace.ToWorld(position, origin);
            float radiusPixels = Mathf.Max(1f, (float)radius);
            float speedRatio = isActive ? 1f : INACTIVE_SPEED_RATIO;
            float alpha = isActive ? 1f : INACTIVE_ALPHA;
            _ringPivot.localRotation = Quaternion.Euler(0f, 0f, unscaledTime * RING_SPIN_DEGREES_PER_SECOND * speedRatio);
            float segmentLength = Mathf.Max(MIN_SEGMENT_PIXELS, 2f * Mathf.PI * radiusPixels / SEGMENT_COUNT * SEGMENT_LENGTH_RATIO);
            Color ringColor = _color;
            ringColor.a *= alpha;
            for (int index = 0; index < SEGMENT_COUNT; index++)
            {
                float angle = index * Mathf.PI * 2f / SEGMENT_COUNT;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                MissionWorldSpace.Place(_segments[index], direction * radiusPixels, new Vector2(segmentLength, SEGMENT_THICKNESS_PIXELS), ringColor);
                // 막대를 고리 접선 방향으로 눕힌다. 회전은 부모 피벗의 localRotation이 더한다.
                _segments[index].transform.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
            }
            float pulse = 0.5f + 0.5f * Mathf.Sin(unscaledTime * CORE_PULSE_SPEED * speedRatio);
            float corePixels = radiusPixels * CORE_SIZE_RATIO * (1f - CORE_PULSE_DEPTH + CORE_PULSE_DEPTH * pulse);
            Color coreColor = _color;
            coreColor.a *= Mathf.Lerp(CORE_ALPHA_MIN, 1f, pulse) * alpha;
            MissionWorldSpace.Place(_core, Vector2.zero, corePixels, coreColor);
            // 문구는 회전하지 않도록 피벗이 아닌 포털 루트에 두고 고리 바로 위에 아래 정렬로 붙인다.
            _label.transform.localPosition = new Vector2(0f, MissionWorldSpace.ToWorldLength(radiusPixels + LABEL_GAP_PIXELS));
            _label.color = ringColor;
        }

        /// <summary>정사각 스프라이트와 월드 재질을 연결한 자식 스프라이트 렌더러를 지정 부모 아래 만든다.</summary>
        private SpriteRenderer CreateSprite(string label, Transform parent, Sprite squareSprite, Material spriteMaterial, int order)
        {
            var sprite = new GameObject(label, typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            sprite.transform.SetParent(parent, false);
            sprite.sprite = squareSprite;
            sprite.sharedMaterial = spriteMaterial;
            sprite.sortingOrder = order;
            return sprite;
        }
    }
}
