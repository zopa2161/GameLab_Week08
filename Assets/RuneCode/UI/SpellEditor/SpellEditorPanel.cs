using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 비주얼 스크립팅 편집 View다. 마법 제목 줄·지표, 룬 팔레트, 노드 그래프, 인스펙터, 편집 도구줄, 튜토리얼 문구를 표시하고
    /// 버튼·검색 입력을 이벤트로 알린다. 편집 진행은 SpellEditorPresenter가 하고, 보관함·공유 등은 Popup 층 팝업으로 연다.
    /// 레이아웃과 직렬화 참조는 저장된 Prefab에서 직접 관리한다.
    /// </summary>
    public sealed class SpellEditorPanel : MonoBehaviour
    {
        public static readonly string[] CATEGORY_IDS = { "all", SpellGrammar.CATEGORY_SHAPE, SpellGrammar.CATEGORY_ELEMENT, SpellGrammar.CATEGORY_BEHAVIOR, SpellGrammar.CATEGORY_MODIFIER, SpellGrammar.CATEGORY_FLOW, SpellGrammar.CATEGORY_METHOD };

        // 탭 버튼은 프리팹에서 CATEGORY_IDS 순번으로 연결되어 있다. 슬롯 방식에서 노드로 배치하지 않는 분류의 탭은 숨긴다.
        private static readonly HashSet<string> HIDDEN_CATEGORY_IDS = new HashSet<string>
        {
            SpellGrammar.CATEGORY_SHAPE, SpellGrammar.CATEGORY_FLOW, SpellGrammar.CATEGORY_METHOD
        };

        [Header("마법 제목 줄")]
        [SerializeField] private TextMeshProUGUI _spellTitle;
        [SerializeField] private Button _libraryButton;
        [SerializeField] private Button _renameButton;
        [SerializeField] private Button _saveButton;
        [SerializeField] private Button _shareButton;

        [Header("지표와 팔레트")]
        [SerializeField] private TextMeshProUGUI _metricsText;
        [SerializeField] private TMP_InputField _searchField;
        [SerializeField] private Button[] _categoryButtons;
        [SerializeField] private RectTransform _runeList;
        [SerializeField] private UiRow _runeRowPrefab;
        [SerializeField] private RunePaletteRowBinding[] _categoryRowPrefabs = Array.Empty<RunePaletteRowBinding>();

        [Header("그래프와 인스펙터")]
        [SerializeField] private RuneGraphCanvas _graphCanvas;
        [SerializeField] private RectTransform _inspectorPanel;
        [SerializeField] private SpellParameterRow _parameterRowPrefab;
        [SerializeField] private UiRow _issueRowPrefab;

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


        /// <summary>노드 그래프 View다.</summary>
        public RuneGraphCanvas GraphCanvas => _graphCanvas;

        /// <summary>인스펙터 표시 도우미다.</summary>
        public SpellInspectorView Inspector => _inspector;

        /// <summary>그래프 라벨에 쓰는 글꼴이다.</summary>
        public TMP_FontAsset Font => _font;

        public event Action LibraryClicked;
        public event Action RenameClicked;
        public event Action SaveClicked;
        public event Action ShareClicked;
        public event Action UndoClicked;
        public event Action RedoClicked;
        public event Action ArrangeClicked;
        public event Action CopyClicked;
        public event Action PasteClicked;
        public event Action TutorialClicked;

        /// <summary>팔레트 검색어가 바뀌었을 때 알린다.</summary>
        public event Action<string> SearchChanged;

        /// <summary>팔레트 분류 버튼을 눌렀을 때 분류 ID와 함께 알린다.</summary>
        public event Action<string> CategoryClicked;

        /// <summary>편집 세션·작업실 호스트·UI 관리자로 Presenter를 만들고 버튼을 이벤트로 연결한다. 이미 초기화했으면 무시한다.</summary>
        public void Initialize(ISpellEditor editor, ISpellEditorHost host, UIManager ui)
        {
            if (_presenter != null) return;
            if (_font == null) _font = TMP_Settings.defaultFontAsset;
            _inspector = new SpellInspectorView(_inspectorPanel, new UiFactory(_font, _panelPrefab, _buttonPrefab), _parameterRowPrefab, _issueRowPrefab);
            BindButtons();
            _presenter = new SpellEditorPresenter(editor, host, ui, this);
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

        /// <summary>도크 실행 하이라이트를 그래프에 표시한다.</summary>
        public void HighlightNode(string nodeId) => _graphCanvas.Highlight(nodeId);

        /// <summary>마법 제목을 표시한다.</summary>
        public void SetTitle(string text) => _spellTitle.text = text;

        /// <summary>RAM·비용·쿨다운 지표 문구를 표시한다.</summary>
        public void SetMetrics(string text) => _metricsText.text = text;

        /// <summary>도구줄 툴팁 문구를 표시한다.</summary>
        public void SetTooltip(string text)
        {
            _tooltipText.richText = true;
            _tooltipText.text = text;
        }

        /// <summary>호버한 룬과 등급을 인스펙터에 표시하며 null이면 선택 정보로 복구한다.</summary>
        public void ShowRuneInfo(RuneDefinition rune, string grade)
        {
            _presenter.ShowRuneInfo(rune, grade);
        }

        /// <summary>현재 팔레트 검색어다.</summary>
        public string SearchQuery => _searchField.text;

        /// <summary>팔레트 목록을 비운다. 이어서 AddPaletteRow로 채운다.</summary>
        public void ClearPalette()
        {
            foreach (ViewPool<UiRow> rows in _paletteRows.Values) rows.ReleaseAll();
        }

        /// <summary>팔레트 행을 추가하고 클릭·드래그 배치를 룬 등급과 연결한다.</summary>
        public void AddPaletteRow(string label, float height, Action onClick, string runeId, string grade, bool canPlace)
        {
            string category = GameData.Runes.Get(runeId).Category;
            UiRow prefab = RunePaletteRowBinding.Resolve(_categoryRowPrefabs, category, _runeRowPrefab);
            if (!_paletteRows.TryGetValue(prefab, out ViewPool<UiRow> rows))
            {
                rows = new ViewPool<UiRow>(prefab, _runeList);
                _paletteRows.Add(prefab, rows);
            }
            UiRow row = rows.Get();
            row.Configure(label, height, onClick, 12);
            RunePaletteDrag drag = row.GetComponent<RunePaletteDrag>();
            if (drag == null) drag = row.gameObject.AddComponent<RunePaletteDrag>();
            drag.Initialize(_graphCanvas, runeId, grade, canPlace, this);
        }

        void Update()
        {
            _presenter?.Tick();
        }

        void OnDestroy()
        {
            _presenter?.Dispose();
        }

        /// <summary>category에 해당하는 팔레트 탭의 선택 표시를 활성화한다.</summary>
        public void ShowCategory(string category)
        {
            for (int i = 0; i < _categoryButtons.Length; i++)
                _categoryButtons[i].GetComponent<TabButton>().SetSelected(CATEGORY_IDS[i] == category);
        }

        /// <summary>편집 버튼과 검색·분류 입력을 화면 이벤트에 연결한다.</summary>
        private void BindButtons()
        {
            _libraryButton.onClick.AddListener(() => LibraryClicked?.Invoke());
            _renameButton.onClick.AddListener(() => RenameClicked?.Invoke());
            _saveButton.onClick.AddListener(() => SaveClicked?.Invoke());
            _shareButton.onClick.AddListener(() => ShareClicked?.Invoke());
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
                if (HIDDEN_CATEGORY_IDS.Contains(category)) _categoryButtons[i].gameObject.SetActive(false);
            }
        }
    }
}
