using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>타이틀 화면 View다. 새로 하기·이어 하기·저장 관리·환경 설정·디버그 시작·종료 입력을 알린다.</summary>
    public sealed class TitleScreen : UiView
    {
        private const float MENU_BUTTON_X = 154f;
        private const float MENU_TOP = 316f;
        private const float MENU_BUTTON_WIDTH = 274f;
        private const float MENU_BUTTON_HEIGHT = 48f;
        private const float MENU_BUTTON_GAP = 12f;

        [Header("화면 구성")]
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _debugStartButton;

        private TitlePresenter _presenter;

        /// <summary>새로 하기 버튼을 눌렀을 때 알린다.</summary>
        public event Action StartClicked;

        /// <summary>이어 하기 버튼을 눌렀을 때 알린다.</summary>
        public event Action ContinueClicked;

        /// <summary>환경 설정 버튼을 눌렀을 때 알린다.</summary>
        public event Action SettingsClicked;

        /// <summary>디버그 시작 버튼을 눌렀을 때 알린다.</summary>
        public event Action DebugStartClicked;

        /// <summary>저장 슬롯 관리 버튼을 눌렀을 때 알린다.</summary>
        public event Action SaveManagementClicked;

        /// <summary>게임 종료 버튼을 눌렀을 때 알린다.</summary>
        public event Action QuitClicked;

        /// <summary>
        /// 타이틀 메뉴를 만들고 활성 버튼만 순서대로 배치한 뒤 세션·UI 관리자와 Presenter를 연결한다.
        /// 디버그 시작은 Unity 에디터에서만 표시한다.
        /// </summary>
        public void Initialize(RuneCodeSession session, UIManager ui)
        {
            if (_presenter != null) return;
            _startButton.onClick.AddListener(() => StartClicked?.Invoke());
            _debugStartButton.onClick.AddListener(() => DebugStartClicked?.Invoke());
            _debugStartButton.gameObject.SetActive(Application.isEditor);
            _startButton.GetComponentInChildren<TextMeshProUGUI>(true).text = GameData.L("ui.newGame");
            Button continueButton = CreateMenuButton("ContinueButton", GameData.L("ui.continueGame"));
            continueButton.onClick.AddListener(() => ContinueClicked?.Invoke());
            Button saveManagementButton = CreateMenuButton("SaveManagementButton", GameData.L("save.manage"));
            saveManagementButton.onClick.AddListener(() => SaveManagementClicked?.Invoke());
            Button settingsButton = CreateMenuButton("SettingsButton", GameData.L("ui.environmentSettings"));
            settingsButton.onClick.AddListener(() => SettingsClicked?.Invoke());
            Button quitButton = CreateMenuButton("QuitButton", GameData.L("ui.quit"));
            quitButton.onClick.AddListener(() => QuitClicked?.Invoke());
            ArrangeButtons(_debugStartButton, _startButton, continueButton, saveManagementButton, settingsButton, quitButton);
            _presenter = new TitlePresenter(session, ui, this);
        }

        /// <summary>일반 시작 버튼을 복제해 지정한 타이틀 메뉴 버튼을 만든다.</summary>
        private Button CreateMenuButton(string name, string labelText)
        {
            Button button = Instantiate(_startButton, _startButton.transform.parent, false);
            button.name = name;
            button.onClick.RemoveAllListeners();
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            label.text = labelText;
            return button;
        }

        /// <summary>활성 메뉴만 지정 순서로 위에서부터 배치하고 버튼 크기와 형제 순서를 맞춘다.</summary>
        private void ArrangeButtons(params Button[] buttons)
        {
            int row = 0;
            foreach (Button button in buttons)
            {
                if (!button.gameObject.activeSelf) continue;
                RectTransform rect = (RectTransform)button.transform;
                rect.anchoredPosition = new Vector2(MENU_BUTTON_X, -MENU_TOP - row * (MENU_BUTTON_HEIGHT + MENU_BUTTON_GAP));
                rect.sizeDelta = new Vector2(MENU_BUTTON_WIDTH, MENU_BUTTON_HEIGHT);
                rect.SetAsLastSibling();
                row++;
            }
        }
    }
}
