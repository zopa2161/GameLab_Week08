using System;
using System.Linq;

using UnityEngine;
using UnityEngine.SceneManagement;

using UnityEngine.InputSystem;

namespace RuneCode
{
    /// <summary>
    /// Boot 씬의 진입 컴포넌트다. 데이터·세이브·세션과 UIManager를 초기화하고, 화면 전환 요청에 따라
    /// UIManager로 화면 Prefab(타이틀·작업실·미션)을 표시한다. 화면은 처음 표시할 때 한 번만 초기화하고 재사용한다.
    /// </summary>
    public sealed class RuneCodeApp : MonoBehaviour
    {
        private const string BOOT_SCENE = "Boot";

        private RuneCodeSession _session;
        private UIManager _ui;
        private bool _isSwitching;
        private AppScreen? _pendingScreen;

#if UNITY_EDITOR
        /// <summary>에디터에서 기능 씬을 직접 실행할 때 Boot 씬이 없으면 단독으로 로드해 부팅 흐름을 보장한다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootScene()
        {
            if (SceneManager.GetSceneByName(BOOT_SCENE).isLoaded) return;
            SceneManager.LoadScene(BOOT_SCENE, LoadSceneMode.Single);
        }
#endif

        void Awake()
        {
            GameData.Load();
            GameData.SetCarrier(CarrierSpawnSettings.LoadDefinition());
            if (SimulationCli.TryRunCommandLine()) { enabled = false; return; }
            Application.targetFrameRate = GameData.Balance.Sim.TickRate;
            PlayerSave save = SaveStore.Load();
            StageCatalog stages = GameData.Stages;
            UpgradeTreeDefinition upgradeTree = Resources.Load<UpgradeTreeDefinition>("RuneCode/UpgradeTree");
            if (upgradeTree == null) throw new InvalidOperationException("업그레이드 트리 자산이 없습니다: RuneCode/UpgradeTree");
            if (!upgradeTree.Validate(out string treeError)) throw new InvalidOperationException(treeError);
            bool isDebug = Environment.GetCommandLineArgs().Contains("-debug") || Application.absoluteURL.Contains("debug=1");
            _session = new RuneCodeSession(save, stages, upgradeTree, isDebug, SaveStore.LastWarning);
            _session.ScreenRequested += OnScreenRequested;
            if (SoundManager.Instance == null)
            {
                GameObject soundRoot = new GameObject("SoundManager");
                soundRoot.transform.SetParent(transform, false);
                soundRoot.AddComponent<SoundManager>();
            }
            _ui = UIManager.Create(transform);
            LocalTelemetry.Record(0, "session", "start");
            UsabilityLogUploader.AttachIfConfigured(gameObject,
                () => LocalTelemetry.EndUsabilityTest("application_quit", _session?.Spells.Graph));
            OnScreenRequested(AppScreen.Title);
        }

        void OnDestroy()
        {
            if (_session != null) _session.ScreenRequested -= OnScreenRequested;
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || UiFactory.IsTyping()) return;
            if (keyboard.f8Key.wasPressedThisFrame) LocalTelemetry.DumpToConsole();
        }

        void OnApplicationQuit()
        {
            LocalTelemetry.EndUsabilityTest("application_quit", _session?.Spells.Graph);
            if (_session != null) _session.SaveAll();
        }

        /// <summary>
        /// 요청한 화면을 표시한다. 화면 표시 도중(진입 처리 안)에 온 요청은 현재 전환이 끝난 뒤 이어서 처리한다.
        /// </summary>
        private void OnScreenRequested(AppScreen screen)
        {
            if (_isSwitching)
            {
                _pendingScreen = screen;
                return;
            }
            _isSwitching = true;
            ShowScreen(screen);
            while (_pendingScreen.HasValue)
            {
                AppScreen next = _pendingScreen.Value;
                _pendingScreen = null;
                ShowScreen(next);
            }
            _isSwitching = false;
        }

        /// <summary>화면 종류에 맞는 화면 View를 UIManager로 표시하고, 처음 표시라면 세션과 UIManager로 초기화한다.</summary>
        private void ShowScreen(AppScreen screen)
        {
            switch (screen)
            {
                case AppScreen.Title: _ui.ShowScreen<TitleScreen>(view => view.Initialize(_session, _ui)); break;
                case AppScreen.Workshop: _ui.ShowScreen<WorkshopScreen>(view => view.Initialize(_session, _ui)); break;
                case AppScreen.Mission: _ui.ShowScreen<MissionScreen>(view => view.Initialize(_session, _ui)); break;
            }
        }
    }
}
