using System.Text;

namespace RuneCode
{
    /// <summary>Hexa 한 개체의 단독 난사·유도 공격 진행 틱·발사 횟수·취소 상태를 보관한다.</summary>
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
        private EnemyDefinition _projectileDefinition;
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

        /// <summary>원본 탄환 정의를 저장하고 첫 공격 전 대기부터 단독 난사 한 번을 준비한다. 취소되거나 이미 시작한 개체는 무시한다.</summary>
        internal void BeginSpray(EnemyDefinition projectileDefinition)
        {
            if (_isCancelled || _hasStartedSpray || _hasStartedHoming) return;
            _projectileDefinition = projectileDefinition;
            _hasStartedSpray = true;
            _isSpraying = true;
            _delayTicks = HexaDefinition.INITIAL_DELAY_TICKS;
        }

        /// <summary>원본 탄환 정의를 저장하고 첫 공격 전 대기부터 단독 유도 5발을 준비한다. 취소되거나 이미 시작한 개체는 무시한다.</summary>
        internal void BeginHoming(EnemyDefinition projectileDefinition)
        {
            if (_isCancelled || _hasStartedSpray || _hasStartedHoming) return;
            _projectileDefinition = projectileDefinition;
            _hasStartedHoming = true;
            _isHoming = true;
            _delayTicks = HexaDefinition.INITIAL_DELAY_TICKS;
        }

        /// <summary>빙결이 아닌 틱마다 첫 대기와 선택된 단독 공격을 진행하며, 유도 공격은 현재 플레이어 방향으로 기존 적 탄환을 1초 간격으로 발사한다.</summary>
        internal void Advance(SimulationEnemy owner, SimulationWorld world)
        {
            if (_isCancelled) return;
            if (_delayTicks > 0) { _delayTicks--; return; }
            if (_isSpraying) { AdvanceSpray(owner, world); return; }
            if (_isHoming) AdvanceHoming(owner, world);
        }

        /// <summary>매 발사 시 현재 플레이어 방향에 독립 편차를 더한 탄환을 6틱 간격으로 30발 발사하고 180틱에 완료한다.</summary>
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
                Shoot(owner, world, direction.Rotated(spread));
                _sprayShotCount++;
            }
            _sprayElapsedTicks++;
        }

        /// <summary>매 발사 시 현재 플레이어 방향으로 유도 탄환을 60틱 간격으로 5발 발사하며 5번째 발사와 함께 완료한다.</summary>
        private void AdvanceHoming(SimulationEnemy owner, SimulationWorld world)
        {
            if (_homingShotCount < HexaDefinition.HOMING_SHOT_COUNT
                && _homingElapsedTicks % HexaDefinition.HOMING_SHOT_INTERVAL_TICKS == 0)
            {
                SimVector direction = (world.Player.Position - owner.Position).Normalized();
                Shoot(owner, world, direction, true);
                _homingShotCount++;
                if (_homingShotCount >= HexaDefinition.HOMING_SHOT_COUNT) { _isHoming = false; return; }
            }
            _homingElapsedTicks++;
        }

        /// <summary>소유자의 중앙에서 원본 적 탄환의 속도·피해·반경·수명과 소유자 피해 배율을 사용해 한 발 생성한다. 유도 발사는 같은 탄환에 유도 시간과 초당 회전 상한을 설정한다.</summary>
        private void Shoot(SimulationEnemy owner, SimulationWorld world, SimVector direction, bool isHoming = false)
        {
            world.EnemyProjectiles.Add(new SimulationProjectile(owner.Position, direction, _projectileDefinition.ProjectileSpeed,
                _projectileDefinition.Damage * owner.DamageMultiplier, _projectileDefinition.ProjectileRadius,
                _projectileDefinition.ProjectileLifetime, sourceEnemyId: owner.Id,
                homingTurnDegreesPerSecond: isHoming ? HexaDefinition.HOMING_TURN_DEGREES_PER_SECOND : 0,
                homingSeconds: isHoming ? HexaDefinition.HOMING_SECONDS : 0));
        }

        /// <summary>사망 또는 전투 종료 시 이 개체의 보스 실행 상태와 진행 중인 공격을 취소한다.</summary>
        internal void Cancel() { _isCancelled = true; _isSpraying = false; _isHoming = false; }

        /// <summary>난사·유도 선택, 취소, 진행 틱·횟수와 원본 탄환 ID를 결정성 상태 버퍼에 추가한다.</summary>
        internal void WriteState(StringBuilder state)
        {
            state.Append("|hexa:").Append(_isCancelled).Append(':').Append(_hasStartedSpray).Append(':').Append(_isSpraying)
                .Append(':').Append(_delayTicks).Append(':').Append(_sprayElapsedTicks).Append(':').Append(_sprayShotCount)
                .Append(':').Append(_hasStartedHoming).Append(':').Append(_isHoming).Append(':').Append(_homingElapsedTicks)
                .Append(':').Append(_homingShotCount).Append(':').Append(_projectileDefinition?.Id);
        }
    }
}
