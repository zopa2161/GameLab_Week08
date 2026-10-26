using UnityEngine;

using UnityEngine.InputSystem;

namespace RuneCode
{
    /// <summary>
    /// 미션 화면 View다. 미션 월드(카메라·월드 표시)·HUD·결과 View를 묶고 진행은 MissionPresenter에 맡긴다.
    /// 작업실은 별도 화면 전환 없이 이 화면 위 오버레이로 두며, Esc는 열린 팝업, 작업실, 미션 순으로 처리한다.
    /// 월드는 UI 캔버스 배율을 받지 않도록 씬 루트에 따로 만들고 화면 표시 여부에 맞춰 켜고 끈다.
    /// </summary>
    public sealed class MissionScreen : UiView
    {
        [Header("연결")]
        [SerializeField] private MissionWorldView _worldPrefab;
        [SerializeField] private MissionHud _hud;
        [SerializeField] private ResultPanel _resultPanel;

        private RuneCodeSession _session;
        private UIManager _ui;
        private WorkshopScreen _workshop;
        private MissionPresenter _presenter;
        private MissionWorldView _worldView;
        private DamageVignetteView _damageVignette;

        /// <summary>미션 화면 위에 표시하는 작업실 오버레이다. Presenter가 표시·숨김과 닫기 이벤트에 쓴다.</summary>
        public WorkshopScreen Workshop => _workshop;

        /// <summary>세션·UI 관리자로 작업실 오버레이와 Presenter를 준비하고 미션 월드를 생성해 카메라·월드 표시를 연결한다. 이미 초기화했으면 무시한다.</summary>
        public void Initialize(RuneCodeSession session, UIManager ui)
        {
            if (_presenter != null) return;
            _session = session;
            _ui = ui;
            // 작업실은 Presenter보다 먼저 준비해 생성자에서 CloseRequested를 구독할 수 있게 한다.
            _workshop = ui.GetView<WorkshopScreen>(UiLayer.Screen);
            _workshop.Initialize(session, ui);
            _damageVignette = DamageVignetteView.Create((RectTransform)transform);
            _presenter = new MissionPresenter(session, ui, this, _hud, _resultPanel);
            _worldView = Instantiate(_worldPrefab);
            _worldView.name = _worldPrefab.name;
            _worldView.Camera.Initialize(() => _presenter.Simulation);
            _worldView.Initialize(() => _presenter.Simulation, () => _session.ScreenShake, () => _session.HitStop,
                () => _presenter.IsRoundBreakPortalActive, () => _presenter.IsRoundBreakPortalVisible, () => _session.Level);
            _worldView.gameObject.SetActive(false);
        }

        /// <summary>마우스가 게임 화면 안에 있으면 미션 카메라 기준 시뮬레이션 좌표의 조준점을 반환한다.</summary>
        public bool TryGetPointer(out SimVector pointer)
        {
            pointer = SimVector.Zero;
            Mouse mouse = Mouse.current;
            return mouse != null && _worldView.Camera.TryGetSimPoint(mouse.position.ReadValue(), out pointer);
        }

        /// <summary>피해 비네트를 strength(0~1) 강도로 점등한다. 점등 판단은 Presenter가 한다.</summary>
        public void ShowDamageFeedback(float strength)
        {
            _damageVignette.Flash(strength);
        }

        /// <summary>저체력 지속 비네트의 목표 강도(0~1)를 표시한다. 판단은 Presenter가 한다.</summary>
        public void ShowLowHealth(float level)
        {
            _damageVignette.SetLowHealth(level);
        }

        /// <summary>현재 미션의 레벨업 연출이 끝났는지 반환한다.</summary>
        public bool HasFinishedLevelUp(RuneSimulation simulation) => _worldView.HasFinishedLevelUp(simulation);

        /// <summary>전투 시간을 진행하지 않고 월드 종료 연출만 갱신하며 완료 여부를 반환한다.</summary>
        public bool AdvanceEnding(RuneSimulation simulation, float deltaTime)
            => _worldView.AdvanceEnding(simulation, deltaTime);

        /// <summary>작업실이 보이면 열린 팝업을 UIManager가 먼저 닫게 하고, 아니면 작업실에 Esc를 전달한다. 작업실이 없으면 Presenter에 전달한다.</summary>
        public override bool HandleEscape()
        {
            if (_workshop.IsVisible) return _ui.IsPopupOpen ? false : _workshop.HandleEscape();
            return _presenter != null && _presenter.HandleEscape();
        }

        /// <summary>미션 화면에 들어올 때마다 월드를 켜고 새 미션을 시작한다.</summary>
        protected override void OnShown()
        {
            _worldView.gameObject.SetActive(true);
            _presenter.Begin();
        }

        /// <summary>미션 화면을 떠날 때 작업실 오버레이를 먼저 닫고 월드를 끈다. 화면이 숨겨지는 경우는 타이틀 복귀뿐이다.</summary>
        protected override void OnHidden()
        {
            if (_workshop.IsVisible) _workshop.Hide();
            _presenter.End();
            _worldView.gameObject.SetActive(false);
        }

        void Update()
        {
            _presenter?.Tick(Time.unscaledDeltaTime);
        }

        void OnDestroy()
        {
            if (_worldView != null) Destroy(_worldView.gameObject);
        }
    }
}
