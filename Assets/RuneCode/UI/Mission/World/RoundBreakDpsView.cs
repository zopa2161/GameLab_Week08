using System;

using UnityEngine;

using TMPro;

namespace RuneCode
{
    /// <summary>라운드 대기 허수아비 아래에 6기 합산 최근 5초 DPS를 표시한다. 반올림한 정수 값이 바뀔 때만 문구를 다시 쓴다.</summary>
    public sealed class RoundBreakDpsView : MonoBehaviour
    {
        private const string LABEL_PREFIX = "DPS ";
        private const float LABEL_FONT_SIZE = 6f;
        private const float LABEL_WIDTH_UNITS = 4f;
        private const float LABEL_HEIGHT_UNITS = 1f;

        private TextMeshPro _label;
        private long _shownDps = long.MinValue;

        /// <summary>월드 루트 아래에 TMP 문구를 만들고 정렬 순서를 지정한다. 문구는 위쪽 기준 가운데 정렬이며 줄바꿈하지 않는다.</summary>
        public void Initialize(Transform parent, int order)
        {
            transform.SetParent(parent, false);
            _label = new GameObject("Label", typeof(TextMeshPro)).GetComponent<TextMeshPro>();
            _label.transform.SetParent(transform, false);
            _label.fontSize = LABEL_FONT_SIZE;
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Top;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.rectTransform.sizeDelta = new Vector2(LABEL_WIDTH_UNITS, LABEL_HEIGHT_UNITS);
            _label.rectTransform.pivot = new Vector2(0.5f, 1f);
            _label.color = Color.white;
            _label.sortingOrder = order;
        }

        /// <summary>시뮬레이션 좌표 위치로 표시를 옮기고 DPS를 반올림한 정수가 이전과 다를 때만 문구를 갱신한다.</summary>
        public void Apply(double dps, SimVector position, SimVector origin)
        {
            transform.localPosition = MissionWorldSpace.ToWorld(position, origin);
            long rounded = (long)Math.Round(dps, MidpointRounding.AwayFromZero);
            if (rounded == _shownDps) return;
            _shownDps = rounded;
            _label.text = LABEL_PREFIX + rounded.ToString("N0");
        }
    }
}
