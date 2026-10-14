using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 작업실 화면 View다. 헤더·탭·상태줄과 탭별 패널 View를 묶고 탭 클릭을 알린다.
    /// 진행은 WorkshopPresenter가 하며, UIManager가 재사용하므로 초기화는 한 번, 진입 갱신은 표시할 때마다 한다.
    /// </summary>
    public sealed class WorkshopScreen : UiView
    {
        public const string TAB_EDITOR = "editor";
        public const string TAB_TREE = "tree";
        public const string TAB_DEPLOY = "deploy";
        private static readonly string[] TABS = { TAB_EDITOR, TAB_TREE };
        private const float STATUS_TOAST_EDGE_PADDING = 12f;

        [Header("탭 구성")]
        [SerializeField] private RectTransform _editorRoot;
        [SerializeField] private RectTransform _treeRoot;
        [SerializeField] private RectTransform _deployRoot;
        [SerializeField] private Button[] _tabButtons;

        [Header("화면 이동")]
        [SerializeField] private Button _deployButton;
        [SerializeField] private Button _helpButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _backButton;

        [Header("패널")]
        [SerializeField] private SpellEditorPanel _spellEditor;
        [SerializeField] private DockPanel _dockPanel;
        [SerializeField] private UpgradeTreePanel _upgradeTreePanel;
        [SerializeField] private DeployPanel _deployPanel;

        [Header("표시")]
        [SerializeField] private TextMeshProUGUI _headerStats;
        [SerializeField] private TextMeshProUGUI _status;

        private WorkshopPresenter _presenter;
        private readonly Vector3[] _graphCanvasCorners = new Vector3[4];
        private bool _showStageSelectionOnNextEntry;

        public SpellEditorPanel SpellEditor => _spellEditor;
        public DockPanel Dock => _dockPanel;
        public UpgradeTreePanel UpgradeTree => _upgradeTreePanel;
        public DeployPanel Deploy => _deployPanel;

        /// <summary>탭 버튼을 눌렀을 때 탭 ID와 함께 알린다.</summary>
        public event Action<string> TabClicked;

        /// <summary>시간제 전투의 스테이지 선택 화면 열기를 알린다.</summary>
        public event Action DeployClicked;

        /// <summary>현재 화면 위에 게임 도움말 팝업 열기를 알린다.</summary>
        public event Action HelpClicked;

        /// <summary>현재 화면 위에 설정 팝업 열기를 알린다.</summary>
        public event Action SettingsClicked;

        /// <summary>스테이지 선택 이전 화면으로 돌아가기를 알린다.</summary>
        public event Action BackClicked;

        /// <summary>탭 버튼을 이벤트로 연결하고 세션·UI 관리자로 Presenter를 만든다. 이미 초기화했으면 무시한다.</summary>
        public void Initialize(RuneCodeSession session, UIManager ui)
        {
            if (_presenter != null) return;
            for (int i = 0; i < TABS.Length; i++)
            {
                string tab = TABS[i];
                _tabButtons[i].onClick.AddListener(() => TabClicked?.Invoke(tab));
            }
            _deployButton.onClick.AddListener(() => DeployClicked?.Invoke());
            _helpButton.onClick.AddListener(() => HelpClicked?.Invoke());
            _settingsButton.onClick.AddListener(() => SettingsClicked?.Invoke());
            _backButton.onClick.AddListener(() => BackClicked?.Invoke());
            _presenter = new WorkshopPresenter(session, ui, this);
        }

        /// <summary>지정 화면만 표시하고 스테이지 선택 중에는 탭과 진입 버튼을 숨기고 뒤로가기를 표시한다.</summary>
        public void ShowTab(string tab)
        {
            _editorRoot.gameObject.SetActive(tab == TAB_EDITOR);
            _treeRoot.gameObject.SetActive(tab == TAB_TREE);
            _deployRoot.gameObject.SetActive(tab == TAB_DEPLOY);
            bool isDeploy = tab == TAB_DEPLOY;
            _deployButton.gameObject.SetActive(!isDeploy);
            _backButton.gameObject.SetActive(isDeploy);
            for (int i = 0; i < TABS.Length; i++)
            {
                _tabButtons[i].gameObject.SetActive(!isDeploy);
                _tabButtons[i].GetComponent<TabButton>().SetSelected(TABS[i] == tab);
            }
        }

        /// <summary>다음 작업실 진입 때 스테이지 선택 UI를 연다.</summary>
        public void ShowStageSelectionOnNextEntry()
        {
            _showStageSelectionOnNextEntry = true;
        }

        /// <summary>스테이지 선택 중 Esc를 누르면 이전 작업실 탭으로 돌아가고 입력 소비 여부를 반환한다.</summary>
        public override bool HandleEscape()
        {
            if (!_deployRoot.gameObject.activeSelf) return false;
            BackClicked?.Invoke();
            return true;
        }

        /// <summary>헤더 수치 문구를 표시한다.</summary>
        public void SetHeader(string text) => _headerStats.text = text;

        /// <summary>상태 토스트 문구를 표시한다.</summary>
        public void SetStatus(string text) => _status.text = text;

        /// <summary>Screen Space Overlay 좌표의 토스트를 그래프 편집 영역 경계 안에 배치한다.</summary>
        public void SetStatusPosition(Vector2 screenPosition)
        {
            RectTransform screenRect = (RectTransform)transform;
            RectTransform statusRect = _status.rectTransform;
            if (statusRect.parent != screenRect)
            {
                Vector2 statusSize = statusRect.rect.size;
                statusRect.SetParent(screenRect, false);
                statusRect.anchorMin = statusRect.anchorMax = new Vector2(0.5f, 0.5f);
                statusRect.sizeDelta = statusSize;
                statusRect.pivot = new Vector2(0.5f, 0.5f);
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(screenRect, screenPosition, null, out Vector2 localPosition)) return;
            RectTransform graphRect = _spellEditor.GraphCanvas.rectTransform;
            graphRect.GetWorldCorners(_graphCanvasCorners);
            Vector2 graphMinimum = screenRect.InverseTransformPoint(_graphCanvasCorners[0]);
            Vector2 graphMaximum = graphMinimum;
            for (int index = 1; index < _graphCanvasCorners.Length; index++)
            {
                Vector2 corner = screenRect.InverseTransformPoint(_graphCanvasCorners[index]);
                graphMinimum = Vector2.Min(graphMinimum, corner);
                graphMaximum = Vector2.Max(graphMaximum, corner);
            }
            Rect statusBounds = statusRect.rect;
            float minX = graphMinimum.x + statusBounds.width * statusRect.pivot.x + STATUS_TOAST_EDGE_PADDING;
            float maxX = graphMaximum.x - statusBounds.width * (1f - statusRect.pivot.x) - STATUS_TOAST_EDGE_PADDING;
            float minY = graphMinimum.y + statusBounds.height * statusRect.pivot.y + STATUS_TOAST_EDGE_PADDING;
            float maxY = graphMaximum.y - statusBounds.height * (1f - statusRect.pivot.y) - STATUS_TOAST_EDGE_PADDING;
            float x = Mathf.Clamp(localPosition.x, minX, maxX);
            float y = Mathf.Clamp(localPosition.y, minY, maxY);
            statusRect.position = screenRect.TransformPoint(new Vector3(x, y, 0f));
            statusRect.SetAsLastSibling();
        }

        /// <summary>작업실 진입을 알리고 요청된 경우 스테이지 선택 UI를 연다.</summary>
        protected override void OnShown()
        {
            _presenter.Enter();
            if (!_showStageSelectionOnNextEntry) return;
            _showStageSelectionOnNextEntry = false;
            DeployClicked?.Invoke();
        }

        /// <summary>작업실을 떠날 때 Presenter에 퇴장을 알린다.</summary>
        protected override void OnHidden() => _presenter.Exit();

        void Update()
        {
            _presenter?.Tick();
        }

        void OnDestroy()
        {
            _presenter?.Dispose();
        }
    }
}
