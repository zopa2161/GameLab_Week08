using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>목록 Prefab의 버튼 한 줄이다. 런타임에 문구, 높이와 클릭 동작을 설정한다.</summary>
    public sealed class UiRow : MonoBehaviour
    {
        [Header("구성 요소")]
        [SerializeField] private Button _button;
        [SerializeField] private Image _background;
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private UiStateVisual _visual;

        public Button Button => _button;
        public TextMeshProUGUI Label => _label;

        /// <summary>문구·글자 크기·행 높이를 적용하고 기존 클릭 리스너를 공통 클릭음과 action으로 교체한다.</summary>
        public void Configure(string label, float height, Action action, float fontSize = 12)
        {
            _label.richText = true;
            _label.text = label;
            bool canClick = action != null;
            _label.fontSize = fontSize;
            _button.interactable = canClick;
            _visual?.Show(canClick ? 0 : 1);
            RectTransform rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            UiFactory.Layout(gameObject, height);
            _button.onClick.RemoveAllListeners();
            UiFactory.BindClickSound(_button);
            if (action != null) _button.onClick.AddListener(() => action());
        }
    }
}
