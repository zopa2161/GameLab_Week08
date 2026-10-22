using System;
using System.Collections.Generic;
using System.Text;

namespace RuneCode
{
    public enum LegolasAttack { Rest, Snipe, Jump, Area }

    /// <summary>레골라스 한 개체의 저격·후방 도약·고정 목표 세 발과 단발 범위 피해를 진행한다.</summary>
    public sealed class LegolasBossState
    {
        public const int VOLLEY_COUNT = 3;
        private const int TARGET_ATTEMPTS = 24;
        private const int TARGET_STRICT_ATTEMPTS = 16;
        private const int ESCAPE_JUMP_COUNT = 2;
        private const double IMPACT_VISUAL_SECONDS = 0.2;

        private static readonly double[] _escapeAngles = { 45, 60, 75, 90, 105, 120, 135 };

        private readonly LegolasDefinition _settings;
        private readonly SimVector[] _targets = new SimVector[VOLLEY_COUNT];
        private LegolasAttack _attack;
        private bool _isNextJump;
        private double _elapsed;
        private double _restSeconds;
        private SimVector _jumpOrigin;
        private SimVector _jumpTarget;
        private SimVector _volleyOrigin;
        private double _volleyLaunchJumpProgress;
        private bool _hasLaunched;
        private bool _hasImpacted;
        private int _escapeJumpsRemaining;
        private int _escapeSide;

        public LegolasDefinition Settings => _settings;
        public LegolasAttack Attack => _attack;
        public IReadOnlyList<SimVector> Targets => _targets;
        public SimVector VolleyOrigin => _volleyOrigin;
        public double VolleyLaunchJumpProgress => _volleyLaunchJumpProgress;
        public bool IsAirborne => _attack == LegolasAttack.Jump && _elapsed >= _settings.JumpWindup;
        public double JumpProgress => IsAirborne ? Math.Min(1, (_elapsed - _settings.JumpWindup) / _settings.JumpSeconds) : 0;
        public bool HasAreaTargets => _attack == LegolasAttack.Jump || _attack == LegolasAttack.Area;
        public bool IsVolleyFlying => HasAreaTargets && _hasLaunched && !_hasImpacted;
        public bool HasImpacted => _hasImpacted;
        public double FlightProgress => Math.Max(0, Math.Min(1, (_elapsed - _settings.AreaWarning) / _settings.AreaFlight));

        /// <summary>공유 설정과 초기 대기 시간으로 개체별 공격 상태를 초기화한다.</summary>
        internal LegolasBossState(LegolasDefinition settings)
        {
            _settings = settings;
            _restSeconds = settings.InitialDelay;
        }

        /// <summary>고정 스텝으로 패턴을 진행하고 적·맵·난수와 공유 피격 처리를 사용해 이동 및 피해를 적용한다.</summary>
        internal void Advance(SimulationEnemy enemy, SimulationWorld world, RuneSimulation simulation, Action<double> hurt)
        {
            _elapsed += RuneSimulation.STEP_SECONDS;
            SimVector offset = world.Player.Position - enemy.Position;
            SimVector direction = offset.LengthSquared > 0.000001 ? offset.Normalized() : enemy.Facing;
            if (_attack == LegolasAttack.Rest)
            {
                enemy.Move(world.Map.Move(enemy.Position, direction * (world.Stages.StationaryAdvanceSpeed * RuneSimulation.STEP_SECONDS), enemy.Radius), direction);
                if (_elapsed < _restSeconds) return;
                _elapsed = 0;
                _attack = _isNextJump ? LegolasAttack.Jump : LegolasAttack.Snipe;
                _isNextJump = !_isNextJump;
                if (_attack == LegolasAttack.Jump)
                {
                    bool isNearWall = !world.Map.CanOccupy(enemy.Position, enemy.Radius + _settings.WallEscapeMargin);
                    _escapeJumpsRemaining = isNearWall ? ESCAPE_JUMP_COUNT : 0;
                    _escapeSide = isNearWall ? enemy.OrbitSign : 0;
                    BeginJump(enemy, world, simulation, direction);
                }
            }
            if (_attack == LegolasAttack.Snipe) AdvanceSnipe(enemy, world, direction);
            else if (_attack == LegolasAttack.Jump || _attack == LegolasAttack.Area)
            {
                if (_attack == LegolasAttack.Jump) AdvanceJump(enemy, world, simulation, direction);
                if (!_hasLaunched && _elapsed >= _settings.AreaWarning)
                {
                    _hasLaunched = true;
                    _volleyOrigin = enemy.Position;
                    _volleyLaunchJumpProgress = JumpProgress;
                }
                double landingAt = _settings.JumpWindup + _settings.JumpSeconds;
                if (_elapsed >= landingAt) _attack = LegolasAttack.Area;
                if (_hasLaunched && !_hasImpacted && _elapsed >= _settings.AreaWarning + _settings.AreaFlight)
                {
                    _hasImpacted = true;
                    foreach (SimVector target in _targets)
                        if (SimVector.Distance(world.Player.Position, target) <= _settings.AreaRadius + world.Sector.PlayerRadius)
                        { hurt(_settings.AreaDamage * enemy.DamageMultiplier); break; }
                }
                if (_elapsed >= Math.Max(landingAt, _settings.AreaWarning + _settings.AreaFlight + IMPACT_VISUAL_SECONDS))
                {
                    if (_escapeJumpsRemaining > 0) BeginJump(enemy, world, simulation, direction);
                    else Rest();
                }
            }
        }

        /// <summary>도약 목표와 플레이어 주변 공격 예고를 고정하고 공중 발사 상태를 초기화하며 남은 연속 도약 수를 줄인다.</summary>
        private void BeginJump(SimulationEnemy enemy, SimulationWorld world, RuneSimulation simulation, SimVector direction)
        {
            _jumpOrigin = enemy.Position;
            if (_escapeJumpsRemaining > 0)
            {
                _jumpTarget = FindEscapeTarget(enemy, world, simulation, direction);
                _escapeJumpsRemaining--;
            }
            else
                _jumpTarget = ClipJumpPath(enemy, world, simulation, _jumpOrigin, _jumpOrigin - direction * _settings.JumpDistance, false);
            _attack = LegolasAttack.Jump;
            _elapsed = 0;
            ScatterTargets(world);
            _volleyOrigin = enemy.Position;
            _volleyLaunchJumpProgress = 0;
            _hasLaunched = false;
            _hasImpacted = false;
        }

        /// <summary>플레이어 좌우의 사선·측면 경로 중 벽과 몸체를 피하면서 맵 중앙에 가장 가까워지는 착지점을 반환한다.</summary>
        private SimVector FindEscapeTarget(SimulationEnemy enemy, SimulationWorld world, RuneSimulation simulation, SimVector direction)
        {
            SimVector center = simulation.MapCenter;
            SimVector best = enemy.Position;
            double bestDistance = (best - center).LengthSquared;
            int preferredSide = _escapeSide;
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                int side = sideIndex == 0 ? preferredSide : -preferredSide;
                foreach (double angle in _escapeAngles)
                {
                    SimVector candidateDirection = direction.Rotated(side * angle * RuneSimulation.DEGREES_TO_RADIANS);
                    SimVector target = ClipJumpPath(enemy, world, simulation, enemy.Position,
                        enemy.Position + candidateDirection * _settings.EscapeJumpDistance, true);
                    double distance = (target - center).LengthSquared;
                    if (distance + RuneSimulation.COLLISION_EPSILON >= bestDistance) continue;
                    bestDistance = distance;
                    best = target;
                    _escapeSide = side;
                }
            }
            return best;
        }

        /// <summary>도약 선분을 벽 앞에서 제한하고 필요하면 현재 플레이어 몸체와 여유 거리에 닿기 전에 멈춘다.</summary>
        private SimVector ClipJumpPath(SimulationEnemy enemy, SimulationWorld world, RuneSimulation simulation,
            SimVector start, SimVector target, bool avoidsPlayer)
        {
            SimVector displacement = target - start;
            double fraction = simulation.FirstWallFraction(start, target, enemy.Radius);
            if (avoidsPlayer)
            {
                double clearance = enemy.Radius + world.Sector.PlayerRadius + _settings.PlayerClearance;
                SimVector awayFromPlayer = start - world.Player.Position;
                bool isLeavingOverlap = awayFromPlayer.LengthSquared <= clearance * clearance
                    && SimVector.Dot(awayFromPlayer, displacement) >= 0;
                if (!isLeavingOverlap)
                    fraction = Math.Min(fraction, SpellGeometry.CircleContactFraction(start, target, world.Player.Position, clearance));
            }
            if (!double.IsPositiveInfinity(fraction)) displacement = displacement * Math.Max(0, fraction - RuneSimulation.COLLISION_EPSILON);
            return world.Map.Move(start, displacement, enemy.Radius);
        }

        /// <summary>비행 중 고정 속도로 목표를 향해 이동하며 탈출 도약은 매 틱 현재 플레이어 위치와의 충돌을 재확인한다.</summary>
        private void AdvanceJump(SimulationEnemy enemy, SimulationWorld world, RuneSimulation simulation, SimVector direction)
        {
            double flightStep = Math.Min(RuneSimulation.STEP_SECONDS, Math.Max(0, _elapsed - _settings.JumpWindup));
            SimVector remaining = _jumpTarget - enemy.Position;
            double distance = Math.Min(remaining.Length, (_jumpTarget - _jumpOrigin).Length * flightStep / _settings.JumpSeconds);
            SimVector next = enemy.Position + remaining.Normalized() * distance;
            if (_escapeSide != 0) next = ClipJumpPath(enemy, world, simulation, enemy.Position, next, true);
            enemy.Move(next, direction);
        }

        /// <summary>기존 저격 필드로 추적 조준·고정 예고를 표시하고 생존 소유자가 있는 단발 탄환을 발사한다.</summary>
        private void AdvanceSnipe(SimulationEnemy enemy, SimulationWorld world, SimVector direction)
        {
            EnemyDefinition definition = enemy.Definition;
            if (_elapsed <= definition.AimSeconds)
            {
                enemy.ShotOrigin = enemy.Position;
                enemy.AttackDirection = direction;
                enemy.Move(enemy.Position, direction);
            }
            enemy.AimEndsAt = world.Time + Math.Max(0, definition.AimSeconds - _elapsed);
            enemy.WarningUntil = world.Time + Math.Max(0, definition.AimSeconds + definition.LockSeconds - _elapsed);
            if (_elapsed < definition.AimSeconds + definition.LockSeconds) return;
            world.EnemyProjectiles.Add(new SimulationProjectile(enemy.AttackOrigin, enemy.AimedDirection, definition.ProjectileSpeed,
                enemy.Damage, definition.ProjectileRadius, definition.ProjectileLifetime, owner: enemy));
            enemy.AimEndsAt = 0;
            enemy.WarningUntil = 0;
            Rest();
        }

        /// <summary>플레이어 주변 바닥에 무작위 목표 3개를 정하고 제한된 재시도 뒤 좁은 지형의 최소 간격을 완화한다.</summary>
        private void ScatterTargets(SimulationWorld world)
        {
            SimVector center = world.Player.Position;
            for (int index = 0; index < VOLLEY_COUNT; index++)
            {
                SimVector candidate = center;
                for (int attempt = 0; attempt < TARGET_ATTEMPTS; attempt++)
                {
                    double angle = world.Random.Next() * Math.PI * 2;
                    double distance = Math.Sqrt(world.Random.Next()) * _settings.ScatterRadius;
                    candidate = world.Map.NearestFree(center + new SimVector(Math.Cos(angle), Math.Sin(angle)) * distance, world.Sector.PlayerRadius);
                    double relaxation = attempt < TARGET_STRICT_ATTEMPTS ? 1
                        : (TARGET_ATTEMPTS - attempt) / (double)(TARGET_ATTEMPTS - TARGET_STRICT_ATTEMPTS);
                    double minimum = _settings.MinimumTargetDistance * relaxation;
                    bool isValid = SimVector.Distance(candidate, center) <= _settings.ScatterRadius;
                    for (int previous = 0; previous < index; previous++)
                        if (SimVector.Distance(candidate, _targets[previous]) < minimum) isValid = false;
                    if (isValid) break;
                    candidate = center;
                }
                _targets[index] = candidate;
            }
        }

        /// <summary>현재 공격을 끝내고 다음 패턴 전 대기 시간을 적용한다.</summary>
        private void Rest()
        {
            _attack = LegolasAttack.Rest;
            _elapsed = 0;
            _restSeconds = _settings.AttackCooldown;
            _hasImpacted = false;
            _hasLaunched = false;
            _volleyLaunchJumpProgress = 0;
            _escapeJumpsRemaining = 0;
            _escapeSide = 0;
        }

        /// <summary>전투 종료 시 도약·투사체·범위 예고를 숨기고 공격 진행 상태를 정리한다.</summary>
        internal void Cancel() => Rest();

        /// <summary>패턴·시간·도약·고정 목표와 단발 피해 여부를 결정성 해시에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        {
            state.Append(FormattableString.Invariant($"|legolas:{_attack}:{_elapsed:R}:{_restSeconds:R}:{_isNextJump}:{_hasImpacted}:{_jumpOrigin.X:R}:{_jumpOrigin.Y:R}:{_jumpTarget.X:R}:{_jumpTarget.Y:R}:{_volleyOrigin.X:R}:{_volleyOrigin.Y:R}"));
            state.Append(FormattableString.Invariant($"|escape:{_escapeJumpsRemaining}:{_escapeSide}"));
            state.Append(FormattableString.Invariant($"|airshot:{_hasLaunched}:{_volleyLaunchJumpProgress:R}"));
            foreach (SimVector target in _targets) state.Append(FormattableString.Invariant($"|target:{target.X:R}:{target.Y:R}"));
        }
    }
}
