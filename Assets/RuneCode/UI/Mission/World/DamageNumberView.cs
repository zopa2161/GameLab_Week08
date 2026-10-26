using UnityEngine;

using TMPro;

namespace RuneCode
{
    /// <summary>피해 숫자 하나를 월드 텍스트로 표시한다. 경과 틱에 따라 위로 떠오르며 흐려진다.</summary>
    public sealed class DamageNumberView : MonoBehaviour
    {
        private const float START_OFFSET = 12f;
        private const float RISE_PER_TICK = 0.6f;
        private const float FADE_TICKS = 42f;

        [Header("표시")]
        [SerializeField] private TextMeshPro _label;

        private double _shownAmount = double.NaN;

        /// <summary>피해 숫자의 위치·양·속성 색과 발생 후 경과 틱으로 맵 중앙(origin) 기준 월드 텍스트를 갱신한다. 문구는 양이 바뀔 때만 다시 만든다.</summary>
        public void Apply(DamageNumber number, int elapsedTicks, SimVector origin)
        {
            transform.localPosition = MissionWorldSpace.ToWorld(number.Position, origin)
                + new Vector2(0, (START_OFFSET + elapsedTicks * RISE_PER_TICK) / MissionWorldSpace.PIXELS_PER_UNIT);
            Color tint = RuneMesh.ElementColor(number.Element); tint.a = 1 - elapsedTicks / FADE_TICKS;
            _label.color = tint;
            if (number.Amount != _shownAmount) { _shownAmount = number.Amount; _label.text = number.Amount.ToString("0.0"); }
        }
    }
}
