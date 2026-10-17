using System;
using System.Collections.Generic;

using UnityEngine;

using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RuneCode
{
    /// <summary>
    /// 미션 화면의 Presenter다. 세션에서 시전 마법을 받아 MissionRun(모델)을 만들고 진행·일시정지·후퇴·재도전·결과를 다루며,
    /// 매 프레임 시뮬레이션 값을 HUD View에 표시한다. 일시정지·디버그 팝업은 UIManager로 연다.
    /// </summary>
    public sealed class MissionPresenter
    {
        private const float TIMER_WARNING_SECONDS = 5f;
        private const float TOAST_DURATION = 0.9f;

        private readonly RuneCodeSession _session;
        private readonly UIManager _ui;
        private readonly MissionScreen _screen;
        private readonly MissionHud _hud;
        private readonly ResultPanel _result;

        private MissionRun _run;
        private bool _isResultShown;
        private int _lastFragments;
        private float _toastUntil;

        /// <summary>현재 진행 중인 미션의 시뮬레이션이다. 경기장 그래픽이 읽으며 미션이 없으면 null이다.</summary>
        public RuneSimulation Simulation => _run?.Simulation;

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
            _result.Bind();
            _result.ImproveClicked += () => _session.RequestScreen(AppScreen.Workshop);
            _result.NextStageClicked += SelectNextStageAndReturnToWorkshop;
            _result.RetryClicked += RetryStage;
            _result.ClearModifierChoiceClicked += ChooseClearModifier;
        }

        /// <summary>미션 화면에 들어올 때 새 미션을 시작한다. 마법 준비에 실패하면 작업실 전환을 요청한다.</summary>
        public void Begin()
        {
            if (!StartRun()) _session.RequestScreen(AppScreen.Workshop);
        }

        /// <summary>미션 화면을 떠날 때 진행 중인 미션을 놓는다.</summary>
        public void End()
        {
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
                if (!_isResultShown) ShowResult();
                return;
            }
            bool isTyping = UiFactory.IsTyping();
            SimVector pointer = SimVector.Zero;
            bool hasPointer = !isTyping && !_ui.IsPopupOpen && _screen.TryGetPointer(out pointer);
            Mouse mouse = Mouse.current;
            bool isCasting = Application.isFocused && hasPointer && mouse != null && mouse.leftButton.isPressed && !IsPointerOverUi();
            _run.Step(isTyping, hasPointer, pointer, isCasting, unscaledDeltaTime,
                isCasting && mouse.leftButton.wasPressedThisFrame, mouse != null && mouse.leftButton.wasReleasedThisFrame);
            RenderHud();
        }

        /// <summary>Esc 입력으로 일시정지를 연다. 미션이 끝났으면 처리하지 않는다. 열린 팝업의 Esc는 UIManager가 먼저 처리한다.</summary>
        public bool HandleEscape()
        {
            if (_run == null || _run.IsFinished) return false;
            TogglePause();
            return true;
        }

        /// <summary>일시정지 상태를 바꾸고 일시정지 팝업을 열거나 닫는다. 결과 표시 중에는 무시한다.</summary>
        private void TogglePause()
        {
            if (_run == null || _run.IsFinished) return;
            if (_run.IsPaused)
            {
                _ui.CloseAllPopups();
                return;
            }
            _ui.CloseAllPopups();
            _run.TogglePause();
            PausePopup popup = _ui.OpenPopup<PausePopup>();
            popup.Bind();
            popup.SetFeedback(_session.Save.ScreenShake, _session.Save.HitStop);
            popup.ShakeClicked += ToggleScreenShake;
            popup.HitStopClicked += ToggleHitStop;
            popup.RetreatClicked += AbandonMission;
            popup.Closed += () => OnPauseClosed(popup);
        }

        /// <summary>일시정지 팝업이 닫히면 이벤트 구독을 풀고, 미션이 아직 일시정지 중이면 재개한다.</summary>
        private void OnPauseClosed(PausePopup popup)
        {
            popup.ShakeClicked -= ToggleScreenShake;
            popup.HitStopClicked -= ToggleHitStop;
            popup.RetreatClicked -= AbandonMission;
            if (_run != null && _run.IsPaused) _run.TogglePause();
        }

        /// <summary>화면 흔들림 설정을 반대로 저장하고 열린 일시정지 팝업 문구를 갱신한다.</summary>
        private void ToggleScreenShake()
        {
            _session.SetSetting("screenShake", !_session.Save.ScreenShake);
            _ui.GetView<PausePopup>(UiLayer.Popup).SetFeedback(_session.Save.ScreenShake, _session.Save.HitStop);
        }

        /// <summary>히트스톱 설정을 반대로 저장하고 열린 일시정지 팝업 문구를 갱신한다.</summary>
        private void ToggleHitStop()
        {
            _session.SetSetting("hitStop", !_session.Save.HitStop);
            _ui.GetView<PausePopup>(UiLayer.Popup).SetFeedback(_session.Save.ScreenShake, _session.Save.HitStop);
        }

        /// <summary>현재 전투를 중단해 보존 비율로 정산하고 팝업을 닫는다. 결과는 다음 Tick에서 표시된다.</summary>
        private void AbandonMission()
        {
            if (_run == null || _run.IsFinished) return;
            _run.AbandonMission();
            _ui.CloseAllPopups();
        }

        /// <summary>디버그 팝업을 열고 디버그 조작을 미션·세션에 연결한다. 디버그 실행이 아니면 무시한다.</summary>
        private void OpenDebug()
        {
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
            _session.SelectStage(_run.Simulation.StageNumber);
            StartRun();
        }

        /// <summary>승리 결과에서 다음 스테이지를 선택하고 노드 강화 작업실로 돌아간다. 클리어나 보상 선택이 끝나지 않았으면 무시한다.</summary>
        private void SelectNextStageAndReturnToWorkshop()
        {
            if (_run == null || !_run.IsFinished || !_run.Simulation.Completed) return;
            if (_run.HasClearModifierChoices && !_run.IsClearModifierChosen) return;
            int nextStage = _run.Simulation.StageNumber + 1;
            _session.SelectStage(nextStage);
            if (_session.SelectedStage != nextStage) return;
            _ui.GetView<WorkshopScreen>(UiLayer.Screen).ShowStageSelectionOnNextEntry();
            _session.RequestScreen(AppScreen.Workshop);
        }

        /// <summary>정산이 끝난 결과 View를 표시하고 클리어 Modifier 선택 후보가 있으면 모달 팝업을 연다.</summary>
        private void ShowResult()
        {
            _isResultShown = true;
            _ui.CloseAllPopups();
            SetResultSummary();
            _result.Show();
            if (_run.HasClearModifierChoices)
            {
                var names = new List<string>();
                var descriptions = new List<string>();
                for (int i = 0; i < _run.ClearModifierChoices.Count; i++)
                {
                    RuneDefinition rune = GameData.Runes.Get(_run.ClearModifierChoices[i]);
                    string grade = _run.ClearModifierGrades[i];
                    names.Add(ModifierGradeText.Colorize(rune.Name, grade));
                    descriptions.Add(ModifierGradeText.Colorize(FormatClearModifierDescription(rune, grade), grade));
                }
                _result.ShowClearModifierChoices(GameData.L("result.clearModifierChoice"), names, descriptions, _run.ClearModifierGrades);
            }
            else _result.HideClearModifierChoices();
        }

        /// <summary>Modifier와 등급의 현재 효과를 보상 카드에 표시할 줄 단위 문구로 반환한다.</summary>
        private static string FormatClearModifierDescription(RuneDefinition rune, string grade)
        {
            var values = new List<string>();
            ModifierGradeTable grades = GameData.ModifierGrades;
            object gradeValue = grades.GetGradeValue(rune.Id, grade);
            switch (grades.GetEffectType(rune.Id))
            {
                case "effectMultiplier":
                    values.Add(GameData.L("ui.gradeDamage") + " ×" + Convert.ToSingle(gradeValue).ToString("0.##"));
                    break;
                case "multipleCount":
                    values.Add(GameData.L("ui.gradeCount") + " " + gradeValue);
                    break;
                case "waveCount":
                    values.Add(GameData.L("ui.gradeWaveCount") + " " + gradeValue);
                    break;
                case "pierceCount":
                    values.Add(GameData.L("ui.gradePierce") + " " + gradeValue);
                    break;
                case "radiusMultiplier":
                    values.Add(GameData.L("ui.gradeRadius") + " ×" + Convert.ToSingle(gradeValue).ToString("0.##"));
                    break;
                case "durationMultiplier":
                    values.Add(GameData.L("ui.gradeDuration") + " ×" + Convert.ToSingle(gradeValue).ToString("0.##"));
                    break;
                case "speedMultiplier":
                    values.Add(GameData.L("ui.gradeSpeed") + " ×" + Convert.ToSingle(gradeValue).ToString("0.##"));
                    break;
                case "homingTier":
                    string tier = gradeValue as string;
                    HomingTierDefinition homing = rune.FindHomingTier(tier);
                    if (homing != null)
                    {
                        values.Add(GameData.L("ui.gradeHomingTurn") + " " + homing.HomingTurn.ToString("0.#") + "°");
                        values.Add(GameData.L("ui.gradeHomingRange") + " " + homing.HomingRange.ToString("0.#"));
                        values.Add(GameData.L("ui.gradeOrbitTargetCount") + " " + homing.OrbitTargetCount);
                    }
                    break;
            }
            return string.Join("\n", values);
        }

        /// <summary>결과 View에 정산 문구와 스테이지·진행 정보를 채운다.</summary>
        private void SetResultSummary()
        {
            RuneSimulation sim = _run.Simulation;
            int elapsedSeconds = (int)sim.Time;
            string playTime = "플레이 타임 " + (elapsedSeconds / 60).ToString("00") + ":" + (elapsedSeconds % 60).ToString("00");
            _result.SetContent(sim.Completed ? "게임 완료" : "게임 종료",
                GameData.L("ui.stage") + " " + sim.StageNumber,
                playTime,
                sim.Completed);
            SetResultRewards();
        }

        /// <summary>이번 미션에서 획득한 보상을 아이콘 수량과 Modifier 등급 효과 문구로 변환해 결과 View에 전달한다.</summary>
        private void SetResultRewards()
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
                string description = ModifierGradeText.Colorize(gradeLabel + FormatClearModifierDescription(rune, reward.Grade), reward.Grade);
                rewards.Add(new ResultRewardContent(true, reward.Count, ModifierGradeText.FormatName(rune.Name, reward.Grade), description));
            }
            _result.SetRewards(rewards);
        }

        /// <summary>클리어 Modifier 후보를 지급하고 선택 확정 상태와 결과 문구를 갱신한다. 유효한 선택이 아니면 무시한다.</summary>
        private void ChooseClearModifier(int index)
        {
            if (_run == null || !_run.ChooseClearModifier(index)) return;
            SetResultSummary();
            _result.HideClearModifierChoices();
        }

        /// <summary>세션에서 시전 마법을 준비받아 새 미션을 만들고 HUD·결과 View를 시작 상태로 되돌린다. 실패하면 false를 반환한다.</summary>
        private bool StartRun()
        {
            if (!_session.TryPrepareMission(out CompiledSpell spell)) return false;
            _run = new MissionRun(_session, spell, _session.Spells.SpellName);
            _isResultShown = false;
            _ui.CloseAllPopups();
            _result.Hide();
            _lastFragments = _run.Simulation.EarnedFragments;
            _toastUntil = 0f;
            _hud.SetToast("");
            _hud.SetStage(GameData.L("ui.stage") + " " + _run.Simulation.StageNumber);
            RenderHud();
            return true;
        }

        /// <summary>포인터가 버튼 등 UI 입력 대상 위에 있는지 반환한다.</summary>
        private static bool IsPointerOverUi() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        /// <summary>현재 시뮬레이션 값으로 HUD 막대·전투 정보·마법 줄과 조각 토스트를 표시한다.</summary>
        private void RenderHud()
        {
            RuneSimulation sim = _run.Simulation;
            _hud.SetBars(sim.Player.Hp, sim.Player.MaxHp, sim.Player.Energy, sim.Player.MaxEnergy, sim.EnergyRegen);
            _hud.SetCooldowns(sim.Player.Cooldowns[0], sim.Player.CooldownDurations[0],
                sim.Player.DashCooldown, sim.Player.DashCooldownDuration);
            _hud.SetStats(GameData.L("ui.kills") + " " + sim.KillCount + "  ·  " + GameData.L("ui.fragments") + " " + sim.EarnedFragments);
            string objective = sim.IsBossStage
                ? GameData.L("ui.bossObjective") + " · " + sim.RemainingTime.ToString("0.0") + "s"
                : GameData.L("ui.killProgress") + " " + sim.TargetKills + " / " + sim.KillTarget;
            _hud.SetTimer(objective,
                sim.IsBossStage && sim.RemainingTime <= TIMER_WARNING_SECONDS);
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
