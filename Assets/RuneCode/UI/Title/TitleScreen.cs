using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>타이틀 화면 View다. 새로 하기·이어 하기·환경 설정·디버그 시작·테스트 모드 시작·종료 입력을 알린다.</summary>
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
        private Button _continueButton;

        /// <summary>새로 하기 버튼을 눌렀을 때 알린다.</summary>
        public event Action StartClicked;

        /// <summary>이어 하기 버튼을 눌렀을 때 알린다.</summary>
        public event Action ContinueClicked;

        /// <summary>환경 설정 버튼을 눌렀을 때 알린다.</summary>
        public event Action SettingsClicked;

        /// <summary>디버그 시작 버튼을 눌렀을 때 알린다.</summary>
        public event Action DebugStartClicked;

        /// <summary>테스트 모드 시작 버튼을 눌렀을 때 알린다.</summary>
        public event Action TestStartClicked;

        /// <summary>게임 종료 버튼을 눌렀을 때 알린다.</summary>
        public event Action QuitClicked;

        /// <summary>
        /// 타이틀 메뉴를 만들고 활성 버튼만 순서대로 배치한 뒤 세션·UI 관리자와 Presenter를 연결한다.
        /// 테스트 모드 시작은 모든 빌드에서 표시하고 디버그 시작은 Unity 에디터에서만 표시한다.
        /// </summary>
        public void Initialize(RuneCodeSession session, UIManager ui)
        {
            if (_presenter != null) return;
            _startButton.onClick.AddListener(() => StartClicked?.Invoke());
            _debugStartButton.onClick.AddListener(() => DebugStartClicked?.Invoke());
            _debugStartButton.gameObject.SetActive(Application.isEditor);
            _startButton.GetComponentInChildren<TextMeshProUGUI>(true).text = GameData.L("ui.newGame");
            Button continueButton = CreateMenuButton("ContinueButton", GameData.L("ui.continueGame"));
            _continueButton = continueButton;
            continueButton.onClick.AddListener(() => ContinueClicked?.Invoke());
            Button settingsButton = CreateMenuButton("SettingsButton", GameData.L("ui.environmentSettings"));
            settingsButton.onClick.AddListener(() => SettingsClicked?.Invoke());
            Button testStartButton = CreateMenuButton("TestStartButton", GameData.L("ui.startTest"));
            testStartButton.onClick.AddListener(() => TestStartClicked?.Invoke());
            Button quitButton = CreateMenuButton("QuitButton", GameData.L("ui.quit"));
            quitButton.onClick.AddListener(() => QuitClicked?.Invoke());
            ArrangeButtons(_debugStartButton, _startButton, continueButton, settingsButton, testStartButton, quitButton);
            _presenter = new TitlePresenter(session, ui, this);
            RefreshSaveAvailability();
        }

        /// <summary>타이틀을 다시 표시할 때 단일 저장의 존재 여부를 이어 하기 버튼에 반영한다.</summary>
        protected override void OnShown() => RefreshSaveAvailability();

        /// <summary>진행 저장이 없으면 이어 하기를 비활성화한다. 손상된 저장은 불러오기 시 오류를 안내한다.</summary>
        public void RefreshSaveAvailability()
        {
            if (_continueButton != null) _continueButton.interactable = SaveStore.HasSlot(SaveStore.ActiveSlot);
        }

        /// <summary>Esc로 타이틀 환경 설정 열기를 알리고 입력을 소비한다. 열린 설정의 닫기는 공용 팝업 흐름이 처리한다.</summary>
        public override bool HandleEscape()
        {
            SettingsClicked?.Invoke();
            return true;
        }

        /// <summary>일반 시작 버튼을 복제해 지정한 타이틀 메뉴 버튼을 만든다.</summary>
        private Button CreateMenuButton(string name, string labelText)
        {
            Button button = Instantiate(_startButton, _startButton.transform.parent, false);
            button.name = name;
            button.onClick.RemoveAllListeners();
            UiFactory.BindClickSound(button);
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
