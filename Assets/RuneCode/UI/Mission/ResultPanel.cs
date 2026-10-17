using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 미션 결과 View다. 완료·스테이지·플레이 시간과 획득 보상을 표시하고 마법 개선·재도전·클리어 Modifier 선택을 알린다.
    /// 미션 화면 안의 전체 화면 패널로, 결과가 나오면 MissionPresenter가 표시한다.
    /// </summary>
    public sealed class ResultPanel : UiView
    {
        [Header("보상 공개 연출")]
        [SerializeField, Min(0.05f)] private float _rewardRevealInterval = 0.3f;
        [SerializeField, Min(0)] private float _rewardRevealFinishDelay = 0.7f;

        [Header("문구")]
        [SerializeField] private TextMeshProUGUI _summaryLabel;
        [SerializeField] private TextMeshProUGUI _stageLabel;
        [SerializeField] private TextMeshProUGUI _progressLabel;

        [Header("획득 보상")]
        [SerializeField] private RectTransform _rewardItemsRoot;
        [SerializeField] private ResultRewardItemView _rewardItemPrefab;
        [SerializeField] private GameObject _rewardTooltip;
        [SerializeField] private TextMeshProUGUI _rewardTooltipTitle;
        [SerializeField] private TextMeshProUGUI _rewardTooltipDescription;

        [Header("버튼")]
        [SerializeField] private GameObject _winOnlyButtonRoot;
        [SerializeField] private Button _nextStageButton;
        [SerializeField] private Button _improveButton;
        [SerializeField] private Button _retryButton;

        [Header("클리어 보상 팝업")]
        [SerializeField] private GameObject _clearChoicePopup;
        [SerializeField] private TextMeshProUGUI _clearChoiceTitleLabel;
        [SerializeField] private TextMeshProUGUI _rewardTitleLabel;

        [Header("보상 카드")]
        [SerializeField] private GameObject[] _clearChoiceCards;
        [SerializeField] private Button[] _clearChoiceButtons;
        [SerializeField] private TextMeshProUGUI[] _clearChoiceGradeLabels;
        [SerializeField] private TextMeshProUGUI[] _clearChoiceNameLabels;
        [SerializeField] private TextMeshProUGUI[] _clearChoiceDescriptionLabels;

        private bool _isBound;
        private ResultRewardItemView _hoveredRewardItem;
        private readonly Vector3[] _tooltipCorners = new Vector3[4];
        private readonly List<ResultRewardItemView> _rewardViews = new List<ResultRewardItemView>();
        private readonly List<ResultRewardContent> _rewardContents = new List<ResultRewardContent>();
        private int _revealedCount;
        private int _revealCompletedFrame;
        private int _revealStartedFrame;
        private float _revealElapsed;
        private float _revealFinishedAt;
        private bool _isRevealing;
        private bool _isWaitingForRelease;

        public bool CanContinue => !_isRevealing && !_isWaitingForRelease && Time.frameCount > _revealCompletedFrame;

        /// <summary>마법 개선(작업실로 이동)을 눌렀을 때 알린다.</summary>
        public event Action ImproveClicked;

        /// <summary>승리 후 다음 스테이지 준비를 선택했을 때 알린다.</summary>
        public event Action NextStageClicked;

        /// <summary>같은 스테이지 재도전을 눌렀을 때 알린다.</summary>
        public event Action RetryClicked;

        /// <summary>클리어 Modifier 선택 후보 버튼을 눌렀을 때 후보 순번으로 알린다.</summary>
        public event Action<int> ClearModifierChoiceClicked;

        /// <summary>버튼 클릭을 이벤트로 처음 한 번만 연결한다.</summary>
        public void Bind()
        {
            if (_isBound) return;
            _nextStageButton.onClick.AddListener(() => { if (CanContinue) NextStageClicked?.Invoke(); });
            _improveButton.onClick.AddListener(() => { if (CanContinue) ImproveClicked?.Invoke(); });
            _retryButton.onClick.AddListener(() => { if (CanContinue) RetryClicked?.Invoke(); });
            for (int i = 0; i < _clearChoiceButtons.Length; i++)
            {
                int index = i;
                _clearChoiceButtons[i].onClick.AddListener(() => { if (CanContinue) ClearModifierChoiceClicked?.Invoke(index); });
            }
            _isBound = true;
        }

        /// <summary>결과 문구와 스테이지·진행 문구를 채우고 승리 결과일 때만 승리 전용 버튼을 표시한다.</summary>
        public void SetContent(string summary, string stage, string progress, bool isVictory)
        {
            _summaryLabel.richText = true;
            _summaryLabel.text = summary;
            _stageLabel.text = stage;
            _progressLabel.text = progress;
            _winOnlyButtonRoot.SetActive(isVictory);
        }

        /// <summary>정렬된 보상 목록을 만들고 animate이면 순차 공개를 준비하며, 아니면 연출 없이 모두 표시한다.</summary>
        public void SetRewards(IReadOnlyList<ResultRewardContent> rewards, bool animate = false)
        {
            HideRewardTooltip();
            UiFactory.ClearChildren(_rewardItemsRoot);
            GridLayoutGroup layout = _rewardItemsRoot.GetComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(96f, 72f);
            layout.constraint = GridLayoutGroup.Constraint.Flexible;
            _rewardViews.Clear();
            _rewardContents.Clear();
            _revealedCount = 0;
            _revealElapsed = 0;
            _revealFinishedAt = -1;
            _isWaitingForRelease = false;
            _revealStartedFrame = Time.frameCount;
            _revealCompletedFrame = Time.frameCount;
            _isRevealing = animate && rewards != null && rewards.Count > 0;
            SetNavigationEnabled(!_isRevealing);
            if (rewards == null) return;
            foreach (ResultRewardContent reward in rewards)
            {
                ResultRewardItemView item = Instantiate(_rewardItemPrefab, _rewardItemsRoot, false);
                ((RectTransform)item.transform).sizeDelta = layout.cellSize;
                item.SetContent(reward, HandleRewardHover);
                layout.cellSize = new Vector2(Mathf.Max(layout.cellSize.x, item.PreferredWidth), layout.cellSize.y);
                _rewardViews.Add(item);
                _rewardContents.Add(reward);
                if (_isRevealing) item.Conceal();
            }
        }

        /// <summary>비전투 시간으로 보상을 순차 공개한다. 화면을 연 클릭은 무시하며 공개 중 클릭은 남은 보상만 즉시 표시한다.</summary>
        public void AdvanceRewards(float deltaTime)
        {
            Mouse mouse = Mouse.current;
            if (_isWaitingForRelease)
            {
                bool hasMouseInput = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed
                    || mouse.leftButton.wasReleasedThisFrame || mouse.rightButton.wasReleasedThisFrame || mouse.middleButton.wasReleasedThisFrame);
                if (Time.frameCount <= _revealCompletedFrame || hasMouseInput) return;
                _isWaitingForRelease = false;
                SetNavigationEnabled(!_clearChoicePopup.activeSelf);
            }
            if (!_isRevealing || Time.frameCount <= _revealStartedFrame) return;
            bool skip = mouse != null && (mouse.leftButton.wasPressedThisFrame
                || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame);
            _revealElapsed += Mathf.Max(0, deltaTime);
            while (_revealedCount < _rewardViews.Count && (skip || _revealElapsed >= _revealedCount * _rewardRevealInterval))
            {
                _rewardViews[_revealedCount].Reveal(!skip, GetClearModifierGradeColor(_rewardContents[_revealedCount].Grade));
                _revealedCount++;
            }
            if (_revealedCount < _rewardViews.Count) return;
            if (_revealFinishedAt < 0) _revealFinishedAt = _revealElapsed + _rewardRevealFinishDelay;
            if (!skip && _revealElapsed < _revealFinishedAt) return;
            if (!skip)
                foreach (ResultRewardItemView item in _rewardViews)
                    if (item.IsEffectPlaying) return;
            if (skip)
                foreach (ResultRewardItemView item in _rewardViews) item.CompleteEffect();
            _isRevealing = false;
            _isWaitingForRelease = true;
            _revealCompletedFrame = Time.frameCount;
            SetNavigationEnabled(false);
        }

        /// <summary>정산 화면 이동 버튼을 보상 공개 및 보스 선택 상태에 맞게 활성화한다.</summary>
        private void SetNavigationEnabled(bool isEnabled)
        {
            _nextStageButton.interactable = isEnabled;
            _improveButton.interactable = isEnabled;
            _retryButton.interactable = isEnabled;
        }

        /// <summary>화면을 숨길 때 진행 중인 공개 상태와 툴팁을 정리한다.</summary>
        protected override void OnHidden()
        {
            _isRevealing = false;
            _isWaitingForRelease = false;
            HideRewardTooltip();
        }

        /// <summary>보상 아이콘에 포인터가 올라오면 이름·설명을 보여 주고 벗어나면 닫는다.</summary>
        private void HandleRewardHover(ResultRewardItemView item, bool isHovered)
        {
            if (!isHovered)
            {
                if (_hoveredRewardItem == item) HideRewardTooltip();
                return;
            }
            _hoveredRewardItem = item;
            _rewardTooltipTitle.richText = true;
            _rewardTooltipDescription.richText = true;
            _rewardTooltipTitle.text = item.Title;
            _rewardTooltipDescription.text = item.Description;
            _rewardTooltip.SetActive(true);
            PositionRewardTooltip(item);
        }

        /// <summary>현재 보상 정보 툴팁을 닫는다.</summary>
        private void HideRewardTooltip()
        {
            _hoveredRewardItem = null;
            _rewardTooltip.SetActive(false);
        }

        /// <summary>포인터가 올라온 아이콘 옆에 툴팁을 배치하고 화면 패널 경계 안으로 맞춘다.</summary>
        private void PositionRewardTooltip(ResultRewardItemView item)
        {
            RectTransform tooltipRect = (RectTransform)_rewardTooltip.transform;
            RectTransform parent = (RectTransform)tooltipRect.parent;
            ((RectTransform)item.transform).GetWorldCorners(_tooltipCorners);
            Vector2 itemTopRight = parent.InverseTransformPoint(_tooltipCorners[2]);
            Vector2 itemTopLeft = parent.InverseTransformPoint(_tooltipCorners[1]);
            float width = tooltipRect.rect.width;
            float height = tooltipRect.rect.height;
            float x = itemTopRight.x + 10f;
            if (x + width > parent.rect.xMax - 12f) x = itemTopLeft.x - width - 10f;
            float y = itemTopRight.y;
            if (y - height < parent.rect.yMin + 12f) y = itemTopRight.y + height + 10f;
            x = Mathf.Clamp(x, parent.rect.xMin + 12f, parent.rect.xMax - width - 12f);
            y = Mathf.Clamp(y, parent.rect.yMin + height + 12f, parent.rect.yMax - 12f);
            tooltipRect.anchoredPosition = new Vector2(x - parent.rect.xMin, y - parent.rect.yMax);
        }

        /// <summary>클리어 Modifier 후보 이름·효과와 후보별 등급(grades, 후보와 같은 순서)을 팝업 카드에 표시하고 선택 입력을 받는다.</summary>
        public void ShowClearModifierChoices(string title, IReadOnlyList<string> names, IReadOnlyList<string> descriptions, IReadOnlyList<string> grades)
        {
            HideRewardTooltip();
            _clearChoicePopup.SetActive(true);
            _clearChoiceTitleLabel.text = title;
            _rewardTitleLabel.gameObject.SetActive(false);
            _nextStageButton.interactable = false;
            _improveButton.interactable = false;
            _retryButton.interactable = false;
            GameObject firstChoice = null;
            for (int i = 0; i < _clearChoiceButtons.Length; i++)
            {
                bool hasChoice = names != null && i < names.Count;
                _clearChoiceCards[i].SetActive(hasChoice);
                if (!hasChoice) continue;
                if (firstChoice == null) firstChoice = _clearChoiceButtons[i].gameObject;
                _clearChoiceButtons[i].interactable = true;
                string grade = grades != null && i < grades.Count ? grades[i] : string.Empty;
                Color gradeColor = GetClearModifierGradeColor(grade);
                _clearChoiceGradeLabels[i].text = grade + "급";
                _clearChoiceGradeLabels[i].color = gradeColor;
                _clearChoiceNameLabels[i].richText = true;
                _clearChoiceDescriptionLabels[i].richText = true;
                _clearChoiceNameLabels[i].text = names[i];
                _clearChoiceDescriptionLabels[i].text = descriptions != null && i < descriptions.Count ? descriptions[i] : string.Empty;
            }
            if (EventSystem.current != null && firstChoice != null)
                EventSystem.current.SetSelectedGameObject(firstChoice);
        }

        /// <summary>클리어 Modifier 선택 팝업을 닫고 결과 버튼을 다시 활성화한다.</summary>
        public void HideClearModifierChoices()
        {
            _clearChoicePopup.SetActive(false);
            _rewardTitleLabel.gameObject.SetActive(true);
            for (int i = 0; i < _clearChoiceButtons.Length; i++)
            {
                _clearChoiceCards[i].SetActive(false);
                _clearChoiceButtons[i].interactable = true;
            }
            SetNavigationEnabled(!_isRevealing && !_isWaitingForRelease);
            if (EventSystem.current != null && gameObject.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(_improveButton.gameObject);
        }

        /// <summary>grade에 대응하는 보상 카드 등급 표시 색상을 반환한다.</summary>
        private static Color GetClearModifierGradeColor(string grade)
        {
            switch (grade)
            {
                case "C": return new Color(0.75f, 0.78f, 0.80f, 1f);
                case "B": return new Color(0.24f, 0.84f, 0.94f, 1f);
                case "A": return new Color(0.66f, 0.40f, 1f, 1f);
                case "S": return new Color(1f, 0.765f, 0.365f, 1f);
                default: return Color.white;
            }
        }
    }
}
