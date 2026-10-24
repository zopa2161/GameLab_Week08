using System;

using UnityEngine;

using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 미션 일시정지 팝업 View다. 계속하기, 타이틀 복귀, 기본 설정 클릭을 알린다.
    /// 닫기(×·Esc·계속하기)는 팝업 공통 Closed로 알려지고 MissionPresenter가 재개한다.
    /// </summary>
    public sealed class PausePopup : UiPopup
    {
        [Header("버튼")]
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _soundSettingsButton;
        [SerializeField] private Button _titleButton;

        private bool _isBound;

        /// <summary>기본 설정 버튼을 눌렀을 때 알린다.</summary>
        public event Action SoundSettingsClicked;
        /// <summary>타이틀로 버튼을 눌렀을 때 알린다.</summary>
        public event Action TitleClicked;

        /// <summary>버튼 클릭을 처음 한 번만 연결한다. 계속하기는 팝업 닫기와 같다.</summary>
        public void Bind()
        {
            if (_isBound) return;
            _resumeButton.onClick.AddListener(Close);
            _titleButton.onClick.AddListener(() => TitleClicked?.Invoke());
            _soundSettingsButton.onClick.AddListener(() => SoundSettingsClicked?.Invoke());
            _isBound = true;
        }

    }
}
