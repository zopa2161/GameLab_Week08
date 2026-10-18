using UnityEngine;

namespace RuneCode
{
    /// <summary>타이틀 시작 요청은 세션으로, 저장 관리는 슬롯 팝업으로, 종료 요청은 앱 종료로 처리한다.</summary>
    public sealed class TitlePresenter
    {
        private readonly RuneCodeSession _session;
        private readonly UIManager _ui;

        /// <summary>세션·UI 관리자와 타이틀 View를 받아 시작·슬롯 관리·종료 이벤트를 구독한다.</summary>
        public TitlePresenter(RuneCodeSession session, UIManager ui, TitleScreen view)
        {
            _session = session;
            _ui = ui;
            view.StartClicked += OpenNewGame;
            view.ContinueClicked += OpenContinueGame;
            view.SettingsClicked += OpenSettings;
            view.DebugStartClicked += StartDebug;
            view.SaveManagementClicked += OpenSaveManagement;
            view.QuitClicked += QuitGame;
        }

        /// <summary>빈 저장 슬롯이 있으면 새로 하기 선택 화면을 열고, 없으면 저장 관리 안내를 표시한다.</summary>
        private void OpenNewGame()
        {
            if (!SaveSlotsPopup.HasEligibleSlot(SaveSlotsPopup.Mode.NewGame))
            {
                _ui.ShowToast(GameData.L("save.noEmptySlots"));
                return;
            }
            _ui.OpenPopup<SaveSlotsPopup>().Configure(_session, _ui, SaveSlotsPopup.Mode.NewGame);
        }

        /// <summary>읽을 수 있는 저장이 있으면 이어 하기 선택 화면을 열고, 없으면 안내를 표시한다.</summary>
        private void OpenContinueGame()
        {
            if (!SaveSlotsPopup.HasEligibleSlot(SaveSlotsPopup.Mode.ContinueGame))
            {
                _ui.ShowToast(GameData.L("save.noContinueSlots"));
                return;
            }
            _ui.OpenPopup<SaveSlotsPopup>().Configure(_session, _ui, SaveSlotsPopup.Mode.ContinueGame);
        }

        /// <summary>타이틀용 환경 설정을 열어 진행 데이터 변경 없이 기기 공통 설정을 조정한다.</summary>
        private void OpenSettings()
        {
            _ui.OpenPopup<SettingsPopup>().Configure(_session, _ui, isTitle: true);
        }

        /// <summary>새 메모리 디버그 진행을 열고 작업실로 전환한다.</summary>
        private void StartDebug()
        {
            _session.StartGame(true);
        }

        /// <summary>타이틀 위에 저장 슬롯 관리 팝업을 연다.</summary>
        private void OpenSaveManagement()
        {
            _ui.OpenPopup<SaveSlotsPopup>().Configure(_session, _ui);
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
