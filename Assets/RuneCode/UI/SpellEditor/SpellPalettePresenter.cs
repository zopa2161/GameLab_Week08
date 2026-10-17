using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 룬 팔레트와 빠른 검색·슬롯 선택 팝업의 Presenter다. 분류·검색어로 행동·속성·모디를 골라 남은/보유 수와 함께 표시한다.
    /// 행동 행은 제안 위치(팔레트) 또는 더블클릭 위치(빠른 검색)에 배치하고, 속성·모디 행은 선택한 행동 노드의 빈 슬롯에 장착한다.
    /// 보유하지 않은 항목은 숨기고 남은 수가 0인 항목은 표시만 한다.
    /// </summary>
    public sealed class SpellPalettePresenter
    {
        public const float ROW_HEIGHT = 52f;

        // 속성은 등급이 없어 빈 등급 하나로 소지량을 조회한다.
        private static readonly string[] ELEMENT_GRADES = { "" };

        private readonly ISpellEditor _editor;
        private readonly UIManager _ui;
        private readonly SpellEditorPanel _view;
        private string _category = "all";
        private string _grade;
        private string _previousCategory;
        private string _previousGrade;
        private string _previousQuery;
        private string _lockedCategory;
        private string _lockedGrade;
        private Action<string, string> _selectMaterial;
        private Func<string, string, int> _reservedCount;

        /// <summary>편집 세션·UI 관리자·편집 View를 받아 검색어·분류 이벤트를 연결한다.</summary>
        public SpellPalettePresenter(ISpellEditor editor, UIManager ui, SpellEditorPanel view)
        {
            _editor = editor;
            _ui = ui;
            _view = view;
            _view.SearchChanged += _ => Refresh();
            _view.CategoryClicked += category =>
            {
                if (_selectMaterial != null && (_lockedCategory != null
                    || (category != "all" && category != SpellGrammar.CATEGORY_BEHAVIOR && category != SpellGrammar.CATEGORY_MODIFIER
                        && category != SpellGrammar.CATEGORY_ELEMENT))) return;
                _category = category;
                if (_selectMaterial != null && category == SpellGrammar.CATEGORY_ELEMENT) _grade = null;
                Refresh();
            };
            _view.GradeClicked += grade =>
            {
                if (_selectMaterial != null && (_lockedGrade != null || _category == SpellGrammar.CATEGORY_ELEMENT
                    || RuneSynthesisRules.NextGrade(grade) == null)) return;
                _grade = _grade == grade ? null : grade;
                Refresh();
            };
        }

        /// <summary>분류를 전체, 등급을 미선택으로 초기화한다. 버튼과 목록은 다음 Refresh에서 갱신한다.</summary>
        public void ResetFilters()
        {
            _category = "all";
            _grade = null;
        }

        /// <summary>기존 필터·검색을 보관하고 합성 재료 클릭과 예약 수량 조회를 연결한다.</summary>
        public void BeginSynthesis(Action<string, string> selectMaterial, Func<string, string, int> reservedCount)
        {
            _previousCategory = _category;
            _previousGrade = _grade;
            _previousQuery = _view.SearchQuery;
            _selectMaterial = selectMaterial;
            _reservedCount = reservedCount;
            _view.SetSearchQuery("");
            SetSynthesisFilter(null, null);
        }

        /// <summary>재료가 있으면 분류·등급을 잠그고 없으면 합성 가능한 모든 분류를 표시한다. 속성은 빈 등급으로 잠근다.</summary>
        public void SetSynthesisFilter(string category, string grade)
        {
            _lockedCategory = category;
            _lockedGrade = grade;
            _category = category ?? "all";
            _grade = grade;
            Refresh();
        }

        /// <summary>합성 클릭과 예약 수량을 해제하고 열기 전 필터·검색으로 복구한다.</summary>
        public void EndSynthesis()
        {
            _selectMaterial = null;
            _reservedCount = null;
            _lockedCategory = null;
            _lockedGrade = null;
            _category = _previousCategory;
            _grade = _previousGrade;
            _view.SetSearchQuery(_previousQuery);
            _view.SetSynthesisFilters(false, null, null);
            Refresh();
        }

        /// <summary>현재 분류·검색어에 맞는 보유 행동(등급별)·속성·모디(등급별)를 이름·등급과 남은/보유 수로 표시한다.</summary>
        public void Refresh()
        {
            _view.SetSynthesisFilters(_selectMaterial != null, _lockedCategory, _lockedGrade, _category == SpellGrammar.CATEGORY_ELEMENT);
            _view.ShowCategory(_category);
            _view.ShowGrade(_grade);
            _view.ClearPalette();
            string query = _view.SearchQuery;
            foreach (RuneDefinition rune in GameData.Runes.All)
            {
                if (!IsPaletteRune(rune) || !_editor.IsRuneUnlocked(rune.Id)
                    || (_category != "all" && rune.Category != _category) || !MatchesSearch(rune, query)) continue;
                foreach (string grade in StockGrades(rune))
                {
                    if (_selectMaterial != null && !RuneSynthesisRules.IsEligible(rune, grade)) continue;
                    if (_grade != null && grade != _grade) continue;
                    if (!TryGetStock(rune.Id, grade, out int remaining, out int owned)) continue;
                    if (_reservedCount != null) remaining = Math.Max(0, remaining - _reservedCount(rune.Id, grade));
                    AddPaletteRune(rune, grade, StockLabel(rune, grade, remaining, owned), remaining > 0);
                }
            }
        }

        /// <summary>빠른 검색 팝업을 열고, 고른 보유 행동을 지정 그래프 좌표에 지정 등급으로 배치하게 한다.</summary>
        public void OpenQuickPalette(Vector2 graphPosition)
        {
            if (_selectMaterial != null) return;
            QuickPalettePopup popup = _ui.OpenPopup<QuickPalettePopup>();
            popup.Begin();
            Action<string> onSearch = query => FillQuickPalette(popup, query, graphPosition);
            popup.SearchChanged += onSearch;
            popup.Closed += () => popup.SearchChanged -= onSearch;
            FillQuickPalette(popup, popup.Query, graphPosition);
        }

        /// <summary>
        /// 지정 속성·모디 슬롯의 선택 팝업을 연다. 장착 가능한 보유 룬과 비어 있음 선택지를 표시한다.
        /// </summary>
        public void OpenSlotPicker(string nodeId, string slotKey)
        {
            if (_selectMaterial != null) return;
            if (!SpellGrammar.TryParseSlotKey(slotKey, out bool isElement, out _)) return;
            QuickPalettePopup popup = _ui.OpenPopup<QuickPalettePopup>();
            popup.Begin(true);
            string category = isElement ? SpellGrammar.CATEGORY_ELEMENT : SpellGrammar.CATEGORY_MODIFIER;
            string selectedGrade = null;
            Action<string> onSearch = query => FillSlotPicker(popup, query, nodeId, slotKey, category, selectedGrade);
            Action<string> onGrade = grade =>
            {
                selectedGrade = selectedGrade == grade ? null : grade;
                popup.ShowGrade(selectedGrade);
                FillSlotPicker(popup, popup.Query, nodeId, slotKey, category, selectedGrade);
            };
            popup.SearchChanged += onSearch;
            popup.GradeClicked += onGrade;
            popup.Closed += () =>
            {
                popup.SearchChanged -= onSearch;
                popup.GradeClicked -= onGrade;
            };
            FillSlotPicker(popup, popup.Query, nodeId, slotKey, category, selectedGrade);
        }

        /// <summary>보유 행동 중 검색어와 일치하는 등급별 항목으로 빠른 검색 결과를 채운다.</summary>
        private void FillQuickPalette(QuickPalettePopup popup, string query, Vector2 graphPosition)
        {
            popup.ClearResults();
            foreach (RuneDefinition rune in GameData.Runes.All)
            {
                if (!IsPaletteRune(rune) || rune.Category != SpellGrammar.CATEGORY_BEHAVIOR
                    || !_editor.IsRuneUnlocked(rune.Id) || !MatchesSearch(rune, query)) continue;
                foreach (string grade in StockGrades(rune))
                {
                    if (!TryGetStock(rune.Id, grade, out int remaining, out int owned) || remaining <= 0) continue;
                    string runeId = rune.Id;
                    popup.AddResult(StockLabel(rune, grade, remaining, owned), () =>
                    {
                        popup.Close();
                        _view.GraphCanvas.PlaceRune(runeId, graphPosition, grade);
                    }, rune.Category);
                }
            }
        }

        /// <summary>지정 분류와 등급 필터에 맞는 장착 가능한 룬과 슬롯 비우기 선택지로 선택 결과를 채운다. gradeFilter가 null이면 모든 등급을 표시한다.</summary>
        private void FillSlotPicker(QuickPalettePopup popup, string query, string nodeId, string slotKey, string category,
            string gradeFilter)
        {
            popup.ClearResults();
            popup.AddResult(GameData.L("editor.slotEmpty"), () =>
            {
                popup.Close();
                _editor.ClearSlot(nodeId, slotKey);
            }, category);
            foreach (RuneDefinition rune in GameData.Runes.All)
            {
                if (!IsPaletteRune(rune) || rune.Category != category || !MatchesSearch(rune, query)) continue;
                foreach (string grade in StockGrades(rune))
                {
                    if (gradeFilter != null && grade != gradeFilter) continue;
                    if (!TryGetStock(rune.Id, grade, out int remaining, out int owned) || remaining <= 0
                        || !_editor.CanEquipToSlot(nodeId, slotKey, rune.Id, grade)) continue;
                    string runeId = rune.Id;
                    popup.AddResult(StockLabel(rune, grade, remaining, owned), () =>
                    {
                        popup.Close();
                        _editor.Equip(nodeId, slotKey, runeId, grade);
                    }, rune.Category);
                }
            }
        }

        /// <summary>
        /// 팔레트 행을 만든다. 행동 행은 누르면 제안 위치에 배치하고, 속성·모디 행은 선택한 행동 노드의 첫 빈 슬롯에 장착한다.
        /// canPlace가 false(남은 수 0)이면 드래그 배치를 막고 누르면 소지량 부족 문구를 띄운다.
        /// </summary>
        private void AddPaletteRune(RuneDefinition rune, string grade, string label, bool canPlace)
        {
            string runeId = rune.Id;
            Action onClick;
            if (!canPlace) onClick = () => _editor.ReportStatus("editor.stockBlocked");
            else if (_selectMaterial != null) onClick = () => _selectMaterial?.Invoke(runeId, grade);
            else if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR)
                onClick = () => _view.GraphCanvas.PlaceRune(runeId, _view.GraphCanvas.SuggestPlacement(runeId), grade);
            else onClick = () => EquipToSelected(runeId, grade);
            _view.AddPaletteRow(label, ROW_HEIGHT, onClick, runeId, grade, canPlace && _selectMaterial == null);
        }

        /// <summary>선택한 행동 노드 하나의 첫 빈 슬롯에 속성·모디를 장착한다. 선택이 없거나 빈 슬롯이 없으면 상태 문구를 띄운다.</summary>
        private void EquipToSelected(string runeId, string grade)
        {
            string nodeId = _view.GraphCanvas.SelectedBehaviorNodeId;
            if (nodeId == null)
            {
                _editor.ReportStatus("editor.selectBehavior");
                return;
            }
            if (!_editor.TryFindEquipSlot(nodeId, runeId, grade, out string slotKey))
            {
                _editor.ReportStatus("editor.slotBlocked");
                return;
            }
            _editor.Equip(nodeId, slotKey, runeId, grade);
        }

        /// <summary>남은 수(remaining)와 보유 수(owned)를 구하고 보유 수가 1 이상이면 true를 반환한다.</summary>
        private bool TryGetStock(string runeId, string grade, out int remaining, out int owned)
        {
            remaining = 0;
            if (!_editor.TryGetModifierUsage(runeId, grade, out int used, out owned)) return false;
            remaining = owned - used;
            return owned > 0;
        }

        /// <summary>룬 분류별 소지 등급 목록을 반환한다. 행동은 행동 등급표, 모디는 Modifier 등급표, 속성은 빈 등급 하나다.</summary>
        internal static IEnumerable<string> StockGrades(RuneDefinition rune)
        {
            if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR) return GameData.Runes.BehaviorGradeOrder;
            if (rune.Category == SpellGrammar.CATEGORY_MODIFIER) return GameData.ModifierGrades.GetAvailableGrades(rune.Id);
            return ELEMENT_GRADES;
        }

        /// <summary>첫 줄에 이름·등급, 둘째 줄에 남은 수/보유 수를 담고 등급 색을 입힌 문구를 반환한다. 등급이 없으면 이름만 쓴다.</summary>
        internal static string StockLabel(RuneDefinition rune, string grade, int remaining, int owned)
        {
            string name = string.IsNullOrEmpty(grade) ? rune.Name : rune.Name + " " + grade;
            string label = name + "\n" + GameData.L("ui.modifierStock") + " " + remaining + "/" + owned;
            return ModifierGradeText.Colorize(label, grade);
        }

        /// <summary>팔레트에 놓을 수 있는 룬인지 반환한다. 행동·전투 속성·모디만 표시하고 비활성 룬과 기본 중립 속성은 제외한다.</summary>
        private static bool IsPaletteRune(RuneDefinition rune)
        {
            if (rune.IsDisabled || rune.Id == SpellGrammar.ElementRune(SpellGrammar.ELEMENT_NEUTRAL)) return false;
            return rune.Category == SpellGrammar.CATEGORY_BEHAVIOR || rune.Category == SpellGrammar.CATEGORY_ELEMENT
                || rune.Category == SpellGrammar.CATEGORY_MODIFIER;
        }

        /// <summary>검색어가 비어 있거나 룬 이름·ID 중 하나와 대소문자 구분 없이 일치하는지 반환한다.</summary>
        internal static bool MatchesSearch(RuneDefinition rune, string query)
        {
            return string.IsNullOrEmpty(query)
                || rune.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || rune.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
