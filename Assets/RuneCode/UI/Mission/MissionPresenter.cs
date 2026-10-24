using System;
using System.Collections.Generic;

using UnityEngine;

using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RuneCode
{
    /// <summary>
    /// 미션 화면의 Presenter다. 세션에서 시전 마법을 받아 MissionRun(모델)을 만들고 진행·일시정지·재도전·결과와
    /// 작업실 오버레이 표시를 다루며, 매 프레임 시뮬레이션 값을 HUD View에 표시한다. 일시정지·디버그 팝업은 UIManager로 연다.
    /// </summary>
    public sealed class MissionPresenter
    {
        private enum HexaDebugActionKind { AddPair, StartSummon, Kill }

        private const float TOAST_DURATION = 0.9f;
        private const float ITEM_TOAST_DURATION = 2.5f;
        private const float VIGNETTE_BASE_STRENGTH = 0.45f;
        private const float VIGNETTE_DAMAGE_STRENGTH = 2f;
        private const float LOW_HEALTH_FRACTION = 0.3f;
        private const float LOW_HEALTH_MAX_LEVEL = 0.4f;
        private const double PLAYER_DAMAGE_EPSILON = 0.000001;
        private const int ONBOARDING_BATTLE_START = 1;
        private const int ONBOARDING_FIRST_CLEAR = 2;
        private const int ONBOARDING_ROUND_RETURN = 4;
        private const int ONBOARDING_SPELL_ORDER = 5;
        private const int ONBOARDING_SPELL_ORDER_STAGE = 1;
        private const int ONBOARDING_SPELL_ORDER_ROUND = 3;
        private const int MAX_HEXA_DEBUG_TARGETS = 2;

        private readonly RuneCodeSession _session;
        private readonly UIManager _ui;
        private readonly MissionScreen _screen;
        private readonly MissionHud _hud;
        private readonly ResultPanel _result;

        private readonly Queue<string> _itemToasts = new Queue<string>();
        private readonly List<(int Stage, int Round, bool IsHexaPreview, bool StartsNormalWave, bool StartsSpiralPreview, bool StartsAutoPreview)> _debugBossTargets
            = new List<(int Stage, int Round, bool IsHexaPreview, bool StartsNormalWave, bool StartsSpiralPreview, bool StartsAutoPreview)>();
        private readonly List<(HexaDebugActionKind Kind, int EntityId)> _hexaDebugActions
            = new List<(HexaDebugActionKind Kind, int EntityId)>();
        private readonly List<string> _hexaDebugActionLabels = new List<string>();

        private MissionRun _run;
        private PausePopup _pausePopup;
        private double _lastPlayerDamage;
        private bool _isResultShown;
        private bool _hasPresentedChoices;
        private bool _hasStartFailed;
        private bool _isInitialDebugRoundBreak;
        private bool _isHexaSummonTestActive;
        private int _hexaDebugActionSignature = int.MinValue;
        private int _lastFragments;
        private int _lastEnemyDamageEventCount;
        private int _lastShotLaunchEventCount;
        private int _lastShotBurstEventCount;
        private int _lastShotPersistEventCount;
        private int _lastShotSlashEventCount;
        private int _lastBurnTickDamageEventCount;
        private int _lastArcDamageEventCount;
        private int _lastFreezeStartEventCount;
        private int _lastNormalScrapPickupEventCount;
        private int _lastBigScrapPickupEventCount;
        private int _lastBigScrapSpawnEventCount;
        private float _toastUntil;
        private int _lastPickedItemCount;
        private int _pendingToastFragments;
        private bool _isItemToastVisible;

        /// <summary>현재 진행 중인 미션의 시뮬레이션이다. 경기장 그래픽이 읽으며 미션이 없으면 null이다.</summary>
        public RuneSimulation Simulation => _run?.Simulation;

        /// <summary>라운드 대기 중 보상 선택지를 표시했거나 디버그 최초 대기 상태이면 작업실을 열 수 있는지 반환한다. 일시정지·팝업 입력 잠금은 따로 확인한다.</summary>
        private bool CanOpenWorkshop => _run != null && !_run.IsFinished && _run.IsWaitingForNextRound
            && !_run.IsDebugRewardChoice && (_isInitialDebugRoundBreak || _hasPresentedChoices);

        /// <summary>보상 선택을 마쳐 라운드 대기 포털로 다음 라운드에 들어갈 수 있는지 반환한다. 경기장 그래픽이 포털 밝기를 정할 때 읽는다.</summary>
        public bool IsRoundBreakPortalActive => _run != null && _run.IsNextRoundReady;

        /// <summary>세션·UI 관리자와 미션 화면의 View들을 받아 View 이벤트를 연결한다.</summary>
        public MissionPresenter(RuneCodeSession session, UIManager ui, MissionScreen screen, MissionHud hud, ResultPanel result)
        {
            _session = session;
            _ui = ui;
            _screen = screen;
            _hud = hud;
            _result = result;
            _hud.Bind(session.IsDebugEnabled);
            _hud.PauseClicked += TogglePause;
            _hud.DebugClicked += OpenDebug;
            _hud.WorkshopClicked += OpenWorkshop;
            _hud.DebugBossClicked += StartDebugBoss;
            _hud.DebugActionClicked += RunDebugAction;
            BuildDebugBosses();
            _result.Bind();
            _result.RetryClicked += RetryStage;
            _result.ClearModifierChoiceClicked += ChooseClearModifier;
            screen.Workshop.CloseRequested += CloseWorkshop;
        }

        /// <summary>
        /// 미션 화면에 들어올 때 새 미션을 시작한 뒤 전투 음악으로 전환한다.
        /// 준비에 실패하면 실패 표시만 남기고 다음 프레임에 처리하며, 보관 중인 런이 있으면 그대로 둔다.
        /// </summary>
        public void Begin()
        {
            if (!StartRun())
            {
                _hasStartFailed = true;
                return;
            }
            SoundManager.Instance.SetMusicState(1);
        }

        /// <summary>미션 화면이 숨겨질 때 빔 음원을 정지하고 기본 음악으로 복원하며 HUD를 끄고 런을 놓는다.</summary>
        public void End()
        {
            SoundManager soundManager = SoundManager.Instance;
            if (soundManager != null)
            {
                soundManager.StopShotBeam();
                soundManager.SetMusicState(0);
            }
            _hud.SetWorkshop(false, false);
            _hud.SetWorkshopHighlight(false);
            _hud.SetDebugBossMenu(false, false);
            ResetHexaDebugActions();
            // 화면이 숨겨지는 경로는 타이틀 복귀뿐이고 ReturnToTitle이 Hide 전에 보관 표시를 해제하므로 런은 항상 버려진다.
            _run = null;
        }

        /// <summary>
        /// 프레임마다 미션을 진행하고 HUD를 갱신한다. 정산이 끝나면 결과를 한 번 표시한다.
        /// 팝업이 열려 있거나 문자 입력 중이면 조준 입력을 넘기지 않는다. 시전은 좌클릭을 누르는 동안이며 HUD 버튼 위에서는 하지 않는다.
        /// </summary>
        public void Tick(float unscaledDeltaTime)
        {
            LocalTelemetry.FlushUsabilityTest();
            if (_hasStartFailed)
            {
                // ShowScreen 처리 도중에는 팝업이 닫히므로 시작 실패 안내는 다음 프레임에 타이틀 복귀와 함께 띄운다.
                _hasStartFailed = false;
                _session.ReturnToTitle();
                _ui.Alert(GameData.L("ui.startFailedTitle"), _session.StatusMessage);
                return;
            }
            if (_run == null) return;
            // 작업실 오버레이가 열려 있는 동안은 미션 진행과 입력·사운드·라운드 처리를 모두 멈춘다.
            if (_screen.Workshop.IsVisible) return;
            if (_run.IsFinished)
            {
                if (_pausePopup != null && _pausePopup.IsVisible) return;
                RenderHud();
                SoundManager.Instance.StopShotBeam();
                PlayEnemyHitSounds();
                PlayShotSounds();
                PlayElementalSounds();
                PlayBigScrapSpawnSounds();
                PlayScrapPickupSounds();
                // 전투가 끝나면 저체력 지속 비네트는 목표를 0으로 두고 남은 점등만 자연스럽게 사라지게 한다.
                _screen.ShowLowHealth(0f);
                if (!_isResultShown && _screen.AdvanceEnding(_run.Simulation, unscaledDeltaTime)) ShowResult();
                return;
            }
            bool isTyping = UiFactory.IsTyping();
            SimVector pointer = SimVector.Zero;
            bool hasPointer = !isTyping && !_ui.IsPopupOpen && _screen.TryGetPointer(out pointer);
            Mouse mouse = Mouse.current;
            bool isCasting = Application.isFocused && hasPointer && mouse != null && mouse.leftButton.isPressed && !IsPointerOverUi();
            double playerDamageBefore = _run.Simulation.Player.DamageReceived + _run.Simulation.Player.ShieldAbsorbed;
            _run.Step(isTyping, hasPointer, pointer, isCasting, unscaledDeltaTime,
                isCasting && mouse.leftButton.wasPressedThisFrame, mouse != null && mouse.leftButton.wasReleasedThisFrame);
            if (!_run.IsPaused && !_run.IsFinished && _run.Simulation.HasAnyActiveBeam())
                SoundManager.Instance.StartShotBeam();
            else SoundManager.Instance.StopShotBeam();
            PlayEnemyHitSounds();
            PlayShotSounds();
            PlayElementalSounds();
            PlayBigScrapSpawnSounds();
            PlayScrapPickupSounds();
            UpdateDamageFeedback(playerDamageBefore);
            if (_run.IsWaitingForNextRound && _run.HasClearModifierChoices && !_hasPresentedChoices && !_ui.IsPopupOpen)
            {
                if (_screen.AdvanceEnding(_run.Simulation, unscaledDeltaTime))
                {
                    ShowRoundChoices();
                    ShowSpellOrderOnboarding();
                }
            }
            // 보상 카드가 뜬 뒤, 후보가 없으면 대기 진입 즉시 한 번만 중앙 이동과 허수아비 배치를 한다.
            if (_run.IsWaitingForNextRound && (_hasPresentedChoices || !_run.HasClearModifierChoices))
                _run.TryPrepareRoundBreakArena();
            // 보상 선택을 마친 뒤 플레이어가 포털에 들어가면 다음 라운드를 시작한다. 막는 조건은 StartNextRound가 확인한다.
            if (_run.Simulation.IsPlayerInRoundBreakPortal) StartNextRound();
            if (_run.IsWaitingForNextRound && (_hasPresentedChoices || !_run.HasClearModifierChoices) && !_ui.IsPopupOpen)
                OpenTestOnboarding(ONBOARDING_FIRST_CLEAR);
            RenderHud();
        }

        /// <summary>결과 본문을 숨긴 기존 패널에서 어두운 배경과 보상 후보 카드만 표시해 하나를 선택하게 한다. 이어하기 복원이면 라운드 클리어 효과음을 재생하지 않는다.</summary>
        private void ShowRoundChoices(bool isResumed = false)
        {
            _result.SetContent(string.Empty, string.Empty, string.Empty, true, true, isChoiceOnly: true);
            _result.SetRewards(Array.Empty<ResultRewardContent>());
            _result.Show();
            ShowPendingChoices();
            if (!isResumed) SoundManager.Instance.PlayRoundClear();
        }

        /// <summary>연속 미션의 지정 스테이지·라운드 보상 선택창이 뜬 직후 테스트 모드의 마법 순서 온보딩을 그 위에 띄운다.</summary>
        private void ShowSpellOrderOnboarding()
        {
            RuneSimulation sim = _run.Simulation;
            if (sim.StageNumber != ONBOARDING_SPELL_ORDER_STAGE || sim.RoundNumber != ONBOARDING_SPELL_ORDER_ROUND) return;
            OpenTestOnboarding(ONBOARDING_SPELL_ORDER);
        }

        /// <summary>지난 확인 이후 적이 실제 피해를 받은 횟수만큼 피격음을 재생하고 소비한 횟수를 갱신한다.</summary>
        private void PlayEnemyHitSounds()
        {
            int count = _run.Simulation.EnemyDamageEventCount - _lastEnemyDamageEventCount;
            _lastEnemyDamageEventCount = _run.Simulation.EnemyDamageEventCount;
            for (int index = 0; index < count; index++) SoundManager.Instance.PlayEnemyHit();
        }

        /// <summary>새로 생성된 발사·폭발·잔류 개체 수만큼 효과음을 재생하고 소비한 누적 횟수를 갱신한다.</summary>
        private void PlayShotSounds()
        {
            RuneSimulation sim = _run.Simulation;
            int launchCount = sim.ShotLaunchEventCount - _lastShotLaunchEventCount;
            int burstCount = sim.ShotBurstEventCount - _lastShotBurstEventCount;
            int persistCount = sim.ShotPersistEventCount - _lastShotPersistEventCount;
            int slashCount = sim.ShotSlashEventCount - _lastShotSlashEventCount;
            _lastShotLaunchEventCount = sim.ShotLaunchEventCount;
            _lastShotBurstEventCount = sim.ShotBurstEventCount;
            _lastShotPersistEventCount = sim.ShotPersistEventCount;
            _lastShotSlashEventCount = sim.ShotSlashEventCount;
            for (int index = 0; index < launchCount; index++) SoundManager.Instance.PlayShotLaunch();
            for (int index = 0; index < burstCount; index++) SoundManager.Instance.PlayShotBurst();
            for (int index = 0; index < persistCount; index++) SoundManager.Instance.PlayShotPersist();
            for (int index = 0; index < slashCount; index++) SoundManager.Instance.PlayShotSlash();
        }

        /// <summary>새 화상 틱·감전 적중·빙결 시작 이벤트를 소비하고 각 효과음을 횟수만큼 재생한다.</summary>
        private void PlayElementalSounds()
        {
            RuneSimulation sim = _run.Simulation;
            int burnCount = sim.BurnTickDamageEventCount - _lastBurnTickDamageEventCount;
            int arcCount = sim.ArcDamageEventCount - _lastArcDamageEventCount;
            int freezeCount = sim.FreezeStartEventCount - _lastFreezeStartEventCount;
            _lastBurnTickDamageEventCount = sim.BurnTickDamageEventCount;
            _lastArcDamageEventCount = sim.ArcDamageEventCount;
            _lastFreezeStartEventCount = sim.FreezeStartEventCount;
            for (int index = 0; index < burnCount; index++) SoundManager.Instance.PlayBurn();
            for (int index = 0; index < arcCount; index++) SoundManager.Instance.PlayChain();
            for (int index = 0; index < freezeCount; index++) SoundManager.Instance.PlayFreeze();
        }

        /// <summary>새로 획득한 스크랩 오브마다 드롭 출처에 맞는 일반 또는 Big 획득음을 한 번 재생한다.</summary>
        private void PlayScrapPickupSounds()
        {
            RuneSimulation sim = _run.Simulation;
            int normalCount = sim.NormalScrapPickupEventCount - _lastNormalScrapPickupEventCount;
            int bigCount = sim.BigScrapPickupEventCount - _lastBigScrapPickupEventCount;
            _lastNormalScrapPickupEventCount = sim.NormalScrapPickupEventCount;
            _lastBigScrapPickupEventCount = sim.BigScrapPickupEventCount;
            for (int index = 0; index < normalCount; index++) SoundManager.Instance.PlayTakeScrap();
            for (int index = 0; index < bigCount; index++) SoundManager.Instance.PlayTakeBigScrap();
        }

        /// <summary>새로 생성된 큰 스크랩 오브마다 등장 효과음을 재생하고 누적 생성 횟수를 갱신한다.</summary>
        private void PlayBigScrapSpawnSounds()
        {
            RuneSimulation sim = _run.Simulation;
            int count = sim.BigScrapSpawnEventCount - _lastBigScrapSpawnEventCount;
            _lastBigScrapSpawnEventCount = sim.BigScrapSpawnEventCount;
            for (int index = 0; index < count; index++) SoundManager.Instance.PlaySpawnBigScrap();
        }

        /// <summary>미션 화면의 Esc 입력으로 라운드 대기·결과 표시 중에도 일시정지 팝업을 연다.</summary>
        public bool HandleEscape()
        {
            if (_run == null) return false;
            TogglePause();
            return true;
        }

        /// <summary>라운드 대기 중 포털에 들어가면 다음 라운드를 시작한다. 작업실 오버레이·팝업·일시정지·문자 입력 중에는 무시한다.</summary>
        private void StartNextRound()
        {
            if (_screen.Workshop.IsVisible) return;
            if (_run == null || _run.IsFinished || _run.IsPaused || _ui.IsPopupOpen || UiFactory.IsTyping()) return;
            if (_run.TryStartNextRound())
            {
                _isInitialDebugRoundBreak = false;
                ResetHexaDebugActions();
                _hasPresentedChoices = false;
                _lastPickedItemCount = _run.Simulation.PickedUpItems.Count;
                _lastFragments = _run.Simulation.EarnedFragments;
                _pendingToastFragments = 0;
                _isItemToastVisible = false;
                _itemToasts.Clear();
                _toastUntil = 0f;
                _hud.SetToast(string.Empty);
                RenderHud();
            }
        }

        /// <summary>
        /// 라운드 대기 중이고 일시정지·팝업·문자 입력이 없으면 런을 보관하고 작업실을 미션 화면 위 오버레이로 연다.
        /// 라운드 보상 카드를 고르기 전에도 열 수 있으며 카드는 돌아왔을 때 그대로 남는다.
        /// </summary>
        private void OpenWorkshop()
        {
            if (_screen.Workshop.IsVisible) return;
            if (!CanOpenWorkshop || _run.IsPaused || _ui.IsPopupOpen || UiFactory.IsTyping()) return;
            _session.SuspendRunForWorkshop(_run.Simulation.StageNumber, _run.Simulation.RoundNumber);
            SoundManager.Instance.StopShotBeam();
            _hud.SetWorkshop(false, false);
            _hud.SetWorkshopHighlight(false);
            WorkshopScreen workshop = _screen.Workshop;
            workshop.transform.SetAsLastSibling();
            workshop.Show();
        }

        /// <summary>
        /// 작업실 오버레이를 닫고 보관 중인 런을 이어 간다. 재개에 실패하면 작업실을 유지하며
        /// 실패 원인은 세션 상태 문구와 다시 그려진 보상 카드로 남는다.
        /// </summary>
        private void CloseWorkshop()
        {
            WorkshopScreen workshop = _screen.Workshop;
            if (!workshop.IsVisible) return;
            if (!ResumeRun()) return;
            workshop.Hide();
            RenderHud();
        }

        /// <summary>보스별 첫 배치와 디버그 진입을 구성하고, Hexa 나선·자동 모드와 일반 웨이브 소환 소유권 시험 항목을 추가한다.</summary>
        private void BuildDebugBosses()
        {
            var labels = new List<string>();
            foreach (BossDefinition boss in GameData.Bosses.Bosses)
            {
                int round = boss.IsMiniBoss ? StageCatalog.MINI_BOSS_ROUND : StageCatalog.PROGRESSION_ROUND_COUNT;
                int stage = FindBossStage(boss.EnemyId, round);
                bool isHexaPreview = stage == 0 && boss.IsHexaPattern;
                if (stage == 0 && !isHexaPreview) continue;
                if (isHexaPreview) stage = 1;
                _debugBossTargets.Add((stage, round, isHexaPreview, false, false, false));
                labels.Add((boss.IsMiniBoss ? "" : "보스 ") + GameData.L(boss.EnemyId)
                    + (isHexaPreview ? " · 기본 모드" : " · 스테이지 " + stage));
            }
            _debugBossTargets.Add((1, StageCatalog.PROGRESSION_ROUND_COUNT, true, false, true, false));
            labels.Add("보스 " + GameData.L(HexaDefinition.ID) + " · 나선 모드");
            _debugBossTargets.Add((1, StageCatalog.PROGRESSION_ROUND_COUNT, true, false, false, true));
            labels.Add("보스 " + GameData.L(HexaDefinition.ID) + " · 자동 모드");
            _debugBossTargets.Add((1, 1, false, true, false, false));
            labels.Add(GameData.L(HexaDefinition.ID) + " 소환 테스트 · 일반 웨이브");
            labels.Add("모드 보상 선택지 · 속성 포함 (임시)");
            _hud.BuildDebugBosses(labels);
        }

        /// <summary>복합 디버그 라벨과 HUD에 표시할 하위 공격 이름을 반환한다.</summary>
        private static string CompositeAttackName(HexaSubAttack attack)
            => attack == HexaSubAttack.Spray ? "난사"
                : attack == HexaSubAttack.Homing ? "유도"
                : attack == HexaSubAttack.Sniper ? "Sniper" : "-";

        /// <summary>복합에서 동시에 진행 중인 하위 공격의 발사 진행을 표시용 문자열로 반환하고, 진행 중인 공격이 없으면 시작 전·전환·완료·취소를 구분해 반환한다.</summary>
        private static string CompositeProgressLabel(HexaBossState hexa)
        {
            if (hexa.IsCancelled) return "취소";
            string label = "";
            if (hexa.IsSpraying) label = "난사 " + hexa.SprayShotCount + "/" + HexaDefinition.SPRAY_SHOT_COUNT;
            if (hexa.IsHoming)
                label += (label.Length > 0 ? " + " : "") + "유도 " + hexa.HomingShotCount + "/" + HexaDefinition.HOMING_SHOT_COUNT;
            if (hexa.IsSniperAttacking)
                label += (label.Length > 0 ? " + " : "") + "Sniper " + hexa.SniperShotCount + "/" + HexaDefinition.SNIPER_SHOT_COUNT;
            if (label.Length > 0) return label;
            return hexa.IsCompositeComplete ? "완료" : hexa.CompositeStage == 0 ? "시작 전" : "전환";
        }

        /// <summary>지정 라운드의 마지막 웨이브에 bossId가 처음 나오는 스테이지 번호를 반환하고 없으면 0을 반환한다.</summary>
        private static int FindBossStage(string bossId, int round)
        {
            foreach (ProgressionStageDefinition stage in GameData.Stages.ProgressionStages)
            {
                WaveDefinition bossWave = stage.Rounds[round - 1].Waves[StageCatalog.PROGRESSION_WAVE_COUNT - 1];
                foreach (WaveUnit unit in bossWave.Units)
                    if (unit.EnemyId == bossId) return stage.Stage;
            }
            return 0;
        }

        /// <summary>디버그 항목에 따라 속성 포함 모드 보상을 표시하거나 보스·일반 웨이브 시험에 진입한다. 작업실 오버레이가 열려 있으면 무시한다.</summary>
        private void StartDebugBoss(int index)
        {
            if (_screen.Workshop.IsVisible) return;
            if (_run == null || _run.IsFinished || _run.IsPaused || _ui.IsPopupOpen || UiFactory.IsTyping()) return;
            if (_run.IsDebugRewardChoice) return;
            if (index == _debugBossTargets.Count)
            {
                if (_run.DebugShowModifierRewardChoices())
                {
                    ShowRoundChoices(isResumed: true);
                    RenderHud();
                }
                return;
            }
            if (index < 0 || index >= _debugBossTargets.Count) return;
            (int Stage, int Round, bool IsHexaPreview, bool StartsNormalWave, bool StartsSpiralPreview, bool StartsAutoPreview) target = _debugBossTargets[index];
            ResetHexaDebugActions();
            bool isStarted = target.StartsNormalWave
                ? _run.DebugStartNormalWave(target.Stage, target.Round)
                : _run.DebugStartBoss(target.Stage, target.Round, target.IsHexaPreview,
                    isHexaSpiralPreview: target.StartsSpiralPreview, isHexaAutoPreview: target.StartsAutoPreview);
            if (!isStarted) return;
            _isHexaSummonTestActive = target.StartsNormalWave
                || (target.IsHexaPreview && !target.StartsSpiralPreview && !target.StartsAutoPreview);
            _isInitialDebugRoundBreak = false;
            _result.HideClearModifierChoices();
            _result.Hide();
            _hasPresentedChoices = false;
            RenderHud();
        }

        /// <summary>디버그 소환 시험 버튼 순번을 두 기 추가·개체별 소환 시작·개체별 처치로 나눠 실행한다. 시험 중이 아니거나 작업실이 열려 있거나 대상이 사라졌으면 무시한다.</summary>
        private void RunDebugAction(int index)
        {
            if (_screen.Workshop.IsVisible) return;
            if (_run == null || _run.IsFinished || _run.IsPaused || _ui.IsPopupOpen || UiFactory.IsTyping()) return;
            if (!_isHexaSummonTestActive || index < 0 || index >= _hexaDebugActions.Count) return;
            (HexaDebugActionKind Kind, int EntityId) action = _hexaDebugActions[index];
            if (action.Kind == HexaDebugActionKind.AddPair) _run.DebugSpawnHexaPair();
            else if (action.Kind == HexaDebugActionKind.StartSummon) _run.DebugStartHexaSummon(action.EntityId);
            else _run.DebugKillHexa(action.EntityId);
        }

        /// <summary>Hexa 소환 시험 상태와 조작 버튼 캐시를 비운다.</summary>
        private void ResetHexaDebugActions()
        {
            _isHexaSummonTestActive = false;
            _hexaDebugActions.Clear();
            _hexaDebugActionLabels.Clear();
            _hexaDebugActionSignature = int.MinValue;
        }

        /// <summary>
        /// 현재 전투의 살아 있는 Hexa를 실제 개체 ID 순서로 최대 두 기까지 찾아 개체별 소환 시작·처치 버튼을 만들고,
        /// 한 기도 없으면 두 기 추가 버튼을 만든다. 개체 목록이 같으면 라벨을 다시 만들지 않고 입력 가능 여부만 갱신한다.
        /// </summary>
        private void UpdateHexaDebugActions(bool isInteractable)
        {
            RuneSimulation sim = _run.Simulation;
            int signature = 17;
            int liveCount = 0;
            foreach (SimulationEnemy enemy in sim.Enemies)
            {
                if (enemy.Hexa == null || !enemy.IsAlive) continue;
                liveCount++;
                if (liveCount <= MAX_HEXA_DEBUG_TARGETS) signature = signature * 31 + enemy.Id;
            }
            signature = signature * 31 + Math.Min(liveCount, MAX_HEXA_DEBUG_TARGETS);
            if (signature != _hexaDebugActionSignature)
            {
                _hexaDebugActionSignature = signature;
                _hexaDebugActions.Clear();
                _hexaDebugActionLabels.Clear();
                liveCount = 0;
                foreach (SimulationEnemy enemy in sim.Enemies)
                {
                    if (enemy.Hexa == null || !enemy.IsAlive) continue;
                    liveCount++;
                    if (liveCount > MAX_HEXA_DEBUG_TARGETS) break;
                    _hexaDebugActions.Add((HexaDebugActionKind.StartSummon, enemy.Id));
                    _hexaDebugActionLabels.Add("Hexa #" + enemy.Id + " 소환 시작");
                    _hexaDebugActions.Add((HexaDebugActionKind.Kill, enemy.Id));
                    _hexaDebugActionLabels.Add("Hexa #" + enemy.Id + " 처치");
                }
                if (liveCount == 0)
                {
                    _hexaDebugActions.Add((HexaDebugActionKind.AddPair, 0));
                    _hexaDebugActionLabels.Add("Hexa 2기 추가");
                }
            }
            _hud.SetDebugActions(_hexaDebugActionLabels, isInteractable);
        }

        /// <summary>미션을 일시정지하고 기존 팝업 위에 일시정지 팝업을 연다. 종료된 미션은 정지 상태를 바꾸지 않는다.</summary>
        private void TogglePause()
        {
            if (_run == null) return;
            if (_run.IsPaused)
            {
                _ui.GetView<PausePopup>(UiLayer.Popup).Close();
                return;
            }
            _run.TogglePause();
            PausePopup popup = _ui.OpenPopup<PausePopup>();
            _pausePopup = popup;
            popup.Bind();
            popup.SoundSettingsClicked += OpenSoundSettings;
            popup.TitleClicked += ReturnToTitle;
            popup.Closed += () => OnPauseClosed(popup);
        }

        /// <summary>일시정지 팝업이 닫히면 이벤트 구독을 풀고, 미션이 아직 일시정지 중이면 재개한다.</summary>
        private void OnPauseClosed(PausePopup popup)
        {
            popup.SoundSettingsClicked -= OpenSoundSettings;
            popup.TitleClicked -= ReturnToTitle;
            if (_run != null && _run.IsPaused) _run.TogglePause();
        }

        /// <summary>일시정지를 유지한 채 화면 피드백과 음량을 조정하는 공통 기본 설정을 연다.</summary>
        private void OpenSoundSettings()
        {
            SettingsPopup popup = _ui.OpenPopup<SettingsPopup>();
            popup.Configure(_session, _ui, isTitle: true);
        }

        /// <summary>세션의 저장·로그 종료 정책을 적용해 타이틀로 돌아가고 화면 종료 흐름으로 미션을 정리한다.</summary>
        private void ReturnToTitle()
        {
            _session.ReturnToTitle();
        }

        /// <summary>디버그 팝업을 열고 디버그 조작을 미션·세션에 연결한다. 디버그 실행이 아니면 무시한다.</summary>
        private void OpenDebug()
        {
            if (_run == null || _run.IsFinished) return;
            if (!_session.IsDebugEnabled) return;
            MissionDebugPopup popup = _ui.OpenPopup<MissionDebugPopup>();
            popup.Bind();
            Action grant = () => _session.DebugGrant();
            Action unlock = () => _session.DebugUnlock();
            Action invulnerable = () => _run?.DebugInvulnerable();
            Action spawn = () => _run?.DebugSpawn();
            Action eliteSpawn = () => _run?.DebugSpawn(isElite: true);
            Action splitterSpawn = () => _run?.DebugSpawn("enemy.splitter");
            Action splitterEliteSpawn = () => _run?.DebugSpawn("enemy.splitter", isElite: true);
            Action<string> additionalSpawn = id => _run?.DebugSpawn(id);
            popup.GrantClicked += grant;
            popup.UnlockClicked += unlock;
            popup.InvulnerableClicked += invulnerable;
            popup.SpawnClicked += spawn;
            popup.EliteSpawnClicked += eliteSpawn;
            popup.SplitterSpawnClicked += splitterSpawn;
            popup.SplitterEliteSpawnClicked += splitterEliteSpawn;
            popup.AdditionalSpawnClicked += additionalSpawn;
            popup.Closed += () =>
            {
                popup.GrantClicked -= grant;
                popup.UnlockClicked -= unlock;
                popup.InvulnerableClicked -= invulnerable;
                popup.SpawnClicked -= spawn;
                popup.EliteSpawnClicked -= eliteSpawn;
                popup.SplitterSpawnClicked -= splitterSpawn;
                popup.SplitterEliteSpawnClicked -= splitterEliteSpawn;
                popup.AdditionalSpawnClicked -= additionalSpawn;
            };
        }

        /// <summary>정산된 결과의 스테이지를 다시 선택해 새 미션을 시작한다. 준비에 실패하면 결과를 유지한다.</summary>
        private void RetryStage()
        {
            if (_run == null || !_run.IsFinished) return;
            if (_run.Simulation.IsProgressionMission && _run.Simulation.Completed) return;
            if (_run.HasClearModifierChoices && !_run.IsClearModifierChosen) return;
            if (!_run.Simulation.IsProgressionMission) _session.SelectStage(_run.Simulation.StageNumber);
            StartRun();
        }

        /// <summary>
        /// 월드 종료 연출 후 클리어·실패 모두 런 결과 팝업을 열고 제목과 효과음만 결과에 맞춘다.
        /// 팝업의 타이틀 복귀 버튼은 세션의 ReturnToTitle로 이어지며 팝업이 닫힐 때 구독을 푼다.
        /// </summary>
        private void ShowResult()
        {
            _isResultShown = true;
            _ui.CloseAllPopups();
            RuneSimulation sim = _run.Simulation;
            if (sim.Completed) SoundManager.Instance.PlayRoundClear();
            else SoundManager.Instance.PlayRoundDefeat();
            RunResultPopup popup = _ui.OpenPopup<RunResultPopup>();
            popup.Configure(_run.ResultGraph, _run.ResultLibrary, _run.ResultSpell, sim);
            popup.TitleClicked += _session.ReturnToTitle;
            popup.Closed += () => popup.TitleClicked -= _session.ReturnToTitle;
        }

        /// <summary>일반 보상 공개가 끝난 다음 프레임에 보스 선택 카드를 한 번만 펼친다. 보스 선택 보상이 있으면 후보 카드를 표시하고 없으면 숨긴다.</summary>
        private void ShowPendingChoices()
        {
            _hasPresentedChoices = true;
            if (_run.HasClearModifierChoices)
            {
                var names = new List<string>();
                var descriptions = new List<string>();
                for (int i = 0; i < _run.ClearModifierChoices.Count; i++)
                {
                    RuneDefinition rune = GameData.Runes.Get(_run.ClearModifierChoices[i]);
                    string grade = _run.ClearModifierGrades[i];
                    names.Add(ModifierGradeText.Colorize(rune.Name, grade));
                    string description = rune.Category == SpellGrammar.CATEGORY_MODIFIER
                        ? string.Join("\n", RuneDescriptionBuilder.GetGradeLines(rune, grade, false, true))
                        : RuneDescriptionBuilder.GetDescription(rune);
                    descriptions.Add(description);
                }
                _result.ShowClearModifierChoices(GameData.L("result.clearModifierChoice"), names, descriptions,
                    _run.ClearModifierGrades, _run.ClearModifierChoices);
            }
            else _result.HideClearModifierChoices();
        }

        /// <summary>결과 View에 정산 문구와 스테이지·진행 정보를 채운다.</summary>
        private void SetResultSummary(bool animateRewards = false)
        {
            RuneSimulation sim = _run.Simulation;
            int elapsedSeconds = (int)sim.Time;
            string playTime = "플레이 타임 " + (elapsedSeconds / 60).ToString("00") + ":" + (elapsedSeconds % 60).ToString("00");
            _result.SetContent(sim.Completed ? (sim.IsProgressionMission ? "미션 완료" : "스테이지 승리")
                    : sim.IsFailed ? "스테이지 패배" : "스테이지 중단",
                GetStageText(sim),
                playTime,
                sim.Completed, sim.IsProgressionMission);
            SetResultRewards(animateRewards);
        }

        /// <summary>이번 미션에서 획득한 보상을 아이콘 수량과 Modifier 등급 효과 문구로 변환해 결과 View에 전달한다.</summary>
        private void SetResultRewards(bool animate)
        {
            var rewards = new List<ResultRewardContent>(_run.Rewards.Count);
            foreach (MissionReward reward in _run.Rewards)
            {
                if (!reward.IsModifier)
                {
                    rewards.Add(new ResultRewardContent(false, reward.Count, GameData.L("ui.fragments"), GameData.L("ui.fragments.description")));
                    continue;
                }
                RuneDefinition rune = GameData.Runes.Get(reward.RuneId);
                // 속성 보상은 등급이 없으므로 등급 줄을 생략하고, 모디·행동 보상은 등급을 표시한다.
                string gradeLabel = string.IsNullOrEmpty(reward.Grade) ? "" : reward.Grade + "급\n";
                string description;
                if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR
                    || rune.Category == SpellGrammar.CATEGORY_ELEMENT)
                {
                    description = ModifierGradeText.Colorize(
                        gradeLabel + RuneDescriptionBuilder.GetDescription(rune), reward.Grade);
                }
                else
                {
                    description = ModifierGradeText.Colorize(
                        gradeLabel + string.Join("\n", RuneDescriptionBuilder.GetGradeLines(rune, reward.Grade, false)),
                        reward.Grade);
                }

                rewards.Add(new ResultRewardContent(
                    true,
                    reward.Count,
                    ModifierGradeText.FormatName(rune.Name, reward.Grade),
                    description,
                    reward.RuneId,
                    reward.Grade));
            }
            rewards.Sort((left, right) =>
            {
                int order = GetRewardOrder(left).CompareTo(GetRewardOrder(right));
                return order != 0 ? order : string.CompareOrdinal(left.RuneId, right.RuneId);
            });
            _result.SetRewards(rewards, animate);
        }

        /// <summary>스크랩·속성·등급 오름차순 행동/모드 순서로 공개할 보상의 정렬 키를 반환한다.</summary>
        private static int GetRewardOrder(ResultRewardContent reward)
        {
            if (!reward.IsModifier) return 0;
            RuneDefinition rune = GameData.Runes.Get(reward.RuneId);
            if (rune.Category == SpellGrammar.CATEGORY_ELEMENT) return 1;
            int grade = Math.Max(0, Array.IndexOf(new[] { "C", "B", "A", "S" }, reward.Grade));
            return 2 + grade * 2 + (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR ? 0 : 1);
        }

        /// <summary>클리어 Modifier 후보를 지급하고 선택 확정 상태와 결과 문구를 갱신한다. 유효한 선택이 아니거나 안내 팝업이 열려 있으면 무시한다.</summary>
        private void ChooseClearModifier(int index)
        {
            if (_run == null || _ui.IsPopupOpen || !_run.ChooseClearModifier(index)) return;
            if (_run.IsWaitingForNextRound)
            {
                _result.HideClearModifierChoices();
                _result.Hide();
                if (_run.HasClearModifierChoices && !_run.IsClearModifierChosen) ShowRoundChoices(isResumed: true);
                RenderHud();
                return;
            }
            SetResultSummary();
            _result.HideClearModifierChoices();
            _result.FocusReward(_run.ClearModifierChoices[index], _run.ClearModifierGrades[index]);
        }

        /// <summary>세션에서 시전 마법을 준비받아 새 미션을 만들고 HUD·결과 View를 시작 상태로 되돌린다. 이어하기로 고르지 않은 라운드 후보가 남으면 종료 연출 없이 카드 선택창을 즉시 연다. 실패하면 false를 반환한다.</summary>
        private bool StartRun()
        {
            if (!_session.TryPrepareMission(out CompiledSpell spell)) return false;
            _run = new MissionRun(_session, spell, _session.Spells.SpellName);
            _isInitialDebugRoundBreak = _session.IsDebugEnabled && _run.IsWaitingForNextRound;
            ResetHexaDebugActions();
            _lastEnemyDamageEventCount = 0;
            _lastShotLaunchEventCount = 0;
            _lastShotBurstEventCount = 0;
            _lastShotPersistEventCount = 0;
            _lastShotSlashEventCount = 0;
            _lastBurnTickDamageEventCount = 0;
            _lastArcDamageEventCount = 0;
            _lastFreezeStartEventCount = 0;
            _lastNormalScrapPickupEventCount = 0;
            _lastBigScrapPickupEventCount = 0;
            _lastBigScrapSpawnEventCount = _run.Simulation.BigScrapSpawnEventCount;
            _isResultShown = false;
            _hasPresentedChoices = false;
            _ui.CloseAllPopups();
            _result.Hide();
            _lastFragments = _run.Simulation.EarnedFragments;
            _lastPickedItemCount = 0;
            _pendingToastFragments = 0;
            _isItemToastVisible = false;
            _itemToasts.Clear();
            _toastUntil = 0f;
            _hud.SetToast("");
            RenderHud();
            if (_run.IsResumedRoundBreak && _run.HasClearModifierChoices && !_run.IsClearModifierChosen)
                ShowRoundChoices(isResumed: true);
            ShowBattleStartOnboarding();
            return true;
        }

        /// <summary>테스트 모드의 첫 전투 직전에 미션을 일시정지하고 온보딩을 띄운다. 온보딩이 닫히면 같은 미션의 일시정지를 푼다.</summary>
        private void ShowBattleStartOnboarding()
        {
            OnboardingPopup popup = OpenTestOnboarding(ONBOARDING_BATTLE_START);
            if (popup == null) return;
            MissionRun run = _run;
            if (!run.IsPaused) run.TogglePause();
            popup.Closed += () => { if (_run == run && run.IsPaused) run.TogglePause(); };
        }

        /// <summary>
        /// 테스트 모드에서 chapter가 다음 순서의 온보딩이면 도움말 챕터를 안내 팝업으로 열어 반환한다.
        /// 순서가 아니면 null을 반환하고, 도움말 데이터에 챕터가 없으면 경고를 남기고 순서만 넘긴다.
        /// </summary>
        private OnboardingPopup OpenTestOnboarding(int chapter)
        {
            if (!_session.TryAdvanceTestOnboarding(chapter)) return null;
            EncyclopediaEntry entry = GameData.Encyclopedia?.FindChapter(chapter);
            if (entry == null)
            {
                Debug.LogWarning("게임 도움말 JSON에 온보딩 챕터가 없습니다: " + chapter);
                return null;
            }
            OnboardingPopup popup = _ui.OpenPopup<OnboardingPopup>();
            popup.Configure(entry.Title, entry.Pages);
            return popup;
        }

        /// <summary>
        /// 작업실 오버레이를 닫을 때 저장한 마법을 다시 검증해 보관 중인 런의 시전 마법을 교체하고 강화 능력치와 허수아비 DPS 초기화를 반영한 뒤
        /// 보관 표시를 해제하고 HUD를 갱신한다. 라운드 대기 상태와 고르지 않은 보상 카드는 유지하며, 준비나 교체에 실패하면 false를 반환한다.
        /// </summary>
        private bool ResumeRun()
        {
            if (!_session.TryPrepareMission(out CompiledSpell spell)) return false;
            if (!_run.TryReplaceSpell(spell, _session.Spells.SpellName)) return false;
            _run.TryApplyUpgradeStats();
            _run.Simulation.ResetRoundBreakDpsForWorkshopReturn();
            _session.ResumeSuspendedRun();
            // 결과 패널은 화면과 함께 꺼졌다 켜질 뿐 상태가 남으므로, 같은 후보를 다시 그려 선택 포커스만 되살린다.
            if (_hasPresentedChoices && _run.HasClearModifierChoices && !_run.IsClearModifierChosen) ShowPendingChoices();
            OpenTestOnboarding(ONBOARDING_ROUND_RETURN);
            RenderHud();
            return true;
        }

        /// <summary>포인터가 버튼 등 UI 입력 대상 위에 있는지 반환한다.</summary>
        private static bool IsPointerOverUi() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        /// <summary>연속 미션은 현재 스테이지·라운드·웨이브와 각 총수를, 기존 전투는 스테이지 번호만 반환한다.</summary>
        private static string GetStageText(RuneSimulation sim)
            => sim.IsProgressionMission
                ? "스테이지 " + sim.StageNumber + "/" + StageCatalog.PROGRESSION_STAGE_COUNT
                    + " · 라운드 " + sim.RoundNumber + "/" + StageCatalog.PROGRESSION_ROUND_COUNT
                    + " · 웨이브 " + sim.WaveNumber + "/" + StageCatalog.PROGRESSION_WAVE_COUNT
                : GameData.L("ui.stage") + " " + sim.StageNumber;

        /// <summary>
        /// 이번 프레임에 플레이어가 입은 피해(체력 피해 + 보호막 흡수)로 피격음을 재생하고 비네트를 점등하며,
        /// 최대 체력 대비 현재 체력으로 저체력 지속 비네트 강도를 정한다. 강도는 피해량에 비례해 최대 1까지 붙는다.
        /// </summary>
        private void UpdateDamageFeedback(double playerDamageBefore)
        {
            RuneSimulation sim = _run.Simulation;
            double damageTaken = sim.Player.DamageReceived + sim.Player.ShieldAbsorbed - playerDamageBefore;
            if (damageTaken > PLAYER_DAMAGE_EPSILON)
            {
                SoundManager.Instance.PlayPlayerHit();
                float strength = VIGNETTE_BASE_STRENGTH + (float)(damageTaken / sim.Player.MaxHp) * VIGNETTE_DAMAGE_STRENGTH;
                _screen.ShowDamageFeedback(Mathf.Clamp01(strength));
            }
            float hpFraction = (float)(sim.Player.Hp / sim.Player.MaxHp);
            _screen.ShowLowHealth(hpFraction >= LOW_HEALTH_FRACTION ? 0f
                : Mathf.Lerp(LOW_HEALTH_MAX_LEVEL, 0f, hpFraction / LOW_HEALTH_FRACTION));
        }

        /// <summary>현재 시뮬레이션 값으로 HUD 막대·전투 정보·마법 줄과 조각 토스트를 표시한다.</summary>
        private void RenderHud()
        {
            RuneSimulation sim = _run.Simulation;
            int? remainingEnemyCount = sim.IsProgressionMission && !_run.IsFinished && !sim.IsWaitingForNextRound
                ? sim.RemainingWaveEnemyCount : (int?)null;
            _hud.SetStage(GetStageText(sim), remainingEnemyCount);
            _hud.SetWorkshop(CanOpenWorkshop, !_run.IsPaused && !_ui.IsPopupOpen);
            // 첫 클리어 온보딩 뒤 보상을 고르면 작업실 온보딩 전까지 작업실 버튼을 강조한다.
            _hud.SetWorkshopHighlight(CanOpenWorkshop && _run.IsClearModifierChosen
                && _session.TestOnboardingChapter == ONBOARDING_FIRST_CLEAR);
            if (_run.IsFinished || sim.IsWaitingForNextRound) ResetHexaDebugActions();
            bool isDebugMenuVisible = _session.IsDebugEnabled && CanOpenWorkshop;
            bool isDebugActionsVisible = _session.IsDebugEnabled && _isHexaSummonTestActive
                && !_run.IsFinished && !sim.IsWaitingForNextRound;
            if (isDebugMenuVisible) _hud.SetDebugBossMenu(true, !_run.IsPaused && !_ui.IsPopupOpen);
            else if (isDebugActionsVisible) UpdateHexaDebugActions(!_run.IsPaused && !_ui.IsPopupOpen);
            else _hud.SetDebugBossMenu(false, false);
            _hud.SetBars(sim.Player.Hp, sim.Player.MaxHp, sim.Player.Energy, sim.Player.MaxEnergy, sim.EnergyRegen);
            _hud.SetCooldowns(sim.Player.Cooldowns[0], sim.Player.CooldownDurations[0],
                sim.Player.DashCooldown, sim.Player.DashCooldownDuration);
            _hud.SetStats(GameData.L("ui.kills") + " " + sim.KillCount + "  ·  " + GameData.L("ui.fragments") + " " + sim.EarnedFragments);
            string objective = _run.IsFinished ? (sim.Completed ? "미션 완료" : sim.IsFailed ? "전투 패배" : "전투 중단")
                : sim.IsWaitingForNextRound ? (_run.IsNextRoundReady ? "포털로 이동해 다음 라운드 시작" : "다음 라운드 대기")
                : (sim.IsBossStage ? "보스를 처치하세요 · " : "") + "남은 적 " + sim.RemainingWaveEnemyCount;
            if (_session.IsDebugEnabled && !_run.IsFinished && !sim.IsWaitingForNextRound)
                foreach (SimulationEnemy enemy in sim.Enemies)
                {
                    HexaBossState hexa = enemy.Hexa;
                    if (hexa == null) continue;
                    if (hexa.HasAutoOperation)
                    {
                        string autoPattern = hexa.AutoPattern == HexaTopPattern.Summon ? "소환"
                            : hexa.AutoPattern == HexaTopPattern.Spiral ? "나선"
                            : hexa.AutoPattern == HexaTopPattern.Composite ? "복합" : "대기";
                        string autoState;
                        if (hexa.IsCancelled) autoState = "취소";
                        else if (hexa.DelayTicks > 0)
                            autoState = "다음 " + (hexa.DelayTicks / (double)RuneSimulation.TICK_RATE).ToString("0.0") + "s";
                        else if (hexa.AutoPattern == HexaTopPattern.Summon)
                            autoState = "소환 " + hexa.SummonBundlesSpawned + "/" + HexaDefinition.SUMMON_BUNDLE_COUNT + " 진행";
                        else if (hexa.AutoPattern == HexaTopPattern.Spiral)
                            autoState = "나선 " + hexa.SpiralBundleCount + "/" + HexaDefinition.SPIRAL_BUNDLE_COUNT + "묶음 · "
                                + hexa.SpiralShotCount + "/" + HexaDefinition.SPIRAL_SHOT_COUNT + "발";
                        else
                            autoState = "복합 " + CompositeAttackName(hexa.CompositeFirstAttack) + "→"
                                + CompositeAttackName(hexa.CompositeSecondAttack) + " · "
                                + hexa.CompositeStage + "/" + HexaDefinition.COMPOSITE_STAGE_COUNT
                                + " " + CompositeProgressLabel(hexa);
                        objective = "Hexa 자동 " + autoPattern + " · 소환 " + hexa.SummonCooldown + " · 나선 " + hexa.SpiralCooldown
                            + " · 완료 " + hexa.AutoCompletionCount + "회 · " + autoState;
                        break;
                    }
                    if (hexa.HasStartedSummon)
                    {
                        int summonCount = 0;
                        foreach (SimulationEnemy summon in sim.Enemies)
                            if (summon.SummonerId == enemy.Id) summonCount++;
                        objective = "Hexa #" + enemy.Id + " 소환 " + hexa.SummonBundlesSpawned + "/" + HexaDefinition.SUMMON_BUNDLE_COUNT
                            + " · 소환물 " + summonCount + " · "
                            + (hexa.IsCancelled ? "취소" : hexa.IsSummoning ? "진행" : "완료");
                        break;
                    }
                    if (hexa.HasStartedSpiral)
                    {
                        string spiralState = hexa.IsCancelled ? "취소" : hexa.DelayTicks > 0 ? "대기"
                            : hexa.IsSpiraling ? "진행" : "완료";
                        objective = "Hexa 나선 " + hexa.SpiralBundleCount + "/" + HexaDefinition.SPIRAL_BUNDLE_COUNT
                            + "묶음 · " + hexa.SpiralShotCount + "/" + HexaDefinition.SPIRAL_SHOT_COUNT + "발"
                            + " · " + (hexa.SpiralElapsedTicks / (double)RuneSimulation.TICK_RATE).ToString("0.0")
                            + "s · " + spiralState;
                        break;
                    }
                    if (hexa.HasStartedComposite)
                    {
                        string compositeState = hexa.IsCancelled ? "취소" : hexa.DelayTicks > 0 ? "대기"
                            : hexa.IsCompositeComplete ? "완료"
                            : hexa.IsSniperPaused ? "빙결"
                            : hexa.IsSpraying || hexa.IsHoming || hexa.IsSniperAttacking ? "진행" : "전환";
                        objective = "Hexa 복합 " + CompositeAttackName(hexa.CompositeFirstAttack)
                            + "→" + CompositeAttackName(hexa.CompositeSecondAttack)
                            + " · " + hexa.CompositeStage + "/" + HexaDefinition.COMPOSITE_STAGE_COUNT
                            + " " + CompositeProgressLabel(hexa)
                            + " · " + compositeState + " · 전체 완료 " + hexa.CompositeCompletionCount + "회";
                        break;
                    }
                    if (hexa.HasStartedSpray)
                    {
                        string sprayState = hexa.IsCancelled ? "취소" : hexa.DelayTicks > 0 ? "대기"
                            : hexa.IsSpraying ? "진행" : "완료";
                        objective = "Hexa 난사 " + hexa.SprayShotCount + "/" + HexaDefinition.SPRAY_SHOT_COUNT
                            + " · " + (hexa.SprayElapsedTicks / (double)RuneSimulation.TICK_RATE).ToString("0.0")
                            + "s · " + sprayState;
                        break;
                    }
                    if (hexa.HasStartedHoming)
                    {
                        string homingState = hexa.IsCancelled ? "취소" : hexa.DelayTicks > 0 ? "대기"
                            : hexa.IsHoming ? "진행" : "완료";
                        objective = "Hexa 유도 " + hexa.HomingShotCount + "/" + HexaDefinition.HOMING_SHOT_COUNT
                            + " · " + (hexa.HomingElapsedTicks / (double)RuneSimulation.TICK_RATE).ToString("0.0")
                            + "s · " + homingState;
                        break;
                    }
                    if (hexa.HasStartedSniper)
                    {
                        string sniperState = hexa.IsCancelled ? "취소" : hexa.DelayTicks > 0 ? "대기"
                            : hexa.IsSniperPaused ? "빙결" : hexa.IsSniperAttacking ? (hexa.IsSniperLocked ? "고정" : "조준") : "완료";
                        objective = "Hexa Sniper " + hexa.SniperShotCount + "/" + HexaDefinition.SNIPER_SHOT_COUNT
                            + " · " + (hexa.SniperElapsedTicks / (double)RuneSimulation.TICK_RATE).ToString("0.0")
                            + "s · " + sniperState;
                        break;
                    }
                }
            _hud.SetTimer(objective, false);
            string executionState = sim.HasActiveBeam(0) ? GameData.L("ui.beamActive") : sim.Player.IsSpellRunning(0)
                ? GameData.L("ui.executing") + " " + sim.Player.ExecutionRemaining(0).ToString("0.0") + "s " + GameData.L("ui.remaining")
                : GameData.L("ui.cooldown") + " " + sim.Player.Cooldowns[0].ToString("0.0") + "s";
            _hud.SetSpellLine(GameData.L("ui.singleSpell") + "  " + _run.SpellName + "  ·  " + executionState
                + "  ·  " + SpellCostText.FormatCastAndMax(_run.SpellCastCosts, _run.SpellCosts));
            RenderPickupToast(sim);
        }

        /// <summary>시뮬레이션의 새 획득 아이템 이름·등급·개수를 차례로 표시하고, 그동안 얻은 크레딧은 합산해 뒤에 표시한다.</summary>
        private void RenderPickupToast(RuneSimulation sim)
        {
            _pendingToastFragments += Math.Max(0, sim.EarnedFragments - _lastFragments);
            _lastFragments = sim.EarnedFragments;
            IReadOnlyList<SimulationItemDrop> pickedItems = sim.PickedUpItems;
            for (; _lastPickedItemCount < pickedItems.Count; _lastPickedItemCount++)
            {
                SimulationItemDrop item = pickedItems[_lastPickedItemCount];
                string name = GameData.Runes.Get(item.RuneId).Name;
                _itemToasts.Enqueue(ModifierGradeText.FormatName(name, item.Grade) + " +" + item.Count);
            }
            float now = Time.unscaledTime;
            if (_isItemToastVisible && now < _toastUntil) return;
            if (_itemToasts.Count > 0)
            {
                _hud.SetToast(_itemToasts.Dequeue());
                _isItemToastVisible = true;
                _toastUntil = now + ITEM_TOAST_DURATION;
                return;
            }
            _isItemToastVisible = false;
            if (_pendingToastFragments > 0)
            {
                _hud.SetToast("+" + _pendingToastFragments + " " + GameData.L("ui.fragments"));
                _pendingToastFragments = 0;
                _toastUntil = Time.unscaledTime + TOAST_DURATION;
            }
            else if (now >= _toastUntil) _hud.SetToast("");
        }
    }
}
