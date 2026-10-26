using System;
using System.Collections.Generic;
using System.Text;

namespace RuneCode
{
    public enum HydraAttack { Idle, Stretch, Poison, Stomp, Spin }

    /// <summary>반사 독 구체의 위치·방향·수명과 명중 후 독 장판의 다음 피해 시각을 관리한다.</summary>
    public sealed class HydraPoison
    {
        private const int MAX_BOUNCES_PER_TICK = 8;
        private const double WALL_SEPARATION = 0.001;

        private readonly double _startedAt;
        private SimVector _position;
        private SimVector _direction;
        private double _age;
        private bool _hasLanded;
        private double _landedAt;
        private double _nextDamageAt;
        public SimVector Position => _position;
        public bool HasLanded => _hasLanded;

        /// <summary>발사 위치·직선 방향과 발사 시각으로 반사 구체 상태를 초기화한다.</summary>
        internal HydraPoison(SimVector origin, SimVector direction, double time)
        { _position = origin; _direction = direction.Normalized(); _startedAt = time; }

        /// <summary>구체 이동·반사·명중 또는 장판의 주기 피해를 진행하고 해당 상태의 수명이 끝났는지 반환한다.</summary>
        internal bool Advance(SimulationWorld world, HydraDefinition settings, double damageMultiplier, RuneSimulation simulation, Action<double, string> hurt)
        {
            if (!_hasLanded)
            {
                double previousAge = _age;
                _age = Math.Max(0, world.Time - _startedAt);
                double moveSeconds = Math.Max(0, Math.Min(_age, settings.PoisonOrbLifetime) - previousAge);
                AdvanceOrb(world, settings, damageMultiplier, simulation, hurt, moveSeconds);
                return !_hasLanded && _age + RuneSimulation.COLLISION_EPSILON >= settings.PoisonOrbLifetime;
            }
            if (world.Time + RuneSimulation.COLLISION_EPSILON >= _landedAt + settings.PoisonLifetime) return true;
            if (world.Time + RuneSimulation.COLLISION_EPSILON >= _nextDamageAt)
            {
                _nextDamageAt += settings.PoisonTickInterval;
                if (SimVector.Distance(_position, world.Player.Position) <= settings.PoisonRadius + world.Sector.PlayerRadius)
                    hurt(settings.PoisonTickDamage * damageMultiplier, "poison");
            }
            return false;
        }

        /// <summary>남은 이동 시간을 벽 반사마다 나누어 연속 충돌을 검사하고 무적이 아닌 플레이어와 접촉하면 독 장판으로 전환한다.</summary>
        private void AdvanceOrb(SimulationWorld world, HydraDefinition settings, double damageMultiplier, RuneSimulation simulation, Action<double, string> hurt, double moveSeconds)
        {
            double remaining = moveSeconds;
            for (int bounce = 0; bounce < MAX_BOUNCES_PER_TICK && remaining > RuneSimulation.COLLISION_EPSILON; bounce++)
            {
                SimVector displacement = _direction * (settings.PoisonOrbSpeed * remaining);
                SimVector end = _position + displacement;
                double wall = simulation.FirstWallFraction(_position, end, settings.PoisonOrbRadius);
                bool canHitPlayer = world.IsMission && !world.DebugInvulnerable && !world.Player.IsInvulnerable(world.Time);
                double hit = canHitPlayer ? SpellGeometry.CircleContactFraction(_position, end, world.Player.Position,
                    settings.PoisonOrbRadius + world.Sector.PlayerRadius) : double.PositiveInfinity;
                if (!double.IsPositiveInfinity(hit) && hit <= wall)
                {
                    _position += displacement * hit;
                    hurt(settings.PoisonDamage * damageMultiplier, "poison");
                    _hasLanded = true;
                    _landedAt = world.Time;
                    _nextDamageAt = world.Time + settings.PoisonTickInterval;
                    return;
                }
                if (double.IsPositiveInfinity(wall)) { _position = end; return; }
                SimVector contact = _position + displacement * wall;
                SimVector normal = world.Map.GetWallContactNormal(contact, settings.PoisonOrbRadius, _direction);
                _position += displacement * Math.Max(0, wall - WALL_SEPARATION / displacement.Length);
                SimVector reflected = (_direction - normal * (2 * SimVector.Dot(_direction, normal))).Normalized();
                _direction = GetBounceDirection(world, settings, normal, reflected);
                remaining *= Math.Max(0, 1 - wall);
            }
        }

        /// <summary>반사 위치에서 플레이어를 향한 방향에 확률적 좌우 각도 오차를 더하고 벽 밖으로 나가는 유효 방향을 반환한다.</summary>
        private SimVector GetBounceDirection(SimulationWorld world, HydraDefinition settings, SimVector normal, SimVector reflected)
        {
            SimVector offset = world.Player.Position - _position;
            if (offset.LengthSquared <= RuneSimulation.COLLISION_EPSILON) return reflected;
            SimVector direction = offset.Normalized();
            if (world.Random.Next() < settings.PoisonBounceMissChance)
            {
                double degrees = settings.PoisonBounceMinOffsetDegrees
                    + world.Random.Next() * (settings.PoisonBounceMaxOffsetDegrees - settings.PoisonBounceMinOffsetDegrees);
                double sign = world.Random.Next() < 0.5 ? -1 : 1;
                direction = direction.Rotated(sign * degrees * RuneSimulation.DEGREES_TO_RADIANS);
            }
            double projection = SimVector.Dot(direction, normal);
            if (Math.Abs(projection) <= RuneSimulation.COLLISION_EPSILON) return reflected;
            if (projection < 0) direction -= normal * (2 * projection);
            direction = direction.Normalized();
            if (!world.Map.CanOccupy(_position + direction * (WALL_SEPARATION * 2), settings.PoisonOrbRadius)) return reflected;
            return direction;
        }

        /// <summary>구체 위치·방향·수명과 장판 전환·주기 피해 상태를 결정성 버퍼에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        { state.Append(FormattableString.Invariant($"|poison:{_position.X:R}:{_position.Y:R}:{_direction.X:R}:{_direction.Y:R}:{_startedAt:R}:{_age:R}:{_hasLanded}:{_landedAt:R}:{_nextDamageAt:R}")); }
    }

    /// <summary>착지 위치에 고정된 확장 고리와 한 번의 통과 피해 상태다.</summary>
    public sealed class HydraWave
    {
        private readonly SimVector _center;
        private readonly double _startedAt;
        private double _age;
        private bool _hasHit;
        public SimVector Center => _center;
        public double Age => _age;

        /// <summary>착지 위치와 발생 시각으로 고리 파동을 생성한다.</summary>
        internal HydraWave(SimVector center, double time) { _center = center; _startedAt = time; }

        /// <summary>현재 수명 비율로 몸체 반경에서 최대 범위까지 확장한 반경을 반환한다.</summary>
        public double GetRadius(HydraDefinition settings, double bodyRadius)
            => bodyRadius + (settings.WaveRadius - bodyRadius) * Math.Min(1, _age / settings.WaveSeconds);

        /// <summary>이전·현재 고리 사이의 통과를 판정해 한 번 피해를 주고 수명 종료 여부를 반환한다.</summary>
        internal bool Advance(SimulationWorld world, HydraDefinition settings, double bodyRadius, double damageMultiplier, Action<double, string> hurt)
        {
            double previousRadius = GetRadius(settings, bodyRadius);
            _age = world.Time - _startedAt;
            double currentRadius = GetRadius(settings, bodyRadius);
            double distance = SimVector.Distance(_center, world.Player.Position);
            double thickness = world.Sector.PlayerRadius + settings.WaveWidth * 0.5;
            if (!_hasHit && distance + thickness >= previousRadius && distance - thickness <= currentRadius)
            { _hasHit = true; hurt(settings.WaveDamage * damageMultiplier, "stomp"); }
            return _age + RuneSimulation.COLLISION_EPSILON >= settings.WaveSeconds;
        }

        /// <summary>고리 위치·수명과 피해 적용 여부를 결정성 버퍼에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        { state.Append(FormattableString.Invariant($"|wave:{_center.X:R}:{_center.Y:R}:{_startedAt:R}:{_age:R}:{_hasHit}")); }
    }

    /// <summary>Hydra 한 개체의 공격·잔류물을 소유하며 같은 기하를 판정과 표시에 제공한다.</summary>
    public sealed class HydraBossState
    {
        public const int HEAD_COUNT = 9;
        public const int STRETCH_HEAD_COUNT = 6;
        public const int SPIN_HEAD_COUNT = 5;
        private const int REPEAT_COUNT = 3;
        private const int NECK_SWEEP_SAMPLES = 4;
        private const double REST_NECK_LENGTH = 34;
        private const double FULL_TURN = Math.PI * 2;

        private readonly HydraDefinition _settings;
        private readonly double[] _stretchLengths = new double[HEAD_COUNT];
        private readonly List<HydraPoison> _poisons = new List<HydraPoison>();
        private readonly List<HydraWave> _waves = new List<HydraWave>();
        private HydraAttack _attack;
        private HydraAttack _lastAttack;
        private double _startedAt;
        private double _elapsed;
        private double _cooldownUntil;
        private double _angle;
        private int _repeat;
        private SimVector _poisonTarget;
        private SimVector _poisonOrigin;
        private SimVector _jumpOrigin;
        private SimVector _jumpTarget;
        private bool _hasJumpStarted;
        private SimVector _spinOrigin;
        private SimVector _spinTarget;
        private double _spinDashDuration;
        private bool _hasSpinHit;

        public HydraAttack Attack => _attack;
        public double Elapsed => _elapsed;
        public HydraDefinition Settings => _settings;
        public SimVector PoisonTarget => _poisonTarget;
        public SimVector PoisonOrigin => _poisonOrigin;
        public double PoisonChargeProgress => Math.Max(0, Math.Min(1, _elapsed / (_settings.PoisonTrack + _settings.PoisonLock)));
        public IReadOnlyList<HydraPoison> Poisons => _poisons;
        public IReadOnlyList<HydraWave> Waves => _waves;
        public SimVector SpinOrigin => _spinOrigin;
        public SimVector SpinTarget => _spinTarget;
        public SimVector SpinDirection => new SimVector(Math.Cos(_angle), Math.Sin(_angle));
        public double SpinDashDuration => _spinDashDuration;
        public bool IsNeckWarning => _attack == HydraAttack.Stretch && _elapsed < _settings.StretchWarning
            || _attack == HydraAttack.Spin && _elapsed < _settings.SpinWarning;
        public bool IsNeckActive => _attack == HydraAttack.Stretch && _elapsed >= _settings.StretchWarning
            || _attack == HydraAttack.Spin && _elapsed >= _settings.SpinWarning && _elapsed < _settings.SpinWarning + _spinDashDuration;
        public bool IsPoisonWarning => _attack == HydraAttack.Poison;
        public bool IsStompWarning => _attack == HydraAttack.Stomp && StompLocalTime < _settings.StompWindup + _settings.StompApproachSeconds;
        public bool IsAirborne => _attack == HydraAttack.Stomp && _hasJumpStarted
            && StompLocalTime >= _settings.StompWindup && StompLocalTime < _settings.StompWindup + _settings.StompApproachSeconds;
        public double JumpProgress => IsAirborne ? Math.Max(0, Math.Min(1, (StompLocalTime - _settings.StompWindup) / _settings.StompApproachSeconds)) : 0;
        public SimVector JumpTarget => _jumpTarget;
        public double StompLocalTime => _elapsed % _settings.StompInterval;

        /// <summary>공유 설정과 생성 시각으로 개별 공격 대기를 초기화한다.</summary>
        internal HydraBossState(HydraDefinition settings, double time)
        {
            _settings = settings;
            _cooldownUntil = time + settings.InitialDelay;
        }

        /// <summary>잔류물과 현재 공격을 한 틱 진행하며 쿨타임 추적·도약 이동과 목·파동·독 피해를 처리한다.</summary>
        internal void Advance(SimulationEnemy enemy, SimulationWorld world, RuneSimulation simulation, Action<double, string> hurt)
        {
            for (int index = _poisons.Count - 1; index >= 0; index--)
                if (_poisons[index].Advance(world, _settings, enemy.DamageMultiplier, simulation, hurt)) _poisons.RemoveAt(index);
            for (int index = _waves.Count - 1; index >= 0; index--)
                if (_waves[index].Advance(world, _settings, enemy.Radius, enemy.DamageMultiplier, hurt)) _waves.RemoveAt(index);
            if (_attack == HydraAttack.Idle)
            {
                MoveTowardsPlayer(enemy, world, enemy.Definition.Speed);
                if (world.Time + RuneSimulation.COLLISION_EPSILON < _cooldownUntil) return;
                BeginAttack(enemy, world, simulation);
            }
            _elapsed = world.Time - _startedAt;
            switch (_attack)
            {
                case HydraAttack.Stretch:
                    double stretchAngle = _angle + GetStretchRotation(_elapsed);
                    enemy.Move(enemy.Position, new SimVector(Math.Cos(stretchAngle), Math.Sin(stretchAngle)));
                    if (_elapsed >= _settings.StretchWarning + _settings.StretchSeconds)
                        RefreshStretchLengths(enemy, world, simulation);
                    if (IsNeckActive) HitNecks(enemy, world, simulation, hurt);
                    if (_elapsed >= _settings.StretchWarning + _settings.StretchSeconds + _settings.StretchHold + _settings.StretchRetractSeconds) FinishAttack(world.Time);
                    break;
                case HydraAttack.Poison: AdvancePoison(enemy, world, simulation); break;
                case HydraAttack.Stomp: AdvanceStomp(enemy, world, simulation); break;
                case HydraAttack.Spin:
                    AdvanceSpin(enemy, world, hurt);
                    break;
            }
        }

        /// <summary>직전 패턴을 제외한 동일 확률 추첨으로 공격을 시작하고 예고 방향·목 끝점을 고정한다.</summary>
        private void BeginAttack(SimulationEnemy enemy, SimulationWorld world, RuneSimulation simulation)
        {
            int selected = 1 + (int)(world.Random.Next() * (_lastAttack == HydraAttack.Idle ? 4 : 3));
            if (_lastAttack != HydraAttack.Idle && selected >= (int)_lastAttack) selected++;
            _attack = (HydraAttack)selected;
            _lastAttack = _attack;
            simulation.RecordBossPatternUse(enemy, _attack.ToString().ToLowerInvariant());
            _startedAt = world.Time;
            _elapsed = 0;
            _repeat = 0;
            _hasJumpStarted = false;
            _poisonTarget = world.Player.Position;
            SimVector offset = world.Player.Position - enemy.Position;
            _angle = Math.Atan2(offset.Y, offset.X);
            if (_attack == HydraAttack.Spin)
            {
                _spinOrigin = enemy.Position;
                _hasSpinHit = false;
                SimVector direction = new SimVector(Math.Cos(_angle), Math.Sin(_angle));
                double distance = enemy.Radius * 2 * _settings.SpinDashBodyLengths;
                SimVector displacement = direction * distance;
                double wall = simulation.FirstWallFraction(_spinOrigin, _spinOrigin + displacement, enemy.Radius);
                double fraction = double.IsPositiveInfinity(wall) ? 1 : Math.Max(0, wall - RuneSimulation.COLLISION_EPSILON);
                _spinTarget = _spinOrigin + displacement * fraction;
                _spinDashDuration = _settings.SpinDashSeconds * fraction;
                enemy.Move(enemy.Position, direction);
            }
            if (_attack != HydraAttack.Stretch) return;
            RefreshStretchLengths(enemy, world, simulation);
        }

        /// <summary>현재 목 뻗기 회전각에서 각 목이 벽까지 뻗을 수 있는 길이를 갱신해 판정과 표시에 공유한다.</summary>
        private void RefreshStretchLengths(SimulationEnemy enemy, SimulationWorld world, RuneSimulation simulation)
        {
            double reach = world.Map.Width + world.Map.Height;
            for (int index = 0; index < STRETCH_HEAD_COUNT; index++)
            {
                double angle = _angle + GetStretchRotation(_elapsed) + index * FULL_TURN / STRETCH_HEAD_COUNT;
                SimVector end = enemy.Position + new SimVector(Math.Cos(angle), Math.Sin(angle)) * reach;
                double fraction = simulation.FirstWallFraction(enemy.Position, end, _settings.NeckWidth * 0.5);
                _stretchLengths[index] = double.IsPositiveInfinity(fraction) ? reach : Math.Max(enemy.Radius, reach * fraction);
            }
        }

        /// <summary>지정 경과 시간에서 확장 후 회전 구간의 진행률과 설정 회전각으로 회전량을 라디안으로 반환하며 회수 중에는 최종 각도를 유지한다.</summary>
        private double GetStretchRotation(double elapsed)
        {
            double rotationTime = elapsed - _settings.StretchWarning - _settings.StretchSeconds;
            return Math.Max(0, Math.Min(1, rotationTime / _settings.StretchHold))
                * _settings.StretchRotationDegrees * RuneSimulation.DEGREES_TO_RADIANS;
        }

        /// <summary>공격을 종료하고 추적 가능한 공통 쿨타임을 시작한다. 잔류 독 지대는 유지한다.</summary>
        private void FinishAttack(double time) { _attack = HydraAttack.Idle; _elapsed = 0; _cooldownUntil = time + _settings.AttackCooldown; }

        /// <summary>준비 전반에는 조준을 추적하고 후반에는 고정하며 준비가 끝나면 반사 구체 한 발을 발사하고 공통 쿨타임으로 전환한다.</summary>
        private void AdvancePoison(SimulationEnemy enemy, SimulationWorld world, RuneSimulation simulation)
        {
            if (_elapsed <= _settings.PoisonTrack + RuneSimulation.COLLISION_EPSILON)
            {
                SimVector offset = world.Player.Position - enemy.Position;
                if (offset.LengthSquared > RuneSimulation.COLLISION_EPSILON) _angle = Math.Atan2(offset.Y, offset.X);
                SimVector direction = new SimVector(Math.Cos(_angle), Math.Sin(_angle));
                enemy.Move(enemy.Position, direction);
                SimVector displacement = direction * (enemy.Radius + _settings.PoisonOrbRadius);
                double wall = simulation.FirstWallFraction(enemy.Position, enemy.Position + displacement, _settings.PoisonOrbRadius);
                _poisonOrigin = enemy.Position + displacement * (double.IsPositiveInfinity(wall) ? 1 : Math.Max(0, wall - RuneSimulation.COLLISION_EPSILON));
                SimVector end = _poisonOrigin + direction * (_settings.PoisonOrbSpeed * _settings.PoisonOrbLifetime);
                wall = simulation.FirstWallFraction(_poisonOrigin, end, _settings.PoisonOrbRadius);
                _poisonTarget = double.IsPositiveInfinity(wall) ? end : _poisonOrigin + (end - _poisonOrigin) * wall;
            }
            if (_elapsed + RuneSimulation.COLLISION_EPSILON < _settings.PoisonTrack + _settings.PoisonLock) return;
            _poisons.Add(new HydraPoison(_poisonOrigin, new SimVector(Math.Cos(_angle), Math.Sin(_angle)), world.Time));
            FinishAttack(world.Time);
        }

        /// <summary>회차마다 준비 중 착지점을 예고하고 이륙 때 고정한 지점으로 도약한 뒤 착지 파동을 생성한다.</summary>
        private void AdvanceStomp(SimulationEnemy enemy, SimulationWorld world, RuneSimulation simulation)
        {
            double local = _elapsed - _repeat * _settings.StompInterval;
            if (_repeat < REPEAT_COUNT && local >= 0)
            {
                if (!_hasJumpStarted)
                {
                    _jumpOrigin = enemy.Position;
                    SimVector offset = world.Player.Position - _jumpOrigin;
                    double distance = Math.Min(offset.Length, _settings.StompSpeed * _settings.StompApproachSeconds);
                    SimVector displacement = offset.Normalized() * distance;
                    double fraction = simulation.FirstWallFraction(_jumpOrigin, _jumpOrigin + displacement, enemy.Radius);
                    if (!double.IsPositiveInfinity(fraction)) displacement = displacement * Math.Max(0, fraction - RuneSimulation.COLLISION_EPSILON);
                    _jumpTarget = world.Map.Move(_jumpOrigin, displacement, enemy.Radius);
                    if (local >= _settings.StompWindup) _hasJumpStarted = true;
                }
                if (_hasJumpStarted)
                {
                    double progress = Math.Max(0, Math.Min(1, (local - _settings.StompWindup) / _settings.StompApproachSeconds));
                    SimVector offset = _jumpTarget - _jumpOrigin;
                    SimVector desired = _jumpOrigin + offset * progress;
                    enemy.Move(world.Map.Move(enemy.Position, desired - enemy.Position, enemy.Radius), offset.Normalized());
                    if (local + RuneSimulation.COLLISION_EPSILON >= _settings.StompWindup + _settings.StompApproachSeconds)
                    {
                        _waves.Add(new HydraWave(enemy.Position, world.Time));
                        _repeat++;
                        _hasJumpStarted = false;
                    }
                }
            }
            if (_elapsed >= REPEAT_COUNT * _settings.StompInterval) FinishAttack(world.Time);
        }

        /// <summary>맵의 벽·반경 이동 규칙으로 지정 속도만큼 플레이어를 추적한다.</summary>
        private static void MoveTowardsPlayer(SimulationEnemy enemy, SimulationWorld world, double speed)
        {
            SimVector offset = world.Player.Position - enemy.Position;
            if (offset.LengthSquared < RuneSimulation.COLLISION_EPSILON) return;
            SimVector direction = offset.Normalized();
            double distance = Math.Min(offset.Length, speed * RuneSimulation.STEP_SECONDS);
            enemy.Move(world.Map.Move(enemy.Position, direction * distance, enemy.Radius), direction);
        }

        /// <summary>목 뻗기의 이전·현재 회전 경로를 여러 지점에서 검사해 빠른 회전 중 통과 누락을 막는다.</summary>
        private void HitNecks(SimulationEnemy enemy, SimulationWorld world, RuneSimulation simulation, Action<double, string> hurt)
        {
            for (int index = 0; index < STRETCH_HEAD_COUNT; index++)
                for (int sample = 0; sample <= NECK_SWEEP_SAMPLES; sample++)
                {
                    double time = Math.Max(0, _elapsed - RuneSimulation.STEP_SECONDS * sample / NECK_SWEEP_SAMPLES);
                    SimVector end = GetNeckEnd(enemy, index, time, false, simulation);
                    if (SpellGeometry.SegmentDistance(enemy.Position, end, world.Player.Position) > _settings.NeckWidth * 0.5 + world.Sector.PlayerRadius) continue;
                    hurt(_settings.NeckDamage * enemy.DamageMultiplier, "stretch");
                    return;
                }
        }

        /// <summary>고정 경로로 한 번 돌진하며 목의 최대 범위를 채운 직사각형으로 한 번만 피해를 주고 벽 또는 목표에 도달하면 목을 회수한다.</summary>
        private void AdvanceSpin(SimulationEnemy enemy, SimulationWorld world, Action<double, string> hurt)
        {
            double previous = Math.Max(_settings.SpinWarning, _elapsed - RuneSimulation.STEP_SECONDS);
            double current = Math.Min(_elapsed, _settings.SpinWarning + _spinDashDuration);
            enemy.Move(GetSpinPosition(_elapsed), new SimVector(Math.Cos(_angle), Math.Sin(_angle)));
            if (!_hasSpinHit && world.IsMission && !world.DebugInvulnerable && !world.Player.IsInvulnerable(world.Time)
                && _spinDashDuration > RuneSimulation.COLLISION_EPSILON && current > previous + RuneSimulation.COLLISION_EPSILON)
            {
                double halfExtent = GetSpinHalfExtent(enemy);
                bool hasHit = SpellGeometry.IntersectsBoxSweep(GetSpinPosition(previous), GetSpinPosition(current), SpinDirection,
                    halfExtent, halfExtent, world.Player.Position, world.Sector.PlayerRadius);
                if (hasHit) { _hasSpinHit = true; hurt(_settings.NeckDamage * enemy.DamageMultiplier, "spin"); }
            }
            if (_elapsed + RuneSimulation.COLLISION_EPSILON >= _settings.SpinWarning + _spinDashDuration + _settings.RetractSeconds)
                FinishAttack(world.Time);
        }

        /// <summary>지정 경과 시간의 몸체 위치를 벽 앞에서 제한된 고정 돌진 경로에서 반환한다.</summary>
        public SimVector GetSpinPosition(double elapsed)
        {
            double progress = _spinDashDuration <= RuneSimulation.COLLISION_EPSILON ? 0
                : Math.Max(0, Math.Min(1, (elapsed - _settings.SpinWarning) / _spinDashDuration));
            return _spinOrigin + (_spinTarget - _spinOrigin) * progress;
        }

        /// <summary>몸체 반경과 확장한 목 길이·두께를 포함하는 돌진 직사각형의 반너비·반길이를 반환한다.</summary>
        public double GetSpinHalfExtent(SimulationEnemy enemy)
            => Math.Max(enemy.Radius, _settings.SpinLength + _settings.NeckWidth * 0.5);

        /// <summary>현재 또는 예고용 최대 길이의 머리 위치를 목의 벽 충돌을 반영해 시뮬레이션 좌표로 반환한다.</summary>
        public SimVector GetNeckEnd(SimulationEnemy enemy, int index, RuneSimulation simulation, bool isPreview = false)
            => GetNeckEnd(enemy, index, _elapsed, isPreview, simulation);

        /// <summary>지정 경과 시간에서 목의 배치·신축·회전각을 계산해 판정과 표시가 공유하는 끝점을 반환한다.</summary>
        private SimVector GetNeckEnd(SimulationEnemy enemy, int index, double elapsed, bool isPreview, RuneSimulation simulation)
        {
            SimVector center = _attack == HydraAttack.Spin ? GetSpinPosition(elapsed) : enemy.Position;
            double rest = enemy.Radius + REST_NECK_LENGTH;
            double length = rest;
            double angle = _angle + index * FULL_TURN / HEAD_COUNT;
            if (_attack == HydraAttack.Stretch && index < STRETCH_HEAD_COUNT)
            {
                angle = _angle + index * FULL_TURN / STRETCH_HEAD_COUNT;
                double time = elapsed - _settings.StretchWarning;
                double progress = time < 0 ? 0 : time < _settings.StretchSeconds ? time / _settings.StretchSeconds
                    : 1 - Math.Max(0, time - _settings.StretchSeconds - _settings.StretchHold) / _settings.StretchRetractSeconds;
                length = isPreview ? _stretchLengths[index] : rest + (_stretchLengths[index] - rest) * Math.Max(0, Math.Min(1, progress));
                angle += GetStretchRotation(elapsed);
            }
            else if (_attack == HydraAttack.Spin && index < SPIN_HEAD_COUNT)
            {
                double spinTime = Math.Max(0, Math.Min(_spinDashDuration, elapsed - _settings.SpinWarning));
                angle = _angle + index * FULL_TURN / SPIN_HEAD_COUNT + spinTime / _settings.SpinSeconds * FULL_TURN;
                double progress = elapsed < _settings.SpinWarning ? elapsed / _settings.SpinWarning
                    : 1 - Math.Max(0, elapsed - _settings.SpinWarning - _spinDashDuration) / _settings.RetractSeconds;
                length = isPreview ? _settings.SpinLength : rest + (_settings.SpinLength - rest) * Math.Max(0, Math.Min(1, progress));
            }
            SimVector end = center + new SimVector(Math.Cos(angle), Math.Sin(angle)) * length;
            if (_attack == HydraAttack.Spin)
            {
                double wall = simulation.FirstWallFraction(center, end, _settings.NeckWidth * 0.5);
                if (!double.IsPositiveInfinity(wall)) end = center + (end - center) * Math.Max(0, wall - RuneSimulation.COLLISION_EPSILON);
            }
            return end;
        }

        /// <summary>전투 취소 시 공격과 잔류물 목록을 비워 재표시 때 이전 판정을 남기지 않는다.</summary>
        internal void Cancel() { _attack = HydraAttack.Idle; _elapsed = 0; _hasJumpStarted = false; _hasSpinHit = false; _spinDashDuration = 0; _poisons.Clear(); _waves.Clear(); }

        /// <summary>추첨 결과·공격 시각·목 길이·잔류물 상태를 결정성 버퍼에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        {
            state.Append(FormattableString.Invariant($"|hydra:{_attack}:{_lastAttack}:{_startedAt:R}:{_elapsed:R}:{_cooldownUntil:R}:{_angle:R}:{_repeat}:{_poisonTarget.X:R}:{_poisonTarget.Y:R}"));
            state.Append(FormattableString.Invariant($"|jump:{_jumpOrigin.X:R}:{_jumpOrigin.Y:R}:{_jumpTarget.X:R}:{_jumpTarget.Y:R}:{_hasJumpStarted}"));
            state.Append(FormattableString.Invariant($"|poisonOrigin:{_poisonOrigin.X:R}:{_poisonOrigin.Y:R}"));
            state.Append(FormattableString.Invariant($"|spinDash:{_spinOrigin.X:R}:{_spinOrigin.Y:R}:{_spinTarget.X:R}:{_spinTarget.Y:R}:{_spinDashDuration:R}:{_hasSpinHit}"));
            foreach (double length in _stretchLengths) SimHash.AppendNumber(state, length);
            foreach (HydraPoison poison in _poisons) poison.WriteState(state);
            foreach (HydraWave wave in _waves) wave.WriteState(state);
        }
    }
}
