using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 그래프 빈 곳 더블클릭으로 여는 빠른 룬 검색 팝업 View다. 검색어 변경을 알리고 결과 행을 재사용해 표시한다.
    /// </summary>
    public sealed class QuickPalettePopup : UiPopup
    {
        private const string TAB_BUTTON_RESOURCE = "RuneCode/UI/Elements/TabButton";
        private const float GRADE_FILTER_LAYOUT_SHIFT = 32f;
        private const float GRADE_FILTER_ROW_TOP_GAP = 6f;
        private const float GRADE_FILTER_BUTTON_WIDTH = 70f;
        private const float GRADE_FILTER_BUTTON_HEIGHT = 25f;
        private const float GRADE_FILTER_BUTTON_GAP = 6f;

        [Header("검색")]
        [SerializeField] private TMP_InputField _search;
        [SerializeField] private RectTransform _list;
        [SerializeField] private UiRow _rowPrefab;
        [SerializeField] private RunePaletteRowBinding[] _categoryRowPrefabs = Array.Empty<RunePaletteRowBinding>();

        private readonly Dictionary<UiRow, ViewPool<UiRow>> _rows = new Dictionary<UiRow, ViewPool<UiRow>>();
        private readonly List<Button> _gradeButtons = new List<Button>();
        private RectTransform _searchRect;
        private RectTransform _scrollRect;
        private Vector2 _searchDefaultPosition;
        private Vector2 _scrollDefaultPosition;
        private Vector2 _scrollDefaultSize;
        private bool _hasCachedLayout;
        private bool _isBound;

        /// <summary>검색어가 바뀌었을 때 알린다.</summary>
        public event Action<string> SearchChanged;

        /// <summary>등급 필터 버튼을 눌렀을 때 클릭한 등급 ID를 알린다.</summary>
        public event Action<string> GradeClicked;

        /// <summary>현재 검색어다.</summary>
        public string Query => _search.text;

        /// <summary>검색 입력을 연결하고 isGradeFilterEnabled에 맞게 등급 행과 검색·목록 위치를 설정한다.</summary>
        public void Begin(bool isGradeFilterEnabled = false)
        {
            if (!_isBound)
            {
                _search.onValueChanged.AddListener(query => SearchChanged?.Invoke(query));
                _isBound = true;
            }
            if (!_hasCachedLayout)
            {
                _searchRect = (RectTransform)_search.transform;
                _scrollRect = (RectTransform)_list.parent.parent;
                _searchDefaultPosition = _searchRect.anchoredPosition;
                _scrollDefaultPosition = _scrollRect.anchoredPosition;
                _scrollDefaultSize = _scrollRect.sizeDelta;
                _hasCachedLayout = true;
            }
            if (isGradeFilterEnabled && _gradeButtons.Count == 0) CreateGradeFilterButtons();
            foreach (Button button in _gradeButtons)
            {
                button.gameObject.SetActive(isGradeFilterEnabled);
                button.interactable = true;
            }
            float layoutShift = isGradeFilterEnabled ? GRADE_FILTER_LAYOUT_SHIFT : 0f;
            _searchRect.anchoredPosition = _searchDefaultPosition + new Vector2(0f, -layoutShift);
            _scrollRect.anchoredPosition = _scrollDefaultPosition + new Vector2(0f, -layoutShift);
            _scrollRect.sizeDelta = _scrollDefaultSize + new Vector2(0f, -layoutShift);
            ShowGrade(null);
            _search.SetTextWithoutNotify("");
            _search.ActivateInputField();
        }

        /// <summary>grade에 해당하는 등급 필터 버튼만 선택 표시한다.</summary>
        public void ShowGrade(string grade)
        {
            IReadOnlyList<string> grades = GameData.ModifierGrades.GradeOrder;
            for (int index = 0; index < _gradeButtons.Count; index++)
                _gradeButtons[index].GetComponent<TabButton>().SetSelected(grades[index] == grade);
        }

        /// <summary>합성 재료 선택에서는 최고 등급을 제외하고 이미 정해진 등급이 있으면 필터 변경을 잠근다.</summary>
        public void SetSynthesisGradeFilter(string lockedGrade)
        {
            IReadOnlyList<string> grades = GameData.ModifierGrades.GradeOrder;
            for (int index = 0; index < _gradeButtons.Count; index++)
                _gradeButtons[index].interactable = lockedGrade == null && RuneSynthesisRules.NextGrade(grades[index]) != null;
        }

        /// <summary>결과 목록을 비운다.</summary>
        public void ClearResults()
        {
            foreach (ViewPool<UiRow> rows in _rows.Values) rows.ReleaseAll();
        }

        /// <summary>category에 연결된 프리팹으로 결과 행을 추가하고 문구와 클릭 동작을 연결한다.</summary>
        public void AddResult(string label, Action onClick, string category = null)
        {
            UiRow prefab = RunePaletteRowBinding.Resolve(_categoryRowPrefabs, category, _rowPrefab);
            if (!_rows.TryGetValue(prefab, out ViewPool<UiRow> rows))
            {
                rows = new ViewPool<UiRow>(prefab, _list);
                _rows.Add(prefab, rows);
            }
            rows.Get().Configure(label, SpellPalettePresenter.ROW_HEIGHT, onClick, 14);
        }

        /// <summary>검색창 위에 기존 TabButton 프리팹으로 등급별 필터를 만든다.</summary>
        private void CreateGradeFilterButtons()
        {
            IReadOnlyList<string> grades = GameData.ModifierGrades.GradeOrder;
            float totalWidth = GRADE_FILTER_BUTTON_WIDTH * grades.Count + GRADE_FILTER_BUTTON_GAP * (grades.Count - 1);
            float startX = 22f;
            //float startX = _searchDefaultPosition.x + (_searchRect.sizeDelta.x - totalWidth) * 0.5f;
            float rowY = _searchDefaultPosition.y + GRADE_FILTER_ROW_TOP_GAP - 5f;
            GameObject prefab = Resources.Load<GameObject>(TAB_BUTTON_RESOURCE);
            for (int index = 0; index < grades.Count; index++)
            {
                string grade = grades[index];
                GameObject instance = Instantiate(prefab, _searchRect.parent, false);
                instance.name = "GradeFilter" + grade;
                RectTransform buttonRect = (RectTransform)instance.transform;
                buttonRect.sizeDelta = new Vector2(GRADE_FILTER_BUTTON_WIDTH, GRADE_FILTER_BUTTON_HEIGHT);
                buttonRect.anchoredPosition = new Vector2(startX + index * (GRADE_FILTER_BUTTON_WIDTH + GRADE_FILTER_BUTTON_GAP), rowY);
                Button button = instance.GetComponent<Button>();
                TextMeshProUGUI label = instance.GetComponentInChildren<TextMeshProUGUI>(true);
                label.fontSize = 10f;
                label.richText = true;
                label.text = ModifierGradeText.Colorize(grade + GameData.L("ui.modifierGrade"), grade);
                string selectedGrade = grade;
                button.onClick.AddListener(() => GradeClicked?.Invoke(selectedGrade));
                _gradeButtons.Add(button);
            }
        }
    }
}
