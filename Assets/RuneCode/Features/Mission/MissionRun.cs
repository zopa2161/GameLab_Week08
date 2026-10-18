using System;
using System.Collections.Generic;

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

        /// <summary>보스 클리어 Modifier 선택 보상을 첫 클리어가 아니라 매 클리어마다 지급한다는 임시 규칙이다. 미정 사항으로 상수 하나로 바꿀 수 있다.</summary>
        private const bool CLEAR_MODIFIER_CHOICE_EVERY_CLEAR = true;

        private readonly RuneCodeSession _session;
        private readonly CompiledSpell _spell;
        private readonly string _spellName;
        private readonly RuneSimulation _simulation;
        private readonly int _seed;
        private readonly string _spellBuildId;
        private readonly string _testCombatId;
        private readonly string _coreNodeId;

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
        private readonly List<SimulationItemDrop> _pickedModifiers = new List<SimulationItemDrop>();
        private readonly List<MissionReward> _rewards = new List<MissionReward>();
        private List<string> _clearModifierChoices;
        private List<string> _clearModifierGrades;
        private bool _isClearModifierChosen;

        /// <summary>현재 전투 시뮬레이션을 반환한다. 경기장 그래픽과 HUD가 읽는다.</summary>
        public RuneSimulation Simulation => _simulation;

        /// <summary>시전할 마법 이름을 반환한다.</summary>
        public string SpellName => _spellName;

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

        /// <summary>세션의 능력치와 선택 스테이지로 전투를 만들고 출격 그래프 스냅샷·전투 ID와 시작 텔레메트리를 기록한다.</summary>
        public MissionRun(RuneCodeSession session, CompiledSpell spell, string spellName)
        {
            _session = session;
            _spell = spell;
            _spellName = spellName;
            // 미션마다 시드를 바꿔 스폰·드롭·보상 순서가 매번 달라지게 한다. 재현용으로 시작 텔레메트리에 남긴다.
            int seed = Environment.TickCount;
            _seed = seed;
            _simulation = new RuneSimulation(seed, true, session.MaxHp, session.MaxEnergy, session.SelectedStage, session.EnergyRegen,
                session.DamageMultiplier, session.MoveSpeedMultiplier, session.ScrapGainMultiplier,
                carrier: CarrierSpawnSettings.LoadDefinition(), scrapPickupRangeMultiplier: session.ScrapPickupRangeMultiplier,
                killEnergyBonus: session.KillEnergyBonus);
            _simulation.SetLoadout(new[] { spell });
            _simulation.SetUnlockedElements(session.GetUnlockedElements());
            _coreNodeId = null;
            foreach (GraphNode node in session.Spells.Graph.Nodes)
            {
                if (node.RuneId != SpellGrammar.CORE_RUNE) continue;
                _coreNodeId = node.Id;
                break;
            }
            _spellBuildId = LocalTelemetry.SaveUsabilityBuild(null, session.Spells.Graph, new Dictionary<string, object>
            {
                ["reason"] = "mission_start",
                ["signature"] = spell.Signature,
                ["comparison_key"] = LocalTelemetry.GetUsabilityGraphSignature(session.Spells.Graph),
                ["stage"] = session.SelectedStage,
                ["ram_used"] = spell.RamUsed,
                ["is_compile_valid"] = true,
            });
            _testCombatId = LocalTelemetry.BeginUsabilityCombat(session.SelectedStage, _spellBuildId, seed);
            LocalTelemetry.RecordUsabilityEvent("stage_enter", "combat", new Dictionary<string, object>
            {
                ["stage"] = session.SelectedStage,
                ["seed"] = seed,
                ["inventory"] = session.GetInventoryLogSnapshot(),
            });
            LocalTelemetry.Record(0, "mission.start", session.SelectedStage + ":" + spell.Signature + ":seed=" + seed);
        }

        /// <summary>미션의 고정 시뮬레이션 갱신을 일시정지하거나 재개한다.</summary>
        public void TogglePause()
        {
            _isPaused = !_isPaused;
            _simulation.CancelBeamInput();
        }

        /// <summary>
        /// 이동·대시·줍기 키, 포인터 조준과 시전 입력(isCasting, 좌클릭 유지)을 모아 MaxFrameSteps 제한 안의 고정 스텝만큼 시뮬레이션을 갱신하고,
        /// 입력·노드 텔레메트리를 기록하고 종료 조건을 확인한다.
        /// </summary>
        public void Step(bool isTyping, bool hasPointer, SimVector pointer, bool isCasting, float unscaledDeltaTime,
            bool castPressed = false, bool castReleased = false)
        {
            if (_settled) return;
            if (_isPaused)
            {
                _accumulator = 0; _hasPendingDash = false;
                _hasPendingCastPress = false; _hasPendingCastRelease = false;
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
            GrantPickedItems();
            if (_simulation.Completed || _simulation.IsFailed) FinishMission();
        }

        /// <summary>새 소지 아이템 드롭을 기록하고 일반 모드에서만 지급·결과 목록에 반영한다.</summary>
        private void GrantPickedItems()
        {
            foreach (SimulationItemDrop drop in _simulation.TakeNewPickedItems())
            {
                LocalTelemetry.RecordUsabilityEvent("combat_item_pickup", "combat", new Dictionary<string, object>
                {
                    ["rune_id"] = drop.RuneId,
                    ["grade"] = drop.Grade,
                    ["count"] = drop.Count,
                    ["inventory_grant_enabled"] = !_session.IsTestMode,
                });
                if (_session.IsTestMode) continue;
                _session.GrantRune(drop.RuneId, drop.Grade, drop.Count, "combat_pickup");
                _pickedModifiers.Add(drop);
                AddModifierReward(drop.RuneId, drop.Grade, drop.Count);
            }
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

        /// <summary>이번 미션 정산으로 획득한 조각을 보상 표시 목록에 기록한다.</summary>
        private void AddFragmentReward(int count)
        {
            if (count > 0) _rewards.Add(new MissionReward(false, null, null, count));
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

        /// <summary>전투를 한 번만 정산하고 성공 시 반복 클리어에도 보장 룬 1개를 추첨해 결과·획득 기록에 넣는다. 보스 선택 보상은 별도로 유지한다.</summary>
        public void FinishMission()
        {
            if (_settled) return;
            _settled = true;
            var cleared = _simulation.Completed;
            // 첫 클리어만 지급하는 규칙으로 바꿀 때를 대비해 정산 기록 전에 판정한다.
            bool isFirstClear = _session.HighestClearedStage < _simulation.StageNumber;
            var fragments = _simulation.SettlementFragments;
            ModifierRewardDefinition clearRuneReward = null;
            if (cleared && !_session.IsTestMode)
            {
                DropRoll roll = _simulation.DrawClearRuneReward();
                clearRuneReward = new ModifierRewardDefinition(roll.RuneId, roll.Grade, roll.Count);
            }
            var modifierRewards = _session.SettleMission(_simulation.StageNumber, fragments, cleared, _simulation.KillCounts, clearRuneReward);
            if (!_session.IsTestMode) AddFragmentReward(fragments);
            foreach (ModifierRewardDefinition reward in modifierRewards)
            {
                // 속성 보상은 등급이 없으므로 그대로 두고, 모디 보상만 등급이 비면 최저 등급으로 채운다.
                string grade = reward.Grade;
                if (string.IsNullOrEmpty(grade) && GameData.Runes.TryGet(reward.RuneId, out RuneDefinition rune)
                    && rune.Category == SpellGrammar.CATEGORY_MODIFIER)
                    grade = GameData.ModifierGrades.GetLowestAvailableGrade(reward.RuneId);
                AddModifierReward(reward.RuneId, grade, reward.Count);
            }
            var result = cleared ? "clear" : _simulation.IsTimedOut ? "timeout" : "death";
            var resultKey = cleared ? "result.stageCleared" : _simulation.IsTimedOut ? "result.timeout" : "result.dead";
            _lastResult = GameData.L(resultKey);
            if (!_session.IsTestMode) _lastResult += "\n" + GameData.L("result.fragments") + " " + fragments;
            if (cleared) _lastResult += "  ·  " + GameData.L("result.clearReward") + " " + _simulation.ClearReward;
            foreach (var reward in modifierRewards)
            {
                string grade = reward.Grade;
                if (string.IsNullOrEmpty(grade) && GameData.Runes.TryGet(reward.RuneId, out RuneDefinition rune)
                    && rune.Category == SpellGrammar.CATEGORY_MODIFIER)
                    grade = GameData.ModifierGrades.GetLowestAvailableGrade(reward.RuneId);
                _lastResult += "\n" + GameData.L("result.modifierReward") + " "
                    + ModifierGradeText.FormatName(GameData.Runes.Get(reward.RuneId).Name, grade) + " +" + reward.Count;
            }
            AppendPickedModifiers();
            SetClearModifierChoices(cleared, isFirstClear);
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
        }

        /// <summary>보스 클리어 Modifier 선택 후보와 후보별 등급을 결정적 난수로 뽑아 결과에 담는다. 현재는 매 클리어 지급이며 상수로 첫 클리어만 지급하도록 바꿀 수 있다.</summary>
        private void SetClearModifierChoices(bool cleared, bool isFirstClear)
        {
            _clearModifierChoices = null;
            _clearModifierGrades = null;
            _isClearModifierChosen = false;
            if (_session.IsTestMode || !cleared || (!CLEAR_MODIFIER_CHOICE_EVERY_CLEAR && !isFirstClear)) return;
            if (!_simulation.TryDrawClearModifierChoices(out _, out List<string> candidates, out List<string> grades)) return;
            _clearModifierChoices = candidates;
            _clearModifierGrades = grades;
            var choices = new List<Dictionary<string, object>>();
            for (int index = 0; index < candidates.Count; index++)
                choices.Add(new Dictionary<string, object>
                {
                    ["rune_id"] = candidates[index], ["grade"] = grades[index], ["count"] = 1,
                });
            LocalTelemetry.RecordUsabilityEvent("clear_reward_offered", "result", new Dictionary<string, object>
            {
                ["combat_id"] = _testCombatId, ["spell_build_id"] = _spellBuildId,
                ["stage"] = _simulation.StageNumber, ["choices"] = choices,
            });
            _lastResult += "\n" + GameData.L("result.clearModifierChoice");
        }

        /// <summary>보스 클리어 Modifier 선택 후보 중 하나를 즉시 지급하고 선택을 확정한다. 이미 선택했거나 후보가 없으면 false를 반환한다.</summary>
        public bool ChooseClearModifier(int index)
        {
            if (_clearModifierChoices == null || _isClearModifierChosen || index < 0 || index >= _clearModifierChoices.Count) return false;
            string grade = _clearModifierGrades[index];
            _session.GrantModifier(_clearModifierChoices[index], grade, 1, "clear_choice");
            AddModifierReward(_clearModifierChoices[index], grade, 1);
            _isClearModifierChosen = true;
            LocalTelemetry.RecordUsabilityEvent("clear_reward_selected", "result", new Dictionary<string, object>
            {
                ["combat_id"] = _testCombatId, ["spell_build_id"] = _spellBuildId,
                ["stage"] = _simulation.StageNumber, ["rune_id"] = _clearModifierChoices[index],
                ["grade"] = grade, ["count"] = 1,
                ["inventory"] = _session.GetInventoryLogSnapshot(),
            });
            _lastResult += "\n" + GameData.L("result.modifierReward") + " "
                + ModifierGradeText.FormatName(GameData.Runes.Get(_clearModifierChoices[index]).Name, grade) + " +1";
            return true;
        }

        /// <summary>전투를 중단해 현재 조각을 정산하고 결과 문구와 중단 전투 로그를 남긴다.</summary>
        public void AbandonMission()
        {
            if (_settled) return;
            _settled = true;
            _simulation.CancelPendingSpellExecutions();
            var fragments = (int)Math.Round(_simulation.EarnedFragments * GameData.Balance.Economy.DeathRetention, MidpointRounding.AwayFromZero);
            _session.SettleMission(_simulation.StageNumber, fragments, false, _simulation.KillCounts);
            if (!_session.IsTestMode) AddFragmentReward(fragments);
            _lastResult = GameData.L("result.retreat");
            if (!_session.IsTestMode) _lastResult += "\n" + GameData.L("result.fragments") + " " + fragments;
            AppendPickedModifiers();
            _isPaused = false;
            LocalTelemetry.EndUsabilityCombat(new Dictionary<string, object>
            {
                ["stage"] = _simulation.StageNumber,
                ["spell_build_id"] = _spellBuildId,
                ["result"] = "retreat",
                ["skill_tree"] = _session.GetSkillTreeLogSnapshot(),
                ["inventory"] = _session.GetInventoryLogSnapshot(),
                ["module_rewards"] = GetModuleRewardsLogSnapshot(),
                ["duration_ms"] = (long)(_simulation.Time * 1000),
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
