using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 미션 화면의 HUD View다. 체력·에너지 막대, 전투 단계, 처치·조각, 전투 목표, 마법 줄과 조각 토스트를 표시하고
    /// 작업실·일시정지·디버그 버튼 클릭을 알린다. 표시 값 계산은 MissionPresenter가 한다.
    /// </summary>
    public sealed class MissionHud : MonoBehaviour
    {
        private const float BAR_WIDTH = 238f;
        private const float BAR_HEIGHT = 9f;
        private const float WORKSHOP_HIGHLIGHT_PULSE_SPEED = 5f;
        private const float WORKSHOP_HIGHLIGHT_PULSE_SCALE = 0.08f;

        private static readonly Color WORKSHOP_HIGHLIGHT_COLOR = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Vector2 WORKSHOP_HIGHLIGHT_DISTANCE = new Vector2(4f, -4f);


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
        [SerializeField] private Button _workshopButton;

        [Header("디버그 보스")]
        [SerializeField] private RectTransform _debugBossRoot;
        [SerializeField] private Button _debugBossButtonTemplate;

        private bool _isBound;
        private readonly List<Button> _debugBossButtons = new List<Button>();
        private readonly List<Button> _debugActionButtons = new List<Button>();
        private Outline _workshopOutline;
        private bool _isWorkshopHighlighted;

        /// <summary>일시정지 버튼을 눌렀을 때 알린다.</summary>
        public event Action PauseClicked;

        /// <summary>디버그 버튼을 눌렀을 때 알린다.</summary>
        public event Action DebugClicked;

        /// <summary>소강상태의 작업실 열기 버튼을 눌렀을 때 알린다.</summary>
        public event Action WorkshopClicked;

        /// <summary>디버그 보스 바로 상대하기 버튼을 눌렀을 때 버튼 순번을 알린다.</summary>
        public event Action<int> DebugBossClicked;

        /// <summary>디버그 소환 시험 조작 버튼을 눌렀을 때 버튼 순번을 알린다.</summary>
        public event Action<int> DebugActionClicked;

        void Update()
        {
            if (!_isWorkshopHighlighted) return;
            float scale = 1f + WORKSHOP_HIGHLIGHT_PULSE_SCALE * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * WORKSHOP_HIGHLIGHT_PULSE_SPEED));
            _workshopButton.transform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>버튼 클릭을 이벤트로 처음 한 번만 연결하고 디버그 버튼 표시 여부를 정한다.</summary>
        public void Bind(bool isDebugVisible)
        {
            if (!_isBound)
            {
                _pauseButton.onClick.AddListener(() => PauseClicked?.Invoke());
                _debugButton.onClick.AddListener(() => DebugClicked?.Invoke());
                _workshopButton.onClick.AddListener(() => WorkshopClicked?.Invoke());
                _isBound = true;
            }
            _debugButton.gameObject.SetActive(isDebugVisible);
            SetWorkshop(false, false);
            SetDebugBossMenu(false, false);
        }

        /// <summary>
        /// 보스 이름 목록으로 디버그 보스 버튼을 처음 한 번만 템플릿에서 복제해 만들고 클릭 시 순번을 알리게 연결한다.
        /// 이미 만들었으면 다시 만들지 않는다.
        /// </summary>
        public void BuildDebugBosses(IReadOnlyList<string> labels)
        {
            if (_debugBossButtons.Count > 0) return;
            _debugBossButtonTemplate.gameObject.SetActive(false);
            for (int index = 0; index < labels.Count; index++)
            {
                int bossIndex = index;
                Button button = Instantiate(_debugBossButtonTemplate, _debugBossRoot, false);
                button.name = "DebugBossButton" + index;
                button.GetComponentInChildren<TextMeshProUGUI>().text = labels[index];
                button.onClick.AddListener(() => DebugBossClicked?.Invoke(bossIndex));
                button.gameObject.SetActive(true);
                _debugBossButtons.Add(button);
            }
        }

        /// <summary>디버그 보스 선택 버튼 묶음을 표시하거나 숨기고 소환 시험 조작 버튼은 숨긴다. 팝업·일시정지 중에는 입력을 잠근다.</summary>
        public void SetDebugBossMenu(bool isVisible, bool isInteractable)
        {
            _debugBossRoot.gameObject.SetActive(isVisible);
            foreach (Button button in _debugBossButtons)
            {
                button.gameObject.SetActive(true);
                button.interactable = isVisible && isInteractable;
            }
            foreach (Button button in _debugActionButtons) button.gameObject.SetActive(false);
        }

        /// <summary>
        /// 소환 시험 조작 버튼을 라벨 순서대로 표시하고 보스 선택 버튼은 숨긴다.
        /// 필요한 버튼은 템플릿에서 복제해 재사용하며 라벨이 같으면 글자를 다시 쓰지 않는다. 팝업·일시정지 중에는 입력을 잠근다.
        /// </summary>
        public void SetDebugActions(IReadOnlyList<string> labels, bool isInteractable)
        {
            _debugBossRoot.gameObject.SetActive(labels.Count > 0);
            foreach (Button button in _debugBossButtons) button.gameObject.SetActive(false);
            while (_debugActionButtons.Count < labels.Count)
            {
                int actionIndex = _debugActionButtons.Count;
                Button button = Instantiate(_debugBossButtonTemplate, _debugBossRoot, false);
                button.name = "DebugActionButton" + actionIndex;
                button.onClick.AddListener(() => DebugActionClicked?.Invoke(actionIndex));
                _debugActionButtons.Add(button);
            }
            for (int index = 0; index < _debugActionButtons.Count; index++)
            {
                Button button = _debugActionButtons[index];
                bool isVisible = index < labels.Count;
                button.gameObject.SetActive(isVisible);
                if (!isVisible) continue;
                button.interactable = isInteractable;
                TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
                if (label.text != labels[index]) label.text = labels[index];
            }
        }

        /// <summary>작업실 열기 가능 여부에 따라 작업실 버튼을 표시하고 팝업·일시정지 중에는 입력을 잠근다.</summary>
        public void SetWorkshop(bool isVisible, bool isInteractable)
        {
            _workshopButton.interactable = isVisible && isInteractable;
            _workshopButton.gameObject.SetActive(isVisible);
        }

        /// <summary>작업실 버튼에 외곽선과 크기 맥동 강조를 켜거나 끈다. 끄면 원래 크기로 되돌린다. 외곽선은 처음 켤 때 만든다.</summary>
        public void SetWorkshopHighlight(bool isHighlighted)
        {
            if (_isWorkshopHighlighted == isHighlighted) return;
            _isWorkshopHighlighted = isHighlighted;
            if (isHighlighted && _workshopOutline == null)
            {
                _workshopOutline = _workshopButton.targetGraphic.gameObject.AddComponent<Outline>();
                _workshopOutline.effectColor = WORKSHOP_HIGHLIGHT_COLOR;
                _workshopOutline.effectDistance = WORKSHOP_HIGHLIGHT_DISTANCE;
            }
            if (_workshopOutline != null) _workshopOutline.enabled = isHighlighted;
            if (!isHighlighted) _workshopButton.transform.localScale = Vector3.one;
        }

        /// <summary>스테이지 문구를 표시하고 남은 적 수가 있으면 기존 타이머 텍스트 색상으로 덧붙인다.</summary>
        public void SetStage(string text, int? remainingEnemyCount = null)
        {
            if (!remainingEnemyCount.HasValue)
            {
                _stageLabel.text = text;
                return;
            }
            string enemyColor = ColorUtility.ToHtmlStringRGBA(_timerLabel.color);
            _stageLabel.text = text + " · <color=#" + enemyColor + ">남은 적 " + remainingEnemyCount.Value + "</color>";
        }

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

        /// <summary>아이템 이름·등급 또는 크레딧 획득 토스트를 표시한다. 빈 문자열이면 숨긴 것과 같다.</summary>
        public void SetToast(string text) => _fragmentToast.text = text;
    }
}
