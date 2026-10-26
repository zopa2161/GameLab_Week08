using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 룬 팔레트와 빠른 검색 팝업의 Presenter다. 분류·검색어로 행동·속성·모디를 골라 팔레트에는 남은 수만, 팝업에는 남은/보유 수를 함께 표시한다.
    /// 행동 행은 제안 위치(팔레트) 또는 더블클릭 위치(빠른 검색)에 배치하고, 속성·모디 행은 클릭하면 커서에 붙여 원하는 슬롯에 장착한다.
    /// 보유하지 않은 항목은 숨기고, 남은 수가 0인 항목은 일반 편집에서는 숨기며 합성 재료 선택 중에는 표시만 한다.
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

        /// <summary>분류·등급 필터와 검색어를 초기 상태("all", 등급 해제, 빈 검색어)로 되돌리고 팔레트를 다시 표시한다.</summary>
        public void ResetFilters()
        {
            _category = "all";
            _grade = null;
            _view.SetSearchQuery("");
            Refresh();
        }

        /// <summary>현재 분류·검색어에 맞는 보유 행동(등급별)·속성·모디(등급별)와 배치 가능한 시작 노드 룬을 이름·등급과 남은 수로 표시한다. 일반 편집에서 남은 수가 0인 항목은 숨기고, 합성 재료 선택 중에는 예약으로 0이 된 항목도 소지량 부족 상태로 표시한다.</summary>
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
                if (rune.Category == SpellGrammar.CATEGORY_CORE)
                {
                    if (_selectMaterial == null) AddStartNodeRune(rune);
                    continue;
                }
                foreach (string grade in StockGrades(rune))
                {
                    if (_selectMaterial != null && !RuneSynthesisRules.IsEligible(rune, grade)) continue;
                    if (_grade != null && grade != _grade) continue;
                    if (!TryGetStock(rune.Id, grade, out int remaining, out _)) continue;
                    if (_reservedCount != null) remaining = Math.Max(0, remaining - _reservedCount(rune.Id, grade));
                    if (remaining <= 0 && _selectMaterial == null) continue;
                    AddPaletteRune(rune, grade, PaletteStockLabel(rune, grade, remaining), remaining > 0);
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

        /// <summary>
        /// 팔레트 행을 만든다. 행동 행은 제안 위치에 배치하고, 속성·모디 행은 클릭하면 커서에 붙여 장착 위치를 고르게 한다.
        /// canPlace가 false(남은 수 0)이면 드래그 배치를 막고 누르면 소지량 부족 문구를 띄운다.
        /// </summary>
        private void AddPaletteRune(RuneDefinition rune, string grade, string label, bool canPlace)
        {
            string runeId = rune.Id;
            Action<UiRow> onClick;
            if (!canPlace) onClick = _ => _editor.ReportStatus("editor.stockBlocked");
            else if (_selectMaterial != null) onClick = _ => _selectMaterial?.Invoke(runeId, grade);
            else if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR)
                onClick = _ => _view.GraphCanvas.PlaceRune(runeId, _view.GraphCanvas.SuggestPlacement(runeId), grade);
            else onClick = row => _view.GraphCanvas.PickUpModule(runeId, grade, row);
            _view.AddPaletteRow(label, ROW_HEIGHT, onClick, runeId, grade, canPlace && _selectMaterial == null);
        }

        /// <summary>남은 수(remaining)와 보유 수(owned)를 구하고 보유 수가 1 이상이면 true를 반환한다. 합성 모드에서는 보관함 전체 배치를 제외한 미사용 수량, 평소에는 현재 프리셋 배치를 제외한 수량을 반환한다.</summary>
        private bool TryGetStock(string runeId, string grade, out int remaining, out int owned)
        {
            remaining = 0;
            if (_selectMaterial != null)
            {
                if (!_editor.TryGetUnusedStock(runeId, grade, out remaining, out owned)) return false;
            }
            else
            {
                if (!_editor.TryGetModifierUsage(runeId, grade, out int used, out owned)) return false;
                remaining = owned - used;
            }
            return owned > 0;
        }

        /// <summary>지정 룬·등급의 현재 남은 수량으로 팔레트 행 문구를 만들어 반환한다. 슬롯에서 집어 든 룬의 드래그 미리보기에 쓴다.</summary>
        internal string BuildStockLabel(string runeId, string grade)
        {
            if (!GameData.Runes.TryGet(runeId, out RuneDefinition rune)) return runeId;
            if (!TryGetStock(runeId, grade, out int remaining, out _)) remaining = 0;
            return PaletteStockLabel(rune, grade, remaining);
        }

        /// <summary>룬 분류별 소지 등급 목록을 반환한다. 행동은 행동 등급표, 모디는 Modifier 등급표, 속성은 빈 등급 하나다.</summary>
        internal static IEnumerable<string> StockGrades(RuneDefinition rune)
        {
            if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR) return GameData.Runes.BehaviorGradeOrder;
            if (rune.Category == SpellGrammar.CATEGORY_MODIFIER) return GameData.ModifierGrades.GetAvailableGrades(rune.Id);
            return ELEMENT_GRADES;
        }

        /// <summary>팔레트 행에 표시할 이름·등급과 현재 남은 수량만 담은 문구를 반환한다.</summary>
        internal static string PaletteStockLabel(RuneDefinition rune, string grade, int remaining)
        {
            string name = string.IsNullOrEmpty(grade) ? rune.Name : rune.Name + " " + grade;
            string label = name + "\n" + GameData.L("ui.modifierStock") + " " + remaining;
            return ModifierGradeText.Colorize(label, grade);
        }

        /// <summary>첫 줄에 이름·등급, 둘째 줄에 남은 수/보유 수를 담고 등급 색을 입힌 문구를 반환한다. 빠른 검색·합성·인스펙터의 전체 수량 표기에 쓴다. 등급이 없으면 이름만 쓴다.</summary>
        internal static string StockLabel(RuneDefinition rune, string grade, int remaining, int owned)
        {
            string name = string.IsNullOrEmpty(grade) ? rune.Name : rune.Name + " " + grade;
            string label = name + "\n" + GameData.L("ui.modifierStock") + " " + remaining + "/" + owned;
            return ModifierGradeText.Colorize(label, grade);
        }

        /// <summary>
        /// 시작 노드 룬의 팔레트 행을 룬 이름만 표시해 클릭·드래그로 제안 위치에 배치하게 한다.
        /// 재고 검사를 하지 않으며 같은 룬의 시작 노드가 그래프에 이미 있으면 행을 만들지 않는다.
        /// </summary>
        private void AddStartNodeRune(RuneDefinition rune)
        {
            foreach (GraphNode node in _editor.Graph.Nodes)
                if (node != null && node.RuneId == rune.Id) return;
            string runeId = rune.Id;
            _view.AddPaletteRow(rune.Name, ROW_HEIGHT,
                _ => _view.GraphCanvas.PlaceRune(runeId, _view.GraphCanvas.SuggestPlacement(runeId), null),
                runeId, null, true);
        }

        /// <summary>팔레트에 놓을 수 있는 룬인지 반환한다. 행동·전투 속성·모디와 시작 노드 룬(core.cast 제외)을 표시하고 비활성 룬과 기본 중립 속성은 제외한다.</summary>
        private static bool IsPaletteRune(RuneDefinition rune)
        {
            if (rune.IsDisabled || rune.Id == SpellGrammar.ElementRune(SpellGrammar.ELEMENT_NEUTRAL)) return false;
            if (rune.Category == SpellGrammar.CATEGORY_CORE) return rune.Id != SpellGrammar.CORE_RUNE;
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
