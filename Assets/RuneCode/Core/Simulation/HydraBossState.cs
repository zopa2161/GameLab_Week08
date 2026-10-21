using System;
using System.Collections.Generic;
using System.Text;

namespace RuneCode
{
    public enum HydraAttack { Idle, Stretch, Poison, Stomp, Spin }

    /// <summary>독 구체의 비행·착탄·잔류와 다음 피해 시각을 관리한다.</summary>
    public sealed class HydraPoison
    {
        private readonly SimVector _origin;
        private readonly SimVector _target;
        private readonly double _startedAt;
        private double _age;
        private bool _hasLanded;
        private double _nextDamageAt;
        public SimVector Origin => _origin;
        public SimVector Target => _target;
        public double Age => _age;
        public bool HasLanded => _hasLanded;

        /// <summary>고정 출발·목표 위치와 비행 시작 시각을 저장한다.</summary>
        internal HydraPoison(SimVector origin, SimVector target, double time)
        { _origin = origin; _target = target; _startedAt = time; }

        /// <summary>시각으로 비행·독 지대를 진행하고 착탄 및 주기 피해를 적용하며 수명 종료 여부를 반환한다.</summary>
        internal bool Advance(SimulationWorld world, HydraDefinition settings, double damageMultiplier, Action<double> hurt)
        {
            _age = world.Time - _startedAt;
            if (_age + RuneSimulation.COLLISION_EPSILON < settings.PoisonFlight) return false;
            bool isInside = SimVector.Distance(_target, world.Player.Position) <= settings.PoisonRadius + world.Sector.PlayerRadius;
            if (!_hasLanded)
            {
                _hasLanded = true;
                _nextDamageAt = world.Time + settings.PoisonTickInterval;
                if (isInside) hurt(settings.PoisonDamage * damageMultiplier);
            }
            if (_age >= settings.PoisonFlight + settings.PoisonLifetime) return true;
            if (world.Time + RuneSimulation.COLLISION_EPSILON >= _nextDamageAt)
            {
                _nextDamageAt += settings.PoisonTickInterval;
                if (isInside) hurt(settings.PoisonTickDamage * damageMultiplier);
            }
            return false;
        }

        /// <summary>비행·착탄·주기 피해 상태를 결정성 버퍼에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        { state.Append(FormattableString.Invariant($"|poison:{_origin.X:R}:{_origin.Y:R}:{_target.X:R}:{_target.Y:R}:{_startedAt:R}:{_age:R}:{_hasLanded}:{_nextDamageAt:R}")); }
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
        internal bool Advance(SimulationWorld world, HydraDefinition settings, double bodyRadius, double damageMultiplier, Action<double> hurt)
        {
            double previousRadius = GetRadius(settings, bodyRadius);
            _age = world.Time - _startedAt;
            double currentRadius = GetRadius(settings, bodyRadius);
            double distance = SimVector.Distance(_center, world.Player.Position);
            double thickness = world.Sector.PlayerRadius + settings.WaveWidth * 0.5;
            if (!_hasHit && distance + thickness >= previousRadius && distance - thickness <= currentRadius)
            { _hasHit = true; hurt(settings.WaveDamage * damageMultiplier); }
            return _age + RuneSimulation.COLLISION_EPSILON >= settings.WaveSeconds;
        }

        /// <summary>고리 위치·수명과 피해 적용 여부를 결정성 버퍼에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        { state.Append(FormattableString.Invariant($"|wave:{_center.X:R}:{_center.Y:R}:{_startedAt:R}:{_age:R}:{_hasHit}")); }
    }

    /// <summary>Hydra 한 개체의 공격·잔류물·피해 예산을 소유하며 같은 기하를 판정과 표시에 제공한다.</summary>
    public sealed class HydraBossState
    {
        public const int HEAD_COUNT = 9;
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
        private SimVector _jumpOrigin;
        private SimVector _jumpTarget;
        private bool _hasJumpStarted;
        private double _damageBudget;
        private double _lastDamageAt;

        public HydraAttack Attack => _attack;
        public double Elapsed => _elapsed;
        public HydraDefinition Settings => _settings;
        public SimVector PoisonTarget => _poisonTarget;
        public IReadOnlyList<HydraPoison> Poisons => _poisons;
        public IReadOnlyList<HydraWave> Waves => _waves;
        public bool IsNeckWarning => _attack == HydraAttack.Stretch && _elapsed < _settings.StretchWarning
            || _attack == HydraAttack.Spin && _elapsed < _settings.SpinWarning;
        public bool IsNeckActive => _attack == HydraAttack.Stretch && _elapsed >= _settings.StretchWarning
            || _attack == HydraAttack.Spin && _elapsed >= _settings.SpinWarning && _elapsed < _settings.SpinWarning + _settings.SpinSeconds;
        public bool IsPoisonWarning => _attack == HydraAttack.Poison && _elapsed % PoisonCycle < _settings.PoisonTrack + _settings.PoisonLock;
        public bool IsStompWarning => _attack == HydraAttack.Stomp && StompLocalTime < _settings.StompWindup + _settings.StompApproachSeconds;
        public bool IsAirborne => _attack == HydraAttack.Stomp && _hasJumpStarted
            && StompLocalTime >= _settings.StompWindup && StompLocalTime < _settings.StompWindup + _settings.StompApproachSeconds;
        public double JumpProgress => IsAirborne ? Math.Max(0, Math.Min(1, (StompLocalTime - _settings.StompWindup) / _settings.StompApproachSeconds)) : 0;
        public SimVector JumpTarget => _jumpTarget;
        public double StompLocalTime => _elapsed % _settings.StompInterval;
        private double PoisonCycle => _settings.PoisonTrack + _settings.PoisonLock + _settings.PoisonFlight + _settings.PoisonGap;

        /// <summary>공유 설정과 생성 시각·최대 체력으로 개별 공격 대기와 1구간 피해 예산을 초기화한다.</summary>
        internal HydraBossState(HydraDefinition settings, double time, double maxHp)
        {
            _settings = settings;
            _cooldownUntil = time + settings.InitialDelay;
            _lastDamageAt = time;
            _damageBudget = maxHp * settings.DpsHealthRatio * settings.DpsWindow;
        }

        /// <summary>현재 시각까지 피해 예산을 회복하고 예산 초과 피해만 감쇠해 반환한다. 화상은 예산을 쓰지 않고 약점 배율을 적용한다.</summary>
        internal double LimitDamage(double damage, bool isBurn, double time, double maxHp)
        {
            if (isBurn) return damage * _settings.BurnMultiplier;
            double dps = maxHp * _settings.DpsHealthRatio;
            _damageBudget = Math.Min(dps * _settings.DpsWindow, _damageBudget + Math.Max(0, time - _lastDamageAt) * dps);
            _lastDamageAt = time;
            double fullDamage = Math.Min(Math.Max(0, damage), _damageBudget);
            _damageBudget -= fullDamage;
            return fullDamage + Math.Max(0, damage - fullDamage) * _settings.ExcessDamageMultiplier;
        }

        /// <summary>잔류물과 현재 공격을 한 틱 진행하며 쿨타임 추적·도약 이동과 목·파동·독 피해를 처리한다.</summary>
        internal void Advance(SimulationEnemy enemy, SimulationWorld world, RuneSimulation simulation, Action<double> hurt)
        {
            for (int index = _poisons.Count - 1; index >= 0; index--)
                if (_poisons[index].Advance(world, _settings, enemy.DamageMultiplier, hurt)) _poisons.RemoveAt(index);
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
                    if (IsNeckActive) HitNecks(enemy, world, hurt);
                    if (_elapsed >= _settings.StretchWarning + _settings.StretchSeconds + _settings.StretchHold + _settings.RetractSeconds) FinishAttack(world.Time);
                    break;
                case HydraAttack.Poison: AdvancePoison(enemy, world); break;
                case HydraAttack.Stomp: AdvanceStomp(enemy, world, simulation); break;
                case HydraAttack.Spin:
                    if (IsNeckActive) HitNecks(enemy, world, hurt);
                    if (_elapsed >= _settings.SpinWarning + _settings.SpinSeconds + _settings.RetractSeconds) FinishAttack(world.Time);
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
            _startedAt = world.Time;
            _elapsed = 0;
            _repeat = 0;
            _hasJumpStarted = false;
            _poisonTarget = world.Player.Position;
            SimVector offset = world.Player.Position - enemy.Position;
            _angle = Math.Atan2(offset.Y, offset.X);
            if (_attack != HydraAttack.Stretch) return;
            double reach = world.Map.Width + world.Map.Height;
            for (int index = 0; index < HEAD_COUNT; index++)
            {
                double angle = _angle + index * FULL_TURN / HEAD_COUNT;
                SimVector end = enemy.Position + new SimVector(Math.Cos(angle), Math.Sin(angle)) * reach;
                double fraction = simulation.FirstWallFraction(enemy.Position, end, _settings.NeckWidth * 0.5);
                _stretchLengths[index] = double.IsPositiveInfinity(fraction) ? reach : Math.Max(enemy.Radius, reach * fraction);
            }
        }

        /// <summary>공격을 종료하고 추적 가능한 공통 쿨타임을 시작한다. 잔류 독 지대는 유지한다.</summary>
        private void FinishAttack(double time) { _attack = HydraAttack.Idle; _elapsed = 0; _cooldownUntil = time + _settings.AttackCooldown; }

        /// <summary>매 발마다 추적·위치 고정·비행을 실행하고 세 번째 발의 종료 뒤 쿨타임으로 전환한다.</summary>
        private void AdvancePoison(SimulationEnemy enemy, SimulationWorld world)
        {
            double local = _elapsed - _repeat * PoisonCycle;
            if (_repeat < REPEAT_COUNT && local >= 0 && local < _settings.PoisonTrack) _poisonTarget = world.Player.Position;
            if (_repeat < REPEAT_COUNT && local + RuneSimulation.COLLISION_EPSILON >= _settings.PoisonTrack + _settings.PoisonLock)
            {
                double angle = _angle + _repeat * FULL_TURN / HEAD_COUNT;
                SimVector origin = enemy.Position + new SimVector(Math.Cos(angle), Math.Sin(angle)) * (enemy.Radius + REST_NECK_LENGTH);
                _poisons.Add(new HydraPoison(origin, _poisonTarget, world.Time));
                _repeat++;
            }
            if (_elapsed >= REPEAT_COUNT * PoisonCycle) FinishAttack(world.Time);
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

        /// <summary>목의 이전·현재 회전 경로를 여러 지점에서 검사해 빠른 회전 중 통과 누락을 막는다.</summary>
        private void HitNecks(SimulationEnemy enemy, SimulationWorld world, Action<double> hurt)
        {
            int count = _attack == HydraAttack.Spin ? SPIN_HEAD_COUNT : HEAD_COUNT;
            for (int index = 0; index < count; index++)
                for (int sample = 0; sample <= NECK_SWEEP_SAMPLES; sample++)
                {
                    double time = Math.Max(0, _elapsed - RuneSimulation.STEP_SECONDS * sample / NECK_SWEEP_SAMPLES);
                    SimVector end = GetNeckEnd(enemy, index, time, false);
                    if (SpellGeometry.SegmentDistance(enemy.Position, end, world.Player.Position) > _settings.NeckWidth * 0.5 + world.Sector.PlayerRadius) continue;
                    hurt(_settings.NeckDamage * enemy.DamageMultiplier);
                    return;
                }
        }

        /// <summary>表示用の現在または予告用の最大長の頭位置をシミュレーション座標で返す。</summary>
        public SimVector GetNeckEnd(SimulationEnemy enemy, int index, bool isPreview = false)
            => GetNeckEnd(enemy, index, _elapsed, isPreview);

        /// <summary>指定経過時間で頭の配置・伸縮・回転角を求め、判定と表示が共有する終点を返す。</summary>
        private SimVector GetNeckEnd(SimulationEnemy enemy, int index, double elapsed, bool isPreview)
        {
            double rest = enemy.Radius + REST_NECK_LENGTH;
            double length = rest;
            double angle = _angle + index * FULL_TURN / HEAD_COUNT;
            if (_attack == HydraAttack.Stretch)
            {
                double time = elapsed - _settings.StretchWarning;
                double progress = time < 0 ? 0 : time < _settings.StretchSeconds ? time / _settings.StretchSeconds
                    : 1 - Math.Max(0, time - _settings.StretchSeconds - _settings.StretchHold) / _settings.RetractSeconds;
                length = isPreview ? _stretchLengths[index] : rest + (_stretchLengths[index] - rest) * Math.Max(0, Math.Min(1, progress));
            }
            else if (_attack == HydraAttack.Spin && index < SPIN_HEAD_COUNT)
            {
                double spinTime = Math.Max(0, Math.Min(_settings.SpinSeconds, elapsed - _settings.SpinWarning));
                angle = _angle + index * FULL_TURN / SPIN_HEAD_COUNT + spinTime / _settings.SpinSeconds * FULL_TURN;
                double progress = elapsed < _settings.SpinWarning ? elapsed / _settings.SpinWarning
                    : 1 - Math.Max(0, elapsed - _settings.SpinWarning - _settings.SpinSeconds) / _settings.RetractSeconds;
                length = isPreview ? _settings.SpinLength : rest + (_settings.SpinLength - rest) * Math.Max(0, Math.Min(1, progress));
            }
            return enemy.Position + new SimVector(Math.Cos(angle), Math.Sin(angle)) * length;
        }

        /// <summary>전투 취소 시 공격과 잔류물 목록을 비워 재표시 때 이전 판정을 남기지 않는다.</summary>
        internal void Cancel() { _attack = HydraAttack.Idle; _elapsed = 0; _hasJumpStarted = false; _poisons.Clear(); _waves.Clear(); }

        /// <summary>추첨 결과·공격 시각·목 길이·피해 예산·잔류물 상태를 결정성 버퍼에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        {
            state.Append(FormattableString.Invariant($"|hydra:{_attack}:{_lastAttack}:{_startedAt:R}:{_elapsed:R}:{_cooldownUntil:R}:{_angle:R}:{_repeat}:{_poisonTarget.X:R}:{_poisonTarget.Y:R}:{_damageBudget:R}:{_lastDamageAt:R}"));
            state.Append(FormattableString.Invariant($"|jump:{_jumpOrigin.X:R}:{_jumpOrigin.Y:R}:{_jumpTarget.X:R}:{_jumpTarget.Y:R}:{_hasJumpStarted}"));
            foreach (double length in _stretchLengths) SimHash.AppendNumber(state, length);
            foreach (HydraPoison poison in _poisons) poison.WriteState(state);
            foreach (HydraWave wave in _waves) wave.WriteState(state);
        }
    }
}
