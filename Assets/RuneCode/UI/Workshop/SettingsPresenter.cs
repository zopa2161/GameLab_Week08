using System;

using UnityEngine;

namespace RuneCode
{
    /// <summary>설정 내용 Presenter다. 저장·디버그 동작과 타이틀 복귀·게임 종료 요청을 처리한다.</summary>
    public sealed class SettingsPresenter
    {
        private readonly RuneCodeSession _session;
        private readonly UIManager _ui;
        private readonly SettingsPanel _view;
        private readonly Action _onReset;
        private readonly Action _onProgressionChanged;
        private bool _isTitle;

        /// <summary>
        /// 세션·UI 관리자·설정 View와 초기화 후 동작(에디터 탭 복귀), 디버그 변경 후 동작(헤더 갱신)을 받아 View 이벤트를 연결한다.
        /// </summary>
        public SettingsPresenter(RuneCodeSession session, UIManager ui, SettingsPanel view, Action onReset, Action onProgressionChanged)
        {
            _session = session;
            _ui = ui;
            _view = view;
            _onReset = onReset;
            _onProgressionChanged = onProgressionChanged;
            _view.Bind();
            _view.ScreenShakeClicked += ToggleScreenShake;
            _view.HitStopClicked += ToggleHitStop;
            _view.SoundSettingsClicked += OpenSoundSettings;
            _view.ResetSaveClicked += ConfirmReset;
            _view.DebugGrantClicked += DebugGrant;
            _view.DebugUnlockClicked += DebugUnlock;
            _view.DebugTreeResetClicked += ResetDebugTree;
            _view.ManualSaveClicked += SaveManually;
            _view.ManualLoadClicked += OpenSaveSlots;
            _view.QuitClicked += QuitGame;
            _view.ReturnToTitleClicked += ReturnToTitle;
        }

        /// <summary>진행 종류에 따른 디버그 도구·저장 버튼·초기화 문구와 화면 흔들림·히트스톱·저장 설정의 현재 값을 표시한다.</summary>
        public void Refresh()
        {
            _view.SetSessionMode(_session.IsDebugEnabled, !_session.IsMemorySession, GetResetTitleKey(), _isTitle);
            _view.SetFeedback(_session.ScreenShake, _session.HitStop);
            _view.SetSaveOptions(_session.IsSaveDirty, !_session.IsMemorySession, !_session.IsMemorySession);
        }

        /// <summary>설정의 진입 위치를 갱신하고 타이틀에서 사용할 수 있는 항목만 표시한다.</summary>
        public void SetTitleMode(bool isTitle)
        {
            _isTitle = isTitle;
            Refresh();
        }

        /// <summary>화면 흔들림 설정을 반전시켜 저장하고 문구를 갱신한다.</summary>
        private void ToggleScreenShake()
        {
            _session.SetSetting("screenShake", !_session.ScreenShake);
            Refresh();
        }

        /// <summary>히트스톱 설정을 반전시켜 저장하고 문구를 갱신한다.</summary>
        private void ToggleHitStop()
        {
            _session.SetSetting("hitStop", !_session.HitStop);
            Refresh();
        }

        /// <summary>현재 설정 팝업 위에 기기 공통 소리 설정 팝업을 연다.</summary>
        private void OpenSoundSettings()
        {
            _ui.OpenPopup<SoundSettingsPopup>();
        }

        /// <summary>공용 확인 팝업으로 저장 초기화를 묻고, 확인하면 진행을 초기화한 뒤 초기화 후 동작을 실행한다.</summary>
        private void ConfirmReset()
        {
            string titleKey = GetResetTitleKey();
            string messageKey = _session.IsTestMode ? "ui.resetTestSessionMessage"
                : _session.IsDebugEnabled ? "ui.resetDebugSessionMessage" : "ui.resetSaveMessage";
            _ui.Confirm(GameData.L(titleKey), GameData.L(messageKey), GameData.L(titleKey), () =>
            {
                _session.ResetSave();
                _onReset();
            });
        }

        /// <summary>진행 종류(테스트·디버그·일반)에 맞는 초기화 버튼·확인 팝업 제목 문자열 키를 반환한다.</summary>
        private string GetResetTitleKey()
        {
            return _session.IsTestMode ? "ui.resetTestSession"
                : _session.IsDebugEnabled ? "ui.resetDebugSession" : "ui.resetSave";
        }

        /// <summary>디버그 실행에서만 조각을 지급하고 진행 표시를 갱신한다.</summary>
        private void DebugGrant()
        {
            _session.DebugGrant();
            _onProgressionChanged();
        }

        /// <summary>디버그 실행에서만 모든 룬을 해금하고 진행 표시를 갱신한다.</summary>
        private void DebugUnlock()
        {
            _session.DebugUnlock();
            _onProgressionChanged();
        }

        /// <summary>디버그 세션의 스탯 강화 레벨을 초기화하고 트리·전투 표시를 갱신한다.</summary>
        private void ResetDebugTree()
        {
            _session.ResetDebugTree();
            _onProgressionChanged();
        }

        /// <summary>현재 진행과 편집 중인 마법을 수동 저장하고 결과를 알린다.</summary>
        private void SaveManually()
        {
            bool isSaved = _session.SaveManually();
            _ui.Alert(GameData.L("save.save"), GameData.L(isSaved ? "save.saved" : "save.unavailable"));
            Refresh();
        }

        /// <summary>저장 슬롯 목록을 열어 선택한 저장 진행으로 전환할 수 있게 한다.</summary>
        private void OpenSaveSlots()
        {
            _ui.OpenPopup<SaveSlotsPopup>().Configure(_session, _ui, SaveSlotsPopup.Mode.SettingsLoad, SaveSlotChanged);
        }

        /// <summary>슬롯 전환 후 작업실 진행 표시와 설정의 활성 슬롯 문구를 갱신한다.</summary>
        private void SaveSlotChanged()
        {
            _onProgressionChanged();
            Refresh();
        }

        /// <summary>세션에 편집 내용 반영과 로그 마감 및 타이틀 화면 전환을 요청한다.</summary>
        private void ReturnToTitle()
        {
            _session.ReturnToTitle();
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
