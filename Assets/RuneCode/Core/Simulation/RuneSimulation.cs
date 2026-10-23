using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RuneCode
{
    public sealed class RuneSimulation
    {
        public const int TICK_RATE = CompiledSpell.SIMULATION_TICK_RATE;
        public const int ARC_LINK_TICKS = 12;
        internal const double STEP_SECONDS = 1.0 / TICK_RATE;
        internal const double DEGREES_TO_RADIANS = Math.PI / 180;
        private const string BOLT_RUNE_ID = "form.bolt";
        internal const double COLLISION_EPSILON = 0.000001;
        private readonly SimulationWorld _world;
        private readonly SimulationFeedback _feedback;
        private readonly SpellRuntimeContext _runtime;
        private readonly SimulationStats _stats;
        private readonly BeamChannelController _beam;
        private readonly SpellExecutor _executor;
        private readonly EnemyStatusSystem _statuses;
        private readonly LootSystem _loot;
        private readonly SpawnSystem _spawn;
        private readonly EnemySystem _enemy;
        private readonly CombatResolver _combat;
        private readonly SpellEntitySystem _entities;
        private readonly BattleRules _rules;
        private readonly BenchScenario _bench;
        public int Tick => _world.Tick;
        public double Time => _world.Time;
        public SimulationPlayer Player => _world.Player;
        public IReadOnlyList<SimulationEnemy> Enemies => _world.Enemies;
        public IReadOnlyList<SimulationSpellEntity> SpellEntities => _world.SpellEntities;

        /// <summary>장착된 마법 목록이다. 화면에서 시작 노드 직결 범위 미리보기를 조회한다.</summary>
        public IReadOnlyList<CompiledSpell> Loadout => _executor.Loadout;

        /// <summary>지면에 배치하는 폭발·잔류 명령인지 반환한다. 궤도 생성 등 다른 모드의 생성 규칙은 보존한다.</summary>
        public static bool IsAreaPreviewAction(SpellAction action)
            => AreaTargeting.IsAreaPreviewAction(action);

        /// <summary>지면 범위 명령 중 공전 모듈이 없는 경우에만 미리보기를 표시할 수 있는지 반환한다.</summary>
        public static bool CanShowAreaPreview(SpellAction action)
            => AreaTargeting.CanShowAreaPreview(action);

        /// <summary>폭발·잔류가 함께 사용하는 플레이어 중심 최대 배치 반경을 반환한다.</summary>
        public double GetAreaPlacementRange() => _world.Balance.Sim.AreaPlacementRange;

        /// <summary>다중 분산을 제외하고 포인터를 최대 사거리로만 제한한 기본 범위 미리보기 위치를 반환한다.</summary>
        public SimVector GetAreaPreviewPosition(SimVector pointer)
            => AreaTargeting.GetAreaPreviewPosition(pointer, _world.Player, GetAreaPlacementRange());

        /// <summary>범위 조준 미리보기의 위치와 방향을 반환한다. 참격은 시전자 중심과 다중 퍼짐 방향을 사용한다.</summary>
        public void GetRangePreview(SpellAction action, SimVector pointer, int index, out SimVector position, out SimVector direction)
            => AreaTargeting.GetRangePreview(_runtime, action, pointer, index, out position, out direction);

        /// <summary>포인터를 시전자 중심 최대 사거리로만 제한하고 index번째 모드 배치를 적용한 생성 위치를 반환한다. 최소 거리는 없다.</summary>
        public SimVector GetAreaTargetPosition(SpellAction action, SimVector pointer, int index = 0, SimVector? playerPosition = null)
            => AreaTargeting.GetAreaTargetPosition(_runtime, action, pointer, index, playerPosition);
        public IReadOnlyList<SimulationProjectile> EnemyProjectiles => _world.EnemyProjectiles;
        /// <summary>짧은 빔의 최초 경로를 화면에서 읽도록 보관한 최근 표시 기록이다. 피해 판정에는 사용하지 않는다.</summary>
        public IReadOnlyList<SimulationBeamFlash> BeamFlashes => _feedback.BeamFlashes;
        /// <summary>도크 초기화·전투 취소 후 이전 표시 기록을 구분하는 세대 번호다.</summary>
        public int BeamFlashGeneration => _feedback.BeamFlashGeneration;
        public IReadOnlyList<FragmentOrb> Orbs => _loot.Orbs;

        /// <summary>바닥에 떨어져 줍기를 기다리는 Modifier 드롭 목록이다.</summary>
        public IReadOnlyList<SimulationItemDrop> ItemDrops => _loot.ItemDrops;
        /// <summary>지급과 분리한 승리 흡수 연출용 잔여 스크랩 위치를 반환한다.</summary>
        public IReadOnlyList<FragmentOrb> ClearOrbs => _loot.ClearOrbs;
        /// <summary>지급과 분리한 승리 흡수 연출용 잔여 룬을 반환한다.</summary>
        public IReadOnlyList<SimulationItemDrop> ClearItemDrops => _loot.ClearItemDrops;

        /// <summary>이번 전투에서 주워 확정한 Modifier 드롭의 누적 목록이다.</summary>
        public IReadOnlyList<SimulationItemDrop> PickedUpItems => _loot.PickedUpItems;
        public IReadOnlyList<DamageNumber> DamageNumbers => _feedback.DamageNumbers;
        public int EnemyDamageEventCount => _stats.EnemyDamageEventCount;
        public int ShotLaunchEventCount => _stats.ShotLaunchEventCount;
        public int ShotBurstEventCount => _stats.ShotBurstEventCount;
        public int ShotPersistEventCount => _stats.ShotPersistEventCount;
        public int ShotSlashEventCount => _stats.ShotSlashEventCount;
        public int BurnTickDamageEventCount => _stats.BurnTickDamageEventCount;
        public int ArcDamageEventCount => _stats.ArcDamageEventCount;
        public int FreezeStartEventCount => _stats.FreezeStartEventCount;
        public IReadOnlyList<HealPulse> HealPulses => _feedback.HealPulses;

        /// <summary>대기 중인 맵형 스폰의 예고 표시 목록이다. 무리는 하나로 묶여 있으며 표시 전용이다.</summary>
        public IReadOnlyList<SpawnWarning> SpawnWarnings => _feedback.SpawnWarnings;

        /// <summary>최근 ARC_LINK_TICKS 동안 발생한 전격 연쇄 구간 목록이다. 표시 전용이다.</summary>
        public IReadOnlyList<ArcLink> ArcLinks => _feedback.ArcLinks;
        public IReadOnlyList<NodeExecutionEvent> NodeEvents => _feedback.NodeEvents;
        public IReadOnlyDictionary<string, int> KillCounts => _stats.KillCounts;
        public AdaptationNet Adaptation => _world.Adaptation;
        public MissionMap Map => _world.Map;
        public MissionStage Stage => _world.Stage;
        public bool Completed => _world.Completed;
        public bool IsDead => _world.Stage == MissionStage.Dead;
        public bool IsTimedOut => _world.Stage == MissionStage.TimedOut;
        public bool IsFailed => _world.IsFailed;
        public bool IsProgressionMission => _world.IsProgressionMission;
        public int StageNumber => _world.StageNumber;
        public int RoundNumber => _world.RoundNumber;
        public int WaveNumber => _world.WaveNumber;
        public bool IsWaitingForNextRound => _world.Stage == MissionStage.RoundBreak;
        /// <summary>연속 미션 전투 중 현재 웨이브의 살아 있는 적과 미배출·예고·분열 예약 수를 반환하며 대기·종료 상태에서는 0이다.</summary>
        public int RemainingWaveEnemyCount => IsProgressionMission && Stage == MissionStage.Combat ? _spawn.RemainingWaveEnemyCount : 0;
        public bool IsBossStage => _world.IsBossStage;
        public int TargetKills => _rules.TargetKills;
        public SimVector MapCenter => new SimVector(_world.Map.Width * 0.5, _world.Map.Height * 0.5);
        public double EnergyRegen => _world.EnergyRegen;
        public int SpawnedEnemies => _spawn.SpawnedEnemies;
        public int KillCount => _loot.KillCount;
        public int EnemyLimit => _world.EnemyLimit;
        public int DroppedExecutions => _executor.DroppedExecutions;
        public int NormalScrapPickupEventCount => _loot.NormalScrapPickupEventCount;
        public int BigScrapPickupEventCount => _loot.BigScrapPickupEventCount;
        public int BigScrapSpawnEventCount => _loot.BigScrapSpawnEventCount;
        /// <summary>플레이어가 직접 줍거나 스테이지 클리어 시 회수한 스크랩 누계를 반환한다.</summary>
        public int EarnedFragments => _loot.CollectedFragments;
        public int SettlementFragments => _rules.GetSettlementFragments(_loot.CollectedFragments);
        public double TotalDamage => _stats.TotalDamage;

        /// <summary>상태 이상(화염) 틱으로 입힌 실제 피해의 누적값이며 TotalDamage에 포함된다.</summary>
        public double StatusDamage => _stats.StatusDamage;
        /// <summary>잔류(Persist) 판정 틱으로 입힌 실제 피해의 누적값이며 연쇄 전격을 포함하고 TotalDamage에 포함된다.</summary>
        public double PersistDamage => _stats.PersistDamage;
        public double RollingDps => _stats.RollingDamage / Math.Min(5, Math.Max(STEP_SECONDS, Time));
        public double EnergySpent => _stats.EnergySpent;
        public IReadOnlyDictionary<string, double> ResourceCostsSpent => _stats.ResourceCostsSpent;
        public int PeakSpellEntities => _stats.PeakSpellEntities;
        public int NodeExecutionCount => _stats.NodeExecutionCount;
        public bool AdaptationEnabled => Adaptation.Enabled;
        public bool DebugInvulnerable => _world.DebugInvulnerable;
        /// <summary>저격수 또는 Hexa 저격 하위 공격의 저장된 조준 경로를 벽과 원본 사거리로 제한한 끝점을 반환한다.</summary>
        public SimVector GetSniperEnd(SimulationEnemy enemy)
        {
            double range = enemy.Hexa != null && enemy.Hexa.SniperAttackRange > 0
                ? enemy.Hexa.SniperAttackRange : enemy.Definition.AttackRange;
            return enemy.AttackOrigin + enemy.AimedDirection * _entities.ClipBeamLength(enemy.AttackOrigin, enemy.AimedDirection, range);
        }
        /// <summary>시험 도크의 적응 학습 및 피해 감쇠 사용 여부를 변경한다.</summary>
        public void SetAdaptationEnabled(bool isEnabled) { Adaptation.SetEnabled(isEnabled); }

        /// <summary>명시적으로 열린 디버그 기능에서 플레이어 무적 상태를 변경한다.</summary>
        public void SetDebugInvulnerable(bool isInvulnerable) { _world.DebugInvulnerable = isInvulnerable; }

        /// <summary>생성 설정의 시드, 스테이지와 트리 능력치·처치 전력 보너스·스크랩 회복 비율, 카탈로그로 독립 전투 또는 시험 도크를 생성한다.</summary>
        public RuneSimulation(SimulationSetup setup)
        {
            _world = new SimulationWorld(setup.Balance, setup.Enemies, setup.Sector, setup.Bosses, setup.Formations, setup.Drops,
                new ModifierGradeDropSource(setup.ModifierGrades), setup.Stages, setup.IsMission, setup.MaxHp, setup.MaxEnergy, setup.StageNumber, setup.EnergyRegen,
                setup.DamageMultiplier, setup.MoveSpeedMultiplier, new SimRandom(setup.Seed), new AdaptationNet(setup.Balance), setup.IsProgressionMission);
            _feedback = new SimulationFeedback(_world);
            _runtime = new SpellRuntimeContext(_world, _feedback);
            _stats = new SimulationStats();
            _beam = new BeamChannelController(_world, _stats);
            _executor = new SpellExecutor(_world, _feedback, _stats, _beam, _runtime);
            _statuses = new EnemyStatusSystem(_world, _stats, this);
            _rules = new BattleRules(_world);
            _spawn = new SpawnSystem(_world, _feedback, _stats);
            _bench = new BenchScenario(_world, _spawn);
            _enemy = new EnemySystem(_world, _feedback, _stats, _spawn, _statuses, _executor, this);
            _loot = new LootSystem(_world, _stats, _statuses, _rules, _spawn, _enemy,
                setup.ScrapGainMultiplier, setup.ScrapPickupRangeMultiplier, setup.ScrapHealRatio, setup.KillEnergyBonus);
            _combat = new CombatResolver(_world, _feedback, _stats, _statuses, _enemy, _executor, setup.Runes, _runtime);
            _entities = new SpellEntitySystem(_world, _combat, _runtime);
            _runtime.Connect(_combat, _entities);
            if (setup.IsMission) BeginTimedBattle(setup.Runes, setup.ProgressionStage, setup.ProgressionRound, setup.ProgressionHp);
            else ResetBench("dummy_single");
        }

        /// <summary>전투에서는 하나의 마법, 시험 도크에서는 최대 3슬롯의 컴파일 마법을 다음 시전부터 사용한다.</summary>
        public void SetLoadout(IReadOnlyList<CompiledSpell> spells) => _executor.SetLoadout(spells);

        /// <summary>해금한 속성 태그를 노이즈의 무작위 후보로 설정하고 기본 화염 후보를 보존한다.</summary>
        public void SetUnlockedElements(IReadOnlyList<string> elements) => _executor.SetUnlockedElements(elements);

        /// <summary>
        /// 라운드 대기 중에만 강화 능력치(최대 체력·전력, 재생, 피해·이동 속도, 스크랩 획득·줍기·회복, 처치 전력)를 월드·플레이어·전리품에 반영한다.
        /// 최대 체력·전력이 늘면 현재 값도 증가분만큼 오른다. 적용하면 true를 반환한다.
        /// </summary>
        public bool TryApplyUpgradeStats(double maxHp, double maxEnergy, double energyRegen, double damageMultiplier, double moveSpeedMultiplier,
            double scrapGainMultiplier, double scrapPickupRangeMultiplier, double scrapHealRatio, double killEnergyBonus)
        {
            if (!IsWaitingForNextRound) return false;
            _world.SetPlayerStats(maxHp, maxEnergy, energyRegen, damageMultiplier, moveSpeedMultiplier);
            _world.Player.SetMaxStats(_world.MaxHp, _world.MaxEnergy);
            _loot.SetUpgradeStats(scrapGainMultiplier, scrapPickupRangeMultiplier, scrapHealRatio, killEnergyBonus);
            return true;
        }

        /// <summary>
        /// 슬롯 쿨다운을 시작하고 루트 실행을 등록한다. 직접 공격 빔은 구간 비용을 선결제하며 연장은 현재 슬롯 입력으로 판단한다.
        /// </summary>
        public bool TryCast(CompiledSpell spell, int slot = 0, bool allowBeamHold = false) => _executor.TryCast(spell, slot, allowBeamHold);

        /// <summary>전투 중단 시 예약 노드 실행을 지우고 진행 중 시전 상태를 환급 없이 취소한다.</summary>
        public void CancelPendingSpellExecutions() => _executor.CancelPendingSpellExecutions();

        /// <summary>아직 지급하지 않은 새로 주운 Modifier 드롭 목록을 반환하고 미지급 목록을 비운다.</summary>
        public List<SimulationItemDrop> TakeNewPickedItems() => _loot.TakeNewPickedItems();

        /// <summary>현재 라운드의 보상 드롭 테이블과 결정적 난수로 서로 다른 룬 후보와 후보별 등급을 뽑아 반환한다. 후보가 없으면 false를 반환한다.</summary>
        public bool TryDrawClearModifierChoices(out List<string> candidates, out List<string> grades)
            => _loot.TryDrawClearModifierChoices(out candidates, out grades);

        /// <summary>지정 슬롯에 아직 종료되지 않은 직접 시전 빔이 있는지 반환한다.</summary>
        public bool HasActiveBeam(int slot) => _beam.HasActiveBeam(slot);

        /// <summary>직접 시전 여부와 슬롯에 관계없이 아직 종료되지 않은 빔 채널이 있는지 반환한다.</summary>
        public bool HasAnyActiveBeam() => _beam.HasAnyActiveBeam();

        /// <summary>입력 중단 시 기존 빔 홀드와 예약 클릭을 취소하며 다른 행동의 입력 규칙은 유지한다.</summary>
        public void CancelBeamInput() => _beam.CancelBeamInput();

        /// <summary>소강상태에서 다음 라운드 또는 스테이지로 전환하며 이전 피해 표시를 지우고 전력을 최대로 회복한 뒤 첫 웨이브를 시작한다.</summary>
        public bool TryStartNextRound()
        {
            if (!_world.IsProgressionMission || _world.Stage != MissionStage.RoundBreak) return false;
            _feedback.ClearDamageNumbers();
            _world.Player.RestoreEnergy(_world.Player.MaxEnergy);
            _loot.ClearClearFeedback();
            if (_world.RoundNumber == StageCatalog.PROGRESSION_ROUND_COUNT)
            {
                _world.StageNumber++;
                _world.RoundNumber = 1;
            }
            else _world.RoundNumber++;
            _world.WaveNumber = 1;
            _world.Stage = MissionStage.Combat;
            StartProgressionWave();
            return true;
        }

        /// <summary>
        /// 디버그 라운드 대기 중 지정 스테이지의 미니 보스 또는 최종 보스 라운드 마지막 웨이브로 전환한다. 기본값은 최종 보스다.
        /// isHexaPreview이면 최종 웨이브만 Hexa로 대체하고, 단독 공격 미리보기 또는 isHexaCompositePreview와 지정한 순서(없으면 SimRandom)의 복합 공격을 준비한다.
        /// isHexaSpiralPreview는 4방향 시계 회전 나선 공격을, isHexaAutoPreview는 우선순위·횟수 쿨타임 자동 운영을 준비한다. 유효하지 않은 요청에는 false를 반환한다.
        /// </summary>
        public bool DebugStartBossWave(int stage, int round = StageCatalog.PROGRESSION_ROUND_COUNT, bool isHexaPreview = false,
            bool isHexaSprayPreview = false, bool isHexaHomingPreview = false, bool isHexaSniperPreview = false,
            bool isHexaCompositePreview = false, HexaSubAttack compositeFirst = HexaSubAttack.None,
            HexaSubAttack compositeSecond = HexaSubAttack.None, bool isHexaSpiralPreview = false,
            bool isHexaAutoPreview = false)
        {
            if (!_world.IsProgressionMission || _world.Stage != MissionStage.RoundBreak
                || stage < 1 || stage > StageCatalog.PROGRESSION_STAGE_COUNT
                || (round != StageCatalog.MINI_BOSS_ROUND && round != StageCatalog.PROGRESSION_ROUND_COUNT)
                || (isHexaPreview && round != StageCatalog.PROGRESSION_ROUND_COUNT)
                || (isHexaSprayPreview && !isHexaPreview)
                || (isHexaHomingPreview && !isHexaPreview)
                || (isHexaSniperPreview && !isHexaPreview)
                || (isHexaCompositePreview && !isHexaPreview)
                || (isHexaSpiralPreview && !isHexaPreview)
                || (isHexaAutoPreview && !isHexaPreview)
                || (isHexaCompositePreview && (compositeFirst == HexaSubAttack.None) != (compositeSecond == HexaSubAttack.None))
                || (isHexaCompositePreview && compositeFirst != HexaSubAttack.None && compositeFirst == compositeSecond)
                || ((isHexaSprayPreview ? 1 : 0) + (isHexaHomingPreview ? 1 : 0) + (isHexaSniperPreview ? 1 : 0)
                    + (isHexaCompositePreview ? 1 : 0) + (isHexaSpiralPreview ? 1 : 0)
                    + (isHexaAutoPreview ? 1 : 0) > 1)) return false;
            _loot.ClearClearFeedback();
            _world.StageNumber = stage;
            _world.RoundNumber = round;
            _world.WaveNumber = StageCatalog.PROGRESSION_WAVE_COUNT;
            _world.Player.SetHp(_world.Player.MaxHp);
            _world.Stage = MissionStage.Combat;
            StartProgressionWave(isHexaPreview);
            if (isHexaSprayPreview || isHexaHomingPreview || isHexaSniperPreview || isHexaCompositePreview
                || isHexaSpiralPreview || isHexaAutoPreview)
            {
                EnemyDefinition projectile = _world.EnemyCatalog.Get(HexaDefinition.PROJECTILE_SOURCE_ID);
                EnemyDefinition sniper = isHexaSniperPreview || isHexaCompositePreview || isHexaAutoPreview
                    ? _world.EnemyCatalog.Get(HexaDefinition.SNIPER_SOURCE_ID) : null;
                foreach (SimulationEnemy enemy in _world.Enemies)
                {
                    if (enemy.Hexa == null) continue;
                    if (isHexaSprayPreview) enemy.Hexa.BeginSpray(projectile);
                    else if (isHexaHomingPreview) enemy.Hexa.BeginHoming(projectile);
                    else if (isHexaSniperPreview) enemy.Hexa.BeginSniper(sniper);
                    else if (isHexaSpiralPreview) enemy.Hexa.BeginSpiral(projectile);
                    else if (isHexaAutoPreview) enemy.Hexa.BeginAutoOperation(projectile, sniper);
                    else enemy.Hexa.BeginComposite(projectile, sniper, _world.Random, compositeFirst, compositeSecond);
                }
            }
            return true;
        }

        /// <summary>
        /// 디버그 라운드 대기 중 지정 스테이지·라운드의 첫 일반 웨이브로 전환한다. Hexa 두 기와 소환 소유권 시험에 쓸
        /// 보스가 없는 전투 구간을 만들며 유효하지 않은 요청에는 false를 반환한다.
        /// </summary>
        public bool DebugStartNormalWave(int stage = 1, int round = 1)
        {
            if (!_world.IsProgressionMission || _world.Stage != MissionStage.RoundBreak
                || stage < 1 || stage > StageCatalog.PROGRESSION_STAGE_COUNT
                || round < 1 || round > StageCatalog.PROGRESSION_ROUND_COUNT) return false;
            _loot.ClearClearFeedback();
            _world.StageNumber = stage;
            _world.RoundNumber = round;
            _world.WaveNumber = 1;
            _world.Player.SetHp(_world.Player.MaxHp);
            _world.Stage = MissionStage.Combat;
            StartProgressionWave();
            return true;
        }

        /// <summary>디버그 소환 시험에서 지정한 개체 ID의 살아 있는 Hexa만 소환 모드를 시작하고, 시작했거나 이미 진행 중이면 true를 반환한다.</summary>
        public bool DebugStartHexaSummon(int entityId)
        {
            if (_world.IsMission && _world.Stage != MissionStage.Combat) return false;
            foreach (SimulationEnemy enemy in _world.Enemies)
            {
                if (enemy.Id != entityId) continue;
                if (!enemy.IsAlive || enemy.Hexa == null || enemy.Hexa.IsCancelled) return false;
                enemy.Hexa.BeginSummon();
                return enemy.Hexa.IsSummoning;
            }
            return false;
        }

        /// <summary>디버그 소환 시험에서 지정한 개체 ID의 살아 있는 Hexa 한 기에만 치명 피해를 적용하고, 정상 사망 정리 경로로 넘긴다. 선택 ID가 없으면 false를 반환한다.</summary>
        public bool DebugKillHexa(int entityId)
        {
            if (_world.IsMission && _world.Stage != MissionStage.Combat) return false;
            foreach (SimulationEnemy enemy in _world.Enemies)
            {
                if (enemy.Id != entityId) continue;
                if (!enemy.IsAlive || enemy.Hexa == null) return false;
                if (enemy.Hurt(enemy.Hp, Time) > 0) _stats.EnemyDamageEventCount++;
                return true;
            }
            return false;
        }

        /// <summary>명시한 방향으로 조준을 갱신한 뒤 슬롯 마법을 시전하고 성공 여부를 반환한다.</summary>
        public bool TryCast(CompiledSpell spell, SimVector aimDirection, int slot = 0) { _world.Player.Aim(aimDirection); return TryCast(spell, slot); }

        /// <summary>입력, 예약된 적 스폰, 전투, 적응 및 전투 종료(전부 처치·사망·보스 제한시간)를 정확히 한 60Hz 틱만큼 갱신한다.</summary>
        public void Step(SimulationInput input)
        {
            if (Completed || IsFailed || IsWaitingForNextRound) return;
            _executor.AimPoint = input.AimPoint;
            _beam.ReadBeamInput(input, _executor.Loadout);
            bool wasDashing = _world.Player.DashRemaining > 0.000001;
            _world.Player.Aim(input.AimDirection);
            _world.Player.Advance(STEP_SECONDS, Time, _world.EnergyRegen);
            bool startedDash = MovePlayer(input);
            if (startedDash) _executor.TryCastTrigger(SpellGrammar.TRIGGER_ON_DASH_START);
            else if (wasDashing && _world.Player.DashRemaining <= 0.000001) _executor.TryCastTrigger(SpellGrammar.TRIGGER_ON_DASH_END);
            if (input.Movement.LengthSquared > 0.000001) _executor.TryCastTrigger(SpellGrammar.TRIGGER_ON_MOVE);
            if (_world.IsMission) { _spawn.AdvancePendingSpawns(); _spawn.AdvanceSpawns(); _spawn.AdvanceSplits(); _spawn.RefreshSpawnWarnings(); }
            _executor.RunScheduled();
            _executor.TryCastAttackSlots(input);
            _statuses.AdvanceStatuses();
            _enemy.AdvanceRelayRegen();
            _enemy.AdvanceEnemies();
            if (_world.IsMission) _enemy.ApplySeparation();
            _enemy.RunEnemyShots();
            _entities.AdvanceSpellEntities();
            _beam.RemoveStoppedChannels();
            _enemy.AdvanceHostileProjectiles();
            _loot.CleanupEnemies();
            _loot.CollectNearbyOrbs();
            _loot.CollectNearbyItemDrops();
            if (_world.IsMission && _world.Player.Hp <= 0) { _world.Stage = MissionStage.Dead; _loot.DiscardUncollectedOrbs(); CancelCombat(); }
            else if (!_world.IsMission) _bench.RespawnAdaptDummy();
            Adaptation.Advance(Time, Time + STEP_SECONDS);
            _world.Tick++;
            _executor.RunScheduled();
            if (_world.IsMission && _world.Stage == MissionStage.Combat) CheckProgressionWaveEnd();
            TrimFeedback();
        }

        /// <summary>독립 시험의 시드·상태·통계·시나리오 적을 초기화한다. dummyCount가 0이면 기존 일렬 배치 기본 수를 사용한다.</summary>
        public void ResetBench(string scenario, int dummyCount = 0)
        {
            _feedback.ClearBeamFlashes();
            _loot.ClearClearFeedback();
            if (_world.IsMission) throw new InvalidOperationException("미션은 시험 도크로 초기화할 수 없습니다.");
            _bench.Begin(scenario);
            _world.ResetForBench();
            _world.Enemies.Clear(); _loot.ClearForBench(); _feedback.ClearForBench(); CancelCombat();
            _statuses.ClearAll();
            _stats.ResetForBench(); _loot.ResetForBench(); _spawn.ResetForBench();
            _executor.ResetForBench();
            SimulationBalance bench = _world.Balance.Sim;
            _world.Map = new MissionMap(_world.Sector.BenchTiles, _world.Sector.TileSize); _world.Player = new SimulationPlayer(_world.MaxHp, _world.MaxEnergy, new SimVector(bench.BenchPlayerX, bench.BenchPlayerY));
            Adaptation.Clear(); Adaptation.SetEnabled(scenario == "adapt_loop");
            _bench.PlaceEnemies(dummyCount);
        }

        /// <summary>디버그 기능에서 지정한 적을 추가한다. 엘리트는 체력·보상 2배, 속도 1.2배, 반경 2배와 현재 스테이지의 드롭 테이블을 사용한다.</summary>
        public SimulationEnemy DebugSpawn(string id, bool isElite = false)
        {
            EliteSpawnDefinition elite = isElite ? new EliteSpawnDefinition(id, 2, 2, 1.2, 2, GetDebugEliteDropTable(id)) : null;
            return SpawnEnemy(id, _world.Map.NearestFree(_world.Player.Position + _world.Player.AimDirection * 260, 24), false, 0, elite);
        }

        /// <summary>디버그 엘리트에 현재 연속 스테이지에서 해당 종족이 쓰는 드롭 테이블을 연결하고, 없으면 첫 설정 테이블을 반환한다.</summary>
        private string GetDebugEliteDropTable(string enemyId)
        {
            IReadOnlyList<EliteSpawnDefinition> elites = _world.Stages.GetProgressionStage(_world.StageNumber).EliteTypes;
            foreach (EliteSpawnDefinition elite in elites)
                if (elite.EnemyId == enemyId && !string.IsNullOrEmpty(elite.DropTable)) return elite.DropTable;
            foreach (EliteSpawnDefinition elite in elites)
                if (!string.IsNullOrEmpty(elite.DropTable)) return elite.DropTable;
            return null;
        }

        /// <summary>현재 적들을 자폭 없이 처치하고 실제 피해 횟수를 기록하며 드롭을 남긴다. 스폰 예약·보스 제한시간은 유지하고 분열 대기는 비운다.</summary>
        public void DebugDefeatRoom()
        {
            if (_world.IsMission && _world.Stage != MissionStage.Combat) return;
            foreach (SimulationEnemy enemy in _world.Enemies)
                if (enemy.Hurt(enemy.Hp, Time) > 0) _stats.EnemyDamageEventCount++;
            _loot.CleanupEnemies(false);
            _spawn.PendingSplits.Clear();
        }

        /// <summary>현재 시뮬레이션의 핵심 상태를 안정적으로 해시하여 동일 입력 실행의 결정성을 비교한다.</summary>
        public string StateHash()
        {
            var state = new StringBuilder();
            _beam.WriteInputState(state);
            state.Append(_world.Tick).Append('|').Append(_world.Random.State).Append('|').Append(_world.NextEntityId).Append('|').Append(_world.Stage).Append('|').Append(_world.StageNumber).Append('|').Append(_rules.TargetKills).Append('|').Append(_rules.IsBossDefeated).Append('|').Append(_spawn.SpawnedEnemies).Append('|').Append(_loot.KillCount).Append('|').Append(_bench.Scenario).Append('|').Append(_world.DebugInvulnerable);
            if (_world.IsProgressionMission) state.Append("|progression:").Append(_world.RoundNumber).Append(':').Append(_world.WaveNumber);
            state.Append('|').Append(_executor.ActionBudgetTick).Append('|').Append(_executor.ActionsThisTick).Append('|').Append(_executor.DroppedExecutions);
            SimHash.AppendNumber(state, _world.EnergyRegen);
            state.Append('|').Append(_executor.AimPoint.HasValue);
            if (_executor.AimPoint.HasValue) { SimHash.AppendNumber(state, _executor.AimPoint.Value.X); SimHash.AppendNumber(state, _executor.AimPoint.Value.Y); }
            SimHash.AppendNumber(state, _world.DamageMultiplier); SimHash.AppendNumber(state, _world.MoveSpeedMultiplier);
            SimHash.AppendNumber(state, _loot.ScrapGainMultiplier); SimHash.AppendNumber(state, _loot.ScrapPickupRangeMultiplier); SimHash.AppendNumber(state, _loot.ScrapHealRatio); SimHash.AppendNumber(state, _loot.ScrapRemainder);
            _world.Player.WriteState(state);
            foreach (SimulationEnemy enemy in _world.Enemies) enemy.WriteState(state);
            foreach (EnemyStatusEffect status in _statuses.Statuses) status.WriteState(state);
            foreach (SimulationSpellEntity entity in _world.SpellEntities) entity.WriteState(state);
            foreach (SimulationProjectile projectile in _world.EnemyProjectiles) projectile.WriteState(state);
            Adaptation.WriteState(state);
            _executor.WriteScheduledState(state);
            foreach (FragmentOrb orb in _loot.Orbs) { SimHash.AppendNumber(state, orb.Position.X); SimHash.AppendNumber(state, orb.Position.Y); state.Append(orb.Amount).Append('|').Append(orb.IsAttracted).Append('|').Append(orb.IsBigScrap); }
            foreach (SimulationItemDrop drop in _loot.ItemDrops) SimHash.AppendItemDropState(state, drop);
            foreach (SimulationItemDrop drop in _loot.PickedUpItems) SimHash.AppendItemDropState(state, drop);
            _executor.WriteLoadoutState(state);
            foreach (PendingEnemyShot shot in _enemy.PendingEnemyShots) { state.Append(shot.Tick).Append('|').Append(shot.Owner.Id); SimHash.AppendNumber(state, shot.Direction.X); SimHash.AppendNumber(state, shot.Direction.Y); SimHash.AppendNumber(state, shot.SpeedMultiplier); }
            foreach (PendingSplit split in _spawn.PendingSplits)
            {
                state.Append('|').Append(split.Tick).Append('|').Append(split.EnemyId).Append('|').Append(split.Elite != null);
                SimHash.AppendNumber(state, split.Position.X); SimHash.AppendNumber(state, split.Position.Y);
                SimHash.AppendNumber(state, split.Elite != null ? split.Elite.RawHpMultiplier : 0);
                SimHash.AppendNumber(state, split.Elite != null ? split.Elite.RawRewardMultiplier : 0);
                SimHash.AppendNumber(state, split.Elite != null ? split.Elite.RawSpeedMultiplier : 0);
                if (split.CarrierOwnerId != 0) state.Append("|owner:").Append(split.CarrierOwnerId);
            }
            foreach (PendingSpawn spawn in _spawn.PendingSpawns)
            {
                state.Append("|ps").Append(spawn.Tick).Append('|').Append(spawn.EnemyId).Append('|').Append(spawn.Elite != null).Append('|').Append(spawn.IsKillTarget);
                SimHash.AppendNumber(state, spawn.Position.X); SimHash.AppendNumber(state, spawn.Position.Y);
                SimHash.AppendNumber(state, spawn.WarningCenter.X); SimHash.AppendNumber(state, spawn.WarningCenter.Y); SimHash.AppendNumber(state, spawn.WarningRadius);
            }
            SimHash.AppendNumber(state, _stats.TotalDamage); state.Append(_loot.CollectedFragments).Append(_stats.NodeExecutionCount).Append('|').Append(_stats.EnemyDamageEventCount)
                .Append('|').Append(_stats.BurnTickDamageEventCount).Append('|').Append(_stats.ArcDamageEventCount).Append('|').Append(_stats.FreezeStartEventCount);
            state.Append('|').Append(_loot.NormalScrapPickupEventCount).Append('|').Append(_loot.BigScrapPickupEventCount);
            if (_spawn.Director != null)
            { _spawn.Director.WriteState(state); SimHash.AppendNumber(state, _world.MoveDirection.X); SimHash.AppendNumber(state, _world.MoveDirection.Y); state.Append(_world.HasMoveDirection); }
            uint hash = 2166136261;
            foreach (char value in state.ToString()) { hash ^= value; hash *= 16777619; }
            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }

        /// <summary>대시와 일반 이동을 처리하고 이번 입력으로 대시가 시작됐는지 반환한다.</summary>
        private bool MovePlayer(SimulationInput input)
        {
            SimVector move = input.Movement.Length > 1 ? input.Movement.Normalized() : input.Movement;
            if (move.LengthSquared > 0.000001) { _world.MoveDirection = move.Normalized(); _world.HasMoveDirection = true; }
            bool startedDash = input.Dash
                && _world.Player.StartDash(move.LengthSquared > 0.000001 ? move : _world.Player.AimDirection, _world.Balance.Player.DashSeconds, _world.Balance.Player.DashCooldown, Time);
            SimVector displacement = _world.Player.DashRemaining > 0 ? _world.Player.GetDashDirection() * (_world.Balance.Player.DashDistance / _world.Balance.Player.DashSeconds * Math.Min(STEP_SECONDS, _world.Player.DashRemaining)) : move * (_world.Balance.Player.Speed * _world.MoveSpeedMultiplier * STEP_SECONDS);
            _world.Player.Move(_world.Map.Move(_world.Player.Position, displacement, _world.Sector.PlayerRadius));
            return startedDash;
        }

        /// <summary>원형 개체의 직선 이동에서 첫 벽 접촉 비율을 반환한다. 마법 개체 시스템에 위임한다.</summary>
        internal double FirstWallFraction(SimVector start, SimVector end, double radius) => _entities.FirstWallFraction(start, end, radius);

        /// <summary>적응·방패·하한을 반영한 실제 피해를 적용하고 반환한다. 상태 이상 화상 틱이 호출하며 전투 판정기에 위임한다.</summary>
        internal double ApplyDamage(SimulationEnemy enemy, double damage, string element, string form, bool noise, bool isDot, SimVector direction, bool applyStatus,
            double minimumDamage = 0d, SpellAction sourceAction = null, string damageStatus = null)
            => _combat.ApplyDamage(enemy, damage, element, form, noise, isDot, direction, applyStatus, minimumDamage, sourceAction, damageStatus);

        /// <summary>적의 상태 이상이 현재 상태 판정 시각에 활성인지 반환한다. 냉기는 스택 보유 여부로 판정한다.</summary>
        public bool HasEnemyStatus(SimulationEnemy enemy, EnemyStatusType type) => _statuses.HasEnemyStatus(enemy, type);

        /// <summary>적의 현재 냉기 스택을 반환하며 냉기가 없으면 0을 반환한다.</summary>
        public int GetChillStacks(SimulationEnemy enemy) => _statuses.GetChillStacks(enemy);

        /// <summary>상태 이상 피해를 플레이어에게 적용한다. 적 시스템에 위임한다.</summary>
        internal void HurtPlayerByStatus(double damage) => _enemy.HurtPlayerByStatus(damage);

        /// <summary>처치 드롭과 같은 테이블·결정적 난수로 완료 스테이지의 클리어 룬 1개를 추첨한다.</summary>
        public DropRoll DrawClearRuneReward() => _loot.DrawClearRuneReward();

        /// <summary>현재 웨이브가 배출·처치 완료되면 다음 웨이브를 시작하거나 라운드 대기·최종 클리어 상태로 전환한다.</summary>
        private void CheckProgressionWaveEnd()
        {
            if (!_rules.IsProgressionWaveFinished(_spawn.IsWaveSpawnFinished)) return;
            if (_world.WaveNumber < StageCatalog.PROGRESSION_WAVE_COUNT)
            {
                _world.WaveNumber++;
                StartProgressionWave();
                return;
            }
            if (_world.IsBossStage)
            {
                foreach (SimulationEnemy enemy in _world.Enemies)
                {
                    enemy.Hexa?.Cancel(enemy);
                    _stats.RecordEnemyEnd(enemy, Time, "wave_ended_alive");
                }
                _world.Enemies.Clear();
            }
            _statuses.ClearAll();
            _world.Player.CancelDash(Time);
            _loot.CollectRemainingDrops();
            if (_world.StageNumber == StageCatalog.PROGRESSION_STAGE_COUNT && _world.RoundNumber == StageCatalog.PROGRESSION_ROUND_COUNT)
            {
                _world.Stage = MissionStage.Cleared;
            }
            else _world.Stage = MissionStage.RoundBreak;
            CancelCombat();
            _spawn.Director = null;
        }

        /// <summary>현재 웨이브 편성을 설치하고 보스 판정을 초기화한다. isHexaPreview일 때만 원본을 보존한 Hexa 단독 편성을 사용한다.</summary>
        private void StartProgressionWave(bool isHexaPreview = false)
        {
            _rules.ResetForWave();
            WaveDefinition wave = _world.Stages.GetProgressionStage(_world.StageNumber).Rounds[_world.RoundNumber - 1].Waves[_world.WaveNumber - 1];
            if (isHexaPreview) wave = wave.WithBoss(HexaDefinition.ID);
            _spawn.StartWave(wave, !isHexaPreview);
        }

        /// <summary>
        /// 스테이지 편성으로 외곽 벽만 있는 맵, 맵 중앙의 플레이어, 스폰 반경, 처치 집계를 준비하고 연속 미션을 시작한다.
        /// 진행 스테이지·라운드와 함께 이어하면 마지막 웨이브를 끝낸 라운드 대기 상태로 두고, 아니면 1스테이지 1라운드 1웨이브로 시작한다.
        /// 스폰 반경 = 기본 발사 사거리 + 가장 큰 적 반경 + 여유이며 막 스폰된 적에게 기본 발사체가 닿지 않게 한다.
        /// 적 분리 격자를 맵 크기와 적 상한, 가장 큰 적 반경의 2배 칸 크기로 준비한다. 가장 큰 적 반경에는 모든 연속 스테이지 엘리트 타입의 반경 배율을 반영한다.
        /// 기본 발사 수치는 runes에서 조회한다.
        /// </summary>
        private void BeginTimedBattle(RuneCatalog runes, int progressionStage, int progressionRound, double progressionHp)
        {
            _world.Stage = MissionStage.Combat;
            bool isResumingRoundBreak = progressionStage > 0;
            if (isResumingRoundBreak)
            {
                _world.StageNumber = progressionStage;
                _world.RoundNumber = progressionRound;
                _world.WaveNumber = StageCatalog.PROGRESSION_WAVE_COUNT;
            }
            else
            {
                _world.StageNumber = 1;
                _world.RoundNumber = 1;
                _world.WaveNumber = 1;
            }
            _world.Map = MissionMap.CreateOpen(_world.Stages.Map.Columns, _world.Stages.Map.Rows, _world.Sector.TileSize);
            _world.Player = new SimulationPlayer(_world.MaxHp, _world.MaxEnergy, MapCenter);
            if (progressionHp > 0) _world.Player.SetHp(progressionHp);
            Adaptation.SetEnabled(false);
            RuneStats bolt = runes.Get(BOLT_RUNE_ID).Stats;
            double largestRadius = 0;
            foreach (EnemyDefinition enemy in _world.EnemyCatalog.Enemies) largestRadius = Math.Max(largestRadius, enemy.Radius);
            foreach (ProgressionStageDefinition progression in _world.Stages.ProgressionStages)
                foreach (EliteSpawnDefinition elite in progression.EliteTypes)
                    largestRadius = Math.Max(largestRadius, _world.EnemyCatalog.Get(elite.EnemyId).Radius * elite.RadiusMultiplier);
            _enemy.ResizeSeparation(_world.Map.Width, _world.Map.Height, EnemyLimit, largestRadius * 2);
            _spawn.SpawnRadius = (double)bolt.Speed * bolt.Lifetime + largestRadius + _world.Stages.Spawn.RingMargin;
            _rules.ResetForBattle();
            if (isResumingRoundBreak) _world.Stage = MissionStage.RoundBreak;
            else StartProgressionWave();
        }


        /// <summary>설정 ID, 위치, 더미 여부, 체력, 엘리트 정의, 목표 집계 여부와 캐리어 소유자 ID로 적을 생성한다. 스폰 시스템에 위임한다.</summary>
        public SimulationEnemy SpawnEnemy(string id, SimVector position, bool isDummy = false, double hp = 0, EliteSpawnDefinition elite = null, bool isKillTarget = false, int carrierOwnerId = 0)
            => _spawn.SpawnEnemy(id, position, isDummy, hp, elite, isKillTarget, carrierOwnerId);

        /// <summary>사망, 클리어 및 시간 초과 시 지속 개체, 예약 실행과 분열 대기를 취소하고 Hexa 소환물과 그 공격을 처치 부작용 없이 정리한다.</summary>
        private void CancelCombat()
        {
            _feedback.CancelBeamFlashes();
            _executor.CancelBeamChannels();
            foreach (SimulationEnemy enemy in _world.Enemies)
            {
                enemy.CancelDisplacement();
                enemy.Hydra?.Cancel();
                enemy.Legolas?.Cancel();
                if (enemy.Legolas != null) { enemy.AimEndsAt = 0; enemy.WarningUntil = 0; }
            }
            _enemy.CleanupAllHexaOwners();
            _world.SpellEntities.Clear(); _world.EnemyProjectiles.Clear(); _executor.ClearScheduled(); _enemy.PendingEnemyShots.Clear(); _spawn.PendingSplits.Clear(); _loot.ClearDeadEnemies();
            _spawn.PendingSpawns.Clear(); _feedback.ClearSpawnWarnings();
            if (_world.Player != null) _world.Player.CancelSpellExecutions();
        }

        /// <summary>표시 수명이 지난 피해 숫자, 체력 회복 표시, 전격 연쇄 구간, 노드 이벤트 및 5초 DPS 샘플을 제거한다.</summary>
        private void TrimFeedback()
        {
            _feedback.Trim();
            _stats.TrimRollingDamage(_world.Tick);
        }

        /// <summary>지정 보스가 현재 스테이지·라운드에서 패턴을 시작한 횟수를 누적한다.</summary>
        internal void RecordBossPatternUse(SimulationEnemy enemy, string pattern)
            => _stats.RecordBossPatternUse(_world, enemy, pattern);

        /// <summary>변경된 적과 보스 패턴 누계를 로그용으로 반환한다.</summary>
        public IReadOnlyList<Dictionary<string, object>> TakeEnemyCombatLogSnapshot()
            => _stats.TakeEnemyCombatLogSnapshot(Time);

        /// <summary>장착된 행동을 순회하고 노드별 실제 피해와 OnHit 후속 생성 누계를 로그용 복사본으로 반환한다.</summary>
        public IReadOnlyList<Dictionary<string, object>> GetActionCombatLogSnapshot()
            => _stats.GetActionCombatLogSnapshot(_executor.Loadout);

    }
}
