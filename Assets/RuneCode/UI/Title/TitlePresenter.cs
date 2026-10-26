using UnityEngine;

namespace RuneCode
{
    /// <summary>타이틀의 새 진행·이어 하기 요청은 세션으로, 설정은 공용 팝업으로, 종료는 앱 종료로 처리한다.</summary>
    public sealed class TitlePresenter
    {
        private readonly RuneCodeSession _session;
        private readonly UIManager _ui;
        private readonly TitleScreen _view;

        /// <summary>세션·UI 관리자와 타이틀 View를 받아 시작·설정·종료 이벤트를 구독한다.</summary>
        public TitlePresenter(RuneCodeSession session, UIManager ui, TitleScreen view)
        {
            _session = session;
            _ui = ui;
            _view = view;
            view.StartClicked += OpenNewGame;
            view.ContinueClicked += OpenContinueGame;
            view.SettingsClicked += OpenSettings;
            view.DebugStartClicked += StartDebug;
            view.TestStartClicked += StartTest;
            view.QuitClicked += QuitGame;
        }

        /// <summary>기존 진행이 있으면 교체를 확인하고 단일 저장에 새 게임을 만든다.</summary>
        private void OpenNewGame()
        {
            if (SaveStore.HasSlot(SaveStore.ActiveSlot))
            {
                _ui.Confirm(GameData.L("ui.newGame"), GameData.L("save.replaceConfirm"), GameData.L("ui.newGame"),
                    StartNewGame, isDestructive: true);
                return;
            }
            StartNewGame();
        }

        /// <summary>새 진행 기록에 성공하면 첫 전투를 시작하고 실패하면 기존 진행을 유지한다.</summary>
        private void StartNewGame()
        {
            if (!_session.CreateSaveSlot(SaveStore.ActiveSlot))
            {
                _ui.Alert(GameData.L("ui.newGame"), GameData.L("save.unavailable"));
                return;
            }
            _session.StartGame(false);
        }

        /// <summary>단일 저장을 바로 읽고 마지막 완료 지점의 실험실에서 이어 한다.</summary>
        private void OpenContinueGame()
        {
            if (!_session.SelectSaveSlot(SaveStore.ActiveSlot))
            {
                _ui.ShowToast(GameData.L(SaveStore.HasSlot(SaveStore.ActiveSlot) ? "save.slotReadError" : "save.noContinueSlots"));
                return;
            }
            _session.StartGame(false, resumeAtLaboratory: true);
        }

        /// <summary>타이틀용 환경 설정을 열어 진행 데이터 변경 없이 기기 공통 설정을 조정한다.</summary>
        private void OpenSettings()
        {
            SettingsPopup popup = _ui.OpenPopup<SettingsPopup>();
            popup.Configure(_session, _ui, isTitle: true);
            popup.Closed += _view.RefreshSaveAvailability;
        }

        /// <summary>새 메모리 디버그 진행을 열고 전투로 바로 전환한다.</summary>
        private void StartDebug()
        {
            _session.StartGame(true);
        }

        /// <summary>익명 참가자 ID를 입력받아 저장하지 않는 테스트 진행과 로그 회차를 시작한다.</summary>
        private void StartTest()
        {
            RenamePopup popup = _ui.OpenPopup<RenamePopup>();
            popup.Configure(string.Empty, StartTestWithParticipantId);
            popup.SetPrompt(GameData.L("usabilityTest.participantTitle"), GameData.L("usabilityTest.participantPlaceholder"));
        }

        /// <summary>참가자 ID를 검증해 테스트 세션을 시작하고 실패하면 타이틀 화면에 안내한다.</summary>
        private void StartTestWithParticipantId(string participantId)
        {
            string normalizedParticipantId = participantId?.Trim();
            if (string.IsNullOrEmpty(normalizedParticipantId))
            {
                _ui.Alert(GameData.L("usabilityTest.participantTitle"), GameData.L("usabilityTest.participantRequired"));
                return;
            }
            if (!_session.StartTestGame(normalizedParticipantId))
                _ui.Alert(GameData.L("usabilityTest.participantTitle"), GameData.L("usabilityTest.startFailed"));
        }

        /// <summary>게임 프로세스 종료를 요청한다.</summary>
        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
