using System;
using System.Collections.Generic;

using UnityEngine;

using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RuneCode
{
    /// <summary>
    /// 미션 화면의 Presenter다. 세션에서 시전 마법을 받아 MissionRun(모델)을 만들고 진행·일시정지·재도전·결과를 다루며,
    /// 매 프레임 시뮬레이션 값을 HUD View에 표시한다. 일시정지·디버그 팝업은 UIManager로 연다.
    /// </summary>
    public sealed class MissionPresenter
    {
        private const float TIMER_WARNING_SECONDS = 5f;
        private const float TOAST_DURATION = 0.9f;
        private const float VIGNETTE_BASE_STRENGTH = 0.45f;
        private const float VIGNETTE_DAMAGE_STRENGTH = 2f;
        private const float LOW_HEALTH_FRACTION = 0.3f;
        private const float LOW_HEALTH_MAX_LEVEL = 0.4f;
        private const double PLAYER_DAMAGE_EPSILON = 0.000001;

        private readonly RuneCodeSession _session;
        private readonly UIManager _ui;
        private readonly MissionScreen _screen;
        private readonly MissionHud _hud;
        private readonly ResultPanel _result;

        private MissionRun _run;
        private PausePopup _pausePopup;
        private double _lastPlayerDamage;
        private bool _isResultShown;
        private bool _hasPresentedChoices;
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

        /// <summary>현재 진행 중인 미션의 시뮬레이션이다. 경기장 그래픽이 읽으며 미션이 없으면 null이다.</summary>
        public RuneSimulation Simulation => _run?.Simulation;

        /// <summary>진행 중인 런이 라운드 대기(RoundBreak) 상태라 작업실을 열 수 있는지 반환한다. 일시정지·팝업 입력 잠금은 따로 확인한다.</summary>
        private bool CanOpenWorkshop => _run != null && !_run.IsFinished && _run.IsWaitingForNextRound;

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
            _hud.NextRoundClicked += StartNextRound;
            _hud.WorkshopClicked += OpenWorkshop;
            _result.Bind();
            _result.ImproveClicked += ReturnToWorkshop;
            _result.NextStageClicked += SelectNextStageAndReturnToWorkshop;
            _result.RetryClicked += RetryStage;
            _result.ClearModifierChoiceClicked += ChooseClearModifier;
        }

        /// <summary>
        /// 미션 화면에 들어올 때 보관 중인 런이 있으면 다시 준비한 마법으로 이어 가고, 없으면 새 미션을 시작한 뒤 전투 음악으로 전환한다.
        /// 준비에 실패하면 작업실 전환을 요청하며 보관 중인 런은 그대로 남는다.
        /// </summary>
        public void Begin()
        {
            bool isStarted = _session.IsRunSuspended && _run != null ? ResumeRun() : StartRun();
            if (!isStarted)
            {
                _session.RequestScreen(AppScreen.Workshop);
                return;
            }
            SoundManager.Instance.SetMusicState(1);
        }

        /// <summary>미션 화면을 떠날 때 빔 음원을 정지하고 기본 음악으로 복원한다. 작업실을 열어 런을 보관 중이면 미션을 유지하고, 아니면 놓는다.</summary>
        public void End()
        {
            SoundManager soundManager = SoundManager.Instance;
            if (soundManager != null)
            {
                soundManager.StopShotBeam();
                soundManager.SetMusicState(0);
            }
            _hud.SetNextRound(false, false);
            _hud.SetWorkshop(false, false);
            if (_session.IsRunSuspended) return;
            _run = null;
        }

        /// <summary>
        /// 프레임마다 미션을 진행하고 HUD를 갱신한다. 정산이 끝나면 결과를 한 번 표시한다.
        /// 팝업이 열려 있거나 문자 입력 중이면 조준 입력을 넘기지 않는다. 시전은 좌클릭을 누르는 동안이며 HUD 버튼 위에서는 하지 않는다.
        /// </summary>
        public void Tick(float unscaledDeltaTime)
        {
            LocalTelemetry.FlushUsabilityTest();
            if (_run == null) return;
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
                if (_screen.AdvanceEnding(_run.Simulation, unscaledDeltaTime)) ShowRoundChoices();
            }
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

        /// <summary>팝업·일시정지·종료 중 클릭을 거부하고 대기 중인 같은 미션을 한 번 진행한 뒤 HUD를 즉시 갱신한다.</summary>
        private void StartNextRound()
        {
            if (_run == null || _run.IsFinished || _run.IsPaused || _ui.IsPopupOpen || UiFactory.IsTyping()) return;
            if (_run.TryStartNextRound())
            {
                _hasPresentedChoices = false;
                RenderHud();
            }
        }

        /// <summary>
        /// 라운드 대기 중이고 일시정지·팝업·문자 입력이 없으면 런을 보관하고 작업실로 전환한다.
        /// 라운드 보상 카드를 고르기 전에도 열 수 있으며 카드는 돌아왔을 때 그대로 남는다.
        /// </summary>
        private void OpenWorkshop()
        {
            if (!CanOpenWorkshop || _run.IsPaused || _ui.IsPopupOpen || UiFactory.IsTyping()) return;
            _session.SuspendRunForWorkshop();
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
            popup.SetFeedback(_session.ScreenShake, _session.HitStop);
            popup.ShakeClicked += ToggleScreenShake;
            popup.HitStopClicked += ToggleHitStop;
            popup.SoundSettingsClicked += OpenSoundSettings;
            popup.TitleClicked += ReturnToTitle;
            popup.Closed += () => OnPauseClosed(popup);
        }

        /// <summary>일시정지 팝업이 닫히면 이벤트 구독을 풀고, 미션이 아직 일시정지 중이면 재개한다.</summary>
        private void OnPauseClosed(PausePopup popup)
        {
            popup.ShakeClicked -= ToggleScreenShake;
            popup.HitStopClicked -= ToggleHitStop;
            popup.SoundSettingsClicked -= OpenSoundSettings;
            popup.TitleClicked -= ReturnToTitle;
            if (_run != null && _run.IsPaused) _run.TogglePause();
        }

        /// <summary>화면 흔들림 설정을 반대로 저장하고 열린 일시정지 팝업 문구를 갱신한다.</summary>
        private void ToggleScreenShake()
        {
            _session.SetSetting("screenShake", !_session.ScreenShake);
            _ui.GetView<PausePopup>(UiLayer.Popup).SetFeedback(_session.ScreenShake, _session.HitStop);
        }

        /// <summary>히트스톱 설정을 반대로 저장하고 열린 일시정지 팝업 문구를 갱신한다.</summary>
        private void ToggleHitStop()
        {
            _session.SetSetting("hitStop", !_session.HitStop);
            _ui.GetView<PausePopup>(UiLayer.Popup).SetFeedback(_session.ScreenShake, _session.HitStop);
        }

        /// <summary>현재 일시정지 팝업 위에 마스터 음량 설정 팝업을 연다.</summary>
        private void OpenSoundSettings()
        {
            _ui.OpenPopup<SoundSettingsPopup>();
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

        /// <summary>정산과 보스 보상 선택을 마친 경우 작업실로 이동한다.</summary>
        private void ReturnToWorkshop()
        {
            if (_run == null || !_run.IsFinished) return;
            if (_run.Simulation.IsProgressionMission && _run.Simulation.Completed) return;
            if (_run.HasClearModifierChoices && !_run.IsClearModifierChosen) return;
            _session.RequestScreen(AppScreen.Workshop);
        }

        /// <summary>승리 결과에서 다음 스테이지를 선택하고 노드 강화 작업실로 돌아간다. 클리어나 보상 선택이 끝나지 않았으면 무시한다.</summary>
        private void SelectNextStageAndReturnToWorkshop()
        {
            if (_run == null || !_run.IsFinished || !_run.Simulation.Completed || _run.Simulation.IsProgressionMission) return;
            if (_run.HasClearModifierChoices && !_run.IsClearModifierChosen) return;
            int nextStage = _run.Simulation.StageNumber + 1;
            _session.SelectStage(nextStage);
            if (_session.SelectedStage != nextStage) return;
            _ui.GetView<WorkshopScreen>(UiLayer.Screen).ShowStageSelectionOnNextEntry();
            _session.RequestScreen(AppScreen.Workshop);
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
            int elapsedSeconds = (int)sim.Time;
            RunResultPopup popup = _ui.OpenPopup<RunResultPopup>();
            popup.Configure(sim.Completed ? "런 클리어" : "런 종료",
                GameData.L("ui.stage") + " " + sim.StageNumber,
                "플레이 타임 " + (elapsedSeconds / 60).ToString("00") + ":" + (elapsedSeconds % 60).ToString("00"),
                GameData.L("ui.kills") + " " + sim.KillCount);
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
                    string description = rune.Category == SpellGrammar.CATEGORY_BEHAVIOR
                        ? RuneDescriptionBuilder.GetDescription(rune)
                        : string.Join("\n", RuneDescriptionBuilder.GetGradeLines(rune, grade, false));
                    descriptions.Add(ModifierGradeText.Colorize(description, grade));
                }
                _result.ShowClearModifierChoices(GameData.L("result.clearModifierChoice"), names, descriptions, _run.ClearModifierGrades);
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
            _toastUntil = 0f;
            _hud.SetToast("");
            RenderHud();
            if (_run.IsResumedRoundBreak && _run.HasClearModifierChoices && !_run.IsClearModifierChosen)
                ShowRoundChoices(isResumed: true);
            return true;
        }

        /// <summary>
        /// 작업실에서 저장한 마법을 다시 검증해 보관 중인 런의 시전 마법을 교체하고 강화 능력치를 반영한 뒤 보관 표시를 해제하고 HUD를 갱신한다.
        /// 라운드 대기 상태와 고르지 않은 보상 카드는 유지하며, 준비나 교체에 실패하면 false를 반환한다.
        /// </summary>
        private bool ResumeRun()
        {
            if (!_session.TryPrepareMission(out CompiledSpell spell)) return false;
            if (!_run.TryReplaceSpell(spell, _session.Spells.SpellName)) return false;
            _run.TryApplyUpgradeStats();
            _session.ResumeSuspendedRun();
            // 결과 패널은 화면과 함께 꺼졌다 켜질 뿐 상태가 남으므로, 같은 후보를 다시 그려 선택 포커스만 되살린다.
            if (_hasPresentedChoices && _run.HasClearModifierChoices && !_run.IsClearModifierChosen) ShowPendingChoices();
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
            _hud.SetNextRound(sim.IsWaitingForNextRound && !_run.IsFinished && _run.IsClearModifierChosen,
                !_run.IsPaused && !_ui.IsPopupOpen);
            _hud.SetWorkshop(CanOpenWorkshop, !_run.IsPaused && !_ui.IsPopupOpen);
            _hud.SetBars(sim.Player.Hp, sim.Player.MaxHp, sim.Player.Energy, sim.Player.MaxEnergy, sim.EnergyRegen);
            _hud.SetCooldowns(sim.Player.Cooldowns[0], sim.Player.CooldownDurations[0],
                sim.Player.DashCooldown, sim.Player.DashCooldownDuration);
            _hud.SetStats(GameData.L("ui.kills") + " " + sim.KillCount + "  ·  " + GameData.L("ui.fragments") + " " + sim.EarnedFragments);
            if (sim.IsProgressionMission)
            {
                string objective = _run.IsFinished ? (sim.Completed ? "미션 완료" : sim.IsFailed ? "전투 패배" : "전투 중단")
                    : sim.IsWaitingForNextRound ? "다음 라운드 대기"
                    : (sim.IsBossStage ? "보스를 처치하세요 · " : "") + "남은 적 " + sim.RemainingWaveEnemyCount;
                _hud.SetTimer(objective, false);
            }
            else
            {
                string objective = GameData.L(sim.IsBossStage ? "ui.bossObjective" : "ui.surviveObjective")
                    + " · " + sim.RemainingTime.ToString("0.0") + "s";
                _hud.SetTimer(objective, sim.IsBossStage && sim.RemainingTime <= TIMER_WARNING_SECONDS);
            }
            string executionState = sim.HasActiveBeam(0) ? GameData.L("ui.beamActive") : sim.Player.IsSpellRunning(0)
                ? GameData.L("ui.executing") + " " + sim.Player.ExecutionRemaining(0).ToString("0.0") + "s " + GameData.L("ui.remaining")
                : GameData.L("ui.cooldown") + " " + sim.Player.Cooldowns[0].ToString("0.0") + "s";
            _hud.SetSpellLine(GameData.L("ui.singleSpell") + "  " + _run.SpellName + "  ·  " + executionState
                + "  ·  " + SpellCostText.FormatCastAndMax(_run.SpellCastCosts, _run.SpellCosts));
            if (sim.EarnedFragments > _lastFragments)
            {
                _hud.SetToast("+" + (sim.EarnedFragments - _lastFragments) + " " + GameData.L("ui.fragments"));
                _toastUntil = Time.unscaledTime + TOAST_DURATION;
            }
            _lastFragments = sim.EarnedFragments;
            if (Time.unscaledTime >= _toastUntil) _hud.SetToast("");
        }
    }
}
