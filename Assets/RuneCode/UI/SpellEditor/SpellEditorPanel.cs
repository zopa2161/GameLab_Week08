using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 비주얼 스크립팅 편집 View다. 지표, 룬 팔레트, 노드 그래프, 편집 도구줄, 튜토리얼 문구를 표시하고
    /// 버튼·검색 입력을 이벤트로 알린다. 편집 진행은 SpellEditorPresenter가 한다.
    /// 레이아웃과 직렬화 참조는 저장된 Prefab에서 직접 관리한다.
    /// </summary>
    public sealed class SpellEditorPanel : MonoBehaviour
    {
        private const float HOVER_WIDTH = 360f;
        private const float HOVER_PADDING = 12f;
        private const float HOVER_OFFSET = 18f;
        private const float DELETE_ZONE_WIDTH = 456f;
        private const float DELETE_ZONE_HEIGHT = 109f;
        private const float DELETE_ZONE_BOTTOM_MARGIN = 16f;
        private const string DELETE_ZONE_SPRITE_PATH = "RuneCode/UI/Skins/ButtonDestructive";

        private static readonly string[] _gradeFilterIds = { "C", "B", "A", "S" };

        public static readonly string[] CATEGORY_IDS = { "all", SpellGrammar.CATEGORY_SHAPE, SpellGrammar.CATEGORY_ELEMENT, SpellGrammar.CATEGORY_BEHAVIOR, SpellGrammar.CATEGORY_MODIFIER, SpellGrammar.CATEGORY_FLOW, SpellGrammar.CATEGORY_METHOD };

        [Header("지표와 팔레트")]
        [SerializeField] private TextMeshProUGUI _metricsText;
        [SerializeField] private TMP_InputField _searchField;
        [SerializeField] private Button[] _categoryButtons;
        [SerializeField] private Button[] _gradeButtons;
        [SerializeField] private RectTransform _runeList;
        [SerializeField] private UiRow _runeRowPrefab;
        [SerializeField] private RunePaletteRowBinding[] _categoryRowPrefabs = Array.Empty<RunePaletteRowBinding>();

        [Header("그래프")]
        [SerializeField] private RuneGraphCanvas _graphCanvas;

        [Header("편집 도구줄")]
        [SerializeField] private Button _undoButton;
        [SerializeField] private Button _redoButton;
        [SerializeField] private Button _arrangeButton;
        [SerializeField] private Button _copyButton;
        [SerializeField] private Button _pasteButton;
        [SerializeField] private TextMeshProUGUI _tooltipText;

        [Header("공용")]
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private GameObject _panelPrefab;
        [SerializeField] private GameObject _buttonPrefab;

        private SpellEditorPresenter _presenter;
        private readonly Dictionary<UiRow, ViewPool<UiRow>> _paletteRows = new Dictionary<UiRow, ViewPool<UiRow>>();
        private SpellInspectorView _inspector;
        private readonly Dictionary<Button, bool> _synthesisButtonStates = new Dictionary<Button, bool>();

        private RectTransform _hoverPanel;
        private TextMeshProUGUI _hoverLabel;
        private RectTransform _deleteZone;


        /// <summary>노드 그래프 View다.</summary>
        public RuneGraphCanvas GraphCanvas => _graphCanvas;

        /// <summary>호버 설명 텍스트 도우미다.</summary>
        public SpellInspectorView Inspector => _inspector;

        /// <summary>그래프 라벨에 쓰는 글꼴이다.</summary>
        public TMP_FontAsset Font => _font;

        public bool IsSynthesisOpen => _presenter != null && _presenter.IsSynthesisOpen;

        public event Action UndoClicked;
        public event Action RedoClicked;
        public event Action ArrangeClicked;
        public event Action CopyClicked;
        public event Action PasteClicked;

        /// <summary>팔레트 검색어가 바뀌었을 때 알린다.</summary>
        public event Action<string> SearchChanged;

        /// <summary>팔레트 분류 버튼을 눌렀을 때 분류 ID와 함께 알린다.</summary>
        public event Action<string> CategoryClicked;

        public event Action<string> GradeClicked;

        /// <summary>편집 세션·작업실 호스트·UI 관리자로 Presenter를 만들고 버튼을 이벤트로 연결한다. 이미 초기화했으면 무시한다.</summary>
        public void Initialize(ISpellEditor editor, ISpellEditorHost host, UIManager ui)
        {
            if (_presenter != null) return;
            if (_font == null) _font = TMP_Settings.defaultFontAsset;
            _inspector = new SpellInspectorView();
            BindButtons();
            CreateDeleteZone();
            _presenter = new SpellEditorPresenter(editor, host, ui, this);
            _graphCanvas.SetDeleteZone(_deleteZone);
        }

        /// <summary>행동 노드를 끌어다 놓으면 삭제되는 드롭 영역을 패널 하단 중앙에 만들고 숨긴다.</summary>
        private void CreateDeleteZone()
        {
            _deleteZone = UiFactory.Rect(transform, "DeleteNodeDropZone", 0f, 0f, DELETE_ZONE_WIDTH, DELETE_ZONE_HEIGHT);
            _deleteZone.anchorMin = _deleteZone.anchorMax = new Vector2(0.5f, 0f);
            _deleteZone.pivot = new Vector2(0.5f, 0f);
            _deleteZone.anchoredPosition = new Vector2(0f, DELETE_ZONE_BOTTOM_MARGIN);
            _deleteZone.sizeDelta = new Vector2(DELETE_ZONE_WIDTH, DELETE_ZONE_HEIGHT);

            Image image = _deleteZone.gameObject.AddComponent<Image>();
            Sprite sprite = Resources.Load<Sprite>(DELETE_ZONE_SPRITE_PATH);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            image.raycastTarget = false;
            _deleteZone.SetAsLastSibling();
            _deleteZone.gameObject.SetActive(false);
        }

        /// <summary>패널을 표시하고 편집을 허용한 뒤 전체를 갱신한다.</summary>
        public void Show()
        {
            gameObject.SetActive(true);
            _presenter.Enter();
        }

        /// <summary>열린 팝업을 닫고 편집을 금지한 뒤 패널을 숨긴다.</summary>
        public void Hide()
        {
            _presenter?.Exit();
            gameObject.SetActive(false);
        }

        /// <summary>RAM·비용·쿨다운 지표 문구를 표시한다.</summary>
        public void SetMetrics(string text) => _metricsText.text = text;

        /// <summary>도구줄 툴팁 문구를 표시한다.</summary>
        public void SetTooltip(string text)
        {
            _tooltipText.richText = true;
            _tooltipText.text = text;
        }

        /// <summary>호버한 룬과 등급 정보를 Presenter에 전달한다.</summary>
        public void ShowRuneInfo(RuneDefinition rune, string grade)
        {
            _presenter.ShowRuneInfo(rune, grade);
        }

        /// <summary>인스펙터와 같은 설명을 포인터 옆의 남색 안내창에 표시하며 빈 내용이면 숨긴다.</summary>
        public void ShowHoverDescription(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (_hoverPanel != null) _hoverPanel.gameObject.SetActive(false);
                return;
            }
            if (_hoverPanel == null)
            {
                RectTransform parent = (RectTransform)_graphCanvas.canvas.rootCanvas.transform;
                _hoverPanel = UiFactory.Rect(parent, "RuneHoverDescription", 0, 0, HOVER_WIDTH, 0);
                Image background = _hoverPanel.gameObject.AddComponent<Image>();
                background.color = new Color(0.035f, 0.06f, 0.1f, 0.98f);
                background.raycastTarget = false;
                _hoverLabel = Instantiate(Resources.Load<TextMeshProUGUI>("RuneCode/UI/Elements/UiText"), _hoverPanel, false);
                _hoverLabel.font = _font;
                _hoverLabel.fontSize = 16f;
                _hoverLabel.enableAutoSizing = true;
                _hoverLabel.fontSizeMin = 12f;
                _hoverLabel.fontSizeMax = 16f;
                _hoverLabel.color = Color.white;
                _hoverLabel.richText = true;
                _hoverLabel.raycastTarget = false;
                _hoverLabel.alignment = TextAlignmentOptions.TopLeft;
                _hoverLabel.rectTransform.anchorMin = Vector2.zero;
                _hoverLabel.rectTransform.anchorMax = Vector2.one;
                _hoverLabel.rectTransform.offsetMin = new Vector2(HOVER_PADDING, HOVER_PADDING);
                _hoverLabel.rectTransform.offsetMax = new Vector2(-HOVER_PADDING, -HOVER_PADDING);
            }
            _hoverLabel.text = text;
            RectTransform bounds = (RectTransform)_hoverPanel.parent;
            float width = Mathf.Min(HOVER_WIDTH, bounds.rect.width - HOVER_PADDING * 2f);
            float height = _hoverLabel.GetPreferredValues(text, width - HOVER_PADDING * 2f, Mathf.Infinity).y + HOVER_PADDING * 2f;
            _hoverPanel.sizeDelta = new Vector2(width, Mathf.Min(height, bounds.rect.height - HOVER_PADDING * 2f));
            _hoverPanel.gameObject.SetActive(true);
            _hoverPanel.SetAsLastSibling();
            PositionHoverDescription();
        }

        /// <summary>호버 안내창이 게임 화면을 벗어나지 않도록 포인터 기준 위치를 보정한다.</summary>
        private void PositionHoverDescription()
        {
            if (_hoverPanel == null || !_hoverPanel.gameObject.activeSelf || Mouse.current == null) return;
            RectTransform parent = (RectTransform)_hoverPanel.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Mouse.current.position.ReadValue(), null, out Vector2 pointer);
            Rect bounds = parent.rect;
            float x = Mathf.Clamp(pointer.x + HOVER_OFFSET, bounds.xMin + HOVER_PADDING,
                bounds.xMax - _hoverPanel.rect.width - HOVER_PADDING);
            float y = Mathf.Clamp(pointer.y - HOVER_OFFSET, bounds.yMin + _hoverPanel.rect.height + HOVER_PADDING,
                bounds.yMax - HOVER_PADDING);
            _hoverPanel.position = parent.TransformPoint(new Vector3(x, y, 0f));
        }

        void OnDisable()
        {
            if (_hoverPanel != null) _hoverPanel.gameObject.SetActive(false);
        }

        /// <summary>현재 팔레트 검색어다.</summary>
        public string SearchQuery => _searchField.text;

        /// <summary>검색어를 변경 이벤트 없이 복구한다.</summary>
        public void SetSearchQuery(string query) => _searchField.SetTextWithoutNotify(query ?? "");

        /// <summary>합성 중 편집 도구 버튼의 입력을 막고 닫을 때 이전 활성 상태를 복구한다.</summary>
        public void SetSynthesisEditingLocked(bool isLocked)
        {
            if (isLocked)
            {
                Button[] buttons = { _undoButton, _redoButton, _arrangeButton, _copyButton, _pasteButton };
                foreach (Button button in buttons)
                {
                    _synthesisButtonStates[button] = button.interactable;
                    button.interactable = false;
                }
            }
            else
            {
                foreach (KeyValuePair<Button, bool> state in _synthesisButtonStates) state.Key.interactable = state.Value;
                _synthesisButtonStates.Clear();
            }
        }

        /// <summary>합성 중 허용 분류·등급만 누를 수 있게 하고 재료가 있으면 필터 변경을 잠근다.</summary>
        public void SetSynthesisFilters(bool isActive, string category, string grade, bool isElementFilter = false)
        {
            for (int index = 0; index < _categoryButtons.Length; index++)
            {
                string id = CATEGORY_IDS[index];
                _categoryButtons[index].interactable = !isActive || (category == null
                    && (id == "all" || id == SpellGrammar.CATEGORY_BEHAVIOR || id == SpellGrammar.CATEGORY_MODIFIER
                        || id == SpellGrammar.CATEGORY_ELEMENT));
            }
            for (int index = 0; index < _gradeButtons.Length; index++)
                _gradeButtons[index].interactable = !isActive || (grade == null && !isElementFilter
                    && RuneSynthesisRules.NextGrade(_gradeFilterIds[index]) != null);
        }

        /// <summary>기존 공용 프리팹으로 합성 UI를 구성할 생성기와 팔레트 영역을 반환한다.</summary>
        public UiFactory CreateSynthesisUi(out RectTransform palette, out RectTransform scroll)
        {
            palette = (RectTransform)_categoryButtons[0].transform.parent;
            scroll = (RectTransform)_runeList.parent.parent;
            return new UiFactory(_font, _panelPrefab, _buttonPrefab);
        }

        /// <summary>합성 슬롯과 결과 표시에서 재사용할 분류별 팔레트 행 프리팹을 반환한다.</summary>
        public UiRow GetSynthesisRowPrefab(string category)
        {
            return RunePaletteRowBinding.Resolve(_categoryRowPrefabs, category, _runeRowPrefab);
        }

        /// <summary>분류별 팔레트 행 프리팹을 임시로 만들어 지정 문구로 채운 뒤 같은 모양의 드래그 미리보기를 반환한다.</summary>
        internal RunePalettePreview CreatePaletteRowPreview(RuneDefinition rune, string label)
        {
            if (_graphCanvas == null) return null;
            UiRow prefab = RunePaletteRowBinding.Resolve(_categoryRowPrefabs, rune.Category, _runeRowPrefab);
            UiRow row = Instantiate(prefab, _runeList, false);
            row.gameObject.SetActive(false);
            row.Configure(label, SpellPalettePresenter.ROW_HEIGHT, () => { }, 12);
            if (_runeList.TryGetComponent(out GridLayoutGroup grid)) ((RectTransform)row.transform).sizeDelta = grid.cellSize;
            RunePalettePreview preview = RunePalettePreview.CreateRow(_graphCanvas.canvas.rootCanvas, row);
            Destroy(row.gameObject);
            return preview;
        }

        /// <summary>합성창이 열려 있으면 결과 안내 또는 창을 닫고 입력을 소비한다.</summary>
        public bool HandleSynthesisEscape() => _presenter != null && _presenter.HandleSynthesisEscape();

        /// <summary>팔레트 목록을 비운다. 이어서 AddPaletteRow로 채운다.</summary>
        public void ClearPalette()
        {
            foreach (ViewPool<UiRow> rows in _paletteRows.Values) rows.ReleaseAll();
        }

        /// <summary>팔레트 행을 추가하고 클릭·드래그 배치를 룬 등급과 연결한다. 클릭 동작에는 생성된 행을 전달한다.</summary>
        public void AddPaletteRow(string label, float height, Action<UiRow> onClick, string runeId, string grade, bool canPlace)
        {
            string category = GameData.Runes.Get(runeId).Category;
            UiRow prefab = RunePaletteRowBinding.Resolve(_categoryRowPrefabs, category, _runeRowPrefab);
            if (!_paletteRows.TryGetValue(prefab, out ViewPool<UiRow> rows))
            {
                rows = new ViewPool<UiRow>(prefab, _runeList);
                _paletteRows.Add(prefab, rows);
            }
            UiRow row = rows.Get();
            row.Configure(label, height, onClick == null ? null : () => onClick(row), 12);
            RunePaletteDrag drag = row.GetComponent<RunePaletteDrag>();
            if (drag == null) drag = row.gameObject.AddComponent<RunePaletteDrag>();
            drag.Initialize(_graphCanvas, runeId, grade, canPlace, this);
        }

        void Update()
        {
            _presenter?.Tick();
            if (Mouse.current != null && Mouse.current.leftButton.isPressed) ShowHoverDescription(null);
            PositionHoverDescription();
        }

        void OnDestroy()
        {
            if (_hoverPanel != null) Destroy(_hoverPanel.gameObject);
            _presenter?.Dispose();
        }

        /// <summary>category에 해당하는 팔레트 탭의 선택 표시를 활성화한다.</summary>
        public void ShowCategory(string category)
        {
            for (int i = 0; i < _categoryButtons.Length; i++)
                _categoryButtons[i].GetComponent<TabButton>().SetSelected(CATEGORY_IDS[i] == category);
        }

        /// <summary>grade에 해당하는 등급 필터 버튼만 선택 표시한다.</summary>
        public void ShowGrade(string grade)
        {
            for (int i = 0; i < _gradeButtons.Length; i++)
                _gradeButtons[i].GetComponent<TabButton>().SetSelected(_gradeFilterIds[i] == grade);
        }

        /// <summary>편집 버튼과 검색·분류 입력을 화면 이벤트에 연결한다.</summary>
        private void BindButtons()
        {
            _undoButton.onClick.AddListener(() => UndoClicked?.Invoke());
            _redoButton.onClick.AddListener(() => RedoClicked?.Invoke());
            _arrangeButton.onClick.AddListener(() => ArrangeClicked?.Invoke());
            _copyButton.onClick.AddListener(() => CopyClicked?.Invoke());
            _pasteButton.onClick.AddListener(() => PasteClicked?.Invoke());
            _searchField.onValueChanged.AddListener(query => SearchChanged?.Invoke(query));
            for (int i = 0; i < _categoryButtons.Length; i++)
            {
                string category = CATEGORY_IDS[i];
                _categoryButtons[i].onClick.AddListener(() => CategoryClicked?.Invoke(category));
            }
            BindGradeFilterButtons();
        }

        /// <summary>프리팹에 배치한 C·B·A·S 등급 필터 버튼에 등급 색 라벨과 클릭 이벤트를 연결한다.</summary>
        private void BindGradeFilterButtons()
        {
            for (int index = 0; index < _gradeButtons.Length; index++)
            {
                string grade = _gradeFilterIds[index];
                TextMeshProUGUI label = _gradeButtons[index].GetComponentInChildren<TextMeshProUGUI>(true);
                label.richText = true;
                label.text = ModifierGradeText.Colorize(grade + GameData.L("ui.modifierGrade"), grade);
                _gradeButtons[index].onClick.AddListener(() => GradeClicked?.Invoke(grade));
            }
        }
    }
}
