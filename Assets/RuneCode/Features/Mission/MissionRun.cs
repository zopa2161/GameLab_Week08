using System;
using System.Collections.Generic;
using System.Diagnostics;

using UnityEngine.InputSystem;

namespace RuneCode
{
    /// <summary>이번 미션에서 획득한 조각 또는 소지 아이템(모디·속성·행동)의 표시용 원본 정보다.</summary>
    public sealed class MissionReward
    {
        private readonly bool _isModifier;
        private readonly string _runeId;
        private readonly string _grade;
        private readonly int _count;

        /// <summary>소지 아이템(모디·속성·행동)인지 반환한다. false면 조각 보상이다.</summary>
        public bool IsModifier => _isModifier;
        public string RuneId => _runeId;
        public string Grade => _grade;
        public int Count => _count;

        /// <summary>소지 아이템 ID·등급 또는 조각 유형과 현재 획득 수량을 보관한다.</summary>
        public MissionReward(bool isModifier, string runeId, string grade, int count)
        {
            _isModifier = isModifier;
            _runeId = runeId;
            _grade = grade;
            _count = count;
        }
    }

    /// <summary>
    /// 미션 전투 한 판의 시뮬레이션, 고정 스텝 입력, 일시정지, 정산을 책임지는 일반 클래스다.
    /// MissionScreen이 소유하며 세이브 반영은 RuneCodeSession.SettleMission으로 위임한다.
    /// </summary>
    public sealed class MissionRun
    {
        private const double FIXED_STEP = 1.0 / RuneSimulation.TICK_RATE;

        private readonly RuneCodeSession _session;
        private readonly RuneSimulation _simulation;
        private readonly int _seed;
        private readonly string _testCombatId;
        private CompiledSpell _spell;
        private string _spellName;
        private string _spellBuildId;
        private string _coreNodeId;

        private double _accumulator;
        private int _telemetryCount;
        private int _castRequestCount;
        private int _castSuccessCount;
        private int _dashInputCount;
        private bool _hasPendingDash;
        private bool _wasCasting;
        private bool _hasPendingCastPress;
        private bool _hasPendingCastRelease;
        private bool _isPaused;
        private bool _settled;
        private string _lastResult;
        private SpellGraph _resultGraph;
        private readonly List<SpellGraph> _resultLibrary = new List<SpellGraph>();
        private readonly List<SimulationItemDrop> _pickedModifiers = new List<SimulationItemDrop>();
        private readonly List<SimulationItemDrop> _pendingRoundDrops = new List<SimulationItemDrop>();
        private int _grantedFragments;
        private readonly List<MissionReward> _rewards = new List<MissionReward>();
        private List<string> _clearModifierChoices;
        private List<string> _clearModifierGrades;
        private bool _isClearModifierChosen;
        private readonly bool _isResumedRoundBreak;
        private Stopwatch _roundBreakClock;
        private readonly Stopwatch _activeMissionClock = Stopwatch.StartNew();

        /// <summary>현재 전투 시뮬레이션을 반환한다. 경기장 그래픽과 HUD가 읽는다.</summary>
        public RuneSimulation Simulation => _simulation;

        /// <summary>시전할 마법 이름을 반환한다.</summary>
        public string SpellName => _spellName;

        /// <summary>진행 초기화 전에 보관한 마지막 빌드와 참조 마법 목록이다.</summary>
        public SpellGraph ResultGraph => _resultGraph;
        public IReadOnlyList<SpellGraph> ResultLibrary => _resultLibrary;
        public CompiledSpell ResultSpell => _spell;

        /// <summary>시전할 마법의 에너지 비용을 반환한다.</summary>
        public float SpellCost => _spell?.EnergyCost ?? 0f;

        /// <summary>시전할 마법의 자원별 최대 비용을 반환한다.</summary>
        public ResourceCostSet SpellCosts => _spell?.ResourceCosts ?? ResourceCostSet.Empty;

        /// <summary>시전할 마법이 적중·소멸 이벤트 없이 시전만으로 차감하는 자원별 비용을 반환한다.</summary>
        public ResourceCostSet SpellCastCosts => _spell?.GetCastCosts(GameData.Balance.Sim.ExecutionTimeScale) ?? ResourceCostSet.Empty;

        /// <summary>일시정지 상태를 반환한다.</summary>
        public bool IsPaused => _isPaused;

        /// <summary>결과 정산이 끝나 전투가 종료되었는지 반환한다.</summary>
        public bool IsFinished => _settled;

        public bool IsWaitingForNextRound => _simulation.IsWaitingForNextRound;

        /// <summary>저장된 라운드 진행을 이어받아 라운드 대기 상태로 시작했는지 반환한다.</summary>
        public bool IsResumedRoundBreak => _isResumedRoundBreak;

        /// <summary>정산 후 표시할 결과 문구를 반환한다.</summary>
        public string LastResult => _lastResult;

        /// <summary>이번 미션에서 획득한 조각과 Modifier 목록을 반환한다.</summary>
        public IReadOnlyList<MissionReward> Rewards => _rewards;

        /// <summary>보스 클리어 Modifier 선택 후보 룬 ID 목록을 반환하고 선택 보상이 없으면 빈 목록이다.</summary>
        public IReadOnlyList<string> ClearModifierChoices => _clearModifierChoices ?? (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>보스 클리어 Modifier 선택 후보별 등급 문자를 후보와 같은 순서로 반환하고 선택 보상이 없으면 빈 목록이다.</summary>
        public IReadOnlyList<string> ClearModifierGrades => _clearModifierGrades ?? (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>보스 클리어 Modifier 선택 후보가 있는지 반환한다.</summary>
        public bool HasClearModifierChoices => _clearModifierChoices != null && _clearModifierChoices.Count > 0;

        /// <summary>보스 클리어 Modifier 선택이 이미 확정됐는지 반환한다.</summary>
        public bool IsClearModifierChosen => _isClearModifierChosen;

        /// <summary>
        /// 세션 능력치로 연속 미션을 만들고 출격 그래프 스냅샷·전투 ID와 시작 텔레메트리를 기록한다.
        /// 저장된 라운드 진행이 있으면 그 스테이지·라운드의 라운드 대기 상태로 이어 시작하며 저장된 보상 후보를 복원한다.
        /// </summary>
        public MissionRun(RuneCodeSession session, CompiledSpell spell, string spellName)
        {
            _session = session;
            _spell = spell;
            _spellName = spellName;
            // 미션마다 시드를 바꿔 스폰·드롭·보상 순서가 매번 달라지게 한다. 재현용으로 시작 텔레메트리에 남긴다.
            int seed = Environment.TickCount;
            _seed = seed;
            PlayerSave save = session.Save;
            _isResumedRoundBreak = save.HasProgressionRound;
            double resumeHp = 0;
            if (_isResumedRoundBreak)
            {
                // 저장 이후 강화로 늘어난 최대 체력 증가분만큼 이어 받은 체력을 올리고 세션 최대치로 제한한다.
                resumeHp = Math.Min(save.ProgressionHp + Math.Max(0, session.MaxHp - save.ProgressionMaxHp), session.MaxHp);
                if (save.PendingRoundChoiceIds.Count > 0)
                {
                    _clearModifierChoices = new List<string>(save.PendingRoundChoiceIds);
                    _clearModifierGrades = new List<string>(save.PendingRoundChoiceGrades);
                }
                else _isClearModifierChosen = true;
                _roundBreakClock = Stopwatch.StartNew();
            }
            _simulation = new RuneSimulation(SimulationSetup.FromGameData(seed, true, session.MaxHp, session.MaxEnergy, 1, session.EnergyRegen,
                session.DamageMultiplier, session.MoveSpeedMultiplier, session.ScrapGainMultiplier,
                scrapPickupRangeMultiplier: session.ScrapPickupRangeMultiplier,
                killEnergyBonus: session.KillEnergyBonus, scrapHealRatio: session.ScrapHealRatio, isProgressionMission: true,
                progressionStage: save.ProgressionStage, progressionRound: save.ProgressionRound, progressionHp: resumeHp));
            _simulation.SetLoadout(new[] { spell });
            _simulation.SetUnlockedElements(session.GetUnlockedElements());
            _coreNodeId = FindCoreNodeId(session.Spells.Graph);
            _spellBuildId = LocalTelemetry.SaveUsabilityBuild(null, session.Spells.Graph, new Dictionary<string, object>
            {
                ["reason"] = "mission_start",
                ["signature"] = spell.Signature,
                ["comparison_key"] = LocalTelemetry.GetUsabilityGraphSignature(session.Spells.Graph),
                ["stage"] = _simulation.StageNumber,
                ["ram_used"] = spell.RamUsed,
                ["is_compile_valid"] = true,
            });
            _testCombatId = LocalTelemetry.BeginUsabilityCombat(_simulation.StageNumber, _spellBuildId, seed, _simulation.TakeEnemyCombatLogSnapshot);
            LocalTelemetry.RecordUsabilityEvent("stage_enter", "combat", new Dictionary<string, object>
            {
                ["stage"] = _simulation.StageNumber,
                ["seed"] = seed,
                ["resumed"] = _isResumedRoundBreak,
                ["inventory"] = session.GetInventoryLogSnapshot(),
            });
            LocalTelemetry.Record(0, "mission.start", _simulation.StageNumber + ":" + spell.Signature + ":seed=" + seed);
        }

        /// <summary>
        /// 라운드 대기 중 작업실에서 다시 준비한 마법으로 시전 마법·이름·핵심 노드를 교체하고 새 빌드 스냅샷을 기록한다.
        /// 라운드 대기가 아니거나 정산이 끝났으면 바꾸지 않고 false를 반환한다.
        /// </summary>
        public bool TryReplaceSpell(CompiledSpell spell, string spellName)
        {
            if (_settled || !_simulation.IsWaitingForNextRound) return false;
            _spell = spell;
            _spellName = spellName;
            _simulation.SetLoadout(new[] { spell });
            _coreNodeId = FindCoreNodeId(_session.Spells.Graph);
            _spellBuildId = LocalTelemetry.SaveUsabilityBuild(null, _session.Spells.Graph, new Dictionary<string, object>
            {
                ["reason"] = "round_break_edit",
                ["signature"] = spell.Signature,
                ["comparison_key"] = LocalTelemetry.GetUsabilityGraphSignature(_session.Spells.Graph),
                ["stage"] = _simulation.StageNumber,
                ["round"] = _simulation.RoundNumber,
                ["ram_used"] = spell.RamUsed,
                ["is_compile_valid"] = true,
            });
            return true;
        }

        /// <summary>
        /// 라운드 대기 중 세션의 현재 강화 능력치를 시뮬레이션에 반영하고 해금 속성을 다시 설정한 뒤 적용 전후 최대 체력·전력을 기록한다.
        /// 정산이 끝났거나 라운드 대기가 아니면 false를 반환한다.
        /// </summary>
        public bool TryApplyUpgradeStats()
        {
            if (_settled) return false;
            double maxHpBefore = _simulation.Player.MaxHp;
            double maxEnergyBefore = _simulation.Player.MaxEnergy;
            if (!_simulation.TryApplyUpgradeStats(_session.MaxHp, _session.MaxEnergy, _session.EnergyRegen,
                _session.DamageMultiplier, _session.MoveSpeedMultiplier, _session.ScrapGainMultiplier,
                _session.ScrapPickupRangeMultiplier, _session.ScrapHealRatio, _session.KillEnergyBonus)) return false;
            _simulation.SetUnlockedElements(_session.GetUnlockedElements());
            LocalTelemetry.RecordUsabilityEvent("upgrade_stats_applied", "round_break", new Dictionary<string, object>
            {
                ["combat_id"] = _testCombatId,
                ["stage"] = _simulation.StageNumber, ["round"] = _simulation.RoundNumber,
                ["max_hp_before"] = maxHpBefore, ["max_hp_after"] = _simulation.Player.MaxHp,
                ["max_energy_before"] = maxEnergyBefore, ["max_energy_after"] = _simulation.Player.MaxEnergy,
                ["skill_tree"] = _session.GetSkillTreeLogSnapshot(),
            });
            return true;
        }

        /// <summary>그래프에서 시작 트리거(core.cast) 노드의 ID를 찾아 반환하고 없으면 null을 반환한다.</summary>
        private static string FindCoreNodeId(SpellGraph graph)
        {
            foreach (GraphNode node in graph.Nodes)
                if (node.RuneId == SpellGrammar.CORE_RUNE) return node.Id;
            return null;
        }

        /// <summary>미션을 일시정지하거나 재개하고 시간계에서 정지 구간을 제외한다. 전환 시점과 정지 시간을 제외한 누적 초를 로그에 기록한다.</summary>
        public void TogglePause()
        {
            if (_settled) return;
            _isPaused = !_isPaused;
            if (_isPaused)
            {
                _activeMissionClock.Stop();
                _roundBreakClock?.Stop();
            }
            else
            {
                _activeMissionClock.Start();
                _roundBreakClock?.Start();
            }
            _simulation.CancelBeamInput();
            LocalTelemetry.RecordUsabilityEvent(_isPaused ? "pause_start" : "pause_end",
                _simulation.IsWaitingForNextRound ? "round_break" : "combat", new Dictionary<string, object>
                {
                    ["stage"] = _simulation.StageNumber, ["round"] = _simulation.RoundNumber,
                    ["active_seconds"] = _activeMissionClock.Elapsed.TotalSeconds,
                });
        }

        /// <summary>선택 완료 후 다음 라운드 또는 스테이지를 시작하고 라운드 종료부터 버튼 클릭까지 일시정지를 제외한 경과 초를 기록한다. 후보가 없는 이어하기는 선택 없이 시작할 수 있다. 시작 성공 여부를 반환한다.</summary>
        public bool TryStartNextRound()
        {
            if (_settled || _isPaused || !_simulation.IsWaitingForNextRound
                || (HasClearModifierChoices && !_isClearModifierChosen)) return false;
            ResetRoundInput();
            int completedStage = _simulation.StageNumber;
            int completedRound = _simulation.RoundNumber;
            if (!_simulation.TryStartNextRound()) return false;
            EndRoundBreak(completedStage, completedRound, "next_round_clicked");
            return true;
        }

        /// <summary>
        /// 디버그 라운드 대기 중 지정 스테이지·라운드의 마지막 웨이브로 진입한다. 기본 라운드는 최종 보스이며 고르지 않은 보상은 버린다.
        /// isHexaPreview이면 이번 진입만 Hexa 한 기로 대체하고 단독 공격 또는 복합 패턴을 준비하며, isHexaSpiralPreview이면 나선 공격을,
        /// isHexaAutoPreview이면 우선순위·횟수 쿨타임 자동 운영을 준비한다. 진입 성공 여부를 반환한다.
        /// </summary>
        public bool DebugStartBoss(int stage, int round = StageCatalog.PROGRESSION_ROUND_COUNT, bool isHexaPreview = false,
            bool isHexaSprayPreview = false, bool isHexaHomingPreview = false, bool isHexaSniperPreview = false,
            bool isHexaCompositePreview = false, HexaSubAttack compositeFirst = HexaSubAttack.None,
            HexaSubAttack compositeSecond = HexaSubAttack.None, bool isHexaSpiralPreview = false,
            bool isHexaAutoPreview = false)
        {
            if (!_session.IsDebugEnabled || _settled || _isPaused || !_simulation.IsWaitingForNextRound) return false;
            ResetRoundInput();
            int completedStage = _simulation.StageNumber;
            int completedRound = _simulation.RoundNumber;
            if (!_simulation.DebugStartBossWave(stage, round, isHexaPreview, isHexaSprayPreview, isHexaHomingPreview,
                isHexaSniperPreview, isHexaCompositePreview, compositeFirst, compositeSecond, isHexaSpiralPreview,
                isHexaAutoPreview)) return false;
            EndRoundBreak(completedStage, completedRound, "debug_boss_clicked");
            return true;
        }

        /// <summary>새 전투 구간 시작 전에 고정 스텝 누적값과 대기 중인 대시·시전 입력을 비우고 빔 입력을 취소한다.</summary>
        private void ResetRoundInput()
        {
            _accumulator = 0;
            _hasPendingDash = false;
            _hasPendingCastPress = false;
            _hasPendingCastRelease = false;
            _wasCasting = false;
            _simulation.CancelBeamInput();
        }

        /// <summary>끝낸 스테이지·라운드와 종료 사유로 라운드 대기 종료를 기록하고 대기 시계와 보상 후보 상태를 비운다.</summary>
        private void EndRoundBreak(int completedStage, int completedRound, string reason)
        {
            _roundBreakClock.Stop();
            LocalTelemetry.RecordUsabilityEvent("round_break_end", "round_break", new Dictionary<string, object>
            {
                ["stage"] = completedStage, ["round"] = completedRound,
                ["seconds"] = _roundBreakClock.Elapsed.TotalSeconds,
                ["active_seconds"] = _activeMissionClock.Elapsed.TotalSeconds,
                ["reason"] = reason,
                ["next_stage"] = _simulation.StageNumber, ["next_round"] = _simulation.RoundNumber,
            });
            _roundBreakClock = null;
            _clearModifierChoices = null;
            _clearModifierGrades = null;
            _isClearModifierChosen = false;
        }

        /// <summary>
        /// 이동·대시·줍기 키, 포인터 조준과 시전 입력(isCasting, 좌클릭 유지)을 모아 MaxFrameSteps 제한 안의 고정 스텝만큼 시뮬레이션을 갱신하고,
        /// 입력·노드 텔레메트리를 기록하고 종료 조건을 확인한다.
        /// </summary>
        public void Step(bool isTyping, bool hasPointer, SimVector pointer, bool isCasting, float unscaledDeltaTime,
            bool castPressed = false, bool castReleased = false)
        {
            if (_settled) return;
            if (_isPaused || _simulation.IsWaitingForNextRound)
            {
                _accumulator = 0; _hasPendingDash = false;
                _hasPendingCastPress = false; _hasPendingCastRelease = false;
                _wasCasting = false;
                _simulation.CancelBeamInput();
                return;
            }
            _hasPendingCastPress |= castPressed || isCasting && !_wasCasting;
            _hasPendingCastRelease |= castReleased || !isCasting && _wasCasting;
            if (isCasting && !_wasCasting)
            {
                _castRequestCount++;
                LocalTelemetry.RecordUsabilityEvent("simulation_request", "combat", new Dictionary<string, object>
                {
                    ["input_method"] = "pointer",
                    ["stage"] = _simulation.StageNumber,
                });
            }
            _wasCasting = isCasting;
            var keyboard = Keyboard.current;
            var movement = SimVector.Zero;
            if (!isTyping && keyboard != null)
            {
                movement = new SimVector((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.sKey.isPressed ? 1 : 0) - (keyboard.wKey.isPressed ? 1 : 0));
                if (keyboard.spaceKey.wasPressedThisFrame)
                {
                    _hasPendingDash = true;
                    _dashInputCount++;
                }
            }
            var maxSteps = GameData.Balance.Limits.MaxFrameSteps;
            _accumulator = Math.Min(_accumulator + unscaledDeltaTime, FIXED_STEP * maxSteps);
            var steps = 0;
            while (_accumulator >= FIXED_STEP && steps++ < maxSteps)
            {
                var aim = hasPointer ? pointer - _simulation.Player.Position : _simulation.Player.AimDirection;
                _simulation.Step(new SimulationInput(movement, aim, isCasting, false, false, _hasPendingDash,
                    castPressed: _hasPendingCastPress, castReleased: _hasPendingCastRelease,
                    aimPoint: hasPointer ? pointer : (SimVector?)null));
                _hasPendingCastPress = false;
                _hasPendingCastRelease = false;
                _hasPendingDash = false;
                _accumulator -= FIXED_STEP;
            }
            CaptureNodeTelemetry();
            CollectPickedItems();
            if (_simulation.IsWaitingForNextRound && !HasClearModifierChoices)
            {
                SetRoundModifierChoices();
                CommitRoundRewards();
                _roundBreakClock = Stopwatch.StartNew();
                LocalTelemetry.RecordUsabilityEvent("round_break_start", "round_break", new Dictionary<string, object>
                {
                    ["stage"] = _simulation.StageNumber, ["round"] = _simulation.RoundNumber,
                    ["active_seconds"] = _activeMissionClock.Elapsed.TotalSeconds,
                    ["enemy_stats"] = _simulation.TakeEnemyCombatLogSnapshot(),
                });
            }
            if (_simulation.Completed || _simulation.IsFailed) FinishMission();
        }

        /// <summary>라운드 종료 시 해당 라운드의 보상 드롭 테이블과 시뮬레이션 난수로 후보와 등급을 한 번 추첨한다.</summary>
        private void SetRoundModifierChoices()
        {
            if (!_simulation.TryDrawClearModifierChoices(out List<string> candidates, out List<string> grades)) return;
            _clearModifierChoices = candidates;
            _clearModifierGrades = grades;
            _isClearModifierChosen = false;
            RecordModifierChoices();
        }

        /// <summary>새 소지 아이템 드롭을 기록하고 라운드 지급 대기 목록과 결과 목록에 담는다. 세이브 지급은 라운드가 끝날 때 한다.</summary>
        private void CollectPickedItems()
        {
            foreach (SimulationItemDrop drop in _simulation.TakeNewPickedItems())
            {
                LocalTelemetry.RecordUsabilityEvent("combat_item_pickup", "combat", new Dictionary<string, object>
                {
                    ["rune_id"] = drop.RuneId,
                    ["grade"] = drop.Grade,
                    ["count"] = drop.Count,
                    ["inventory_grant_enabled"] = true,
                });
                _pendingRoundDrops.Add(drop);
                _pickedModifiers.Add(drop);
                AddModifierReward(drop.RuneId, drop.Grade, drop.Count);
            }
        }

        /// <summary>이번 라운드에 모은 조각(아직 지급하지 않은 수집량)과 소지 아이템, 끝낸 라운드 위치·체력·보상 후보를 세이브에 한 번에 지급·기록하고 대기 목록을 비운다.</summary>
        private void CommitRoundRewards()
        {
            int fragments = _simulation.EarnedFragments;
            _session.CommitRoundRewards(fragments - _grantedFragments, _pendingRoundDrops,
                _simulation.StageNumber, _simulation.RoundNumber, _simulation.Player.Hp, _simulation.Player.MaxHp,
                _clearModifierChoices, _clearModifierGrades);
            _grantedFragments = fragments;
            _pendingRoundDrops.Clear();
        }

        /// <summary>같은 룬과 등급의 이번 미션 획득 수량을 합산한다.</summary>
        private void AddModifierReward(string runeId, string grade, int count)
        {
            if (count <= 0) return;
            for (int index = 0; index < _rewards.Count; index++)
            {
                MissionReward reward = _rewards[index];
                if (!reward.IsModifier || reward.RuneId != runeId || reward.Grade != grade) continue;
                _rewards[index] = new MissionReward(true, runeId, grade, reward.Count + count);
                return;
            }
            _rewards.Add(new MissionReward(true, runeId, grade, count));
        }

        /// <summary>현재 전투에서 실제 획득한 모듈 보상을 종류·등급·수량 목록으로 복사한다.</summary>
        private List<Dictionary<string, object>> GetModuleRewardsLogSnapshot()
        {
            var rewards = new List<Dictionary<string, object>>();
            foreach (MissionReward reward in _rewards)
            {
                if (!reward.IsModifier) continue;
                rewards.Add(new Dictionary<string, object>
                {
                    ["rune_id"] = reward.RuneId,
                    ["grade"] = string.IsNullOrEmpty(reward.Grade) ? null : reward.Grade,
                    ["count"] = reward.Count,
                });
            }
            return rewards;
        }

        /// <summary>이번 전투에서 주운 Modifier를 룬 ID와 등급별로 합산해 결과 문구에 더한다.</summary>
        private void AppendPickedModifiers()
        {
            if (_pickedModifiers.Count == 0) return;
            var totals = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
            foreach (SimulationItemDrop drop in _pickedModifiers)
            {
                if (!totals.TryGetValue(drop.RuneId, out SortedDictionary<string, int> grades))
                {
                    grades = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    totals.Add(drop.RuneId, grades);
                }
                string grade = drop.Grade ?? string.Empty;
                grades[grade] = (grades.TryGetValue(grade, out int count) ? count : 0) + drop.Count;
            }
            foreach (var pair in totals)
            {
                foreach (var gradePair in pair.Value)
                    _lastResult += "\n" + GameData.L("result.modifierPickup") + " "
                        + ModifierGradeText.FormatName(GameData.Runes.Get(pair.Key).Name, gradePair.Key) + " +" + gradePair.Value;
            }
        }

        /// <summary>
        /// 전투를 한 번만 종료 처리한다. 클리어와 사망 모두 런 종료이므로 정산·보상 지급 없이 텔레메트리를 남긴 뒤
        /// 세션의 EndRun으로 저장을 전부 새로 만든다. 라운드 대기 목록에 남은 획득물도 지급하지 않는다.
        /// </summary>
        public void FinishMission()
        {
            if (_settled) return;
            _activeMissionClock.Stop();
            _settled = true;
            var cleared = _simulation.Completed;
            var fragments = _simulation.SettlementFragments;
            _pendingRoundDrops.Clear();
            var result = cleared ? "clear" : _simulation.IsTimedOut ? "timeout" : "death";
            var resultKey = cleared ? (_simulation.IsBossStage ? "result.bossDefeated" : "result.survived")
                : _simulation.IsTimedOut ? "result.timeout" : "result.dead";
            _lastResult = GameData.L(resultKey);
            AppendPickedModifiers();
            _isPaused = false;
            LocalTelemetry.Record(_simulation.Tick, "mission.result", result + ":" + fragments);
            LocalTelemetry.EndUsabilityCombat(new Dictionary<string, object>
            {
                ["stage"] = _simulation.StageNumber,
                ["spell_build_id"] = _spellBuildId,
                ["result"] = result,
                ["skill_tree"] = _session.GetSkillTreeLogSnapshot(),
                ["inventory"] = _session.GetInventoryLogSnapshot(),
                ["module_rewards"] = GetModuleRewardsLogSnapshot(),
                ["duration_ms"] = (long)(_simulation.Time * 1000),
                ["active_seconds"] = _activeMissionClock.Elapsed.TotalSeconds,
                ["damage_dealt"] = _simulation.TotalDamage,
                ["action_stats"] = _simulation.GetActionCombatLogSnapshot(),
                ["damage_received"] = _simulation.Player.DamageReceived,
                ["shield_absorbed"] = _simulation.Player.ShieldAbsorbed,
                ["energy_spent"] = _simulation.EnergySpent,
                ["cast_requests"] = _castRequestCount,
                ["cast_successes"] = _castSuccessCount,
                ["dash_inputs"] = _dashInputCount,
                ["dashes_started"] = _simulation.Player.DashesStarted,
                ["kill_count"] = _simulation.KillCount,
                ["fragments"] = fragments,
                ["seed"] = _seed,
            });
            if (cleared)
            {
                string signature = _spell.Signature;
                bool isFirstCompletion = LocalTelemetry.MarkFirstUsabilityCompletion(_session.Spells.Graph, out string comparisonKey);
                LocalTelemetry.RecordUsabilityEvent("spell_complete", "combat", new Dictionary<string, object>
                {
                    ["combat_id"] = _testCombatId,
                    ["spell_build_id"] = _spellBuildId,
                    ["signature"] = signature,
                    ["comparison_key"] = comparisonKey,
                    ["is_first_completion"] = isFirstCompletion,
                });
            }
            _resultGraph = _session.Spells.Graph.Clone();
            _resultLibrary.Clear();
            foreach (SpellGraph graph in _session.Spells.Library) _resultLibrary.Add(graph.Clone());
            _session.EndRun();
        }

        /// <summary>현재 후보 순번·룬·등급·수량과 스테이지·라운드를 기존 보상 제시 이벤트에 기록하며 별도 선택 시간은 측정하지 않는다.</summary>
        private void RecordModifierChoices()
        {
            var choices = new List<Dictionary<string, object>>();
            for (int index = 0; index < _clearModifierChoices.Count; index++)
                choices.Add(new Dictionary<string, object>
                {
                    ["choice_index"] = index,
                    ["rune_id"] = _clearModifierChoices[index], ["grade"] = _clearModifierGrades[index], ["count"] = 1,
                });
            LocalTelemetry.RecordUsabilityEvent("clear_reward_offered", _settled ? "result" : "round_break", new Dictionary<string, object>
            {
                ["combat_id"] = _testCombatId, ["spell_build_id"] = _spellBuildId,
                ["stage"] = _simulation.StageNumber, ["round"] = _simulation.RoundNumber, ["choices"] = choices,
            });
        }

        /// <summary>라운드 또는 최종 클리어 후보 순번의 행동·Modifier 룬을 해당 등급으로 한 번 지급한다. 선택 불가 상태나 중복 선택이면 false를 반환한다.</summary>
        public bool ChooseClearModifier(int index)
        {
            if ((!_settled && !_simulation.IsWaitingForNextRound) || _clearModifierChoices == null
                || _isClearModifierChosen || index < 0 || index >= _clearModifierChoices.Count) return false;
            string grade = _clearModifierGrades[index];
            if (_simulation.IsProgressionMission && _settled && _session.PendingProgressionChoiceIds.Count > 0)
            {
                if (!_session.ClaimProgressionChoice(index)) return false;
            }
            else if (!_settled) _session.GrantRoundChoice(_clearModifierChoices[index], grade);
            else _session.GrantRune(_clearModifierChoices[index], grade, 1, "clear_choice");
            AddModifierReward(_clearModifierChoices[index], grade, 1);
            _isClearModifierChosen = true;
            LocalTelemetry.RecordUsabilityEvent("clear_reward_selected", _settled ? "result" : "round_break", new Dictionary<string, object>
            {
                ["combat_id"] = _testCombatId, ["spell_build_id"] = _spellBuildId,
                ["stage"] = _simulation.StageNumber, ["rune_id"] = _clearModifierChoices[index],
                ["round"] = _simulation.RoundNumber, ["choice_index"] = index,
                ["grade"] = grade, ["count"] = 1,
                ["inventory"] = _session.GetInventoryLogSnapshot(),
            });
            _lastResult += "\n" + GameData.L("result.modifierReward") + " "
                + ModifierGradeText.FormatName(GameData.Runes.Get(_clearModifierChoices[index]).Name, grade) + " +1";
            return true;
        }

        /// <summary>디버그 실행에서만 미션 플레이어 무적을 토글한다.</summary>
        public void DebugInvulnerable()
        {
            if (!_session.IsDebugEnabled) return;
            _simulation.SetDebugInvulnerable(!_simulation.DebugInvulnerable);
        }

        /// <summary>디버그 실행에서만 지정한 종류의 적을 미션에 생성한다. 엘리트 지정 시 엘리트 배율로 생성한다.</summary>
        public void DebugSpawn(string kind = "enemy.scout", bool isElite = false)
        {
            if (!_session.IsDebugEnabled) return;
            _simulation.DebugSpawn(kind, isElite);
        }

        /// <summary>디버그 라운드 대기 중 지정 스테이지·라운드의 첫 일반 웨이브로 진입해 Hexa 소환 시험 구간을 시작하고 성공 여부를 반환한다.</summary>
        public bool DebugStartNormalWave(int stage = 1, int round = 1)
        {
            if (!_session.IsDebugEnabled || _settled || _isPaused || !_simulation.IsWaitingForNextRound) return false;
            ResetRoundInput();
            int completedStage = _simulation.StageNumber;
            int completedRound = _simulation.RoundNumber;
            if (!_simulation.DebugStartNormalWave(stage, round)) return false;
            EndRoundBreak(completedStage, completedRound, "debug_normal_wave_clicked");
            return true;
        }

        /// <summary>디버그 실행에서만 현재 전투에 Hexa 두 기를 생성하고 실제로 생성된 수를 반환한다.</summary>
        public int DebugSpawnHexaPair()
        {
            if (!_session.IsDebugEnabled) return 0;
            int count = 0;
            if (_simulation.DebugSpawn(HexaDefinition.ID) != null) count++;
            if (_simulation.DebugSpawn(HexaDefinition.ID) != null) count++;
            return count;
        }

        /// <summary>디버그 실행에서만 지정한 개체 ID의 살아 있는 Hexa에 소환 모드를 시작하고 성공 여부를 반환한다.</summary>
        public bool DebugStartHexaSummon(int entityId)
        {
            return _session.IsDebugEnabled && _simulation.DebugStartHexaSummon(entityId);
        }

        /// <summary>디버그 실행에서만 지정한 개체 ID의 살아 있는 Hexa 한 기에 치명 피해를 적용하고 성공 여부를 반환한다.</summary>
        public bool DebugKillHexa(int entityId)
        {
            return _session.IsDebugEnabled && _simulation.DebugKillHexa(entityId);
        }

        /// <summary>새 노드 실행을 로컬 로그에 기록하고 Core 실행을 성공 시전 수에 더한다.</summary>
        private void CaptureNodeTelemetry()
        {
            var count = _simulation.NodeExecutionCount - _telemetryCount;
            var start = Math.Max(0, _simulation.NodeEvents.Count - count);
            for (var index = start; index < _simulation.NodeEvents.Count; index++)
            {
                var entry = _simulation.NodeEvents[index];
                LocalTelemetry.Record(entry.Tick, "mission.node", entry.NodeId);
                if (entry.NodeId == _coreNodeId)
                {
                    _castSuccessCount++;
                    LocalTelemetry.RecordUsabilityEvent("simulation_cast_result", "combat", new Dictionary<string, object>
                    {
                        ["result"] = "success",
                        ["node_id"] = entry.NodeId,
                    });
                }
            }
            _telemetryCount = _simulation.NodeExecutionCount;
        }
    }
}
