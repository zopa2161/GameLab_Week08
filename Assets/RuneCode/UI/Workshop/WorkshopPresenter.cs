using System;
using System.Collections.Generic;

using UnityEngine;

using UnityEngine.InputSystem;

namespace RuneCode
{
    /// <summary>
    /// 작업실 화면 Presenter다. 도크·강화 트리·출격을 조립하고 두 탭 전환, 스테이지 선택 복귀, 설정 팝업과 헤더·상태줄을 다룬다.
    /// 편집 패널(SpellEditorPanel)의 ISpellEditorHost 역할을 맡는다.
    /// </summary>
    public sealed class WorkshopPresenter : ISpellEditorHost, IDisposable
    {
        private const int HIGHLIGHT_TICKS = 12;
        private const float STATUS_TOAST_DURATION = 4f;

        private readonly RuneCodeSession _session;
        private readonly UIManager _ui;
        private readonly WorkshopScreen _view;
        private readonly DockRun _dockRun;
        private readonly DockPresenter _dock;
        private readonly UpgradeTreePresenter _upgradeTree;
        private readonly DeployPresenter _deploy;

        private string _selectedTab = WorkshopScreen.TAB_EDITOR;
        private string _returnTab = WorkshopScreen.TAB_EDITOR;
        private RuneSimulation _observedDock;
        private int _lastDockEventCount;
        private long _observedStatusRevision;
        private float _statusExpiresAt;
        private Vector2 _lastMouseScreenPosition;

        /// <summary>세션·UI 관리자·작업실 View로 도크 구동과 탭 Presenter를 만들고 편집 패널과 탭 이벤트를 연결한다.</summary>
        public WorkshopPresenter(RuneCodeSession session, UIManager ui, WorkshopScreen view)
        {
            _session = session;
            _observedStatusRevision = session.StatusRevision;
            _ui = ui;
            _view = view;
            _dockRun = new DockRun(session);
            _dock = new DockPresenter(session, _dockRun, view.Dock, IsEditorInputBlocked);
            view.SpellEditor.Initialize(session.Spells, this, ui);
            _upgradeTree = new UpgradeTreePresenter(session, view.UpgradeTree, RefreshProgression);
            _deploy = new DeployPresenter(session, view.Deploy);
            _view.TabClicked += SetTab;
            _view.DeployClicked += OpenDeploy;
            _view.SettingsClicked += OpenSettings;
            _view.HelpClicked += OpenHelp;
            _view.BackClicked += ReturnFromDeploy;
            _session.Spells.Compiled += RefreshHeader;
            _session.Spells.Compiled += RecordCompileResult;
        }

        /// <summary>작업실에 들어올 때 도크를 다시 시작한 뒤 에디터 탭을 갱신한다.</summary>
        public void Enter()
        {
            LocalTelemetry.BeginUsabilityEditingEpisode();
            ShowEditorTab();
        }

        /// <summary>작업실을 떠날 때 편집 패널을 닫고 도크 누적 시간을 버린다.</summary>
        public void Exit()
        {
            LocalTelemetry.EndUsabilityEditingEpisode("screen_exit");
            _view.SpellEditor.Hide();
            _dock.Suspend();
        }

        /// <summary>프레임마다 편집 세션 시간 처리, 상태 토스트 만료, 에디터 탭의 도크 진행과 실행 하이라이트를 한다.</summary>
        public void Tick()
        {
            _session.Spells.Tick(Time.unscaledTime);
            LocalTelemetry.FlushUsabilityTest();
            Mouse mouse = Mouse.current;
            if (mouse != null) _lastMouseScreenPosition = mouse.position.ReadValue();
            bool hasNewStatus = _observedStatusRevision != _session.StatusRevision;
            if (hasNewStatus)
            {
                _observedStatusRevision = _session.StatusRevision;
                _statusExpiresAt = string.IsNullOrEmpty(_session.StatusMessage)
                    ? 0f
                    : Time.unscaledTime + STATUS_TOAST_DURATION;
            }
            string status = Time.unscaledTime < _statusExpiresAt ? _session.StatusMessage : string.Empty;
            _view.SetStatus(status);
            if (hasNewStatus && !string.IsNullOrEmpty(status)) _view.SetStatusPosition(_lastMouseScreenPosition);
            if (_selectedTab != WorkshopScreen.TAB_EDITOR) return;
            _dock.Tick();
            ForwardDockHighlights();
        }

        /// <summary>컴파일 이벤트 구독을 풀고 도크 구동을 정리한다.</summary>
        public void Dispose()
        {
            _view.TabClicked -= SetTab;
            _view.DeployClicked -= OpenDeploy;
            _view.SettingsClicked -= OpenSettings;
            _view.HelpClicked -= OpenHelp;
            _view.BackClicked -= ReturnFromDeploy;
            _session.Spells.Compiled -= RefreshHeader;
            _session.Spells.Compiled -= RecordCompileResult;
            _dockRun.Dispose();
        }

        int ISpellEditorHost.EquippedRam => _session.EquippedRam;

        int ISpellEditorHost.Capacity => _session.Capacity;

        float ISpellEditorHost.MaxEnergy => _session.MaxEnergy;

        float ISpellEditorHost.EnergyRegen => _session.EnergyRegen;

        bool ISpellEditorHost.CanSynthesizeRunes => !_session.IsTestMode;

        int ISpellEditorHost.SynthesisStage => _session.SelectedStage;

        int ISpellEditorHost.SynthesisScrap => _session.Save.Currency;

        /// <summary>편집 UI의 재료·등급을 세션에 전달하고 합성 결과 또는 오류를 반환한다.</summary>
        bool ISpellEditorHost.TrySynthesizeRunes(IReadOnlyList<string> materials, string grade,
            out string resultId, out string resultGrade, out string error)
        {
            return _session.TrySynthesizeRunes(materials, grade, out resultId, out resultGrade, out error);
        }

        void ISpellEditorHost.FireTest() => _dockRun.FireDock();

        /// <summary>
        /// 탭을 바꾸고 진입한 탭의 내용과 헤더를 갱신한다. 에디터 탭을 떠나면 편집 패널을 닫고 도크를 멈춘다.
        /// 보관 중인 런이 있으면 진입 버튼을 전투 복귀로 표시한다.
        /// </summary>
        private void SetTab(string tab)
        {
            _selectedTab = tab;
            _view.ShowTab(tab);
            _view.SetDeployResume(_session.IsRunSuspended);
            if (tab == WorkshopScreen.TAB_EDITOR) _view.SpellEditor.Show();
            else
            {
                _view.SpellEditor.Hide();
                _dock.Suspend();
            }
            if (tab == WorkshopScreen.TAB_TREE) _upgradeTree.Refresh();
            if (tab == WorkshopScreen.TAB_DEPLOY) _deploy.Refresh();
            RefreshHeader();
        }

        /// <summary>
        /// 직전에 보던 룬 설계 또는 강화 트리 탭을 기억하고 스테이지 선택 화면을 연다.
        /// 보관 중인 런이 있으면 스테이지 선택 없이 바로 전투 복귀를 요청한다.
        /// </summary>
        private void OpenDeploy()
        {
            if (_session.IsRunSuspended)
            {
                _deploy.Launch();
                return;
            }
            if (_selectedTab == WorkshopScreen.TAB_DEPLOY) return;
            _returnTab = _selectedTab;
            SetTab(WorkshopScreen.TAB_DEPLOY);
        }

        /// <summary>스테이지 선택에서 직전에 보던 작업실 탭으로 돌아간다.</summary>
        private void ReturnFromDeploy()
        {
            if (_selectedTab == WorkshopScreen.TAB_DEPLOY) SetTab(_returnTab);
        }

        /// <summary>현재 화면을 유지한 채 설정 팝업을 열고 세션과 설정 변경 후 갱신 동작을 연결한다.</summary>
        private void OpenSettings()
        {
            _ui.OpenPopup<SettingsPopup>().Configure(_session, _ui, ShowEditorTab, RefreshProgression);
        }

        /// <summary>헤더 도움말 버튼으로 가장 최근에 개방된 항목을 선택한 도움말을 연다.</summary>
        private void OpenHelp()
        {
            _ui.OpenPopup<EncyclopediaPopup>().Configure(GameData.Encyclopedia, _session.UnlockedHelpCount, 0);
        }

        /// <summary>도크를 다시 시작한 뒤 에디터 탭으로 전환하고 출격·헤더를 갱신한다.</summary>
        private void ShowEditorTab()
        {
            _dockRun.ResetDock();
            SetTab(WorkshopScreen.TAB_EDITOR);
            _deploy.Refresh();
        }

        /// <summary>구매 등으로 진행이 바뀌면 도크를 다시 시작하고 트리·출격·헤더를 갱신한다.</summary>
        private void RefreshProgression()
        {
            _dockRun.ResetDock();
            _upgradeTree.Refresh();
            _deploy.Refresh();
            RefreshHeader();
        }

        /// <summary>보유 조각과 단일 마법의 RAM 사용량·용량을 헤더에 표시한다.</summary>
        private void RefreshHeader()
        {
            //_view.SetHeader(GameData.L("ui.fragments") + "  " + _session.Save.Currency + "   /   " + GameData.L("ui.capacity") + " "
            //    + _session.EquippedRam + "/" + _session.Capacity);
            _view.SetHeader(GameData.L("ui.fragments") + "  " + _session.Save.Currency);
        }

        /// <summary>컴파일 변경을 테스트 로그에 결과·오류·그래프 크기로 기록한다.</summary>
        private void RecordCompileResult()
        {
            if (!LocalTelemetry.IsUsabilityTestActive) return;
            CompileResult result = _session.Spells.CompileResult;
            var errors = new List<string>(result.Errors.Count);
            foreach (CompileIssue issue in result.Errors) errors.Add(issue.Code);
            var warnings = new List<string>(result.Warnings.Count);
            foreach (CompileIssue warning in result.Warnings) warnings.Add(warning.Code);
            LocalTelemetry.RecordUsabilityEvent("compile_result", "editor", new Dictionary<string, object>
            {
                ["is_valid"] = result.Ok,
                ["errors"] = errors,
                ["warnings"] = warnings,
                ["ram_used"] = result.Ok ? result.Spell.RamUsed : (int?)null,
                ["node_count"] = _session.Spells.Graph.Nodes.Count,
                ["edge_count"] = _session.Spells.Graph.Edges.Count,
            });
        }

        /// <summary>팝업이 열려 있어 도크 클릭 시전을 막아야 하는지 반환한다.</summary>
        private bool IsEditorInputBlocked()
        {
            return _ui.IsPopupOpen || _view.SpellEditor.IsSynthesisOpen;
        }

        /// <summary>최근 도크 노드 실행 중 하이라이트 유지 틱 이내의 것을 편집 그래프 하이라이트로 전달한다.</summary>
        private void ForwardDockHighlights()
        {
            RuneSimulation dock = _dockRun.Dock;
            if (dock == null) return;
            if (_observedDock != dock)
            {
                _observedDock = dock;
                _lastDockEventCount = 0;
            }
            int newEvents = dock.NodeExecutionCount - _lastDockEventCount;
            for (int index = Math.Max(0, dock.NodeEvents.Count - newEvents); index < dock.NodeEvents.Count; index++)
            {
                NodeExecutionEvent entry = dock.NodeEvents[index];
                if (dock.Tick - entry.Tick <= HIGHLIGHT_TICKS) _view.SpellEditor.HighlightNode(entry.NodeId);
            }
            _lastDockEventCount = dock.NodeExecutionCount;
        }
    }
}
