using System.Text;

namespace RuneCode
{
    /// <summary>Hexa 한 개체의 난사 진행 틱·발사 횟수·취소 상태를 보관한다.</summary>
    public sealed class HexaBossState
    {
        private bool _isCancelled;
        private bool _hasStartedSpray;
        private bool _isSpraying;
        private int _delayTicks;
        private int _sprayElapsedTicks;
        private int _sprayShotCount;
        private EnemyDefinition _projectileDefinition;
        public bool IsCancelled => _isCancelled;
        public bool HasStartedSpray => _hasStartedSpray;
        public bool IsSpraying => _isSpraying;
        public int DelayTicks => _delayTicks;
        public int SprayElapsedTicks => _sprayElapsedTicks;
        public int SprayShotCount => _sprayShotCount;

        /// <summary>원본 탄환 정의를 저장하고 첫 공격 전 대기부터 단독 난사 한 번을 준비한다. 취소되거나 이미 시작한 개체는 무시한다.</summary>
        internal void BeginSpray(EnemyDefinition projectileDefinition)
        {
            if (_isCancelled || _hasStartedSpray) return;
            _projectileDefinition = projectileDefinition;
            _hasStartedSpray = true;
            _isSpraying = true;
            _delayTicks = HexaDefinition.INITIAL_DELAY_TICKS;
        }

        /// <summary>빙결이 아닌 틱마다 대기와 난사를 진행하고 현재 플레이어 방향에 편차를 적용해 기존 적 탄환을 생성한다.</summary>
        internal void Advance(SimulationEnemy owner, SimulationWorld world)
        {
            if (_isCancelled || !_isSpraying) return;
            if (_delayTicks > 0) { _delayTicks--; return; }
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

        /// <summary>소유자의 중앙에서 원본 적 탄환의 속도·피해·반경·수명과 소유자 피해 배율을 사용해 지정 방향으로 한 발 생성한다.</summary>
        private void Shoot(SimulationEnemy owner, SimulationWorld world, SimVector direction)
        {
            world.EnemyProjectiles.Add(new SimulationProjectile(owner.Position, direction, _projectileDefinition.ProjectileSpeed,
                _projectileDefinition.Damage * owner.DamageMultiplier, _projectileDefinition.ProjectileRadius,
                _projectileDefinition.ProjectileLifetime, sourceEnemyId: owner.Id));
        }

        /// <summary>사망 또는 전투 종료 시 이 개체의 보스 실행 상태를 취소한다.</summary>
        internal void Cancel() { _isCancelled = true; _isSpraying = false; }

        /// <summary>난사 선택·취소·진행 틱·횟수와 원본 탄환 ID를 결정성 상태 버퍼에 추가한다.</summary>
        internal void WriteState(StringBuilder state)
        {
            state.Append("|hexa:").Append(_isCancelled).Append(':').Append(_hasStartedSpray).Append(':').Append(_isSpraying)
                .Append(':').Append(_delayTicks).Append(':').Append(_sprayElapsedTicks).Append(':').Append(_sprayShotCount)
                .Append(':').Append(_projectileDefinition?.Id);
        }
    }
}
