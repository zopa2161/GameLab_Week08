using System;
using System.Text;

namespace RuneCode
{
    public enum HexaSubAttack { None, Spray, Homing, Sniper }

    public enum HexaTopPattern { None, Summon, Spiral, Composite }

    /// <summary>Hexa 한 개체의 단독 난사·유도·저격·나선, 두 하위 공격 복합 패턴과 6마리 3묶음 소환의 진행·취소 상태를 보관하고, 자동 운영의 우선순위·횟수 쿨타임·3초 대기를 관리한다.</summary>
    public sealed class HexaBossState
    {
        private bool _isCancelled;
        private bool _hasStartedSpray;
        private bool _isSpraying;
        private int _delayTicks;
        private int _sprayElapsedTicks;
        private int _sprayShotCount;
        private bool _hasStartedHoming;
        private bool _isHoming;
        private int _homingElapsedTicks;
        private int _homingShotCount;
        private bool _hasStartedSniper;
        private bool _isSniperAttacking;
        private bool _isSniperPaused;
        private bool _isSniperLocked;
        private int _sniperElapsedTicks;
        private int _sniperShotCount;
        private double _sniperRemainingAimSeconds;
        private double _sniperRemainingLockSeconds;
        private bool _hasStartedComposite;
        private int _compositeStage;
        private int _compositeElapsedTicks;
        private int _compositeCompletionCount;
        private HexaSubAttack _compositeFirst;
        private HexaSubAttack _compositeSecond;
        private bool _hasStartedSummon;
        private bool _isSummoning;
        private int _summonBundlesSpawned;
        private int _summonNextTick;
        private bool _hasStartedSpiral;
        private bool _isSpiraling;
        private int _spiralElapsedTicks;
        private int _spiralBundleCount;
        private int _spiralShotCount;
        private bool _hasAutoOperation;
        private HexaTopPattern _autoPattern;
        private int _summonCooldown;
        private int _spiralCooldown;
        private int _autoCompletionCount;
        private EnemyDefinition _projectileDefinition;
        private EnemyDefinition _sniperDefinition;
        public bool IsCancelled => _isCancelled;
        public bool HasStartedSpray => _hasStartedSpray;
        public bool IsSpraying => _isSpraying;
        public int DelayTicks => _delayTicks;
        public int SprayElapsedTicks => _sprayElapsedTicks;
        public int SprayShotCount => _sprayShotCount;
        public bool HasStartedHoming => _hasStartedHoming;
        public bool IsHoming => _isHoming;
        public int HomingElapsedTicks => _homingElapsedTicks;
        public int HomingShotCount => _homingShotCount;
        public bool HasStartedSniper => _hasStartedSniper;
        public bool IsSniperAttacking => _isSniperAttacking;
        public bool IsSniperPaused => _isSniperPaused;
        public bool IsSniperLocked => _isSniperLocked;
        public int SniperElapsedTicks => _sniperElapsedTicks;
        public int SniperShotCount => _sniperShotCount;
        public double SniperAttackRange => _sniperDefinition != null ? _sniperDefinition.AttackRange : 0;
        public bool HasStartedComposite => _hasStartedComposite;
        public int CompositeStage => _compositeStage;
        public bool IsCompositeComplete => _compositeStage >= HexaDefinition.COMPOSITE_STAGE_COUNT && !HasActiveSubAttack();
        public HexaSubAttack CompositeFirstAttack => _compositeFirst;
        public HexaSubAttack CompositeSecondAttack => _compositeSecond;
        public int CompositeCompletionCount => _compositeCompletionCount;
        public bool HasStartedSummon => _hasStartedSummon;
        public bool IsSummoning => _isSummoning;
        public int SummonBundlesSpawned => _summonBundlesSpawned;
        public bool HasStartedSpiral => _hasStartedSpiral;
        public bool IsSpiraling => _isSpiraling;
        public int SpiralElapsedTicks => _spiralElapsedTicks;
        public int SpiralBundleCount => _spiralBundleCount;
        public int SpiralShotCount => _spiralShotCount;
        public bool HasAutoOperation => _hasAutoOperation;
        public HexaTopPattern AutoPattern => _autoPattern;
        public int SummonCooldown => _summonCooldown;
        public int SpiralCooldown => _spiralCooldown;
        public int AutoCompletionCount => _autoCompletionCount;

        /// <summary>원본 탄환 정의를 저장하고 첫 공격 전 대기부터 단독 난사 한 번을 준비한다. 취소되거나 이미 시작한 개체는 무시한다.</summary>
        internal void BeginSpray(EnemyDefinition projectileDefinition)
        {
            if (_isCancelled || _hasAutoOperation || _hasStartedSpray || _hasStartedHoming || _hasStartedSniper
                || _hasStartedComposite || _hasStartedSummon || _hasStartedSpiral) return;
            _projectileDefinition = projectileDefinition;
            _hasStartedSpray = true;
            _isSpraying = true;
            _delayTicks = HexaDefinition.INITIAL_DELAY_TICKS;
        }

        /// <summary>원본 탄환 정의를 저장하고 첫 공격 전 대기부터 단독 유도 10발을 준비한다. 취소되거나 이미 시작한 개체는 무시한다.</summary>
        internal void BeginHoming(EnemyDefinition projectileDefinition)
        {
            if (_isCancelled || _hasAutoOperation || _hasStartedSpray || _hasStartedHoming || _hasStartedSniper
                || _hasStartedComposite || _hasStartedSummon || _hasStartedSpiral) return;
            _projectileDefinition = projectileDefinition;
            _hasStartedHoming = true;
            _isHoming = true;
            _delayTicks = HexaDefinition.INITIAL_DELAY_TICKS;
        }

        /// <summary>원본 저격 정의를 저장하고 첫 공격 전 대기부터 1초 예고의 단독 저격 5발을 준비한다. 취소되거나 이미 시작한 개체는 무시한다.</summary>
        internal void BeginSniper(EnemyDefinition sniperDefinition)
        {
            if (_isCancelled || _hasAutoOperation || _hasStartedSpray || _hasStartedHoming || _hasStartedSniper
                || _hasStartedComposite || _hasStartedSummon || _hasStartedSpiral) return;
            _sniperDefinition = sniperDefinition;
            _hasStartedSniper = true;
            _isSniperAttacking = true;
            _delayTicks = HexaDefinition.INITIAL_DELAY_TICKS;
        }

        /// <summary>
        /// 원본 탄환·저격 정의를 저장하고 지정된 순서 또는 SimRandom으로 고른 서로 다른 두 하위 공격을
        /// 첫 3초 대기 뒤 첫 공격부터 시작하고, 첫 공격 시작 3초 뒤 두 번째를 시작하도록 준비한다.
        /// 취소되거나 이미 시작한 개체는 무시한다.
        /// </summary>
        internal void BeginComposite(EnemyDefinition projectileDefinition, EnemyDefinition sniperDefinition, SimRandom random,
            HexaSubAttack first = HexaSubAttack.None, HexaSubAttack second = HexaSubAttack.None)
        {
            if (_isCancelled || _hasAutoOperation || _hasStartedSpray || _hasStartedHoming || _hasStartedSniper
                || _hasStartedComposite || _hasStartedSummon || _hasStartedSpiral) return;
            _projectileDefinition = projectileDefinition;
            _sniperDefinition = sniperDefinition;
            if (first == HexaSubAttack.None || second == HexaSubAttack.None)
                SelectCompositePair(random, out first, out second);
            _compositeFirst = first;
            _compositeSecond = second;
            _hasStartedComposite = true;
            _compositeStage = 0;
            _compositeElapsedTicks = 0;
            _compositeCompletionCount = 0;
            _delayTicks = HexaDefinition.INITIAL_DELAY_TICKS;
        }

        /// <summary>6마리 묶음 3회를 1초 간격으로 소환하는 단독 소환 모드를 처음부터 준비한다. 취소되거나 다른 공격을 시작한 개체는 무시한다.</summary>
        internal void BeginSummon()
        {
            if (_isCancelled || _hasAutoOperation || _hasStartedSpray || _hasStartedHoming || _hasStartedSniper
                || _hasStartedComposite || _hasStartedSummon || _hasStartedSpiral) return;
            _hasStartedSummon = true;
            _isSummoning = true;
            _summonBundlesSpawned = 0;
            _summonNextTick = -1;
        }

        /// <summary>원본 탄환 정의를 저장하고 첫 공격 전 대기부터 시계 방향으로 도는 4방향 나선 탄막 한 번을 준비한다. 취소되거나 이미 시작한 개체는 무시한다.</summary>
        internal void BeginSpiral(EnemyDefinition projectileDefinition)
        {
            if (_isCancelled || _hasAutoOperation || _hasStartedSpray || _hasStartedHoming || _hasStartedSniper
                || _hasStartedComposite || _hasStartedSummon || _hasStartedSpiral) return;
            _projectileDefinition = projectileDefinition;
            _hasStartedSpiral = true;
            _isSpiraling = true;
            _delayTicks = HexaDefinition.INITIAL_DELAY_TICKS;
        }

        /// <summary>
        /// 원본 탄환·저격 정의를 저장하고 첫 3초 대기 뒤 소환·나선·복합을 우선순위와 횟수 쿨타임으로 자동 선택하는 운영을 준비한다.
        /// 초기 카운터는 소환 2, 나선 0이며 복합은 항상 사용할 수 있고, 취소되거나 이미 시작한 개체는 무시한다.
        /// </summary>
        internal void BeginAutoOperation(EnemyDefinition projectileDefinition, EnemyDefinition sniperDefinition)
        {
            if (_isCancelled || _hasAutoOperation || _hasStartedSpray || _hasStartedHoming || _hasStartedSniper
                || _hasStartedComposite || _hasStartedSummon || _hasStartedSpiral) return;
            _projectileDefinition = projectileDefinition;
            _sniperDefinition = sniperDefinition;
            _hasAutoOperation = true;
            _autoPattern = HexaTopPattern.None;
            _summonCooldown = HexaDefinition.AUTO_INITIAL_SUMMON_COOLDOWN;
            _spiralCooldown = HexaDefinition.AUTO_INITIAL_SPIRAL_COOLDOWN;
            _autoCompletionCount = 0;
            _delayTicks = HexaDefinition.INITIAL_DELAY_TICKS;
        }

        /// <summary>소환 중이고 다음 묶음을 시도할 틱에 도달했는지 반환한다.</summary>
        internal bool IsSummonDue(int tick) => _isSummoning && !_isCancelled && tick >= _summonNextTick;

        /// <summary>
        /// 묶음 생성 결과를 반영한다. 성공하면 소환 묶음 수를 늘리고 세 묶음이 끝나면 소환을 종료하며,
        /// 다음 성공 묶음은 실제 성공 틱에서 1초 뒤에 시도한다. 실패한 묶음은 수에 넣지 않고 다음 틱에 다시 시도한다.
        /// </summary>
        internal void CompleteSummonBundle(int tick, bool isSuccess)
        {
            if (!_isSummoning) return;
            if (!isSuccess) { _summonNextTick = tick + 1; return; }
            _summonBundlesSpawned++;
            if (_summonBundlesSpawned >= HexaDefinition.SUMMON_BUNDLE_COUNT)
            {
                _isSummoning = false;
                if (_autoPattern == HexaTopPattern.Summon) CompleteAutoPattern();
                return;
            }
            _summonNextTick = tick + HexaDefinition.SUMMON_INTERVAL_TICKS;
        }

        /// <summary>SimRandom의 두 번의 추첨으로 난사·유도·Sniper 중 서로 다른 첫·두 번째 공격을 균등한 여섯 순서로 고른다.</summary>
        private static void SelectCompositePair(SimRandom random, out HexaSubAttack first, out HexaSubAttack second)
        {
            int firstIndex = (int)(random.Next() * HexaDefinition.COMPOSITE_SUB_ATTACK_COUNT);
            int secondIndex = (int)(random.Next() * (HexaDefinition.COMPOSITE_SUB_ATTACK_COUNT - 1));
            if (secondIndex >= firstIndex) secondIndex++;
            first = (HexaSubAttack)(firstIndex + 1);
            second = (HexaSubAttack)(secondIndex + 1);
        }

        /// <summary>
        /// 빙결이 아닌 틱마다 첫 대기와 선택된 공격을 진행한다. 자동 운영은 대기 후 우선순위로 다음 상위 패턴을 선택하고,
        /// 수동 모드의 복합은 첫 공격 시작 3초 뒤 두 번째 공격을 시작해 동시 진행할 수 있다.
        /// </summary>
        internal void Advance(SimulationEnemy owner, SimulationWorld world)
        {
            if (_isCancelled) return;
            if (_delayTicks > 0) { _delayTicks--; return; }
            if (_hasAutoOperation)
            {
                if (_autoPattern == HexaTopPattern.None) BeginAutoPattern(world);
                if (_autoPattern == HexaTopPattern.Summon) return;
                if (_autoPattern == HexaTopPattern.Spiral) { AdvanceSpiral(owner, world); return; }
                if (_autoPattern == HexaTopPattern.Composite) AdvanceComposite(owner, world);
                return;
            }
            if (_hasStartedSummon) return;
            if (_hasStartedComposite) { AdvanceComposite(owner, world); return; }
            if (_isSpraying) { AdvanceSpray(owner, world); return; }
            if (_isHoming) { AdvanceHoming(owner, world); return; }
            if (_isSpiraling) { AdvanceSpiral(owner, world); return; }
            if (_isSniperAttacking) AdvanceSniper(owner, world);
        }

        /// <summary>남은 카운터가 0인 패턴 중 소환→나선→복합 우선순위로 다음 상위 공격을 선택해 즉시 시작한다.</summary>
        private void BeginAutoPattern(SimulationWorld world)
        {
            if (_summonCooldown == 0) BeginAutoSummon();
            else if (_spiralCooldown == 0) BeginAutoSpiral();
            else BeginAutoComposite(world);
        }

        /// <summary>자동 운영에서 소환 6마리 묶음 3회를 처음부터 다시 시작하고 현재 상위 패턴을 소환으로 표시한다.</summary>
        private void BeginAutoSummon()
        {
            _autoPattern = HexaTopPattern.Summon;
            _hasStartedSummon = true;
            _isSummoning = true;
            _summonBundlesSpawned = 0;
            _summonNextTick = -1;
        }

        /// <summary>자동 운영에서 나선 탄막의 경과·묶음·발사 수를 초기화하고 현재 상위 패턴을 나선으로 표시한다.</summary>
        private void BeginAutoSpiral()
        {
            _autoPattern = HexaTopPattern.Spiral;
            _hasStartedSpiral = true;
            _isSpiraling = true;
            _spiralElapsedTicks = 0;
            _spiralBundleCount = 0;
            _spiralShotCount = 0;
        }

        /// <summary>자동 운영에서 SimRandom으로 서로 다른 두 하위 공격을 새로 골라 복합 경과를 초기화하고 첫 단계부터 시작한다.</summary>
        private void BeginAutoComposite(SimulationWorld world)
        {
            _autoPattern = HexaTopPattern.Composite;
            _hasStartedComposite = true;
            _compositeStage = 0;
            _compositeElapsedTicks = 0;
            _isSpraying = false;
            _isHoming = false;
            _isSniperAttacking = false;
            SelectCompositePair(world.Random, out HexaSubAttack first, out HexaSubAttack second);
            _compositeFirst = first;
            _compositeSecond = second;
        }

        /// <summary>
        /// 정상 완료된 상위 패턴의 카운터를 기본값으로 복원하고 다른 상위 패턴의 카운터를 1씩 감소시킨 뒤
        /// 완료 횟수를 한 번만 늘리고 다음 선택 전 3초 대기를 설정한다. 취소되었거나 진행 중인 패턴이 없으면 아무것도 바꾸지 않는다.
        /// </summary>
        private void CompleteAutoPattern()
        {
            if (!_hasAutoOperation || _isCancelled) return;
            switch (_autoPattern)
            {
                case HexaTopPattern.Summon:
                    _summonCooldown = HexaDefinition.AUTO_SUMMON_COOLDOWN;
                    _spiralCooldown = Math.Max(0, _spiralCooldown - 1);
                    break;
                case HexaTopPattern.Spiral:
                    _spiralCooldown = HexaDefinition.AUTO_SPIRAL_COOLDOWN;
                    _summonCooldown = Math.Max(0, _summonCooldown - 1);
                    break;
                case HexaTopPattern.Composite:
                    _summonCooldown = Math.Max(0, _summonCooldown - 1);
                    _spiralCooldown = Math.Max(0, _spiralCooldown - 1);
                    break;
                default:
                    return;
            }
            _autoCompletionCount++;
            _autoPattern = HexaTopPattern.None;
            _delayTicks = HexaDefinition.AUTO_INTERVAL_TICKS;
        }

        /// <summary>매 발사 시 현재 플레이어 방향에 독립 편차를 더한 탄환을 3틱 간격으로 100발 발사하고 300틱에 완료한다. 난사 탄환은 원본 속도의 2배로 발사한다.</summary>
        private void AdvanceSpray(SimulationEnemy owner, SimulationWorld world)
        {
            if (_sprayElapsedTicks >= HexaDefinition.SPRAY_DURATION_TICKS)
            {
                _isSpraying = false;
                return;
            }
            if (_sprayShotCount < HexaDefinition.SPRAY_SHOT_COUNT
                && _sprayElapsedTicks % HexaDefinition.SPRAY_INTERVAL_TICKS == 0)
            {
                SimVector direction = (world.Player.Position - owner.Position).Normalized();
                double spread = (world.Random.Next() * 2 - 1) * HexaDefinition.SPRAY_HALF_SPREAD_DEGREES
                    * RuneSimulation.DEGREES_TO_RADIANS;
                Shoot(owner, world, direction.Rotated(spread), false, HexaDefinition.SPRAY_SPEED_MULTIPLIER);
                _sprayShotCount++;
            }
            _sprayElapsedTicks++;
        }

        /// <summary>매 발사 시 현재 플레이어 방향으로 유도 탄환을 30틱 간격으로 10발 발사하며 마지막 발사와 함께 완료한다.</summary>
        private void AdvanceHoming(SimulationEnemy owner, SimulationWorld world)
        {
            if (_homingShotCount < HexaDefinition.HOMING_SHOT_COUNT
                && _homingElapsedTicks % HexaDefinition.HOMING_SHOT_INTERVAL_TICKS == 0)
            {
                SimVector direction = (world.Player.Position - owner.Position).Normalized();
                Shoot(owner, world, direction, true, HexaDefinition.HOMING_SPEED_MULTIPLIER);
                _homingShotCount++;
                if (_homingShotCount >= HexaDefinition.HOMING_SHOT_COUNT) { _isHoming = false; return; }
            }
            _homingElapsedTicks++;
        }

        /// <summary>
        /// 6틱마다 네 방향의 공통 각도를 묶음마다 6도씩 시계 방향으로 누적해 4발씩 발사하고, 360틱에 완료한다.
        /// 시작 방향은 화면 기준 상하좌우이며 발사된 탄환은 유도 없이 직진한다.
        /// </summary>
        private void AdvanceSpiral(SimulationEnemy owner, SimulationWorld world)
        {
            if (_spiralElapsedTicks >= HexaDefinition.SPIRAL_DURATION_TICKS)
            {
                _isSpiraling = false;
                if (_autoPattern == HexaTopPattern.Spiral) CompleteAutoPattern();
                return;
            }
            if (_spiralBundleCount < HexaDefinition.SPIRAL_BUNDLE_COUNT
                && _spiralElapsedTicks % HexaDefinition.SPIRAL_INTERVAL_TICKS == 0)
            {
                double rotation = _spiralBundleCount * HexaDefinition.SPIRAL_ROTATION_DEGREES_PER_BUNDLE
                    * RuneSimulation.DEGREES_TO_RADIANS;
                for (int stream = 0; stream < HexaDefinition.SPIRAL_STREAM_COUNT; stream++)
                {
                    double angle = rotation + stream * Math.PI / 2;
                    Shoot(owner, world, new SimVector(Math.Cos(angle), Math.Sin(angle)));
                }
                _spiralBundleCount++;
                _spiralShotCount += HexaDefinition.SPIRAL_STREAM_COUNT;
            }
            _spiralElapsedTicks++;
        }

        /// <summary>원본 저격 정의로 각 사격마다 1초 동안 플레이어 방향을 추적한 뒤 원점과 방향으로 탄환을 발사하며, 5발을 모두 쏘면 하위 공격을 완료한다.</summary>
        private void AdvanceSniper(SimulationEnemy owner, SimulationWorld world)
        {
            if (_sniperDefinition == null) { _isSniperAttacking = false; return; }
            if (_isSniperPaused) ResumeSniper(owner, world.Time);
            bool isBeginning = !owner.IsAiming;
            SimVector direction = (world.Player.Position - owner.Position).Normalized();
            bool isReady = EnemySystem.AdvanceSniperAim(owner, direction, world.Time,
                HexaDefinition.SNIPER_AIM_SECONDS, HexaDefinition.SNIPER_LOCK_SECONDS, out bool isLocked);
            if (isBeginning) { _isSniperLocked = false; return; }
            _sniperElapsedTicks++;
            _isSniperLocked = isLocked && !isReady;
            if (!isReady) return;
            world.EnemyProjectiles.Add(new SimulationProjectile(owner.AttackOrigin, owner.AimedDirection,
                _sniperDefinition.ProjectileSpeed, _sniperDefinition.Damage * owner.DamageMultiplier,
                _sniperDefinition.ProjectileRadius, _sniperDefinition.ProjectileLifetime, sourceEnemyId: owner.Id,
                style: HostileProjectileStyle.Sniper));
            owner.AimEndsAt = 0; owner.WarningUntil = 0;
            _sniperShotCount++;
            _isSniperLocked = false;
            if (_sniperShotCount >= HexaDefinition.SNIPER_SHOT_COUNT) _isSniperAttacking = false;
        }

        /// <summary>
        /// 복합 첫 하위 공격을 시작하고 경과 틱을 세며, 첫 공격 시작 180틱 뒤 두 번째 하위 공격을 시작한다.
        /// 진행 중인 하위 공격을 각각 한 틱씩 갱신하고 둘 다 끝난 틱에 완료 횟수를 한 번만 증가시킨다.
        /// 이후에는 같은 복합을 다시 시작하지 않는다.
        /// </summary>
        private void AdvanceComposite(SimulationEnemy owner, SimulationWorld world)
        {
            if (_compositeStage >= HexaDefinition.COMPOSITE_STAGE_COUNT && !HasActiveSubAttack()) return;
            if (_compositeStage == 0)
            {
                BeginCompositeAttack(owner, _compositeFirst);
                _compositeStage = 1;
            }
            if (_compositeStage == 1 && _compositeElapsedTicks >= HexaDefinition.COMPOSITE_SECOND_START_TICKS)
            {
                BeginCompositeAttack(owner, _compositeSecond);
                _compositeStage = HexaDefinition.COMPOSITE_STAGE_COUNT;
            }
            if (_isSpraying) AdvanceSpray(owner, world);
            if (_isHoming) AdvanceHoming(owner, world);
            if (_isSniperAttacking) AdvanceSniper(owner, world);
            _compositeElapsedTicks++;
            if (_compositeStage >= HexaDefinition.COMPOSITE_STAGE_COUNT && !HasActiveSubAttack())
            {
                _compositeCompletionCount++;
                if (_autoPattern == HexaTopPattern.Composite) CompleteAutoPattern();
            }
        }

        /// <summary>복합에서 지정한 하위 공격의 진행 틱·발사 수와 저격 일시정지 상태를 초기화하고 실행을 시작한다.</summary>
        private void BeginCompositeAttack(SimulationEnemy owner, HexaSubAttack attack)
        {
            switch (attack)
            {
                case HexaSubAttack.Spray:
                    _sprayElapsedTicks = 0; _sprayShotCount = 0; _isSpraying = true; break;
                case HexaSubAttack.Homing:
                    _homingElapsedTicks = 0; _homingShotCount = 0; _isHoming = true; break;
                case HexaSubAttack.Sniper:
                    _sniperElapsedTicks = 0; _sniperShotCount = 0; _isSniperPaused = false; _isSniperLocked = false;
                    _sniperRemainingAimSeconds = 0; _sniperRemainingLockSeconds = 0;
                    owner.AimEndsAt = 0; owner.WarningUntil = 0;
                    _isSniperAttacking = true; break;
            }
        }

        /// <summary>복합 하위 공격 중 하나라도 진행 중인지 반환한다.</summary>
        private bool HasActiveSubAttack() => _isSpraying || _isHoming || _isSniperAttacking;

        /// <summary>빙결 시작 시 저격 예고선을 숨기고 남은 조준·고정 시간을 보존한다. 저격 중이 아니면 아무것도 바꾸지 않는다.</summary>
        internal void PauseSniper(SimulationEnemy owner, double time)
        {
            if (!_isSniperAttacking || _isSniperPaused || !owner.IsAiming) return;
            _sniperRemainingAimSeconds = Math.Max(0, owner.AimEndsAt - time);
            _sniperRemainingLockSeconds = Math.Max(0, owner.WarningUntil - Math.Max(time, owner.AimEndsAt));
            owner.AimEndsAt = 0; owner.WarningUntil = 0;
            _isSniperLocked = false;
            _isSniperPaused = true;
        }

        /// <summary>빙결 해제 시 보존한 조준·고정 시간을 현재 시각 기준으로 복원해 남은 저격 진행을 잇는다.</summary>
        private void ResumeSniper(SimulationEnemy owner, double time)
        {
            owner.AimEndsAt = time + _sniperRemainingAimSeconds;
            owner.WarningUntil = owner.AimEndsAt + _sniperRemainingLockSeconds;
            _sniperRemainingAimSeconds = 0;
            _sniperRemainingLockSeconds = 0;
            _isSniperPaused = false;
        }

        /// <summary>소유자의 중앙에서 원본 적 탄환의 속도·피해·반경·수명과 소유자 피해 배율을 사용해 한 발 생성한다. speedMultiplier는 원본 속도에 곱하고, 유도 발사는 같은 탄환에 유도 시간과 초당 회전 상한 및 보라색 표시 스타일을 설정한다.</summary>
        private void Shoot(SimulationEnemy owner, SimulationWorld world, SimVector direction, bool isHoming = false, double speedMultiplier = 1)
        {
            world.EnemyProjectiles.Add(new SimulationProjectile(owner.Position, direction, _projectileDefinition.ProjectileSpeed * speedMultiplier,
                _projectileDefinition.Damage * owner.DamageMultiplier, _projectileDefinition.ProjectileRadius,
                _projectileDefinition.ProjectileLifetime, sourceEnemyId: owner.Id,
                homingTurnDegreesPerSecond: isHoming ? HexaDefinition.HOMING_TURN_DEGREES_PER_SECOND : 0,
                homingSeconds: isHoming ? HexaDefinition.HOMING_SECONDS : 0,
                style: isHoming ? HostileProjectileStyle.Homing : HostileProjectileStyle.Default));
        }

        /// <summary>사망 또는 전투 종료 시 이 개체의 보스 실행 상태와 진행 중인 공격·소환·저격 예고선을 취소하며, 복합의 남은 하위 공격과 완료 집계는 진행하지 않는다.</summary>
        internal void Cancel(SimulationEnemy owner)
        {
            _isCancelled = true; _isSpraying = false; _isHoming = false; _isSummoning = false; _isSpiraling = false;
            _isSniperAttacking = false; _isSniperPaused = false; _isSniperLocked = false;
            _sniperRemainingAimSeconds = 0; _sniperRemainingLockSeconds = 0;
            owner.AimEndsAt = 0; owner.WarningUntil = 0;
        }

        /// <summary>난사·유도·저격·복합·소환·나선 선택, 자동 운영의 현재 패턴·쿨타임·완료 횟수, 취소, 진행 틱·횟수, 복합 순서·경과·완료 횟수, 소환 묶음·다음 틱, 나선 묶음·발사 수, 보존한 저격 시간과 원본 탄환·저격 ID를 결정성 상태 버퍼에 추가한다.</summary>
        internal void WriteState(StringBuilder state)
        {
            state.Append("|hexa:").Append(_isCancelled).Append(':').Append(_hasStartedSpray).Append(':').Append(_isSpraying)
                .Append(':').Append(_delayTicks).Append(':').Append(_sprayElapsedTicks).Append(':').Append(_sprayShotCount)
                .Append(':').Append(_hasStartedHoming).Append(':').Append(_isHoming).Append(':').Append(_homingElapsedTicks)
                .Append(':').Append(_homingShotCount).Append(':').Append(_projectileDefinition?.Id)
                .Append(':').Append(_hasStartedSniper).Append(':').Append(_isSniperAttacking).Append(':').Append(_isSniperPaused)
                .Append(':').Append(_isSniperLocked).Append(':').Append(_sniperElapsedTicks).Append(':').Append(_sniperShotCount)
                .Append(':').Append(_sniperDefinition?.Id)
                .Append(':').Append(_hasStartedComposite).Append(':').Append(_compositeStage)
                .Append(':').Append(_compositeElapsedTicks)
                .Append(':').Append((int)_compositeFirst).Append(':').Append((int)_compositeSecond)
                .Append(':').Append(_compositeCompletionCount)
                .Append(':').Append(_hasStartedSummon).Append(':').Append(_isSummoning)
                .Append(':').Append(_summonBundlesSpawned).Append(':').Append(_summonNextTick)
                .Append(':').Append(_hasStartedSpiral).Append(':').Append(_isSpiraling)
                .Append(':').Append(_spiralElapsedTicks).Append(':').Append(_spiralBundleCount).Append(':').Append(_spiralShotCount)
                .Append(':').Append(_hasAutoOperation).Append(':').Append((int)_autoPattern)
                .Append(':').Append(_summonCooldown).Append(':').Append(_spiralCooldown).Append(':').Append(_autoCompletionCount)
                .Append(FormattableString.Invariant($":{_sniperRemainingAimSeconds:R}:{_sniperRemainingLockSeconds:R}"));
        }
    }
}
