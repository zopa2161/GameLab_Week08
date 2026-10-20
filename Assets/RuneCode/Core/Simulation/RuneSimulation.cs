using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RuneCode
{
    public sealed class RuneSimulation
    {
        public const int TICK_RATE = CompiledSpell.SIMULATION_TICK_RATE;
        private const int MAX_BEAM_FLASHES = 128;
        public const int ARC_LINK_TICKS = 12;
        private const int RELAY_REGEN_INTERVAL_TICKS = 60;
        private const double MIN_KILL_ENERGY = 1;
        internal const double STEP_SECONDS = 1.0 / TICK_RATE;
        private const double DEGREES_TO_RADIANS = Math.PI / 180;
        private const int STATUS_TYPE_COUNT = 4;
        private const double SAME_SPAWN_DISTANCE = 0.5;
        private const int ARENA_PICK_ROUNDS = 2;
        private const string BOLT_RUNE_ID = "form.bolt";
        private const double BEAM_CLIP_MIN_STEP = 4;
        private const double BEAM_CLIP_STEP_RATIO = 0.25;
        private const int BEAM_CLIP_REFINEMENT_STEPS = 6;
        private const double EXPLOSION_VISUAL_SECONDS = 0.25;
        internal const double COLLISION_EPSILON = 0.000001;
        private const int WALL_CONTACT_REFINEMENT_STEPS = 12;
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
        private readonly double _scrapHealRatio;
        private readonly double _killEnergyBonus;
        private readonly List<SimulationEnemy> _enemies = new List<SimulationEnemy>();
        private readonly List<SimulationSpellEntity> _spellEntities = new List<SimulationSpellEntity>();
        private readonly List<SimulationBeamFlash> _beamFlashes = new List<SimulationBeamFlash>();
        private readonly List<SimulationProjectile> _enemyProjectiles = new List<SimulationProjectile>();
        private readonly List<FragmentOrb> _orbs = new List<FragmentOrb>();
        private readonly List<SimulationItemDrop> _itemDrops = new List<SimulationItemDrop>();
        private readonly List<FragmentOrb> _clearOrbs = new List<FragmentOrb>();
        private readonly List<SimulationItemDrop> _clearItemDrops = new List<SimulationItemDrop>();
        private readonly List<SimulationItemDrop> _pickedUpItems = new List<SimulationItemDrop>();
        private readonly List<SimulationItemDrop> _newPickedItems = new List<SimulationItemDrop>();
        private readonly List<DamageNumber> _damageNumbers = new List<DamageNumber>();
        private readonly List<HealPulse> _healPulses = new List<HealPulse>();
        private readonly List<SpawnWarning> _spawnWarnings = new List<SpawnWarning>();
        private readonly List<ArcLink> _arcLinks = new List<ArcLink>();
        private readonly List<NodeExecutionEvent> _nodeEvents = new List<NodeExecutionEvent>();
        private readonly List<ScheduledExecution> _scheduled = new List<ScheduledExecution>();
        private readonly List<PendingEnemyShot> _pendingEnemyShots = new List<PendingEnemyShot>();
        private readonly List<PendingSplit> _pendingSplits = new List<PendingSplit>();
        private readonly List<PendingSpawn> _pendingSpawns = new List<PendingSpawn>();
        private readonly List<EnemyStatusEffect> _enemyStatuses = new List<EnemyStatusEffect>();
        private readonly Dictionary<int, EnemyStatusEffect> _statusIndex = new Dictionary<int, EnemyStatusEffect>();
        private readonly Queue<DamageSample> _damageWindow = new Queue<DamageSample>();
        private readonly Dictionary<SpellAction, ActionCombatStats> _actionCombatStats = new Dictionary<SpellAction, ActionCombatStats>();
        private readonly Dictionary<SpellAction, int> _actionLogSlots = new Dictionary<SpellAction, int>();
        private readonly Dictionary<string, int> _killCounts = new Dictionary<string, int>();
        private readonly CompiledSpell[] _loadout = new CompiledSpell[3];
        private SimVector? _aimPoint;
        private readonly bool[] _castHeld = new bool[3];
        private readonly bool[] _beamQueued = new bool[3];
        private readonly List<BeamBehaviorRuntime.BeamChannel> _beamChannels = new List<BeamBehaviorRuntime.BeamChannel>();
        private readonly List<string> _unlockedElements = new List<string> { "fire" };
        private readonly List<SimVector> _tickSpawnPositions = new List<SimVector>();
        private readonly SpawnSlots _spawnSlots;
        private readonly ArenaSpawnPoints _arenaSpawnPoints;
        private readonly Func<SimVector, bool> _isArenaPointReserved;
        private readonly EnemySeparation _separation = new EnemySeparation();
        private readonly Func<SimulationEnemy, bool> _isSeparationFixed;
        private readonly SimRandom _random;
        private SimulationPlayer _player;
        private MissionMap _map;
        private MissionStage _stage;
        private int _tick;
        private double _statusTime;
        private int _nextEntityId;
        private int _beamFlashGeneration;
        private StageDefinition _stageDefinition;
        private SpawnDirector _director;
        private double _spawnRadius;
        private int _tickSpawnPositionsTick = -1;
        private SimVector _moveDirection;
        private bool _hasMoveDirection;
        private int _streamSpawns;
        private int _spawnedEnemies;
        private int _killCount;
        private int _targetKills;
        private bool _isBossDefeated;
        private int _actionBudgetTick = -1;
        private int _actionsThisTick;
        private int _droppedExecutions;
        private int _collectedFragments;
        private int _normalScrapPickupEventCount;
        private int _bigScrapPickupEventCount;
        private int _bigScrapSpawnEventCount;
        private double _scrapRemainder;
        private double _totalDamage;
        private double _statusDamage;
        private double _persistDamage;
        private double _rollingDamage;
        private readonly Dictionary<string, double> _resourceCostsSpent = new Dictionary<string, double>(StringComparer.Ordinal);
        private int _peakSpellEntities;
        private int _nodeExecutionCount;
        private int _enemyDamageEventCount;
        private int _shotLaunchEventCount;
        private int _shotBurstEventCount;
        private int _shotPersistEventCount;
        private int _shotSlashEventCount;
        private int _burnTickDamageEventCount;
        private int _arcDamageEventCount;
        private int _freezeStartEventCount;
        private string _scenario;
        private bool _debugInvulnerable;
        public int Tick => _tick;
        public double Time => _tick * STEP_SECONDS;
        public SimulationPlayer Player => _player;
        public IReadOnlyList<SimulationEnemy> Enemies => _enemies;
        public IReadOnlyList<SimulationSpellEntity> SpellEntities => _spellEntities;

        /// <summary>장착된 마법 목록이다. 화면에서 시작 노드 직결 범위 미리보기를 조회한다.</summary>
        public IReadOnlyList<CompiledSpell> Loadout => _loadout;

        /// <summary>지면에 배치하는 폭발·잔류 명령인지 반환한다. 궤도 생성 등 다른 모드의 생성 규칙은 보존한다.</summary>
        public static bool IsAreaPreviewAction(SpellAction action)
            => AreaTargeting.IsAreaPreviewAction(action);

        /// <summary>지면 범위 명령 중 공전 모듈이 없는 경우에만 미리보기를 표시할 수 있는지 반환한다.</summary>
        public static bool CanShowAreaPreview(SpellAction action)
            => AreaTargeting.CanShowAreaPreview(action);

        /// <summary>폭발·잔류가 함께 사용하는 플레이어 중심 최대 배치 반경을 반환한다.</summary>
        public double GetAreaPlacementRange() => _balance.Sim.AreaPlacementRange;

        /// <summary>다중 분산을 제외하고 포인터를 최대 사거리로만 제한한 기본 범위 미리보기 위치를 반환한다.</summary>
        public SimVector GetAreaPreviewPosition(SimVector pointer)
            => AreaTargeting.GetAreaPreviewPosition(pointer, _player, GetAreaPlacementRange());

        /// <summary>범위 조준 미리보기의 위치와 방향을 반환한다. 참격은 시전자 중심과 다중 퍼짐 방향을 사용한다.</summary>
        public void GetRangePreview(SpellAction action, SimVector pointer, int index, out SimVector position, out SimVector direction)
            => AreaTargeting.GetRangePreview(this, action, pointer, index, out position, out direction);

        /// <summary>포인터를 시전자 중심 최대 사거리로만 제한하고 index번째 모드 배치를 적용한 생성 위치를 반환한다. 최소 거리는 없다.</summary>
        public SimVector GetAreaTargetPosition(SpellAction action, SimVector pointer, int index = 0, SimVector? playerPosition = null)
            => AreaTargeting.GetAreaTargetPosition(this, action, pointer, index, playerPosition);
        public IReadOnlyList<SimulationProjectile> EnemyProjectiles => _enemyProjectiles;
        /// <summary>짧은 빔의 최초 경로를 화면에서 읽도록 보관한 최근 표시 기록이다. 피해 판정에는 사용하지 않는다.</summary>
        public IReadOnlyList<SimulationBeamFlash> BeamFlashes => _beamFlashes;
        /// <summary>도크 초기화·전투 취소 후 이전 표시 기록을 구분하는 세대 번호다.</summary>
        public int BeamFlashGeneration => _beamFlashGeneration;
        public IReadOnlyList<FragmentOrb> Orbs => _orbs;

        /// <summary>바닥에 떨어져 줍기를 기다리는 Modifier 드롭 목록이다.</summary>
        public IReadOnlyList<SimulationItemDrop> ItemDrops => _itemDrops;
        /// <summary>지급과 분리한 승리 흡수 연출용 잔여 스크랩 위치를 반환한다.</summary>
        public IReadOnlyList<FragmentOrb> ClearOrbs => _clearOrbs;
        /// <summary>지급과 분리한 승리 흡수 연출용 잔여 룬을 반환한다.</summary>
        public IReadOnlyList<SimulationItemDrop> ClearItemDrops => _clearItemDrops;

        /// <summary>이번 전투에서 주워 확정한 Modifier 드롭의 누적 목록이다.</summary>
        public IReadOnlyList<SimulationItemDrop> PickedUpItems => _pickedUpItems;
        public IReadOnlyList<DamageNumber> DamageNumbers => _damageNumbers;
        public int EnemyDamageEventCount => _enemyDamageEventCount;
        public int ShotLaunchEventCount => _shotLaunchEventCount;
        public int ShotBurstEventCount => _shotBurstEventCount;
        public int ShotPersistEventCount => _shotPersistEventCount;
        public int ShotSlashEventCount => _shotSlashEventCount;
        public int BurnTickDamageEventCount => _burnTickDamageEventCount;
        public int ArcDamageEventCount => _arcDamageEventCount;
        public int FreezeStartEventCount => _freezeStartEventCount;
        public IReadOnlyList<HealPulse> HealPulses => _healPulses;

        /// <summary>대기 중인 맵형 스폰의 예고 표시 목록이다. 무리는 하나로 묶여 있으며 표시 전용이다.</summary>
        public IReadOnlyList<SpawnWarning> SpawnWarnings => _spawnWarnings;

        /// <summary>최근 ARC_LINK_TICKS 동안 발생한 전격 연쇄 구간 목록이다. 표시 전용이다.</summary>
        public IReadOnlyList<ArcLink> ArcLinks => _arcLinks;
        public IReadOnlyList<NodeExecutionEvent> NodeEvents => _nodeEvents;
        public IReadOnlyDictionary<string, int> KillCounts => _killCounts;
        public AdaptationNet Adaptation { get; }
        public MissionMap Map => _map;

        /// <summary>전투 규칙 수치다. 행동 실행 동작이 다중 배치 간격 등을 읽는다.</summary>
        internal BalanceData Balance => _balance;
        internal double PlayerRadius => _sector.PlayerRadius;
        public MissionStage Stage => _stage;
        public bool Completed => _stage == MissionStage.Cleared;
        public bool IsDead => _stage == MissionStage.Dead;
        public bool IsTimedOut => _stage == MissionStage.TimedOut;
        public bool IsFailed => IsDead || IsTimedOut;
        public bool IsTimedBattle => _isMission;
        public int StageNumber => _stageNumber;
        public bool IsBossStage => _stageDefinition != null && _stageDefinition.IsBoss;
        public double TimeLimit => _stageDefinition?.TimeLimit ?? 0;
        public double RemainingTime => Math.Max(0, TimeLimit - Time);
        public int TargetKills => _targetKills;
        public SimVector MapCenter => new SimVector(_map.Width * 0.5, _map.Height * 0.5);
        public double EnergyRegen => _energyRegen;
        public int SpawnedEnemies => _spawnedEnemies;
        public int KillCount => _killCount;
        public int EnemyLimit => _balance.Limits.MaxEnemies;
        public int DroppedExecutions => _droppedExecutions;
        public int NormalScrapPickupEventCount => _normalScrapPickupEventCount;
        public int BigScrapPickupEventCount => _bigScrapPickupEventCount;
        public int BigScrapSpawnEventCount => _bigScrapSpawnEventCount;
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
        public bool AdaptationEnabled => Adaptation.Enabled;
        public bool DebugInvulnerable => _debugInvulnerable;
        /// <summary>저격수의 저장된 조준 경로를 벽과 공격 사거리로 제한한 끝점을 반환한다.</summary>
        public SimVector GetSniperEnd(SimulationEnemy enemy) => enemy.AttackOrigin + enemy.AimedDirection * ClipBeamLength(enemy.AttackOrigin, enemy.AimedDirection, enemy.Definition.AttackRange);
        /// <summary>시험 도크의 적응 학습 및 피해 감쇠 사용 여부를 변경한다.</summary>
        public void SetAdaptationEnabled(bool isEnabled) { Adaptation.SetEnabled(isEnabled); }

        /// <summary>명시적으로 열린 디버그 기능에서 플레이어 무적 상태를 변경한다.</summary>
        public void SetDebugInvulnerable(bool isInvulnerable) { _debugInvulnerable = isInvulnerable; }

        /// <summary>시드, 스테이지와 트리 능력치·처치 전력 보너스·스크랩 회복 비율을 받아 독립 전투 또는 시험 도크를 생성한다. 생략한 배율은 1, 보너스는 0을 사용한다.</summary>
        public RuneSimulation(int seed = 1, bool isMission = false, double maxHp = 0, double maxEnergy = 0, int stageNumber = 1, double energyRegen = 0,
            double damageMultiplier = 1, double moveSpeedMultiplier = 1, double scrapGainMultiplier = 1, CarrierDefinition carrier = null,
            double scrapPickupRangeMultiplier = 1, double killEnergyBonus = 0, double scrapHealRatio = 0)
        {
            GameData.Load();
            _killEnergyBonus = Math.Max(0, killEnergyBonus);
            _balance = GameData.Balance;
            _enemyCatalog = GameData.Enemies;
            _carrier = carrier;
            _sector = GameData.Sector;
            _formations = GameData.Formations;
            _bosses = GameData.Bosses;
            _drops = GameData.Drops;
            _eliteDrops = new ModifierGradeDropSource(GameData.ModifierGrades);
            _stages = GameData.Stages;
            _isMission = isMission; _random = new SimRandom(seed); _maxHp = maxHp > 0 ? maxHp : _balance.Player.Hp; _maxEnergy = maxEnergy > 0 ? maxEnergy : _balance.Player.Energy;
            _stageNumber = Math.Max(1, stageNumber);
            _energyRegen = energyRegen > 0 ? energyRegen : _balance.Player.EnergyRegen;
            _damageMultiplier = damageMultiplier;
            _moveSpeedMultiplier = moveSpeedMultiplier;
            _scrapGainMultiplier = scrapGainMultiplier;
            _scrapPickupRangeMultiplier = scrapPickupRangeMultiplier;
            _spawnSlots = new SpawnSlots(_random.Next, _enemies);
            _arenaSpawnPoints = new ArenaSpawnPoints(_random.Next, _enemies);
            _isArenaPointReserved = IsArenaPointReserved;
            _scrapHealRatio = Math.Max(0, Math.Min(1, scrapHealRatio));
            _isSeparationFixed = IsSeparationFixed;
            Adaptation = new AdaptationNet(_balance);
            if (isMission) BeginTimedBattle();
            else ResetBench("dummy_single");
        }

        /// <summary>전투에서는 하나의 마법, 시험 도크에서는 최대 3슬롯의 컴파일 마법을 다음 시전부터 사용한다.</summary>
        public void SetLoadout(IReadOnlyList<CompiledSpell> spells)
        {
            for (int i = 0; i < _loadout.Length; i++)
            {
                CompiledSpell next = i < spells.Count && (!_isMission || i == 0) ? spells[i] : null;
                if (!ReferenceEquals(_loadout[i], next)) CancelBeamChannels();
                _loadout[i] = next;
            }
        }

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
                candidates.Add(pool[Math.Min(pool.Count - 1, (int)(_random.Next() * pool.Count))]);
                grades.Add(grade);
            }
            return candidates.Count > 0;
        }

        /// <summary>등급 확률 목록(퍼센트)으로 등급 하나를 굴려 반환한다. 확률 0인 등급은 뽑지 않는다.</summary>
        private string RollSlotGrade(IReadOnlyList<EliteModifierGradeChance> chances)
        {
            double roll = _random.Next() * 100;
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
        /// 슬롯 쿨다운을 시작하고 루트 실행을 등록한다. 직접 공격 빔은 구간 비용을 선결제하며 연장은 현재 슬롯 입력으로 판단한다.
        /// </summary>
        public bool TryCast(CompiledSpell spell, int slot = 0, bool allowBeamHold = false)
        {
            if (spell == null || slot < 0 || slot >= _loadout.Length || (_stage != MissionStage.Bench && _stage != MissionStage.Combat)) return false;
            if (_isMission && slot != 0) return false;
            if (_player.Cooldowns[slot] > 0.000001 || _player.IsSpellRunning(slot)) return false;
            double executionSeconds = spell.GetEstimatedExecutionSeconds(_balance.Sim.ExecutionTimeScale);
            int cycleTicks = spell.GetSlotCycleTicks(_balance.Sim.ExecutionTimeScale);
            double cooldownSeconds = spell.Trigger == SpellGrammar.TRIGGER_ON_ATTACK && HasDirectBeam(spell)
                ? cycleTicks / (double)TICK_RATE : spell.Cooldown + executionSeconds;
            if (!_player.TryBeginSpell(slot, executionSeconds, cooldownSeconds)) return false;
            RecordNode(spell.CoreNodeId);
            _beamQueued[slot] = false;
            SimVector delta = _aimPoint.HasValue ? _aimPoint.Value - _player.Position : SimVector.Zero;
            SimVector targetDirection = delta.LengthSquared > 0.000001 ? delta.Normalized() : _player.AimDirection;
            var rootCast = new RootSpellExecution(slot, spell, cycleTicks, _tick + cycleTicks,
                _player.Position, targetDirection, _aimPoint ?? _player.Position + targetDirection * GetAreaPlacementRange());
            var context = new SpellContext(_player.Position + _player.AimDirection * (_sector.PlayerRadius + _balance.Sim.WandOffset), _player.AimDirection, null, false, null,
                caster: _player, randomState: new SpellRandomState());
            var tracker = new ExecutionTracker(rootCast, () => CompleteRootSpell(rootCast));
            ExecuteRootBranches(spell.Root, context, tracker);
            return true;
        }

        /// <summary>선택 Trigger와 일치하는 슬롯 마법을 시전하고 성공 여부를 반환한다.</summary>
        private bool TryCastTriggered(CompiledSpell spell, string trigger, int slot)
        {
            if (spell == null || spell.Trigger != trigger) return false;
            return TryCast(spell, slot, trigger == SpellGrammar.TRIGGER_ON_ATTACK);
        }

        /// <summary>공격 코어에 직접 연결된 빔이 있는지 검사해 슬롯 주기 길이를 틱으로 맞출 마법을 구분한다.</summary>
        private static bool HasDirectBeam(CompiledSpell spell)
        {
            foreach (SpellAction action in spell.Root)
                if (action.Form == BeamBehaviorRules.BEAM_KIND) return true;
            return false;
        }

        /// <summary>슬롯별 현재 홀드와 짧은 클릭을 기록한다. 해제·재클릭은 결제한 빔 구간을 취소하지 않는다.</summary>
        private void ReadBeamInput(SimulationInput input)
        {
            for (int slot = 0; slot < _castHeld.Length; slot++)
            {
                bool held = slot == 0 ? input.CastA : slot == 1 ? input.CastB : input.CastC;
                bool released = _castHeld[slot] && !held || slot == 0 && input.CastReleased;
                bool pressed = held && !_castHeld[slot] || slot == 0 && input.CastPressed;
                if (released)
                {
                    _beamQueued[slot] = false;
                }
                if (pressed)
                {
                    if (_loadout[slot] != null && _loadout[slot].Trigger == SpellGrammar.TRIGGER_ON_ATTACK
                        && HasDirectBeam(_loadout[slot]))
                        _beamQueued[slot] = true;
                }
                _castHeld[slot] = held;
            }
        }

        /// <summary>지정 슬롯에 아직 종료되지 않은 직접 시전 빔이 있는지 반환한다.</summary>
        public bool HasActiveBeam(int slot)
        {
            foreach (BeamBehaviorRuntime.BeamChannel channel in _beamChannels)
                if (channel.Slot == slot && channel.FollowsPlayer && !channel.IsStopped) return true;
            return false;
        }

        /// <summary>직접 시전 여부와 슬롯에 관계없이 아직 종료되지 않은 빔 채널이 있는지 반환한다.</summary>
        public bool HasAnyActiveBeam()
        {
            foreach (BeamBehaviorRuntime.BeamChannel channel in _beamChannels)
                if (!channel.IsStopped) return true;
            return false;
        }

        /// <summary>직접 빔 종료 시 추가 쿨다운 없이 채널의 실행 추적 참조를 해제한다.</summary>
        private void CompleteBeamChannel(ExecutionTracker tracker)
        {
            tracker.Release();
        }

        /// <summary>입력 중단 시 기존 빔 홀드와 예약 클릭을 취소하며 다른 행동의 입력 규칙은 유지한다.</summary>
        public void CancelBeamInput()
        {
            for (int slot = 0; slot < _castHeld.Length; slot++)
            {
                _castHeld[slot] = false;
                _beamQueued[slot] = false;
            }
        }

        /// <summary>빔의 모든 비용 자원을 원자적으로 차감하고 기존 누적 소비 통계에 기록한다.</summary>
        internal bool TrySpendBeamCosts(ResourceCostSet costs)
        {
            if (!_player.TrySpend(costs)) return false;
            foreach (ResourceAmount amount in costs.Amounts)
            {
                _resourceCostsSpent.TryGetValue(amount.Resource, out double spent);
                _resourceCostsSpent[amount.Resource] = spent + amount.Amount;
            }
            return true;
        }

        /// <summary>명시한 방향으로 조준을 갱신한 뒤 슬롯 마법을 시전하고 성공 여부를 반환한다.</summary>
        public bool TryCast(CompiledSpell spell, SimVector aimDirection, int slot = 0) { _player.Aim(aimDirection); return TryCast(spell, slot); }

        /// <summary>루트 노드와 예약 분기가 끝나면 실행 잠금만 해제하고 시전 시 시작한 슬롯 쿨다운은 유지한다.</summary>
        private void CompleteRootSpell(RootSpellExecution rootCast)
        {
            _player.FinishSpell(rootCast.Slot);
        }

        /// <summary>입력, 예약된 적 스폰, 전투, 적응 및 전투 종료(전부 처치·사망·보스 제한시간)를 정확히 한 60Hz 틱만큼 갱신한다.</summary>
        public void Step(SimulationInput input)
        {
            if (Completed || IsFailed) return;
            _aimPoint = input.AimPoint;
            ReadBeamInput(input);
            bool wasDashing = _player.DashRemaining > 0.000001;
            _player.Aim(input.AimDirection);
            _player.Advance(STEP_SECONDS, Time, _energyRegen);
            bool startedDash = MovePlayer(input);
            if (startedDash) TryCastTrigger(SpellGrammar.TRIGGER_ON_DASH_START);
            else if (wasDashing && _player.DashRemaining <= 0.000001) TryCastTrigger(SpellGrammar.TRIGGER_ON_DASH_END);
            if (input.Movement.LengthSquared > 0.000001) TryCastTrigger(SpellGrammar.TRIGGER_ON_MOVE);
            if (_isMission) { AdvancePendingSpawns(); AdvanceSpawns(); AdvanceSplits(); RefreshSpawnWarnings(); }
            RunScheduled();
            if (input.CastA || _beamQueued[0]) TryCastTriggered(_loadout[0], SpellGrammar.TRIGGER_ON_ATTACK, 0);
            if (input.CastB || _beamQueued[1]) TryCastTriggered(_loadout[1], SpellGrammar.TRIGGER_ON_ATTACK, 1);
            if (input.CastC || _beamQueued[2]) TryCastTriggered(_loadout[2], SpellGrammar.TRIGGER_ON_ATTACK, 2);
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

        /// <summary>전투 중단 시 예약 노드 실행을 지우고 진행 중 시전 상태를 환급 없이 취소한다.</summary>
        public void CancelPendingSpellExecutions()
        {
            CancelBeamChannels();
            _scheduled.Clear();
            _player.CancelSpellExecutions();
        }

        /// <summary>독립 시험의 시드·상태·통계·시나리오 적을 초기화한다. dummyCount가 0이면 기존 일렬 배치 기본 수를 사용한다.</summary>
        public void ResetBench(string scenario, int dummyCount = 0)
        {
            _beamFlashes.Clear();
            _clearOrbs.Clear();
            _clearItemDrops.Clear();
            if (_isMission) throw new InvalidOperationException("미션은 시험 도크로 초기화할 수 없습니다.");
            if (scenario != "dummy_single" && scenario != "dummy_line" && scenario != "dummy_swarm" && scenario != "aegis" && scenario != "adapt_loop") throw new ArgumentException("알 수 없는 시험 시나리오입니다.", nameof(scenario));
            _scenario = scenario; _stage = MissionStage.Bench; _tick = 0; _nextEntityId = 0; _random.Reset();
            _enemies.Clear(); _orbs.Clear(); _itemDrops.Clear(); _pickedUpItems.Clear(); _newPickedItems.Clear(); _damageNumbers.Clear(); _healPulses.Clear(); _arcLinks.Clear(); _nodeEvents.Clear(); _damageWindow.Clear(); _killCounts.Clear(); CancelCombat();
            _enemyStatuses.Clear(); _statusIndex.Clear(); _statusTime = 0;
            _totalDamage = 0; _statusDamage = 0; _persistDamage = 0; _rollingDamage = 0; _resourceCostsSpent.Clear(); _peakSpellEntities = 0;
            _nodeExecutionCount = 0; _collectedFragments = 0; _scrapRemainder = 0; _killCount = 0; _spawnedEnemies = 0;
            _enemyDamageEventCount = 0;
            _shotLaunchEventCount = 0;
            _shotBurstEventCount = 0;
            _shotPersistEventCount = 0;
            _shotSlashEventCount = 0;
            _burnTickDamageEventCount = 0;
            _arcDamageEventCount = 0;
            _freezeStartEventCount = 0;
            _normalScrapPickupEventCount = 0;
            _bigScrapPickupEventCount = 0;
            _bigScrapSpawnEventCount = 0;
            _actionBudgetTick = -1; _actionsThisTick = 0; _droppedExecutions = 0;
            SimulationBalance bench = _balance.Sim;
            _map = new MissionMap(_sector.BenchTiles, _sector.TileSize); _player = new SimulationPlayer(_maxHp, _maxEnergy, new SimVector(bench.BenchPlayerX, bench.BenchPlayerY));
            Adaptation.Clear(); Adaptation.SetEnabled(scenario == "adapt_loop");
            if (scenario == "dummy_line")
            {
                int count = dummyCount == 0 ? bench.BenchLineCount : dummyCount;
                for (int i = count - 1; i >= 0; i--)
                    SpawnBenchDummy(new SimVector(bench.BenchLineStartX + i * bench.BenchLineGap, bench.BenchDummyY), bench.BenchHp);
            }
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

        /// <summary>현재 적들을 자폭 없이 처치하고 실제 피해 횟수를 기록하며 드롭을 남긴다. 스폰 예약·보스 제한시간은 유지하고 분열 대기는 비운다.</summary>
        public void DebugDefeatRoom()
        {
            if (_isMission && _stage != MissionStage.Combat) return;
            foreach (SimulationEnemy enemy in _enemies)
                if (enemy.Hurt(enemy.Hp, Time) > 0) _enemyDamageEventCount++;
            CleanupEnemies(false);
            _pendingSplits.Clear();
        }

        /// <summary>현재 시뮬레이션의 핵심 상태를 안정적으로 해시하여 동일 입력 실행의 결정성을 비교한다.</summary>
        public string StateHash()
        {
            var state = new StringBuilder();
            state.Append("beamInput:");
            for (int slot = 0; slot < _castHeld.Length; slot++)
                state.Append('|').Append(_castHeld[slot]).Append(':').Append(_beamQueued[slot]);
            state.Append(_tick).Append('|').Append(_random.State).Append('|').Append(_nextEntityId).Append('|').Append(_stage).Append('|').Append(_stageNumber).Append('|').Append(_targetKills).Append('|').Append(_isBossDefeated).Append('|').Append(_spawnedEnemies).Append('|').Append(_killCount).Append('|').Append(_scenario).Append('|').Append(_debugInvulnerable);
            state.Append('|').Append(_actionBudgetTick).Append('|').Append(_actionsThisTick).Append('|').Append(_droppedExecutions);
            SimHash.AppendNumber(state, TimeLimit); SimHash.AppendNumber(state, _energyRegen);
            state.Append('|').Append(_aimPoint.HasValue);
            if (_aimPoint.HasValue) { SimHash.AppendNumber(state, _aimPoint.Value.X); SimHash.AppendNumber(state, _aimPoint.Value.Y); }
            SimHash.AppendNumber(state, _damageMultiplier); SimHash.AppendNumber(state, _moveSpeedMultiplier);
            SimHash.AppendNumber(state, _scrapGainMultiplier); SimHash.AppendNumber(state, _scrapPickupRangeMultiplier); SimHash.AppendNumber(state, _scrapHealRatio); SimHash.AppendNumber(state, _scrapRemainder);
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
                else if (pending.Wave > 0)
                    state.Append("|wave:").Append(pending.Action.NodeId).Append(':').Append(pending.Wave);
                else state.Append("|finish:").Append(pending.Action.NodeId);
                SimHash.AppendNumber(state, pending.Context.Origin.X); SimHash.AppendNumber(state, pending.Context.Origin.Y);
                SimHash.AppendNumber(state, pending.Context.Direction.X); SimHash.AppendNumber(state, pending.Context.Direction.Y);
                SimHash.AppendNumber(state, pending.Context.DirectionOffset);
                state.Append(pending.Context.Target?.Id ?? 0).Append('|').Append(pending.Context.FromEvent).Append('|').Append(pending.Context.NoiseElement);
                SimHash.AppendNumber(state, pending.Context.CostMultiplier);
                if (pending.Context.IsManaPrepaid) state.Append("|manaPrepaid");
                state.Append(pending.Context.SourceEntityId).Append('|');
                if (pending.Tracker != null)
                {
                    state.Append("|flow:").Append(pending.Tracker.RootCast?.Slot ?? -1).Append(':').Append(pending.Tracker.Pending);
                    state.Append(':').Append(pending.Tracker.RootCast?.CycleEndTick ?? -1);
                    pending.Tracker.RootCast?.WriteTargetState(state);
                }
                pending.Context.Modifiers.AppendState(state);
                if (pending.Context.Target != null && !pending.Context.Target.IsAlive) pending.Context.Target.WriteState(state);
            }
            foreach (FragmentOrb orb in _orbs) { SimHash.AppendNumber(state, orb.Position.X); SimHash.AppendNumber(state, orb.Position.Y); state.Append(orb.Amount).Append('|').Append(orb.IsAttracted).Append('|').Append(orb.IsBigScrap); }
            foreach (SimulationItemDrop drop in _itemDrops) SimHash.AppendItemDropState(state, drop);
            foreach (SimulationItemDrop drop in _pickedUpItems) SimHash.AppendItemDropState(state, drop);
            foreach (CompiledSpell spell in _loadout) state.Append('|').Append(spell?.Signature);
            foreach (string element in _unlockedElements) state.Append('|').Append(element);
            foreach (PendingEnemyShot shot in _pendingEnemyShots) { state.Append(shot.Tick).Append('|').Append(shot.Owner.Id); SimHash.AppendNumber(state, shot.Direction.X); SimHash.AppendNumber(state, shot.Direction.Y); SimHash.AppendNumber(state, shot.SpeedMultiplier); }
            foreach (PendingSplit split in _pendingSplits)
            {
                state.Append('|').Append(split.Tick).Append('|').Append(split.EnemyId).Append('|').Append(split.Elite != null);
                SimHash.AppendNumber(state, split.Position.X); SimHash.AppendNumber(state, split.Position.Y);
                SimHash.AppendNumber(state, split.Elite != null ? split.Elite.RawHpMultiplier : 0);
                SimHash.AppendNumber(state, split.Elite != null ? split.Elite.RawRewardMultiplier : 0);
                SimHash.AppendNumber(state, split.Elite != null ? split.Elite.RawSpeedMultiplier : 0);
                if (split.CarrierOwnerId != 0) state.Append("|owner:").Append(split.CarrierOwnerId);
            }
            foreach (PendingSpawn spawn in _pendingSpawns)
            {
                state.Append("|ps").Append(spawn.Tick).Append('|').Append(spawn.EnemyId).Append('|').Append(spawn.Elite != null).Append('|').Append(spawn.IsKillTarget);
                SimHash.AppendNumber(state, spawn.Position.X); SimHash.AppendNumber(state, spawn.Position.Y);
                SimHash.AppendNumber(state, spawn.WarningCenter.X); SimHash.AppendNumber(state, spawn.WarningCenter.Y); SimHash.AppendNumber(state, spawn.WarningRadius);
            }
            SimHash.AppendNumber(state, _totalDamage); state.Append(_collectedFragments).Append(_nodeExecutionCount).Append('|').Append(_enemyDamageEventCount)
                .Append('|').Append(_burnTickDamageEventCount).Append('|').Append(_arcDamageEventCount).Append('|').Append(_freezeStartEventCount);
            state.Append('|').Append(_normalScrapPickupEventCount).Append('|').Append(_bigScrapPickupEventCount);
            if (_director != null)
            { _director.WriteState(state); SimHash.AppendNumber(state, _moveDirection.X); SimHash.AppendNumber(state, _moveDirection.Y); state.Append(_hasMoveDirection); }
            uint hash = 2166136261;
            foreach (char value in state.ToString()) { hash ^= value; hash *= 16777619; }
            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }

        /// <summary>대시와 일반 이동을 처리하고 이번 입력으로 대시가 시작됐는지 반환한다.</summary>
        private bool MovePlayer(SimulationInput input)
        {
            SimVector move = input.Movement.Length > 1 ? input.Movement.Normalized() : input.Movement;
            if (move.LengthSquared > 0.000001) { _moveDirection = move.Normalized(); _hasMoveDirection = true; }
            bool startedDash = input.Dash
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

        /// <summary>현재 틱에 도달한 노드 완료와 연속 회차 예약을 등록 순서대로 적용한다.</summary>
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
                else if (pending.Wave > 0) RunSequenceWave(pending);
                else CompleteAction(pending.Action, pending.Context, pending.Tracker);
            }
        }

        /// <summary>연속 회차 예약을 위치·조준 재계산과 함께 비용 없이 실행하고 완료 추적을 해제한다.</summary>
        private void RunSequenceWave(ScheduledExecution pending)
        {
            pending.Tracker.BeginDispatch();
            try
            {
                SpellContext context = GetEffectContext(pending.Context, pending.Tracker);
                SpawnForm(pending.Action, context, pending.Tracker, pending.Wave);
            }
            finally
            {
                pending.Tracker.Release();
                pending.Tracker.EndDispatch();
            }
        }

        /// <summary>직접 빔은 주기 경계에서 결제·연장하고 다른 루트 분기는 기존 0.05초 간격으로 시작한다.</summary>
        private void ExecuteRootBranches(IReadOnlyList<SpellAction> actions, SpellContext context, ExecutionTracker tracker)
        {
            tracker.BeginDispatch();
            try
            {
                int intervalTicks = GetExecutionDelayTicks(CompiledSpell.ROOT_BRANCH_INTERVAL_SECONDS);
                for (int index = 1; index < actions.Count; index++)
                {
                    if (tracker.RootCast != null && tracker.RootCast.IsSlotCycleBeam(actions[index]))
                        continue;
                    if (_scheduled.Count >= _balance.Limits.MaxScheduledExecutions)
                    {
                        _droppedExecutions++;
                        continue;
                    }
                    tracker.Acquire();
                    _scheduled.Add(new ScheduledExecution(_tick + intervalTicks * index, actions[index], context, tracker, true));
                }
                if (actions.Count > 0)
                {
                    if (tracker.RootCast != null && tracker.RootCast.IsSlotCycleBeam(actions[0]))
                        ExecuteBeamCycle(actions[0], context, tracker, 0);
                    else Execute(new[] { actions[0] }, context, tracker);
                }
                for (int index = 1; index < actions.Count; index++)
                    if (tracker.RootCast != null && tracker.RootCast.IsSlotCycleBeam(actions[index]))
                        ExecuteBeamCycle(actions[index], context, tracker, intervalTicks * index);
            }
            finally
            {
                tracker.EndDispatch();
            }
        }

        /// <summary>직접 빔은 원본 구간 비용에 현재 충전 마나 배율을 적용해 갱신한다. 최초 비용은 원래대로 결제하고 생성 예약한다.</summary>
        private void ExecuteBeamCycle(SpellAction action, SpellContext context, ExecutionTracker tracker, int branchDelayTicks)
        {
            if (_actionBudgetTick != _tick) { _actionBudgetTick = _tick; _actionsThisTick = 0; }
            if (_actionsThisTick >= _balance.Limits.MaxActionsPerTick)
            {
                _droppedExecutions++;
                return;
            }
            _actionsThisTick++;
            RootSpellExecution root = tracker.RootCast;
            BeamBehaviorRuntime.BeamChannel active = null;
            foreach (BeamBehaviorRuntime.BeamChannel channel in _beamChannels)
            {
                if (!channel.IsStopped && channel.UsesSlotCycle && channel.Slot == root.Slot
                    && ReferenceEquals(channel.Action, action) && channel.PaidUntilTick >= _tick)
                {
                    active = channel;
                    break;
                }
            }
            int delayTicks = branchDelayTicks + GetExecutionDelayTicks(GetExecutionSeconds(action, context));
            if (active == null && delayTicks > 0 && _scheduled.Count >= _balance.Limits.MaxScheduledExecutions)
            {
                _droppedExecutions++;
                return;
            }
            ResourceCostSet costs = active != null ? active.CycleCosts
                : root.Spell.GetBeamCycleCosts(action, _balance.Sim.ExecutionTimeScale,
                    context.Modifiers, context.CostMultiplier, context.IsManaPrepaid);
            if (active != null)
                costs = costs.WithAmount("mana", costs.GetAmount("mana") * active.GetManaMultiplier(_tick));
            if (!TrySpendBeamCosts(costs))
            {
                active?.Stop();
                return;
            }
            RecordNode(action.NodeId);
            if (active != null)
            {
                active.Renew(root.CycleEndTick);
                foreach (string nodeId in action.AttachedNodeIds) RecordNode(nodeId);
                return;
            }
            root.SetBeamCycleCosts(action, costs);
            tracker.Acquire();
            if (delayTicks == 0) CompleteAction(action, context, tracker);
            else _scheduled.Add(new ScheduledExecution(_tick + delayTicks, action, context, tracker));
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
                        break;
                    }
                    _actionsThisTick++;
                    SpellAction action = actions[i];
                    double seconds = GetExecutionSeconds(action, context);
                    int durationTicks = GetExecutionDelayTicks(seconds);
                    if (durationTicks > 0 && _scheduled.Count >= _balance.Limits.MaxScheduledExecutions)
                    {
                        _droppedExecutions++;
                        continue;
                    }
                    if (!TrySpendFor(action, context))
                    {
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
            double seconds = action.GetExecutionSeconds(context.Modifiers);
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
            try
            {
                context = GetEffectContext(context, tracker);
                switch (action.Kind)
                {
                    case "spawn": SpawnForm(action, context, tracker); break;
                }
            }
            finally
            {
                tracker.Release();
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
            int index = Math.Min(_unlockedElements.Count - 1, (int)(_random.Next() * _unlockedElements.Count));
            string element = _unlockedElements[index];
            context.RandomState.SetNoiseElement(element);
            return element;
        }

        /// <summary>
        /// 최초 행동의 onHit 전력 선결제와 나머지 노드 자원 비용을 함께 차감하고 성공 여부를 반환한다. 선결제 후속은 전력을 다시 내지 않는다.
        /// 비용이 0이면 항상 성공한다.
        /// </summary>
        private bool TrySpendFor(SpellAction action, SpellContext context)
        {
            ResourceCostSet costs = action.GetExecutionCosts(context.Modifiers, context.CostMultiplier,
                context.IsManaPrepaid);
            if (costs.Amounts.Count == 0) return true;
            if (!_player.TrySpend(costs)) return false;
            foreach (ResourceAmount amount in costs.Amounts)
            {
                _resourceCostsSpent.TryGetValue(amount.Resource, out double spent);
                _resourceCostsSpent[amount.Resource] = spent + amount.Amount;
            }
            return true;
        }

        /// <summary>
        /// 행동 실행 동작에 따라 개체 수만큼 배치·예열 시간을 정해 마법 개체를 생성하고 생성 직후 처리를 실행한다.
        /// 배치는 배치 모드(행동 배치 전) → 행동 PlaceSpawn → 배치 모드(행동 배치 후) 순서다. 이벤트 앵커와 노이즈 속성을 반영한다.
        /// wave가 0이면 첫 회차로 부착 노드를 기록하고 연속의 남은 회차를 예약하며, 0보다 크면 기록·재예약 없이 개체만 다시 만든다.
        /// </summary>
        private void SpawnForm(SpellAction action, SpellContext context, ExecutionTracker tracker, int wave = 0)
        {
            if (wave == 0)
            {
                foreach (string attachedNodeId in action.AttachedNodeIds) RecordNode(attachedNodeId);
                ScheduleSequenceWaves(action, context, tracker);
            }
            string element = action.Noise ? ChooseNoiseElement(context) : action.Element;
            SpellStats stats = action.Stats.Apply(context.Modifiers);
            SpellBehaviorRuntime behavior = SpellBehaviorRuntimeRegistry.Get(action.Form);
            SpellModifierRuntime[] modifiers = SpellModifierRuntimeRegistry.Resolve(action.ModifierRules);
            BeamBehaviorRuntime.BeamChannel channel = null;
            if (action.Form == BeamBehaviorRules.BEAM_KIND && action.Count > 0 && _spellEntities.Count < _balance.Limits.MaxLiveSpellEntities)
            {
                RootSpellExecution root = tracker.RootCast;
                bool isDirect = !context.FromEvent && root != null && root.IsDirectBeam(action);
                bool isSlotCycle = isDirect && root.IsSlotCycleBeam(action);
                if (isDirect && !isSlotCycle) tracker.Acquire();
                channel = new BeamBehaviorRuntime.BeamChannel(_tick, stats, root?.Slot ?? -1,
                    isDirect, action, isSlotCycle, root?.CycleEndTick ?? 0,
                    isSlotCycle ? root.GetBeamCycleCosts(action) : ResourceCostSet.Empty,
                    isDirect && !isSlotCycle ? (Action)(() => CompleteBeamChannel(tracker)) : null);
                _beamChannels.Add(channel);
            }
            for (int i = 0; i < action.Count; i++)
            {
                if (_spellEntities.Count >= _balance.Limits.MaxLiveSpellEntities) return;
                SimVector direction = context.Direction;
                SimVector position = context.Origin;
                if (action.Form == SlashBehaviorRules.KIND && !context.FromEvent) direction = context.Caster.AimDirection;
                foreach (SpellModifierRuntime modifier in modifiers) modifier.BeforePlaceSpawn(this, action, stats, i, ref position, ref direction);
                behavior.PlaceSpawn(this, action, stats, i, context.FromEvent, context.Caster, ref position, ref direction);
                foreach (SpellModifierRuntime modifier in modifiers) modifier.AfterPlaceSpawn(this, action, stats, i, ref position, ref direction);
                RootSpellExecution targetCast = tracker.RootCast;
                if (!context.FromEvent && targetCast != null && targetCast.AimPoint.HasValue
                    && IsAreaPreviewAction(action) && targetCast.IsDirectAction(action))
                {
                    position = GetAreaTargetPosition(action, targetCast.AimPoint.Value, i, targetCast.PlayerPosition);
                    direction = targetCast.AimDirection;
                }
                // 개체 순번 각도다. 공전 모드가 궤도 자리를 등각으로 나누는 데 쓴다.
                double angle = i * 2 * Math.PI / action.Count;
                double warmup = behavior.GetWarmupSeconds(stats);
                var entity = new SimulationSpellEntity(++_nextEntityId, action, stats, element, position, direction,
                    context.FromEvent, context.Origin, angle, context.NoiseElement, _balance.Sim.SpellVisualSeconds, warmup,
                    context.Modifiers, context.CostMultiplier, context.Caster);
                if (entity.BehaviorState is SlashBehaviorRuntime.SlashState slash) slash.SetAnchorTarget(context.Target);
                RecordActionSpawn(action, context);
                _spellEntities.Add(entity); _peakSpellEntities = Math.Max(_peakSpellEntities, _spellEntities.Count);
                if (action.Form == SpellGrammar.FORM_BOLT) _shotLaunchEventCount++;
                else if (action.Form == SpellGrammar.FORM_BURST) _shotBurstEventCount++;
                else if (action.Form == SpellGrammar.FORM_ZONE) _shotPersistEventCount++;
                else if (action.Form == SlashBehaviorRules.KIND) _shotSlashEventCount++;
                if (channel != null) ((BeamBehaviorRuntime.BeamState)entity.BehaviorState).Attach(channel);
                behavior.OnSpawned(this, entity);
            }
        }

        /// <summary>
        /// 연속 모드가 붙은 spawn의 남은 회차를 회차 간격 뒤로 예약한다. 빔에는 적용하지 않으며,
        /// 예약 상한을 넘은 회차는 추적을 걸지 않고 버리고 dropped 실행 수만 센다.
        /// </summary>
        private void ScheduleSequenceWaves(SpellAction action, SpellContext context, ExecutionTracker tracker)
        {
            if (action.WaveCount <= 1 || action.Form == BeamBehaviorRules.BEAM_KIND) return;
            int waveTicks = Math.Max(1, GetExecutionDelayTicks(action.WaveInterval * _balance.Sim.ExecutionTimeScale));
            for (int wave = 1; wave < action.WaveCount; wave++)
            {
                if (_scheduled.Count >= _balance.Limits.MaxScheduledExecutions)
                {
                    _droppedExecutions++;
                    continue;
                }
                tracker.Acquire();
                _scheduled.Add(new ScheduledExecution(_tick + waveTicks * wave, action, context, tracker, wave));
            }
        }

        /// <summary>
        /// 마법 개체마다 이동 전 위치를 기록하고, 우선순위가 높은 이동 모드가 이동을 맡거나 없으면 행동의 기본 경로로 옮긴 뒤
        /// 행동의 판정을 처리한다. 이후 수명을 늘리고 소멸 이벤트를 한 번만 발생시킨다.
        /// </summary>
        private void AdvanceSpellEntities()
        {
            int initialCount = _spellEntities.Count;
            int processed = 0;
            for (int i = 0; processed < initialCount && i < _spellEntities.Count; processed++)
            {
                SimulationSpellEntity entity = _spellEntities[i];
                entity.MarkPreviousPosition();
                if (!entity.MoveByModifiers(this)) entity.Behavior.MoveAlongPath(this, entity);
                entity.Behavior.Judge(this, entity);
                entity.Advance(STEP_SECONDS);
                entity.Behavior.AfterAdvance(this, entity);
                if ((entity.Behavior.RemovesWhenExpired && entity.HasExpired) || entity.Age + 0.000001 >= entity.Lifetime)
                { Expire(entity); _spellEntities.RemoveAt(i); }
                else i++;
            }
            for (int index = _beamChannels.Count - 1; index >= 0; index--)
                if (_beamChannels[index].IsStopped) _beamChannels.RemoveAt(index);
        }

        /// <summary>첫 판정 빔의 경로를 표시 전용으로 복사하고 최근 기록 수를 제한한다.</summary>
        internal void RecordBeamFlash(SimulationSpellEntity entity, IReadOnlyList<SimVector> path)
        {
            if (path.Count < 2) return;
            if (_beamFlashes.Count >= MAX_BEAM_FLASHES) _beamFlashes.RemoveAt(0);
            _beamFlashes.Add(new SimulationBeamFlash(entity, path, _tick));
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
        /// 범위 마법의 가까운 적부터 대상별 주기에 맞춰 피해를 적용하고, 실제 피해 타격 횟수에 따른 행동별 규칙으로 OnHit을 낸다.
        /// isBurst이면 대상별 한 번만 적중하고 확장 범위가 대상과 처음 겹친 접촉 위치로 OnHit을 낸다(Burst).
        /// </summary>
        internal void HitArea(SimulationSpellEntity entity, bool isBurst)
        {
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
            foreach (SimulationEnemy enemy in targets)
            {
                if (!enemy.IsAlive) continue;
                bool hasHit = entity.HitTimes.TryGetValue(enemy.Id, out double lastTime);
                if (!hasHit || (!isBurst && Time - lastTime + 0.000001 >= interval))
                {
                    entity.HitTimes[enemy.Id] = Time;
                    SimVector pushDirection = enemy.Position - entity.Position;
                    if (pushDirection.Length <= COLLISION_EPSILON) pushDirection = entity.Direction;
                    double previousHp = enemy.Hp;
                    Hit(entity, enemy, entity.Stats.Damage, entity.Behavior.IsDamageOverTime, pushDirection);
                    // 폭발은 대상별 한 번, 잔류는 실제 피해 타격 횟수에 따라 접촉 위치에서 후속 행동을 실행한다.
                    if (isBurst || (enemy.Hp < previousHp && entity.Behavior.RecordPeriodicHit(entity, enemy.Id)))
                        RaiseHitEvent(entity, SpellGeometry.GetContactPoint(entity, enemy.Position), enemy);
                }
            }
        }

        /// <summary>스펠 피해·상태와 전격 연쇄를 처리하며 관통 직접 피해는 보정 후에도 최소 1을 유지한다. 직접 실제 피해에 비례한 밀어내기를 pushDirection으로 예약하고 잔류 피해를 누적한다.</summary>
        internal void Hit(SimulationSpellEntity entity, SimulationEnemy enemy, double damage, bool isDot, SimVector pushDirection)
        {
            double minimumDamage = entity.Behavior.TracksDirectHit && entity.Stats.Pierce > 0 && !isDot ? 1d : 0d;
            double actual = ApplyDamage(enemy, damage, entity.Element, entity.Kind, entity.Action.Noise, isDot, entity.Direction, true, minimumDamage, entity.Action);
            if (IsSeparationFixed(enemy)) enemy.CancelDisplacement();
            else if (actual > 0 && enemy.IsAlive && entity.Stats.KnockbackDistance > 0)
            {
                double referenceDamage = GameData.Runes.Get("mod.knockback").Stats.KnockbackReferenceDamage;
                double distance = entity.Stats.KnockbackDistance * actual / referenceDamage;
                enemy.QueueDisplacement(pushDirection, distance, entity.Stats.KnockbackSeconds, _tick);
            }
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
                    actual += ApplyDamage(chain[i], damage * arc.ArcMultiplier, "arc", entity.Kind, entity.Action.Noise, isDot, (chain[i].Position - enemy.Position).Normalized(), true, sourceAction: entity.Action, damageStatus: "arc_chain");
                    _arcLinks.Add(new ArcLink(enemy.Position, chain[i].Position, _tick));
                }
                if (actual > 0) _arcDamageEventCount++;
            }
            if (entity.Behavior.AccumulatesPersistDamage) _persistDamage += actual;
        }

        /// <summary>
        /// 개체의 OnHit 이벤트를 지정 위치·대상(벽이면 null)으로 실행한다. 직접 충돌을 추적하는 행동(발사)은 직접 충돌로 기록하고
        /// 첫 이벤트이면 OnFirstHitOrExpire도 실행한다.
        /// </summary>
        internal void RaiseHitEvent(SimulationSpellEntity entity, SimVector position, SimulationEnemy target)
        {
            GetActionCombatStats(entity.Action).RecordOnHit();
            bool tracksDirectHit = entity.Behavior.TracksDirectHit;
            if (tracksDirectHit) entity.HasDirectHit = true;
            SpellContext context = new SpellContext(position, GetEventDirection(entity, position, target), target, true,
                entity.CastNoiseElement, entity.Modifiers, entity.CostMultiplier, entity.Caster, entity.Id, onHitSource: entity.Action);
            bool isFirstEvent = entity.Behavior.TracksFirstEvent && entity.TryMarkFirstEvent();
            Execute(entity.Action.OnHit, context.WithManaPrepaid(true));
            if (isFirstEvent) Execute(entity.Action.OnFirstHitOrExpire, context);
        }

        /// <summary>
        /// 발사체의 적중 보정과 피해 배율을 보호막 체력에 적용하고 명중·소멸 이벤트를 실행한다.
        /// OnHit과 첫 이벤트는 접촉 위치와 쉴드 소유자 대상으로, OnExpire는 개체 위치와 대상 없이 낸다.
        /// </summary>
        internal void RaiseShieldHitEvent(SimulationSpellEntity entity, SimVector contact, SimulationEnemy shieldOwner)
        {
            double damage = entity.Stats.Damage;
            bool isConsumed = true;
            entity.ApplyHitModifiers(this, shieldOwner, ref damage, ref isConsumed);
            shieldOwner.HurtShield(damage * _damageMultiplier * Adaptation.GetMultiplier(entity.Element, entity.Kind));
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

        /// <summary>피해에 적응·이지스 방패와 minimumDamage 하한을 반영하고 보스 무적은 유지하며 실제 피해와 학습을 기록한다.</summary>
        private double ApplyDamage(SimulationEnemy enemy, double damage, string element, string form, bool noise, bool isDot, SimVector direction, bool applyStatus,
            double minimumDamage = 0d, SpellAction sourceAction = null, string damageStatus = null)
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
            actual = enemy.Hurt(Math.Max(minimumDamage, actual), Time);
            if (applyStatus) ApplyStatus(enemy, element, form, noise, damage, sourceAction);
            if (actual > 0)
            {
                if (sourceAction != null) GetActionCombatStats(sourceAction).RecordDamage(actual, element, damageStatus);
                _enemyDamageEventCount++;
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
                {
                    double actual = ApplyDamage(enemy, burn.TickDamage, "fire", burn.SourceForm, burn.IsNoise, true, SimVector.Zero, false, sourceAction: burn.DamageSourceAction, damageStatus: "burn");
                    _statusDamage += actual;
                    if (actual > 0) _burnTickDamageEventCount++;
                    burn.NextTickTime += _balance.Combat.BurnInterval;
                }
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

        /// <summary>피해 속성에 맞는 상태 이상을 부여한다. 화염은 전투 공통 보정 전 공격 피해 sourceDamage를 틱 피해 계산에 전달한다.</summary>
        private void ApplyStatus(SimulationEnemy enemy, string element, string form, bool noise, double sourceDamage, SpellAction sourceAction)
        {
            if (element == "fire") ApplyBurn(enemy, form, noise, sourceDamage, sourceAction);
            else if (element == "ice") ApplyChill(enemy);
            else if (element == "arc") GetOrAddStatus(enemy.Id, EnemyStatusType.Emp).Until = Time + _balance.Combat.EmpSeconds;
        }

        /// <summary>공격 피해 sourceDamage의 설정 비율을 화상 틱 피해로 계산하고, 새 화상이거나 기존보다 클 때만 피해·출처를 갱신한다. 재점화 때마다 지속시간은 갱신하며 진행 중인 화상의 다음 틱 시각은 유지한다.</summary>
        private void ApplyBurn(SimulationEnemy enemy, string form, bool noise, double sourceDamage, SpellAction sourceAction)
        {
            EnemyStatusEffect burn = FindStatus(enemy.Id, EnemyStatusType.Burn);
            bool isNewBurn = burn == null || Time >= burn.Until;
            if (isNewBurn)
            {
                burn ??= AddStatus(enemy.Id, EnemyStatusType.Burn);
                burn.NextTickTime = Time + _balance.Combat.BurnInterval;
            }
            burn.Until = Time + _balance.Combat.BurnSeconds;
            double tickDamage = sourceDamage * _balance.Combat.BurnDamageRatio;
            if (isNewBurn || tickDamage > burn.TickDamage)
            {
                burn.TickDamage = tickDamage;
                burn.DamageSourceAction = sourceAction;
                burn.SourceForm = form;
                burn.IsNoise = noise;
            }
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
            _freezeStartEventCount++;
            RemoveStatusIndex(chill); _enemyStatuses.Remove(chill);
        }

        /// <summary>적의 상태 이상이 현재 상태 판정 시각에 활성인지 반환한다. 냉기는 스택 보유 여부로 판정한다.</summary>
        public bool HasEnemyStatus(SimulationEnemy enemy, EnemyStatusType type)
        {
            EnemyStatusEffect status = FindStatus(enemy.Id, type);
            if (status == null) return false;
            return type == EnemyStatusType.Chill ? status.Stacks > 0 : _statusTime < status.Until;
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
                bool hasDisplacement = AdvanceEnemyDisplacement(enemy);
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
                if (!hasDisplacement) ApplyMove(enemy, EnemyMovements.Decide(enemy, context));
                if (isBoss) AdvanceBoss(enemy, bossPattern, direction);
                else AdvanceEnemyAttack(enemy, offset, direction);
                if (IsSeparationFixed(enemy)) enemy.CancelDisplacement();
                if (enemy.Definition.HasTrait(EnemyDefinition.TRAIT_CARRIER)) AdvanceCarrier(enemy);
            }
        }

        /// <summary>면역을 확인하고 적의 강제 이동을 진행한다. 벽 앞에서 멈추며 이번 틱에 자발적 이동을 보류할지 반환한다.</summary>
        private bool AdvanceEnemyDisplacement(SimulationEnemy enemy)
        {
            if (IsSeparationFixed(enemy)) { enemy.CancelDisplacement(); return false; }
            if (!enemy.TryAdvanceDisplacement(_tick, STEP_SECONDS, out SimVector displacement)) return false;
            SimVector next = enemy.Position + displacement;
            double wall = FirstWallFraction(enemy.Position, next, enemy.Radius);
            if (!double.IsPositiveInfinity(wall))
            {
                next = enemy.Position + displacement * Math.Max(0, wall - COLLISION_EPSILON);
                enemy.CancelDisplacement();
            }
            enemy.Move(next, enemy.Facing);
            return true;
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
        {
            if (!_isMission || Time < enemy.ContactGraceUntil || SimVector.Distance(enemy.Position, _player.Position) > enemy.Radius + _sector.PlayerRadius) return false;
            HurtPlayer(enemy.Damage); return true;
        }

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
        /// 보스 패턴의 폭발 사격, 조준 사격, 예고 장판, 증원 주기 및 접촉 피해를 페이즈에 맞춰 처리한다.
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
            boss.Patch(next, Time, pattern.PatchSeconds); boss.Observe(Time); Adaptation.LockTopDamageTags();
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

        /// <summary>소멸 이벤트를 개체 위치와 대상 없이 실행한다. OnExpire 다음 첫 이벤트 순서다.</summary>
        private void RaiseExpireEvent(SimulationSpellEntity entity)
        {
            SpellContext context = new SpellContext(entity.Position, entity.Direction, null, true,
                entity.CastNoiseElement, entity.Modifiers, entity.CostMultiplier, entity.Caster, entity.Id);
            bool isFirstEvent = entity.Behavior.TracksFirstEvent && entity.TryMarkFirstEvent();
            Execute(entity.Action.OnExpire, context);
            if (isFirstEvent) Execute(entity.Action.OnFirstHitOrExpire, context);
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
                        bool isBigScrap = enemy.IsElite || enemy.Boss != null;
                        _orbs.Add(new FragmentOrb(enemy.Position, droppedCredits, isBigScrap));
                        if (isBigScrap) _bigScrapSpawnEventCount++;
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
                if (actual > 0)
                {
                    _enemyDamageEventCount++;
                    _damageNumbers.Add(new DamageNumber(target.Position, actual, "raw", _tick));
                }
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

        /// <summary>스크랩을 매 틱 끌어당기고 도착한 오브의 재화·획득 횟수를 더하며 살아 있는 플레이어의 최대 체력 비율만큼 회복한다.</summary>
        private void CollectNearbyOrbs()
        {
            for (int i = _orbs.Count - 1; i >= 0; i--)
            {
                FragmentOrb orb = _orbs[i];
                if (!TryAttractPickup(orb.Position, orb.IsAttracted, out SimVector nextPosition, out bool hasArrived)) continue;
                if (hasArrived)
                {
                    _collectedFragments += orb.Amount;
                    if (_scrapHealRatio > 0 && _player.Hp > 0) _player.Heal(_player.MaxHp * _scrapHealRatio);
                    if (orb.IsBigScrap) _bigScrapPickupEventCount++;
                    else _normalScrapPickupEventCount++;
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

        /// <summary>클리어 시 잔여 드롭을 연출용으로 보존하고 스크랩과 룬을 한 번 회수한 뒤 바닥 드롭 목록을 비운다.</summary>
        private void CollectRemainingDrops()
        {
            _clearOrbs.AddRange(_orbs);
            _clearItemDrops.AddRange(_itemDrops);
            foreach (FragmentOrb orb in _orbs) _collectedFragments += orb.Amount;
            _orbs.Clear();
            _pickedUpItems.AddRange(_itemDrops);
            _newPickedItems.AddRange(_itemDrops);
            _itemDrops.Clear();
        }

        /// <summary>
        /// 처치된 적의 드롭 테이블 ID를 반환하고 없으면 null을 반환한다. 엘리트는 엘리트 정의의 드롭 확률로 판정해 당첨되면 그 테이블을 쓴다.
        /// 엘리트·보스가 아닌 일반 적은 보스 스테이지를 포함한 모든 스테이지에서 그 스테이지의 일반 적 드롭 확률로 1회 판정해 당첨되면 설정된 테이블을 쓴다.
        /// </summary>
        private string GetDropTableId(SimulationEnemy enemy)
        {
            if (enemy.Elite != null)
            {
                EliteSpawnDefinition elite = enemy.Elite;
                if (string.IsNullOrEmpty(elite.DropTable)) return null;
                // 확률이 100%면 난수를 쓰지 않아 기존 판정 순서를 유지한다.
                return elite.DropChancePercent >= 100 || _random.Next() * 100 < elite.DropChancePercent ? elite.DropTable : null;
            }
            NormalEnemyDropDefinition normalDrop = _drops.NormalEnemyDrop;
            if (enemy.Boss != null || normalDrop == null || !normalDrop.IsEnabled(_stageNumber)) return null;
            return _random.Next() * 100 < normalDrop.GetChancePercent(_stageNumber) ? normalDrop.DropTable : null;
        }

        /// <summary>적 처치 스크랩에 획득 배율을 적용하고 소수 보너스를 다음 드롭으로 이월해 정수 스크랩량을 반환한다.</summary>
        private int ScaleKillScrap(int amount)
        {
            double scaled = amount * _scrapGainMultiplier + _scrapRemainder;
            int result = (int)Math.Floor(scaled + 0.000001);
            _scrapRemainder = Math.Max(0, scaled - result);
            return result;
        }

        /// <summary>처치 드롭과 같은 테이블·결정적 난수로 완료 스테이지의 클리어 룬 1개를 추첨한다.</summary>
        public DropRoll DrawClearRuneReward()
        {
            return RuneRewardRoller.DrawRune(_drops.Get(_drops.NormalEnemyDrop.DropTable), _stageNumber, _eliteDrops, _random.Next);
        }

        /// <summary>처치 위치에 놓을 룬의 분류·종류·등급을 추첨해 드롭 개체 1개를 생성한다.</summary>
        private void RollDropTable(DropTableDefinition table, SimVector position)
        {
            DropRoll roll = RuneRewardRoller.DrawRune(table, _stageNumber, _eliteDrops, _random.Next);
            _itemDrops.Add(new SimulationItemDrop(roll.RuneId, roll.Grade, roll.Count, _tick, position));
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
        /// 일반 스테이지는 TimeLimit까지 버티면, 보스 스테이지는 TimeLimit 안에 보스를 처치하면 클리어하고 남은 스크랩·Modifier를 모두 회수한다.
        /// 보스 제한시간 초과는 미획득 스크랩을 버린다. 클리어는 남은 적을 정지된 전장 표시용으로 보존하고 상태 이상을 정리하며 같은 틱의 시간 초과보다 우선한다.
        /// 같은 틱의 사망은 Step에서 먼저 판정되므로 이 확인보다 우선한다.
        /// </summary>
        private void CheckBattleEnd()
        {
            bool hasReachedTimeLimit = Time + 0.000001 >= TimeLimit;
            if (IsBossStage ? _isBossDefeated : hasReachedTimeLimit) _stage = MissionStage.Cleared;
            else if (IsBossStage && hasReachedTimeLimit) _stage = MissionStage.TimedOut;
            else return;
            if (_stage == MissionStage.Cleared) CollectRemainingDrops();
            else DiscardUncollectedOrbs();
            CancelCombat();
            if (_stage != MissionStage.Cleared) return;
            _enemyStatuses.Clear(); _statusIndex.Clear();
        }

        /// <summary>
        /// 스테이지 편성으로 외곽 벽만 있는 맵, 맵 중앙의 플레이어, 스폰 반경, 처치 집계와 스폰 진행을 준비하고 0틱 예약을 바로 스폰한다.
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
            _targetKills = 0; _isBossDefeated = false;
            _director = new SpawnDirector(_stageDefinition, _stageNumber, _formations, TICK_RATE);
            AdvanceSpawns();
            RefreshSpawnWarnings();
        }

        /// <summary>
        /// 현재 틱까지 도달한 스폰을 순서대로 처리한다. 기본 흐름 스폰 순번이 엘리트 주기와 일치하면 그 종족을 엘리트로 생성한다.
        /// 살아 있는 적과 예고 대기 스폰을 합친 수가 상한이면 이번 틱 처리를 멈춘다.
        /// </summary>
        private void AdvanceSpawns()
        {
            while (_director.TryPeek(_tick, out SpawnOrder order))
            {
                if (_enemies.Count + _pendingSpawns.Count >= EnemyLimit) return;
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
        /// 묶음의 첫 스폰에서 슬롯 시작 각도(무작위, 진행 방향 또는 고정 0도)와 기준 슬롯을 정하고, 위치는 스폰 순간의 플레이어 위치 기준으로 계산한다.
        /// 맵형 진형은 원형 슬롯 대신 EnqueueArenaSpawn으로 예고 대기 스폰을 등록한다.
        /// </summary>
        private void SpawnByFormation(SpawnOrder order, string enemyId, EliteSpawnDefinition elite, bool isKillTarget)
        {
            FormationSettings settings = _director.GetSettings(order.Group);
            SpawnFormation formation = SpawnFormations.Get(settings.Shape);
            double radius = _enemyCatalog.Get(enemyId).Radius * (elite != null ? elite.RadiusMultiplier : 1);
            if (formation.IsArena) { EnqueueArenaSpawn(order, enemyId, elite, isKillTarget, formation, settings, radius); return; }
            int slotCount = settings.Slots > 0 ? settings.Slots : _stages.Spawn.RingSlots;
            double spawnRadius = _spawnRadius * settings.RadiusScale;
            if (!_director.IsStarted(order.Group))
            {
                double startAngle = settings.Angle == SpawnFormations.ANGLE_AHEAD ? GetAheadAngle()
                    : settings.Angle == SpawnFormations.ANGLE_FIXED ? 0 : _random.Next() * 2 * Math.PI / slotCount;
                _spawnSlots.Refresh(_map, _player.Position, spawnRadius, slotCount, startAngle, radius);
                _director.Start(order.Group, startAngle, formation.ChooseAnchor(_spawnSlots, settings));
            }
            else _spawnSlots.Refresh(_map, _player.Position, spawnRadius, slotCount, _director.GetStartAngle(order.Group), radius);
            SpawnPlacement placement = formation.Place(order.Index, order.Count, _director.GetAnchor(order.Group), _spawnSlots, settings);
            SpawnEnemy(enemyId, ResolvePlacement(placement, radius), false, 0, elite, isKillTarget);
        }

        /// <summary>
        /// 맵형 진형 예약의 생성 위치를 정해 예고 시간 뒤에 생성할 대기 스폰으로 등록한다. 무리를 공유하는 진형은 묶음의 첫 스폰에서 무리 중심을 정하고
        /// 각 기체를 중심 기준 오프셋에 두며, 그 위치가 맵 밖이거나 플레이어 최소 거리 안이면 무리 중심을 쓴다. 예고 표시는 무리 중심과 무리 반경을 쓴다.
        /// 그 외 맵형 진형은 기체마다 맵 전체에서 위치를 고른다.
        /// </summary>
        private void EnqueueArenaSpawn(SpawnOrder order, string enemyId, EliteSpawnDefinition elite, bool isKillTarget,
            SpawnFormation formation, in FormationSettings settings, double radius)
        {
            SimVector position;
            SimVector warningCenter;
            double warningRadius = radius;
            if (formation.SharesArenaAnchor)
            {
                if (!_director.IsStarted(order.Group)) _director.StartAt(order.Group, PickArenaPoint(radius));
                warningCenter = _director.GetAnchorPoint(order.Group);
                warningRadius += formation.GetArenaSpread(order.Count, settings);
                position = warningCenter + formation.GetArenaOffset(order.Index, settings);
                if (!_map.CanOccupy(position, radius) || (position - _player.Position).Length < _stages.Spawn.MinPlayerDistance) position = warningCenter;
            }
            else
            {
                position = PickArenaPoint(radius);
                warningCenter = position;
            }
            int warningTicks = (int)Math.Round(_stages.Spawn.WarningSeconds * TICK_RATE);
            _pendingSpawns.Add(new PendingSpawn(_tick + warningTicks, enemyId, position, elite, isKillTarget, warningCenter, warningRadius));
        }

        /// <summary>
        /// 맵 전체에서 플레이어 거리 띠와 벽, 다른 예고 위치를 피한 스폰 위치를 고른다. 두 번 추첨해도 후보가 없으면 맵 안에서 플레이어로부터 가장 먼 지점을 반환한다.
        /// </summary>
        private SimVector PickArenaPoint(double radius)
        {
            StageSpawnSettings spawn = _stages.Spawn;
            for (int round = 0; round < ARENA_PICK_ROUNDS; round++)
                if (_arenaSpawnPoints.TryPick(_map, _player.Position, radius, spawn.MinPlayerDistance, spawn.MaxPlayerDistance, _isArenaPointReserved, out SimVector point))
                    return point;
            return GetFarthestPoint(_player.Position, radius);
        }

        /// <summary>맵 좌표가 대기 중인 맵형 스폰의 예고 영역(예고 반경 + 여유) 안에 있는지 반환한다.</summary>
        private bool IsArenaPointReserved(SimVector position)
        {
            foreach (PendingSpawn spawn in _pendingSpawns)
            {
                double gap = spawn.WarningRadius * 2;
                if ((spawn.WarningCenter - position).LengthSquared < gap * gap) return true;
            }
            return false;
        }

        /// <summary>
        /// 생성 틱에 도달한 대기 스폰을 등록 순서대로 생성하고 접촉 피해 유예를 준다. 적 수가 상한이라 생성하지 못하면 남은 항목을 다음 틱에 다시 시도한다.
        /// </summary>
        private void AdvancePendingSpawns()
        {
            for (int i = 0; i < _pendingSpawns.Count;)
            {
                PendingSpawn spawn = _pendingSpawns[i];
                if (spawn.Tick > _tick) { i++; continue; }
                SimulationEnemy enemy = SpawnEnemy(spawn.EnemyId, spawn.Position, false, 0, spawn.Elite, spawn.IsKillTarget);
                if (enemy == null) return;
                enemy.ContactGraceUntil = Time + _stages.Spawn.SpawnGraceSeconds;
                _pendingSpawns.RemoveAt(i);
            }
        }

        /// <summary>
        /// 대기 스폰으로 예고 표시 목록을 다시 만든다. 같은 예고 중심을 쓰는 무리는 하나로 묶고 반경은 가장 큰 값, 진행도는 가장 앞선 값, 엘리트는 하나라도 있으면 표시한다.
        /// </summary>
        private void RefreshSpawnWarnings()
        {
            _spawnWarnings.Clear();
            int warningTicks = (int)Math.Round(_stages.Spawn.WarningSeconds * TICK_RATE);
            foreach (PendingSpawn spawn in _pendingSpawns)
            {
                double progress = warningTicks <= 0 ? 1 : Math.Max(0, Math.Min(1, 1 - (spawn.Tick - _tick) / (double)warningTicks));
                int merged = -1;
                for (int i = 0; i < _spawnWarnings.Count; i++)
                    if (_spawnWarnings[i].Position.X == spawn.WarningCenter.X && _spawnWarnings[i].Position.Y == spawn.WarningCenter.Y) { merged = i; break; }
                if (merged < 0) { _spawnWarnings.Add(new SpawnWarning(spawn.WarningCenter, spawn.WarningRadius, progress, spawn.Elite != null)); continue; }
                SpawnWarning current = _spawnWarnings[merged];
                _spawnWarnings[merged] = new SpawnWarning(current.Position, Math.Max(current.Radius, spawn.WarningRadius),
                    Math.Max(current.Progress, progress), current.IsElite || spawn.Elite != null);
            }
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
        /// 호 이동·바깥 오프셋을 반영한 위치가 맵 밖이면 슬롯 위치를 쓰며, 같은 틱에 같은 위치로 나온 적이 있으면 흔들어 겹치지 않게 한다.
        /// </summary>
        private SimVector ResolvePlacement(SpawnPlacement placement, double radius)
        {
            if (!placement.HasSlot) return GetFarthestPoint(_player.Position, radius);
            SimVector position = _spawnSlots.GetPosition(placement.Slot);
            if (placement.Arc != 0)
            {
                SimVector moved = _spawnSlots.GetArcPosition(placement.Slot, placement.Arc, placement.Outward);
                if (_map.CanOccupy(moved, radius)) position = moved;
            }
            else if (placement.Outward > 0)
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
            SimVector spread = position + new SimVector((_random.Next() * 2 - 1) * range, (_random.Next() * 2 - 1) * range);
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
            double choice = _random.Next() * totalWeight;
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
            _beamFlashGeneration++;
            _beamFlashes.Clear();
            CancelBeamChannels();
            foreach (SimulationEnemy enemy in _enemies) enemy.CancelDisplacement();
            _spellEntities.Clear(); _enemyProjectiles.Clear(); _scheduled.Clear(); _pendingEnemyShots.Clear(); _pendingSplits.Clear(); _deadEnemies.Clear();
            _pendingSpawns.Clear(); _spawnWarnings.Clear();
            if (_player != null) _player.CancelSpellExecutions();
        }

        /// <summary>도크·장착·전투가 바뀔 때 빔 실행 추적과 입력 권한을 이벤트 없이 정리한다.</summary>
        private void CancelBeamChannels()
        {
            foreach (BeamBehaviorRuntime.BeamChannel channel in _beamChannels) channel.Stop();
            _beamChannels.Clear();
            for (int index = _scheduled.Count - 1; index >= 0; index--)
            {
                ScheduledExecution pending = _scheduled[index];
                if (pending.Action?.Form != BeamBehaviorRules.BEAM_KIND) continue;
                _scheduled.RemoveAt(index);
                pending.Tracker.Release();
            }
            for (int index = _spellEntities.Count - 1; index >= 0; index--)
                if (_spellEntities[index].Kind == BeamBehaviorRules.BEAM_KIND) _spellEntities.RemoveAt(index);
            CancelBeamInput();
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

        /// <summary>이미 적중한 적을 제외하고 탐지 반경 내 가장 가까운 생존 적을 반환한다.</summary>
        internal SimulationEnemy NearestEnemy(SimVector position, double range, Dictionary<int, double> excluded)
        { SimulationEnemy result = null; double best = range * range; foreach (SimulationEnemy enemy in _enemies) { if (!enemy.IsAlive || excluded.ContainsKey(enemy.Id)) continue; double distance = (enemy.Position - position).LengthSquared; if (distance < best) { result = enemy; best = distance; } } return result; }

        /// <summary>장착된 행동을 순회하고 노드별 실제 피해와 OnHit 후속 생성 누계를 로그용 복사본으로 반환한다.</summary>
        public IReadOnlyList<Dictionary<string, object>> GetActionCombatLogSnapshot()
        {
            var visited = new HashSet<SpellAction>();
            for (int slot = 0; slot < _loadout.Length; slot++)
                if (_loadout[slot] != null) RegisterActionLogNodes(_loadout[slot].Root, slot, visited);
            var result = new List<Dictionary<string, object>>();
            foreach (KeyValuePair<SpellAction, ActionCombatStats> entry in _actionCombatStats)
            {
                SpellAction action = entry.Key;
                ActionCombatStats stats = entry.Value;
                var targets = new List<Dictionary<string, object>>();
                foreach (KeyValuePair<SpellAction, long> target in stats.OnHitTargets)
                    targets.Add(new Dictionary<string, object>
                    {
                        ["action_key"] = GetActionCombatStats(target.Key).Key,
                        ["node_id"] = target.Key.NodeId,
                        ["spawn_count"] = target.Value,
                    });
                result.Add(new Dictionary<string, object>
                {
                    ["action_key"] = stats.Key,
                    ["node_id"] = action.NodeId,
                    ["loadout_slot"] = _actionLogSlots.TryGetValue(action, out int slot) ? slot : -1,
                    ["action_type"] = action.Form ?? action.Kind,
                    ["element"] = action.Element,
                    ["mods"] = action.Mods,
                    ["attached_node_ids"] = action.AttachedNodeIds,
                    ["spawn_count"] = stats.SpawnCount,
                    ["damage_dealt"] = stats.Damage,
                    ["status_damage"] = stats.StatusDamage,
                    ["direct_damage_by_element"] = new Dictionary<string, double>(stats.DirectDamageByElement),
                    ["status_damage_by_type"] = new Dictionary<string, double>(stats.StatusDamageByType),
                    ["damage_count"] = stats.DamageCount,
                    ["hit_count"] = stats.HitCount,
                    ["on_hit_events"] = stats.OnHitEvents,
                    ["on_hit_spawn_count"] = stats.OnHitSpawnCount,
                    ["triggered_by_on_hit_count"] = stats.TriggeredByOnHitCount,
                    ["on_hit_targets"] = targets,
                });
            }
            return result;
        }

        /// <summary>장착 슬롯의 연결 분기를 순회해 미발동 생성 행동도 0으로 등록하고 노드의 슬롯을 연결한다.</summary>
        private void RegisterActionLogNodes(IReadOnlyList<SpellAction> actions, int slot, HashSet<SpellAction> visited)
        {
            foreach (SpellAction action in actions)
            {
                if (!visited.Add(action)) continue;
                _actionLogSlots[action] = slot;
                if (action.Kind == "spawn") GetActionCombatStats(action);
                foreach (IReadOnlyList<SpellAction> branch in action.Branches) RegisterActionLogNodes(branch, slot, visited);
                if (action.CalledSpell != null) RegisterActionLogNodes(action.CalledSpell.Root, slot, visited);
            }
        }

        /// <summary>컴파일 행동 인스턴스의 전투 누계를 반환하고 처음 관측하면 로그 내 고유 키로 생성한다.</summary>
        private ActionCombatStats GetActionCombatStats(SpellAction action)
        {
            if (!_actionCombatStats.TryGetValue(action, out ActionCombatStats stats))
            {
                stats = new ActionCombatStats("action-" + (_actionCombatStats.Count + 1).ToString(CultureInfo.InvariantCulture));
                _actionCombatStats.Add(action, stats);
            }
            return stats;
        }

        /// <summary>실제로 생성된 개체만 세고 OnHit 문맥이면 원본 행동과 후속 행동의 연결 누계를 함께 갱신한다.</summary>
        private void RecordActionSpawn(SpellAction action, SpellContext context)
        {
            ActionCombatStats stats = GetActionCombatStats(action);
            stats.RecordSpawn(context.OnHitSource != null);
            if (context.OnHitSource != null) GetActionCombatStats(context.OnHitSource).RecordOnHitTarget(action);
        }

    }
}
