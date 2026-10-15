using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using UnityEngine;

namespace RuneCode
{
    public sealed class RuneSimulation
    {
        public const int TICK_RATE = CompiledSpell.SIMULATION_TICK_RATE;
        public const int ARC_LINK_TICKS = 12;
        private const int RELAY_REGEN_INTERVAL_TICKS = 60;
        private const double MIN_KILL_ENERGY = 1;
        internal const double STEP_SECONDS = 1.0 / TICK_RATE;
        private const double DEGREES_TO_RADIANS = Math.PI / 180;
        private const int STATUS_TYPE_COUNT = 4;
        private const int PLAYER_TARGET_ID = -1;
        private const double SAME_SPAWN_DISTANCE = 0.5;
        private const string BOLT_RUNE_ID = "form.bolt";
        private const double BEAM_CLIP_MIN_STEP = 4;
        private const double BEAM_CLIP_STEP_RATIO = 0.25;
        private const int BEAM_CLIP_REFINEMENT_STEPS = 6;
        private const double EXPLOSION_VISUAL_SECONDS = 0.25;
        internal const double COLLISION_EPSILON = 0.000001;
        private const int WALL_CONTACT_REFINEMENT_STEPS = 12;
        private const double ORBIT_CONTACT_OVERLAP = 1;
        internal static readonly Dictionary<int, double> NoExcludedTargets = new Dictionary<int, double>();
        private readonly BalanceData _balance;
        private readonly EnemyCatalog _enemyCatalog;
        private readonly SectorDefinition _sector;
        private readonly BossCatalog _bosses;
        private readonly FormationCatalog _formations;
        private readonly DropCatalog _drops;

        /// <summary>엘리트 Modifier 등급표 조회 창구다. B 워커의 정식 로더로 교체할 지점은 생성자다.</summary>
        private readonly IEliteModifierDropSource _eliteDrops;
        private readonly StageCatalog _stages;
        private readonly CarrierDefinition _carrier;
        private readonly Queue<SimulationEnemy> _deadEnemies = new Queue<SimulationEnemy>();
        private readonly bool _isMission;
        private readonly int _stageNumber;
        private readonly double _maxHp;
        private readonly double _maxEnergy;
        private readonly double _energyRegen;
        private readonly double _damageMultiplier;
        private readonly double _moveSpeedMultiplier;
        private readonly double _scrapGainMultiplier;
        private readonly double _scrapPickupRangeMultiplier;
        private readonly double _killEnergyBonus;
        private readonly List<SimulationEnemy> _enemies = new List<SimulationEnemy>();
        private readonly List<SimulationSpellEntity> _spellEntities = new List<SimulationSpellEntity>();
        private readonly List<SimulationProjectile> _enemyProjectiles = new List<SimulationProjectile>();
        private readonly List<FragmentOrb> _orbs = new List<FragmentOrb>();
        private readonly List<SimulationItemDrop> _itemDrops = new List<SimulationItemDrop>();
        private readonly List<SimulationItemDrop> _pickedUpItems = new List<SimulationItemDrop>();
        private readonly List<SimulationItemDrop> _newPickedItems = new List<SimulationItemDrop>();
        private readonly List<DamageNumber> _damageNumbers = new List<DamageNumber>();
        private readonly List<HealPulse> _healPulses = new List<HealPulse>();
        private readonly List<ArcLink> _arcLinks = new List<ArcLink>();
        private readonly List<NodeExecutionEvent> _nodeEvents = new List<NodeExecutionEvent>();
        private readonly List<ScheduledExecution> _scheduled = new List<ScheduledExecution>();
        private readonly List<PendingEnemyShot> _pendingEnemyShots = new List<PendingEnemyShot>();
        private readonly List<PendingSplit> _pendingSplits = new List<PendingSplit>();
        private readonly List<EnemyStatusEffect> _enemyStatuses = new List<EnemyStatusEffect>();
        private readonly Dictionary<int, EnemyStatusEffect> _statusIndex = new Dictionary<int, EnemyStatusEffect>();
        private readonly Queue<DamageSample> _damageWindow = new Queue<DamageSample>();
        private readonly Dictionary<string, int> _killCounts = new Dictionary<string, int>();
        private readonly CompiledSpell[] _loadout = new CompiledSpell[3];
        private readonly List<string> _unlockedElements = new List<string> { "fire" };
        private readonly List<SimVector> _tickSpawnPositions = new List<SimVector>();
        private readonly SpawnSlots _spawnSlots;
        private readonly EnemySeparation _separation = new EnemySeparation();
        private readonly Func<SimulationEnemy, bool> _isSeparationFixed;
        private readonly int _initialSeed;
        private uint _rngState;
        private SimulationPlayer _player;
        private MissionMap _map;
        private MissionStage _stage;
        private int _tick;
        private double _statusTime;
        private int _nextEntityId;
        private StageDefinition _stageDefinition;
        private SpawnDirector _director;
        private double _spawnRadius;
        private int _tickSpawnPositionsTick = -1;
        private SimVector _moveDirection;
        private bool _hasMoveDirection;
        private int _streamSpawns;
        private int _spawnedEnemies;
        private int _killCount;
        private int _killTarget;
        private int _targetKills;
        private bool _isBossDefeated;
        private int _actionBudgetTick = -1;
        private int _actionsThisTick;
        private int _droppedExecutions;
        private int _collectedFragments;
        private double _scrapRemainder;
        private double _totalDamage;
        private double _statusDamage;
        private double _persistDamage;
        private double _rollingDamage;
        private readonly Dictionary<string, double> _resourceCostsSpent = new Dictionary<string, double>(StringComparer.Ordinal);
        private int _peakSpellEntities;
        private int _nodeExecutionCount;
        private string _scenario;
        private bool _debugInvulnerable;
        private string[] _lastPatchTags = Array.Empty<string>();
        public int Tick => _tick;
        public double Time => _tick * STEP_SECONDS;
        public SimulationPlayer Player => _player;
        public IReadOnlyList<SimulationEnemy> Enemies => _enemies;
        public IReadOnlyList<SimulationSpellEntity> SpellEntities => _spellEntities;
        public IReadOnlyList<SimulationProjectile> EnemyProjectiles => _enemyProjectiles;
        public IReadOnlyList<FragmentOrb> Orbs => _orbs;

        /// <summary>바닥에 떨어져 줍기를 기다리는 Modifier 드롭 목록이다.</summary>
        public IReadOnlyList<SimulationItemDrop> ItemDrops => _itemDrops;

        /// <summary>이번 전투에서 주워 확정한 Modifier 드롭의 누적 목록이다.</summary>
        public IReadOnlyList<SimulationItemDrop> PickedUpItems => _pickedUpItems;
        public IReadOnlyList<DamageNumber> DamageNumbers => _damageNumbers;
        public IReadOnlyList<HealPulse> HealPulses => _healPulses;

        /// <summary>최근 ARC_LINK_TICKS 동안 발생한 전격 연쇄 구간 목록이다. 표시 전용이다.</summary>
        public IReadOnlyList<ArcLink> ArcLinks => _arcLinks;
        public IReadOnlyList<NodeExecutionEvent> NodeEvents => _nodeEvents;
        public IReadOnlyDictionary<string, int> KillCounts => _killCounts;
        public AdaptationNet Adaptation { get; }
        public MissionMap Map => _map;

        /// <summary>전투 규칙 수치다. 행동 실행 동작이 다중 배치 간격 등을 읽는다.</summary>
        internal BalanceData Balance => _balance;
        public MissionStage Stage => _stage;
        public bool Completed => _stage == MissionStage.Cleared;
        public bool IsDead => _stage == MissionStage.Dead;
        public bool IsTimedOut => _stage == MissionStage.TimedOut;
        public bool IsFailed => IsDead || IsTimedOut;
        public bool IsTerminal => _stage == MissionStage.Terminal;
        public bool IsTimedBattle => _isMission;
        public int StageNumber => _stageNumber;
        public bool IsBossStage => _stageDefinition != null && _stageDefinition.IsBoss;
        public double TimeLimit => _stageDefinition?.TimeLimit ?? 0;
        public double RemainingTime => IsBossStage ? Math.Max(0, TimeLimit - Time) : 0;
        public int KillTarget => _killTarget;
        public int TargetKills => _targetKills;
        public SimVector MapCenter => new SimVector(_map.Width * 0.5, _map.Height * 0.5);
        public double EnergyRegen => _energyRegen;
        public int SpawnedEnemies => _spawnedEnemies;
        public int KillCount => _killCount;
        public int EnemyLimit => _balance.Limits.MaxEnemies;
        public int DroppedExecutions => _droppedExecutions;
        public int RoomIndex => _stageNumber - 1;
        public string RoomName => _isMission ? GameData.L("incremental.stage") + " " + _stageNumber : GameData.L("scenario." + _scenario);
        public int CollectedFragments => _collectedFragments;
        public int Fragments => _collectedFragments;
        /// <summary>플레이어가 직접 줍거나 스테이지 클리어 시 회수한 스크랩 누계를 반환한다.</summary>
        public int EarnedFragments => _collectedFragments;
        public int ClearReward => Completed && _stageDefinition != null ? _stages.Growth.GetClearReward(_stageDefinition.ClearReward, _stageNumber) : 0;
        public int SettlementFragments => IsFailed ? (int)Math.Round(_collectedFragments * _balance.Economy.DeathRetention, MidpointRounding.AwayFromZero) : _collectedFragments + ClearReward;
        public double TotalDamage => _totalDamage;

        /// <summary>상태 이상(화염) 틱으로 입힌 실제 피해의 누적값이며 TotalDamage에 포함된다.</summary>
        public double StatusDamage => _statusDamage;
        /// <summary>잔류(Persist) 판정 틱으로 입힌 실제 피해의 누적값이며 연쇄 전격을 포함하고 TotalDamage에 포함된다.</summary>
        public double PersistDamage => _persistDamage;
        public double RollingDps => _rollingDamage / Math.Min(5, Math.Max(STEP_SECONDS, Time));
        public double EnergySpent => _resourceCostsSpent.TryGetValue("mana", out double amount) ? amount : 0;
        public IReadOnlyDictionary<string, double> ResourceCostsSpent => _resourceCostsSpent;
        public int PeakSpellEntities => _peakSpellEntities;
        public int NodeExecutionCount => _nodeExecutionCount;
        public IReadOnlyList<string> LastPatchTags => _lastPatchTags;
        public bool AdaptationEnabled => Adaptation.Enabled;
        public bool DebugInvulnerable => _debugInvulnerable;
        /// <summary>저격수의 저장된 조준 경로를 벽과 공격 사거리로 제한한 끝점을 반환한다.</summary>
        public SimVector GetSniperEnd(SimulationEnemy enemy) => enemy.AttackOrigin + enemy.AimedDirection * ClipBeamLength(enemy.AttackOrigin, enemy.AimedDirection, enemy.Definition.AttackRange);
        /// <summary>시험 도크의 적응 학습 및 피해 감쇠 사용 여부를 변경한다.</summary>
        public void SetAdaptationEnabled(bool isEnabled) { Adaptation.SetEnabled(isEnabled); }

        /// <summary>명시적으로 열린 디버그 기능에서 플레이어 무적 상태를 변경한다.</summary>
        public void SetDebugInvulnerable(bool isInvulnerable) { _debugInvulnerable = isInvulnerable; }

        /// <summary>시드, 스테이지와 트리 능력치·처치 전력 보너스를 받아 독립 전투 또는 시험 도크를 생성한다. 생략한 배율은 1, 보너스는 0을 사용한다.</summary>
        public RuneSimulation(int seed = 1, bool isMission = false, double maxHp = 0, double maxEnergy = 0, int stageNumber = 1, double energyRegen = 0,
            double damageMultiplier = 1, double moveSpeedMultiplier = 1, double scrapGainMultiplier = 1, CarrierDefinition carrier = null,
            double scrapPickupRangeMultiplier = 1, double killEnergyBonus = 0)
        {
            GameData.Load();
            _killEnergyBonus = Math.Max(0, killEnergyBonus);
            _balance = GameData.Balance;
            _enemyCatalog = EnemyCatalog.FromJson(LoadJson("enemies"));
            _carrier = carrier;
            _carrier?.Validate(_enemyCatalog);
            _sector = SectorDefinition.FromJson(LoadJson("sector1"), _enemyCatalog);
            _formations = FormationCatalog.FromJson(LoadJson("formations"));
            _bosses = BossCatalog.FromJson(LoadJson("bosses"), _enemyCatalog, _formations);
            _drops = DropCatalog.FromJson(LoadJson("drops"));
            _eliteDrops = new ModifierGradeDropSource(GameData.ModifierGrades);
            _stages = StageCatalog.FromJson(LoadJson("stages"), _enemyCatalog, _formations, _drops);
            _isMission = isMission; _initialSeed = seed; _rngState = (uint)seed; _maxHp = maxHp > 0 ? maxHp : _balance.Player.Hp; _maxEnergy = maxEnergy > 0 ? maxEnergy : _balance.Player.Energy;
            _stageNumber = Math.Max(1, stageNumber);
            _energyRegen = energyRegen > 0 ? energyRegen : _balance.Player.EnergyRegen;
            _damageMultiplier = damageMultiplier;
            _moveSpeedMultiplier = moveSpeedMultiplier;
            _scrapGainMultiplier = scrapGainMultiplier;
            _scrapPickupRangeMultiplier = scrapPickupRangeMultiplier;
            _spawnSlots = new SpawnSlots(NextRandom);
            _isSeparationFixed = IsSeparationFixed;
            Adaptation = new AdaptationNet(_balance);
            if (isMission) BeginTimedBattle();
            else ResetBench("dummy_single");
        }

        /// <summary>스테이지·진형·드롭 JSON을 읽고 검증한 스테이지 목록을 반환한다. 앱의 출격 정보 표시에 쓴다.</summary>
        public static StageCatalog LoadStageCatalog()
        {
            GameData.Load();
            EnemyCatalog enemies = EnemyCatalog.FromJson(LoadJson("enemies"));
            DropCatalog drops = DropCatalog.FromJson(LoadJson("drops"));
            return StageCatalog.FromJson(LoadJson("stages"), enemies, FormationCatalog.FromJson(LoadJson("formations")), drops);
        }

        /// <summary>Resources에 포함된 JSON 텍스트를 읽고 누락된 필수 데이터 오류를 보고한다.</summary>
        private static string LoadJson(string name)
        { TextAsset asset = Resources.Load<TextAsset>("RuneCode/" + name); if (asset == null) throw new FormatException("누락된 룬 코드 데이터: " + name); return asset.text; }

        /// <summary>전투에서는 하나의 마법, 시험 도크에서는 최대 3슬롯의 컴파일 마법을 다음 시전부터 사용한다.</summary>
        public void SetLoadout(IReadOnlyList<CompiledSpell> spells)
        { for (int i = 0; i < _loadout.Length; i++) _loadout[i] = i < spells.Count && (!_isMission || i == 0) ? spells[i] : null; }

        /// <summary>해금한 속성 태그를 노이즈의 무작위 후보로 설정하고 기본 화염 후보를 보존한다.</summary>
        public void SetUnlockedElements(IReadOnlyList<string> elements)
        {
            _unlockedElements.Clear();
            for (int i = 0; i < elements.Count; i++)
            { string tag = elements[i].StartsWith("elem.", StringComparison.Ordinal) ? elements[i].Substring(5) : elements[i]; if ((tag == "fire" || tag == "ice" || tag == "arc") && !_unlockedElements.Contains(tag)) _unlockedElements.Add(tag); }
            if (_unlockedElements.Count == 0) _unlockedElements.Add("fire");
        }

        /// <summary>아직 지급하지 않은 새로 주운 Modifier 드롭 목록을 반환하고 미지급 목록을 비운다.</summary>
        public List<SimulationItemDrop> TakeNewPickedItems()
        {
            var items = new List<SimulationItemDrop>(_newPickedItems);
            _newPickedItems.Clear();
            return items;
        }

        /// <summary>
        /// 보스 스테이지 클리어 Modifier 선택 정보와 결정적 난수로 후보와 후보별 등급을 뽑아 반환한다. 선택 보상이 없으면 false를 반환한다.
        /// 슬롯별 등급 확률이 있으면 슬롯마다 등급을 따로 굴리고, 없으면 모든 슬롯이 설정 등급을 쓴다. 같은 Modifier는 다시 뽑지 않는다.
        /// </summary>
        public bool TryDrawClearModifierChoices(out EliteModifierClearChoice choice, out List<string> candidates, out List<string> grades)
        {
            choice = _eliteDrops.GetClearChoice(_stageNumber);
            candidates = new List<string>();
            grades = new List<string>();
            if (choice == null || choice.ChoiceCount <= 0) return false;
            for (int i = 0; i < choice.ChoiceCount; i++)
            {
                string grade = choice.SlotGradeChances != null ? RollSlotGrade(choice.SlotGradeChances) : choice.Grade;
                var pool = new List<string>();
                foreach (string runeId in _eliteDrops.GetModifiersWithGrade(grade))
                    if (!candidates.Contains(runeId)) pool.Add(runeId);
                // 이 등급에 남은 후보가 없으면 슬롯을 비운다.
                if (pool.Count == 0) continue;
                candidates.Add(pool[Math.Min(pool.Count - 1, (int)(NextRandom() * pool.Count))]);
                grades.Add(grade);
            }
            return candidates.Count > 0;
        }

        /// <summary>등급 확률 목록(퍼센트)으로 등급 하나를 굴려 반환한다. 확률 0인 등급은 뽑지 않는다.</summary>
        private string RollSlotGrade(IReadOnlyList<EliteModifierGradeChance> chances)
        {
            double roll = NextRandom() * 100;
            string last = null;
            foreach (EliteModifierGradeChance chance in chances)
            {
                if (chance.Percent <= 0) continue;
                last = chance.Grade;
                roll -= chance.Percent;
                if (roll < 0) return chance.Grade;
            }
            return last;
        }

        /// <summary>
        /// 시전 가능 상태와 슬롯 쿨다운을 확인하고 루트 실행을 시작한다. 마나는 선지불하지 않고 실행 노드에 도달할 때마다 차감한다(백서 7.1).
        /// </summary>
        public bool TryCast(CompiledSpell spell, int slot = 0)
        {
            if (spell == null || slot < 0 || slot >= _loadout.Length || (_stage != MissionStage.Bench && _stage != MissionStage.Combat)) return false;
            if (_isMission && slot != 0) return false;
            double executionSeconds = spell.GetEstimatedExecutionSeconds(_balance.Sim.ExecutionTimeScale);
            double cooldownSeconds = spell.Cooldown + executionSeconds;
            if (!_player.TryBeginSpell(slot, executionSeconds, cooldownSeconds)) return false;
            RecordNode(spell.CoreNodeId);
            var rootCast = new RootSpellExecution(slot);
            var context = new SpellContext(_player.Position + _player.AimDirection * (_sector.PlayerRadius + _balance.Sim.WandOffset), _player.AimDirection, null, false, null,
                caster: _player, randomState: new SpellRandomState());
            var tracker = new ExecutionTracker(rootCast, () => CompleteRootSpell(rootCast));
            ExecuteRootBranches(spell.Root, context, tracker);
            return true;
        }

        /// <summary>선택 Trigger와 일치하는 슬롯 마법을 시전하고 성공 여부를 반환한다.</summary>
        private bool TryCastTriggered(CompiledSpell spell, string trigger, int slot)
        {
            return spell != null && spell.Trigger == trigger && TryCast(spell, slot);
        }

        /// <summary>명시한 방향으로 조준을 갱신한 뒤 슬롯 마법을 시전하고 성공 여부를 반환한다.</summary>
        public bool TryCast(CompiledSpell spell, SimVector aimDirection, int slot = 0) { _player.Aim(aimDirection); return TryCast(spell, slot); }

        /// <summary>루트 체인의 모든 노드와 완료 분기가 끝난 슬롯에 쿨다운을 시작한다.</summary>
        private void CompleteRootSpell(RootSpellExecution rootCast)
        {
            _player.FinishSpell(rootCast.Slot);
        }

        /// <summary>입력, 예약된 적 스폰, 전투, 적응 및 전투 종료(전부 처치·사망·보스 제한시간)를 정확히 한 60Hz 틱만큼 갱신한다.</summary>
        public void Step(SimulationInput input)
        {
            if (Completed || IsFailed) return;
            bool wasDashing = _player.DashRemaining > 0.000001;
            _player.Aim(input.AimDirection);
            _player.Advance(STEP_SECONDS, Time, _energyRegen);
            bool startedDash = MovePlayer(input);
            if (startedDash) TryCastTrigger(SpellGrammar.TRIGGER_ON_DASH_START);
            else if (wasDashing && _player.DashRemaining <= 0.000001) TryCastTrigger(SpellGrammar.TRIGGER_ON_DASH_END);
            if (input.Movement.LengthSquared > 0.000001) TryCastTrigger(SpellGrammar.TRIGGER_ON_MOVE);
            if (_isMission) { AdvanceSpawns(); AdvanceSplits(); }
            RunScheduled();
            if (input.CastA) TryCastTriggered(_loadout[0], SpellGrammar.TRIGGER_ON_ATTACK, 0);
            if (input.CastB) TryCastTriggered(_loadout[1], SpellGrammar.TRIGGER_ON_ATTACK, 1);
            if (input.CastC) TryCastTriggered(_loadout[2], SpellGrammar.TRIGGER_ON_ATTACK, 2);
            AdvanceStatuses();
            AdvanceRelayRegen();
            AdvanceEnemies();
            if (_isMission) _separation.Apply(_enemies, _map, _balance.Combat, _isSeparationFixed);
            RunEnemyShots();
            AdvanceSpellEntities();
            AdvanceHostileProjectiles();
            CleanupEnemies();
            CollectNearbyOrbs();
            CollectNearbyItemDrops();
            if (_isMission && _player.Hp <= 0) { _stage = MissionStage.Dead; DiscardUncollectedOrbs(); CancelCombat(); }
            else if (!_isMission && _scenario == "adapt_loop" && _enemies.Count == 0) SpawnBenchDummy(new SimVector(_balance.Sim.BenchDummyX, _balance.Sim.BenchDummyY), _balance.Sim.BenchHp);
            Adaptation.Advance(Time, Time + STEP_SECONDS);
            _tick++;
            RunScheduled();
            if (_isMission && _stage == MissionStage.Combat) CheckBattleEnd();
            TrimFeedback();
        }

        /// <summary>시간제 전투에는 터미널 전환이 없으므로 진행 요청을 거부한다.</summary>
        public bool InteractTerminal() => false;

        /// <summary>전투 중단 시 예약 노드 실행을 지우고 진행 중 시전 상태를 환급 없이 취소한다.</summary>
        public void CancelPendingSpellExecutions()
        {
            _scheduled.Clear();
            _player.CancelSpellExecutions();
        }

        /// <summary>터미널의 다음 방 진행 성공 여부를 반환한다.</summary>
        public bool TryContinue() => InteractTerminal();

        /// <summary>독립 시험 도크의 시드, 상태, 통계 및 선택 시나리오 적을 초기화한다.</summary>
        public void ResetBench(string scenario)
        {
            if (_isMission) throw new InvalidOperationException("미션은 시험 도크로 초기화할 수 없습니다.");
            if (scenario != "dummy_single" && scenario != "dummy_line" && scenario != "dummy_swarm" && scenario != "aegis" && scenario != "adapt_loop") throw new ArgumentException("알 수 없는 시험 시나리오입니다.", nameof(scenario));
            _scenario = scenario; _stage = MissionStage.Bench; _tick = 0; _nextEntityId = 0; _rngState = (uint)_initialSeed;
            _enemies.Clear(); _orbs.Clear(); _itemDrops.Clear(); _pickedUpItems.Clear(); _newPickedItems.Clear(); _damageNumbers.Clear(); _healPulses.Clear(); _arcLinks.Clear(); _nodeEvents.Clear(); _damageWindow.Clear(); _killCounts.Clear(); CancelCombat();
            _enemyStatuses.Clear(); _statusIndex.Clear(); _statusTime = 0;
            _totalDamage = 0; _statusDamage = 0; _persistDamage = 0; _rollingDamage = 0; _resourceCostsSpent.Clear(); _peakSpellEntities = 0;
            _nodeExecutionCount = 0; _collectedFragments = 0; _scrapRemainder = 0; _killCount = 0; _spawnedEnemies = 0;
            _actionBudgetTick = -1; _actionsThisTick = 0; _droppedExecutions = 0;
            SimulationBalance bench = _balance.Sim;
            _map = new MissionMap(_sector.Rooms[0].Tiles, _sector.TileSize); _player = new SimulationPlayer(_maxHp, _maxEnergy, new SimVector(bench.BenchPlayerX, bench.BenchPlayerY));
            Adaptation.Clear(); Adaptation.SetEnabled(scenario == "adapt_loop");
            if (scenario == "dummy_line") for (int i = 0; i < bench.BenchLineCount; i++) SpawnBenchDummy(new SimVector(bench.BenchLineStartX + i * bench.BenchLineGap, bench.BenchDummyY), bench.BenchHp);
            else if (scenario == "dummy_swarm")
                for (int i = 0; i < bench.BenchSwarmColumns * bench.BenchSwarmRows; i++) SpawnEnemy("enemy.scout", new SimVector(bench.BenchSwarmX + (i % bench.BenchSwarmColumns) * bench.BenchSwarmGapX, bench.BenchSwarmY + (i / bench.BenchSwarmColumns) * bench.BenchSwarmGapY), false, bench.BenchHp);
            else if (scenario == "aegis") SpawnEnemy("enemy.aegis", new SimVector(bench.BenchDummyX, bench.BenchDummyY), true, bench.BenchHp);
            else SpawnBenchDummy(new SimVector(bench.BenchDummyX, bench.BenchDummyY), bench.BenchHp);
        }

        /// <summary>디버그 기능에서 지정한 적을 추가한다. 엘리트는 체력·보상 2배, 속도 1.2배, 반경 2배와 현재 스테이지의 드롭 테이블을 사용한다.</summary>
        public SimulationEnemy DebugSpawn(string id, bool isElite = false)
        {
            EliteSpawnDefinition elite = isElite ? new EliteSpawnDefinition(id, 2, 2, 1.2, 2, GetDebugEliteDropTable(id)) : null;
            return SpawnEnemy(id, _map.NearestFree(_player.Position + _player.AimDirection * 260, 24), false, 0, elite);
        }

        /// <summary>디버그 엘리트에 현재 스테이지에서 해당 종족이 쓰는 드롭 테이블을 연결하고, 없으면 첫 설정 테이블을 반환한다.</summary>
        private string GetDebugEliteDropTable(string enemyId)
        {
            IReadOnlyList<EliteSpawnDefinition> elites = _stages.Get(_stageNumber).EliteTypes;
            foreach (EliteSpawnDefinition elite in elites)
                if (elite.EnemyId == enemyId && !string.IsNullOrEmpty(elite.DropTable)) return elite.DropTable;
            foreach (EliteSpawnDefinition elite in elites)
                if (!string.IsNullOrEmpty(elite.DropTable)) return elite.DropTable;
            return null;
        }

        /// <summary>디버그 기능에서 현재 적들을 자폭 없이 처치하고 드롭을 남기되 스폰 예약과 보스 제한시간은 유지한다. 처치로 생긴 분열 대기도 함께 비운다.</summary>
        public void DebugDefeatRoom()
        { if (_isMission && _stage != MissionStage.Combat) return; foreach (SimulationEnemy enemy in _enemies) enemy.Hurt(enemy.Hp, Time); CleanupEnemies(false); _pendingSplits.Clear(); }

        /// <summary>현재 시뮬레이션의 핵심 상태를 안정적으로 해시하여 동일 입력 실행의 결정성을 비교한다.</summary>
        public string StateHash()
        {
            var state = new StringBuilder();
            state.Append(_tick).Append('|').Append(_rngState).Append('|').Append(_nextEntityId).Append('|').Append(_stage).Append('|').Append(_stageNumber).Append('|').Append(_killTarget).Append('|').Append(_targetKills).Append('|').Append(_isBossDefeated).Append('|').Append(_spawnedEnemies).Append('|').Append(_killCount).Append('|').Append(_scenario).Append('|').Append(_debugInvulnerable);
            state.Append('|').Append(_actionBudgetTick).Append('|').Append(_actionsThisTick).Append('|').Append(_droppedExecutions);
            AppendNumber(state, TimeLimit); AppendNumber(state, _energyRegen);
            AppendNumber(state, _damageMultiplier); AppendNumber(state, _moveSpeedMultiplier);
            AppendNumber(state, _scrapGainMultiplier); AppendNumber(state, _scrapPickupRangeMultiplier); AppendNumber(state, _scrapRemainder);
            _player.WriteState(state);
            foreach (SimulationEnemy enemy in _enemies) enemy.WriteState(state);
            foreach (EnemyStatusEffect status in _enemyStatuses) status.WriteState(state);
            foreach (SimulationSpellEntity entity in _spellEntities) entity.WriteState(state);
            foreach (SimulationProjectile projectile in _enemyProjectiles) projectile.WriteState(state);
            Adaptation.WriteState(state);
            foreach (ScheduledExecution pending in _scheduled)
            {
                state.Append(pending.Tick);
                if (pending.IsBranchStart)
                    state.Append("|branch:").Append(pending.Action.NodeId);
                else if (pending.IsIteration)
                {
                    state.Append("|iteration");
                    foreach (SpellAction action in pending.Actions) state.Append('|').Append(action.NodeId);
                }
                else state.Append("|finish:").Append(pending.Action.NodeId);
                AppendNumber(state, pending.Context.Origin.X); AppendNumber(state, pending.Context.Origin.Y);
                AppendNumber(state, pending.Context.Direction.X); AppendNumber(state, pending.Context.Direction.Y);
                AppendNumber(state, pending.Context.DirectionOffset);
                state.Append(pending.Context.Target?.Id ?? 0).Append('|').Append(pending.Context.FromEvent).Append('|').Append(pending.Context.NoiseElement);
                state.Append('|').Append(pending.IsIteration);
                AppendNumber(state, pending.Context.CostMultiplier);
                state.Append(pending.Context.SourceEntityId).Append('|');
                if (pending.Tracker != null)
                    state.Append("|flow:").Append(pending.Tracker.RootCast?.Slot ?? -1).Append(':').Append(pending.Tracker.Pending);
                pending.Context.Repeat?.AppendState(state);
                pending.Context.Modifiers.AppendState(state);
                pending.Context.CallEvents?.AppendState(state);
                if (pending.Context.Target != null && !pending.Context.Target.IsAlive) pending.Context.Target.WriteState(state);
            }
            foreach (FragmentOrb orb in _orbs) { AppendNumber(state, orb.Position.X); AppendNumber(state, orb.Position.Y); state.Append(orb.Amount).Append('|').Append(orb.IsAttracted); }
            foreach (SimulationItemDrop drop in _itemDrops) AppendItemDropState(state, drop);
            foreach (SimulationItemDrop drop in _pickedUpItems) AppendItemDropState(state, drop);
            foreach (CompiledSpell spell in _loadout) state.Append('|').Append(spell?.Signature);
            foreach (string element in _unlockedElements) state.Append('|').Append(element);
            foreach (PendingEnemyShot shot in _pendingEnemyShots) { state.Append(shot.Tick).Append('|').Append(shot.Owner.Id); AppendNumber(state, shot.Direction.X); AppendNumber(state, shot.Direction.Y); AppendNumber(state, shot.SpeedMultiplier); }
            foreach (PendingSplit split in _pendingSplits)
            {
                state.Append('|').Append(split.Tick).Append('|').Append(split.EnemyId).Append('|').Append(split.Elite != null);
                AppendNumber(state, split.Position.X); AppendNumber(state, split.Position.Y);
                AppendNumber(state, split.Elite != null ? split.Elite.RawHpMultiplier : 0);
                AppendNumber(state, split.Elite != null ? split.Elite.RawRewardMultiplier : 0);
                AppendNumber(state, split.Elite != null ? split.Elite.RawSpeedMultiplier : 0);
                if (split.CarrierOwnerId != 0) state.Append("|owner:").Append(split.CarrierOwnerId);
            }
            AppendNumber(state, _totalDamage); state.Append(_collectedFragments).Append(_nodeExecutionCount);
            if (_director != null)
            { _director.WriteState(state); AppendNumber(state, _moveDirection.X); AppendNumber(state, _moveDirection.Y); state.Append(_hasMoveDirection); }
            uint hash = 2166136261;
            foreach (char value in state.ToString()) { hash ^= value; hash *= 16777619; }
            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }

        /// <summary>불변 문화권 숫자 표기를 상태 해시 버퍼에 추가한다.</summary>
        private static void AppendNumber(StringBuilder state, double value) { state.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|'); }

        /// <summary>바닥 드롭 또는 주운 Modifier 아이템의 종류, 등급, 수량, 위치를 결정성 해시 버퍼에 기록한다.</summary>
        private static void AppendItemDropState(StringBuilder state, SimulationItemDrop drop)
        {
            state.Append('|').Append(drop.RuneId).Append('|').Append(drop.Grade).Append('|').Append(drop.Count);
            AppendNumber(state, drop.Position.X); AppendNumber(state, drop.Position.Y);
            state.Append('|').Append(drop.IsAttracted);
        }

        /// <summary>대시와 일반 이동을 처리하고 이번 입력으로 대시가 시작됐는지 반환한다.</summary>
        private bool MovePlayer(SimulationInput input)
        {
            SimVector move = input.Movement.Length > 1 ? input.Movement.Normalized() : input.Movement;
            if (move.LengthSquared > 0.000001) { _moveDirection = move.Normalized(); _hasMoveDirection = true; }
            bool startedDash = input.Dash && _stage != MissionStage.Terminal
                && _player.StartDash(move.LengthSquared > 0.000001 ? move : _player.AimDirection, _balance.Player.DashSeconds, _balance.Player.DashCooldown, Time);
            SimVector displacement = _player.DashRemaining > 0 ? _player.GetDashDirection() * (_balance.Player.DashDistance / _balance.Player.DashSeconds * Math.Min(STEP_SECONDS, _player.DashRemaining)) : move * (_balance.Player.Speed * _moveSpeedMultiplier * STEP_SECONDS);
            _player.Move(_map.Move(_player.Position, displacement, _sector.PlayerRadius));
            return startedDash;
        }

        /// <summary>지정 Trigger를 가진 슬롯을 순서대로 시전해 입력 이벤트 실행을 결정적으로 유지한다.</summary>
        private void TryCastTrigger(string trigger)
        {
            for (int slot = 0; slot < _loadout.Length; slot++) TryCastTriggered(_loadout[slot], trigger, slot);
        }

        /// <summary>현재 틱에 도달한 노드 완료와 Repeat 회차 예약을 등록 순서대로 적용한다.</summary>
        private void RunScheduled()
        {
            for (int i = 0; i < _scheduled.Count;)
            {
                ScheduledExecution pending = _scheduled[i];
                if (pending.Tick > _tick) { i++; continue; }
                _scheduled.RemoveAt(i);
                if (pending.IsBranchStart)
                {
                    Execute(new[] { pending.Action }, pending.Context, pending.Tracker);
                    pending.Tracker.Release();
                }
                else if (pending.IsIteration)
                {
                    RepeatScope scope = pending.Context.Repeat;
                    if (scope == null) continue;
                    if (scope.IsFailed) ReleaseRepeat(scope);
                    else StartRepeatIteration(pending.Actions, pending.Context, scope);
                }
                else CompleteAction(pending.Action, pending.Context, pending.Tracker);
            }
        }

        /// <summary>Starts root-connected branches in parallel with a 0.05-second offset between their start times.</summary>
        private void ExecuteRootBranches(IReadOnlyList<SpellAction> actions, SpellContext context, ExecutionTracker tracker)
        {
            tracker.BeginDispatch();
            try
            {
                int intervalTicks = GetExecutionDelayTicks(CompiledSpell.ROOT_BRANCH_INTERVAL_SECONDS);
                for (int index = 1; index < actions.Count; index++)
                {
                    if (_scheduled.Count >= _balance.Limits.MaxScheduledExecutions)
                    {
                        _droppedExecutions++;
                        continue;
                    }
                    tracker.Acquire();
                    _scheduled.Add(new ScheduledExecution(_tick + intervalTicks * index, actions[index], context, tracker, true));
                }
                if (actions.Count > 0) Execute(new[] { actions[0] }, context, tracker);
            }
            finally
            {
                tracker.EndDispatch();
            }
        }

        /// <summary>Repeat 회차를 간격대로 시작하고 Body 실행이 모두 끝난 뒤 완료 분기를 연결한다.</summary>
        private void ExecuteRepeat(SpellAction action, SpellContext context, ExecutionTracker tracker)
        {
            var scope = new RepeatScope(action.OnComplete, context, context.Repeat, tracker);
            SpellContext bodyContext = context.WithRepeat(scope);
            double interval = action.Interval * _balance.Sim.ExecutionTimeScale;
            int immediateIterations = 0;
            for (int repeat = 1; repeat < action.Times; repeat++)
            {
                if (action.Body.Count == 0) break;
                if (interval <= 0)
                {
                    scope.Acquire();
                    immediateIterations++;
                }
                else if (!ScheduleRepeatIteration(action.Body, bodyContext, scope, repeat * interval)) break;
            }
            if (action.Times > 0)
            {
                scope.Acquire();
                StartRepeatIteration(action.Body, bodyContext, scope);
                for (int repeat = 0; repeat < immediateIterations; repeat++)
                    StartRepeatIteration(action.Body, bodyContext, scope);
            }
            else FinishRepeat(scope);
        }

        /// <summary>Repeat 회차마다 실행 추적기를 만들고 해당 Body의 timed node들이 끝나면 회차를 반환한다.</summary>
        private void StartRepeatIteration(IReadOnlyList<SpellAction> actions, SpellContext context, RepeatScope scope)
        {
            var tracker = new ExecutionTracker(scope.ExecutionTracker.RootCast, () => ReleaseRepeat(scope));
            Execute(actions, context, tracker);
        }

        /// <summary>Repeat 회차 Body를 나중에 시작하도록 예약하고 수용 상한 오류를 표시한다.</summary>
        private bool ScheduleRepeatIteration(IReadOnlyList<SpellAction> actions, SpellContext context, RepeatScope scope, double seconds)
        {
            if (_scheduled.Count >= _balance.Limits.MaxScheduledExecutions)
            {
                _droppedExecutions += actions.Count;
                scope.Fail();
                return false;
            }
            scope.Acquire();
            int delayTicks = Math.Max(1, (int)Math.Min(int.MaxValue / 4, Math.Round(seconds * TICK_RATE)));
            _scheduled.Add(new ScheduledExecution(_tick + delayTicks, actions, context, true));
            return true;
        }

        /// <summary>Repeat 회차를 끝내고 마지막 회차라면 완료 분기를 시작한다.</summary>
        private void ReleaseRepeat(RepeatScope scope)
        {
            if (scope.Release()) FinishRepeat(scope);
        }

        /// <summary>모든 회차가 끝난 Repeat의 완료 분기를 실행하고 Repeat 노드 실행 토큰을 반환한다.</summary>
        private void FinishRepeat(RepeatScope scope)
        {
            if (!scope.IsFailed) Execute(scope.OnComplete, scope.CompleteContext, scope.ExecutionTracker);
            scope.ExecutionTracker.Release();
        }

        /// <summary>이벤트로 시작한 후속 분기를 슬롯과 분리된 실행 추적기로 시작한다.</summary>
        private void Execute(IReadOnlyList<SpellAction> actions, SpellContext context)
        {
            Execute(actions, context, new ExecutionTracker(null, null));
        }

        /// <summary>실행 명령의 병렬 분기를 시작하고 노드 시간 종료 예약을 추적기에 연결한다.</summary>
        private void Execute(IReadOnlyList<SpellAction> actions, SpellContext context, ExecutionTracker tracker)
        {
            tracker.BeginDispatch();
            if (_actionBudgetTick != _tick) { _actionBudgetTick = _tick; _actionsThisTick = 0; }
            try
            {
                for (int i = 0; i < actions.Count; i++)
                {
                    if (_actionsThisTick >= _balance.Limits.MaxActionsPerTick)
                    {
                        _droppedExecutions += actions.Count - i;
                        context.Repeat?.Fail();
                        break;
                    }
                    _actionsThisTick++;
                    SpellAction action = actions[i];
                    double seconds = GetExecutionSeconds(action, context);
                    int durationTicks = GetExecutionDelayTicks(seconds);
                    if (durationTicks > 0 && _scheduled.Count >= _balance.Limits.MaxScheduledExecutions)
                    {
                        _droppedExecutions++;
                        context.Repeat?.Fail();
                        continue;
                    }
                    if (!TrySpendFor(action, context))
                    {
                        context.Repeat?.Fail();
                        continue;
                    }
                    RecordNode(action.NodeId);
                    tracker.Acquire();
                    if (durationTicks == 0) CompleteAction(action, context, tracker);
                    else _scheduled.Add(new ScheduledExecution(_tick + durationTicks, action, context, tracker));
                }
            }
            finally
            {
                tracker.EndDispatch();
            }
        }

        /// <summary>지정 노드가 시작된 시점의 배율을 적용한 실행 시간을 반환한다.</summary>
        private double GetExecutionSeconds(SpellAction action, SpellContext context)
        {
            double seconds = action.Kind == "delay" ? action.Seconds : action.GetExecutionSeconds(context.Modifiers);
            return seconds * _balance.Sim.ExecutionTimeScale;
        }

        /// <summary>노드 실행 시간을 60Hz 완료 틱으로 올림하고 0이면 같은 틱 실행을 반환한다.</summary>
        private static int GetExecutionDelayTicks(double seconds)
        {
            if (seconds <= 0) return 0;
            double ticks = Math.Ceiling(seconds * TICK_RATE - 0.000000001);
            return (int)Math.Min(int.MaxValue / 4, Math.Max(1, ticks));
        }

        /// <summary>노드가 시작할 때 자원을 차감하고 실행 시간이 끝난 시점에 효과를 적용한다.</summary>
        private void CompleteAction(SpellAction action, SpellContext context, ExecutionTracker tracker)
        {
            tracker.BeginDispatch();
            bool isRepeat = action.Kind == "repeat";
            try
            {
                context = GetEffectContext(context, tracker);
                switch (action.Kind)
                {
                    case "spawn": SpawnForm(action, context); break;
                    case "buff": ApplyBuff(action, context, tracker); break;
                    case "call": ExecuteCall(action, context, tracker); break;
                    case "delay": Execute(action.Then, context, tracker); break;
                    case "repeat": ExecuteRepeat(action, context, tracker); break;
                    case "if": Execute(Evaluate(action.Condition, context) ? action.Then : action.Else, context, tracker); break;
                    case "blink":
                        SimVector destination = context.FromEvent ? _map.NearestFree(context.Origin, _sector.PlayerRadius) : _map.Move(_player.Position, context.Direction * action.Distance, _sector.PlayerRadius);
                        _player.Move(destination); Execute(action.Next, context.WithOrigin(destination), tracker); break;
                    case "shield": _player.GiveShield(action.ShieldAmount, action.ShieldSeconds, Time); Execute(action.Next, context, tracker); break;
                }
            }
            finally
            {
                if (!isRepeat) tracker.Release();
                tracker.EndDispatch();
            }
        }

        /// <summary>루트 시전은 효과 적용 시점의 지팡이 끝과 조준을 쓰고 이벤트 체인은 사건 문맥을 보존한다.</summary>
        private SpellContext GetEffectContext(SpellContext context, ExecutionTracker tracker)
        {
            if (context.FromEvent || tracker.RootCast == null) return context;
            SimVector aim = _player.AimDirection;
            SimVector origin = _player.Position + aim * (_sector.PlayerRadius + _balance.Sim.WandOffset);
            return context.WithOriginAndDirection(origin, aim.Rotated(context.DirectionOffset));
        }

        /// <summary>첫 Noise 효과가 적용되는 순간 원소를 결정하고 같은 루트의 다른 Noise 노드와 공유한다.</summary>
        private string ChooseNoiseElement(SpellContext context)
        {
            if (context.NoiseElement != null) return context.NoiseElement;
            int index = Math.Min(_unlockedElements.Count - 1, (int)(NextRandom() * _unlockedElements.Count));
            string element = _unlockedElements[index];
            context.RandomState.SetNoiseElement(element);
            return element;
        }

        /// <summary>
        /// 실행 노드에 도달했을 때 자원별 노드 비용 × 문맥 비용 배율을 함께 차감하고 성공 여부를 반환한다.
        /// 흐름 제어(Delay·Repeat·Condition)는 비용이 없고, 비용이 0이면 항상 성공한다.
        /// </summary>
        private bool TrySpendFor(SpellAction action, SpellContext context)
        {
            if (action.Kind == "delay" || action.Kind == "repeat" || action.Kind == "if") return true;
            ResourceCostSet costs = action.GetNodeCosts(context.Modifiers).Multiply(context.CostMultiplier);
            if (costs.Amounts.Count == 0) return true;
            if (!_player.TrySpend(costs)) return false;
            foreach (ResourceAmount amount in costs.Amounts)
            {
                _resourceCostsSpent.TryGetValue(amount.Resource, out double spent);
                _resourceCostsSpent[amount.Resource] = spent + amount.Amount;
            }
            return true;
        }

        /// <summary>SpellCall의 컴파일된 대상과 호출부 수식·이벤트 범위를 적용해 반복 실행한다.</summary>
        private void ExecuteCall(SpellAction action, SpellContext context, ExecutionTracker tracker)
        {
            foreach (string attachedNodeId in action.AttachedNodeIds) RecordNode(attachedNodeId);
            SpellModifierValues modifiers = context.Modifiers.Combine(action.ModifierValues);
            SpellEventScope events = new SpellEventScope(action.OnHit, action.OnExpire, action.OnFirstHitOrExpire, context.CallEvents, context.Modifiers,
                context.CostMultiplier);
            // 호출 노드에 붙은 효과의 비용 배율은 호출된 마법의 각 노드 비용에 곱한다.
            SpellContext callContext = new SpellContext(context.Origin, context.Direction, context.Target,
                context.FromEvent, context.NoiseElement, modifiers, events, context.Repeat, context.CostMultiplier * action.EnergyMultiplier,
                context.Caster, context.SourceEntityId, context.DirectionOffset, context.RandomState);
            for (int instance = 0; instance < action.Count; instance++)
            {
                double spread = action.Count > 1
                    ? (instance / (double)(action.Count - 1) * 2 - 1) * action.ModifierValues.SpreadAngle * DEGREES_TO_RADIANS
                    : 0;
                SpellContext instanceContext = new SpellContext(callContext.Origin, callContext.Direction.Rotated(spread),
                    callContext.Target, callContext.FromEvent, callContext.NoiseElement, callContext.Modifiers, callContext.CallEvents, callContext.Repeat,
                    callContext.CostMultiplier, callContext.Caster, callContext.SourceEntityId,
                    callContext.DirectionOffset + spread, callContext.RandomState);
                ExecuteRootBranches(action.CalledSpell.Root, instanceContext, tracker);
            }
        }

        /// <summary>
        /// Apply의 자기 적용 효과를 호출부 수식에 맞춰 시전자에게 적용하고 완료 분기를 실행한다.
        /// 회복은 즉시 회복, 보호는 보호막, 화염은 시전자에게 화상(자해 허용)을 부여한다.
        /// </summary>
        private void ApplyBuff(SpellAction action, SpellContext context, ExecutionTracker tracker)
        {
            foreach (string attachedNodeId in action.AttachedNodeIds) RecordNode(attachedNodeId);
            // Apply는 전달된 명중 대상이 아니라 문맥의 시전자에게 적용한다(백서 6장).
            SimulationPlayer caster = context.Caster;
            double amount = action.Power * action.Count * action.ModifierValues.DamageMultiplier * context.Modifiers.DamageMultiplier;
            if (action.SourceElement == "heal") caster.Heal(amount);
            else if (action.SourceElement == "protection")
                caster.GiveShield(amount, action.BuffDuration * action.ModifierValues.DurationMultiplier
                    * context.Modifiers.DurationMultiplier, Time);
            else if (action.SourceElement == "fire") caster.ApplyBurn(Time, _balance.Combat.BurnInterval, _balance.Combat.BurnSeconds);
            for (int instance = 0; instance < action.Count; instance++) Execute(action.OnFirstHitOrExpire, context, tracker);
        }

        /// <summary>대상 또는 자신 상태에 따른 조건 분기를 평가하며 대상이 없으면 대상 조건을 false로 반환한다.</summary>
        private bool Evaluate(SpellCondition condition, SpellContext context)
        {
            if (condition.Type == "selfHpBelow") return _player.Hp / _player.MaxHp * 100 < condition.Pct;
            if (context.Target == null) return false;
            switch (condition.Type)
            { case "targetHpBelow": return context.Target.Hp / context.Target.MaxHp * 100 < condition.Pct; case "targetHasStatus": return HasEnemyStatus(context.Target, condition.Status); case "targetDistanceBelow": return SimVector.Distance(_player.Position, context.Target.Position) < condition.Px; default: return false; }
        }

        /// <summary>
        /// 행동 실행 동작에 따라 개체 수만큼 배치·예열 시간을 정해 마법 개체를 생성하고 생성 직후 처리를 실행한다.
        /// 이벤트 앵커와 노이즈 속성을 반영한다. 공전은 수식으로 바뀔 예정이라 기존 궤도 배치·보호막 분기를 그대로 둔다.
        /// </summary>
        private void SpawnForm(SpellAction action, SpellContext context)
        {
            foreach (string attachedNodeId in action.AttachedNodeIds) RecordNode(attachedNodeId);
            string element = action.Noise ? ChooseNoiseElement(context) : action.Element;
            SpellStats stats = action.Stats.Apply(context.Modifiers);
            SpellBehaviorRuntime behavior = SpellBehaviorRuntimeRegistry.Get(action.Form);
            for (int i = 0; i < action.Count; i++)
            {
                if (_spellEntities.Count >= _balance.Limits.MaxLiveSpellEntities) return;
                SimVector direction = context.Direction;
                SimVector position = context.Origin;
                behavior.PlaceSpawn(this, action, stats, i, context.FromEvent, context.Caster, ref position, ref direction);
                double angle = i * 2 * Math.PI / action.Count;
                if (action.Form == "orbit") position = (context.FromEvent ? context.Origin : _player.Position) + new SimVector(Math.Cos(angle), Math.Sin(angle)) * stats.OrbitRadius;
                double warmup = behavior.GetWarmupSeconds(stats);
                var entity = new SimulationSpellEntity(++_nextEntityId, action, stats, element, position, direction,
                    context.FromEvent, context.Origin, angle, context.NoiseElement, _balance.Sim.SpellVisualSeconds, warmup,
                    context.CallEvents, context.Modifiers, context.CostMultiplier, context.Caster);
                _spellEntities.Add(entity); _peakSpellEntities = Math.Max(_peakSpellEntities, _spellEntities.Count);
                // 보호 Orbit(방벽)은 생성 시 보호막을 준다. 보호 Burst는 범위 안의 시전자에게만 HitArea에서 준다.
                if (action.SourceElement == "protection" && action.Form == SpellGrammar.FORM_ORBIT)
                    _player.GiveShield(action.Power * action.ModifierValues.DamageMultiplier * context.Modifiers.DamageMultiplier,
                        action.BuffDuration * action.ModifierValues.DurationMultiplier * context.Modifiers.DurationMultiplier, Time);
                behavior.OnSpawned(this, entity);
            }
        }

        /// <summary>투사체 이동, 유도, 회전 및 지속 범위 피해를 처리하고 소멸 이벤트를 한 번만 발생시킨다.</summary>
        private void AdvanceSpellEntities()
        {
            int initialCount = _spellEntities.Count;
            int processed = 0;
            for (int i = 0; processed < initialCount && i < _spellEntities.Count; processed++)
            {
                SimulationSpellEntity entity = _spellEntities[i];
                // 공전은 수식으로 바뀔 예정이라 행동 실행 클래스로 옮기지 않고 기존 궤도 이동을 그대로 둔다.
                if (entity.Kind == "orbit")
                {
                    entity.Angle += entity.Stats.AngularSpeed * DEGREES_TO_RADIANS * STEP_SECONDS;
                    SimVector anchor = entity.FromEvent ? entity.Anchor : _player.Position;
                    SimVector slot = anchor + new SimVector(Math.Cos(entity.Angle), Math.Sin(entity.Angle)) * entity.Stats.OrbitRadius;
                    SimVector tangent = new SimVector(-Math.Sin(entity.Angle), Math.Cos(entity.Angle));
                    // 보호 공전(방벽)은 시전자를 막는 역할이라 유도가 있어도 궤도를 떠나지 않는다.
                    if (entity.Stats.HomingRange > 0 && entity.Action.SourceElement != "protection") AdvanceHomingOrbit(entity, anchor, slot, tangent);
                    else entity.Move(slot, tangent);
                    if (entity.Action.SourceElement != "protection") HitArea(entity, false);
                }
                else entity.Behavior.Advance(this, entity);
                entity.Advance(STEP_SECONDS);
                entity.Behavior.AfterAdvance(this, entity);
                if ((entity.Behavior.RemovesWhenExpired && entity.HasExpired) || entity.Age + 0.000001 >= entity.Lifetime)
                { Expire(entity); _spellEntities.RemoveAt(i); }
                else i++;
            }
        }

        /// <summary>
        /// 유도 공전을 궤도 위 → 적에게 돌진 → 궤도 자리로 복귀 순서로 반복해 움직인다. 궤도 중심(anchor)에서 유도 거리 안의 가장 가까운 적을 노리고,
        /// 이동 속도로 직선 추적해 부딪치면 그 자리에서 궤도 자리(slot, 계속 회전함)로 날아 돌아온다. 돌아온 다음 틱에 다시 대상을 고른다.
        /// 대상이 죽거나 유도 거리를 벗어나면 바로 복귀하고, 대상이 없으면 궤도를 그대로 돈다. 벽은 일반 공전처럼 무시한다.
        /// </summary>
        private void AdvanceHomingOrbit(SimulationSpellEntity entity, SimVector anchor, SimVector slot, SimVector tangent)
        {
            double step = entity.Stats.Speed * STEP_SECONDS;
            double range = entity.Stats.HomingRange;
            if (entity.HomingTargetId == 0 && !entity.IsReturning)
            {
                SimulationEnemy next = NearestEnemy(anchor, range, NoExcludedTargets);
                if (next == null)
                {
                    entity.Move(slot, tangent);
                    return;
                }
                entity.HomingTargetId = next.Id;
            }
            if (entity.HomingTargetId != 0)
            {
                SimulationEnemy target = null;
                foreach (SimulationEnemy enemy in _enemies)
                {
                    if (enemy.Id == entity.HomingTargetId && enemy.IsAlive) { target = enemy; break; }
                }
                if (target == null || SimVector.Distance(anchor, target.Position) > range)
                {
                    entity.HomingTargetId = 0;
                    entity.IsReturning = true;
                }
                else
                {
                    SimVector offset = target.Position - entity.Position;
                    double contact = entity.Radius + target.Radius;
                    SimVector heading = offset.Normalized();
                    if (offset.Length - step <= contact)
                    {
                        // 이번 틱에 닿으므로 접촉 위치(살짝 겹치게)까지만 가고 복귀로 바꾼다. 피해·OnHit은 이어지는 범위 판정이 처리한다.
                        double stop = Math.Max(0, contact - ORBIT_CONTACT_OVERLAP);
                        SimVector touch = offset.Length > stop ? target.Position - heading * stop : entity.Position;
                        entity.Move(touch, heading);
                        entity.HomingTargetId = 0;
                        entity.IsReturning = true;
                        return;
                    }
                    entity.Move(entity.Position + heading * step, heading);
                    return;
                }
            }
            SimVector back = slot - entity.Position;
            if (back.Length <= step)
            {
                entity.Move(slot, tangent);
                entity.IsReturning = false;
                return;
            }
            entity.Move(entity.Position + back.Normalized() * step, back.Normalized());
        }

        /// <summary>원점 origin과 방향 direction의 중심선을 최대 length까지 검사해 첫 벽 앞의 길이를 반환한다.</summary>
        private double ClipBeamLength(SimVector origin, SimVector direction, double length)
        {
            double step = Math.Max(BEAM_CLIP_MIN_STEP, _map.TileSize * BEAM_CLIP_STEP_RATIO);
            double free = 0;
            double distance = Math.Min(step, length);
            while (distance <= length + COLLISION_EPSILON)
            {
                if (_map.IsWall(origin + direction * distance)) return RefineBeamClip(origin, direction, free, distance);
                free = distance;
                distance += step;
            }
            return length;
        }

        /// <summary>원점 origin과 방향 direction에서 통과 길이 free와 벽 진입 길이 hit 사이를 좁혀 벽 앞 길이를 반환한다.</summary>
        private double RefineBeamClip(SimVector origin, SimVector direction, double free, double hit)
        {
            for (int i = 0; i < BEAM_CLIP_REFINEMENT_STEPS; i++)
            {
                double middle = (free + hit) * 0.5;
                if (_map.IsWall(origin + direction * middle)) hit = middle;
                else free = middle;
            }
            return free;
        }

        /// <summary>원형 개체의 직선 이동을 작은 구간으로 검사하고 첫 벽 접촉 비율을 반환한다.</summary>
        internal double FirstWallFraction(SimVector start, SimVector end, double radius)
        {
            int steps = Math.Max(1, (int)Math.Ceiling((end - start).Length / Math.Max(1, Math.Min(radius, _map.TileSize * 0.25))));
            double last = 0;
            for (int i = 1; i <= steps; i++)
            {
                double high = i / (double)steps;
                if (_map.CanOccupy(start + (end - start) * high, radius)) { last = high; continue; }
                for (int j = 0; j < WALL_CONTACT_REFINEMENT_STEPS; j++)
                {
                    double middle = (last + high) * 0.5;
                    if (_map.CanOccupy(start + (end - start) * middle, radius)) last = middle; else high = middle;
                }
                return high;
            }
            return double.PositiveInfinity;
        }

        /// <summary>
        /// 범위 마법의 가까운 적부터 대상별 주기(행동의 범위 판정 주기)에 맞춰 피해를 적용한다. 주기 판정 적중은 OnHit을 만들지 않는다.
        /// isBurst이면 대상별 한 번만 적중하고 확장 범위가 대상과 처음 겹친 접촉 위치로 OnHit을 낸다(Burst).
        /// 공전은 대상과의 접촉이 시작될 때(떨어졌다 다시 닿을 때 포함) OnHit을 낸다.
        /// </summary>
        internal void HitArea(SimulationSpellEntity entity, bool isBurst)
        {
            string source = entity.Action.SourceElement;
            if (source == "heal" || (source == "protection" && isBurst))
            {
                ApplyFunctionalArea(entity, isBurst);
                return;
            }
            if (source == "protection") return;
            var targets = new List<SimulationEnemy>();
            foreach (SimulationEnemy enemy in _enemies)
            {
                if (enemy.IsAlive && SpellGeometry.IsInsideArea(entity, enemy.Position, enemy.Radius)) targets.Add(enemy);
            }
            targets.Sort((a, b) =>
            {
                int distanceOrder = (a.Position - entity.Position).LengthSquared.CompareTo((b.Position - entity.Position).LengthSquared);
                return distanceOrder != 0 ? distanceOrder : a.Id.CompareTo(b.Id);
            });
            double interval = entity.Behavior.GetAreaHitInterval(entity.Stats);
            bool isOrbit = entity.Kind == SpellGrammar.FORM_ORBIT;
            foreach (SimulationEnemy enemy in targets)
            {
                if (!enemy.IsAlive) continue;
                bool isNewContact = isOrbit && entity.MarkContact(enemy.Id);
                bool hasHit = entity.HitTimes.TryGetValue(enemy.Id, out double lastTime);
                if (!hasHit || (!isBurst && Time - lastTime + 0.000001 >= interval))
                {
                    entity.HitTimes[enemy.Id] = Time;
                    Hit(entity, enemy, entity.Stats.Damage, entity.Behavior.IsDamageOverTime);
                    // Burst.OnHit은 확장 범위가 대상과 처음 겹칠 때 대상별 한 번, 접촉 위치로 낸다.
                    if (isBurst) RaiseHitEvent(entity, SpellGeometry.GetContactPoint(entity, enemy.Position), enemy);
                }
                if (isNewContact) RaiseHitEvent(entity, enemy.Position, enemy);
            }
            if (isOrbit) entity.EndContactScan();
        }

        /// <summary>
        /// 회복·보호(Functional) 범위 효과를 범위 안의 시전자에게만 적용한다(결정 Q15). 적에게는 효과가 없다.
        /// Burst는 한 번, Persist는 시전자 기준 TickInterval마다 즉시 회복하며 별도 상태 이상을 만들지 않는다.
        /// </summary>
        private void ApplyFunctionalArea(SimulationSpellEntity entity, bool isBurst)
        {
            if (!SpellGeometry.IsInsideArea(entity, _player.Position, _sector.PlayerRadius)) return;
            bool hasApplied = entity.HitTimes.TryGetValue(PLAYER_TARGET_ID, out double lastTime);
            if (hasApplied && (isBurst || Time - lastTime + 0.000001 < entity.Stats.TickInterval)) return;
            entity.HitTimes[PLAYER_TARGET_ID] = Time;
            SpellAction action = entity.Action;
            double amount = action.Power * action.ModifierValues.DamageMultiplier * entity.Modifiers.DamageMultiplier;
            if (action.SourceElement == "heal") _player.Heal(amount);
            else _player.GiveShield(amount, action.BuffDuration * action.ModifierValues.DurationMultiplier * entity.Modifiers.DurationMultiplier, Time);
        }

        /// <summary>스펠 피해와 상태를 적용하고 전격 연쇄를 처리한다. 잔류 피해를 누적하는 행동은 실제 피해를 PersistDamage에도 누적한다. 이벤트 실행은 호출부가 RaiseHitEvent로 따로 처리한다.</summary>
        internal void Hit(SimulationSpellEntity entity, SimulationEnemy enemy, double damage, bool isDot)
        {
            double actual = ApplyDamage(enemy, damage, entity.Element, entity.Kind, entity.Action.Noise, isDot, entity.Direction, true);
            if (entity.Element == "arc")
            {
                RuneStats arc = GameData.Runes.Get("elem.arc").Stats;
                var chain = new List<SimulationEnemy>();
                foreach (SimulationEnemy other in _enemies)
                    if (other != enemy && other.IsAlive && SimVector.Distance(other.Position, enemy.Position) <= arc.ArcRange) chain.Add(other);
                chain.Sort((a, b) =>
                {
                    int distanceOrder = (a.Position - enemy.Position).LengthSquared.CompareTo((b.Position - enemy.Position).LengthSquared);
                    return distanceOrder != 0 ? distanceOrder : a.Id.CompareTo(b.Id);
                });
                for (int i = 0; i < Math.Min(arc.ArcTargets, chain.Count); i++)
                {
                    actual += ApplyDamage(chain[i], damage * arc.ArcMultiplier, "arc", entity.Kind, entity.Action.Noise, isDot, (chain[i].Position - enemy.Position).Normalized(), true);
                    _arcLinks.Add(new ArcLink(enemy.Position, chain[i].Position, _tick));
                }
            }
            if (entity.Behavior.AccumulatesPersistDamage) _persistDamage += actual;
        }

        /// <summary>
        /// 개체의 OnHit 이벤트를 지정 위치·대상(벽이면 null)으로 실행한다. 직접 충돌을 추적하는 행동(발사)은 직접 충돌로 기록하고
        /// 첫 이벤트이면 OnFirstHitOrExpire도 실행한다.
        /// </summary>
        internal void RaiseHitEvent(SimulationSpellEntity entity, SimVector position, SimulationEnemy target)
        {
            bool tracksDirectHit = entity.Behavior.TracksDirectHit;
            if (tracksDirectHit) entity.HasDirectHit = true;
            SpellContext context = new SpellContext(position, GetEventDirection(entity, position, target), target, true,
                entity.CastNoiseElement, entity.Modifiers, entity.CallEvents, null, entity.CostMultiplier, entity.Caster, entity.Id);
            bool isFirstEvent = tracksDirectHit && entity.TryMarkFirstEvent();
            Execute(entity.Action.OnHit, context);
            if (isFirstEvent) Execute(entity.Action.OnFirstHitOrExpire, context);
            ExecuteCallEvent(entity, true, isFirstEvent, position, target);
        }

        /// <summary>
        /// 원형 쉴드에 막아 소멸한 발사체의 명중·소멸 이벤트를 모두 실행한다.
        /// OnHit과 첫 이벤트는 접촉 위치와 쉴드 소유자 대상으로, OnExpire는 개체 위치와 대상 없이 낸다.
        /// </summary>
        internal void RaiseShieldHitEvent(SimulationSpellEntity entity, SimVector contact, SimulationEnemy shieldOwner)
        {
            RaiseHitEvent(entity, contact, shieldOwner);
            RaiseExpireEvent(entity);
        }

        /// <summary>
        /// 적중 후속 실행의 방향을 접촉 위치(시작점)에서 적중 대상의 위치(도착점)로 반환한다(백서 v7 6장).
        /// 대상이 없거나 두 위치가 같으면 개체의 진행 방향을 쓴다.
        /// </summary>
        private static SimVector GetEventDirection(SimulationSpellEntity entity, SimVector contact, SimulationEnemy target)
        {
            if (target == null) return entity.Direction;
            SimVector offset = target.Position - contact;
            return offset.Length > 0.000001 ? offset.Normalized() : entity.Direction;
        }

        /// <summary>
        /// 개체를 만든 호출 객체들의 이벤트 분기를 안쪽에서 바깥쪽으로 각각 한 번 실행한다. 이벤트 원본은 그 개체다.
        /// 개체의 첫 적중·소멸 이벤트이면 적중·소멸 분기 다음에 onFirstHitOrExpire 분기도 실행한다.
        /// </summary>
        private void ExecuteCallEvent(SimulationSpellEntity entity, bool isHit, bool isFirstEvent, SimVector origin, SimulationEnemy target)
        {
            for (SpellEventScope current = entity.CallEvents; current != null; current = current.Parent)
            {
                IReadOnlyList<SpellAction> actions = isHit ? current.OnHit : current.OnExpire;
                SpellContext context = new SpellContext(origin, GetEventDirection(entity, origin, target), target, true, entity.CastNoiseElement,
                    current.ParentModifiers, current.Parent, null, current.ParentCostMultiplier, entity.Caster, entity.Id);
                Execute(actions, context);
                if (isFirstEvent) Execute(current.OnFirstHitOrExpire, context);
            }
        }

        /// <summary>피해 파이프라인에 적응, 이지스 방패 및 보스 무적을 반영하고 실제 피해와 학습을 기록한다.</summary>
        private double ApplyDamage(SimulationEnemy enemy, double damage, string element, string form, bool noise, bool isDot, SimVector direction, bool applyStatus)
        {
            if (!enemy.IsAlive || enemy.IsPatching) return 0;
            double actual = damage * _damageMultiplier * Adaptation.GetMultiplier(element, form);
            if (enemy.Definition.HasTrait(EnemyDefinition.TRAIT_AEGIS_SHIELD) && !HasEnemyStatus(enemy, EnemyStatusType.Emp) && form == "bolt" && !isDot && SimVector.Dot(enemy.Facing, direction * -1) >= Math.Cos(_balance.Combat.AegisAngle * 0.5 * DEGREES_TO_RADIANS)) actual *= 1 - _balance.Combat.AegisReduction;
            bool relayAlive = false;
            foreach (SimulationEnemy relay in _enemies)
            {
                if (!relay.Definition.HasTrait(EnemyDefinition.TRAIT_RELAY_AURA) || !relay.IsAlive) continue;
                relayAlive = true;
                break;
            }
            actual = enemy.Hurt(actual, Time);
            if (applyStatus) ApplyStatus(enemy, element, form, noise);
            if (actual > 0)
            {
                enemy.SetDamageTags(element, form);
                _totalDamage += actual; _rollingDamage += actual; _damageWindow.Enqueue(new DamageSample(_tick, actual));
                _damageNumbers.Add(new DamageNumber(enemy.Position, actual, element, _tick));
                Adaptation.Learn(element, form, Time, actual, isDot, noise, relayAlive);
            }
            if (enemy.Boss != null && enemy.Boss.IsGovernorPattern && enemy.IsAlive) CheckBossPatch(enemy, enemy.Boss);
            return actual;
        }

        /// <summary>
        /// 60틱마다 살아 있는 릴레이 반경 안의 다른 적 체력을 RelayRegen만큼 회복시킨다.
        /// 릴레이 자신은 오라 대상이 아니고 최대 체력까지만 회복한다.
        /// </summary>
        private void AdvanceRelayRegen()
        {
            if (_tick % RELAY_REGEN_INTERVAL_TICKS != 0) return;
            foreach (SimulationEnemy relay in _enemies)
            {
                if (!relay.IsAlive || !relay.Definition.HasTrait(EnemyDefinition.TRAIT_RELAY_AURA)) continue;
                foreach (SimulationEnemy enemy in _enemies)
                {
                    if (enemy == relay || !enemy.IsAlive || SimVector.Distance(relay.Position, enemy.Position) > _balance.Combat.RelayRadius) continue;
                    if (enemy.Heal(_balance.Combat.RelayRegen) > 0) _healPulses.Add(new HealPulse(enemy.Position, _tick));
                }
            }
        }

        /// <summary>
        /// 상태 판정 시각을 현재 틱으로 갱신하고, 적 목록 순서로 화염의 마지막 만료 틱까지 지속 피해를 적용한 뒤 만료된 상태 이상을 제거한다.
        /// </summary>
        private void AdvanceStatuses()
        {
            _statusTime = Time;
            while (_player.Hp > 0 && _player.TryTakeBurnTick(Time, _balance.Combat.BurnInterval))
                HurtPlayerByStatus(_balance.Combat.BurnDps * _balance.Combat.BurnInterval);
            foreach (SimulationEnemy enemy in _enemies)
            {
                enemy.Observe(Time);
                EnemyStatusEffect burn = FindStatus(enemy.Id, EnemyStatusType.Burn);
                if (burn == null) continue;
                while (enemy.IsAlive && burn.NextTickTime <= burn.Until + 0.000001 && Time + 0.000001 >= burn.NextTickTime)
                { _statusDamage += ApplyDamage(enemy, _balance.Combat.BurnDps * _balance.Combat.BurnInterval, "fire", burn.SourceForm, burn.IsNoise, true, SimVector.Zero, false); burn.NextTickTime += _balance.Combat.BurnInterval; }
            }
            for (int i = _enemyStatuses.Count - 1; i >= 0; i--)
                if (IsStatusExpired(_enemyStatuses[i])) { RemoveStatusIndex(_enemyStatuses[i]); _enemyStatuses.RemoveAt(i); }
        }

        /// <summary>
        /// 상태 이상이 더 이상 판정·진행에 쓰이지 않는지 반환한다.
        /// 화염은 마지막 지속 피해 틱까지 처리된 뒤, 빙결은 빙결 면역까지 끝난 뒤 만료된다.
        /// </summary>
        private bool IsStatusExpired(EnemyStatusEffect status)
        {
            switch (status.Type)
            {
                case EnemyStatusType.Burn: return status.NextTickTime > status.Until + 0.000001 && Time >= status.Until;
                case EnemyStatusType.Freeze: return Time >= status.ImmuneUntil;
                default: return Time >= status.Until;
            }
        }

        /// <summary>피해 속성에 맞는 상태 이상(화염·냉기·EMP)을 현재 시각 기준으로 부여한다.</summary>
        private void ApplyStatus(SimulationEnemy enemy, string element, string form, bool noise)
        {
            if (element == "fire") ApplyBurn(enemy, form, noise);
            else if (element == "ice") ApplyChill(enemy);
            else if (element == "arc") GetOrAddStatus(enemy.Id, EnemyStatusType.Emp).Until = Time + _balance.Combat.EmpSeconds;
        }

        /// <summary>화염의 출처 형태와 노이즈를 보관하고 지속시간을 갱신하되, 진행 중인 화염이면 다음 틱 시각을 유지한다.</summary>
        private void ApplyBurn(SimulationEnemy enemy, string form, bool noise)
        {
            EnemyStatusEffect burn = FindStatus(enemy.Id, EnemyStatusType.Burn);
            if (burn == null || Time >= burn.Until)
            {
                burn ??= AddStatus(enemy.Id, EnemyStatusType.Burn);
                burn.NextTickTime = Time + _balance.Combat.BurnInterval;
            }
            burn.Until = Time + _balance.Combat.BurnSeconds; burn.SourceForm = form; burn.IsNoise = noise;
        }

        /// <summary>
        /// 냉기 스택과 지속시간을 갱신하고, 최대 스택에서 빙결 면역이 아니면 빙결을 부여하며 냉기를 제거한다.
        /// 빙결 면역 중에는 냉기 스택이 면역 중 상한(백서 기준 2)을 넘지 않는다.
        /// </summary>
        private void ApplyChill(SimulationEnemy enemy)
        {
            CombatBalance combat = _balance.Combat;
            EnemyStatusEffect freeze = FindStatus(enemy.Id, EnemyStatusType.Freeze);
            bool isImmune = freeze != null && Time < freeze.ImmuneUntil;
            EnemyStatusEffect chill = GetOrAddStatus(enemy.Id, EnemyStatusType.Chill);
            int maxStacks = isImmune ? combat.ChillImmuneMaxStacks : combat.ChillMaxStacks;
            chill.Stacks = Math.Min(maxStacks, chill.Stacks + 1); chill.Until = Time + combat.ChillSeconds;
            if (isImmune || chill.Stacks < combat.ChillMaxStacks) return;
            freeze ??= AddStatus(enemy.Id, EnemyStatusType.Freeze);
            freeze.Until = Time + combat.FreezeSeconds; freeze.ImmuneUntil = freeze.Until + combat.FreezeImmunity;
            RemoveStatusIndex(chill); _enemyStatuses.Remove(chill);
        }

        /// <summary>적의 상태 이상이 현재 상태 판정 시각에 활성인지 반환한다. 냉기는 스택 보유 여부로 판정한다.</summary>
        public bool HasEnemyStatus(SimulationEnemy enemy, EnemyStatusType type)
        {
            EnemyStatusEffect status = FindStatus(enemy.Id, type);
            if (status == null) return false;
            return type == EnemyStatusType.Chill ? status.Stacks > 0 : _statusTime < status.Until;
        }

        /// <summary>조건 룬의 상태 이름(burn, chill, freeze, emp)으로 활성 여부를 반환하며 알 수 없는 이름은 false를 반환한다.</summary>
        private bool HasEnemyStatus(SimulationEnemy enemy, string status)
        {
            switch (status)
            {
                case "burn": return HasEnemyStatus(enemy, EnemyStatusType.Burn);
                case "chill": return HasEnemyStatus(enemy, EnemyStatusType.Chill);
                case "freeze": return HasEnemyStatus(enemy, EnemyStatusType.Freeze);
                case "emp": return HasEnemyStatus(enemy, EnemyStatusType.Emp);
                default: return false;
            }
        }

        /// <summary>적의 현재 냉기 스택을 반환하며 냉기가 없으면 0을 반환한다.</summary>
        public int GetChillStacks(SimulationEnemy enemy) => FindStatus(enemy.Id, EnemyStatusType.Chill)?.Stacks ?? 0;

        /// <summary>적 ID와 종류로 상태 이상을 찾고 없으면 null을 반환한다.</summary>
        private EnemyStatusEffect FindStatus(int enemyId, EnemyStatusType type)
        { _statusIndex.TryGetValue(GetStatusKey(enemyId, type), out EnemyStatusEffect status); return status; }

        /// <summary>적 ID와 종류의 상태 이상을 찾고 없으면 새로 추가하여 반환한다.</summary>
        private EnemyStatusEffect GetOrAddStatus(int enemyId, EnemyStatusType type) => FindStatus(enemyId, type) ?? AddStatus(enemyId, type);

        /// <summary>새 상태 이상을 목록 끝과 조회 인덱스에 추가하여 반환한다.</summary>
        private EnemyStatusEffect AddStatus(int enemyId, EnemyStatusType type)
        { var status = new EnemyStatusEffect(enemyId, type); _enemyStatuses.Add(status); _statusIndex.Add(GetStatusKey(enemyId, type), status); return status; }

        /// <summary>제거된 적의 모든 상태 이상을 목록과 조회 인덱스에서 삭제한다.</summary>
        private void RemoveEnemyStatuses(int enemyId)
        {
            for (int i = _enemyStatuses.Count - 1; i >= 0; i--)
                if (_enemyStatuses[i].EnemyId == enemyId) { RemoveStatusIndex(_enemyStatuses[i]); _enemyStatuses.RemoveAt(i); }
        }

        /// <summary>상태 이상을 조회 인덱스에서 삭제한다.</summary>
        private void RemoveStatusIndex(EnemyStatusEffect status) { _statusIndex.Remove(GetStatusKey(status.EnemyId, status.Type)); }

        /// <summary>적 ID와 상태 이상 종류를 하나의 조회 키로 합친다.</summary>
        private static int GetStatusKey(int enemyId, EnemyStatusType type) => enemyId * STATUS_TYPE_COUNT + (int)type;

        /// <summary>
        /// 적마다 이동 전 플레이어 방향을 구해 이동 판단을 적용한 뒤 접촉, 예고 사격, 돌진 및 거버너 페이즈 패턴을 진행한다.
        /// 공격 판정은 이동 전 거리·방향을, 접촉과 발사 위치는 이동 후 위치를 사용한다.
        /// </summary>
        private void AdvanceEnemies()
        {
            int initialCount = _enemies.Count;
            for (int i = 0; i < initialCount; i++)
            {
                SimulationEnemy enemy = _enemies[i]; if (!enemy.IsAlive) continue;
                enemy.Observe(Time);
                if (enemy.IsDummy) continue;
                if (HasEnemyStatus(enemy, EnemyStatusType.Freeze))
                {
                    if (enemy.Definition.Attack == EnemyDefinition.ATTACK_SNIPER)
                    { enemy.AimEndsAt = 0; enemy.WarningUntil = 0; enemy.AttackAt = Time + STEP_SECONDS; }
                    continue;
                }
                SimVector offset = _player.Position - enemy.Position; SimVector direction = offset.Normalized();
                BossDefinition bossPattern = enemy.Boss;
                bool isBoss = bossPattern != null && bossPattern.IsGovernorPattern;
                if (isBoss) { CheckBossPatch(enemy, bossPattern); if (enemy.IsPatching) continue; }
                double speedMultiplier = isBoss && enemy.Phase == 3 ? bossPattern.PhaseThreeMultiplier : 1;
                var context = new EnemyMoveContext(offset, direction, Time, _isMission, _stages.StationaryAdvanceSpeed, isBoss ? bossPattern.PreferredDistance : 0, speedMultiplier);
                ApplyMove(enemy, EnemyMovements.Decide(enemy, context));
                if (isBoss) AdvanceBoss(enemy, bossPattern, direction);
                else AdvanceEnemyAttack(enemy, offset, direction);
                if (enemy.Definition.HasTrait(EnemyDefinition.TRAIT_CARRIER)) AdvanceCarrier(enemy);
            }
        }

        /// <summary>이동 판단 결과에 따라 정지, 방향 전환 또는 감속·벽 충돌을 반영한 이동을 적용한다.</summary>
        private void ApplyMove(SimulationEnemy enemy, EnemyMoveDecision decision)
        {
            if (decision.Mode == EnemyMoveMode.Face) enemy.Move(enemy.Position, decision.Direction);
            else if (decision.Mode == EnemyMoveMode.Move) MoveEnemy(enemy, decision.Direction, decision.Speed);
        }

        /// <summary>
        /// 보스가 아닌 적의 공격 일정을 이동 전 거리·방향으로 진행한다.
        /// 연사형은 예고 후 연사, 돌진 접촉형은 예고 후 돌진·돌진 중 접촉, 공격 없음은 쉬며 그 외는 주기적 접촉 피해를 준다.
        /// </summary>
        private void AdvanceEnemyAttack(SimulationEnemy enemy, SimVector offset, SimVector direction)
        {
            EnemyDefinition definition = enemy.Definition;
            if (definition.Attack == EnemyDefinition.ATTACK_SNIPER) { AdvanceSniper(enemy, direction); return; }
            if (definition.Attack == EnemyDefinition.ATTACK_INTERCEPTOR) { AdvanceInterceptor(enemy, direction); return; }
            if (definition.Attack == EnemyDefinition.ATTACK_NONE) return;
            if (definition.Attack == EnemyDefinition.ATTACK_BURST)
            {
                if (enemy.WarningUntil > 0 && Time + 0.000001 >= enemy.WarningUntil)
                { ShootBurst(enemy, enemy.AttackDirection, 1); enemy.WarningUntil = 0; enemy.AttackAt = Time + definition.AttackInterval - definition.WarningSeconds; }
                else if (enemy.WarningUntil == 0 && Time + 0.000001 >= enemy.AttackAt)
                { enemy.AttackDirection = direction; enemy.WarningUntil = Time + definition.WarningSeconds; }
                return;
            }
            if (definition.Attack == EnemyDefinition.ATTACK_DASH_CONTACT)
            {
                if (Time < enemy.DashUntil) { HurtByContact(enemy); return; }
                if (enemy.WarningUntil > 0)
                { if (Time + 0.000001 >= enemy.WarningUntil) { enemy.WarningUntil = 0; enemy.DashUntil = Time + definition.DashSeconds; enemy.AttackAt = Time + definition.AttackInterval; } return; }
                if (offset.Length < definition.AttackRange && Time >= enemy.AttackAt)
                { enemy.WarningUntil = Time + definition.WarningSeconds; enemy.AttackDirection = direction; }
                return;
            }
            if (Time >= enemy.AttackAt && HurtByContact(enemy)) enemy.AttackAt = Time + definition.AttackInterval;
        }

        /// <summary>저격수의 추적 조준, 고정 대기와 단발 발사를 진행하며 같은 저장 경로를 표시와 사격에 사용한다.</summary>
        private void AdvanceSniper(SimulationEnemy enemy, SimVector direction)
        {
            EnemyDefinition definition = enemy.Definition;
            if (!enemy.IsAiming)
            {
                if (Time + COLLISION_EPSILON < enemy.AttackAt) return;
                enemy.AimEndsAt = Time + definition.AimSeconds;
                enemy.WarningUntil = enemy.AimEndsAt + definition.LockSeconds;
                enemy.ShotOrigin = enemy.Position; enemy.AttackDirection = direction;
                return;
            }
            if (Time + COLLISION_EPSILON < enemy.AimEndsAt)
            { enemy.ShotOrigin = enemy.Position; enemy.AttackDirection = direction; return; }
            if (Time + COLLISION_EPSILON < enemy.WarningUntil) return;
            _enemyProjectiles.Add(new SimulationProjectile(enemy.AttackOrigin, enemy.AttackDirection, definition.ProjectileSpeed,
                enemy.Damage, definition.ProjectileRadius, definition.ProjectileLifetime));
            enemy.AimEndsAt = 0; enemy.WarningUntil = 0;
            enemy.AttackAt = Time + definition.AttackInterval - definition.AimSeconds - definition.LockSeconds;
        }

        /// <summary>인터셉터의 이동·정지 남은 시간을 줄이고 이동 종료마다 플레이어 방향에 한 발을 발사한다.</summary>
        private void AdvanceInterceptor(SimulationEnemy enemy, SimVector direction)
        {
            enemy.BehaviorRemaining -= STEP_SECONDS;
            if (enemy.BehaviorRemaining > COLLISION_EPSILON) return;
            if (!enemy.IsStopping) ShootFan(enemy, direction, 1, 1);
            enemy.IsBehaviorStopping = !enemy.IsStopping;
            enemy.BehaviorRemaining += enemy.IsStopping ? enemy.Definition.StopSeconds : enemy.Definition.MoveSeconds;
        }

        /// <summary>캐리어의 생성 타이머와 보류 수를 진행하고 설정 목록 순서대로 보상 없는 자식을 주변에 생성한다.</summary>
        private void AdvanceCarrier(SimulationEnemy enemy)
        {
            enemy.SpawnRemainingSeconds -= STEP_SECONDS;
            if (enemy.SpawnRemaining == 0)
            {
                if (enemy.SpawnRemainingSeconds > COLLISION_EPSILON) return;
                enemy.SpawnRemaining = _carrier.Count;
            }
            int alive = 0;
            foreach (SimulationEnemy child in _enemies) if (child.IsAlive && child.CarrierOwnerId == enemy.Id) alive++;
            while (enemy.SpawnRemaining > 0 && _enemies.Count < EnemyLimit && alive < _carrier.MaxAlive)
            {
                string id = _carrier.EnemyIds[enemy.SpawnCursor % _carrier.EnemyIds.Count];
                double radius = _enemyCatalog.Get(id).Radius;
                double angle = enemy.SpawnCursor * Math.PI * (3 - Math.Sqrt(5));
                double distance = Math.Max(_carrier.Distance, enemy.Radius + radius + SAME_SPAWN_DISTANCE);
                SimVector position = enemy.Position + new SimVector(Math.Cos(angle), Math.Sin(angle)) * distance;
                SpawnEnemy(id, _map.NearestFree(position, radius), carrierOwnerId: enemy.Id);
                enemy.SpawnCursor++; enemy.SpawnRemaining--; alive++;
            }
            if (enemy.SpawnRemaining == 0) enemy.SpawnRemainingSeconds = _carrier.Interval;
            else enemy.SpawnRemainingSeconds = 0;
        }

        /// <summary>냉기 감속을 반영하여 적을 벽 충돌 가능한 방향으로 이동시킨다.</summary>
        private void MoveEnemy(SimulationEnemy enemy, SimVector direction, double speed)
        { double slow = Math.Max(0, 1 - _balance.Combat.ChillSlow * GetChillStacks(enemy)); enemy.Move(_map.Move(enemy.Position, direction * (speed * enemy.SpeedMultiplier * slow * STEP_SECONDS), enemy.Radius), direction); }

        /// <summary>적 분리에서 밀리지 않는 고정 대상인지 반환한다. 보스, 빙결 중인 적, 돌진 중인 적은 자리를 지키고 상대만 밀어낸다.</summary>
        private bool IsSeparationFixed(SimulationEnemy enemy) => enemy.Boss != null || HasEnemyStatus(enemy, EnemyStatusType.Freeze) || Time < enemy.DashUntil;

        /// <summary>적과 플레이어가 겹치면 피해를 적용하고 접촉 여부를 반환한다.</summary>
        private bool HurtByContact(SimulationEnemy enemy)
        { if (!_isMission || SimVector.Distance(enemy.Position, _player.Position) > enemy.Radius + _sector.PlayerRadius) return false; HurtPlayer(enemy.Damage); return true; }

        /// <summary>플레이어 피해에 디버그 무적과 기본 피격 무적을 반영한다.</summary>
        private void HurtPlayer(double damage)
        {
            if (_debugInvulnerable || !_isMission) return;
            double shieldBefore = _player.Shield;
            double actual = _player.Hurt(damage, Time, _balance.Player.HurtInvulnerability);
            if (actual > 0) _damageNumbers.Add(new DamageNumber(_player.Position, actual, "player", _tick));
            if (actual > 0 || _player.Shield < shieldBefore) TryCastTrigger(SpellGrammar.TRIGGER_ON_HIT_TAKEN);
        }

        /// <summary>
        /// 시전자의 상태 이상 피해(화상 DoT)를 적용한다. 피격 무적을 무시하고 주지 않으며 OnHitTaken을 발생시키지 않는다.
        /// 시험 도크(비전투)에서는 상태만 표시하고 체력을 깎지 않는다(결정 Q13).
        /// </summary>
        private void HurtPlayerByStatus(double damage)
        {
            if (_debugInvulnerable || !_isMission) return;
            double actual = _player.HurtByStatus(damage);
            if (actual > 0) _damageNumbers.Add(new DamageNumber(_player.Position, actual, "player", _tick));
        }

        /// <summary>적 공격 설정의 탄환을 부채꼴로 발사한다.</summary>
        private void ShootFan(SimulationEnemy enemy, SimVector direction, int count, double speedMultiplier)
        {
            EnemyDefinition definition = enemy.Definition;
            for (int i = 0; i < count; i++)
            { double spread = count == 1 ? 0 : (i / (double)(count - 1) * 2 - 1) * definition.SpreadDegrees * DEGREES_TO_RADIANS; SimVector aim = direction.Rotated(spread); _enemyProjectiles.Add(new SimulationProjectile(enemy.Position + aim * (enemy.Radius + _balance.Sim.WandOffset), aim, definition.ProjectileSpeed * speedMultiplier, enemy.Damage, definition.ProjectileRadius, definition.ProjectileLifetime)); }
        }

        /// <summary>예고된 조준 방향에 첫 탄을 발사하고 데이터 간격에 맞춰 나머지 2발을 예약한다.</summary>
        private void ShootBurst(SimulationEnemy enemy, SimVector direction, double speedMultiplier)
        {
            ShootFan(enemy, direction, 1, speedMultiplier);
            for (int i = 1; i < 3; i++) _pendingEnemyShots.Add(new PendingEnemyShot(_tick + Math.Max(1, (int)Math.Round(i * enemy.Definition.BurstInterval * TICK_RATE)), enemy, direction, speedMultiplier));
        }

        /// <summary>예약 시점이 된 연사 탄환을 생존하며 행동 가능한 적의 현재 위치에서 발사한다.</summary>
        private void RunEnemyShots()
        {
            for (int i = 0; i < _pendingEnemyShots.Count;)
            {
                PendingEnemyShot shot = _pendingEnemyShots[i];
                if (!shot.Owner.IsAlive || shot.Owner.IsPatching || HasEnemyStatus(shot.Owner, EnemyStatusType.Freeze)) { _pendingEnemyShots.RemoveAt(i); continue; }
                if (shot.Tick > _tick) { i++; continue; }
                _pendingEnemyShots.RemoveAt(i); ShootFan(shot.Owner, shot.Direction, 1, shot.SpeedMultiplier);
            }
        }

        /// <summary>
        /// 보스 패턴의 방사 사격, 조준 사격, 예고 장판, 증원 주기 및 접촉 피해를 페이즈에 맞춰 처리한다.
        /// 페이즈 전환과 이동은 호출 전에 끝난 상태여야 하며 조준은 이동 전 방향을 사용한다.
        /// </summary>
        private void AdvanceBoss(SimulationEnemy boss, BossDefinition pattern, SimVector direction)
        {
            double multiplier = boss.Phase == 3 ? pattern.PhaseThreeMultiplier : 1;
            if (Time + 0.000001 >= boss.AttackAt)
            {
                if (boss.Phase == 1 || boss.Phase == 3)
                    for (int i = 0; i < pattern.RadialCount; i++)
                    { double angle = i * 2 * Math.PI / pattern.RadialCount; ShootFan(boss, new SimVector(Math.Cos(angle), Math.Sin(angle)), 1, multiplier); }
                if (boss.Phase >= 2) ShootBurst(boss, direction, multiplier);
                boss.AttackAt = Time + (boss.Phase == 2 ? pattern.AimInterval : pattern.RadialInterval) / multiplier;
            }
            if (boss.Phase >= 2 && Time >= boss.HazardAt)
            { _enemyProjectiles.Add(new SimulationProjectile(_player.Position, SimVector.Zero, 0, pattern.HazardDamage * boss.DamageMultiplier, pattern.HazardRadius, pattern.HazardLifetime + pattern.HazardWarning, true, pattern.HazardWarning)); boss.HazardAt = Time + pattern.HazardInterval / multiplier; }
            if (Time >= boss.ReinforcementAt)
            {
                if (_isMission) SpawnReinforcements(boss, pattern.Reinforcement);
                else for (int i = 0; i < pattern.Reinforcement.Count; i++) SpawnEnemy(pattern.Reinforcement.EnemyId, _map.GetPoint((char)('1' + i * 2)), false, 0);
                boss.ReinforcementAt = Time + pattern.Reinforcement.Interval;
            }
            HurtByContact(boss);
        }

        /// <summary>보스 체력 문턱에서 패치 무적과 최고 피해 태그 잠금을 적용한다.</summary>
        private void CheckBossPatch(SimulationEnemy boss, BossDefinition pattern)
        {
            if (boss.IsPatching) return;
            int next = boss.Phase == 1 && boss.Hp / boss.MaxHp <= pattern.PhaseTwoThreshold ? 2 : boss.Phase == 2 && boss.Hp / boss.MaxHp <= pattern.PhaseThreeThreshold ? 3 : boss.Phase;
            if (next == boss.Phase) return;
            boss.Patch(next, Time, pattern.PatchSeconds); boss.Observe(Time); _lastPatchTags = Adaptation.LockTopDamageTags();
            boss.AttackAt = Time + pattern.PatchSeconds + boss.Definition.WarningSeconds;
        }

        /// <summary>적 탄환과 위험 장판의 수명, 벽 충돌 및 플레이어 피격을 처리한다.</summary>
        private void AdvanceHostileProjectiles()
        {
            for (int i = _enemyProjectiles.Count - 1; i >= 0; i--)
            {
                SimulationProjectile projectile = _enemyProjectiles[i];
                SimVector previous = projectile.Position;
                projectile.Advance(Math.Min(STEP_SECONDS, Math.Max(0, projectile.Lifetime - projectile.Age)));
                if (!projectile.IsHazard)
                {
                    double wall = FirstWallFraction(previous, projectile.Position, projectile.Radius);
                    double hit = SpellGeometry.CircleContactFraction(previous, projectile.Position, _player.Position, projectile.Radius + _sector.PlayerRadius);
                    if (hit < wall && !double.IsPositiveInfinity(hit)) { HurtPlayer(projectile.Damage); _enemyProjectiles.RemoveAt(i); continue; }
                    if (!double.IsPositiveInfinity(wall)) { _enemyProjectiles.RemoveAt(i); continue; }
                }
                else if (!projectile.IsExplosion && !projectile.IsWarning && SimVector.Distance(projectile.Position, _player.Position) <= projectile.Radius + _sector.PlayerRadius)
                    HurtPlayer(projectile.Damage);
                if (projectile.Age + COLLISION_EPSILON >= projectile.Lifetime) _enemyProjectiles.RemoveAt(i);
            }
        }

        /// <summary>
        /// 개체의 자연 소멸(수명·사거리 종료)을 처리하고 OnExpire를 소멸 위치에서 한 번 실행한다.
        /// 소멸 이벤트를 내지 않는 행동(Burst)과 직접 충돌한 발사체는 OnExpire를 내지 않는다. 명중 없이 사라지는 발사체만 OnFirstHitOrExpire도 실행한다.
        /// </summary>
        private void Expire(SimulationSpellEntity entity)
        {
            if (entity.HasExpired) return;
            entity.HasExpired = true;
            if (!entity.Behavior.RaisesExpireEvent || entity.HasDirectHit) return;
            RaiseExpireEvent(entity);
        }

        /// <summary>소멸 이벤트를 개체 위치와 대상 없이 실행한다. OnExpire 다음 첫 이벤트, 호출부 onExpire 분기 순서다.</summary>
        private void RaiseExpireEvent(SimulationSpellEntity entity)
        {
            SpellContext context = new SpellContext(entity.Position, entity.Direction, null, true,
                entity.CastNoiseElement, entity.Modifiers, entity.CallEvents, null, entity.CostMultiplier, entity.Caster, entity.Id);
            bool isFirstEvent = entity.Behavior.TracksDirectHit && entity.TryMarkFirstEvent();
            Execute(entity.Action.OnExpire, context);
            if (isFirstEvent) Execute(entity.Action.OnFirstHitOrExpire, context);
            ExecuteCallEvent(entity, false, isFirstEvent, entity.Position, null);
        }

        /// <summary>적의 처치 재화에 설정 비율을 곱해 내림한 기본 회복량을 최소 1로 보정하고 트리의 추가 전력을 더해 회복한다.</summary>
        private void GrantKillEnergy(int droppedCredits)
        {
            double amount = Math.Floor(droppedCredits * _balance.Economy.KillEnergyCreditRatio);
            _player.RestoreEnergy(Math.Max(MIN_KILL_ENERGY, amount) + _killEnergyBonus);
        }

        /// <summary>처치된 적과 그 상태 이상을 제거하고 시간제 전투의 처치 통계, 목표 처치 수 집계, 보스 처치 판정과 RAM 오브를 생성한다. 분열형은 미션 중 처치되면 자식 생성을 예약한다.</summary>
        private void CleanupEnemies(bool canExplode = true)
        {
            CollectDeadEnemies();
            while (_deadEnemies.Count > 0)
            {
                SimulationEnemy enemy = _deadEnemies.Dequeue();
                int droppedCredits = 0;
                if (_isMission)
                {
                    if (enemy.IsKillTarget) _targetKills++;
                    if (enemy.Boss != null) _isBossDefeated = true;
                    if (enemy.Definition.CanSplit) EnqueueSplits(enemy);
                    if (enemy.Reward > 0)
                    {
                        // 오브에 실제로 담기는 크레딧을 한 번만 산출해 나머지 누적이 이중 반영되지 않게 한다.
                        droppedCredits = ScaleKillScrap(enemy.Reward);
                        _orbs.Add(new FragmentOrb(enemy.Position, droppedCredits));
                    }
                    string dropTableId = enemy.CarrierOwnerId == 0 ? GetDropTableId(enemy) : null;
                    if (dropTableId != null) RollDropTable(_drops.Get(dropTableId), enemy.Position);
                    _killCounts.TryGetValue(enemy.Kind, out int count); _killCounts[enemy.Kind] = count + 1; _killCount++;
                }
                GrantKillEnergy(droppedCredits);
                RemoveEnemyStatuses(enemy.Id);
                if (canExplode && enemy.Definition.HasTrait(EnemyDefinition.TRAIT_DEATH_EXPLOSION))
                { ExplodeEnemy(enemy); CollectDeadEnemies(); }
            }
        }

        /// <summary>죽은 적을 역순으로 목록에서 분리해 사망 처리 큐에 넣는다. 큐에 넣은 적은 다시 발견되지 않는다.</summary>
        private void CollectDeadEnemies()
        {
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                if (_enemies[i].IsAlive) continue;
                _deadEnemies.Enqueue(_enemies[i]); _enemies.RemoveAt(i);
            }
        }

        /// <summary>사망 위치 주변 플레이어와 살아 있는 적에게 단발 폭발 피해를 주고 짧은 표시를 남긴다. 플레이어 강화·학습·DPS는 적용하지 않는다.</summary>
        private void ExplodeEnemy(SimulationEnemy source)
        {
            double radius = source.Definition.ExplosionRadius;
            double damage = source.Definition.ExplosionDamage * source.DamageMultiplier;
            _enemyProjectiles.Add(new SimulationProjectile(source.Position, SimVector.Zero, 0, 0, radius, EXPLOSION_VISUAL_SECONDS, true, 0, true));
            if (SimVector.Distance(source.Position, _player.Position) <= radius + _sector.PlayerRadius) HurtPlayer(damage);
            foreach (SimulationEnemy target in _enemies)
            {
                if (!target.IsAlive || target.IsPatching || SimVector.Distance(source.Position, target.Position) > radius + target.Radius) continue;
                double actual = target.Hurt(damage, Time);
                if (actual > 0) _damageNumbers.Add(new DamageNumber(target.Position, actual, "raw", _tick));
                if (target.IsAlive && target.Boss != null && target.Boss.IsGovernorPattern) CheckBossPatch(target, target.Boss);
            }
        }

        /// <summary>분열형 적이 미션 중 처치되면 자식 수만큼 대기 항목을 만든다. 위치는 부모 진행 방향의 수직선 위에서 자식 반경 간격으로 균등 분배하고, 엘리트 정의는 그대로 물려준다.</summary>
        private void EnqueueSplits(SimulationEnemy parent)
        {
            EnemyDefinition definition = parent.Definition;
            int delayTicks = Math.Max(1, (int)Math.Round(definition.SplitDelay * TICK_RATE));
            SimVector side = new SimVector(-parent.Facing.Y, parent.Facing.X);
            double childRadius = _enemyCatalog.Get(definition.SplitInto).Radius * (parent.Elite != null ? parent.Elite.RadiusMultiplier : 1);
            for (int i = 0; i < definition.SplitCount; i++)
            {
                double offsetRatio = definition.SplitCount == 1 ? 0 : i / (double)(definition.SplitCount - 1) * 2 - 1;
                _pendingSplits.Add(new PendingSplit(_tick + delayTicks, definition.SplitInto, parent.Position + side * (childRadius * offsetRatio), parent.Elite, parent.CarrierOwnerId));
            }
        }

        /// <summary>줍기 반경에 들어온 스크랩을 매 틱 플레이어에게 끌어당기고 도착한 오브만 획득한다.</summary>
        private void CollectNearbyOrbs()
        {
            for (int i = _orbs.Count - 1; i >= 0; i--)
            {
                FragmentOrb orb = _orbs[i];
                if (!TryAttractPickup(orb.Position, orb.IsAttracted, out SimVector nextPosition, out bool hasArrived)) continue;
                if (hasArrived)
                {
                    _collectedFragments += orb.Amount;
                    _orbs.RemoveAt(i);
                }
                else
                {
                    orb.AttractTo(nextPosition);
                    _orbs[i] = orb;
                }
            }
        }

        /// <summary>공통 줍기 반경과 추적 상태로 자석 이동 여부를 반환하고 다음 위치와 플레이어 도착 여부를 계산한다.</summary>
        private bool TryAttractPickup(SimVector position, bool isAttracted, out SimVector nextPosition, out bool hasArrived)
        {
            SimVector offset = _player.Position - position;
            double distance = offset.Length;
            nextPosition = position;
            hasArrived = false;
            if (!isAttracted && distance > _balance.Economy.OrbAbsorbRadius * _scrapPickupRangeMultiplier) return false;
            double step = _balance.Economy.PickupAttractSpeed * STEP_SECONDS;
            hasArrived = distance <= step;
            nextPosition = hasArrived ? _player.Position : position + offset * (step / distance);
            return true;
        }

        /// <summary>전투 종료 시 획득하지 않은 스크랩 오브를 보상에 더하지 않고 정리한다.</summary>
        private void DiscardUncollectedOrbs() { _orbs.Clear(); }

        /// <summary>클리어 시 남은 스크랩을 획득량에 더하고 Modifier를 누적·미지급 목록으로 옮긴 뒤 바닥 드롭을 비운다.</summary>
        private void CollectRemainingDrops()
        {
            foreach (FragmentOrb orb in _orbs) _collectedFragments += orb.Amount;
            _orbs.Clear();
            _pickedUpItems.AddRange(_itemDrops);
            _newPickedItems.AddRange(_itemDrops);
            _itemDrops.Clear();
        }

        /// <summary>
        /// 처치된 적의 드롭 테이블 ID를 반환하고 없으면 null을 반환한다. 엘리트는 엘리트 정의의 테이블을 쓴다.
        /// 엘리트·보스가 아닌 일반 적은 보스 스테이지가 아닐 때 일반 적 드롭 확률로 1회 판정해 당첨되면 설정된 테이블을 쓴다.
        /// </summary>
        private string GetDropTableId(SimulationEnemy enemy)
        {
            if (enemy.Elite != null) return string.IsNullOrEmpty(enemy.Elite.DropTable) ? null : enemy.Elite.DropTable;
            NormalEnemyDropDefinition normalDrop = _drops.NormalEnemyDrop;
            if (enemy.Boss != null || _stageDefinition.IsBoss || normalDrop == null || !normalDrop.IsEnabled) return null;
            return NextRandom() * 100 < normalDrop.ChancePercent ? normalDrop.DropTable : null;
        }

        /// <summary>적 처치 스크랩에 획득 배율을 적용하고 소수 보너스를 다음 드롭으로 이월해 정수 스크랩량을 반환한다.</summary>
        private int ScaleKillScrap(int amount)
        {
            double scaled = amount * _scrapGainMultiplier + _scrapRemainder;
            int result = (int)Math.Floor(scaled + 0.000001);
            _scrapRemainder = Math.Max(0, scaled - result);
            return result;
        }

        /// <summary>드롭 테이블을 결정적 난수로 추첨해 스크랩은 배율이 적용된 오브로, 모디·속성·행동은 바닥 드롭 개체로 처치 위치에 떨군다.</summary>
        private void RollDropTable(DropTableDefinition table, SimVector position)
        {
            foreach (DropRoll roll in table.Roll(NextRandom))
            {
                if (roll.Type == DropCatalog.TYPE_FRAGMENTS) _orbs.Add(new FragmentOrb(position, ScaleKillScrap(roll.Fragments)));
                else if (roll.Type == DropCatalog.TYPE_MODIFIER) DropModifier(roll, position);
                else if (roll.Type == DropCatalog.TYPE_ELEMENT) _itemDrops.Add(new SimulationItemDrop(roll.RuneId, "", roll.Count, position));
                else if (roll.Type == DropCatalog.TYPE_BEHAVIOR) DropBehavior(roll, position);
            }
        }

        /// <summary>행동 추첨 결과를 바닥 드롭 개체로 만든다. 항목 등급이 비어 있으면 현재 스테이지의 행동 등급 확률로 굴리고, 확률이 없는 스테이지(보스)면 드롭하지 않는다.</summary>
        private void DropBehavior(DropRoll roll, SimVector position)
        {
            string grade = roll.Grade;
            if (string.IsNullOrEmpty(grade))
            {
                IReadOnlyList<EliteModifierGradeChance> chances = _eliteDrops.GetBehaviorGradeChances(_stageNumber);
                if (chances == null) return;
                grade = RollSlotGrade(chances);
                if (grade == null) return;
            }
            _itemDrops.Add(new SimulationItemDrop(roll.RuneId, grade, roll.Count, position));
        }

        /// <summary>Modifier 추첨 결과를 바닥 드롭 개체로 만든다. eliteModifier 항목은 등급과 종류를 등급표로 정하고, 지정 Modifier 항목은 항목 등급(없으면 최저 등급)을 쓴다.</summary>
        private void DropModifier(DropRoll roll, SimVector position)
        {
            bool isEliteEntry = roll.RuneId == DropCatalog.ELITE_MODIFIER_ENTRY_ID;
            string grade = isEliteEntry ? RollEliteModifierGrade()
                : string.IsNullOrEmpty(roll.Grade) ? GameData.ModifierGrades.GetLowestAvailableGrade(roll.RuneId) : roll.Grade;
            if (isEliteEntry && grade == null) return;
            string runeId = isEliteEntry ? PickEliteModifier(grade) : roll.RuneId;
            if (runeId == null) return;
            _itemDrops.Add(new SimulationItemDrop(runeId, grade, roll.Count, position));
        }

        /// <summary>현재 스테이지의 등급 확률표(C→S 순서)로 엘리트 Modifier 등급을 1회 추첨한다. 보스 스테이지처럼 등급표가 없으면 null을 반환하고 난수를 쓰지 않는다.</summary>
        private string RollEliteModifierGrade()
        {
            IReadOnlyList<EliteModifierGradeChance> chances = _eliteDrops.GetGradeChances(_stageNumber);
            if (chances == null || chances.Count == 0) return null;
            double choice = NextRandom() * 100;
            foreach (EliteModifierGradeChance chance in chances)
            {
                choice -= chance.Percent;
                if (choice <= 0) return chance.Grade;
            }
            return chances[chances.Count - 1].Grade;
        }

        /// <summary>등급 값이 null이 아닌 Modifier 중 균등 추첨으로 하나를 고른다. 후보가 없으면 null을 반환한다.</summary>
        private string PickEliteModifier(string grade)
        {
            IReadOnlyList<string> candidates = _eliteDrops.GetModifiersWithGrade(grade);
            if (candidates.Count == 0) return null;
            return candidates[(int)(NextRandom() * candidates.Count)];
        }

        /// <summary>스크랩과 같은 반경에서 Modifier를 끌어당기고 플레이어에게 도착하면 누적 목록과 미지급 목록에 넣는다.</summary>
        private void CollectNearbyItemDrops()
        {
            for (int i = _itemDrops.Count - 1; i >= 0; i--)
            {
                SimulationItemDrop drop = _itemDrops[i];
                if (!TryAttractPickup(drop.Position, drop.IsAttracted, out SimVector nextPosition, out bool hasArrived)) continue;
                drop.AttractTo(nextPosition);
                if (!hasArrived) continue;
                _itemDrops.RemoveAt(i);
                _pickedUpItems.Add(drop);
                _newPickedItems.Add(drop);
            }
        }

        /// <summary>
        /// 일반 스테이지는 목표 처치 수, 보스 스테이지는 보스 처치로 클리어하고 남은 스크랩·Modifier를 모두 회수한다.
        /// 보스 제한시간 초과는 미획득 스크랩을 버리며, 클리어는 남은 적·상태 이상도 정리하고 같은 틱의 시간 초과보다 우선한다.
        /// </summary>
        private void CheckBattleEnd()
        {
            if (IsBossStage ? _isBossDefeated : _targetKills >= _killTarget) _stage = MissionStage.Cleared;
            else if (IsBossStage && Time + 0.000001 >= TimeLimit) _stage = MissionStage.TimedOut;
            else return;
            if (_stage == MissionStage.Cleared) CollectRemainingDrops();
            else DiscardUncollectedOrbs();
            CancelCombat();
            if (_stage != MissionStage.Cleared) return;
            _enemies.Clear(); _enemyStatuses.Clear(); _statusIndex.Clear();
        }

        /// <summary>
        /// 스테이지 편성으로 외곽 벽만 있는 맵, 맵 중앙의 플레이어, 스폰 반경, 목표 처치 수와 스폰 진행을 준비하고 0틱 예약을 바로 스폰한다.
        /// 스폰 반경 = 기본 발사 사거리 + 가장 큰 적 반경 + 여유이며 막 스폰된 적에게 기본 발사체가 닿지 않게 한다.
        /// 적 분리 격자를 맵 크기와 적 상한, 가장 큰 적 반경의 2배 칸 크기로 준비한다. 가장 큰 적 반경에는 이 스테이지 엘리트 타입의 반경 배율을 반영한다.
        /// </summary>
        private void BeginTimedBattle()
        {
            _stage = MissionStage.Combat;
            _stageDefinition = _stages.Get(_stageNumber);
            _map = MissionMap.CreateOpen(_stages.Map.Columns, _stages.Map.Rows, _sector.TileSize);
            _player = new SimulationPlayer(_maxHp, _maxEnergy, MapCenter);
            Adaptation.SetEnabled(false);
            RuneStats bolt = GameData.Runes.Get(BOLT_RUNE_ID).Stats;
            double largestRadius = 0;
            foreach (EnemyDefinition enemy in _enemyCatalog.Enemies) largestRadius = Math.Max(largestRadius, enemy.Radius);
            foreach (EliteSpawnDefinition elite in _stageDefinition.EliteTypes) largestRadius = Math.Max(largestRadius, _enemyCatalog.Get(elite.EnemyId).Radius * elite.RadiusMultiplier);
            _separation.Resize(_map.Width, _map.Height, EnemyLimit, largestRadius * 2);
            _spawnRadius = (double)bolt.Speed * bolt.Lifetime + largestRadius + _stages.Spawn.RingMargin;
            // 보스 스테이지는 처치로 종료하고, 일반 스테이지는 11스테이지부터 목표 처치 수를 50으로 고정한다.
            _killTarget = _stages.GetKillTarget(_stageDefinition, _stageNumber);
            _targetKills = 0; _isBossDefeated = false;
            _director = new SpawnDirector(_stageDefinition, _stageNumber, _formations, TICK_RATE);
            AdvanceSpawns();
        }

        /// <summary>현재 틱까지 도달한 스폰을 순서대로 처리한다. 기본 흐름 스폰 순번이 엘리트 주기와 일치하면 그 종족을 엘리트로 생성한다. 적 수가 상한이면 이번 틱 처리를 멈춘다.</summary>
        private void AdvanceSpawns()
        {
            while (_director.TryPeek(_tick, out SpawnOrder order))
            {
                if (_enemies.Count >= EnemyLimit) return;
                _director.Consume(_tick);
                bool isStream = order.IsStream;
                EliteSpawnDefinition elite = null;
                if (isStream && _stageDefinition.TryGetElite(++_streamSpawns, out EliteSpawnDefinition found)) elite = found;
                SpawnByFormation(order, elite != null ? elite.EnemyId : (isStream ? PickStreamEnemy() : order.EnemyId), elite, isKillTarget: true);
            }
        }

        /// <summary>분열 대기 항목 중 생성 틱에 도달한 것을 목록 순서대로 생성한다. 적 수가 상한이면 남은 항목을 다음 틱에 다시 시도한다.</summary>
        private void AdvanceSplits()
        {
            for (int i = 0; i < _pendingSplits.Count;)
            {
                if (_enemies.Count >= EnemyLimit) return;
                PendingSplit split = _pendingSplits[i];
                if (split.Tick > _tick) { i++; continue; }
                _pendingSplits.RemoveAt(i);
                SpawnEnemy(split.EnemyId, split.Position, false, 0, split.Elite, carrierOwnerId: split.CarrierOwnerId);
            }
        }

        /// <summary>
        /// 예약의 진형으로 플레이어 주변 원형 슬롯 중 위치를 정해 적을 생성한다. elite가 있으면 엘리트 배율로 생성하고,
        /// isKillTarget은 기본 흐름과 웨이브 스폰에 true로 받아 목표 처치 수 집계 대상으로 만든다.
        /// 묶음의 첫 스폰에서 슬롯 시작 각도(무작위 또는 진행 방향)와 기준 슬롯을 정하고, 위치는 스폰 순간의 플레이어 위치 기준으로 계산한다.
        /// </summary>
        private void SpawnByFormation(SpawnOrder order, string enemyId, EliteSpawnDefinition elite, bool isKillTarget)
        {
            FormationSettings settings = _director.GetSettings(order.Group);
            SpawnFormation formation = SpawnFormations.Get(settings.Shape);
            double radius = _enemyCatalog.Get(enemyId).Radius * (elite != null ? elite.RadiusMultiplier : 1);
            int slotCount = settings.Slots > 0 ? settings.Slots : _stages.Spawn.RingSlots;
            if (!_director.IsStarted(order.Group))
            {
                double startAngle = settings.Angle == SpawnFormations.ANGLE_AHEAD ? GetAheadAngle() : NextRandom() * 2 * Math.PI / slotCount;
                _spawnSlots.Refresh(_map, _player.Position, _spawnRadius, slotCount, startAngle, radius);
                _director.Start(order.Group, startAngle, formation.ChooseAnchor(_spawnSlots, settings));
            }
            else _spawnSlots.Refresh(_map, _player.Position, _spawnRadius, slotCount, _director.GetStartAngle(order.Group), radius);
            SpawnPlacement placement = formation.Place(order.Index, order.Count, _director.GetAnchor(order.Group), _spawnSlots, settings);
            SpawnEnemy(enemyId, ResolvePlacement(placement, radius), false, 0, elite, isKillTarget);
        }

        /// <summary>보스 증원 설정으로 보스 주변 원 위에 같은 간격으로 증원을 생성한다. 시작 각도는 보스가 바라보는 방향이며 맵 밖 위치는 원형 슬롯과 같이 제외한다.</summary>
        private void SpawnReinforcements(SimulationEnemy boss, BossReinforcement reinforcement)
        {
            int count = reinforcement.Count;
            double radius = _enemyCatalog.Get(reinforcement.EnemyId).Radius;
            _spawnSlots.Refresh(_map, boss.Position, boss.Radius + _stages.Spawn.ReinforcementGap, count, Math.Atan2(boss.Facing.Y, boss.Facing.X), radius);
            SpawnFormation formation = SpawnFormations.Get(_formations.Get(reinforcement.Formation).Shape);
            for (int i = 0; i < count; i++)
                SpawnEnemy(reinforcement.EnemyId, ResolvePlacement(formation.Place(i, count, -1, _spawnSlots, default), radius), false, 0);
        }

        /// <summary>
        /// 배치 결과를 마지막으로 Refresh한 슬롯 기준 좌표로 바꾼다. 쓸 슬롯이 없으면 맵 안에서 플레이어로부터 가장 먼 지점을 쓰고,
        /// 바깥 오프셋 위치가 맵 밖이면 슬롯 위치를 쓰며, 같은 틱에 같은 위치로 나온 적이 있으면 흔들어 겹치지 않게 한다.
        /// </summary>
        private SimVector ResolvePlacement(SpawnPlacement placement, double radius)
        {
            if (!placement.HasSlot) return GetFarthestPoint(_player.Position, radius);
            SimVector position = _spawnSlots.GetPosition(placement.Slot);
            if (placement.Outward > 0)
            {
                SimVector pushed = position + _spawnSlots.GetDirection(placement.Slot) * placement.Outward;
                if (_map.CanOccupy(pushed, radius)) position = pushed;
            }
            return SpreadSameTickSpawn(position, radius);
        }

        /// <summary>이번 틱에 같은 위치로 스폰된 적이 있으면 ±(반경 × 흔들림 배율) 안의 맵 안 위치로 옮겨 반환한다.</summary>
        private SimVector SpreadSameTickSpawn(SimVector position, double radius)
        {
            if (_tickSpawnPositionsTick != _tick) { _tickSpawnPositionsTick = _tick; _tickSpawnPositions.Clear(); }
            bool isTaken = false;
            foreach (SimVector taken in _tickSpawnPositions)
                if ((taken - position).LengthSquared < SAME_SPAWN_DISTANCE * SAME_SPAWN_DISTANCE) { isTaken = true; break; }
            _tickSpawnPositions.Add(position);
            if (!isTaken) return position;
            double range = radius * _stages.Spawn.JitterScale;
            SimVector spread = position + new SimVector((NextRandom() * 2 - 1) * range, (NextRandom() * 2 - 1) * range);
            return _map.CanOccupy(spread, radius) ? spread : position;
        }

        /// <summary>외곽 벽 안쪽으로 반경만큼 들어온 맵 네 모서리 중 기준점에서 가장 먼 곳을 반환한다.</summary>
        private SimVector GetFarthestPoint(SimVector origin, double radius)
        {
            double inset = _map.TileSize + radius;
            SimVector best = origin; double bestDistance = -1;
            for (int corner = 0; corner < 4; corner++)
            {
                var candidate = new SimVector(corner % 2 == 0 ? inset : _map.Width - inset, corner < 2 ? inset : _map.Height - inset);
                double distance = (candidate - origin).LengthSquared;
                if (distance > bestDistance) { best = candidate; bestDistance = distance; }
            }
            return best;
        }

        /// <summary>마지막 이동 입력 방향의 각도를 반환하며 이동한 적이 없으면 조준 방향 각도를 반환한다.</summary>
        private double GetAheadAngle()
        {
            SimVector direction = _hasMoveDirection ? _moveDirection : _player.AimDirection;
            return Math.Atan2(direction.Y, direction.X);
        }

        /// <summary>기본 흐름 후보 중 현재 스테이지에서 나올 수 있는 적을 가중 난수로 고른다. 부동소수 오차로 고르지 못하면 마지막 후보를 쓴다.</summary>
        private string PickStreamEnemy()
        {
            IReadOnlyList<StreamCandidate> candidates = _stageDefinition.Stream.Candidates;
            double totalWeight = 0;
            foreach (StreamCandidate candidate in candidates) if (candidate.FirstStage <= _stageNumber) totalWeight += candidate.Weight;
            double choice = NextRandom() * totalWeight;
            string last = null;
            foreach (StreamCandidate candidate in candidates)
            {
                if (candidate.FirstStage > _stageNumber) continue;
                last = candidate.EnemyId;
                choice -= candidate.Weight;
                if (choice <= 0) return candidate.EnemyId;
            }
            return last;
        }

        /// <summary>무반격 더미 적을 지정한 위치와 체력으로 생성한다.</summary>
        private SimulationEnemy SpawnBenchDummy(SimVector position, double hp) => SpawnEnemy("enemy.scout", position, true, hp);

        /// <summary>설정 ID, 위치, 더미 여부, 체력, 엘리트 정의, 목표 집계 여부와 캐리어 소유자 ID로 적을 생성하고 공격 일정을 설정한다. 엘리트는 체력·보상·이동 속도·반경 배율을 적용하며 캐리어 자식의 보상은 0으로 설정한다.</summary>
        public SimulationEnemy SpawnEnemy(string id, SimVector position, bool isDummy = false, double hp = 0, EliteSpawnDefinition elite = null, bool isKillTarget = false, int carrierOwnerId = 0)
        {
            if (Completed || IsFailed || _enemies.Count >= EnemyLimit) return null;
            EnemyDefinition definition = _enemyCatalog.Get(id);
            if (definition.HasTrait(EnemyDefinition.TRAIT_CARRIER) && _carrier == null)
                throw new InvalidOperationException("캐리어 Prefab 생성 설정을 시뮬레이션에 전달해야 합니다.");
            double hpMultiplier = _isMission ? _stages.GetHpMultiplier(_stageNumber) : 1;
            if (elite != null) hpMultiplier *= elite.HpMultiplier;
            double damageMultiplier = _isMission ? _stages.Growth.GetDamageMultiplier(_stageNumber) : 1;
            int reward = _isMission ? _stages.GetReward(id, definition.Reward) : definition.Reward;
            if (elite != null) reward = (int)Math.Round(reward * elite.RewardMultiplier, MidpointRounding.AwayFromZero);
            _bosses.TryGet(id, out BossDefinition boss);
            double radius = definition.Radius * (elite != null ? elite.RadiusMultiplier : 1);
            var enemy = new SimulationEnemy(++_nextEntityId, definition, EnemyMovements.Resolve(definition.Movement), boss, _map.NearestFree(position, radius), isDummy, hp, _balance.Sim.HitFlashSeconds, hpMultiplier, damageMultiplier, carrierOwnerId == 0 ? reward : 0, elite != null, elite != null ? elite.SpeedMultiplier : 1, elite, isKillTarget, carrierOwnerId);
            enemy.AttackAt = Time + definition.AttackInterval;
            if (definition.Attack == EnemyDefinition.ATTACK_SNIPER) enemy.AttackAt = Time;
            if (definition.HasTrait(EnemyDefinition.TRAIT_CARRIER)) enemy.SpawnRemainingSeconds = _carrier.Interval;
            if (boss != null && boss.IsGovernorPattern) { enemy.ReinforcementAt = Time + boss.Reinforcement.Interval; enemy.HazardAt = Time + boss.HazardInterval; }
            enemy.Observe(Time); _enemies.Add(enemy); if (_isMission) _spawnedEnemies++; return enemy;
        }

        /// <summary>사망, 클리어 및 시간 초과 시 지속 개체, 예약 실행과 분열 대기를 취소한다.</summary>
        private void CancelCombat()
        {
            _spellEntities.Clear(); _enemyProjectiles.Clear(); _scheduled.Clear(); _pendingEnemyShots.Clear(); _pendingSplits.Clear(); _deadEnemies.Clear();
            if (_player != null) _player.CancelSpellExecutions();
        }

        /// <summary>노드 실행 틱을 기록하고 누적 실행 수를 증가시킨다.</summary>
        private void RecordNode(string nodeId) { _nodeEvents.Add(new NodeExecutionEvent(nodeId, _tick)); _nodeExecutionCount++; }

        /// <summary>표시 수명이 지난 피해 숫자, 체력 회복 표시, 전격 연쇄 구간, 노드 이벤트 및 5초 DPS 샘플을 제거한다.</summary>
        private void TrimFeedback()
        {
            for (int i = _damageNumbers.Count - 1; i >= 0; i--) if (_tick - _damageNumbers[i].Tick > _balance.Sim.DamageNumberSeconds * TICK_RATE) _damageNumbers.RemoveAt(i);
            for (int i = _healPulses.Count - 1; i >= 0; i--) if (_tick - _healPulses[i].Tick > _balance.Sim.DamageNumberSeconds * TICK_RATE) _healPulses.RemoveAt(i);
            for (int i = _arcLinks.Count - 1; i >= 0; i--) if (_tick - _arcLinks[i].Tick > ARC_LINK_TICKS) _arcLinks.RemoveAt(i);
            for (int i = _nodeEvents.Count - 1; i >= 0; i--) if (_tick - _nodeEvents[i].Tick > TICK_RATE) _nodeEvents.RemoveAt(i);
            while (_damageWindow.Count > 0 && _tick - _damageWindow.Peek().Tick > 5 * TICK_RATE) _rollingDamage -= _damageWindow.Dequeue().Amount;
        }

        /// <summary>시드 기반 mulberry32 연산으로 0 이상 1 미만의 결정적 난수를 반환한다.</summary>
        private double NextRandom()
        {
            unchecked { _rngState += 0x6D2B79F5; uint value = _rngState; value = (value ^ (value >> 15)) * (value | 1); value ^= value + ((value ^ (value >> 7)) * (value | 61)); return (value ^ (value >> 14)) / 4294967296.0; }
        }

        /// <summary>이미 적중한 적을 제외하고 탐지 반경 내 가장 가까운 생존 적을 반환한다.</summary>
        internal SimulationEnemy NearestEnemy(SimVector position, double range, Dictionary<int, double> excluded)
        { SimulationEnemy result = null; double best = range * range; foreach (SimulationEnemy enemy in _enemies) { if (!enemy.IsAlive || excluded.ContainsKey(enemy.Id)) continue; double distance = (enemy.Position - position).LengthSquared; if (distance < best) { result = enemy; best = distance; } } return result; }

        /// <summary>시전의 모든 실행 분기를 읽어 노이즈가 있을 때만 시전 단위 속성 선택이 필요한지 반환한다.</summary>
        private static bool ContainsNoise(IReadOnlyList<SpellAction> actions)
        {
            foreach (SpellAction action in actions)
                if (action.Noise || (action.CalledSpell != null && ContainsNoise(action.CalledSpell.Root))
                    || ContainsNoise(action.OnHit) || ContainsNoise(action.OnExpire) || ContainsNoise(action.OnFirstHitOrExpire)
                    || ContainsNoise(action.Then) || ContainsNoise(action.Else) || ContainsNoise(action.Body) || ContainsNoise(action.OnComplete)
                    || ContainsNoise(action.Next)) return true;
            return false;
        }

        private readonly struct SpellContext
        {
            private readonly SimVector _origin;
            private readonly SimVector _direction;
            private readonly SimulationEnemy _target;
            private readonly bool _fromEvent;
            private readonly string _noiseElement;
            private readonly SpellModifierValues _modifiers;
            private readonly SpellEventScope _callEvents;
            private readonly RepeatScope _repeat;
            private readonly double _costMultiplier;
            private readonly SimulationPlayer _caster;
            private readonly int _sourceEntityId;
            private readonly double _directionOffset;
            private readonly SpellRandomState _randomState;
            public SimVector Origin => _origin;
            public SimVector Direction => _direction;
            public SimulationEnemy Target => _target;
            public bool FromEvent => _fromEvent;
            public string NoiseElement => _randomState.NoiseElement ?? _noiseElement;
            public SpellModifierValues Modifiers => _modifiers;
            public SpellEventScope CallEvents => _callEvents;
            public double DirectionOffset => _directionOffset;
            public SpellRandomState RandomState => _randomState;

            /// <summary>이 실행 경로가 속한 가장 안쪽 Repeat다. 개체 이벤트로 시작한 경로는 Repeat에 속하지 않는다.</summary>
            public RepeatScope Repeat => _repeat;

            /// <summary>이 경로의 노드 비용에 곱하는 배율이다. 프리셋 호출 안에서는 호출 노드 효과의 비용 배율이 곱해진다.</summary>
            public double CostMultiplier => _costMultiplier;

            /// <summary>마법을 시전한 주체다. Apply 등 자기 적용 효과의 대상이다.</summary>
            public SimulationPlayer Caster => _caster;

            /// <summary>이 경로를 시작한 이벤트의 원본 개체 ID다. 시전·흐름 제어로 시작한 경로는 0이다.</summary>
            public int SourceEntityId => _sourceEntityId;

            /// <summary>
            /// 발생 위치, 방향, 선택 대상, 이벤트 유래 여부, 소속 Repeat, 비용 배율, 시전자와 이벤트 원본을 예약 가능한 스냅샷으로 보관한다.
            /// </summary>
            public SpellContext(SimVector origin, SimVector direction, SimulationEnemy target, bool fromEvent,
                string noiseElement, SpellModifierValues modifiers = null, SpellEventScope callEvents = null, RepeatScope repeat = null,
                double costMultiplier = 1d, SimulationPlayer caster = null, int sourceEntityId = 0,
                double directionOffset = 0, SpellRandomState randomState = null)
            {
                _origin = origin; _direction = direction; _target = target; _fromEvent = fromEvent; _noiseElement = noiseElement;
                _modifiers = modifiers ?? SpellModifierValues.None; _callEvents = callEvents; _repeat = repeat; _costMultiplier = costMultiplier;
                _caster = caster; _sourceEntityId = sourceEntityId; _directionOffset = directionOffset;
                _randomState = randomState ?? new SpellRandomState(noiseElement);
            }

            /// <summary>소속 Repeat만 바꾼 문맥을 반환한다.</summary>
            public SpellContext WithRepeat(RepeatScope repeat)
                => new SpellContext(_origin, _direction, _target, _fromEvent, _noiseElement, _modifiers, _callEvents, repeat, _costMultiplier, _caster, _sourceEntityId, _directionOffset, _randomState);

            /// <summary>실행 위치만 바꾼 문맥을 반환한다.</summary>
            public SpellContext WithOrigin(SimVector origin)
                => new SpellContext(origin, _direction, _target, _fromEvent, _noiseElement, _modifiers, _callEvents, _repeat, _costMultiplier, _caster, _sourceEntityId, _directionOffset, _randomState);

            /// <summary>시전 시점 문맥의 원점과 방향을 완료 순간 값으로 바꾼 복사본을 반환한다.</summary>
            public SpellContext WithOriginAndDirection(SimVector origin, SimVector direction)
                => new SpellContext(origin, direction, _target, _fromEvent, _noiseElement, _modifiers, _callEvents, _repeat, _costMultiplier, _caster, _sourceEntityId, _directionOffset, _randomState);
        }

        /// <summary>같은 루트 시전에서 첫 Noise 효과 시 정한 원소를 공유한다.</summary>
        private sealed class SpellRandomState
        {
            private string _noiseElement;
            public string NoiseElement => _noiseElement;

            /// <summary>선택된 원소를 공유 상태에 저장한다.</summary>
            public SpellRandomState(string noiseElement = null) { _noiseElement = noiseElement; }

            /// <summary>아직 선택하지 않은 경우 첫 효과 시점의 원소를 기록한다.</summary>
            public void SetNoiseElement(string noiseElement)
            {
                if (_noiseElement == null) _noiseElement = noiseElement;
            }
        }

        /// <summary>슬롯과 컴파일 마법을 루트 체인 완료 시점까지 연결한다.</summary>
        private sealed class RootSpellExecution
        {
            private readonly int _slot;
            public int Slot => _slot;

            /// <summary>Tracks which player slot should be marked complete when the root execution finishes.</summary>
            public RootSpellExecution(int slot) { _slot = slot; }
        }

        /// <summary>분기와 예약된 후속 노드의 대기 수를 세고 모두 끝나면 콜백을 한 번 호출한다.</summary>
        private sealed class ExecutionTracker
        {
            private readonly RootSpellExecution _rootCast;
            private readonly Action _onCompleted;
            private int _pending;
            private int _dispatchDepth;
            private bool _isCompleted;
            public RootSpellExecution RootCast => _rootCast;
            public int Pending => _pending;

            /// <summary>루트 시전 범위와 완료 작업을 추적기에 연결한다.</summary>
            public ExecutionTracker(RootSpellExecution rootCast, Action onCompleted)
            {
                _rootCast = rootCast;
                _onCompleted = onCompleted;
            }

            /// <summary>동일 출력의 병렬 노드를 모두 등록할 때까지 완료 확인을 보류한다.</summary>
            public void BeginDispatch() { _dispatchDepth++; }

            /// <summary>노드 목록 등록을 끝내고 비어 있는 실행 분기를 완료한다.</summary>
            public void EndDispatch() { _dispatchDepth--; TryComplete(); }

            /// <summary>실행 중인 노드 또는 대기 예약 하나를 추적한다.</summary>
            public void Acquire() { _pending++; }

            /// <summary>노드 실행을 끝내고 마지막 대기 작업이면 완료 콜백을 호출한다.</summary>
            public void Release() { _pending--; TryComplete(); }

            /// <summary>실행 대기 수와 분기 등록이 모두 끝난 경우 완료 작업을 한 번 실행한다.</summary>
            private void TryComplete()
            {
                if (_isCompleted || _pending != 0 || _dispatchDepth != 0) return;
                _isCompleted = true;
                _onCompleted?.Invoke();
            }
        }

        /// <summary>
        /// 실행 중인 Repeat 하나의 완료 추적 상태다. 대기 수는 아직 끝나지 않은 회차 예약·Delay 예약·안쪽 Repeat와
        /// 회차 예약 단계 자체를 센다. 0이 되면 완료이며, 실패 표시가 있으면 OnComplete를 실행하지 않는다.
        /// </summary>
        private sealed class RepeatScope
        {
            private readonly IReadOnlyList<SpellAction> _onComplete;
            private readonly SpellContext _completeContext;
            private readonly RepeatScope _parent;
            private readonly ExecutionTracker _executionTracker;
            private int _pending;
            private bool _isFailed;
            public IReadOnlyList<SpellAction> OnComplete => _onComplete;
            public SpellContext CompleteContext => _completeContext;
            public RepeatScope Parent => _parent;
            public ExecutionTracker ExecutionTracker => _executionTracker;
            public bool IsFailed => _isFailed;

            /// <summary>완료 분기와 Repeat 진입 문맥(바깥 Repeat 소속), 바깥 Repeat로 추적 상태를 만든다.</summary>
            public RepeatScope(IReadOnlyList<SpellAction> onComplete, SpellContext entryContext, RepeatScope parent,
                ExecutionTracker executionTracker)
            {
                _onComplete = onComplete;
                _completeContext = entryContext;
                _parent = parent;
                _executionTracker = executionTracker;
            }

            /// <summary>기다릴 작업을 하나 추가한다.</summary>
            public void Acquire() { _pending++; }

            /// <summary>기다리던 작업 하나를 끝내고 모든 작업이 끝났으면 true를 반환한다.</summary>
            public bool Release() { _pending--; return _pending == 0; }

            /// <summary>회차 실패를 표시한다. 남은 회차는 실행하지 않으며 OnComplete도 실행하지 않는다.</summary>
            public void Fail() { _isFailed = true; }

            /// <summary>대기 수와 실패 여부를 바깥 Repeat까지 결정성 해시 버퍼에 기록한다.</summary>
            public void AppendState(StringBuilder state)
            {
                for (RepeatScope scope = this; scope != null; scope = scope._parent)
                    state.Append("|r").Append(scope._pending).Append(scope._isFailed ? 'f' : 'o');
            }
        }

        private readonly struct ScheduledExecution
        {
            private readonly int _tick;
            private readonly IReadOnlyList<SpellAction> _actions;
            private readonly SpellAction _action;
            private readonly SpellContext _context;
            private readonly bool _isIteration;
            private readonly bool _isBranchStart;
            private readonly ExecutionTracker _tracker;
            public int Tick => _tick;
            public IReadOnlyList<SpellAction> Actions => _actions;
            public SpellAction Action => _action;
            public SpellContext Context => _context;
            public ExecutionTracker Tracker => _tracker;

            /// <summary>Repeat의 남은 회차 예약인지 나타낸다. 소속 Repeat가 실패하면 실행하지 않는다.</summary>
            public bool IsIteration => _isIteration;
            public bool IsBranchStart => _isBranchStart;

            /// <summary>목표 틱, Repeat 실행 목록, 컨텍스트와 회차 여부를 예약 실행 데이터에 보관한다.</summary>
            public ScheduledExecution(int tick, IReadOnlyList<SpellAction> actions, SpellContext context, bool isIteration)
            { _tick = tick; _actions = actions; _action = null; _context = context; _isIteration = isIteration; _isBranchStart = false; _tracker = null; }

            /// <summary>목표 틱, 완료할 노드, 문맥과 실행 추적기를 저장한다.</summary>
            public ScheduledExecution(int tick, SpellAction action, SpellContext context, ExecutionTracker tracker)
            { _tick = tick; _actions = null; _action = action; _context = context; _isIteration = false; _isBranchStart = false; _tracker = tracker; }

            /// <summary>Stores a root branch scheduled to start after the other root branches.</summary>
            public ScheduledExecution(int tick, SpellAction action, SpellContext context, ExecutionTracker tracker, bool isBranchStart)
            { _tick = tick; _actions = null; _action = action; _context = context; _isIteration = false; _isBranchStart = isBranchStart; _tracker = tracker; }
        }

        private readonly struct DamageSample
        {
            private readonly int _tick;
            private readonly double _amount;
            public int Tick => _tick;
            public double Amount => _amount;
            /// <summary>피해 틱과 실제 적용량을 5초 롤링 DPS 계산용으로 보관한다.</summary>
            public DamageSample(int tick, double amount) { _tick = tick; _amount = amount; }
        }

        private readonly struct PendingEnemyShot
        {
            private readonly int _tick;
            private readonly SimulationEnemy _owner;
            private readonly SimVector _direction;
            private readonly double _speedMultiplier;
            public int Tick => _tick;
            public SimulationEnemy Owner => _owner;
            public SimVector Direction => _direction;
            public double SpeedMultiplier => _speedMultiplier;

            /// <summary>연사 탄환의 발사 틱, 소유 적, 방향 및 속도 배율을 보관한다.</summary>
            public PendingEnemyShot(int tick, SimulationEnemy owner, SimVector direction, double speedMultiplier) { _tick = tick; _owner = owner; _direction = direction; _speedMultiplier = speedMultiplier; }
        }

        private readonly struct PendingSplit
        {
            private readonly int _tick;
            private readonly string _enemyId;
            private readonly SimVector _position;
            private readonly EliteSpawnDefinition _elite;
            private readonly int _carrierOwnerId;
            public int Tick => _tick;
            public string EnemyId => _enemyId;
            public SimVector Position => _position;
            public EliteSpawnDefinition Elite => _elite;
            public int CarrierOwnerId => _carrierOwnerId;

            /// <summary>분열 자식의 생성 틱, 적 ID, 생성 위치와 부모에게서 물려받은 엘리트 정의를 보관한다.</summary>
            public PendingSplit(int tick, string enemyId, SimVector position, EliteSpawnDefinition elite, int carrierOwnerId = 0) { _tick = tick; _enemyId = enemyId; _position = position; _elite = elite; _carrierOwnerId = carrierOwnerId; }
        }
    }
}
