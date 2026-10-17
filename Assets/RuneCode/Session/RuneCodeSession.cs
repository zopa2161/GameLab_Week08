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

        // 이 레벨 상한을 가진 노드는 반복 성장용 endgame 노드로 보고 테스트 프로필에서 구매하지 않는다.
        private const int TEST_ENDGAME_NODE_LEVEL = 99;

        private PlayerSave _save;
        private PlayerSave _normalSave;
        private readonly StageCatalog _stages;
        private readonly UpgradeTreeDefinition _upgradeTree;
        private bool _isDebugEnabled;
        private bool _isTestMode;
        private readonly SpellEditSession _spells;
        private string _statusMessage;
        private long _statusRevision;
        private bool _isSaveDirty;
        private bool _hasPendingFirstRunHelp;

        private string _dockScenario = "dummy_single";
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
        public string StatusMessage => _statusMessage;
        /// <summary>SetStatus 또는 SetStatusText가 호출될 때마다 증가하는 상태 메시지 revision이다.</summary>
        public long StatusRevision => _statusRevision;

        public string DockScenario => _dockScenario;
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
        public float KillEnergyBonus => GetUpgradeTreeEffectTotal(UpgradeEffectType.KillEnergy);
        public float BattleDuration => (float)_stages.Get(1).Stream.RampSeconds;
        public int EquippedRam => CalculateEquippedRam(_spells.Graph);
        public int SelectedStage => _isTestMode ? TEST_STAGE : _save.SelectedStage;
        public StageDefinition SelectedStageDefinition => _stages.Get(SelectedStage);
        public int HighestClearedStage => _save.HighestClearedStage;

        /// <summary>화면 전환 요청을 받는 콜백이다. RuneCodeApp만 등록한다.</summary>
        public event Action<AppScreen> ScreenRequested;

        /// <summary>
        /// 세이브, 스테이지 정의(제한시간 성장 표시 기준값), 디버그 여부와 시작 경고 문구로 세션과 마법 편집 세션을 만든다.
        /// </summary>
        public RuneCodeSession(PlayerSave save, StageCatalog stages, UpgradeTreeDefinition upgradeTree, bool isDebugEnabled,
            string initialStatus)
        {
            _normalSave = save;
            _save = isDebugEnabled ? PlayerSave.CreateNew() : save;
            _stages = stages;
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

        /// <summary>일반 저장 또는 메모리 디버그 진행을 열고, 빌드의 일반 시작은 로그 회차도 자동 생성해 작업실로 전환한다.</summary>
        public void StartGame(bool isDebug)
        {
            if (LocalTelemetry.IsUsabilityTestActive) LocalTelemetry.EndUsabilityTest("mode_changed", _spells.Graph);
            bool isFirstGameStart = !isDebug && !SaveStore.HasSlot(SaveStore.ActiveSlot);
            if (isDebug) _hasPendingFirstRunHelp = false;
            else _hasPendingFirstRunHelp |= isFirstGameStart;
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
            RequestScreen(AppScreen.Workshop);
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
            _hasPendingFirstRunHelp = false;
            _isDebugEnabled = false;
            _isTestMode = true;
            _save = CreateTestSave();
            _spells.Reload();

            var stock = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> pair in _save.CreateStockMap()) stock[pair.Key] = pair.Value;
            var upgrades = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.Nodes)
                if (node != null) upgrades[node.Id] = GetUpgradeNodeLevel(node.Id);
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.UnplacedNodes)
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
            RequestScreen(AppScreen.Workshop);
            return true;
        }

        /// <summary>새 세이브로 처음 시작할 때 대기 중인 도움말 요청을 반환하고 한 번만 소비한다.</summary>
        public bool ConsumeFirstRunHelpRequest()
        {
            bool hasPendingRequest = _hasPendingFirstRunHelp;
            _hasPendingFirstRunHelp = false;
            return hasPendingRequest;
        }

        /// <summary>타이틀에서 선택한 저장 슬롯을 읽어 일반 진행으로 설정하고 성공 여부를 반환한다.</summary>
        public bool SelectSaveSlot(int slot)
        {
            if (!SaveStore.TryLoadSlot(slot, out PlayerSave loaded)) return false;
            _hasPendingFirstRunHelp = false;
            SaveStore.SetActiveSlot(slot);
            _normalSave = loaded;
            if (!IsMemorySession)
            {
                _save = loaded;
                _isSaveDirty = false;
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
            _hasPendingFirstRunHelp = true;
            _normalSave = created;
            if (!IsMemorySession)
            {
                _save = created;
                _isSaveDirty = false;
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

        /// <summary>현재 슬롯의 저장 데이터를 다시 읽어 미저장 진행과 편집 사본을 교체한다.</summary>
        public bool LoadActiveSave()
        {
            if (IsMemorySession || !SaveStore.TryLoadSlot(SaveStore.ActiveSlot, out PlayerSave loaded)) return false;
            _save = loaded;
            _normalSave = loaded;
            _isSaveDirty = false;
            bool restoredRuneUnlocks = RestorePurchasedRuneUnlocks(_save);
            _spells.Reload();
            PersistRestoredRuneUnlocks(restoredRuneUnlocks);
            return true;
        }

        /// <summary>자동 저장 설정을 변경하고 켤 때 현재 진행을 즉시 기록한다.</summary>
        public bool SetAutoSaveEnabled(bool isEnabled)
        {
            SaveStore.SetAutoSaveEnabled(isEnabled);
            if (!isEnabled || IsMemorySession) return true;
            _spells.Save();
            return !_isSaveDirty;
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

        /// <summary>스테이지, 선행 노드, 재화를 확인하고 테스트 모드 구매를 포함해 구매 불가 사유를 반환한다.</summary>
        public bool CanPurchaseUpgradeNode(string nodeId, out string reasonKey)
        {
            reasonKey = null;
            UpgradeTreeNodeDefinition node = _upgradeTree.FindNode(nodeId);
            if (node == null || node.MaxLevel == 0)
            {
                reasonKey = "tree.invalidNode";
                return false;
            }
            if (_isTestMode)
            {
                reasonKey = "tree.maxed";
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
            if (!_isDebugEnabled)
                foreach (UpgradeTreePrerequisite prerequisite in node.Prerequisites)
                    if (GetUpgradeNodeLevel(prerequisite.NodeId) < prerequisite.RequiredLevel)
                    {
                        reasonKey = "tree.prerequisiteLocked";
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

        /// <summary>screenShake 또는 hitStop 설정을 저장한다.</summary>
        public void SetSetting(string key, bool enabled)
        {
            _save.SetFeedback(key == "screenShake" ? enabled : _save.ScreenShake, key == "hitStop" ? enabled : _save.HitStop);
            PersistProgress();
        }

        /// <summary>진행을 초기화하고 새 시작 마법으로 편집 세션을 다시 연다. 테스트 모드는 테스트 프로필로 다시 만든다.</summary>
        public void ResetSave()
        {
            _save = _isTestMode ? CreateTestSave() : PlayerSave.CreateNew();
            if (_isDebugEnabled) UnlockDebugRunes();
            else if (!_isTestMode) _normalSave = _save;
            _spells.Reload();
            PersistProgress();
        }

        /// <summary>디버그 진행의 배치·미배치 스탯 구매 기록을 0으로 되돌린다. SO 시작 레벨·룬·일반 저장은 유지한다.</summary>
        public void ResetDebugTree()
        {
            if (!_isDebugEnabled) return;
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.Nodes)
                if (node != null && node.EffectType != UpgradeEffectType.RuneUnlock)
                    _save.SetUpgradeNodeLevel(node.Id, 0);
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.UnplacedNodes)
                if (node != null && node.EffectType != UpgradeEffectType.RuneUnlock)
                    _save.SetUpgradeNodeLevel(node.Id, 0);
            _spells.Recompile();
        }

        /// <summary>튜토리얼 완료 단계를 저장한다.</summary>
        public void AdvanceTutorial(int step)
        {
            _save.SetTutorialStep(step);
            PersistProgress();
        }

        /// <summary>마법을 저장·컴파일하고 장착 가능하면 시전할 마법을 반환한다. 불가하면 상태 문구를 남기고 false를 반환한다.</summary>
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
            spell = _spells.CompileResult.Spell;
            return true;
        }

        /// <summary>
        /// 일반 모드에서 전투 보상과 처치 기록을 세이브에 반영한다. 테스트 모드에서는 시작 조건 유지를 위해 보상을 반영하지 않는다.
        /// 클리어 때 지급한 룬 보상 목록을 반환한다.
        /// </summary>
        public IReadOnlyList<ModifierRewardDefinition> SettleMission(int stageNumber, int fragments, bool isCleared, IEnumerable<KeyValuePair<string, int>> killCounts)
        {
            if (_isTestMode) return Array.Empty<ModifierRewardDefinition>();
            var modifierRewards = new List<ModifierRewardDefinition>();
            if (isCleared && stageNumber > _save.HighestClearedStage)
            {
                foreach (ModifierRewardDefinition reward in _stages.Get(stageNumber).ModifierRewards)
                {
                    if (!GameData.Runes.TryGet(reward.RuneId, out RuneDefinition rune))
                    { Debug.LogWarning("[Stage] 룬이 없는 보상 ID를 건너뜁니다: " + reward.RuneId); continue; }
                    if (rune.Category == SpellGrammar.CATEGORY_ELEMENT)
                    {
                        GrantRune(reward.RuneId, "", reward.Count);
                    }
                    else if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR)
                    {
                        if (string.IsNullOrEmpty(reward.Grade))
                        { Debug.LogWarning("[Stage] 등급이 없는 행동 보상을 건너뜁니다: " + reward.RuneId); continue; }
                        GrantRune(reward.RuneId, reward.Grade, reward.Count);
                    }
                    else if (rune.Category == SpellGrammar.CATEGORY_MODIFIER)
                    {
                        GrantRune(reward.RuneId, string.IsNullOrEmpty(reward.Grade) ? GameData.ModifierGrades.GetLowestAvailableGrade(reward.RuneId) : reward.Grade, reward.Count);
                    }
                    else
                    {
                        // 소지 아이템이 아닌 ID는 데이터 오류로 보고 지급하지 않는다.
                        Debug.LogWarning("[Stage] 소지 아이템이 아닌 보상 ID를 건너뜁니다: " + reward.RuneId); continue;
                    }
                    modifierRewards.Add(reward);
                }
            }
            if (isCleared) _save.RecordStageClear(stageNumber);
            _save.Settle(fragments, isCleared);
            foreach (var pair in killCounts) _save.RecordKills(pair.Key, pair.Value);
            PersistProgress();
            if (modifierRewards.Count > 0) _spells.Recompile();
            return modifierRewards;
        }

        /// <summary>일반 모드에서 주운·선택·보상받은 룬 소지 아이템을 즉시 반영한다. 테스트 모드는 시작 소지량을 유지한다.</summary>
        public void GrantRune(string runeId, string grade, int count)
        {
            if (_isTestMode || count <= 0 || !GameData.Runes.TryGet(runeId, out RuneDefinition rune)
                || (rune.Category != SpellGrammar.CATEGORY_MODIFIER && rune.Category != SpellGrammar.CATEGORY_ELEMENT
                    && rune.Category != SpellGrammar.CATEGORY_BEHAVIOR)) return;
            _save.AddStock(runeId, grade, count);
            PersistProgress();
        }

        /// <summary>동일 분류·등급의 미사용 룬 3개를 다음 등급 무작위 룬으로 교환한다. 저장 실패 시 재고를 복구하고 오류를 반환한다.</summary>
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
            string nextGrade = RuneSynthesisRules.NextGrade(grade);
            if (materials == null || materials.Count != RuneSynthesisRules.MATERIAL_COUNT || nextGrade == null) return false;
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
                if (!_spells.TryGetModifierUsage(pair.Key, grade, out int used, out int owned) || owned - used < pair.Value) return false;
            List<string> candidates = RuneSynthesisRules.GetResults(category, nextGrade);
            if (candidates.Count == 0)
            {
                error = "해당 등급에서 얻을 수 있는 룬이 없습니다.";
                return false;
            }
            string selectedId = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            if (!_save.TryConsumeStock(counts, grade)) return false;
            _save.AddStock(selectedId, nextGrade, 1);
            if (!PersistSpellStorage(_save))
            {
                _save.TryConsumeStock(new Dictionary<string, int> { [selectedId] = 1 }, nextGrade);
                foreach (KeyValuePair<string, int> pair in counts) _save.AddStock(pair.Key, grade, pair.Value);
                error = SaveStore.LastWarning;
                return false;
            }
            resultId = selectedId;
            resultGrade = nextGrade;
            error = null;
            _spells.Recompile();
            return true;
        }

        /// <summary>주운·선택·보상받은 Modifier를 (종류, 등급) 소지량에 즉시 반영하고 저장한다. 이후 사망·시간 초과·후퇴와 무관하게 유지된다. 그 Modifier에 없는 등급이면 지급하지 않는다.</summary>
        public void GrantModifier(string runeId, string grade, int count)
        {
            GrantRune(runeId, grade, count);
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

        /// <summary>편집 중인 마법을 반영하고 자동 저장 설정에 맞춰 저장한다. 앱 종료 시 호출한다.</summary>
        public void SaveAll()
        {
            _spells.Save();
        }

        /// <summary>마법 보관함 변경을 일반 세션의 저장·자동 저장 정책에 맞춰 반영한다.</summary>
        private bool PersistSpellStorage(PlayerSave save)
        {
            if (IsMemorySession) return true;
            _isSaveDirty = true;
            if (!SaveStore.AutoSaveEnabled) return true;
            return WriteCurrentSave(save);
        }

        /// <summary>진행 변경을 미저장으로 표시하고 자동 저장이 켜졌을 때 현재 슬롯에 기록한다.</summary>
        private void PersistProgress()
        {
            if (IsMemorySession) return;
            _isSaveDirty = true;
            if (SaveStore.AutoSaveEnabled && !WriteCurrentSave(_save)) SetStatusText(SaveStore.LastWarning);
        }

        /// <summary>구매 레벨이 남은 룬 해금 노드의 룬을 저장 데이터에 복구하고 변경 여부를 반환한다.</summary>
        private bool RestorePurchasedRuneUnlocks(PlayerSave save)
        {
            bool hasRestoredRune = false;
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.Nodes)
                hasRestoredRune |= RestorePurchasedRuneUnlock(save, node);
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.UnplacedNodes)
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

        /// <summary>복구한 룬 해금을 미저장으로 표시하고 자동 저장 설정에 맞춰 현재 슬롯에 반영한다.</summary>
        private void PersistRestoredRuneUnlocks(bool hasRestoredRune)
        {
            if (!hasRestoredRune || IsMemorySession) return;
            _isSaveDirty = true;
            if (SaveStore.AutoSaveEnabled && !WriteCurrentSave(_save)) SetStatusText(SaveStore.LastWarning);
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
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.UnplacedNodes)
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
        /// 테스트 프로필 세이브를 만든다. 1~10스테이지 클리어, endgame 노드를 제외한 강화 트리 최대 레벨과 룬 해금, 시작 소지량에 더해
        /// 활성 모디·행동의 사용 가능한 모든 등급을 최소 2개씩, 무속성을 제외한 전투 속성을 각 2개 지급한다.
        /// 행동 C등급의 시작 소지량은 유지하며 새 룬·등급은 등록된 데이터에서 자동으로 포함한다.
        /// </summary>
        private PlayerSave CreateTestSave()
        {
            PlayerSave save = PlayerSave.CreateNew();
            for (int stage = 1; stage <= TEST_CLEARED_STAGE; stage++) save.RecordStageClear(stage);
            save.SelectStage(TEST_STAGE, false);
            MaxUpgradeNodes(save, _upgradeTree.Nodes);
            MaxUpgradeNodes(save, _upgradeTree.UnplacedNodes);
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

        /// <summary>노드 목록의 강화 노드를 최대 레벨로 기록하고 룬 해금 노드의 룬을 해금한다. 반복 성장용 endgame 노드는 0레벨로 둔다.</summary>
        private static void MaxUpgradeNodes(PlayerSave save, IReadOnlyList<UpgradeTreeNodeDefinition> nodes)
        {
            foreach (UpgradeTreeNodeDefinition node in nodes)
            {
                if (node == null || node.MaxLevel >= TEST_ENDGAME_NODE_LEVEL) continue;
                save.SetUpgradeNodeLevel(node.Id, node.MaxLevel);
                if (node.EffectType == UpgradeEffectType.RuneUnlock) save.Unlock(node.RuneId);
            }
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
