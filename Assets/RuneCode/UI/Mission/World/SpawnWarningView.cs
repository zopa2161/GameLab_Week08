using UnityEngine;

namespace RuneCode
{
    /// <summary>적 스폰 예고를 X자 막대 두 개로 표시한다. 진행도에 따라 밝아지고 점점 빠르게 깜빡인다.</summary>
    public sealed class SpawnWarningView : MonoBehaviour
    {
        private const float MIN_ARM_PIXELS = 8f;
        private const float THICKNESS_PIXELS = 3f;
        private const float MIN_ALPHA = 0.35f;
        private const float BLINK_DEPTH = 0.35f;
        private const float BLINK_SPEED_START = 6f;
        private const float BLINK_SPEED_END = 20f;

        private readonly SpriteRenderer[] _bars = new SpriteRenderer[2];
        private Color _color;
        private Color _eliteColor;

        /// <summary>맵용 정사각 스프라이트 두 장을 ±45도로 회전해 X 모양을 만든다. 일반·엘리트 색과 정렬 순서는 월드 뷰에서 받는다.</summary>
        public void Initialize(Transform parent, Sprite squareSprite, Material spriteMaterial, Color color, Color eliteColor, int order)
        {
            transform.SetParent(parent, false);
            _color = color;
            _eliteColor = eliteColor;
            _bars[0] = CreateBar(squareSprite, spriteMaterial, 45f, order);
            _bars[1] = CreateBar(squareSprite, spriteMaterial, -45f, order);
        }

        /// <summary>예고 위치와 반경(맵 픽셀), 진행도(0~1), 엘리트 여부로 X의 위치와 크기, 색, 알파를 갱신한다. 깜빡임 위상은 unscaledTime 기준이다.</summary>
        public void Apply(SpawnWarning warning, SimVector origin, float unscaledTime)
        {
            transform.localPosition = MissionWorldSpace.ToWorld(warning.Position, origin);
            float armPixels = Mathf.Max(MIN_ARM_PIXELS, (float)warning.Radius);
            float progress = (float)warning.Progress;
            float speed = Mathf.Lerp(BLINK_SPEED_START, BLINK_SPEED_END, progress);
            float blink = 1f - BLINK_DEPTH * (0.5f + 0.5f * Mathf.Sin(unscaledTime * speed));
            Color tint = warning.IsElite ? _eliteColor : _color;
            tint.a *= Mathf.Lerp(MIN_ALPHA, 1f, progress) * blink;
            Vector2 dimensions = new Vector2(armPixels * 2f, THICKNESS_PIXELS);
            foreach (SpriteRenderer bar in _bars) MissionWorldSpace.Place(bar, Vector2.zero, dimensions, tint);
        }

        /// <summary>X 중심에 정사각 스프라이트를 막대 크기로 늘려 지정 각도로 회전시킨 하나의 막대를 만든다.</summary>
        private SpriteRenderer CreateBar(Sprite squareSprite, Material spriteMaterial, float angleDegrees, int order)
        {
            var bar = new GameObject("Bar", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            bar.transform.SetParent(transform, false);
            bar.sprite = squareSprite;
            bar.sharedMaterial = spriteMaterial;
            bar.sortingOrder = order;
            bar.transform.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
            return bar;
        }
    }
}
