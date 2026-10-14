using System;
using System.Collections.Generic;
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

        private PlayerSave _save;
        private PlayerSave _normalSave;
        private readonly StageCatalog _stages;
        private readonly UpgradeTreeDefinition _upgradeTree;
        private bool _isDebugEnabled;
        private readonly SpellEditSession _spells;
        private string _statusMessage;
        private bool _isSaveDirty;
        private bool _hasPendingFirstRunHelp;

        private string _dockScenario = "dummy_single";
        private bool _dockAutoFire;
        private bool _dockAdaptation;
        private float _dockSpeed = 1;

        public PlayerSave Save => _save;
        public ISpellEditor Spells => _spells;
        public bool IsDebugEnabled => _isDebugEnabled;
        public bool IsSaveDirty => _isSaveDirty;
        public string StatusMessage => _statusMessage;

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
        public int SelectedStage => _save.SelectedStage;
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

        /// <summary>타이틀에서 선택한 일반 저장 또는 새 메모리 디버그 진행을 열고 작업실로 전환한다.</summary>
        public void StartGame(bool isDebug)
        {
            bool isFirstGameStart = !isDebug && !SaveStore.HasSlot(SaveStore.ActiveSlot);
            if (isDebug) _hasPendingFirstRunHelp = false;
            else _hasPendingFirstRunHelp |= isFirstGameStart;
            _isDebugEnabled = isDebug;
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
            RequestScreen(AppScreen.Workshop);
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
            if (!_isDebugEnabled)
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
            if (!_isDebugEnabled)
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
                if (!_isDebugEnabled)
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
            if (_isDebugEnabled) return false;
            _spells.Save();
            if (!_isSaveDirty) return true;
            _isSaveDirty = true;
            return WriteCurrentSave(_save);
        }

        /// <summary>현재 슬롯의 저장 데이터를 다시 읽어 미저장 진행과 편집 사본을 교체한다.</summary>
        public bool LoadActiveSave()
        {
            if (_isDebugEnabled || !SaveStore.TryLoadSlot(SaveStore.ActiveSlot, out PlayerSave loaded)) return false;
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
            if (!isEnabled || _isDebugEnabled) return true;
            _spells.Save();
            return !_isSaveDirty;
        }

        /// <summary>문자열 키를 번역해 상태 문구로 저장한다.</summary>
        public void SetStatus(string key) { _statusMessage = GameData.L(key); }

        /// <summary>이미 번역된 문구를 상태 문구로 저장한다.</summary>
        public void SetStatusText(string text) { _statusMessage = text; }

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

        /// <summary>스테이지, 선행 노드, 재화를 확인하고 노드를 구매할 수 없으면 이유 키를 반환한다.</summary>
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

        /// <summary>진행을 초기화하고 새 시작 마법으로 편집 세션을 다시 연다.</summary>
        public void ResetSave()
        {
            _save = PlayerSave.CreateNew();
            if (_isDebugEnabled) UnlockDebugRunes();
            else _normalSave = _save;
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
        /// 전투 보상과 처치 기록을 세이브에 반영하고 저장한다. 클리어면 다음 스테이지를 해금하고, 처음 클리어한 스테이지면
        /// 스테이지 데이터의 룬 소지량 보상(모디·속성·행동)을 지급한다. 지급한 보상 목록을 반환한다.
        /// </summary>
        public IReadOnlyList<ModifierRewardDefinition> SettleMission(int stageNumber, int fragments, bool isCleared, IEnumerable<KeyValuePair<string, int>> killCounts)
        {
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

        /// <summary>주운·선택·보상받은 룬 소지 아이템(모디·속성·행동)을 (종류, 등급) 소지량에 즉시 반영하고 저장한다. 이후 사망·시간 초과·후퇴와 무관하게 유지된다. 받을 수 없는 (룬, 등급)이면 지급하지 않는다.</summary>
        public void GrantRune(string runeId, string grade, int count)
        {
            if (count <= 0 || !GameData.Runes.TryGet(runeId, out RuneDefinition rune)
                || (rune.Category != SpellGrammar.CATEGORY_MODIFIER && rune.Category != SpellGrammar.CATEGORY_ELEMENT
                    && rune.Category != SpellGrammar.CATEGORY_BEHAVIOR)) return;
            _save.AddStock(runeId, grade, count);
            PersistProgress();
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
            if (_isDebugEnabled) return true;
            _isSaveDirty = true;
            if (!SaveStore.AutoSaveEnabled) return true;
            return WriteCurrentSave(save);
        }

        /// <summary>진행 변경을 미저장으로 표시하고 자동 저장이 켜졌을 때 현재 슬롯에 기록한다.</summary>
        private void PersistProgress()
        {
            if (_isDebugEnabled) return;
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
            if (!hasRestoredRune || _isDebugEnabled) return;
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

        /// <summary>새 디버그 진행에 SO의 모든 룬 해금을 적용하고 유효한 Modifier 등급별 소지량을 9개로 채운다.</summary>
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
                if (rune.Category != SpellGrammar.CATEGORY_MODIFIER) continue;
                foreach (string grade in GameData.ModifierGrades.GetAvailableGrades(rune.Id))
                    _save.AddModifierStock(rune.Id, grade, DEBUG_MODIFIER_STOCK - _save.GetModifierStock(rune.Id, grade));
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
