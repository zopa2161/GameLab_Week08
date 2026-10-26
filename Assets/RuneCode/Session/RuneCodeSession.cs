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
        private const string PLAY_LOG_PARTICIPANT_KEY = "RuneCode.PlayLogParticipantId";

        private PlayerSave _save;
        private PlayerSave _normalSave;
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
        private int _workshopStage;
        private int _workshopRound;
        private int _testOnboardingChapter;
        private bool _hasEnteredTestEditorDuringWorkshop;
        private bool _hasCompletedTestEditorVisit;

        private string _dockScenario = "dummy_single";
        private int _dockDummyCount = 1;
        private bool _dockAutoFire;
        private bool _dockAdaptation;
        private float _dockSpeed = 1;

        public PlayerSave Save => _save;
        public ISpellEditor Spells => _spells;
        public bool IsDebugEnabled => _isDebugEnabled;

        /// <summary>타이틀의 테스트 모드로 시작한 메모리 진행인지 반환한다. 규칙은 일반 진행과 같고 디버그 권한은 주지 않는다.</summary>
        public bool IsTestMode => _isTestMode;

        /// <summary>테스트 모드에서 마지막으로 띄운 온보딩 챕터 번호다. 아직 띄우지 않았으면 0이다.</summary>
        public int TestOnboardingChapter => _testOnboardingChapter;

        /// <summary>테스트 모드에서 첫 편집창 방문과 정상 복귀를 마칠 때까지 다음 라운드 포털을 잠그는지 반환한다.</summary>
        public bool IsTestEditorVisitRequired => _isTestMode && !_hasCompletedTestEditorVisit;

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

        public float MaxEnergy => GameData.Balance.Player.MaxEnergy + GetUpgradeTreeEffectTotal(UpgradeEffectType.MaxEnergy);
        public float MaxHp => GameData.Balance.Player.MaxHp + GetUpgradeTreeEffectTotal(UpgradeEffectType.MaxHp);
        public float EnergyRegen => GameData.Balance.Player.EnergyRegen + GetUpgradeTreeEffectTotal(UpgradeEffectType.EnergyRegen);
        public float DamageMultiplier => 1 + GetUpgradeTreeEffectTotal(UpgradeEffectType.Damage);
        public float MoveSpeedMultiplier => 1 + GetUpgradeTreeEffectTotal(UpgradeEffectType.MoveSpeed);
        public float ScrapGainMultiplier => 1 + GetUpgradeTreeEffectTotal(UpgradeEffectType.ScrapGain);
        public float ScrapPickupRangeMultiplier => 1 + GetUpgradeTreeEffectTotal(UpgradeEffectType.ScrapPickupRange);
        public float ScrapHealRatio => GetUpgradeTreeEffectTotal(UpgradeEffectType.ScrapHeal);
        public float KillEnergyBonus => GetUpgradeTreeEffectTotal(UpgradeEffectType.KillEnergy);
        public int SelectedStage => _save.SelectedStage;
        public int HighestClearedStage => _save.HighestClearedStage;
        public bool HasClearedProgression => _save.HasClearedProgression;
        public IReadOnlyList<string> PendingProgressionChoiceIds => _save.PendingProgressionChoiceIds;
        public IReadOnlyList<string> PendingProgressionChoiceGrades => _save.PendingProgressionChoiceGrades;

        /// <summary>트리 속성 선택 노드를 사고 아직 속성을 고르지 않았는지 반환한다.</summary>
        public bool HasPendingElementPick => _save.HasPendingElementPick;

        /// <summary>도움말에서 열람할 수 있는 항목 수다. 자동 개방 조건이 없으므로 항상 전체를 연다.</summary>
        public int UnlockedHelpCount => int.MaxValue;

        /// <summary>화면 전환 요청을 받는 콜백이다. RuneCodeApp만 등록한다.</summary>
        public event Action<AppScreen> ScreenRequested;

        /// <summary>
        /// 세이브, 강화 트리, 디버그 여부와 시작 경고 문구로 세션과 마법 편집 세션을 만든다.
        /// </summary>
        public RuneCodeSession(PlayerSave save, UpgradeTreeDefinition upgradeTree, bool isDebugEnabled,
            string initialStatus)
        {
            _normalSave = save;
            SaveStore.InitializeFeedbackSettings(save);
            _save = isDebugEnabled ? PlayerSave.CreateNew() : save;
            _synthesisGrades = new ModifierGradeDropSource(GameData.ModifierGrades);
            _upgradeTree = upgradeTree;
            _isDebugEnabled = isDebugEnabled;
            _statusMessage = initialStatus;
            bool restoredRuneUnlocks = !isDebugEnabled && RestorePurchasedRuneUnlocks(_save);
            if (isDebugEnabled) PrepareDebugSave();
            var storage = new SessionSpellStorage(() => _save, PersistSpellStorage);
            var policy = new SessionSpellPolicy(this);
            _spells = new SpellEditSession(storage, policy);
            PersistRestoredRuneUnlocks(restoredRuneUnlocks);
        }

        /// <summary>등록된 전환 콜백으로 지정 화면 전환을 요청한다.</summary>
        public void RequestScreen(AppScreen screen) { ScreenRequested?.Invoke(screen); }

        /// <summary>진행 중인 런을 보관 상태로 표시하고 보관 시점의 스테이지와 라운드를 기록한다. 미션 화면은 이 표시를 보고 런을 버리지 않으며, 작업실은 미션 화면 위 오버레이로 표시한다.</summary>
        public void SuspendRunForWorkshop(int stage, int round)
        {
            _isRunSuspended = true;
            _hasEnteredTestEditorDuringWorkshop = false;
            _workshopStage = stage;
            _workshopRound = round;
        }

        /// <summary>온보딩의 편집창 안내 단계에서 실제 편집창 탭에 들어왔음을 기록한다. 테스트 모드의 작업실 방문 중에만 적용한다.</summary>
        public void RecordTestEditorVisit()
        {
            if (IsTestEditorVisitRequired && _isRunSuspended) _hasEnteredTestEditorDuringWorkshop = true;
        }

        /// <summary>검증을 마친 런의 보관 표시를 해제하고, 이번 작업실에서 테스트 편집창을 방문했다면 최초 방문을 완료한다.</summary>
        public void ResumeSuspendedRun()
        {
            if (_isRunSuspended && IsTestEditorVisitRequired && _hasEnteredTestEditorDuringWorkshop)
                _hasCompletedTestEditorVisit = true;
            _hasEnteredTestEditorDuringWorkshop = false;
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
            if (isDebug) PrepareDebugSave();
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

        /// <summary>
        /// 익명 참가자 ID로 저장하지 않는 새 진행과 로컬 로그 세션을 열고 전투 화면으로 전환한다.
        /// 진행 규칙은 일반 모드와 같으며 시작에 성공하면 true를 반환한다.
        /// </summary>
        public bool StartTestGame(string participantId)
        {
            if (string.IsNullOrWhiteSpace(participantId)) return false;
            if (LocalTelemetry.IsUsabilityTestActive) LocalTelemetry.EndUsabilityTest("restarted", _spells.Graph);
            _isDebugEnabled = false;
            _isTestMode = true;
            _testOnboardingChapter = 0;
            _hasEnteredTestEditorDuringWorkshop = false;
            _hasCompletedTestEditorVisit = false;
            _save = PlayerSave.CreateNew();
            RestorePurchasedRuneUnlocks(_save);
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
            LocalTelemetry.RecordUsabilityEvent("simulation_request", "title", new Dictionary<string, object>
            {
                ["input_method"] = "title_start",
                ["stage"] = SelectedStage,
            });
            RequestScreen(AppScreen.Mission);
            return true;
        }

        /// <summary>
        /// 테스트 모드에서 chapter가 다음 순서의 온보딩이면 띄운 것으로 기록하고 로그를 남긴 뒤 true를 반환한다.
        /// 테스트 모드가 아니거나 순서가 맞지 않으면 false를 반환한다.
        /// </summary>
        public bool TryAdvanceTestOnboarding(int chapter)
        {
            if (!_isTestMode || chapter != _testOnboardingChapter + 1) return false;
            _testOnboardingChapter = chapter;
            LocalTelemetry.RecordUsabilityEvent("onboarding_shown", "onboarding", new Dictionary<string, object>
            {
                ["chapter"] = chapter,
            });
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
            if (cost < 0 || (!_isDebugEnabled && _save.UpgradePoints < cost))
            {
                reasonKey = "tree.insufficientPoints";
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

        /// <summary>작업실의 라운드·진입 경로·보유 룬·강화와 의미상 빌드 비교키를 복사해 반환한다.</summary>
        public Dictionary<string, object> GetWorkshopLogSnapshot()
        {
            return new Dictionary<string, object>
            {
                ["stage"] = _isRunSuspended ? _workshopStage : SelectedStage,
                ["round"] = _isRunSuspended ? (object)_workshopRound : null,
                ["entry_reason"] = _isRunSuspended ? "round_break" : "workshop",
                ["inventory"] = GetInventoryLogSnapshot(),
                ["skill_tree"] = GetSkillTreeLogSnapshot(),
                ["comparison_key"] = LocalTelemetry.GetUsabilityGraphSignature(_spells.Graph),
                ["node_count"] = _spells.Graph.Nodes.Count, ["edge_count"] = _spells.Graph.Edges.Count,
            };
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
                ["max_hp"] = MaxHp,
                ["max_energy"] = MaxEnergy,
                ["energy_regen"] = EnergyRegen,
                ["damage_multiplier"] = DamageMultiplier,
                ["move_speed_multiplier"] = MoveSpeedMultiplier,
            };
        }

        /// <summary>선행 조건을 만족하는 트리 노드를 강화 포인트로 구매하고 효과·룬 해금·속성 선택 대기·저장을 반영한다.</summary>
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
            int pointsBefore = _save.UpgradePoints;
            if (!_save.TrySpendUpgradePoints(cost))
            {
                SetStatus("tree.insufficientPoints");
                return false;
            }
            _save.SetUpgradeNodeLevel(nodeId, currentLevel + 1);
            if (node.EffectType == UpgradeEffectType.RuneUnlock) _save.Unlock(node.RuneId);
            if (node.EffectType == UpgradeEffectType.ElementPick) _save.SetElementPickPending();
            PersistProgress();
            _spells.Recompile();
            LocalTelemetry.RecordUsabilityEvent("upgrade_purchase", "workshop", new Dictionary<string, object>
            {
                ["node_id"] = nodeId, ["level_before"] = currentLevel, ["level_after"] = currentLevel + 1,
                ["cost"] = cost, ["points_before"] = pointsBefore, ["points_after"] = _save.UpgradePoints,
                ["skill_tree"] = GetSkillTreeLogSnapshot(),
            });
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

        /// <summary>현재 마법을 컴파일 규칙으로 검증해 지정 슬롯에 장착한다.</summary>
        public void Equip(int slot)
        {
            if (slot != 0) return;
            _spells.Recompile();
            if (!_spells.CompileResult.Ok) { SetStatus("editor.invalidEquip"); return; }
            var editingId = _spells.Graph.Id;
            _spells.Save();
            _save.SetLoadout(slot, editingId);
            PersistProgress();
        }

        /// <summary>screenShake 또는 hitStop 설정을 기기 공통 환경설정에 저장한다.</summary>
        public void SetSetting(string key, bool enabled)
        {
            SaveStore.SetFeedbackSetting(key, enabled);
        }

        /// <summary>진행을 초기화하고 새 시작 마법으로 편집 세션을 다시 연다.</summary>
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
            _hasEnteredTestEditorDuringWorkshop = false;
            _save = PlayerSave.CreateNew();
            if (_isDebugEnabled) PrepareDebugSave();
            else if (_isTestMode) RestorePurchasedRuneUnlocks(_save);
            else _normalSave = _save;
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

        /// <summary>마법을 저장·컴파일하고 최대 전력 제한을 만족하면 출격할 마법을 반환한다. 유지형 빔은 첫 시전 비용으로 검사하며 시작 노드별 결과 각각에 전력 검사를 적용한다. 불가하면 상태 문구를 남기고 false를 반환한다.</summary>
        public bool TryPrepareMission(out CompiledSpell spell)
        {
            _spells.Save();
            _spells.Recompile();
            if (!_spells.CompileResult.Ok)
            {
                SetStatus("editor.invalidEquip");
                spell = null;
                return false;
            }
            if (!IsEntriesWithinMaxEnergy())
            {
                spell = null;
                return false;
            }
            spell = _spells.CompileResult.Spell;
            return true;
        }

        /// <summary>
        /// 시작 노드별 컴파일 결과(Entries) 각각의 필요 전력이 최대 전력 이하인지 검사한다. 유지형 빔 판단은 Entry의 Trigger가 onAttack일 때만 하고,
        /// 대시 종료 Entry는 밸런스의 DashEndCostMultiplier를 곱한 값으로 비교한다. 하나라도 넘으면 editor.energyBlocked 문구를 남기고 false를 반환한다.
        /// </summary>
        private bool IsEntriesWithinMaxEnergy()
        {
            IReadOnlyList<CompiledSpell> entries = _spells.CompileResult.Entries;
            if (entries == null || entries.Count == 0) entries = new[] { _spells.CompileResult.Spell };
            foreach (CompiledSpell entry in entries)
            {
                if (entry == null) continue;
                bool hasHeldBeam = entry.Trigger == SpellGrammar.TRIGGER_ON_ATTACK
                    && entry.Root.Any(action => action.Form == BeamBehaviorRules.BEAM_KIND);
                double requiredEnergy = hasHeldBeam
                    ? entry.GetCastCosts(GameData.Balance.Sim.ExecutionTimeScale).GetAmount("mana") : entry.EnergyCost;
                if (entry.Trigger == SpellGrammar.TRIGGER_ON_DASH_END) requiredEnergy *= GameData.Balance.Player.DashEndCostMultiplier;
                if (requiredEnergy <= MaxEnergy) continue;
                SetStatusText(string.Format(GameData.L("editor.energyBlocked"), requiredEnergy, MaxEnergy));
                return false;
            }
            return true;
        }

        /// <summary>
        /// 전투 보상과 처치 기록을 세이브에 반영하고, 연속 미션은 기존 스테이지 선택 기록 대신 별도 완주 기록을 남긴다.
        /// 클리어 때 지급한 룬 보상 목록을 반환한다.
        /// </summary>
        public IReadOnlyList<ModifierRewardDefinition> SettleMission(int stageNumber, int fragments, bool isCleared, IEnumerable<KeyValuePair<string, int>> killCounts,
            ModifierRewardDefinition clearRuneReward = null, bool isProgressionMission = false,
            IReadOnlyList<string> progressionChoiceIds = null, IReadOnlyList<string> progressionChoiceGrades = null)
        {
            var modifierRewards = new List<ModifierRewardDefinition>();
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
        /// 새 런은 라운드 분류의 다음 보상 등급을 소비하므로 속성 후보의 위치와 무관하다. persist이면 후보와 소비 상태를 함께 기록하고 아이템이 늘면 다시 컴파일한다.
        /// </summary>
        public void CommitRoundRewards(int fragments, IReadOnlyList<SimulationItemDrop> drops, int stageNumber, int roundNumber,
            double hp, double maxHp, IReadOnlyList<string> roundChoiceIds, IReadOnlyList<string> roundChoiceGrades, bool persist = true,
            string rewardGrade = null)
        {
            if (_save.UsesFixedRewardGrades && roundChoiceIds != null && roundChoiceIds.Count > 0)
            {
                string category = GameData.Stages.GetRoundRewardCategory(stageNumber, roundNumber, GameData.Drops);
                if (!_save.TryGetNextRewardGrade(category, out string nextRewardGrade))
                    throw new InvalidOperationException("라운드 선택 보상 등급 목록이 소진되었습니다.");
                _save.ConsumeRewardGrade(category, nextRewardGrade);
            }
            _save.Settle(fragments, false);
            _save.GrantUpgradePoints(GameData.Stages.GetRoundUpgradePoints(stageNumber, roundNumber));
            foreach (SimulationItemDrop drop in drops)
                GrantRune(drop.RuneId, drop.Grade, drop.Count, "combat_pickup", persist: false);
            if (drops.Count > 0) _spells.Recompile();
            _save.RecordProgressionRound(stageNumber, roundNumber, hp, maxHp, roundChoiceIds, roundChoiceGrades, rewardGrade);
            if (persist) PersistProgress();
        }

        /// <summary>현재 라운드의 미사용 카드 index 하나만 교체하고 후보·카드별 사용 상태·열람 이력을 함께 저장한다. 저장 실패 시 이전 상태를 복원하고 false를 반환한다.</summary>
        public bool TrySaveRerolledRoundChoice(int stage, int round, int index, string runeId, string grade, string rewardGrade)
        {
            if (_save.ProgressionStage != stage || _save.ProgressionRound != round
                || index < 0 || index >= _save.PendingRoundChoiceIds.Count || _save.HasRerolledRoundChoice(index)
                || _save.PendingRoundChoiceIds.Contains(runeId) || string.IsNullOrEmpty(rewardGrade)
                || !RuneRewardRoller.IsCandidateGradeAvailable(GameData.Runes, GameData.ModifierGrades, runeId, grade)) return false;
            string tableId = GameData.Stages.GetProgressionStage(stage).Rounds[round - 1].RewardDropTable;
            DropTableDefinition table = GameData.Drops.Get(tableId);
            string category = GameData.Stages.GetRoundRewardCategory(stage, round, GameData.Drops);
            string expectedRewardGrade = _save.PendingRoundRewardGrade;
            if (string.IsNullOrEmpty(expectedRewardGrade))
                expectedRewardGrade = RuneRewardRoller.ResolveChoiceGrade(table, _save.PendingRoundChoiceIds, _save.PendingRoundChoiceGrades);
            string expectedGrade = GameData.Runes.Get(runeId).Category == SpellGrammar.CATEGORY_ELEMENT ? "" : rewardGrade;
            if (rewardGrade != expectedRewardGrade || grade != expectedGrade
                || !RuneRewardRoller.CollectChoiceCandidates(table, category, rewardGrade, GameData.Runes,
                    GameData.ModifierGrades, null).Exists(candidate => candidate.Key == runeId && candidate.Value > 0)) return false;
            IReadOnlyList<string> previousIds = _save.PendingRoundChoiceIds;
            IReadOnlyList<string> previousGrades = _save.PendingRoundChoiceGrades;
            IReadOnlyList<string> previousSeenIds = _save.PendingRoundSeenRuneIds;
            string previousRewardGrade = _save.PendingRoundRewardGrade;
            var previousRerolls = new bool[previousIds.Count];
            for (int slot = 0; slot < previousRerolls.Length; slot++) previousRerolls[slot] = _save.HasRerolledRoundChoice(slot);
            var ids = new List<string>(previousIds);
            var grades = new List<string>(previousGrades);
            var rerolls = (bool[])previousRerolls.Clone();
            var seenIds = new List<string>(previousSeenIds);
            ids[index] = runeId;
            grades[index] = grade;
            rerolls[index] = true;
            if (!seenIds.Contains(runeId)) seenIds.Add(runeId);
            bool wasSaveDirty = _isSaveDirty;
            _save.SetPendingRoundChoice(ids, grades, rewardGrade, rerolls, seenIds);
            if (!IsMemorySession && !WriteCurrentSave(_save))
            {
                _save.SetPendingRoundChoice(previousIds, previousGrades, previousRewardGrade, previousRerolls, previousSeenIds);
                _isSaveDirty = wasSaveDirty;
                SetStatusText(SaveStore.LastWarning);
                return false;
            }
            return true;
        }

        /// <summary>디버그 진입 라운드 앞의 고정 보상을 지급 없이 소비하고 대기 후보를 지운 뒤 저장 정책에 따라 보관한다.</summary>
        public void SkipRoundRewardsBefore(int stage, int round)
        {
            if (!_isDebugEnabled) return;
            _save.SkipRewardGradesBefore(stage, round);
            PersistProgress();
        }

        /// <summary>저장된 연속 미션 보상 후보 index를 하나 지급하고 후보 삭제와 소지량 변경을 한 번에 저장한다.</summary>
        public bool ClaimProgressionChoice(int index)
        {
            if (index < 0 || index >= _save.PendingProgressionChoiceIds.Count) return false;
            string runeId = _save.PendingProgressionChoiceIds[index];
            string grade = _save.PendingProgressionChoiceGrades[index];
            if (!RuneRewardRoller.IsCandidateGradeAvailable(GameData.Runes, GameData.ModifierGrades, runeId, grade)) return false;
            RuneDefinition rune = GameData.Runes.Get(runeId);
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

        /// <summary>라운드 보상 후보 행동·Modifier 룬을 소지량에 즉시 반영하고 대기 후보를 비운 뒤 한 번에 저장하고 다시 컴파일한다.</summary>
        public void GrantRoundChoice(string runeId, string grade)
        {
            if (!RuneRewardRoller.IsCandidateGradeAvailable(GameData.Runes, GameData.ModifierGrades, runeId, grade)) return;
            RuneDefinition rune = GameData.Runes.Get(runeId);
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

        /// <summary>트리 속성 선택 보상으로 고른 속성 룬을 지급하고 대기 상태를 해제한 뒤 한 번에 저장하고 다시 컴파일한다. 대기가 없으면 무시한다.</summary>
        public void ChooseElementPick(string runeId)
        {
            if (!_save.HasPendingElementPick) return;
            GrantRune(runeId, "", 1, "tree_element_pick", persist: false);
            _save.ClearElementPickPending();
            PersistProgress();
            _spells.Recompile();
        }

        /// <summary>룬 소지량을 늘리고 persist가 참이면 저장 정책에 맞춰 기록한다.</summary>
        public void GrantRune(string runeId, string grade, int count, string reason = "unspecified", bool persist = true)
        {
            if (count <= 0 || !GameData.Runes.TryGet(runeId, out RuneDefinition rune)
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
            int currencyBefore = _save.Currency;
            bool isSuccess = TrySynthesizeRunesCore(materials, grade, out resultId, out resultGrade, out error);
            LocalTelemetry.RecordUsabilityEvent("rune_synthesis", "workshop", new Dictionary<string, object>
            {
                ["materials"] = materials == null ? null : new List<string>(materials), ["material_grade"] = grade,
                ["result"] = isSuccess ? "success" : "rejected", ["rune_id"] = resultId, ["grade"] = resultGrade,
                ["error"] = error, ["currency_before"] = currencyBefore, ["currency_after"] = _save.Currency,
                ["inventory"] = GetInventoryLogSnapshot(),
            });
            return isSuccess;
        }

        /// <summary>재료·등급과 재화를 검증해 합성을 실행하고 결과 룬 또는 실패 사유를 반환한다. 저장 실패 시 소비를 복구한다.</summary>
        private bool TrySynthesizeRunesCore(IReadOnlyList<string> materials, string grade,
            out string resultId, out string resultGrade, out string error)
        {
            resultId = null;
            resultGrade = null;
            error = "같은 분류와 등급의 사용하지 않는 룬 3개가 필요합니다.";
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

        /// <summary>새 디버그 진행에 룬을 전부 지급하고 1스테이지 1라운드를 끝낸 라운드 대기 기록을 최대 체력으로 남긴다. 미션은 이 기록으로 대기 상태에서 시작한다.</summary>
        private void PrepareDebugSave()
        {
            UnlockDebugRunes();
            _save.RecordProgressionRound(1, 1, MaxHp, MaxHp, null, null);
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

        /// <summary>해당 강화 유형에 속한 트리 노드의 저장 레벨까지 효과량을 합산한다.</summary>
        public float GetUpgradeTreeEffectTotal(UpgradeEffectType effectType)
        {
            float total = 0;
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.Nodes)
                if (node != null && node.EffectType == effectType)
                    total += node.GetTotalAmount(GetUpgradeNodeLevel(node.Id));
            return total;
        }

        /// <summary>해당 효과 유형의 트리 노드 중 레벨 1 이상인 노드가 있는지 반환한다. 플래그형 강화 판정에 쓴다.</summary>
        public bool HasUpgradeEffect(UpgradeEffectType effectType)
        {
            foreach (UpgradeTreeNodeDefinition node in _upgradeTree.Nodes)
                if (node != null && node.EffectType == effectType && GetUpgradeNodeLevel(node.Id) >= 1)
                    return true;
            return false;
        }

        /// <summary>현재 트리 레벨로 합산한 능력치와 효과 플래그를 시뮬레이션 전달용 묶음으로 만들어 반환한다.</summary>
        public PlayerUpgradeStats CreateUpgradeStats()
        {
            return new PlayerUpgradeStats(MaxHp, MaxEnergy, EnergyRegen, DamageMultiplier, MoveSpeedMultiplier,
                ScrapGainMultiplier, ScrapPickupRangeMultiplier, ScrapHealRatio, KillEnergyBonus,
                elementDamageBonus: GetUpgradeTreeEffectTotal(UpgradeEffectType.ElementDamage),
                statusDurationBonus: GetUpgradeTreeEffectTotal(UpgradeEffectType.StatusDuration),
                dashCooldownReduction: GetUpgradeTreeEffectTotal(UpgradeEffectType.DashCooldownReduction),
                dashInvulnerabilityBonus: GetUpgradeTreeEffectTotal(UpgradeEffectType.DashInvulnerability),
                evasionChance: GetUpgradeTreeEffectTotal(UpgradeEffectType.Evasion),
                nearDamageBonus: GetUpgradeTreeEffectTotal(UpgradeEffectType.NearDamage),
                farDamageBonus: GetUpgradeTreeEffectTotal(UpgradeEffectType.FarDamage),
                fullManaDamageBonus: GetUpgradeTreeEffectTotal(UpgradeEffectType.FullManaDamage),
                postDashDamageBonus: GetUpgradeTreeEffectTotal(UpgradeEffectType.PostDashDamage),
                stationaryRegenBonus: GetUpgradeTreeEffectTotal(UpgradeEffectType.StationaryRegen),
                hasDashTeleport: HasUpgradeEffect(UpgradeEffectType.DashTeleport),
                hasDashCollisionGuard: HasUpgradeEffect(UpgradeEffectType.DashCollisionGuard),
                hasPlayerAura: HasUpgradeEffect(UpgradeEffectType.PlayerAura),
                hasArcEnhance: HasUpgradeEffect(UpgradeEffectType.ArcEnhance),
                hasIceEnhance: HasUpgradeEffect(UpgradeEffectType.IceEnhance),
                hasFireEnhance: HasUpgradeEffect(UpgradeEffectType.FireEnhance));
        }
    }
}
