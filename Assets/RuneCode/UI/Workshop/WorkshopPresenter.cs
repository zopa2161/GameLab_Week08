using System;
using System.Collections.Generic;

using UnityEngine;

using UnityEngine.InputSystem;

namespace RuneCode
{
    /// <summary>
    /// 작업실 화면 Presenter다. 강화 트리를 조립하고 두 탭 전환과 헤더·상태줄을 다룬다.
    /// 편집 패널(SpellEditorPanel)의 ISpellEditorHost 역할을 맡는다.
    /// </summary>
    public sealed class WorkshopPresenter : ISpellEditorHost, IDisposable
    {
        private const float STATUS_TOAST_DURATION = 4f;
        private const int ONBOARDING_WORKSHOP_ENTER = 4;
        private const int ONBOARDING_UPGRADE_TREE = 7;

        private readonly RuneCodeSession _session;
        private readonly UIManager _ui;
        private readonly WorkshopScreen _view;
        private readonly UpgradeTreePresenter _upgradeTree;

        private string _selectedTab = WorkshopScreen.TAB_EDITOR;
        public string SelectedTab => _selectedTab;
        private long _observedStatusRevision;
        private float _statusExpiresAt;
        private Vector2 _lastMouseScreenPosition;

        /// <summary>세션·UI 관리자·작업실 View로 탭 Presenter를 만들고 편집 패널과 탭 이벤트를 연결한다.</summary>
        public WorkshopPresenter(RuneCodeSession session, UIManager ui, WorkshopScreen view)
        {
            _session = session;
            _observedStatusRevision = session.StatusRevision;
            _ui = ui;
            _view = view;
            view.SpellEditor.Initialize(session.Spells, this, ui);
            _upgradeTree = new UpgradeTreePresenter(session, view.UpgradeTree, RefreshProgression);
            _view.TabClicked += SetTab;
            _view.HelpClicked += OpenHelp;
            _session.Spells.Compiled += RefreshHeader;
            _session.Spells.Compiled += RecordCompileResult;
        }

        /// <summary>작업실에 들어올 때 요청한 탭을 갱신하고 일반 진행의 작업실 온보딩 차례면 띄운다.</summary>
        public void Enter(string tab = WorkshopScreen.TAB_EDITOR)
        {
            _selectedTab = tab;
            LocalTelemetry.BeginUsabilityEditingEpisode(_session.GetWorkshopLogSnapshot, tab);
            SetTab(tab);
        }

        /// <summary>일반 진행에서 chapter가 다음 순서면 도움말 챕터를 안내 팝업으로 열고 순서를 넘겼으면 true를 반환한다. 챕터가 없으면 경고만 남긴다.</summary>
        private bool ShowWorkshopOnboarding(int chapter)
        {
            if (!_session.TryAdvanceOnboarding(chapter)) return false;
            EncyclopediaEntry entry = GameData.Encyclopedia?.FindChapter(chapter);
            if (entry == null)
            {
                Debug.LogWarning("게임 도움말 JSON에 온보딩 챕터가 없습니다: " + chapter);
                return true;
            }
            _ui.OpenPopup<OnboardingPopup>().Configure(entry.Title, entry.Pages);
            return true;
        }

        /// <summary>작업실을 떠날 때 편집 패널을 닫는다.</summary>
        public void Exit()
        {
            LocalTelemetry.EndUsabilityEditingEpisode("screen_exit");
            _view.SpellEditor.Hide();
        }

        /// <summary>프레임마다 편집 세션 시간 처리와 상태 토스트 만료를 한다.</summary>
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
        }

        /// <summary>컴파일 이벤트 구독을 푼다.</summary>
        public void Dispose()
        {
            _view.TabClicked -= SetTab;
            _view.HelpClicked -= OpenHelp;
            _session.Spells.Compiled -= RefreshHeader;
            _session.Spells.Compiled -= RecordCompileResult;
        }

        float ISpellEditorHost.MaxEnergy => _session.MaxEnergy;

        float ISpellEditorHost.EnergyRegen => _session.EnergyRegen;

        bool ISpellEditorHost.CanSynthesizeRunes => true;

        int ISpellEditorHost.SynthesisStage => _session.SelectedStage;

        int ISpellEditorHost.SynthesisScrap => _session.Save.Currency;

        /// <summary>편집 UI의 재료·등급을 세션에 전달하고 합성 결과 또는 오류를 반환한다.</summary>
        bool ISpellEditorHost.TrySynthesizeRunes(IReadOnlyList<string> materials, string grade,
            out string resultId, out string resultGrade, out string error)
        {
            return _session.TrySynthesizeRunes(materials, grade, out resultId, out resultGrade, out error);
        }

        /// <summary>탭의 내용과 헤더를 갱신한다. 편집창 진입 시 차례가 된 온보딩을 표시하고 테스트 방문을 기록하며, 편집창을 떠나면 편집 패널을 닫는다.</summary>
        public void SetTab(string tab)
        {
            if (_selectedTab != tab)
                LocalTelemetry.RecordUsabilityEvent("workshop_tab_switch", "workshop", new Dictionary<string, object>
                {
                    ["from_tab"] = _selectedTab, ["to_tab"] = tab,
                });
            _selectedTab = tab;
            _view.ShowTab(tab);
            if (tab == WorkshopScreen.TAB_EDITOR) _view.SpellEditor.Show();
            else _view.SpellEditor.Hide();
            if (tab == WorkshopScreen.TAB_TREE) _upgradeTree.Refresh();
            RefreshHeader();
            if (tab == WorkshopScreen.TAB_EDITOR && !_ui.IsPopupOpen)
            {
                if (!ShowWorkshopOnboarding(ONBOARDING_WORKSHOP_ENTER)) ShowWorkshopOnboarding(ONBOARDING_UPGRADE_TREE);
                if (_session.OnboardingChapter >= ONBOARDING_WORKSHOP_ENTER) _session.RecordOnboardingEditorVisit();
            }
        }

        /// <summary>헤더 도움말 버튼으로 가장 최근에 개방된 항목을 선택한 도움말을 연다.</summary>
        private void OpenHelp()
        {
            _ui.OpenPopup<EncyclopediaPopup>().Configure(GameData.Encyclopedia, _session.UnlockedHelpCount, 0);
        }

        /// <summary>구매 등으로 진행이 바뀌면 트리·헤더를 갱신한다.</summary>
        private void RefreshProgression()
        {
            _upgradeTree.Refresh();
            RefreshHeader();
        }

        /// <summary>보유 강화 포인트를 기존 아이콘 옆 헤더에 표시한다.</summary>
        private void RefreshHeader()
        {
            _view.SetHeader(string.Format(GameData.L("ui.tree.points"), _session.Save.UpgradePoints));
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
                ["node_count"] = _session.Spells.Graph.Nodes.Count,
                ["edge_count"] = _session.Spells.Graph.Edges.Count,
            });
        }
    }
}
