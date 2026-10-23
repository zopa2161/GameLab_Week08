using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>적 이동·공격·저격·인터셉터·캐리어·릴레이 회복, 보스 패턴과 패치, 적 탄환·예약 사격, 플레이어 피격과 자폭을 처리한다.</summary>
    internal sealed class EnemySystem
    {
        private const int RELAY_REGEN_INTERVAL_TICKS = 60;
        private const double EXPLOSION_VISUAL_SECONDS = 0.25;

        private readonly SimulationWorld _world;
        private readonly SimulationFeedback _feedback;
        private readonly SimulationStats _stats;
        private readonly SpawnSystem _spawn;
        private readonly EnemyStatusSystem _statuses;
        private readonly SpellExecutor _executor;
        private readonly RuneSimulation _simulation;
        private readonly List<PendingEnemyShot> _pendingEnemyShots = new List<PendingEnemyShot>();
        private readonly List<SimulationEnemy> _hexaOwners = new List<SimulationEnemy>();
        private readonly List<int> _hexaCleanupIds = new List<int>();
        private readonly EnemySeparation _separation = new EnemySeparation();
        private readonly Func<SimulationEnemy, bool> _isSeparationFixed;
        private readonly Action<double> _hurtPlayer;
        private int _hydraSourceEnemyId;

        /// <summary>예약 사격 목록이다. 상태 해시와 전투 취소가 같은 인스턴스를 쓴다.</summary>
        internal List<PendingEnemyShot> PendingEnemyShots => _pendingEnemyShots;

        /// <summary>
        /// 월드·표시 버퍼·통계·스폰·상태 이상·스펠 실행기와 시뮬레이션 참조로 만들고 분리 고정 판정 대리자를 준비한다.
        /// 시뮬레이션 참조는 생성 순서가 순환하는 벽 접촉 판정(FirstWallFraction)에만 쓴다.
        /// </summary>
        internal EnemySystem(SimulationWorld world, SimulationFeedback feedback, SimulationStats stats, SpawnSystem spawn, EnemyStatusSystem statuses, SpellExecutor executor, RuneSimulation simulation)
        {
            _world = world;
            _feedback = feedback;
            _stats = stats;
            _spawn = spawn;
            _statuses = statuses;
            _executor = executor;
            _simulation = simulation;
            _isSeparationFixed = IsSeparationFixed;
            _hurtPlayer = HurtPlayer;
        }

        /// <summary>미션 적 분리 격자를 맵 크기, 적 상한과 칸 크기로 다시 만든다.</summary>
        internal void ResizeSeparation(int width, int height, int enemyLimit, double cellSize) => _separation.Resize(width, height, enemyLimit, cellSize);

        /// <summary>미션 적끼리의 겹침을 분리한다. 보스·빙결·돌진 중인 적은 고정한다.</summary>
        internal void ApplySeparation() => _separation.Apply(_world.Enemies, _world.Map, _world.Balance.Combat, _isSeparationFixed);
        /// <summary>
        /// 60틱마다 살아 있는 릴레이 반경 안의 다른 적 체력을 RelayRegen만큼 회복시킨다.
        /// 릴레이 자신은 오라 대상이 아니고 최대 체력까지만 회복한다.
        /// </summary>
        internal void AdvanceRelayRegen()
        {
            if (_world.Tick % RELAY_REGEN_INTERVAL_TICKS != 0) return;
            foreach (SimulationEnemy relay in _world.Enemies)
            {
                if (!relay.IsAlive || !relay.Definition.HasTrait(EnemyDefinition.TRAIT_RELAY_AURA)) continue;
                foreach (SimulationEnemy enemy in _world.Enemies)
                {
                    if (enemy == relay || !enemy.IsAlive || SimVector.Distance(relay.Position, enemy.Position) > _world.Balance.Combat.RelayRadius) continue;
                    if (enemy.Heal(_world.Balance.Combat.RelayRegen) > 0) _feedback.AddHealPulse(enemy.Position);
                }
            }
        }

        /// <summary>
        /// 적마다 이동 전 플레이어 방향을 구해 이동 판단을 적용한 뒤 접촉, 예고 사격, 돌진 및 거버너 페이즈 패턴을 진행한다.
        /// 공격 판정은 이동 전 거리·방향을, 접촉과 발사 위치는 이동 후 위치를 사용한다.
        /// </summary>
        internal void AdvanceEnemies()
        {
            CleanupDeadHexaOwners();
            int initialCount = _world.Enemies.Count;
            for (int i = 0; i < initialCount; i++)
            {
                SimulationEnemy enemy = _world.Enemies[i]; if (!enemy.IsAlive) continue;
                enemy.Observe(_world.Time);
                bool hasDisplacement = AdvanceEnemyDisplacement(enemy);
                if (enemy.IsDummy) continue;
                if (enemy.Hydra != null)
                {
                    _hydraSourceEnemyId = enemy.Id;
                    enemy.Hydra.Advance(enemy, _world, _simulation, _hurtPlayer);
                    _hydraSourceEnemyId = 0;
                    if (!enemy.Hydra.IsAirborne) HurtByContact(enemy);
                    continue;
                }
                if (_statuses.HasEnemyStatus(enemy, EnemyStatusType.Freeze))
                {
                    if (enemy.Definition.Attack == EnemyDefinition.ATTACK_SNIPER)
                    { enemy.AimEndsAt = 0; enemy.WarningUntil = 0; enemy.AttackAt = _world.Time + RuneSimulation.STEP_SECONDS; }
                    enemy.Hexa?.PauseSniper(enemy, _world.Time);
                    continue;
                }
                if (enemy.Hexa != null)
                {
                    if (!enemy.Hexa.IsCancelled)
                    {
                        enemy.Hexa.Advance(enemy, _world);
                        if (enemy.Hexa.IsSummonDue(_world.Tick))
                            enemy.Hexa.CompleteSummonBundle(_world.Tick,
                                _spawn.SpawnHexaSummonBundle(enemy) == HexaDefinition.SUMMON_COUNT);
                        HurtByContact(enemy);
                    }
                    continue;
                }
                SimVector offset = _world.Player.Position - enemy.Position; SimVector direction = offset.Normalized();
                BossDefinition bossPattern = enemy.Boss;
                bool isBoss = bossPattern != null && bossPattern.IsGovernorPattern;
                if (isBoss) { CheckBossPatch(enemy, bossPattern); if (enemy.IsPatching) continue; }
                double speedMultiplier = isBoss && enemy.Phase == 3 ? bossPattern.PhaseThreeMultiplier : 1;
                var context = new EnemyMoveContext(offset, direction, _world.Time, _world.IsMission, _world.Stages.StationaryAdvanceSpeed, isBoss ? bossPattern.PreferredDistance : 0, speedMultiplier);
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
            if (!enemy.TryAdvanceDisplacement(_world.Tick, RuneSimulation.STEP_SECONDS, out SimVector displacement)) return false;
            SimVector next = enemy.Position + displacement;
            double wall = _simulation.FirstWallFraction(enemy.Position, next, enemy.Radius);
            if (!double.IsPositiveInfinity(wall))
            {
                next = enemy.Position + displacement * Math.Max(0, wall - RuneSimulation.COLLISION_EPSILON);
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
                if (enemy.WarningUntil > 0 && _world.Time + 0.000001 >= enemy.WarningUntil)
                { ShootBurst(enemy, enemy.AttackDirection, 1); enemy.WarningUntil = 0; enemy.AttackAt = _world.Time + definition.AttackInterval - definition.WarningSeconds; }
                else if (enemy.WarningUntil == 0 && _world.Time + 0.000001 >= enemy.AttackAt)
                { enemy.AttackDirection = direction; enemy.WarningUntil = _world.Time + definition.WarningSeconds; }
                return;
            }
            if (definition.Attack == EnemyDefinition.ATTACK_DASH_CONTACT)
            {
                if (_world.Time < enemy.DashUntil) { HurtByContact(enemy); return; }
                if (enemy.WarningUntil > 0)
                { if (_world.Time + 0.000001 >= enemy.WarningUntil) { enemy.WarningUntil = 0; enemy.DashUntil = _world.Time + definition.DashSeconds; enemy.AttackAt = _world.Time + definition.AttackInterval; } return; }
                if (offset.Length < definition.AttackRange && _world.Time >= enemy.AttackAt)
                { enemy.WarningUntil = _world.Time + definition.WarningSeconds; enemy.AttackDirection = direction; }
                return;
            }
            if (_world.Time >= enemy.AttackAt && HurtByContact(enemy)) enemy.AttackAt = _world.Time + definition.AttackInterval;
        }

        /// <summary>저격수의 추적 조준, 고정 대기와 단발 발사를 진행하며 같은 저장 경로를 표시와 사격에 사용한다.</summary>
        private void AdvanceSniper(SimulationEnemy enemy, SimVector direction)
        {
            EnemyDefinition definition = enemy.Definition;
            if (!enemy.IsAiming && _world.Time + RuneSimulation.COLLISION_EPSILON < enemy.AttackAt) return;
            if (!AdvanceSniperAim(enemy, definition, direction, _world.Time)) return;
            _world.EnemyProjectiles.Add(new SimulationProjectile(enemy.AttackOrigin, enemy.AttackDirection, definition.ProjectileSpeed,
                enemy.Damage, definition.ProjectileRadius, definition.ProjectileLifetime, sourceEnemyId: enemy.Id));
            enemy.AimEndsAt = 0; enemy.WarningUntil = 0;
            enemy.AttackAt = _world.Time + definition.AttackInterval - definition.AimSeconds - definition.LockSeconds;
        }

        /// <summary>저격 정의의 조준 시작·추적·고정을 한 틱 진행하고 발사 시점이면 true를 반환한다.</summary>
        internal static bool AdvanceSniperAim(SimulationEnemy enemy, EnemyDefinition definition, SimVector direction, double time)
            => AdvanceSniperAim(enemy, definition, direction, time, out _);

        /// <summary>
        /// 저격 정의의 조준 시작·추적·고정을 한 틱 진행하고 발사 시점이면 true를 반환한다.
        /// 조준 중에는 현재 위치와 방향을 저장하고, 고정 중에는 저장값을 유지하며 isLocked로 고정 구간을 알린다.
        /// </summary>
        internal static bool AdvanceSniperAim(SimulationEnemy enemy, EnemyDefinition definition, SimVector direction, double time, out bool isLocked)
            => AdvanceSniperAim(enemy, direction, time, definition.AimSeconds, definition.LockSeconds, out isLocked);

        /// <summary>
        /// 지정한 조준·고정 시간으로 저격 조준을 한 틱 진행하고 발사 시점이면 true를 반환한다.
        /// 조준 중에는 현재 위치와 방향을 저장하고, 고정 중에는 저장값을 유지하며 isLocked로 고정 구간을 알린다.
        /// </summary>
        internal static bool AdvanceSniperAim(SimulationEnemy enemy, SimVector direction, double time,
            double aimSeconds, double lockSeconds, out bool isLocked)
        {
            isLocked = false;
            if (!enemy.IsAiming)
            {
                enemy.AimEndsAt = time + aimSeconds;
                enemy.WarningUntil = enemy.AimEndsAt + lockSeconds;
                enemy.ShotOrigin = enemy.Position; enemy.AttackDirection = direction;
                return false;
            }
            if (time + RuneSimulation.COLLISION_EPSILON < enemy.AimEndsAt)
            { enemy.ShotOrigin = enemy.Position; enemy.AttackDirection = direction; return false; }
            isLocked = true;
            return time + RuneSimulation.COLLISION_EPSILON >= enemy.WarningUntil;
        }

        /// <summary>인터셉터의 이동·정지 남은 시간을 줄이고 이동 종료마다 플레이어 방향에 한 발을 발사한다.</summary>
        private void AdvanceInterceptor(SimulationEnemy enemy, SimVector direction)
        {
            enemy.BehaviorRemaining -= RuneSimulation.STEP_SECONDS;
            if (enemy.BehaviorRemaining > RuneSimulation.COLLISION_EPSILON) return;
            if (!enemy.IsStopping) ShootFan(enemy, direction, 1, 1);
            enemy.IsBehaviorStopping = !enemy.IsStopping;
            enemy.BehaviorRemaining += enemy.IsStopping ? enemy.Definition.StopSeconds : enemy.Definition.MoveSeconds;
        }

        /// <summary>캐리어의 생성 타이머와 보류 수를 진행하고 적 정의의 생성 대상 순서대로 보상 없는 자식을 주변에 생성한다.</summary>
        private void AdvanceCarrier(SimulationEnemy enemy)
        {
            EnemyDefinition carrier = enemy.Definition;
            enemy.SpawnRemainingSeconds -= RuneSimulation.STEP_SECONDS;
            if (enemy.SpawnRemaining == 0)
            {
                if (enemy.SpawnRemainingSeconds > RuneSimulation.COLLISION_EPSILON) return;
                enemy.SpawnRemaining = carrier.CarrierCount;
            }
            int alive = 0;
            foreach (SimulationEnemy child in _world.Enemies) if (child.IsAlive && child.CarrierOwnerId == enemy.Id) alive++;
            while (enemy.SpawnRemaining > 0 && _world.Enemies.Count < _world.EnemyLimit && alive < carrier.CarrierMaxAlive)
            {
                string id = carrier.CarrierTargets[enemy.SpawnCursor % carrier.CarrierTargets.Count];
                double radius = _world.EnemyCatalog.Get(id).Radius;
                double angle = enemy.SpawnCursor * Math.PI * (3 - Math.Sqrt(5));
                double distance = Math.Max(carrier.CarrierDistance, enemy.Radius + radius + SpawnSystem.SAME_SPAWN_DISTANCE);
                SimVector position = enemy.Position + new SimVector(Math.Cos(angle), Math.Sin(angle)) * distance;
                _spawn.SpawnEnemy(id, _world.Map.NearestFree(position, radius), carrierOwnerId: enemy.Id);
                enemy.SpawnCursor++; enemy.SpawnRemaining--; alive++;
            }
            if (enemy.SpawnRemaining == 0) enemy.SpawnRemainingSeconds = carrier.CarrierInterval;
            else enemy.SpawnRemainingSeconds = 0;
        }

        /// <summary>냉기 감속을 반영하여 적을 벽 충돌 가능한 방향으로 이동시킨다.</summary>
        private void MoveEnemy(SimulationEnemy enemy, SimVector direction, double speed)
        { double slow = Math.Max(0, 1 - _world.Balance.Combat.ChillSlow * _statuses.GetChillStacks(enemy)); enemy.Move(_world.Map.Move(enemy.Position, direction * (speed * enemy.SpeedMultiplier * slow * RuneSimulation.STEP_SECONDS), enemy.Radius), direction); }

        /// <summary>적 분리에서 밀리지 않는 고정 대상인지 반환한다. 보스, 빙결 중인 적, 돌진 중인 적은 자리를 지키고 상대만 밀어낸다.</summary>
        internal bool IsSeparationFixed(SimulationEnemy enemy) => enemy.Boss != null || _statuses.HasEnemyStatus(enemy, EnemyStatusType.Freeze) || _world.Time < enemy.DashUntil;

        /// <summary>적과 플레이어가 겹치면 피해를 적용하고 접촉 여부를 반환한다.</summary>
        private bool HurtByContact(SimulationEnemy enemy)
        {
            if (!_world.IsMission || _world.Time < enemy.ContactGraceUntil || SimVector.Distance(enemy.Position, _world.Player.Position) > enemy.Radius + _world.Sector.PlayerRadius) return false;
            HurtPlayer(enemy.Damage, enemy.Id); return true;
        }

        /// <summary>플레이어 피해에 디버그 무적과 기본 피격 무적을 반영한다.</summary>
        internal void HurtPlayer(double damage) => HurtPlayer(damage, _hydraSourceEnemyId);

        /// <summary>피해량과 공격한 적 ID로 피격을 처리하고 실제 HP 피해·보호막 흡수를 해당 적에게 귀속한다.</summary>
        private void HurtPlayer(double damage, int sourceEnemyId)
        {
            if (_world.DebugInvulnerable || !_world.IsMission) return;
            double shieldBefore = _world.Player.Shield;
            double actual = _world.Player.Hurt(damage, _world.Time, _world.Balance.Player.HurtInvulnerability);
            if (actual > 0) _feedback.AddDamageNumber(_world.Player.Position, actual, "player");
            _stats.RecordPlayerDamage(_world, sourceEnemyId, actual, Math.Max(0, shieldBefore - _world.Player.Shield));
            if (actual > 0 || _world.Player.Shield < shieldBefore) _executor.TryCastTrigger(SpellGrammar.TRIGGER_ON_HIT_TAKEN);
        }

        /// <summary>
        /// 시전자의 상태 이상 피해(화상 DoT)를 적용한다. 피격 무적을 무시하고 주지 않으며 OnHitTaken을 발생시키지 않는다.
        /// 시험 도크(비전투)에서는 상태만 표시하고 체력을 깎지 않는다(결정 Q13).
        /// </summary>
        internal void HurtPlayerByStatus(double damage)
        {
            if (_world.DebugInvulnerable || !_world.IsMission) return;
            double shieldBefore = _world.Player.Shield;
            double actual = _world.Player.HurtByStatus(damage);
            _stats.RecordPlayerDamage(_world, -1, actual, Math.Max(0, shieldBefore - _world.Player.Shield));
            if (actual > 0) _feedback.AddDamageNumber(_world.Player.Position, actual, "player");
        }

        /// <summary>적 공격 설정의 탄환을 부채꼴로 발사한다.</summary>
        private void ShootFan(SimulationEnemy enemy, SimVector direction, int count, double speedMultiplier)
        {
            EnemyDefinition definition = enemy.Definition;
            for (int i = 0; i < count; i++)
            { double spread = count == 1 ? 0 : (i / (double)(count - 1) * 2 - 1) * definition.SpreadDegrees * RuneSimulation.DEGREES_TO_RADIANS; SimVector aim = direction.Rotated(spread); _world.EnemyProjectiles.Add(new SimulationProjectile(enemy.Position + aim * (enemy.Radius + _world.Balance.Sim.WandOffset), aim, definition.ProjectileSpeed * speedMultiplier, enemy.Damage, definition.ProjectileRadius, definition.ProjectileLifetime, sourceEnemyId: enemy.Id)); }
        }

        /// <summary>예고된 조준 방향에 첫 탄을 발사하고 데이터 간격에 맞춰 나머지 2발을 예약한다.</summary>
        private void ShootBurst(SimulationEnemy enemy, SimVector direction, double speedMultiplier)
        {
            ShootFan(enemy, direction, 1, speedMultiplier);
            for (int i = 1; i < 3; i++) _pendingEnemyShots.Add(new PendingEnemyShot(_world.Tick + Math.Max(1, (int)Math.Round(i * enemy.Definition.BurstInterval * RuneSimulation.TICK_RATE)), enemy, direction, speedMultiplier));
        }

        /// <summary>예약 시점이 된 연사 탄환을 생존하며 행동 가능한 적의 현재 위치에서 발사한다.</summary>
        internal void RunEnemyShots()
        {
            for (int i = 0; i < _pendingEnemyShots.Count;)
            {
                PendingEnemyShot shot = _pendingEnemyShots[i];
                if (!shot.Owner.IsAlive || shot.Owner.IsPatching || _statuses.HasEnemyStatus(shot.Owner, EnemyStatusType.Freeze)) { _pendingEnemyShots.RemoveAt(i); continue; }
                if (shot.Tick > _world.Tick) { i++; continue; }
                _pendingEnemyShots.RemoveAt(i); ShootFan(shot.Owner, shot.Direction, 1, shot.SpeedMultiplier);
            }
        }

        /// <summary>
        /// 보스 패턴의 폭발 사격, 조준 사격, 세트 단위 예고 장판, 증원 주기 및 접촉 피해를 페이즈에 맞춰 처리한다.
        /// 장판은 현재 페이즈 설정으로 세트당 Count개를 Interval 간격으로 쓰고 다음 세트 전에 SetInterval만큼 쉰다.
        /// 페이즈 전환과 이동은 호출 전에 끝난 상태여야 하며 조준은 이동 전 방향을 사용한다.
        /// </summary>
        private void AdvanceBoss(SimulationEnemy boss, BossDefinition pattern, SimVector direction)
        {
            if (pattern.IsMiniBoss) { AdvanceMiniGovernor(boss, pattern, direction); return; }
            double multiplier = boss.Phase == 3 ? pattern.PhaseThreeMultiplier : 1;
            if (_world.Time + 0.000001 >= boss.AttackAt)
            {
                if (boss.Phase == 1 || boss.Phase == 3)
                    ShootRadial(boss, pattern.RadialCount, multiplier);
                if (boss.Phase >= 2) ShootBurst(boss, direction, multiplier);
                boss.AttackAt = _world.Time + (boss.Phase == 2 ? pattern.AimInterval : pattern.RadialInterval) / multiplier;
            }
            if (boss.Phase >= 2 && _world.Time >= boss.HazardAt)
            { BossHazard hazard = pattern.GetHazard(boss.Phase); _world.EnemyProjectiles.Add(new SimulationProjectile(_world.Player.Position, SimVector.Zero, 0, hazard.Damage * boss.DamageMultiplier, hazard.Radius, hazard.Lifetime + hazard.Warning, true, hazard.Warning, sourceEnemyId: boss.Id)); bool setEnded = --boss.HazardRemaining <= 0; if (setEnded) boss.HazardRemaining = hazard.Count; boss.HazardAt = _world.Time + (setEnded ? hazard.SetInterval : hazard.Interval) / multiplier; }
            if (_world.Time >= boss.ReinforcementAt)
            {
                if (_world.IsMission) _spawn.SpawnReinforcements(boss, pattern.Reinforcement);
                else for (int i = 0; i < pattern.Reinforcement.Count; i++) _spawn.SpawnEnemy(pattern.Reinforcement.EnemyId, _world.Map.GetPoint((char)('1' + i * 2)), false, 0);
                boss.ReinforcementAt = _world.Time + pattern.Reinforcement.Interval;
            }
            HurtByContact(boss);
        }

        /// <summary>보스 설정의 개수와 탄속 배율로 전방위에 균등한 간격의 단발 탄환을 발사한다.</summary>
        private void ShootRadial(SimulationEnemy boss, int count, double speedMultiplier)
        {
            for (int i = 0; i < count; i++)
            {
                double angle = i * 2 * Math.PI / count;
                ShootFan(boss, new SimVector(Math.Cos(angle), Math.Sin(angle)), 1, speedMultiplier);
            }
        }

        /// <summary>짧은 예고 후 미니 거버너의 방사형 사격과 2페이즈의 방향 고정 3연사를 교대로 실행한다.</summary>
        private void AdvanceMiniGovernor(SimulationEnemy boss, BossDefinition pattern, SimVector direction)
        {
            if (boss.WarningUntil > 0)
            {
                if (_world.Time + RuneSimulation.COLLISION_EPSILON >= boss.WarningUntil)
                {
                    if (boss.Phase == 2 && boss.IsNextBossBurst) ShootBurst(boss, boss.AttackDirection, 1);
                    else ShootRadial(boss, pattern.RadialCount, 1);
                    if (boss.Phase == 2) boss.IsNextBossBurst = !boss.IsNextBossBurst;
                    boss.WarningUntil = 0;
                    boss.AttackAt = _world.Time + (boss.Phase == 2 ? pattern.AimInterval : pattern.RadialInterval);
                }
            }
            else if (_world.Time + RuneSimulation.COLLISION_EPSILON >= boss.AttackAt)
            {
                boss.AttackDirection = direction;
                boss.WarningUntil = _world.Time + boss.Definition.WarningSeconds;
            }
            HurtByContact(boss);
        }

        /// <summary>체력 문턱에서 보스 페이즈를 전환한다. 미니 보스는 2페이즈까지만 전환하고 장판·학습을 적용하지 않는다.</summary>
        internal void CheckBossPatch(SimulationEnemy boss, BossDefinition pattern)
        {
            if (boss.IsPatching) return;
            if (pattern.IsMiniBoss)
            {
                if (boss.Phase != 1 || boss.Hp / boss.MaxHp > pattern.PhaseTwoThreshold) return;
                boss.Patch(2, _world.Time, pattern.PatchSeconds);
                boss.Observe(_world.Time);
                boss.WarningUntil = 0;
                boss.IsNextBossBurst = false;
                boss.AttackAt = _world.Time + pattern.PatchSeconds;
                return;
            }
            int next = boss.Phase == 1 && boss.Hp / boss.MaxHp <= pattern.PhaseTwoThreshold ? 2 : boss.Phase == 2 && boss.Hp / boss.MaxHp <= pattern.PhaseThreeThreshold ? 3 : boss.Phase;
            if (next == boss.Phase) return;
            boss.Patch(next, _world.Time, pattern.PatchSeconds); boss.Observe(_world.Time); boss.HazardRemaining = pattern.GetHazard(next).Count; _world.Adaptation.LockTopDamageTags();
            boss.AttackAt = _world.Time + pattern.PatchSeconds + boss.Definition.WarningSeconds;
        }

        /// <summary>
        /// Hexa 한 기의 실행을 취소하고 그 보스가 소환한 적만 월드에서 제거한다. 제거한 소환물의 탄환·예약 사격·상태 이상과
        /// 보스 자신의 탄환도 함께 지우며 처치 보상·폭발·분열·통계는 만들지 않는다. 다른 소유자의 소환물과 일반 적은 유지한다.
        /// 여러 번 호출해도 이미 제거된 개체를 다시 처리하지 않는다.
        /// </summary>
        internal void CleanupHexaOwner(SimulationEnemy owner)
        {
            if (owner == null) return;
            owner.Hexa?.Cancel(owner);
            _hexaCleanupIds.Clear();
            _hexaCleanupIds.Add(owner.Id);
            for (int i = _world.Enemies.Count - 1; i >= 0; i--)
            {
                SimulationEnemy summon = _world.Enemies[i];
                if (summon.SummonerId != owner.Id) continue;
                _hexaCleanupIds.Add(summon.Id);
                _statuses.RemoveEnemyStatuses(summon.Id);
                _world.Enemies.RemoveAt(i);
            }
            for (int i = _pendingEnemyShots.Count - 1; i >= 0; i--)
                if (_hexaCleanupIds.Contains(_pendingEnemyShots[i].Owner.Id)) _pendingEnemyShots.RemoveAt(i);
            for (int i = _world.EnemyProjectiles.Count - 1; i >= 0; i--)
                if (_hexaCleanupIds.Contains(_world.EnemyProjectiles[i].SourceEnemyId)) _world.EnemyProjectiles.RemoveAt(i);
            _hexaCleanupIds.Clear();
        }

        /// <summary>전투 종료 시 모든 Hexa 소유자의 소환물과 공격을 정리한다. 정리 목록을 먼저 모아 컬렉션을 순회 중 수정하지 않는다.</summary>
        internal void CleanupAllHexaOwners()
        {
            _hexaOwners.Clear();
            foreach (SimulationEnemy enemy in _world.Enemies)
                if (enemy.Hexa != null) _hexaOwners.Add(enemy);
            foreach (SimulationEnemy owner in _hexaOwners) CleanupHexaOwner(owner);
            _hexaOwners.Clear();
        }

        /// <summary>이번 틱에 죽은 Hexa 소유자의 소환물을 적 행동과 탄환 피해 처리 전에 정리한다.</summary>
        private void CleanupDeadHexaOwners()
        {
            _hexaOwners.Clear();
            foreach (SimulationEnemy enemy in _world.Enemies)
                if (enemy.Hexa != null && !enemy.IsAlive) _hexaOwners.Add(enemy);
            foreach (SimulationEnemy owner in _hexaOwners) CleanupHexaOwner(owner);
            _hexaOwners.Clear();
        }

        /// <summary>적 탄환과 위험 장판의 수명, 벽 충돌 및 플레이어 피격을 처리하며 유도 탄환은 이동 전에 플레이어 방향으로 보정하고 죽은 Hexa의 탄환·소환물은 피해 처리 전에 제거한다.</summary>
        internal void AdvanceHostileProjectiles()
        {
            CleanupDeadHexaOwners();
            for (int i = _world.EnemyProjectiles.Count - 1; i >= 0; i--)
            {
                SimulationProjectile projectile = _world.EnemyProjectiles[i];
                SimVector previous = projectile.Position;
                projectile.Advance(Math.Min(RuneSimulation.STEP_SECONDS, Math.Max(0, projectile.Lifetime - projectile.Age)), _world.Player.Position);
                if (!projectile.IsHazard)
                {
                    double wall = _simulation.FirstWallFraction(previous, projectile.Position, projectile.Radius);
                    double hit = SpellGeometry.CircleContactFraction(previous, projectile.Position, _world.Player.Position, projectile.Radius + _world.Sector.PlayerRadius);
                    if (hit < wall && !double.IsPositiveInfinity(hit)) { HurtPlayer(projectile.Damage, projectile.SourceEnemyId); _world.EnemyProjectiles.RemoveAt(i); continue; }
                    if (!double.IsPositiveInfinity(wall)) { _world.EnemyProjectiles.RemoveAt(i); continue; }
                }
                else if (!projectile.IsExplosion && !projectile.IsWarning && SimVector.Distance(projectile.Position, _world.Player.Position) <= projectile.Radius + _world.Sector.PlayerRadius)
                    HurtPlayer(projectile.Damage, projectile.SourceEnemyId);
                if (projectile.Age + RuneSimulation.COLLISION_EPSILON >= projectile.Lifetime) _world.EnemyProjectiles.RemoveAt(i);
            }
        }
        /// <summary>사망 위치 주변 플레이어와 살아 있는 적에게 단발 폭발 피해를 주고 짧은 표시를 남긴다. 플레이어 강화·학습·DPS는 적용하지 않는다.</summary>
        internal void ExplodeEnemy(SimulationEnemy source)
        {
            double radius = source.Definition.ExplosionRadius;
            double damage = source.Definition.ExplosionDamage * source.DamageMultiplier;
            _world.EnemyProjectiles.Add(new SimulationProjectile(source.Position, SimVector.Zero, 0, 0, radius, EXPLOSION_VISUAL_SECONDS, true, 0, true));
            if (SimVector.Distance(source.Position, _world.Player.Position) <= radius + _world.Sector.PlayerRadius) HurtPlayer(damage, source.Id);
            foreach (SimulationEnemy target in _world.Enemies)
            {
                if (!target.IsAlive || target.IsPatching || SimVector.Distance(source.Position, target.Position) > radius + target.Radius) continue;
                double incoming = target.Hydra == null ? damage : target.Hydra.LimitDamage(damage, false, _world.Time, target.MaxHp);
                double actual = target.Hurt(incoming, _world.Time);
                if (actual > 0)
                {
                    _stats.EnemyDamageEventCount++;
                    _feedback.AddDamageNumber(target.Position, actual, "raw");
                }
                if (target.IsAlive && target.Boss != null && target.Boss.IsGovernorPattern) CheckBossPatch(target, target.Boss);
            }
        }
    }
}
