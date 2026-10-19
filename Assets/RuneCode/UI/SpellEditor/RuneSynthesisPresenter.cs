using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>팔레트 옆 합성창의 재료 예약·슬롯 선택·결과 안내와 편집 입력 잠금을 관리한다.</summary>
    public sealed class RuneSynthesisPresenter : IDisposable
    {
        private const float PALETTE_BUTTON_SPACE = 42f;
        private const float AUTOMATIC_BUTTON_SPACE = 58f;
        private readonly ISpellEditor _editor;
        private readonly ISpellEditorHost _host;
        private readonly UIManager _ui;
        private readonly SpellPalettePresenter _palette;
        private readonly SpellEditorPanel _view;
        private readonly RectTransform _blocker;
        private readonly Button _openButton;
        private readonly Button _synthesizeButton;
        private readonly Button _autoSelectButton;
        private readonly Button _emptySlotTemplate;
        private readonly Button[] _slotButtons = new Button[RuneSynthesisRules.MATERIAL_COUNT];
        private readonly string[] _materials = new string[RuneSynthesisRules.MATERIAL_COUNT];
        private readonly TextMeshProUGUI _hint;
        private readonly RectTransform _resultOverlay;
        private readonly RectTransform _resultContent;
        private readonly Dictionary<string, UiRow> _resultRows = new Dictionary<string, UiRow>();
        private string _category;
        private string _grade;
        private bool _isProcessing;
        private QuickPalettePopup _picker;

        public bool IsOpen => _blocker.gameObject.activeSelf;

        /// <summary>기존 UI 프리팹을 재사용해 팔레트 하단 버튼, 편집 차단 영역과 삼각형 슬롯을 생성한다.</summary>
        public RuneSynthesisPresenter(ISpellEditor editor, ISpellEditorHost host, UIManager ui,
            SpellEditorPanel view, SpellPalettePresenter palette)
        {
            _editor = editor;
            _host = host;
            _ui = ui;
            _palette = palette;
            _view = view;
            UiFactory factory = view.CreateSynthesisUi(out RectTransform paletteRect, out RectTransform scroll);
            _emptySlotTemplate = factory.ButtonPrefab.GetComponent<Button>();
            scroll.offsetMin += new Vector2(0f, PALETTE_BUTTON_SPACE);
            _openButton = factory.Button(paletteRect, 10, 0, 170, 30, "룬 합성", Open, 13, "OpenRuneSynthesis");
            RectTransform openRect = (RectTransform)_openButton.transform;
            openRect.anchorMin = new Vector2(0f, 0f);
            openRect.anchorMax = new Vector2(1f, 0f);
            openRect.pivot = new Vector2(0.5f, 0f);
            openRect.anchoredPosition = new Vector2(0f, 8f);
            openRect.sizeDelta = new Vector2(-20f, 30f);

            _blocker = UiFactory.Rect(view.transform, "RuneSynthesisBlocker", 0, 0, 0, 0);
            Stretch(_blocker);
            _blocker.offsetMin = new Vector2(paletteRect.anchoredPosition.x + paletteRect.rect.width, 0f);
            Image shade = _blocker.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.65f);
            shade.raycastTarget = true;
            RectTransform panel = factory.Panel(_blocker, 0, 40, 400, 400 + AUTOMATIC_BUTTON_SPACE, "RuneSynthesis");
            panel.GetComponent<Image>().raycastTarget = true;
            factory.Text(panel, 16, 16, 270, 26, "룬 합성", 20);
            Button closeButton = factory.Button(panel, 320, 14, 64, 28, "닫기", Close, 12);
            UiFactory.ApplyDestructiveButtonStyle(closeButton);
            factory.Text(panel, 16, 52, 368, 58, "행동·모드: C·B·A 승급, S는 행동/모드 S로 변환\n속성 3개: 선택한 스테이지 확률로 행동/모드 생성", 12);
            _autoSelectButton = factory.Button(panel, 145, 116, 110, 30, "자동 선택", AutoSelectMaterials, 13, "AutoSelectMaterials");
            Vector2[] positions = { new Vector2(145, 100), new Vector2(20, 260), new Vector2(270, 260) };
            for (int index = 0; index < _slotButtons.Length; index++)
            {
                int slot = index;
                _slotButtons[index] = factory.Button(panel, positions[index].x, positions[index].y + AUTOMATIC_BUTTON_SPACE,
                    110, 58, "비어있음", () => OpenSlotPicker(slot), 12, "SynthesisSlot" + index);
                EventTrigger trigger = _slotButtons[index].gameObject.AddComponent<EventTrigger>();
                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                entry.callback.AddListener(data => OnSlotPointerClick(slot, data));
                trigger.triggers.Add(entry);
            }
            _synthesizeButton = factory.Button(panel, 145, 216 + AUTOMATIC_BUTTON_SPACE, 110, 40, "합성\n" + RuneSynthesisRules.SCRAP_COST + " 스크랩", Synthesize, 13, "Synthesize");
            _hint = factory.Text(panel, 16, 336 + AUTOMATIC_BUTTON_SPACE, 368, 56, "", 12);
            _hint.richText = true;

            Canvas canvas = view.GetComponentInParent<Canvas>().rootCanvas;
            _resultOverlay = UiFactory.Rect(canvas.transform, "RuneSynthesisResult", 0, 0, 0, 0);
            Stretch(_resultOverlay);
            Image resultShade = _resultOverlay.gameObject.AddComponent<Image>();
            resultShade.color = new Color(0f, 0f, 0f, 0.85f);
            Button dismiss = _resultOverlay.gameObject.AddComponent<Button>();
            UiFactory.BindClickSound(dismiss);
            dismiss.targetGraphic = resultShade;
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(DismissResult);
            _resultContent = UiFactory.Rect(_resultOverlay, "SynthesisResultContent", 0, 0, 320, 150);
            _resultContent.pivot = new Vector2(0.5f, 0.5f);
            _resultContent.anchorMin = _resultContent.anchorMax = new Vector2(0.5f, 0.5f);
            factory.Text(_resultContent, 0, 0, 320, 28, "합성 완료", 18).alignment = TextAlignmentOptions.Center;
            string[] resultCategories = { SpellGrammar.CATEGORY_BEHAVIOR, SpellGrammar.CATEGORY_MODIFIER };
            foreach (string category in resultCategories)
            {
                UiRow row = UnityEngine.Object.Instantiate(view.GetSynthesisRowPrefab(category), _resultContent, false);
                row.name = "SynthesisResult" + category;
                RectTransform rect = (RectTransform)row.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(75f, -40f);
                rect.sizeDelta = new Vector2(170f, SpellPalettePresenter.ROW_HEIGHT);
                row.Label.font = view.Font;
                row.gameObject.SetActive(false);
                _resultRows.Add(category, row);
            }
            factory.Text(_resultContent, 0, 108, 320, 40, "아무 곳이나 클릭하면 닫힙니다.", 14).alignment = TextAlignmentOptions.Center;
            _resultOverlay.gameObject.SetActive(false);
            _blocker.gameObject.SetActive(false);
            RefreshAvailability();
        }

        /// <summary>일반 세션·테스트 세션 전환에 맞춰 합성 진입 버튼의 사용 가능 상태를 갱신한다.</summary>
        public void RefreshAvailability()
        {
            _openButton.interactable = _host.CanSynthesizeRunes;
            _openButton.GetComponentInChildren<TextMeshProUGUI>().text = _host.CanSynthesizeRunes ? "룬 합성" : "룬 합성 (테스트 제한)";
        }

        /// <summary>편집 내용을 저장하고 합성 예약을 시작하며 그래프 입력을 잠근다.</summary>
        public void Open()
        {
            if (IsOpen || !_host.CanSynthesizeRunes || _ui.IsPopupOpen) return;
            _editor.Save();
            _editor.SetEditable(false);
            _view.SetSynthesisEditingLocked(true);
            _blocker.gameObject.SetActive(true);
            Array.Clear(_materials, 0, _materials.Length);
            _category = null;
            _grade = null;
            _palette.BeginSynthesis(AddFirstEmpty, ReservedCount);
            RefreshSlots();
        }

        /// <summary>선택 팝업과 결과 안내·예약을 해제하고 팔레트 및 그래프 입력을 복구한다. 취소는 재고를 변경하지 않는다.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            _picker?.Close();
            _resultOverlay.gameObject.SetActive(false);
            _blocker.gameObject.SetActive(false);
            Array.Clear(_materials, 0, _materials.Length);
            _category = null;
            _grade = null;
            _palette.EndSynthesis();
            _view.SetSynthesisEditingLocked(false);
            _editor.SetEditable(true);
        }

        /// <summary>결과 안내가 있으면 초기화하고 그 외에는 합성창을 닫아 Esc를 소비한다.</summary>
        public bool HandleEscape()
        {
            if (!IsOpen) return false;
            if (_resultOverlay.gameObject.activeSelf) DismissResult();
            else Close();
            return true;
        }

        /// <summary>편집 패널 수명 밖 Canvas에 생성한 결과 차단 UI를 제거한다.</summary>
        public void Dispose()
        {
            if (_resultOverlay != null) UnityEngine.Object.Destroy(_resultOverlay.gameObject);
        }

        /// <summary>RectTransform을 부모의 전체 영역에 맞춘다.</summary>
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>팔레트 또는 빈 슬롯 템플릿의 배경 스프라이트·색·버튼 상태색을 슬롯에 적용한다.</summary>
        private static void ApplySlotStyle(Button slot, Button template)
        {
            Image target = (Image)slot.targetGraphic;
            Image source = (Image)template.targetGraphic;
            target.sprite = source.sprite;
            target.color = source.color;
            target.type = source.type;
            target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
            slot.colors = template.colors;
        }

        /// <summary>지정 룬·등급에 대해 현재 슬롯들이 예약한 수량을 반환한다.</summary>
        private int ReservedCount(string runeId, string grade)
        {
            if (grade != _grade) return 0;
            int count = 0;
            foreach (string material in _materials) if (material == runeId) count++;
            return count;
        }

        /// <summary>미사용 행동·모드를 C부터 S까지 먼저 찾고 모두 불가능하면 속성 3개를 찾는다. 기존 슬롯 예약은 교체하므로 차감하지 않는다.</summary>
        private bool TryFindAutomaticMaterials(out string[] materials, out string grade)
        {
            materials = null;
            grade = null;
            foreach (string candidateGrade in RuneSynthesisRules.GradeOrder)
            {
                var byCategory = new Dictionary<string, List<string>>();
                var categoryOrder = new List<string>();
                foreach (RuneDefinition rune in GameData.Runes.All)
                {
                    if (!RuneSynthesisRules.IsEligible(rune, candidateGrade)) continue;
                    if (!byCategory.TryGetValue(rune.Category, out List<string> candidates))
                    {
                        candidates = new List<string>(RuneSynthesisRules.MATERIAL_COUNT);
                        byCategory.Add(rune.Category, candidates);
                        categoryOrder.Add(rune.Category);
                    }
                    if (candidates.Count == RuneSynthesisRules.MATERIAL_COUNT
                        || !_editor.TryGetUnusedStock(rune.Id, candidateGrade, out int unused, out int owned)) continue;
                    int available = Math.Min(unused, RuneSynthesisRules.MATERIAL_COUNT - candidates.Count);
                    for (int index = 0; index < available; index++) candidates.Add(rune.Id);
                }
                foreach (string category in categoryOrder)
                {
                    List<string> candidates = byCategory[category];
                    if (candidates.Count != RuneSynthesisRules.MATERIAL_COUNT
                        || RuneSynthesisRules.GetResults(category, RuneSynthesisRules.NextGrade(candidateGrade)).Count == 0) continue;
                    materials = candidates.ToArray();
                    grade = candidateGrade;
                    return true;
                }
            }
            var elements = new List<string>(RuneSynthesisRules.MATERIAL_COUNT);
            foreach (string runeId in RuneSynthesisRules.GetResults(SpellGrammar.CATEGORY_ELEMENT, ""))
            {
                if (!_editor.TryGetUnusedStock(runeId, "", out int unused, out int owned)) continue;
                int elementAvailable = Math.Min(unused, RuneSynthesisRules.MATERIAL_COUNT - elements.Count);
                for (int index = 0; index < elementAvailable; index++) elements.Add(runeId);
                if (elements.Count != RuneSynthesisRules.MATERIAL_COUNT) continue;
                materials = elements.ToArray();
                grade = "";
                return true;
            }
            return false;
        }

        /// <summary>합성 가능한 우선순위의 재료를 모두 확보한 뒤 기존 슬롯을 한 번에 교체하고 팔레트·슬롯 표시를 갱신한다.</summary>
        private void AutoSelectMaterials()
        {
            if (!IsOpen || _isProcessing || _resultOverlay.gameObject.activeSelf || _ui.IsPopupOpen || !_host.CanSynthesizeRunes) return;
            if (!TryFindAutomaticMaterials(out string[] materials, out string grade))
            {
                _autoSelectButton.interactable = false;
                _hint.text = "자동 선택할 수 있는 미사용 재료 3개가 없습니다.";
                return;
            }
            Array.Copy(materials, _materials, _materials.Length);
            _grade = grade;
            _category = GameData.Runes.Get(_materials[0]).Category;
            _view.SetSearchQuery("");
            _palette.SetSynthesisFilter(_category, _grade);
            RefreshSlots();
        }

        /// <summary>팔레트에서 선택한 재료를 첫 빈 슬롯에 넣는다. 이미 가득 찼으면 안내한다.</summary>
        private void AddFirstEmpty(string runeId, string grade)
        {
            int index = Array.FindIndex(_materials, string.IsNullOrEmpty);
            if (index < 0) { _hint.text = "슬롯이 가득 찼습니다. 좌클릭으로 교체하거나 우클릭으로 비우세요."; return; }
            SelectMaterial(index, runeId, grade);
        }

        /// <summary>분류·등급·미사용 수량을 검사하고 지정 슬롯을 예약하거나 비운다.</summary>
        private void SelectMaterial(int index, string runeId, string grade)
        {
            if (!IsOpen || _isProcessing || _resultOverlay.gameObject.activeSelf) return;
            if (runeId != null)
            {
                RuneDefinition rune = GameData.Runes.Get(runeId);
                if (!RuneSynthesisRules.IsEligible(rune, grade)
                    || (_category != null && (_category != rune.Category || _grade != grade))) return;
                int reserved = ReservedCount(runeId, grade) - (_materials[index] == runeId ? 1 : 0);
                if (!_editor.TryGetUnusedStock(runeId, grade, out int unused, out int owned) || unused <= reserved) return;
                _category = rune.Category;
                _grade = grade;
            }
            _materials[index] = runeId;
            if (Array.TrueForAll(_materials, string.IsNullOrEmpty)) { _category = null; _grade = null; }
            _palette.SetSynthesisFilter(_category, _grade);
            RefreshSlots();
        }

        /// <summary>지정 슬롯의 우클릭이면 선택 팝업 없이 재료 예약을 해제하고 수량·필터·표시를 갱신한다.</summary>
        private void OnSlotPointerClick(int index, BaseEventData data)
        {
            if (data is PointerEventData pointer && pointer.button == PointerEventData.InputButton.Right
                && !_ui.IsPopupOpen && _materials[index] != null)
                SelectMaterial(index, null, null);
        }

        /// <summary>기존 빠른 룬 선택창에 슬롯 교체와 비우기를 연결하고 현재 선택 제약을 유지한다.</summary>
        private void OpenSlotPicker(int index)
        {
            if (!IsOpen || _resultOverlay.gameObject.activeSelf || _ui.IsPopupOpen) return;
            QuickPalettePopup popup = _ui.OpenPopup<QuickPalettePopup>();
            _picker = popup;
            popup.Begin(_category != SpellGrammar.CATEGORY_ELEMENT);
            popup.SetSynthesisGradeFilter(_grade);
            string gradeFilter = _grade;
            Action<string> search = query => FillPicker(popup, index, query, gradeFilter);
            Action<string> filter = grade =>
            {
                if (_grade != null || RuneSynthesisRules.NextGrade(grade) == null) return;
                gradeFilter = gradeFilter == grade ? null : grade;
                popup.ShowGrade(gradeFilter);
                FillPicker(popup, index, popup.Query, gradeFilter);
            };
            popup.SearchChanged += search;
            popup.GradeClicked += filter;
            popup.Closed += () =>
            {
                popup.SearchChanged -= search;
                popup.GradeClicked -= filter;
                _picker = null;
            };
            popup.ShowGrade(gradeFilter);
            FillPicker(popup, index, popup.Query, gradeFilter);
        }

        /// <summary>슬롯 재료와 현재 예약을 제외한 미사용 재고를 검색·등급별로 표시한다. 현재 슬롯의 수량은 교체에 재사용할 수 있다.</summary>
        private void FillPicker(QuickPalettePopup popup, int index, string query, string gradeFilter)
        {
            popup.ClearResults();
            popup.AddResult("비어있음", () => { popup.Close(); SelectMaterial(index, null, null); });
            foreach (RuneDefinition rune in GameData.Runes.All)
            {
                if ((_category != null && rune.Category != _category) || !SpellPalettePresenter.MatchesSearch(rune, query)) continue;
                foreach (string grade in SpellPalettePresenter.StockGrades(rune))
                {
                    if ((gradeFilter != null && grade != gradeFilter) || (_grade != null && grade != _grade)
                        || !RuneSynthesisRules.IsEligible(rune, grade)) continue;
                    if (!_editor.TryGetUnusedStock(rune.Id, grade, out int unused, out int owned)) continue;
                    int remaining = unused - ReservedCount(rune.Id, grade) + (_materials[index] == rune.Id ? 1 : 0);
                    if (remaining <= 0) continue;
                    string runeId = rune.Id;
                    popup.AddResult(SpellPalettePresenter.StockLabel(rune, grade, remaining, owned),
                        () => { popup.Close(); SelectMaterial(index, runeId, grade); }, rune.Category);
                }
            }
        }

        /// <summary>예약한 재료와 잠금 조건을 표시하고 미사용 재고 3개와 스크랩 비용이 있을 때만 합성 버튼을 활성화한다.</summary>
        private void RefreshSlots()
        {
            bool hasEnoughScrap = _host.SynthesisScrap >= RuneSynthesisRules.SCRAP_COST;
            bool canSynthesize = !_isProcessing && _host.CanSynthesizeRunes && hasEnoughScrap;
            for (int index = 0; index < _materials.Length; index++)
            {
                string id = _materials[index];
                ApplySlotStyle(_slotButtons[index], id == null ? _emptySlotTemplate
                    : _view.GetSynthesisRowPrefab(GameData.Runes.Get(id).Category).Button);
                _slotButtons[index].GetComponentInChildren<TextMeshProUGUI>().text = id == null ? "비어있음"
                    : ModifierGradeText.FormatName(GameData.Runes.Get(id).Name, _grade);
                if (id == null || !_editor.TryGetUnusedStock(id, _grade, out int unused, out int owned)
                    || unused < ReservedCount(id, _grade)) canSynthesize = false;
            }
            _synthesizeButton.interactable = canSynthesize;
            bool hasAutomaticMaterials = TryFindAutomaticMaterials(out _, out _);
            _autoSelectButton.interactable = !_isProcessing && !_resultOverlay.gameObject.activeSelf
                && _host.CanSynthesizeRunes && hasAutomaticMaterials;
            _hint.text = _grade == null ? (hasAutomaticMaterials
                ? "사용 중인 룬은 재료에서 제외됩니다.\n자동 선택 또는 팔레트·슬롯을 눌러 선택하세요."
                : "자동 선택할 수 있는 미사용 재료 3개가 없습니다.\n행동·모드 또는 속성을 더 모으세요.")
                : _category == SpellGrammar.CATEGORY_ELEMENT
                    ? "속성 3개: " + RuneRewardRoller.GetRewardStage(_host.SynthesisStage) + "스테이지 확률로 행동/모드 생성\n좌클릭: 교체 / 우클릭: 비우기"
                    : (_category == SpellGrammar.CATEGORY_BEHAVIOR ? "행동 " : "모드 ") + _grade + "등급만 선택할 수 있습니다.\n좌클릭: 교체 / 우클릭: 비우기";
            if (!hasEnoughScrap)
                _hint.text = "스크랩이 부족합니다. 합성에는 " + RuneSynthesisRules.SCRAP_COST
                    + "개가 필요합니다. (보유: " + _host.SynthesisScrap + "개)";
        }

        /// <summary>세션에서 재고와 비용을 재검사해 합성·저장을 마치면 결과를 표시하고 다음 클릭을 소비한다.</summary>
        private void Synthesize()
        {
            if (!IsOpen || _isProcessing || !_synthesizeButton.interactable || _resultOverlay.gameObject.activeSelf) return;
            _isProcessing = true;
            _synthesizeButton.interactable = false;
            _autoSelectButton.interactable = false;
            string failure = null;
            try
            {
                if (!_host.TrySynthesizeRunes(_materials, _grade, out string resultId, out string resultGrade, out string error))
                {
                    failure = error;
                    return;
                }
                RuneDefinition resultRune = GameData.Runes.Get(resultId);
                foreach (KeyValuePair<string, UiRow> pair in _resultRows) pair.Value.gameObject.SetActive(pair.Key == resultRune.Category);
                _editor.TryGetUnusedStock(resultId, resultGrade, out int unused, out int owned);
                _resultRows[resultRune.Category].Configure(SpellPalettePresenter.StockLabel(resultRune, resultGrade, unused, owned),
                    SpellPalettePresenter.ROW_HEIGHT, DismissResult, 12);
                _resultOverlay.SetAsLastSibling();
                _resultOverlay.gameObject.SetActive(true);
                EventSystem.current.SetSelectedGameObject(_resultOverlay.gameObject);
                _resultContent.position = _synthesizeButton.transform.position
                    + _synthesizeButton.transform.TransformVector(new Vector3(55f, -20f, 0f));
                _palette.Refresh();
            }
            finally
            {
                _isProcessing = false;
                if (!_resultOverlay.gameObject.activeSelf)
                {
                    _palette.Refresh();
                    RefreshSlots();
                    _hint.text = failure;
                }
            }
        }

        /// <summary>결과 안내를 닫고 확정된 재료 예약과 잠금 필터를 비워 다음 합성을 준비한다.</summary>
        private void DismissResult()
        {
            if (!_resultOverlay.gameObject.activeSelf) return;
            _resultOverlay.gameObject.SetActive(false);
            EventSystem.current.SetSelectedGameObject(null);
            Array.Clear(_materials, 0, _materials.Length);
            _category = null;
            _grade = null;
            _view.SetSearchQuery("");
            _palette.SetSynthesisFilter(null, null);
            RefreshSlots();
        }
    }
}
