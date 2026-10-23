using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>공통 설정과 음량 초안을 표시하고 저장·타이틀 복귀·게임 종료·디버그 입력을 알린다.</summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        private const float LOWER_CONTROLS_TOP = 370f;
        private const float CONTROL_ROW_STEP = 54f;
        private const float CONTROL_GROUP_GAP = 12f;

        private static readonly Vector2 BASIC_PANEL_SIZE = new Vector2(494, 470);
        private static readonly Vector2 WORKSHOP_PANEL_SIZE = new Vector2(1000, 700);

        [Header("설정")]
        [SerializeField] private RectTransform _card;
        [SerializeField] private Button _screenShakeButton;
        [SerializeField] private TextMeshProUGUI _screenShakeLabel;
        [SerializeField] private Button _hitStopButton;
        [SerializeField] private TextMeshProUGUI _hitStopLabel;
        [SerializeField] private Button _resetSaveButton;

        [Header("음량")]
        [SerializeField] private Slider _masterVolumeSlider;
        [SerializeField] private Slider _bgmVolumeSlider;
        [SerializeField] private Slider _sfxVolumeSlider;
        [SerializeField] private TextMeshProUGUI _masterVolumeLabel;
        [SerializeField] private TextMeshProUGUI _bgmVolumeLabel;
        [SerializeField] private TextMeshProUGUI _sfxVolumeLabel;
        [SerializeField] private Button _applyVolumeButton;

        [Header("작업실 안내")]
        [SerializeField] private GameObject _workshopControls;
        [SerializeField] private GameObject _workshopDivider;

        [Header("디버그")]
        [SerializeField] private GameObject _debugGroup;
        [SerializeField] private Button _debugGrantButton;
        [SerializeField] private Button _debugUnlockButton;
        [SerializeField] private Button _debugResetTreeButton;

        private Button _manualSaveButton;
        private Button _manualLoadButton;
        private Button _returnToTitleButton;

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

        /// <summary>수동 저장을 눌렀을 때 알린다.</summary>
        public event Action ManualSaveClicked;

        /// <summary>수동 불러오기를 눌렀을 때 알린다.</summary>
        public event Action ManualLoadClicked;

        /// <summary>게임 종료를 눌렀을 때 알린다.</summary>
        public event Action QuitClicked;

        /// <summary>타이틀 복귀를 눌렀을 때 알린다.</summary>
        public event Action ReturnToTitleClicked;

        /// <summary>버튼 입력을 이벤트로 연결하고 저장·타이틀 복귀 버튼을 만든다. Presenter가 한 번만 호출한다.</summary>
        public void Bind()
        {
            _screenShakeButton.onClick.AddListener(() => ScreenShakeClicked?.Invoke());
            _hitStopButton.onClick.AddListener(() => HitStopClicked?.Invoke());
            _applyVolumeButton.onClick.AddListener(ApplyVolumes);
            _masterVolumeSlider.onValueChanged.AddListener(_ => RefreshVolumeLabels());
            _bgmVolumeSlider.onValueChanged.AddListener(_ => RefreshVolumeLabels());
            _sfxVolumeSlider.onValueChanged.AddListener(_ => RefreshVolumeLabels());
            _resetSaveButton.onClick.AddListener(() => ResetSaveClicked?.Invoke());
            _debugGrantButton.onClick.AddListener(() => DebugGrantClicked?.Invoke());
            _debugUnlockButton.onClick.AddListener(() => DebugUnlockClicked?.Invoke());
            if (_debugResetTreeButton != null) _debugResetTreeButton.onClick.AddListener(() => DebugTreeResetClicked?.Invoke());
            CreateSaveButtons();
            CreateReturnToTitleButton();
        }

        /// <summary>
        /// 진행 종류에 맞춰 디버그 도구·저장 버튼 표시와 초기화 버튼 문구(resetLabelKey)를 갱신한다.
        /// 같은 팝업 인스턴스를 일반·디버그·테스트 진행이 함께 쓰므로 열 때마다 호출한다.
        /// </summary>
        public void SetSessionMode(bool isDebugVisible, bool isSaveVisible, string resetLabelKey, bool isTitle = false)
        {
            bool isDebugGroupVisible = !isTitle && isDebugVisible;
            bool isSaveRowVisible = !isTitle && isSaveVisible;
            _resetSaveButton.GetComponentInChildren<TextMeshProUGUI>(true).text = GameData.L(resetLabelKey);
            _debugGroup.SetActive(isDebugGroupVisible);
            _workshopControls.SetActive(!isTitle);
            _workshopDivider.SetActive(!isTitle);
            Vector2 panelSize = isTitle ? BASIC_PANEL_SIZE : WORKSHOP_PANEL_SIZE;
            _card.sizeDelta = panelSize;
            _card.anchoredPosition = new Vector2((UiTheme.SCREEN_WIDTH - panelSize.x) * 0.5f,
                -(UiTheme.SCREEN_HEIGHT - panelSize.y) * 0.5f);
            _resetSaveButton.gameObject.SetActive(!isTitle);
            _returnToTitleButton.gameObject.SetActive(!isTitle);
            _manualSaveButton.gameObject.SetActive(isSaveRowVisible);
            _manualLoadButton.gameObject.SetActive(isSaveRowVisible);
            LayoutLowerControls(isDebugGroupVisible, isSaveRowVisible);
        }

        /// <summary>화면 흔들림·히트스톱 설정의 켜짐 여부를 버튼 문구에 표시한다.</summary>
        public void SetFeedback(bool isScreenShake, bool isHitStop)
        {
            _screenShakeLabel.text = GameData.L("ui.shake") + "  " + GameData.L(isScreenShake ? "ui.on" : "ui.off");
            _hitStopLabel.text = GameData.L("ui.hitstop") + "  " + GameData.L(isHitStop ? "ui.on" : "ui.off");
        }

        /// <summary>설정창을 열 때 현재 런타임 음량으로 초안과 퍼센트 표시를 초기화한다.</summary>
        public void ResetVolumeDraft()
        {
            _masterVolumeSlider.SetValueWithoutNotify(SoundManager.Instance.MasterVolume);
            _bgmVolumeSlider.SetValueWithoutNotify(SoundManager.Instance.BGMVolume);
            _sfxVolumeSlider.SetValueWithoutNotify(SoundManager.Instance.SFXVolume);
            RefreshVolumeLabels();
        }

        /// <summary>각 슬라이더의 현재 음량 초안을 정수 퍼센트로 표시한다.</summary>
        private void RefreshVolumeLabels()
        {
            _masterVolumeLabel.text = Mathf.RoundToInt(_masterVolumeSlider.value * 100f) + "%";
            _bgmVolumeLabel.text = Mathf.RoundToInt(_bgmVolumeSlider.value * 100f) + "%";
            _sfxVolumeLabel.text = Mathf.RoundToInt(_sfxVolumeSlider.value * 100f) + "%";
        }

        /// <summary>음량 초안을 사운드 버스와 기기 공통 설정에 적용하고 기존 적용음을 재생한다.</summary>
        private void ApplyVolumes()
        {
            float masterVolume = _masterVolumeSlider.value;
            float bgmVolume = _bgmVolumeSlider.value;
            float sfxVolume = _sfxVolumeSlider.value;
            SoundManager.Instance.SetVolumes(masterVolume, bgmVolume, sfxVolume);
            SaveStore.SetVolumes(masterVolume, bgmVolume, sfxVolume);
            SoundManager.Instance.PlayUnlockSkillTree();
        }

        /// <summary>미저장 여부를 표시하고 저장·불러오기 가능 상태를 갱신한다.</summary>
        public void SetSaveOptions(bool isDirty, bool canSave, bool canLoad)
        {
            SetButtonLabel(_manualSaveButton, GameData.L(isDirty ? "save.savePending" : "save.save"));
            SetButtonLabel(_manualLoadButton, GameData.L("save.load"));
            _manualSaveButton.interactable = canSave;
            _manualLoadButton.interactable = canLoad;
        }

        /// <summary>기존 설정 버튼을 복제해 수동 저장·불러오기 버튼을 한 줄에 만들고 이벤트를 연결한다.</summary>
        private void CreateSaveButtons()
        {
            if (_manualSaveButton != null) return;
            Transform parent = _screenShakeButton.transform.parent;
            _manualSaveButton = CreateSaveButton(parent, "ManualSaveButton", 0);
            _manualLoadButton = CreateSaveButton(parent, "ManualLoadButton", 151);
            _manualSaveButton.onClick.AddListener(() => ManualSaveClicked?.Invoke());
            _manualLoadButton.onClick.AddListener(() => ManualLoadClicked?.Invoke());
        }

        /// <summary>저장 초기화 버튼을 반으로 줄이고 기존 게임 종료 버튼 위치에 타이틀 복귀 버튼을 만든다.</summary>
        private void CreateReturnToTitleButton()
        {
            Transform parent = _resetSaveButton.transform.parent;
            RectTransform resetRect = (RectTransform)_resetSaveButton.transform;
            resetRect.sizeDelta = new Vector2(216, resetRect.sizeDelta.y);

            Button button = Instantiate(_resetSaveButton, parent, false);
            button.name = "ReturnToTitleButton";
            _returnToTitleButton = button;
            button.onClick.RemoveAllListeners();
            RectTransform rect = (RectTransform)button.transform;
            rect.anchoredPosition = new Vector2(230, resetRect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(216, 48);
            SetButtonLabel(button, GameData.L("ui.returnToTitle"));
            button.onClick.AddListener(() => ReturnToTitleClicked?.Invoke());
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
            UiFactory.BindClickSound(button);
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
            UiFactory.BindClickSound(button);
            RectTransform rect = (RectTransform)button.transform;
            rect.anchoredPosition = new Vector2(x, -LOWER_CONTROLS_TOP);
            rect.sizeDelta = new Vector2(144, 48);
            return button;
        }

        /// <summary>표시 중인 디버그·저장 행만 차례로 배치해 소리 설정 아래의 버튼이 겹치지 않게 한다.</summary>
        private void LayoutLowerControls(bool isDebugVisible, bool isSaveVisible)
        {
            float nextTop = LOWER_CONTROLS_TOP;
            RectTransform debugRect = (RectTransform)_debugGroup.transform;
            SetTop(debugRect, nextTop);
            if (isDebugVisible) nextTop += debugRect.sizeDelta.y + CONTROL_GROUP_GAP;

            SetTop((RectTransform)_manualSaveButton.transform, nextTop);
            SetTop((RectTransform)_manualLoadButton.transform, nextTop);
            if (isSaveVisible) nextTop += CONTROL_ROW_STEP;

            SetTop((RectTransform)_resetSaveButton.transform, nextTop);
            SetTop((RectTransform)_returnToTitleButton.transform, nextTop);
        }

        /// <summary>rect의 가로 위치와 크기를 유지하고 위쪽 좌표를 top에 맞춘다.</summary>
        private static void SetTop(RectTransform rect, float top)
        {
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -top);
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
