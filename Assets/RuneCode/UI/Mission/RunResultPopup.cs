using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 런 종료(패배) 결과 팝업 View다. 종료 제목과 스테이지·플레이 타임·처치 수를 표시하고 타이틀 복귀 버튼 클릭을 알린다.
    /// Esc로 닫히지 않으며 MissionPresenter가 연다.
    /// </summary>
    public sealed class RunResultPopup : UiPopup
    {
        [Header("내용")]
        [SerializeField] private TextMeshProUGUI _titleLabel;
        [SerializeField] private TextMeshProUGUI _stageLabel;
        [SerializeField] private TextMeshProUGUI _playTimeLabel;
        [SerializeField] private TextMeshProUGUI _killLabel;

        [Header("버튼")]
        [SerializeField] private Button _titleButton;

        private bool _isBound;

        /// <summary>타이틀로 돌아가기 버튼을 눌렀을 때 알린다.</summary>
        public event Action TitleClicked;

        /// <summary>타이틀 복귀 버튼 리스너를 처음 한 번만 연결하고 종료 결과 문구를 채운다. 전달받은 문구는 각 레이블에 그대로 표시한다.</summary>
        public void Configure(string title, string stage, string playTime, string kills)
        {
            if (!_isBound)
            {
                _titleButton.onClick.AddListener(() => TitleClicked?.Invoke());
                _isBound = true;
            }
            _titleLabel.text = title;
            _stageLabel.text = stage;
            _playTimeLabel.text = playTime;
            _killLabel.text = kills;
        }

        /// <summary>Esc 입력을 소비해 팝업이 닫히지 않게 한다. 팝업은 타이틀 복귀 버튼으로만 닫는다.</summary>
        public override bool HandleEscape() => true;
    }
}
