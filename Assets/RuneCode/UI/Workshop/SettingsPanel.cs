using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>설정 팝업의 내용 View다. 설정·저장·게임 종료·디버그 입력을 알린다.</summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        [Header("설정")]
        [SerializeField] private Button _screenShakeButton;
        [SerializeField] private TextMeshProUGUI _screenShakeLabel;
        [SerializeField] private Button _hitStopButton;
        [SerializeField] private TextMeshProUGUI _hitStopLabel;
        [SerializeField] private Button _resetSaveButton;

        [Header("디버그")]
        [SerializeField] private GameObject _debugGroup;
        [SerializeField] private Button _debugGrantButton;
        [SerializeField] private Button _debugUnlockButton;
        [SerializeField] private Button _debugResetTreeButton;

        private Button _autoSaveButton;
        private Button _manualSaveButton;
        private Button _manualLoadButton;

        /// <summary>화면 흔들림 버튼을 눌렀을 때 알린다.</summary>
        public event Action ScreenShakeClicked;

        /// <summary>히트스톱 버튼을 눌렀을 때 알린다.</summary>
        public event Action HitStopClicked;

        /// <summary>저장 초기화 버튼을 눌렀을 때 알린다.</summary>
        public event Action ResetSaveClicked;

        /// <summary>디버그 조각 지급을 눌렀을 때 알린다.</summary>
        public event Action DebugGrantClicked;

        /// <summary>디버그 전체 해금을 눌렀을 때 알린다.</summary>
        public event Action DebugUnlockClicked;

        /// <summary>디버그 강화 트리 초기화를 눌렀을 때 알린다.</summary>
        public event Action DebugTreeResetClicked;

        /// <summary>자동 저장 전환을 눌렀을 때 알린다.</summary>
        public event Action AutoSaveClicked;

        /// <summary>수동 저장을 눌렀을 때 알린다.</summary>
        public event Action ManualSaveClicked;

        /// <summary>수동 불러오기를 눌렀을 때 알린다.</summary>
        public event Action ManualLoadClicked;

        /// <summary>게임 종료를 눌렀을 때 알린다.</summary>
        public event Action QuitClicked;

        /// <summary>버튼 입력을 이벤트로 연결하고 저장·종료 버튼을 만든다. Presenter가 한 번만 호출한다.</summary>
        public void Bind()
        {
            _screenShakeButton.onClick.AddListener(() => ScreenShakeClicked?.Invoke());
            _hitStopButton.onClick.AddListener(() => HitStopClicked?.Invoke());
            _resetSaveButton.onClick.AddListener(() => ResetSaveClicked?.Invoke());
            _debugGrantButton.onClick.AddListener(() => DebugGrantClicked?.Invoke());
            _debugUnlockButton.onClick.AddListener(() => DebugUnlockClicked?.Invoke());
            if (_debugResetTreeButton != null) _debugResetTreeButton.onClick.AddListener(() => DebugTreeResetClicked?.Invoke());
            CreateSaveButtons();
            CreateQuitButton();
        }

        /// <summary>
        /// 진행 종류에 맞춰 디버그 도구·저장 버튼 표시와 초기화 버튼 문구(resetLabelKey)를 갱신한다.
        /// 같은 팝업 인스턴스를 일반·디버그·테스트 진행이 함께 쓰므로 열 때마다 호출한다.
        /// </summary>
        public void SetSessionMode(bool isDebugVisible, bool isSaveVisible, string resetLabelKey)
        {
            _resetSaveButton.GetComponentInChildren<TextMeshProUGUI>().text = GameData.L(resetLabelKey);
            _debugGroup.SetActive(isDebugVisible);
            _autoSaveButton.gameObject.SetActive(isSaveVisible);
            _manualSaveButton.gameObject.SetActive(isSaveVisible);
            _manualLoadButton.gameObject.SetActive(isSaveVisible);
        }

        /// <summary>화면 흔들림·히트스톱 설정의 켜짐 여부를 버튼 문구에 표시한다.</summary>
        public void SetFeedback(bool isScreenShake, bool isHitStop)
        {
            _screenShakeLabel.text = GameData.L("ui.shake") + "  " + GameData.L(isScreenShake ? "ui.on" : "ui.off");
            _hitStopLabel.text = GameData.L("ui.hitstop") + "  " + GameData.L(isHitStop ? "ui.on" : "ui.off");
        }

        /// <summary>자동 저장 상태와 미저장 여부를 표시하고 저장·불러오기 가능 상태를 갱신한다.</summary>
        public void SetSaveOptions(bool isAutoSaveEnabled, bool isDirty, bool canSave, bool canLoad)
        {
            SetButtonLabel(_autoSaveButton, GameData.L("save.autoSave") + "  " + GameData.L(isAutoSaveEnabled ? "ui.on" : "ui.off"));
            SetButtonLabel(_manualSaveButton, GameData.L(isDirty ? "save.savePending" : "save.save"));
            SetButtonLabel(_manualLoadButton, GameData.L("save.load"));
            _manualSaveButton.interactable = canSave;
            _manualLoadButton.interactable = canLoad;
        }

        /// <summary>기존 설정 버튼을 복제해 자동 저장·수동 저장·불러오기 버튼을 한 줄에 만들고 이벤트를 연결한다.</summary>
        private void CreateSaveButtons()
        {
            if (_autoSaveButton != null) return;
            Transform parent = _screenShakeButton.transform.parent;
            _autoSaveButton = CreateSaveButton(parent, "AutoSaveButton", 0);
            _manualSaveButton = CreateSaveButton(parent, "ManualSaveButton", 151);
            _manualLoadButton = CreateSaveButton(parent, "ManualLoadButton", 302);
            _autoSaveButton.onClick.AddListener(() => AutoSaveClicked?.Invoke());
            _manualSaveButton.onClick.AddListener(() => ManualSaveClicked?.Invoke());
            _manualLoadButton.onClick.AddListener(() => ManualLoadClicked?.Invoke());
        }

        /// <summary>저장 초기화 버튼을 반으로 줄이고 같은 줄에 게임 종료 버튼을 만든다.</summary>
        private void CreateQuitButton()
        {
            Transform parent = _resetSaveButton.transform.parent;
            RectTransform resetRect = (RectTransform)_resetSaveButton.transform;
            resetRect.sizeDelta = new Vector2(216, resetRect.sizeDelta.y);

            Button button = Instantiate(_resetSaveButton, parent, false);
            button.name = "QuitButton";
            button.onClick.RemoveAllListeners();
            RectTransform rect = (RectTransform)button.transform;
            rect.anchoredPosition = new Vector2(230, -280);
            rect.sizeDelta = new Vector2(216, 48);
            SetButtonLabel(button, GameData.L("ui.quit"));
            button.onClick.AddListener(() => QuitClicked?.Invoke());
        }

        /// <summary>기존 피드백 버튼을 복제해 지정 위치에 144×48 크기의 새 버튼을 만든다.</summary>
        private Button CreateSaveButton(Transform parent, string name, float x)
        {
            Button button = Instantiate(_screenShakeButton, parent, false);
            button.name = name;
            button.onClick.RemoveAllListeners();
            RectTransform rect = (RectTransform)button.transform;
            rect.anchoredPosition = new Vector2(x, -210);
            rect.sizeDelta = new Vector2(144, 48);
            return button;
        }

        /// <summary>버튼 안의 TMP 레이블이 있으면 표시 문구를 바꾼다.</summary>
        private static void SetButtonLabel(Button button, string value)
        {
            if (button == null) return;
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = value;
        }
    }
}
