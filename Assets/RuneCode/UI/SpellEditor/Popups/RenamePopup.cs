using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>마법 이름 변경 팝업 View다. 현재 이름을 입력 칸에 채우고 저장 클릭 시 입력값을 알린다.</summary>
    public sealed class RenamePopup : UiPopup
    {
        [Header("이름")]
        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private TMP_InputField _nameField;
        [SerializeField] private Button _saveButton;

        private Action<string> _onSave;
        private bool _isBound;
        private string _defaultTitle;
        private string _defaultPlaceholder;

        void Awake()
        {
            _defaultTitle = _titleLabel.text;
            _defaultPlaceholder = _nameField.placeholder is TMP_Text placeholder ? placeholder.text : string.Empty;
        }

        /// <summary>현재 이름과 저장 동작을 설정한다. 저장하면 입력값을 넘기고 팝업을 닫는다.</summary>
        public void Configure(string currentName, Action<string> onSave)
        {
            if (!_isBound)
            {
                _saveButton.onClick.AddListener(Save);
                _isBound = true;
            }
            _titleLabel.text = _defaultTitle;
            if (_nameField.placeholder is TMP_Text placeholder) placeholder.text = _defaultPlaceholder;
            _nameField.text = currentName;
            _onSave = onSave;
        }

        /// <summary>팝업 제목과 입력 안내 문구를 지정한다.</summary>
        public void SetPrompt(string title, string placeholder)
        {
            _titleLabel.text = title;
            if (_nameField.placeholder is TMP_Text placeholderLabel) placeholderLabel.text = placeholder;
        }

        /// <summary>팝업을 닫은 뒤 입력한 이름으로 저장 동작을 실행한다.</summary>
        private void Save()
        {
            Action<string> onSave = _onSave;
            string value = _nameField.text;
            Close();
            onSave?.Invoke(value);
        }
    }
}
