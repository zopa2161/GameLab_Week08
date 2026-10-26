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
        private const float REWARD_HEIGHT = 72f;
        private const float SCROLL_EPSILON = 0.5f;
        private const float REROLL_BUTTON_WIDTH = 224f;
        private const float REROLL_BUTTON_HEIGHT = 42f;
        private const float REROLL_BUTTON_RIGHT_MARGIN = 24f;
        private const float REROLL_BUTTON_TOP = 48f;

        [Header("보상 공개 연출")]
        [SerializeField, Min(0.05f)] private float _rewardRevealInterval = 0.3f;
        [SerializeField, Min(0)] private float _rewardRevealFinishDelay = 0.7f;
        [SerializeField, Min(1f)] private float _rewardScrollSpeed = 900f;
        [SerializeField, Min(0)] private float _rewardEffectMargin = 72f;

        [Header("문구")]
        [SerializeField] private TextMeshProUGUI _summaryLabel;
        [SerializeField] private TextMeshProUGUI _stageLabel;
        [SerializeField] private TextMeshProUGUI _progressLabel;

        [Header("획득 보상")]
        [SerializeField] private RectTransform _rewardItemsRoot;
        [SerializeField] private ScrollRect _rewardScroll;
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
        [SerializeField] private TextMeshProUGUI[] _clearChoiceTargetLabels;
        [SerializeField] private GameObject[] _clearChoiceModPanels;
        [SerializeField] private GameObject[] _clearChoiceBehaviorPanels;

        private bool _isBound;
        private GameObject _resultContent;
        private Image _background;
        private Canvas _rewardCanvas;
        private Image _elementChoiceBackground;
        private Image[] _choiceModBackgrounds;
        private Sprite[] _choiceModSprites;
        private Color[] _choiceModColors;
        private ResultRewardItemView _hoveredRewardItem;
        private readonly Vector3[] _tooltipCorners = new Vector3[4];
        private readonly List<ResultRewardItemView> _rewardViews = new List<ResultRewardItemView>();
        private readonly List<ResultRewardContent> _rewardContents = new List<ResultRewardContent>();
        private int _revealedCount;
        private int _revealCompletedFrame;
        private int _revealStartedFrame;
        private float _revealElapsed;
        private int _focusedRewardIndex = -1;
        private bool _isRevealing;
        private bool _isWaitingForRelease;
        private bool _isCompletionOnly;
        private bool _hasNextStage;
        private Button _rerollChoiceButton;
        private TextMeshProUGUI _rerollChoiceLabel;
        private bool _canRerollChoice;
        private int _choiceInputReadyFrame;

        public bool CanContinue => !_isRevealing && !_isWaitingForRelease && Time.frameCount > _revealCompletedFrame;

        /// <summary>마법 개선(작업실로 이동)을 눌렀을 때 알린다.</summary>
        public event Action ImproveClicked;

        /// <summary>승리 후 다음 스테이지 준비를 선택했을 때 알린다.</summary>
        public event Action NextStageClicked;

        /// <summary>같은 스테이지 재도전을 눌렀을 때 알린다.</summary>
        public event Action RetryClicked;

        /// <summary>클리어 Modifier 선택 후보 버튼을 눌렀을 때 후보 순번으로 알린다.</summary>
        public event Action<int> ClearModifierChoiceClicked;

        /// <summary>보상 후보 전체의 무료 재추첨을 요청했을 때 알린다.</summary>
        public event Action ClearChoiceRerollClicked;

        /// <summary>버튼 클릭을 처음 한 번 연결하고 속성 팔레트 배경과 원래 모드 카드 배경을 캐싱한다.</summary>
        public void Bind()
        {
            if (_isBound) return;
            _elementChoiceBackground = (Image)Resources.Load<UiRow>("RuneCode/UI/Elements/RunePaletteElementRow").Button.targetGraphic;
            _choiceModBackgrounds = new Image[_clearChoiceModPanels.Length];
            _choiceModSprites = new Sprite[_clearChoiceModPanels.Length];
            _choiceModColors = new Color[_clearChoiceModPanels.Length];
            for (int index = 0; index < _clearChoiceModPanels.Length; index++)
            {
                Image background = _clearChoiceModPanels[index].GetComponent<Image>();
                _choiceModBackgrounds[index] = background;
                _choiceModSprites[index] = background.sprite;
                _choiceModColors[index] = background.color;
            }
            _background = GetComponent<Image>();
            Transform content = _summaryLabel.transform;
            while (content.parent != transform) content = content.parent;
            _resultContent = content.gameObject;
            _rewardCanvas = _rewardScroll.GetComponentInParent<Canvas>().rootCanvas;
            _rewardScroll.onValueChanged.AddListener(HandleRewardScroll);
            CreateRerollChoiceButton();
            _nextStageButton.onClick.AddListener(() => { if (CanContinue && _hasNextStage && !_isCompletionOnly) NextStageClicked?.Invoke(); });
            _improveButton.onClick.AddListener(() => { if (CanContinue && !_isCompletionOnly) ImproveClicked?.Invoke(); });
            _retryButton.onClick.AddListener(() => { if (CanContinue && !_isCompletionOnly) RetryClicked?.Invoke(); });
            for (int i = 0; i < _clearChoiceButtons.Length; i++)
            {
                int index = i;
                _clearChoiceButtons[i].onClick.AddListener(() =>
                {
                    if (CanContinue && Time.frameCount > _choiceInputReadyFrame) ClearModifierChoiceClicked?.Invoke(index);
                });
            }
            _isBound = true;
        }

        /// <summary>기존 결과 버튼 모양을 재사용해 보상 제목 오른쪽에 리롤 버튼을 만들고 전용 클릭 이벤트를 연결한다.</summary>
        private void CreateRerollChoiceButton()
        {
            _rerollChoiceButton = Instantiate(_nextStageButton, _clearChoiceTitleLabel.transform.parent, false);
            _rerollChoiceButton.name = "RerollChoiceButton";
            _rerollChoiceButton.onClick = new Button.ButtonClickedEvent();
            UiFactory.BindClickSound(_rerollChoiceButton);
            RectTransform rect = (RectTransform)_rerollChoiceButton.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-REROLL_BUTTON_RIGHT_MARGIN, -REROLL_BUTTON_TOP);
            rect.sizeDelta = new Vector2(REROLL_BUTTON_WIDTH, REROLL_BUTTON_HEIGHT);
            _rerollChoiceLabel = _rerollChoiceButton.GetComponentInChildren<TextMeshProUGUI>(true);
            _rerollChoiceLabel.fontSize = 16f;
            _rerollChoiceButton.onClick.AddListener(() =>
            {
                if (!_canRerollChoice || !CanContinue || Time.frameCount <= _choiceInputReadyFrame) return;
                _canRerollChoice = false;
                _rerollChoiceButton.interactable = false;
                ClearChoiceRerollClicked?.Invoke();
            });
            _rerollChoiceButton.gameObject.SetActive(true);
        }

        /// <summary>리롤 가능 여부와 사용 완료·대체 후보 없음 상태를 문구와 버튼에 반영하고 이번 프레임의 카드 입력을 막는다.</summary>
        public void SetClearChoiceReroll(bool canReroll, bool hasRerolled, bool isPreview)
        {
            _canRerollChoice = canReroll;
            _rerollChoiceButton.gameObject.SetActive(!isPreview);
            _rerollChoiceButton.interactable = canReroll;
            _rerollChoiceLabel.text = GameData.L(hasRerolled ? "result.rerollUsed"
                : canReroll ? "result.rerollAvailable" : "result.rerollUnavailable");
            _choiceInputReadyFrame = Time.frameCount;
        }

        /// <summary>결과 문구와 승리별 버튼을 설정한다. isChoiceOnly이면 결과 본문과 불투명 배경을 숨겨 선택 팝업의 어두운 배경만 사용한다.</summary>
        public void SetContent(string summary, string stage, string progress, bool isVictory, bool isProgressionMission = false,
            bool isChoiceOnly = false)
        {
            _resultContent.SetActive(!isChoiceOnly);
            _background.enabled = !isChoiceOnly;
            _summaryLabel.richText = true;
            _summaryLabel.text = summary;
            _stageLabel.text = stage;
            _progressLabel.text = progress;
            _isCompletionOnly = isProgressionMission && isVictory;
            _hasNextStage = isVictory && !isProgressionMission;
            _winOnlyButtonRoot.SetActive(_hasNextStage);
            _improveButton.gameObject.SetActive(!_isCompletionOnly);
            _retryButton.gameObject.SetActive(!_isCompletionOnly);
            SetNavigationEnabled(CanContinue && !_clearChoicePopup.activeSelf);
        }

        /// <summary>정렬된 보상 목록을 만들고 animate이면 순차 공개를 준비하며, 아니면 연출 없이 모두 표시한다.</summary>
        public void SetRewards(IReadOnlyList<ResultRewardContent> rewards, bool animate = false)
        {
            float previousPosition = _rewardItemsRoot.anchoredPosition.x;
            bool canPreservePosition = !animate && IsVisible;
            HideRewardTooltip();
            UiFactory.ClearChildren(_rewardItemsRoot);
            _rewardScroll.StopMovement();
            _focusedRewardIndex = -1;
            _rewardViews.Clear();
            _rewardContents.Clear();
            _revealedCount = 0;
            _revealElapsed = 0;
            _isWaitingForRelease = false;
            _revealStartedFrame = Time.frameCount;
            _revealCompletedFrame = Time.frameCount;
            _isRevealing = animate && rewards != null && rewards.Count > 0;
            SetNavigationEnabled(!_isRevealing);
            if (rewards != null)
            {
                foreach (ResultRewardContent reward in rewards)
                {
                    ResultRewardItemView item = Instantiate(_rewardItemPrefab, _rewardItemsRoot, false);
                    item.SetContent(reward, HandleRewardHover);
                    ((RectTransform)item.transform).sizeDelta = new Vector2(item.PreferredWidth, REWARD_HEIGHT);
                    _rewardViews.Add(item);
                    _rewardContents.Add(reward);
                    if (_isRevealing) item.Conceal();
                }
            }
            ResetRewardScroll();
            if (canPreservePosition)
            {
                float overflow = Mathf.Max(0, _rewardItemsRoot.rect.width - _rewardScroll.viewport.rect.width);
                Vector2 position = _rewardItemsRoot.anchoredPosition;
                position.x = Mathf.Clamp(previousPosition, -overflow, 0);
                _rewardItemsRoot.anchoredPosition = position;
            }
        }

        /// <summary>표시 직후 활성 레이아웃을 계산해 보상 목록을 왼쪽 시작 위치로 맞춘다.</summary>
        protected override void OnShown()
        {
            ResetRewardScroll();
        }

        /// <summary>보상 너비를 레이아웃에 반영하고 이전 결과의 관성과 스크롤 위치를 초기화한다.</summary>
        private void ResetRewardScroll()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rewardItemsRoot);
            _rewardScroll.StopMovement();
            _rewardScroll.horizontalNormalizedPosition = 0;
        }

        /// <summary>비전투 시간으로 보상을 화면 안에 이동한 뒤 공개한다. 클릭·목록 위 휠은 공개를 완료하고 수동 탐색으로 전환한다.</summary>
        public void AdvanceRewards(float deltaTime)
        {
            Mouse mouse = Mouse.current;
            bool hasScrollInput = mouse != null && mouse.scroll.ReadValue().sqrMagnitude > 0
                && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_rewardScroll.transform,
                    mouse.position.ReadValue(), _rewardCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _rewardCanvas.worldCamera);
            bool hasPointerPress = mouse != null && (mouse.leftButton.wasPressedThisFrame
                || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame);
            if (hasPointerPress || hasScrollInput) _focusedRewardIndex = -1;
            if (_focusedRewardIndex >= 0)
            {
                if (MoveRewardIntoView(_focusedRewardIndex, deltaTime))
                {
                    RevealReward(_focusedRewardIndex, true);
                    _focusedRewardIndex = -1;
                }
            }
            if (_isWaitingForRelease)
            {
                bool hasMouseInput = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed
                    || mouse.leftButton.wasReleasedThisFrame || mouse.rightButton.wasReleasedThisFrame || mouse.middleButton.wasReleasedThisFrame);
                if (Time.frameCount <= _revealCompletedFrame || hasMouseInput || hasScrollInput) return;
                _isWaitingForRelease = false;
                SetNavigationEnabled(!_clearChoicePopup.activeSelf);
            }
            if (!_isRevealing || Time.frameCount <= _revealStartedFrame) return;
            if (hasPointerPress || hasScrollInput)
            {
                while (_revealedCount < _rewardViews.Count) RevealReward(_revealedCount++, false);
                foreach (ResultRewardItemView item in _rewardViews) item.CompleteEffect();
                CompleteRewardReveal();
                return;
            }
            if (_revealedCount < _rewardViews.Count)
            {
                if (_revealElapsed < _rewardRevealInterval && _revealedCount > 0)
                {
                    _revealElapsed += Mathf.Max(0, deltaTime);
                    return;
                }
                if (!IsRewardInView(_revealedCount))
                    foreach (ResultRewardItemView item in _rewardViews)
                        if (item.IsEffectPlaying) return;
                if (!MoveRewardIntoView(_revealedCount, deltaTime)) return;
                RevealReward(_revealedCount++, true);
                _revealElapsed = 0;
                return;
            }
            _revealElapsed += Mathf.Max(0, deltaTime);
            if (_revealElapsed < _rewardRevealFinishDelay) return;
            foreach (ResultRewardItemView item in _rewardViews)
                if (item.IsEffectPlaying) return;
            CompleteRewardReveal();
        }

        /// <summary>지정 순번의 보상을 등급 색상과 선택한 연출 사용 여부로 공개한다.</summary>
        private void RevealReward(int index, bool playEffect)
        {
            _rewardViews[index].Reveal(playEffect, RuneMesh.GradeColor(_rewardContents[index].Grade));
        }

        /// <summary>순차 공개를 끝내고 현재 입력이 해제된 다음 프레임까지 이동 버튼을 잠근다.</summary>
        private void CompleteRewardReveal()
        {
            _isRevealing = false;
            _isWaitingForRelease = true;
            _revealCompletedFrame = Time.frameCount;
            SetNavigationEnabled(false);
        }

        /// <summary>카드와 효과 여백을 표시할 수 있도록 필요한 콘텐츠의 가로 위치를 계산한다.</summary>
        private float GetRewardScrollTarget(int index)
        {
            RectTransform item = (RectTransform)_rewardViews[index].transform;
            float viewWidth = _rewardScroll.viewport.rect.width;
            float current = -_rewardItemsRoot.anchoredPosition.x;
            float margin = Mathf.Min(_rewardEffectMargin, Mathf.Max(0, (viewWidth - item.rect.width) * 0.5f));
            float left = item.localPosition.x + item.rect.xMin - margin;
            float right = item.localPosition.x + item.rect.xMax + margin;
            float target = current;
            if (right > current + viewWidth) target = right - viewWidth;
            if (left < target) target = left;
            return -Mathf.Clamp(target, 0, Mathf.Max(0, _rewardItemsRoot.rect.width - viewWidth));
        }

        /// <summary>지정 보상이 효과 여백까지 현재 스크롤 영역 안에 있는지 반환한다.</summary>
        private bool IsRewardInView(int index)
        {
            return Mathf.Abs(_rewardItemsRoot.anchoredPosition.x - GetRewardScrollTarget(index)) <= SCROLL_EPSILON;
        }

        /// <summary>지정 보상이 보일 때까지 비전투 시간으로 가로 이동하고 이동 완료 여부를 반환한다.</summary>
        private bool MoveRewardIntoView(int index, float deltaTime)
        {
            _rewardScroll.StopMovement();
            Vector2 position = _rewardItemsRoot.anchoredPosition;
            float target = GetRewardScrollTarget(index);
            position.x = Mathf.MoveTowards(position.x, target, _rewardScrollSpeed * Mathf.Max(0, deltaTime));
            _rewardItemsRoot.anchoredPosition = position;
            return Mathf.Abs(position.x - target) <= SCROLL_EPSILON;
        }

        /// <summary>보스 선택으로 추가된 룬을 찾아 다음 갱신에서 해당 위치로 이동한 뒤 강조한다.</summary>
        public void FocusReward(string runeId, string grade)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rewardItemsRoot);
            for (int index = 0; index < _rewardContents.Count; index++)
            {
                ResultRewardContent reward = _rewardContents[index];
                if (reward.RuneId != runeId || reward.Grade != grade) continue;
                _focusedRewardIndex = index;
                return;
            }
        }

        /// <summary>스크롤 위치가 바뀌면 이전 카드에 남은 툴팁을 닫는다.</summary>
        private void HandleRewardScroll(Vector2 position)
        {
            HideRewardTooltip();
        }

        /// <summary>정산 화면 이동 버튼을 보상 공개 및 보스 선택 상태에 맞게 활성화한다.</summary>
        private void SetNavigationEnabled(bool isEnabled)
        {
            _improveButton.interactable = isEnabled && !_isCompletionOnly;
            _nextStageButton.interactable = isEnabled && _hasNextStage && !_isCompletionOnly;
            _retryButton.interactable = isEnabled && !_isCompletionOnly;
        }

        /// <summary>화면을 숨길 때 진행 중인 공개 상태와 툴팁을 정리한다.</summary>
        protected override void OnHidden()
        {
            _isRevealing = false;
            _isWaitingForRelease = false;
            _focusedRewardIndex = -1;
            _rewardScroll.StopMovement();
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
            if (_isRevealing || _focusedRewardIndex >= 0) return;
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

        /// <summary>후보 이름·효과·장착 가능 행동·등급을 카드에 표시하고 같은 순서의 runeIds로 행동·Modifier 패널을 전환해 선택 입력을 받는다.</summary>
        public void ShowClearModifierChoices(string title, IReadOnlyList<string> names, IReadOnlyList<string> descriptions,
            IReadOnlyList<string> targetTexts, IReadOnlyList<string> grades, IReadOnlyList<string> runeIds)
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
                string category = hasChoice ? GameData.Runes.Get(runeIds[i]).Category : string.Empty;
                _clearChoiceModPanels[i].SetActive(category == SpellGrammar.CATEGORY_MODIFIER || category == SpellGrammar.CATEGORY_ELEMENT);
                _clearChoiceBehaviorPanels[i].SetActive(category == SpellGrammar.CATEGORY_BEHAVIOR);
                if (!hasChoice) continue;
                if (firstChoice == null) firstChoice = _clearChoiceButtons[i].gameObject;
                _clearChoiceButtons[i].interactable = true;
                string grade = grades != null && i < grades.Count ? grades[i] : string.Empty;
                bool isElement = category == SpellGrammar.CATEGORY_ELEMENT;
                _choiceModBackgrounds[i].sprite = isElement ? _elementChoiceBackground.sprite : _choiceModSprites[i];
                _choiceModBackgrounds[i].color = isElement ? _elementChoiceBackground.color : _choiceModColors[i];
                Color gradeColor = RuneMesh.GradeColor(grade);
                _clearChoiceGradeLabels[i].text = isElement ? "속성" : grade + "급";
                _clearChoiceGradeLabels[i].color = gradeColor;
                _clearChoiceNameLabels[i].richText = true;
                _clearChoiceDescriptionLabels[i].richText = true;
                _clearChoiceDescriptionLabels[i].color = Color.white;
                _clearChoiceNameLabels[i].text = names[i];
                _clearChoiceDescriptionLabels[i].text = descriptions != null && i < descriptions.Count ? descriptions[i] : string.Empty;
                _clearChoiceTargetLabels[i].richText = true;
                _clearChoiceTargetLabels[i].text = targetTexts != null && i < targetTexts.Count ? targetTexts[i] : string.Empty;
            }
            if (EventSystem.current != null && firstChoice != null)
                EventSystem.current.SetSelectedGameObject(firstChoice);
        }

        /// <summary>클리어 Modifier 선택 팝업을 닫고 결과 버튼을 다시 활성화한다.</summary>
        public void HideClearModifierChoices()
        {
            _canRerollChoice = false;
            _clearChoicePopup.SetActive(false);
            _rewardTitleLabel.gameObject.SetActive(true);
            for (int i = 0; i < _clearChoiceButtons.Length; i++)
            {
                _clearChoiceCards[i].SetActive(false);
                _clearChoiceModPanels[i].SetActive(false);
                _clearChoiceBehaviorPanels[i].SetActive(false);
                _clearChoiceButtons[i].interactable = true;
            }
            SetNavigationEnabled(!_isRevealing && !_isWaitingForRelease);
            if (EventSystem.current != null && gameObject.activeInHierarchy && !_isCompletionOnly)
                EventSystem.current.SetSelectedGameObject(_improveButton.gameObject);
        }

    }
}
