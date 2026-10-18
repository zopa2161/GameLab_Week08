using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 공용 확인·알림 팝업 View다. 확인 문구가 있으면 확인·취소 두 버튼, 없으면 닫기 버튼 하나만 표시한다.
    /// UIManager.Confirm·Alert로 연다.
    /// </summary>
    public sealed class ConfirmPopup : UiPopup
    {
        [Header("내용")]
        [SerializeField] private TextMeshProUGUI _title;
        [SerializeField] private TextMeshProUGUI _message;

        [Header("버튼")]
        [SerializeField] private Button _confirmButton;
        [SerializeField] private TextMeshProUGUI _confirmLabel;
        [SerializeField] private Button _cancelButton;

        private Action _onConfirm;
        private Image _confirmImage;
        private Sprite _defaultConfirmSprite;
        private float _defaultConfirmPixelsPerUnit;
        private bool _isBound;

        /// <summary>제목·내용·확인 동작과 버튼의 위험 강조 여부를 반영한다. confirmLabel이 null이면 닫기 버튼만 표시한다.</summary>
        public void Configure(string title, string message, string confirmLabel, Action onConfirm, bool isDestructive = false)
        {
            BindButtons();
            _title.text = title;
            _message.text = message;
            _onConfirm = onConfirm;
            if (isDestructive) UiFactory.ApplyDestructiveButtonStyle(_confirmButton);
            else
            {
                _confirmImage.sprite = _defaultConfirmSprite;
                _confirmImage.pixelsPerUnitMultiplier = _defaultConfirmPixelsPerUnit;
            }
            bool isConfirm = confirmLabel != null;
            _confirmButton.gameObject.SetActive(isConfirm);
            if (isConfirm) _confirmLabel.text = confirmLabel;
        }

        /// <summary>확인·취소 버튼 리스너를 처음 한 번만 등록한다.</summary>
        private void BindButtons()
        {
            if (_isBound) return;
            _confirmImage = (Image)_confirmButton.targetGraphic;
            _defaultConfirmSprite = _confirmImage.sprite;
            _defaultConfirmPixelsPerUnit = _confirmImage.pixelsPerUnitMultiplier;
            _confirmButton.onClick.AddListener(ConfirmAndClose);
            _cancelButton.onClick.AddListener(Close);
            _isBound = true;
        }

        /// <summary>팝업을 닫은 뒤 확인 동작을 실행한다. 확인 동작이 다른 팝업을 열어도 순서가 꼬이지 않게 닫기를 먼저 한다.</summary>
        private void ConfirmAndClose()
        {
            Action onConfirm = _onConfirm;
            _onConfirm = null;
            Close();
            onConfirm?.Invoke();
        }
    }
}
