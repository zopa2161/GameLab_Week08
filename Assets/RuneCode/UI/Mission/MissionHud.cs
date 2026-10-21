using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 미션 화면의 HUD View다. 체력·에너지 막대, 전투 단계, 처치·조각, 전투 목표, 마법 줄과 조각 토스트를 표시하고
    /// 라운드 시작·작업실·일시정지·디버그 버튼 클릭을 알린다. 표시 값 계산은 MissionPresenter가 한다.
    /// </summary>
    public sealed class MissionHud : MonoBehaviour
    {
        private const float BAR_WIDTH = 238f;
        private const float BAR_HEIGHT = 9f;
        private const float WORKSHOP_BUTTON_GAP = 12f;


        [Header("막대")]
        [SerializeField] private TextMeshProUGUI _hpCaption;
        [SerializeField] private TextMeshProUGUI _energyCaption;
        [SerializeField] private Image _hpFill;
        [SerializeField] private Image _energyFill;

        [Header("전투 정보")]
        [SerializeField] private TextMeshProUGUI _stageLabel;
        [SerializeField] private TextMeshProUGUI _statsLabel;
        [SerializeField] private TextMeshProUGUI _timerLabel;
        [SerializeField] private TextMeshProUGUI _timerWarningLabel;
        [SerializeField] private UiStateVisual _timerVisual;
        [SerializeField] private TextMeshProUGUI _spellLabel;
        [SerializeField] private TextMeshProUGUI _fragmentToast;

        [Header("Cooldown gauges")]
        [SerializeField] private CooldownRadialView _attackCooldown;
        [SerializeField] private CooldownRadialView _dashCooldown;

        [Header("버튼")]
        [SerializeField] private Button _pauseButton;
        [SerializeField] private Button _debugButton;
        [SerializeField] private Button _nextRoundButton;

        private bool _isBound;
        private Button _workshopButton;

        /// <summary>일시정지 버튼을 눌렀을 때 알린다.</summary>
        public event Action PauseClicked;

        /// <summary>디버그 버튼을 눌렀을 때 알린다.</summary>
        public event Action DebugClicked;

        /// <summary>소강상태의 다음 라운드 시작 버튼을 눌렀을 때 알린다.</summary>
        public event Action NextRoundClicked;

        /// <summary>소강상태의 작업실 열기 버튼을 눌렀을 때 알린다.</summary>
        public event Action WorkshopClicked;

        /// <summary>버튼 클릭을 이벤트로 처음 한 번만 연결하고 디버그 버튼 표시 여부를 정한다.</summary>
        public void Bind(bool isDebugVisible)
        {
            if (!_isBound)
            {
                _pauseButton.onClick.AddListener(() => PauseClicked?.Invoke());
                _debugButton.onClick.AddListener(() => DebugClicked?.Invoke());
                _nextRoundButton.onClick.AddListener(() => NextRoundClicked?.Invoke());
                _workshopButton = CreateWorkshopButton();
                _workshopButton.onClick.AddListener(() => WorkshopClicked?.Invoke());
                _isBound = true;
            }
            _debugButton.gameObject.SetActive(isDebugVisible);
            SetNextRound(false, false);
            SetWorkshop(false, false);
        }

        /// <summary>라운드 대기 여부에 따라 시작 버튼을 표시하고 팝업·일시정지 중에는 입력을 잠근다.</summary>
        public void SetNextRound(bool isVisible, bool isInteractable)
        {
            _nextRoundButton.interactable = isVisible && isInteractable;
            _nextRoundButton.gameObject.SetActive(isVisible);
        }

        /// <summary>작업실 열기 가능 여부에 따라 작업실 버튼을 표시하고 팝업·일시정지 중에는 입력을 잠근다.</summary>
        public void SetWorkshop(bool isVisible, bool isInteractable)
        {
            _workshopButton.interactable = isVisible && isInteractable;
            _workshopButton.gameObject.SetActive(isVisible);
        }

        /// <summary>다음 라운드 버튼을 복제해 바로 아래에 작업실 버튼을 만들고 문구를 바꾼다.</summary>
        private Button CreateWorkshopButton()
        {
            Button button = Instantiate(_nextRoundButton, _nextRoundButton.transform.parent, false);
            button.name = "WorkshopButton";
            button.onClick.RemoveAllListeners();
            UiFactory.BindClickSound(button);
            button.GetComponentInChildren<TextMeshProUGUI>(true).text = GameData.L("ui.openWorkshop");
            RectTransform source = (RectTransform)_nextRoundButton.transform;
            ((RectTransform)button.transform).anchoredPosition = source.anchoredPosition
                - new Vector2(0f, source.sizeDelta.y + WORKSHOP_BUTTON_GAP);
            return button;
        }

        /// <summary>스테이지 번호 문구를 표시한다.</summary>
        public void SetStage(string text) => _stageLabel.text = text;

        /// <summary>체력·전력 막대와 현재·최대 수치를 표시하고 energyRegen을 초당 전력 회복량으로 안내한다.</summary>
        public void SetBars(double hp, double maxHp, double energy, double maxEnergy, double energyRegen)
        {
            _hpFill.rectTransform.sizeDelta = new Vector2(BAR_WIDTH * (float)(hp / maxHp), BAR_HEIGHT);
            _energyFill.rectTransform.sizeDelta = new Vector2(BAR_WIDTH * (float)(energy / maxEnergy), BAR_HEIGHT);
            _hpCaption.text = GameData.L("ui.hp") + "  " + hp.ToString("0") + "/" + maxHp.ToString("0");
            _energyCaption.text = GameData.L("ui.energy") + "  " + energy.ToString("0") + "/" + maxEnergy.ToString("0")
                + "   " + GameData.L("ui.energy") + " 회복 " + energyRegen.ToString("0.#") + "/s";
        }

        /// <summary>처치·조각 문구를 표시한다.</summary>
        public void SetStats(string text) => _statsLabel.text = text;

        /// <summary>전투 목표 또는 대기·완료 문구를 표시하고 isWarning이면 경고 표시로 바꾼다.</summary>
        public void SetTimer(string text, bool isWarning)
        {
            _timerLabel.text = text;
            _timerWarningLabel.text = text;
            _timerVisual.Show(isWarning ? 1 : 0);
        }

        /// <summary>하단 마법 정보 줄을 표시한다.</summary>
        public void SetSpellLine(string text) => _spellLabel.text = text;

        /// <summary>Updates attack and dash cooldown gauges using their remaining time and full duration.</summary>
        public void SetCooldowns(double attackRemaining, double attackDuration, double dashRemaining, double dashDuration)
        {
            _attackCooldown.SetCooldown(attackRemaining, attackDuration);
            _dashCooldown.SetCooldown(dashRemaining, dashDuration);
        }

        /// <summary>조각 획득 토스트 문구를 표시한다. 빈 문자열이면 숨긴 것과 같다.</summary>
        public void SetToast(string text) => _fragmentToast.text = text;
    }
}
