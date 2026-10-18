using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 미션 일시정지 팝업 View다. 계속하기, 화면 흔들림·히트스톱 설정, 후퇴 클릭을 알린다.
    /// 닫기(×·Esc·계속하기)는 팝업 공통 Closed로 알려지고 MissionPresenter가 재개한다.
    /// </summary>
    public sealed class PausePopup : UiPopup
    {
        [Header("버튼")]
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _shakeButton;
        [SerializeField] private TextMeshProUGUI _shakeLabel;
        [SerializeField] private Button _hitStopButton;
        [SerializeField] private TextMeshProUGUI _hitStopLabel;
        [SerializeField] private Button _soundSettingsButton;
        [SerializeField] private Button _retreatButton;

        private bool _isBound;

        /// <summary>화면 흔들림 버튼을 눌렀을 때 알린다.</summary>
        public event Action ShakeClicked;

        /// <summary>히트스톱 버튼을 눌렀을 때 알린다.</summary>
        public event Action HitStopClicked;

        /// <summary>후퇴 버튼을 눌렀을 때 알린다.</summary>
        public event Action RetreatClicked;
        /// <summary>소리 설정 버튼을 눌렀을 때 알린다.</summary>
        public event Action SoundSettingsClicked;


        /// <summary>버튼 클릭을 처음 한 번만 연결한다. 계속하기는 팝업 닫기와 같다.</summary>
        public void Bind()
        {
            if (_isBound) return;
            _resumeButton.onClick.AddListener(Close);
            _shakeButton.onClick.AddListener(() => ShakeClicked?.Invoke());
            _hitStopButton.onClick.AddListener(() => HitStopClicked?.Invoke());
            _retreatButton.onClick.AddListener(() => RetreatClicked?.Invoke());
            _soundSettingsButton.onClick.AddListener(() => SoundSettingsClicked?.Invoke());
            _isBound = true;
        }

        /// <summary>화면 흔들림·히트스톱 설정의 켜짐 여부를 버튼 문구에 표시한다.</summary>
        public void SetFeedback(bool isScreenShake, bool isHitStop)
        {
            _shakeLabel.text = GameData.L("ui.shake") + " " + GameData.L(isScreenShake ? "ui.on" : "ui.off");
            _hitStopLabel.text = GameData.L("ui.hitstop") + " " + GameData.L(isHitStop ? "ui.on" : "ui.off");
        }

    }
}
