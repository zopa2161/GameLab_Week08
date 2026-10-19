using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 새로 개방된 온보딩 챕터나 이벤트 안내 하나를 목차 없이 보여 주는 팝업이다. 마지막 페이지의 다음 버튼과 한 페이지 항목의 버튼은 닫기로 동작한다.
    /// </summary>
    public sealed class OnboardingPopup : UiPopup
    {
        [Header("안내")]
        [SerializeField] private TextMeshProUGUI _titleLabel;
        [SerializeField] private EncyclopediaPageView _pageView;

        [Header("창 바깥 닫기")]
        [SerializeField] private Button _backgroundButton;

        void Awake()
        {
            _backgroundButton.onClick.AddListener(Close);
        }

        /// <summary>title과 pages의 첫 페이지를 표시하고 마지막 페이지의 다음 버튼을 닫기로 연결한다.</summary>
        public void Configure(string title, IReadOnlyList<EncyclopediaPage> pages)
        {
            _titleLabel.text = title;
            _pageView.Show(pages, Close);
        }
    }
}
