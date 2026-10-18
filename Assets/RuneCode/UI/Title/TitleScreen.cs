using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>타이틀 화면 View다. 시작·저장 슬롯 관리·게임 종료 입력을 알리고 처리는 TitlePresenter가 한다.</summary>
    public sealed class TitleScreen : UiView
    {
        private const float MENU_BUTTON_X = 154f;
        private const float DEBUG_START_BUTTON_Y = -568f;
        private const float DEBUG_BUILD_QUIT_BUTTON_Y = -642f;

        [Header("화면 구성")]
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _debugStartButton;

        private Button _saveManagementButton;
        private TitlePresenter _presenter;

        /// <summary>시작 버튼을 눌렀을 때 알린다.</summary>
        public event Action StartClicked;

        /// <summary>디버그 시작 버튼을 눌렀을 때 알린다.</summary>
        public event Action DebugStartClicked;

        /// <summary>저장 슬롯 관리 버튼을 눌렀을 때 알린다.</summary>
        public event Action SaveManagementClicked;

        /// <summary>게임 종료 버튼을 눌렀을 때 알린다.</summary>
        public event Action QuitClicked;

        /// <summary>
        /// 시작·슬롯 관리·게임 종료 버튼을 연결하고 세션·UI 관리자와 Presenter를 만든다.
        /// 디버그 시작은 Unity 에디터에서만 표시한다.
        /// </summary>
        public void Initialize(RuneCodeSession session, UIManager ui)
        {
            if (_presenter != null) return;
            _startButton.onClick.AddListener(() => StartClicked?.Invoke());
            _debugStartButton.onClick.AddListener(() => DebugStartClicked?.Invoke());
            _debugStartButton.gameObject.SetActive(Application.isEditor);
            ((RectTransform)_debugStartButton.transform).anchoredPosition = new Vector2(MENU_BUTTON_X, DEBUG_START_BUTTON_Y);
            _saveManagementButton = CreateMenuButton("SaveManagementButton", GameData.L("save.manage"), -420);
            _saveManagementButton.onClick.AddListener(() => SaveManagementClicked?.Invoke());
            Button quitButton = CreateMenuButton("QuitButton", GameData.L("ui.quit"), DEBUG_BUILD_QUIT_BUTTON_Y);
            quitButton.onClick.AddListener(() => QuitClicked?.Invoke());
            _presenter = new TitlePresenter(session, ui, this);
        }

        /// <summary>일반 시작 버튼을 복제해 지정한 타이틀 메뉴 버튼을 만든다.</summary>
        private Button CreateMenuButton(string name, string labelText, float y)
        {
            Button button = Instantiate(_startButton, _startButton.transform.parent, false);
            button.name = name;
            RectTransform rect = (RectTransform)button.transform;
            rect.anchoredPosition = new Vector2(MENU_BUTTON_X, y);
            button.onClick.RemoveAllListeners();
            UiFactory.BindClickSound(button);
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            label.text = labelText;
            return button;
        }
    }
}
