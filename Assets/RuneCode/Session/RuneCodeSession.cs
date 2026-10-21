using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 씬 전환과 무관하게 유지되는 앱 상태(세이브, 성장, 설정, 상태 문구, 마법 편집 세션)를 소유한다.
    /// Boot 씬의 RuneCodeApp이 생성하고 각 화면 컴포넌트의 Initialize로 전달한다.
    /// </summary>
    public sealed class RuneCodeSession
    {
        private const int DEBUG_MODIFIER_STOCK = 9;
        private const int TEST_CLEARED_STAGE = 10;
        private const int TEST_STAGE = 9;
        private const int TEST_STOCK_COUNT = 2;
        private const string PLAY_LOG_PARTICIPANT_KEY = "RuneCode.PlayLogParticipantId";

        private PlayerSave _save;
        private PlayerSave _normalSave;
        private readonly StageCatalog _stages;
        private readonly ModifierGradeDropSource _synthesisGrades;
        private readonly UpgradeTreeDefinition _upgradeTree;
        private bool _isDebugEnabled;
        private bool _isTestMode;
        private readonly SpellEditSession _spells;
        private string _statusMessage;
        private long _statusRevision;
        private bool _isSaveDirty;
        private bool _hasStartedGame;
        private bool _isRunSuspended;

        private string _dockScenario = "dummy_single";
        private int _dockDummyCount = 1;
        private bool _dockAutoFire;
        private bool _dockAdaptation;
        private float _dockSpeed = 1;

        public PlayerSave Save => _save;
        public ISpellEditor Spells => _spells;
        public bool IsDebugEnabled => _isDebugEnabled;

        /// <summary>타이틀의 테스트 모드로 시작한 메모리 진행인지 반환한다. 디버그 권한은 주지 않는다.</summary>
        public bool IsTestMode => _isTestMode;

        /// <summary>디버그·테스트처럼 저장 슬롯에 쓰지 않는 메모리 진행인지 반환한다.</summary>
        public bool IsMemorySession => _isDebugEnabled || _isTestMode;
        public bool IsSaveDirty => _isSaveDirty;
        public bool ScreenShake => SaveStore.ScreenShake;
        public bool HitStop => SaveStore.HitStop;
        public string StatusMessage => _statusMessage;
        /// <summary>SetStatus 또는 SetStatusText가 호출될 때마다 증가하는 상태 메시지 revision이다.</summary>
        public long StatusRevision => _statusRevision;

        /// <summary>라운드 대기 중 작업실을 열어 진행 중인 런을 보관하고 있는지 반환한다.</summary>
        public bool IsRunSuspended => _isRunSuspended;

        public string DockScenario => _dockScenario;
        public int DockDummyCount => _dockDummyCount;
        public bool DockAutoFire => _dockAutoFire;
        public bool DockAdaptation => _dockAdaptation;
        public float DockSpeed => _dockSpeed;

        public int Capacity => GameData.Balance.Economy.BaseCapacity + Mathf.RoundToInt(GetUpgradeTreeEffectTotal(UpgradeEffectType.RamCapacity));
        public float MaxEnergy => GameData.Balance.Player.MaxEnergy + GetUpgradeTreeEffectTotal(UpgradeEffectType.MaxEnergy);
        public float MaxHp => GameData.Balance.Player.MaxHp + GetUpgradeTreeEffectTotal(UpgradeEffectType.MaxHp);
        public float EnergyRegen => GameData.Balance.Player.EnergyRegen + GetUpgradeTreeEffectTotal(UpgradeEffectType.EnergyRegen);
        public float DamageMultiplier => 1 + GetUpgradeTreeEffectTotal(UpgradeEffectType.Damage);
        public float MoveSpeedMultiplier => 1 + GetUpgradeTreeEffectTotal(UpgradeEffectType.MoveSpeed);
        public float ScrapGainMultiplier => 1 + GetUpgradeTreeEffectTotal(UpgradeEffectType.ScrapGain);
        public float ScrapPickupRangeMultiplier => 1 + GetUpgradeTreeEffectTotal(UpgradeEffectType.ScrapPickupRange);
        public float ScrapHealRatio => GetUpgradeTreeEffectTotal(UpgradeEffectType.ScrapHeal);
        public float KillEnergyBonus => GetUpgradeTreeEffectTotal(UpgradeEffectType.KillEnergy);
        public float BattleDuration => (float)_stages.Get(1).Stream.RampSeconds;
        public int EquippedRam => CalculateEquippedRam(_spells.Graph);
        public int SelectedStage => _isTestMode ? TEST_STAGE : _save.SelectedStage;
        public StageDefinition SelectedStageDefinition => _stages.Get(SelectedStage);
        public int HighestClearedStage => _save.HighestClearedStage;
        public bool HasClearedProgression => _save.HasClearedProgression;
        public IReadOnlyList<string> PendingProgressionChoiceIds => _save.PendingProgressionChoiceIds;
        public IReadOnlyList<string> PendingProgressionChoiceGrades => _save.PendingProgressionChoiceGrades;

        /// <summary>도움말에서 열람할 수 있는 항목 수다. 자동 개방 조건이 없으므로 항상 전체를 연다.</summary>
        public int UnlockedHelpCount => int.MaxValue;

        /// <summary>화면 전환 요청을 받는 콜백이다. RuneCodeApp만 등록한다.</summary>
        public event Action<AppScreen> ScreenRequested;

        /// <summary>
        /// 세이브, 스테이지 정의(제한시간 성장 표시 기준값), 디버그 여부와 시작 경고 문구로 세션과 마법 편집 세션을 만든다.
        /// </summary>
        public RuneCodeSession(PlayerSave save, StageCatalog stages, UpgradeTreeDefinition upgradeTree, bool isDebugEnabled,
            string initialStatus)
        {
            _normalSave = save;
            SaveStore.InitializeFeedbackSettings(save);
            _save = isDebugEnabled ? PlayerSave.CreateNew() : save;
            _stages = stages;
            _synthesisGrades = new ModifierGradeDropSource(GameData.ModifierGrades);
            _upgradeTree = upgradeTree;
            _isDebugEnabled = isDebugEnabled;
            _statusMessage = initialStatus;
            bool restoredRuneUnlocks = !isDebugEnabled && RestorePurchasedRuneUnlocks(_save);
            if (isDebugEnabled) UnlockDebugRunes();
            var storage = new SessionSpellStorage(() => _save, PersistSpellStorage);
            var policy = new SessionSpellPolicy(this);
            _spells = new SpellEditSession(storage, policy);
            PersistRestoredRuneUnlocks(restoredRuneUnlocks);
        }

        /// <summary>등록된 전환 콜백으로 지정 화면 전환을 요청한다.</summary>
        public void RequestScreen(AppScreen screen) { ScreenRequested?.Invoke(screen); }

        /// <summary>진행 중인 런을 보관 상태로 표시하고 작업실 화면 전환을 요청한다. 미션 화면은 이 표시를 보고 런을 버리지 않는다.</summary>
        public void SuspendRunForWorkshop()
        {
            _isRunSuspended = true;
            RequestScreen(AppScreen.Workshop);
        }

        /// <summary>보관 중인 런을 이어 가기 위해 보관 표시를 해제한다.</summary>
        public void ResumeSuspendedRun()
        {
            _isRunSuspended = false;
        }

        /// <summary>기존 저장 정책으로 편집 내용을 반영하고 로그 회차를 마감한 뒤 보관 중인 런을 버리고 타이틀 화면으로 전환한다.</summary>
        public void ReturnToTitle()
        {
            SaveAll();
            LocalTelemetry.EndUsabilityTest("return_to_title", _spells.Graph);
            _hasStartedGame = false;
            _isRunSuspended = false;
            RequestScreen(AppScreen.Title);
        }

        /// <summary>
        /// 일반 저장 또는 메모리 디버그 진행을 열고, 빌드의 일반 시작은 로그 회차도 자동 생성한다.
        /// 출격 요청을 기록한 뒤 전투 화면으로 바로 전환하며, 출격 준비에 실패하면 미션 화면이 작업실로 보낸다.
        /// </summary>
        public void StartGame(bool isDebug)
        {
            _hasStartedGame = true;
            _isRunSuspended = false;
            if (LocalTelemetry.IsUsabilityTestActive) LocalTelemetry.EndUsabilityTest("mode_changed", _spells.Graph);
            bool isFirstGameStart = !isDebug && !SaveStore.HasSlot(SaveStore.ActiveSlot);
            _isDebugEnabled = isDebug;
            _isTestMode = false;
            _save = isDebug ? PlayerSave.CreateNew() : _normalSave;
            bool restoredRuneUnlocks = !isDebug && RestorePurchasedRuneUnlocks(_save);
            if (isFirstGameStart)
            {
                if (!SaveStore.WriteToSlot(SaveStore.ActiveSlot, _save)) SetStatusText(SaveStore.LastWarning);
                else _isSaveDirty = false;
            }
            if (isDebug) UnlockDebugRunes();
            else PersistRestoredRuneUnlocks(restoredRuneUnlocks);
            _spells.Reload();
            if (!isDebug && !Application.isEditor) BeginNormalPlayLog();
            LocalTelemetry.RecordUsabilityEvent("simulation_request", "title", new Dictionary<string, object>
            {
                ["input_method"] = "title_start",
                ["stage"] = SelectedStage,
            });
            RequestScreen(AppScreen.Mission);
        }

        /// <summary>기기에 저장한 임의 참가자 ID로 일반 플레이 로그를 시작하고 실패해도 게임 진행은 유지한다.</summary>
        private void BeginNormalPlayLog()
        {
            try
            {
                string participantId = PlayerPrefs.GetString(PLAY_LOG_PARTICIPANT_KEY, string.Empty);
                if (!Guid.TryParse(participantId, out Guid parsedId))
                {
                    participantId = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(PLAY_LOG_PARTICIPANT_KEY, participantId);
                    PlayerPrefs.Save();
                }
                else participantId = parsedId.ToString("N");

                var startState = new Dictionary<string, object>
                {
                    ["play_mode"] = "normal",
                    ["selected_stage"] = SelectedStage,
                    ["save_slot"] = SaveStore.ActiveSlot,
                    ["ram_capacity"] = Capacity,
                    ["inventory"] = GetInventoryLogSnapshot(),
                };
                var metadata = new Dictionary<string, object>
                {
                    ["participant_id"] = "player-" + participantId,
                    ["app_version"] = Application.version,
                    ["condition"] = "Develop",
                    ["editor_model"] = "combined_nodes",
                    ["test_order"] = null,
                    ["is_test_mode"] = false,
                    ["is_debug"] = false,
                    ["is_practice"] = false,
                    ["play_mode"] = "normal",
                    ["starting_state"] = startState,
                };
                LocalTelemetry.BeginUsabilityTest(metadata, null);
                LocalTelemetry.RecordUsabilityEvent("test_start", "session", startState);
                LocalTelemetry.SaveUsabilityBuild(null, _spells.Graph, new Dictionary<string, object>
                {
                    ["reason"] = "normal_play_start",
                    ["signature"] = GraphCompiler.Signature(_spells.Graph),
                    ["comparison_key"] = LocalTelemetry.GetUsabilityGraphSignature(_spells.Graph),
                    ["is_compile_valid"] = _spells.CompileResult.Ok,
                });
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is PlayerPrefsException)
            {
                LocalTelemetry.AbortUsabilityTest();
                Debug.LogWarning("일반 플레이 로그 기록을 시작하지 못했습니다: " + error.GetType().Name);
            }
        }

        /// <summary>익명 참가자 ID로 테스트 프로필과 로컬 로그 세션을 열고 시작에 성공하면 true를 반환한다.</summary>
        public bool StartTestGame(string participantId)
        {
            if (string.IsNullOrWhiteSpace(participantId)) return false;
            if (LocalTelemetry.IsUsabilityTestActive) LocalTelemetry.EndUsabilityTest("restarted", _spells.Graph);
            _isDebugEnabled = false;
            _isTestMode = true;
            _save = CreateTestSave();
            _spells.Reload();

            var stock = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> pair in _save.CreateStockMap()) stock[pair.Key] = pair.Value;
            var upgrades = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.Nodes)
                if (node != null) upgrades[node.Id] = GetUpgradeNodeLevel(node.Id);
            var startState = new Dictionary<string, object>
            {
                ["selected_stage"] = SelectedStage,
                ["currency"] = _save.Currency,
                ["ram_capacity"] = Capacity,
                ["equipped_ram"] = EquippedRam,
                ["max_energy"] = MaxEnergy,
                ["max_hp"] = MaxHp,
                ["unlocked_runes"] = new List<string>(_save.UnlockedRunes),
                ["stock"] = stock,
                ["upgrade_levels"] = upgrades,
                ["inventory"] = GetInventoryLogSnapshot(),
            };
            var metadata = new Dictionary<string, object>
            {
                ["participant_id"] = participantId,
                ["app_version"] = Application.version,
                ["condition"] = "Develop",
                ["editor_model"] = "combined_nodes",
                ["test_order"] = null,
                ["is_test_mode"] = true,
                ["is_debug"] = false,
                ["is_practice"] = false,
                ["starting_state"] = startState,
            };
            try
            {
                LocalTelemetry.BeginUsabilityTest(metadata, null);
                LocalTelemetry.RecordUsabilityEvent("test_start", "title", startState);
                LocalTelemetry.SaveUsabilityBuild(null, _spells.Graph, new Dictionary<string, object>
                {
                    ["reason"] = "test_start",
                    ["signature"] = GraphCompiler.Signature(_spells.Graph),
                    ["comparison_key"] = LocalTelemetry.GetUsabilityGraphSignature(_spells.Graph),
                    ["is_compile_valid"] = _spells.CompileResult.Ok,
                    ["ram_used"] = _spells.CompileResult.Spell?.RamUsed ?? 0,
                });
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                LocalTelemetry.AbortUsabilityTest();
                _isTestMode = false;
                _save = _normalSave;
                _spells.Reload();
                Debug.LogException(exception);
                return false;
            }
            _hasStartedGame = true;
            _isRunSuspended = false;
            RequestScreen(AppScreen.Workshop);
            return true;
        }

        /// <summary>타이틀에서 선택한 저장 슬롯을 읽어 일반 진행으로 설정하고 성공 여부를 반환한다.</summary>
        public bool SelectSaveSlot(int slot)
        {
            if (!SaveStore.TryLoadSlot(slot, out PlayerSave loaded)) return false;
            SaveStore.SetActiveSlot(slot);
            _normalSave = loaded;
            if (!IsMemorySession)
            {
                _save = loaded;
                _isSaveDirty = false;
                _isRunSuspended = false;
                bool restoredRuneUnlocks = RestorePurchasedRuneUnlocks(_save);
                _spells.Reload();
                PersistRestoredRuneUnlocks(restoredRuneUnlocks);
            }
            return true;
        }

        /// <summary>빈 타이틀 슬롯에 새 진행을 만들고 현재 일반 세션에도 적용한다.</summary>
        public bool CreateSaveSlot(int slot)
        {
            if (!SaveStore.CreateSlot(slot, out PlayerSave created)) return false;
            _normalSave = created;
            if (!IsMemorySession)
            {
                _save = created;
                _isSaveDirty = false;
                _isRunSuspended = false;
                _spells.Reload();
            }
            return true;
        }

        /// <summary>지정 저장 슬롯을 삭제하고 활성 슬롯이면 새 시작 진행으로 바꾼다.</summary>
        public bool DeleteSaveSlot(int slot)
        {
            if (!SaveStore.DeleteSlot(slot)) return false;
            if (SaveStore.ActiveSlot == slot)
            {
                _normalSave = PlayerSave.CreateNew();
                if (!IsMemorySession)
                {
                    _save = _normalSave;
                    _isSaveDirty = false;
                    _isRunSuspended = false;
                    _spells.Reload();
                }
            }
            return true;
        }

        /// <summary>현재 진행과 편집 중인 마법을 즉시 저장하고 성공 여부를 반환한다.</summary>
        public bool SaveManually()
        {
            if (IsMemorySession) return false;
            _spells.Save();
            if (!_isSaveDirty) return true;
            _isSaveDirty = true;
            return WriteCurrentSave(_save);
        }

        /// <summary>현재 슬롯의 저장 데이터를 다시 읽어 미저장 진행과 편집 사본을 교체하고 보관 중인 런을 버린다.</summary>
        public bool LoadActiveSave()
        {
            if (IsMemorySession || !SaveStore.TryLoadSlot(SaveStore.ActiveSlot, out PlayerSave loaded)) return false;
            _save = loaded;
            _normalSave = loaded;
            _isSaveDirty = false;
            _isRunSuspended = false;
            bool restoredRuneUnlocks = RestorePurchasedRuneUnlocks(_save);
            _spells.Reload();
            PersistRestoredRuneUnlocks(restoredRuneUnlocks);
            return true;
        }

        /// <summary>문자열 키를 번역해 상태 문구로 저장한다.</summary>
        public void SetStatus(string key)
        {
            _statusMessage = GameData.L(key);
            _statusRevision++;
        }

        /// <summary>이미 번역된 문구를 상태 문구로 저장한다.</summary>
        public void SetStatusText(string text)
        {
            _statusMessage = text;
            _statusRevision++;
        }

        /// <summary>시험 더미 수를 밸런스의 일렬 배치 상한 내에 저장해 작업실 재진입 후에도 유지한다.</summary>
        public void SetDockDummyCount(int count)
        {
            _dockDummyCount = Math.Max(1, Math.Min(GameData.Balance.Sim.BenchLineCount, count));
        }

        /// <summary>시험 도크의 시나리오와 자동 시전·적응·배속을 세션에 저장해 작업실 재진입 후에도 유지한다.</summary>
        public void SetDockOptions(string scenario, bool autoFire, bool adaptation, float speed)
        {
            _dockScenario = scenario;
            _dockAutoFire = autoFire;
            _dockAdaptation = adaptation;
            _dockSpeed = Math.Max(0.5f, Math.Min(2, speed));
        }

        /// <summary>노이즈 후보로 사용할 해금 속성 태그와 raw를 반환한다.</summary>
        public IReadOnlyList<string> GetUnlockedElements()
        {
            var elements = new List<string> { "raw" };
            foreach (var id in _save.UnlockedRunes) if (id.StartsWith("elem.", StringComparison.Ordinal)) elements.Add(id.Substring(5));
            // 문법 v3 속성은 element.* 룬으로 해금하므로 해금된 속성의 시뮬레이션 태그도 넣는다.
            foreach (ElementDefinition element in GameData.Runes.Elements)
                if (!string.IsNullOrEmpty(element.RuntimeTag) && _save.UnlockedRunes.Contains(element.RuneId) && !elements.Contains(element.RuntimeTag))
                    elements.Add(element.RuntimeTag);
            return elements;
        }

        /// <summary>SO의 시작 레벨과 저장된 구매 단계 중 높은 값을 최대 레벨 이내로 반환한다. 이미 얻은 룬은 완료로 처리한다.</summary>
        public int GetUpgradeNodeLevel(string nodeId)
        {
            UpgradeTreeNodeDefinition node = _upgradeTree.FindNode(nodeId);
            if (node == null) return 0;
            if (node.EffectType == UpgradeEffectType.RuneUnlock && _save.UnlockedRunes.Contains(node.RuneId)) return node.MaxLevel;
            return Math.Min(Math.Max(node.InitialLevel, _save.GetUpgradeNodeLevel(nodeId)), node.MaxLevel);
        }

        /// <summary>노드의 다음 강화 비용을 반환하고 최대 레벨이거나 등록되지 않은 노드는 -1을 반환한다.</summary>
        public int GetUpgradeNodeCost(string nodeId)
        {
            UpgradeTreeNodeDefinition node = _upgradeTree.FindNode(nodeId);
            if (node == null) return -1;
            int cost = node.GetNextCost(GetUpgradeNodeLevel(nodeId));
            return _isDebugEnabled && cost >= 0 ? 0 : cost;
        }

        /// <summary>스테이지, 선행 노드(하나 이상 만족), 배타 세트, 재화를 확인해 구매 불가 사유를 반환한다.</summary>
        public bool CanPurchaseUpgradeNode(string nodeId, out string reasonKey)
        {
            reasonKey = null;
            UpgradeTreeNodeDefinition node = _upgradeTree.FindNode(nodeId);
            if (node == null || node.MaxLevel == 0)
            {
                reasonKey = "tree.invalidNode";
                return false;
            }
            int currentLevel = GetUpgradeNodeLevel(nodeId);
            if (currentLevel >= node.MaxLevel)
            {
                reasonKey = "tree.maxed";
                return false;
            }
            if (node.EffectType == UpgradeEffectType.RuneUnlock && !GameData.Runes.TryGet(node.RuneId, out _))
            {
                reasonKey = "tree.invalidRune";
                return false;
            }
            if (!_isDebugEnabled && node.RequiredStage > _save.HighestClearedStage + 1)
            {
                reasonKey = "tree.stageLocked";
                return false;
            }
            bool isPrerequisiteMet = node.Prerequisites.Count == 0;
            foreach (UpgradeTreePrerequisite prerequisite in node.Prerequisites)
                if (GetUpgradeNodeLevel(prerequisite.NodeId) >= prerequisite.RequiredLevel)
                {
                    isPrerequisiteMet = true;
                    break;
                }
            if (!isPrerequisiteMet)
            {
                reasonKey = "tree.prerequisiteLocked";
                return false;
            }
            if (IsExclusiveSiblingPurchased(node))
            {
                reasonKey = "tree.exclusiveLocked";
                return false;
            }
            int cost = GetUpgradeNodeCost(nodeId);
            if (cost < 0 || (!_isDebugEnabled && _save.Currency < cost))
            {
                reasonKey = "tree.insufficient";
                return false;
            }
            return true;
        }

        /// <summary>같은 배타 세트에 속한 다른 노드 중 이미 레벨 1 이상을 보유한 노드가 있는지 반환한다.</summary>
        private bool IsExclusiveSiblingPurchased(UpgradeTreeNodeDefinition node)
        {
            if (string.IsNullOrWhiteSpace(node.ExclusiveGroupId)) return false;
            foreach (UpgradeTreeNodeDefinition other in _upgradeTree.Nodes)
                if (other != null && other != node && other.ExclusiveGroupId == node.ExclusiveGroupId && GetUpgradeNodeLevel(other.Id) >= 1)
                    return true;
            return false;
        }

        /// <summary>보유 모듈의 종류·등급·총수량, 해금 목록과 재화를 복사해 로그용 스냅샷을 반환한다.</summary>
        public Dictionary<string, object> GetInventoryLogSnapshot()
        {
            var owned = new List<Dictionary<string, object>>();
            foreach (KeyValuePair<string, int> stock in _save.CreateStockMap())
            {
                if (stock.Value <= 0 || !SpellGrammar.TryParseStockKey(stock.Key, out string runeId, out string grade)) continue;
                string category = GameData.Runes.TryGet(runeId, out RuneDefinition rune) ? rune.Category : "unknown";
                owned.Add(new Dictionary<string, object>
                {
                    ["rune_id"] = runeId, ["category"] = category,
                    ["grade"] = string.IsNullOrEmpty(grade) ? null : grade, ["count"] = stock.Value,
                });
            }
            return new Dictionary<string, object>
            {
                ["owned_modules"] = owned,
                ["unlocked_runes"] = new List<string>(_save.UnlockedRunes),
                ["currency"] = _save.Currency,
            };
        }

        /// <summary>현재 스킬트리에 등록된 노드 레벨과 적용된 주요 능력치를 복사해 전투 결과 로그용 스냅샷을 반환한다.</summary>
        public Dictionary<string, object> GetSkillTreeLogSnapshot()
        {
            var levels = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.Nodes)
                if (node != null) levels[node.Id] = GetUpgradeNodeLevel(node.Id);
            return new Dictionary<string, object>
            {
                ["node_levels"] = levels,
                ["ram_capacity"] = Capacity,
                ["max_hp"] = MaxHp,
                ["max_energy"] = MaxEnergy,
                ["energy_regen"] = EnergyRegen,
                ["damage_multiplier"] = DamageMultiplier,
                ["move_speed_multiplier"] = MoveSpeedMultiplier,
            };
        }

        /// <summary>선행 조건을 만족하는 트리 노드를 구매하고 효과·룬 해금·저장을 반영한다.</summary>
        public bool BuyUpgradeNode(string nodeId)
        {
            if (!CanPurchaseUpgradeNode(nodeId, out string reasonKey))
            {
                SetStatus(reasonKey);
                return false;
            }
            UpgradeTreeNodeDefinition node = _upgradeTree.FindNode(nodeId);
            int currentLevel = GetUpgradeNodeLevel(nodeId);
            int cost = GetUpgradeNodeCost(nodeId);
            if (!_save.Spend(cost))
            {
                SetStatus("tree.insufficient");
                return false;
            }
            _save.SetUpgradeNodeLevel(nodeId, currentLevel + 1);
            if (node.EffectType == UpgradeEffectType.RuneUnlock) _save.Unlock(node.RuneId);
            PersistProgress();
            _spells.Recompile();
            LocalTelemetry.Record(0, "upgradeTree.purchase", nodeId + ":" + (currentLevel + 1) + ":" + cost);
            return true;
        }

        /// <summary>출격 스테이지를 선택하고 저장한다. 디버그 모드에서는 클리어에 따른 선택 제한을 무시한다.</summary>
        public void SelectStage(int stage)
        {
            if (_isTestMode && stage != TEST_STAGE) return;
            if (stage < 1 || (!_isDebugEnabled && stage > HighestClearedStage + 1)) return;
            _save.SelectStage(stage, _isDebugEnabled);
            PersistProgress();
        }

        /// <summary>현재 마법을 컴파일 규칙과 RAM 규칙으로 검증해 지정 슬롯에 장착한다.</summary>
        public void Equip(int slot)
        {
            if (slot != 0) return;
            _spells.Recompile();
            if (!_spells.CompileResult.Ok) { SetStatus("editor.invalidEquip"); return; }
            var editingId = _spells.Graph.Id;
            var ram = 0;
            for (var i = 0; i < _save.SlotCount; i++)
                ram += i == slot ? _spells.CompileResult.Spell.RamUsed : GetGraphRam(_save.Loadout[i] == editingId ? _spells.Graph : _save.FindGraph(_save.Loadout[i]));
            if (GameData.Balance.Ram.Mode == "shared" && ram > Capacity) { SetStatus("editor.ramBlocked"); return; }
            _spells.Save();
            _save.SetLoadout(slot, editingId);
            PersistProgress();
        }

        /// <summary>screenShake 또는 hitStop 설정을 기기 공통 환경설정에 저장한다.</summary>
        public void SetSetting(string key, bool enabled)
        {
            SaveStore.SetFeedbackSetting(key, enabled);
        }

        /// <summary>진행을 초기화하고 새 시작 마법으로 편집 세션을 다시 연다. 테스트 모드는 테스트 프로필로 다시 만든다.</summary>
        public void ResetSave()
        {
            RenewSave();
            PersistProgress();
        }

        /// <summary>런 종료로 진행을 전부 새로 만들고 편집 세션을 다시 연다. 일반 세션은 즉시 활성 슬롯에 기록한다.</summary>
        public void EndRun()
        {
            RenewSave();
            if (IsMemorySession) return;
            _isSaveDirty = true;
            if (!WriteCurrentSave(_save)) SetStatusText(SaveStore.LastWarning);
        }

        /// <summary>현재 모드에 맞는 새 진행으로 바꾸고 보관 중인 런을 버린 뒤 편집 세션을 다시 연다. 일반 진행은 _normalSave도 함께 바꾸고 디버그면 룬을 전부 해금한다.</summary>
        private void RenewSave()
        {
            _save = _isTestMode ? CreateTestSave() : PlayerSave.CreateNew();
            if (_isDebugEnabled) UnlockDebugRunes();
            else if (!_isTestMode) _normalSave = _save;
            _isRunSuspended = false;
            _spells.Reload();
        }

        /// <summary>디버그 진행의 스탯 구매 기록을 0으로 되돌린다. SO 시작 레벨·룬·일반 저장은 유지한다.</summary>
        public void ResetDebugTree()
        {
            if (!_isDebugEnabled) return;
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.Nodes)
                if (node != null && node.EffectType != UpgradeEffectType.RuneUnlock)
                    _save.SetUpgradeNodeLevel(node.Id, 0);
            _spells.Recompile();
        }

        /// <summary>마법을 저장·컴파일하고 RAM과 최대 전력 제한을 만족하면 출격할 마법을 반환한다. 유지형 빔은 첫 시전 비용으로 검사하며, 불가하면 상태 문구를 남기고 false를 반환한다.</summary>
        public bool TryPrepareMission(out CompiledSpell spell)
        {
            _spells.Save();
            _spells.Recompile();
            if (!_spells.CompileResult.Ok || EquippedRam > Capacity)
            {
                SetStatus("editor.invalidEquip");
                spell = null;
                return false;
            }
            CompiledSpell preparedSpell = _spells.CompileResult.Spell;
            bool hasHeldBeam = preparedSpell.Trigger == SpellGrammar.TRIGGER_ON_ATTACK
                && preparedSpell.Root.Any(action => action.Form == BeamBehaviorRules.BEAM_KIND);
            double requiredEnergy = hasHeldBeam
                ? preparedSpell.GetCastCosts(GameData.Balance.Sim.ExecutionTimeScale).GetAmount("mana") : preparedSpell.EnergyCost;
            if (requiredEnergy > MaxEnergy)
            {
                SetStatusText(string.Format(GameData.L("editor.energyBlocked"), requiredEnergy, MaxEnergy));
                spell = null;
                return false;
            }
            spell = preparedSpell;
            return true;
        }

        /// <summary>
        /// 일반 모드에서 전투 보상과 처치 기록을 세이브에 반영하고, 연속 미션은 기존 스테이지 선택 기록 대신 별도 완주 기록을 남긴다. 테스트 모드에서는 반영하지 않는다.
        /// 클리어 때 지급한 룬 보상 목록을 반환한다.
        /// </summary>
        public IReadOnlyList<ModifierRewardDefinition> SettleMission(int stageNumber, int fragments, bool isCleared, IEnumerable<KeyValuePair<string, int>> killCounts,
            ModifierRewardDefinition clearRuneReward = null, bool isProgressionMission = false,
            IReadOnlyList<string> progressionChoiceIds = null, IReadOnlyList<string> progressionChoiceGrades = null)
        {
            if (_isTestMode) return Array.Empty<ModifierRewardDefinition>();
            var modifierRewards = new List<ModifierRewardDefinition>();
            if (isCleared && (!isProgressionMission || !_save.HasClearedProgression))
            {
                foreach (ModifierRewardDefinition reward in _stages.Get(stageNumber).ModifierRewards)
                {
                    GrantRune(reward.RuneId, reward.Grade, reward.Count, "stage_clear", persist: !isProgressionMission);
                    modifierRewards.Add(reward);
                }
            }
            if (isCleared && clearRuneReward != null)
            {
                GrantRune(clearRuneReward.RuneId, clearRuneReward.Grade, clearRuneReward.Count, "stage_clear", persist: !isProgressionMission);
                modifierRewards.Add(clearRuneReward);
            }
            if (isCleared)
            {
                if (isProgressionMission)
                {
                    if (!_save.HasClearedProgression && progressionChoiceIds != null && progressionChoiceGrades != null
                        && progressionChoiceIds.Count > 0)
                        _save.SetPendingProgressionChoice(progressionChoiceIds, progressionChoiceGrades);
                    _save.RecordProgressionClear();
                }
                else _save.RecordStageClear(stageNumber);
            }
            _save.Settle(fragments, isCleared && !isProgressionMission);
            foreach (var pair in killCounts) _save.RecordKills(pair.Key, pair.Value);
            PersistProgress();
            if (modifierRewards.Count > 0) _spells.Recompile();
            return modifierRewards;
        }

        /// <summary>
        /// 라운드 동안 모아 둔 조각과 소지 아이템 드롭을 세이브에 한 번에 지급하고, 끝낸 라운드 위치와 그 시점 체력, 라운드 보상 후보를 기록한다.
        /// persist이면 저장 정책에 맞춰 한 번 기록한다. 아이템이 늘면 소지량 검사를 위해 다시 컴파일한다. 테스트 모드에서는 지급하지 않는다.
        /// </summary>
        public void CommitRoundRewards(int fragments, IReadOnlyList<SimulationItemDrop> drops, int stageNumber, int roundNumber,
            double hp, double maxHp, IReadOnlyList<string> roundChoiceIds, IReadOnlyList<string> roundChoiceGrades, bool persist = true)
        {
            if (_isTestMode) return;
            _save.Settle(fragments, false);
            foreach (SimulationItemDrop drop in drops)
                GrantRune(drop.RuneId, drop.Grade, drop.Count, "combat_pickup", persist: false);
            if (drops.Count > 0) _spells.Recompile();
            _save.RecordProgressionRound(stageNumber, roundNumber, hp, maxHp, roundChoiceIds, roundChoiceGrades);
            if (persist) PersistProgress();
        }

        /// <summary>저장된 연속 미션 보상 후보 index를 하나 지급하고 후보 삭제와 소지량 변경을 한 번에 저장한다.</summary>
        public bool ClaimProgressionChoice(int index)
        {
            if (_isTestMode || index < 0 || index >= _save.PendingProgressionChoiceIds.Count) return false;
            string runeId = _save.PendingProgressionChoiceIds[index];
            string grade = _save.PendingProgressionChoiceGrades[index];
            if (!GameData.Runes.TryGet(runeId, out RuneDefinition rune) || rune.Category != SpellGrammar.CATEGORY_MODIFIER
                || !GameData.ModifierGrades.IsGradeAvailable(runeId, grade)) return false;
            int before = _save.GetStock(runeId, grade);
            _save.AddStock(runeId, grade, 1);
            _save.ClearPendingProgressionChoice();
            PersistProgress();
            _spells.Recompile();
            LocalTelemetry.RecordUsabilityEvent("module_acquired", "inventory", new Dictionary<string, object>
            {
                ["rune_id"] = runeId, ["category"] = rune.Category,
                ["grade"] = grade, ["count"] = 1, ["stock_before"] = before, ["stock_after"] = _save.GetStock(runeId, grade),
                ["reason"] = "clear_choice",
            });
            return true;
        }

        /// <summary>라운드 보상 후보 Modifier를 소지량에 즉시 반영하고 대기 후보를 비운 뒤 한 번에 저장하고 다시 컴파일한다. 테스트 모드는 지급하지 않는다.</summary>
        public void GrantRoundChoice(string runeId, string grade)
        {
            if (_isTestMode || !GameData.Runes.TryGet(runeId, out RuneDefinition rune) || rune.Category != SpellGrammar.CATEGORY_MODIFIER
                || !GameData.ModifierGrades.IsGradeAvailable(runeId, grade)) return;
            int before = _save.GetStock(runeId, grade);
            _save.AddStock(runeId, grade, 1);
            _save.ClearPendingRoundChoice();
            PersistProgress();
            _spells.Recompile();
            LocalTelemetry.RecordUsabilityEvent("module_acquired", "inventory", new Dictionary<string, object>
            {
                ["rune_id"] = runeId, ["category"] = rune.Category,
                ["grade"] = grade, ["count"] = 1, ["stock_before"] = before, ["stock_after"] = _save.GetStock(runeId, grade),
                ["reason"] = "round_clear_choice",
            });
        }

        /// <summary>일반 모드에서 룬 소지량을 늘리고 persist가 참이면 즉시 저장한다. 테스트 모드는 시작 소지량을 유지한다.</summary>
        public void GrantRune(string runeId, string grade, int count, string reason = "unspecified", bool persist = true)
        {
            if (_isTestMode || count <= 0 || !GameData.Runes.TryGet(runeId, out RuneDefinition rune)
                || (rune.Category != SpellGrammar.CATEGORY_MODIFIER && rune.Category != SpellGrammar.CATEGORY_ELEMENT
                    && rune.Category != SpellGrammar.CATEGORY_BEHAVIOR)) return;
            int before = _save.GetStock(runeId, grade);
            _save.AddStock(runeId, grade, count);
            if (persist) PersistProgress();
            int after = _save.GetStock(runeId, grade);
            if (after > before)
                LocalTelemetry.RecordUsabilityEvent("module_acquired", "inventory", new Dictionary<string, object>
                {
                    ["rune_id"] = runeId, ["category"] = rune.Category,
                    ["grade"] = string.IsNullOrEmpty(grade) ? null : grade,
                    ["count"] = after - before, ["stock_before"] = before, ["stock_after"] = after,
                    ["reason"] = reason,
                });
        }

        /// <summary>미사용 룬 3개와 스크랩 비용으로 합성한다. 속성은 스테이지 확률을, S는 교차 추첨을 쓰며 저장 실패 시 재고와 스크랩을 복구한다.</summary>
        public bool TrySynthesizeRunes(IReadOnlyList<string> materials, string grade,
            out string resultId, out string resultGrade, out string error)
        {
            resultId = null;
            resultGrade = null;
            error = "같은 분류와 등급의 사용하지 않는 룬 3개가 필요합니다.";
            if (_isTestMode)
            {
                error = "테스트 모드에서는 시작 지급 수량을 유지하므로 합성을 사용할 수 없습니다.";
                return false;
            }
            grade = grade ?? "";
            if (materials == null || materials.Count != RuneSynthesisRules.MATERIAL_COUNT) return false;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            string category = null;
            foreach (string id in materials)
            {
                if (string.IsNullOrEmpty(id) || !GameData.Runes.TryGet(id, out RuneDefinition rune)
                    || !RuneSynthesisRules.IsEligible(rune, grade) || (category != null && category != rune.Category)) return false;
                category = rune.Category;
                counts.TryGetValue(id, out int count);
                counts[id] = count + 1;
            }
            foreach (KeyValuePair<string, int> pair in counts)
                if (!_spells.TryGetUnusedStock(pair.Key, grade, out int unused, out _) || unused < pair.Value) return false;
            int scrapCost = RuneSynthesisRules.GetScrapCost(grade);
            if (_save.Currency < scrapCost)
            {
                error = "합성에는 스크랩 " + scrapCost + "개가 필요합니다.";
                return false;
            }
            string resultCategory = category;
            if (category == SpellGrammar.CATEGORY_ELEMENT || grade == "S")
                resultCategory = UnityEngine.Random.Range(0, 2) == 0 ? SpellGrammar.CATEGORY_BEHAVIOR : SpellGrammar.CATEGORY_MODIFIER;
            string selectedId;
            string selectedGrade;
            if (category == SpellGrammar.CATEGORY_ELEMENT)
            {
                DropRoll roll = RuneRewardRoller.DrawCategory(resultCategory, SelectedStage, _synthesisGrades, () => UnityEngine.Random.value);
                selectedId = roll.RuneId;
                selectedGrade = roll.Grade;
            }
            else
            {
                selectedGrade = RuneSynthesisRules.NextGrade(grade);
                selectedId = RuneRewardRoller.PickRune(resultCategory, selectedGrade, () => UnityEngine.Random.value);
            }
            if (!_save.TryConsumeStock(counts, grade)) return false;
            _save.Spend(scrapCost);
            _save.AddStock(selectedId, selectedGrade, 1);
            if (!PersistSpellStorage(_save))
            {
                _save.TryConsumeStock(new Dictionary<string, int> { [selectedId] = 1 }, selectedGrade);
                foreach (KeyValuePair<string, int> pair in counts) _save.AddStock(pair.Key, grade, pair.Value);
                _save.Settle(scrapCost, false);
                error = SaveStore.LastWarning;
                return false;
            }
            resultId = selectedId;
            resultGrade = selectedGrade;
            error = null;
            _spells.Recompile();
            return true;
        }

        /// <summary>주운·선택·보상받은 Modifier를 (종류, 등급) 소지량에 즉시 반영하고 저장한다. 이후 사망·시간 초과·후퇴와 무관하게 유지된다. 그 Modifier에 없는 등급이면 지급하지 않는다.</summary>
        public void GrantModifier(string runeId, string grade, int count, string reason = "unspecified")
        {
            GrantRune(runeId, grade, count, reason);
        }

        /// <summary>디버그 실행에서만 조각을 지급한다.</summary>
        public void DebugGrant()
        {
            if (!_isDebugEnabled) return;
            _save.Settle(500, false);
            PersistProgress();
        }

        /// <summary>디버그 실행에서만 모든 룬을 해금한다.</summary>
        public void DebugUnlock()
        {
            if (!_isDebugEnabled) return;
            foreach (var rune in GameData.Runes.All) _save.Unlock(rune.Id);
            PersistProgress();
            _spells.Recompile();
        }

        /// <summary>게임이 시작된 상태에서만 편집 마법을 기존 저장 정책으로 반영하고 타이틀의 빈 슬롯은 생성하지 않는다.</summary>
        public void SaveAll()
        {
            if (!_hasStartedGame) return;
            _spells.Save();
        }

        /// <summary>마법 보관함 변경을 일반 세션의 현재 슬롯 저장에 반영한다.</summary>
        private bool PersistSpellStorage(PlayerSave save)
        {
            if (IsMemorySession) return true;
            _isSaveDirty = true;
            return WriteCurrentSave(save);
        }

        /// <summary>진행 변경을 현재 슬롯에 즉시 기록하고 실패하면 상태 문구로 알린다.</summary>
        private void PersistProgress()
        {
            if (IsMemorySession) return;
            _isSaveDirty = true;
            if (!WriteCurrentSave(_save)) SetStatusText(SaveStore.LastWarning);
        }

        /// <summary>구매 레벨이 남은 룬 해금 노드의 룬을 저장 데이터에 복구하고 변경 여부를 반환한다.</summary>
        private bool RestorePurchasedRuneUnlocks(PlayerSave save)
        {
            bool hasRestoredRune = false;
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.Nodes)
                hasRestoredRune |= RestorePurchasedRuneUnlock(save, node);
            return hasRestoredRune;
        }

        /// <summary>구매 또는 시작 레벨이 있는 룬 해금 노드의 룬을 복구하고 실제 추가 여부를 반환한다.</summary>
        private static bool RestorePurchasedRuneUnlock(PlayerSave save, UpgradeTreeNodeDefinition node)
        {
            if (node == null || node.EffectType != UpgradeEffectType.RuneUnlock) return false;
            int level = Math.Min(Math.Max(node.InitialLevel, save.GetUpgradeNodeLevel(node.Id)), node.MaxLevel);
            if (level <= 0 || save.UnlockedRunes.Contains(node.RuneId)) return false;
            save.Unlock(node.RuneId);
            return save.UnlockedRunes.Contains(node.RuneId);
        }

        /// <summary>복구한 룬 해금을 현재 슬롯에 반영하고 실패하면 상태 문구로 알린다.</summary>
        private void PersistRestoredRuneUnlocks(bool hasRestoredRune)
        {
            if (!hasRestoredRune || IsMemorySession) return;
            _isSaveDirty = true;
            if (!WriteCurrentSave(_save)) SetStatusText(SaveStore.LastWarning);
        }

        /// <summary>일반 진행을 활성 슬롯에 기록하고 성공하면 미저장 상태를 해제한다.</summary>
        private bool WriteCurrentSave(PlayerSave save)
        {
            if (SaveStore.WriteToSlot(SaveStore.ActiveSlot, save))
            {
                _isSaveDirty = false;
                return true;
            }
            _isSaveDirty = true;
            return false;
        }

        /// <summary>
        /// 새 디버그 진행에 SO의 모든 룬 해금을 적용하고, 유효한 Modifier 등급별·비활성 아닌 행동 등급별·전투 속성별
        /// 소지량을 9개로 채운다.
        /// </summary>
        private void UnlockDebugRunes()
        {
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.Nodes)
                if (node != null && node.EffectType == UpgradeEffectType.RuneUnlock)
                    _save.Unlock(node.RuneId);
            foreach (RuneDefinition rune in GameData.Runes.All)
            {
                if (rune.Category == SpellGrammar.CATEGORY_MODIFIER)
                {
                    foreach (string grade in GameData.ModifierGrades.GetAvailableGrades(rune.Id))
                        _save.AddModifierStock(rune.Id, grade, DEBUG_MODIFIER_STOCK - _save.GetModifierStock(rune.Id, grade));
                }
                else if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR && !rune.IsDisabled)
                {
                    foreach (string grade in GameData.Runes.BehaviorGradeOrder)
                        _save.AddStock(rune.Id, grade, DEBUG_MODIFIER_STOCK - _save.GetStock(rune.Id, grade));
                }
                else if (rune.Category == SpellGrammar.CATEGORY_ELEMENT && !rune.IsDisabled)
                {
                    // AddStock이 전투 속성만 받으므로 무속성·기능 속성은 지급되지 않는다.
                    _save.AddStock(rune.Id, "", DEBUG_MODIFIER_STOCK - _save.GetStock(rune.Id, ""));
                }
            }
        }

        /// <summary>
        /// 테스트 프로필 세이브를 만든다. 1~10스테이지 클리어, 시작 소지량에 더해
        /// 활성 모디·행동의 사용 가능한 모든 등급을 최소 2개씩, 무속성을 제외한 전투 속성을 각 2개 지급한다.
        /// 행동 C등급의 시작 소지량은 유지하며 새 룬·등급은 등록된 데이터에서 자동으로 포함한다.
        /// </summary>
        private PlayerSave CreateTestSave()
        {
            PlayerSave save = PlayerSave.CreateNew();
            for (int stage = 1; stage <= TEST_CLEARED_STAGE; stage++) save.RecordStageClear(stage);
            save.SelectStage(TEST_STAGE, false);
            foreach (RuneDefinition rune in GameData.Runes.All)
            {
                if (rune.IsDisabled) continue;
                if (rune.Category == SpellGrammar.CATEGORY_MODIFIER)
                {
                    foreach (string grade in GameData.ModifierGrades.GetAvailableGrades(rune.Id))
                        save.AddModifierStock(rune.Id, grade, TEST_STOCK_COUNT - save.GetModifierStock(rune.Id, grade));
                }
                else if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR && !rune.IsDisabled)
                {
                    int startStock = GameData.Balance.Economy.BehaviorStartStock;
                    save.AddStock(rune.Id, SpellGrammar.DEFAULT_BEHAVIOR_GRADE, startStock - save.GetStock(rune.Id, SpellGrammar.DEFAULT_BEHAVIOR_GRADE));
                    foreach (string grade in GameData.Runes.BehaviorGradeOrder)
                        save.AddStock(rune.Id, grade, TEST_STOCK_COUNT - save.GetStock(rune.Id, grade));
                }
                else if (rune.Category == SpellGrammar.CATEGORY_ELEMENT && !rune.IsDisabled
                    && GameData.Runes.TryGetRuneElement(rune.Id, out ElementDefinition element) && element.Id != SpellGrammar.ELEMENT_NEUTRAL)
                {
                    save.AddStock(rune.Id, "", TEST_STOCK_COUNT);
                }
            }
            return save;
        }

        /// <summary>그래프에서 엣지에 연결된 룬의 RAM만 합산한다.</summary>
        private int GetGraphRam(SpellGraph graph)
        {
            if (graph == null) return 0;
            var ram = 0;
            foreach (GraphNode node in graph.Nodes)
                if (graph.IsNodeConnected(node.Id)) ram += GameData.Runes.NodeRam(node);
            return ram;
        }

        /// <summary>해당 강화 유형에 속한 트리 노드의 저장 레벨까지 효과량을 합산한다.</summary>
        public float GetUpgradeTreeEffectTotal(UpgradeEffectType effectType)
        {
            float total = 0;
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.Nodes)
                if (node != null && node.EffectType == effectType)
                    total += node.GetTotalAmount(GetUpgradeNodeLevel(node.Id));
            return total;
        }

        /// <summary>편집 사본을 반영한 장착 마법 전체의 RAM 사용량을 반환한다.</summary>
        private int CalculateEquippedRam(SpellGraph edited)
        {
            var ram = 0;
            foreach (var id in _save.Loadout.Take(_save.SlotCount)) ram += GetGraphRam(edited != null && id == edited.Id ? edited : _save.FindGraph(id));
            return ram;
        }
    }
}
