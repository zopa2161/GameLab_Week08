using System;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 작업실 화면 View다. 헤더·탭·상태줄과 탭별 패널 View를 묶고 탭 클릭을 알린다.
    /// UIManager 화면 전환 대신 미션 화면 위 오버레이로 표시하며, 진행은 WorkshopPresenter가 하고
    /// UIManager가 재사용하므로 초기화는 한 번, 진입 갱신은 표시할 때마다 한다.
    /// </summary>
    public sealed class WorkshopScreen : UiView
    {
        public const string TAB_EDITOR = "editor";
        public const string TAB_TREE = "tree";
        private static readonly string[] TABS = { TAB_EDITOR, TAB_TREE };
        private const float STATUS_TOAST_EDGE_PADDING = 12f;

        [Header("탭 구성")]
        [SerializeField] private RectTransform _editorRoot;
        [SerializeField] private RectTransform _treeRoot;
        [SerializeField] private Button[] _tabButtons;

        [Header("헤더 버튼")]
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _helpButton;

        [Header("패널")]
        [SerializeField] private SpellEditorPanel _spellEditor;
        [SerializeField] private UpgradeTreePanel _upgradeTreePanel;

        [Header("표시")]
        [SerializeField] private TextMeshProUGUI _headerStats;
        [SerializeField] private TextMeshProUGUI _status;

        private WorkshopPresenter _presenter;

        public SpellEditorPanel SpellEditor => _spellEditor;
        public UpgradeTreePanel UpgradeTree => _upgradeTreePanel;

        /// <summary>탭 버튼을 눌렀을 때 탭 ID와 함께 알린다.</summary>
        public event Action<string> TabClicked;

        /// <summary>작업실 오버레이를 닫으라고 알린다.</summary>
        public event Action CloseRequested;

        /// <summary>현재 화면 위에 게임 도움말 팝업 열기를 알린다.</summary>
        public event Action HelpClicked;

        /// <summary>탭 버튼을 이벤트로 연결하고 세션·UI 관리자로 Presenter를 만든다. 이미 초기화했으면 무시한다.</summary>
        public void Initialize(RuneCodeSession session, UIManager ui)
        {
            if (_presenter != null) return;
            for (int i = 0; i < TABS.Length; i++)
            {
                string tab = TABS[i];
                _tabButtons[i].onClick.AddListener(() => TabClicked?.Invoke(tab));
            }
            _closeButton.onClick.AddListener(() => CloseRequested?.Invoke());
            _helpButton.onClick.AddListener(() => HelpClicked?.Invoke());
            _presenter = new WorkshopPresenter(session, ui, this);
        }

        /// <summary>지정 화면만 표시하고 탭 버튼의 선택 상태를 맞춘다.</summary>
        public void ShowTab(string tab)
        {
            _editorRoot.gameObject.SetActive(tab == TAB_EDITOR);
            _treeRoot.gameObject.SetActive(tab == TAB_TREE);
            for (int i = 0; i < TABS.Length; i++)
                _tabButtons[i].GetComponent<TabButton>().SetSelected(TABS[i] == tab);
        }

        /// <summary>집어 든 모듈을 먼저 취소하고 합성창을 닫은 뒤, 그 외에는 오버레이 닫기를 알리고 Esc 입력을 소비한다.</summary>
        public override bool HandleEscape()
        {
            if (_spellEditor.GraphCanvas.CancelPickedModule()) return true;
            if (_spellEditor.IsSynthesisOpen) return _spellEditor.HandleSynthesisEscape();
            CloseRequested?.Invoke();
            return true;
        }

        /// <summary>헤더 수치 문구를 표시한다.</summary>
        public void SetHeader(string text) => _headerStats.text = text;

        /// <summary>상태 토스트 문구를 표시한다.</summary>
        public void SetStatus(string text) => _status.text = text;

        /// <summary>Screen Space Overlay 좌표의 토스트를 편집 영역과 무관하게 배치하고 게임 전체 화면 가장자리에서만 위치를 보정한다.</summary>
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
            RectTransformUtility.ScreenPointToLocalPointInRectangle(screenRect, Vector2.zero, null, out Vector2 screenMinimum);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(screenRect, new Vector2(Screen.width, Screen.height), null,
                out Vector2 screenMaximum);
            Rect statusBounds = statusRect.rect;
            float minX = screenMinimum.x + statusBounds.width * statusRect.pivot.x + STATUS_TOAST_EDGE_PADDING;
            float maxX = screenMaximum.x - statusBounds.width * (1f - statusRect.pivot.x) - STATUS_TOAST_EDGE_PADDING;
            float minY = screenMinimum.y + statusBounds.height * statusRect.pivot.y + STATUS_TOAST_EDGE_PADDING;
            float maxY = screenMaximum.y - statusBounds.height * (1f - statusRect.pivot.y) - STATUS_TOAST_EDGE_PADDING;
            float x = Mathf.Clamp(localPosition.x, minX, maxX);
            float y = Mathf.Clamp(localPosition.y, minY, maxY);
            statusRect.position = screenRect.TransformPoint(new Vector3(x, y, 0f));
            statusRect.SetAsLastSibling();
        }

        /// <summary>작업실 진입을 알린다.</summary>
        protected override void OnShown() => _presenter.Enter();

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
