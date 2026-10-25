using System;
using System.Collections.Generic;
using System.Text;

namespace RuneCode
{
    public readonly struct SimVector
    {
        private readonly double _x;
        private readonly double _y;
        public double X => _x;
        public double Y => _y;
        public double Length => Math.Sqrt(_x * _x + _y * _y);
        public double LengthSquared => _x * _x + _y * _y;
        public static SimVector Zero => new SimVector(0, 0);

        /// <summary>주어진 좌표로 시뮬레이션 위치 또는 방향을 생성한다.</summary>
        public SimVector(double x, double y) { _x = x; _y = y; }

        /// <summary>길이가 0이면 기본 우측 방향, 그 외에는 단위 방향을 반환한다.</summary>
        public SimVector Normalized() => Length > 0.000001 ? this / Length : new SimVector(1, 0);

        /// <summary>벡터를 라디안 각도로 회전한 결과를 반환한다.</summary>
        public SimVector Rotated(double radians) => new SimVector(_x * Math.Cos(radians) - _y * Math.Sin(radians), _x * Math.Sin(radians) + _y * Math.Cos(radians));

        /// <summary>두 좌표의 거리를 반환한다.</summary>
        public static double Distance(SimVector a, SimVector b) => (a - b).Length;

        /// <summary>두 벡터의 내적을 반환한다.</summary>
        public static double Dot(SimVector a, SimVector b) => a._x * b._x + a._y * b._y;

        /// <summary>두 벡터를 더한 좌표를 반환한다.</summary>
        public static SimVector operator +(SimVector a, SimVector b) => new SimVector(a._x + b._x, a._y + b._y);

        /// <summary>두 벡터의 차이를 반환한다.</summary>
        public static SimVector operator -(SimVector a, SimVector b) => new SimVector(a._x - b._x, a._y - b._y);

        /// <summary>벡터 각 좌표에 배율을 적용한 결과를 반환한다.</summary>
        public static SimVector operator *(SimVector a, double scale) => new SimVector(a._x * scale, a._y * scale);

        /// <summary>벡터 각 좌표를 나눈 결과를 반환한다.</summary>
        public static SimVector operator /(SimVector a, double scale) => new SimVector(a._x / scale, a._y / scale);
    }

    public readonly struct SimulationInput
    {
        private readonly SimVector _movement;
        private readonly SimVector _aimDirection;
        private readonly SimVector? _aimPoint;
        private readonly bool _castA;
        private readonly bool _castB;
        private readonly bool _castC;
        private readonly bool _dash;
        private readonly bool _interact;
        private readonly bool _castPressed;
        private readonly bool _castReleased;
        private readonly bool _isAutomatic;
        public SimVector Movement => _movement;
        public SimVector AimDirection => _aimDirection;
        public SimVector? AimPoint => _aimPoint;
        public bool CastA => _castA;
        public bool CastB => _castB;
        public bool CastC => _castC;
        public bool Dash => _dash;
        public bool Interact => _interact;
        public bool CastPressed => _castPressed;
        public bool CastReleased => _castReleased;
        public bool IsAutomatic => _isAutomatic;

        /// <summary>한 고정 틱의 이동, 조준 방향·선택적 포인터 위치, 슬롯 시전, 대시, 상호작용 입력을 보관한다.</summary>
        public SimulationInput(SimVector movement, SimVector aimDirection, bool castA = false, bool castB = false, bool castC = false, bool dash = false, bool interact = false,
            bool castPressed = false, bool castReleased = false, bool isAutomatic = false, SimVector? aimPoint = null)
        {
            _movement = movement; _aimDirection = aimDirection; _castA = castA; _castB = castB; _castC = castC;
            _aimPoint = aimPoint;
            _dash = dash; _interact = interact; _castPressed = castPressed; _castReleased = castReleased; _isAutomatic = isAutomatic;
        }
    }

    public enum MissionStage { Bench, Combat, Cleared, Dead, TimedOut, RoundBreak }

    public sealed class SimulationPlayer
    {
        private readonly double[] _cooldowns = new double[3];
        private readonly double[] _cooldownDurations = new double[3];
        private readonly bool[] _isSpellRunning = new bool[3];
        private readonly double[] _executionRemaining = new double[3];
        private readonly Dictionary<string, double> _otherResources = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _otherResourceCaps = new Dictionary<string, double>(StringComparer.Ordinal);
        private SimVector _position;
        private SimVector _aimDirection = new SimVector(1, 0);
        private SimVector _dashDirection;
        private double _hp;
        private double _maxHp;
        private double _damageReceived;
        private double _energy;
        private double _maxEnergy;
        private double _shield;
        private double _shieldAbsorbed;
        private double _shieldUntil;
        private double _dashCooldown;
        private double _dashCooldownDuration;
        private double _dashRemaining;
        private int _dashesStarted;
        private double _lastDashStartTime = double.NegativeInfinity;
        private bool _isDashGuardUsed;
        private double _invulnerableUntil;
        private double _burnUntil;
        private double _burnNextTick;
        public SimVector Position => _position;
        public SimVector AimDirection => _aimDirection;
        public double Hp => _hp;
        public double MaxHp => _maxHp;
        /// <summary>실제 HP에서 감소한 누적 피해를 반환한다. 초과 피해는 포함하지 않는다.</summary>
        public double DamageReceived => _damageReceived;
        public double Energy => _energy;
        public double MaxEnergy => _maxEnergy;
        public double Shield => _shield;
        /// <summary>보호막이 흡수한 누적 피해를 반환한다.</summary>
        public double ShieldAbsorbed => _shieldAbsorbed;
        public double DashCooldown => _dashCooldown;
        public double DashCooldownDuration => _dashCooldownDuration;
        public double DashRemaining => _dashRemaining;
        /// <summary>실제로 시작한 대시 횟수를 반환한다.</summary>
        public int DashesStarted => _dashesStarted;

        /// <summary>마지막으로 대시를 시작한 시각이다. 대시한 적이 없으면 음의 무한대다.</summary>
        public double LastDashStartTime => _lastDashStartTime;

        /// <summary>현재 대시에서 대시 충돌 보호를 이미 사용했는지 반환한다. 대시를 시작할 때 초기화된다.</summary>
        public bool IsDashGuardUsed => _isDashGuardUsed;
        public IReadOnlyList<double> Cooldowns => _cooldowns;
        public IReadOnlyList<double> CooldownDurations => _cooldownDurations;

        /// <summary>지정 슬롯의 루트 체인이 아직 실행 중인지 반환한다.</summary>
        public bool IsSpellRunning(int slot) => _isSpellRunning[slot];

        /// <summary>지정 슬롯 루트 체인의 예상 남은 시간을 반환한다.</summary>
        public double ExecutionRemaining(int slot) => _executionRemaining[slot];

        /// <summary>최대 체력과 에너지 및 시작 위치로 플레이어의 전투 상태를 초기화한다.</summary>
        internal SimulationPlayer(double hp, double energy, SimVector position) { _maxHp = hp; _hp = hp; _maxEnergy = energy; _energy = energy; _position = position; }

        /// <summary>이어하기 시작 체력을 0 초과 최대 체력 이하로 제한해 설정한다.</summary>
        internal void SetHp(double hp)
        {
            if (hp <= 0) return;
            _hp = Math.Min(_maxHp, hp);
        }

        /// <summary>최대 체력·최대 전력을 바꾼다. 최대치가 늘면 현재 값에도 증가분을 더하고, 줄면 현재 값을 새 최대치로만 제한한다.</summary>
        internal void SetMaxStats(double maxHp, double maxEnergy)
        {
            _hp = Math.Min(maxHp, _hp + Math.Max(0, maxHp - _maxHp));
            _energy = Math.Min(maxEnergy, _energy + Math.Max(0, maxEnergy - _maxEnergy));
            _maxHp = maxHp;
            _maxEnergy = maxEnergy;
        }

        /// <summary>이동 처리 후 플레이어 위치를 갱신한다.</summary>
        internal void Move(SimVector position) { _position = position; }

        /// <summary>입력 방향으로 조준 상태를 갱신한다.</summary>
        internal void Aim(SimVector direction) { if (direction.LengthSquared > 0.000001) _aimDirection = direction.Normalized(); }

        /// <summary>쿨다운이 끝났을 때 주문을 시작하고 예상 실행시간과 실행에 포함된 쿨다운을 설정한다.</summary>
        internal bool TryBeginSpell(int slot, double executionSeconds, double cooldownSeconds)
        {
            if (_cooldowns[slot] > 0.000001 || _isSpellRunning[slot]) return false;
            _isSpellRunning[slot] = true;
            _executionRemaining[slot] = Math.Max(0, executionSeconds);
            _cooldowns[slot] = Math.Max(0, cooldownSeconds);
            _cooldownDurations[slot] = _cooldowns[slot];
            return true;
        }

        /// <summary>슬롯의 쿨다운을 지정한 초부터 새로 시작한다. 진행 중인 주문 상태는 유지한다.</summary>
        internal void StartSpellCooldown(int slot, double seconds)
        {
            _cooldowns[slot] = Math.Max(0, seconds);
            _cooldownDurations[slot] = _cooldowns[slot];
        }

        /// <summary>주문 실행 상태를 끝내고 이미 감소 중인 쿨다운은 계속 유지한다.</summary>
        internal void FinishSpell(int slot)
        {
            _isSpellRunning[slot] = false;
            _executionRemaining[slot] = 0;
        }

        /// <summary>전투가 끝나거나 취소될 때 진행 중 루트 시전을 중단하고 환급 없이 상태를 지운다.</summary>
        internal void CancelSpellExecutions()
        {
            for (int slot = 0; slot < _isSpellRunning.Length; slot++)
            {
                _isSpellRunning[slot] = false;
                _executionRemaining[slot] = 0;
            }
        }

        /// <summary>모든 슬롯의 쿨다운 잔여 시간을 0으로 되돌린다. 쿨다운 표시에 쓰는 지속시간은 건드리지 않는다.</summary>
        internal void ResetCooldowns()
        {
            for (int slot = 0; slot < _cooldowns.Length; slot++) _cooldowns[slot] = 0;
        }

        /// <summary>모든 비용 자원이 충분할 때만 함께 차감하고 하나라도 부족하면 상태를 바꾸지 않은 채 false를 반환한다.</summary>
        internal bool TrySpend(ResourceCostSet costs)
        {
            foreach (ResourceAmount amount in costs.Amounts)
                if (GetResourceAmount(amount.Resource) + 0.000001 < amount.Amount) return false;
            foreach (ResourceAmount amount in costs.Amounts)
                SetResourceAmount(amount.Resource, Math.Max(0, GetResourceAmount(amount.Resource) - amount.Amount));
            return true;
        }

        /// <summary>현재 자원량을 반환하고 관리 대상이 아닌 자원은 0을 반환한다.</summary>
        public double GetResourceAmount(string resource)
        {
            if (resource == "mana") return _energy;
            return _otherResources.TryGetValue(resource, out double amount) ? amount : 0;
        }

        /// <summary>재생 자원 이외의 현재 보유량과 한도를 시뮬레이션 상태에 등록한다.</summary>
        internal void SetResource(string resource, double amount, double maximum)
        {
            if (resource == "mana") return;
            _otherResources[resource] = Math.Max(0, amount);
            _otherResourceCaps[resource] = Math.Max(0, maximum);
        }

        /// <summary>지정 자원의 현재 보유량을 갱신하고 mana는 기존 에너지 필드에 기록한다.</summary>
        private void SetResourceAmount(string resource, double amount)
        {
            if (resource == "mana") _energy = amount;
            else if (_otherResources.ContainsKey(resource)) _otherResources[resource] = amount;
        }

        /// <summary>시간 경과에 따라 에너지를 회복하고 쿨다운, 보호막 및 대시 지속 상태를 갱신한다.</summary>
        internal void Advance(double dt, double time, double regen)
        { _energy = Math.Min(_maxEnergy, _energy + dt * regen); for (int i = 0; i < _cooldowns.Length; i++) { _cooldowns[i] = Math.Max(0, _cooldowns[i] - dt); _executionRemaining[i] = Math.Max(0, _executionRemaining[i] - dt); } _dashCooldown = Math.Max(0, _dashCooldown - dt); _dashRemaining = Math.Max(0, _dashRemaining - dt); if (time >= _shieldUntil) _shield = 0; }

        /// <summary>에너지를 지정량만큼 회복하고 최대 에너지를 넘지 않게 한다.</summary>
        internal void RestoreEnergy(double amount) { _energy = Math.Min(_maxEnergy, _energy + Math.Max(0, amount)); }

        /// <summary>대시가 가능한 경우 방향, 지속 시간, 강화 보너스가 더해진 무적 및 쿨다운을 설정하고 대시 시작 시각과 충돌 보호 사용 여부를 갱신한다.</summary>
        internal bool StartDash(SimVector direction, double seconds, double cooldown, double time, double invulnerabilityBonus)
        { if (_dashCooldown > 0.000001) return false; _dashesStarted++; _lastDashStartTime = time; _isDashGuardUsed = false; _dashDirection = direction.Normalized(); _dashRemaining = seconds; _dashCooldown = cooldown; _dashCooldownDuration = cooldown; _invulnerableUntil = Math.Max(_invulnerableUntil, time + seconds + invulnerabilityBonus); return true; }

        /// <summary>현재 대시에서 대시 충돌 보호를 사용한 것으로 표시한다.</summary>
        internal void MarkDashGuardUsed() => _isDashGuardUsed = true;

        /// <summary>무적 종료 시각을 현재 값과 지정 시각 뒤 지속시간 중 더 늦는 값으로 늘린다. 대시 충돌 보호가 쓴다.</summary>
        internal void ExtendInvulnerability(double time, double seconds) { _invulnerableUntil = Math.Max(_invulnerableUntil, time + seconds); }

        /// <summary>진행 중인 대시의 이동 방향을 반환한다.</summary>
        internal SimVector GetDashDirection() => _dashDirection;

        /// <summary>라운드 종료 시 진행 중인 대시와 대시 무적만 취소하고 위치·자원·쿨다운을 유지한다.</summary>
        internal void CancelDash(double time)
        {
            if (_dashRemaining <= 0) return;
            _dashRemaining = 0;
            _invulnerableUntil = Math.Min(_invulnerableUntil, time);
        }

        /// <summary>지정 시각에 대시 또는 피격 무적이 유지되는지 반환한다.</summary>
        internal bool IsInvulnerable(double time) => time < _invulnerableUntil;

        /// <summary>무적을 확인하고 보호막과 체력에 피해를 적용하여 실제 체력 피해를 반환한다.</summary>
        internal double Hurt(double damage, double time, double invulnerability)
        { if (time < _invulnerableUntil) return 0; double absorbed = Math.Min(_shield, damage); _shield -= absorbed; _shieldAbsorbed += absorbed; double actual = Math.Min(_hp, damage - absorbed); _hp -= actual; _damageReceived += actual; _invulnerableUntil = time + invulnerability; return actual; }

        /// <summary>
        /// 상태 이상 피해를 보호막과 체력에 적용하고 실제 체력 피해를 반환한다. 피격 무적을 무시하며 새 무적도 주지 않는다(결정 Q13).
        /// </summary>
        internal double HurtByStatus(double damage)
        { double absorbed = Math.Min(_shield, damage); _shield -= absorbed; _shieldAbsorbed += absorbed; double actual = Math.Min(_hp, damage - absorbed); _hp -= actual; _damageReceived += actual; return actual; }

        /// <summary>지정 시각에 화상 상태인지 반환한다.</summary>
        public bool IsBurning(double time) => time < _burnUntil;

        /// <summary>화상을 부여한다. 진행 중이면 지속시간만 갱신하고 피해 틱 일정은 유지하며, 끝난 뒤면 새 일정을 시작한다.</summary>
        internal void ApplyBurn(double time, double interval, double seconds)
        {
            if (time >= _burnUntil) _burnNextTick = time + interval;
            _burnUntil = time + seconds;
        }

        /// <summary>현재 시각에 도달한 화상 피해 틱이 있으면 다음 틱으로 넘기고 true를 반환한다. 마지막 틱은 만료 시각까지다.</summary>
        internal bool TryTakeBurnTick(double time, double interval)
        {
            if (_burnUntil <= 0 || _burnNextTick > _burnUntil + 0.000001 || time + 0.000001 < _burnNextTick) return false;
            _burnNextTick += interval;
            return true;
        }

        /// <summary>회복량을 최대 체력 범위로 제한하여 플레이어 체력에 적용한다.</summary>
        internal void Heal(double amount) { _hp = Math.Min(_maxHp, _hp + Math.Max(0, amount)); }

        /// <summary>터미널에 도착하여 최대 체력 비율을 회복하고 에너지를 충전한다.</summary>
        internal void Rest(double healFraction) { _hp = Math.Min(_maxHp, _hp + _maxHp * healFraction); _energy = _maxEnergy; for (int i = 0; i < _cooldowns.Length; i++) _cooldowns[i] = 0; _dashRemaining = 0; }

        /// <summary>위치, 능력치, 보호막, 대시 및 무적의 현재와 예정 상태를 결정성 해시 버퍼에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        {
            state.Append(FormattableString.Invariant($"|{_position.X:R}|{_position.Y:R}|{_aimDirection.X:R}|{_aimDirection.Y:R}|{_dashDirection.X:R}|{_dashDirection.Y:R}|{_hp:R}|{_maxHp:R}|{_energy:R}|{_maxEnergy:R}|{_shield:R}|{_shieldUntil:R}|{_dashCooldown:R}|{_dashRemaining:R}|{_invulnerableUntil:R}|{_lastDashStartTime:R}|{_isDashGuardUsed}|{_burnUntil:R}|{_burnNextTick:R}"));
            for (int slot = 0; slot < _isSpellRunning.Length; slot++)
                if (_isSpellRunning[slot]) state.Append(FormattableString.Invariant($"|running:{slot}:{_executionRemaining[slot]:R}"));
            var resources = new List<string>(_otherResources.Keys);
            resources.Sort(StringComparer.Ordinal);
            foreach (string resource in resources)
                state.Append(FormattableString.Invariant($"|{resource}:{_otherResources[resource]:R}/{_otherResourceCaps[resource]:R}"));
            foreach (double cooldown in _cooldowns) state.Append(FormattableString.Invariant($"|{cooldown:R}"));
        }
    }

    public sealed class SimulationEnemy
    {
        private const double CIRCLE_SHIELD_HP_MULTIPLIER = 7;

        private readonly int _id;
        private readonly EnemyDefinition _definition;
        private readonly EnemyMovementType _movementType;
        private readonly BossDefinition _boss;
        private HydraBossState _hydra;
        private HexaBossState _hexa;
        private LegolasBossState _legolas;
        private readonly bool _isDummy;
        private readonly bool _isElite;
        private readonly bool _isKillTarget;
        private readonly double _speedMultiplier;
        private readonly double _hitFlashSeconds;
        private readonly double _damage;
        private readonly double _damageMultiplier;
        private readonly int _reward;
        private readonly EliteSpawnDefinition _elite;
        private SimVector _position;
        private SimVector _facing = new SimVector(-1, 0);
        private double _hp;
        private double _maxHp;
        private double _attackAt;
        private double _warningUntil;
        private double _contactGraceUntil;
        private double _dashUntil;
        private SimVector _attackDirection;
        private double _flashUntil;
        private int _phase = 1;
        private double _patchUntil;
        private double _reinforcementAt;
        private double _hazardAt;
        private int _hazardRemaining;
        private bool _isNextBossBurst;
        private double _observedTime;
        private string _lastDamageElement = "raw";
        private string _lastDamageForm = "";
        private SimVector _attackOrigin;
        private double _aimUntil;
        private double _behaviorRemaining;
        private bool _isStopping;
        private int _carrierOwnerId;
        private readonly int _summonerId;
        private double _shieldHp;
        private int _spawnCursor;
        private int _spawnRemaining;
        private double _spawnRemainingSeconds;

        private SimVector _pendingDisplacement;
        private double _pendingDisplacementLimit;
        private double _pendingDisplacementSeconds;
        private int _pendingDisplacementTick = -1;
        private SimVector _displacementVelocity;
        private double _displacementRemaining;

        public int Id => _id;
        public string Kind => _definition.Id;
        public SimVector Position => _position;
        public SimVector Facing => _facing;
        public double Hp => _hp;
        public double MaxHp => _maxHp;
        // 엘리트는 엘리트 정의의 반경 배율을 곱한 크기로 판정·표시한다.
        public double Radius => _elite != null ? _definition.Radius * _elite.RadiusMultiplier : _definition.Radius;
        public double Damage => _damage;
        public int Reward => _reward;
        public bool IsDummy => _isDummy;
        public bool IsElite => _isElite;
        internal bool IsKillTarget => _isKillTarget;
        public double SpeedMultiplier => _speedMultiplier;
        public bool IsAlive => _hp > 0;
        public bool IsFlashing => _observedTime < _flashUntil;
        public bool IsWarning => _observedTime < _warningUntil;
        public int Phase => _phase;
        public bool IsPatching => _observedTime < _patchUntil;
        public string LastDamageElement => _lastDamageElement;
        public string LastDamageForm => _lastDamageForm;
        public EnemyDefinition Definition => _definition;
        public BossDefinition Boss => _boss;
        public HydraBossState Hydra => _hydra;
        public HexaBossState Hexa => _hexa;
        public LegolasBossState Legolas => _legolas;
        public SimVector AttackOrigin => _attackOrigin;
        public SimVector AimedDirection => _attackDirection;
        public bool IsAiming => _aimUntil > 0;
        public double AimUntil => _aimUntil;
        public bool IsStopping => _isStopping;
        public int CarrierOwnerId => _carrierOwnerId;

        /// <summary>이 적을 소환한 Hexa 보스의 개체 ID다. 직접 소환된 적이 아니면 0이다.</summary>
        public int SummonerId => _summonerId;

        public double ShieldHp => _shieldHp;
        public bool HasCircleShield => IsAlive && _shieldHp > 0;
        internal EnemyMovementType MovementType => _movementType;
        internal double DamageMultiplier => _damageMultiplier;
        internal EliteSpawnDefinition Elite => _elite;
        internal double AttackAt { get => _attackAt; set => _attackAt = value; }
        internal double WarningUntil { get => _warningUntil; set => _warningUntil = value; }

        /// <summary>이 시각 전까지는 접촉 피해를 주지 않는다. 맵형 스폰 직후 유예에 쓴다.</summary>
        internal double ContactGraceUntil { get => _contactGraceUntil; set => _contactGraceUntil = value; }
        internal double DashUntil { get => _dashUntil; set => _dashUntil = value; }
        internal SimVector AttackDirection { get => _attackDirection; set => _attackDirection = value; }
        internal double ReinforcementAt { get => _reinforcementAt; set => _reinforcementAt = value; }
        internal double HazardAt { get => _hazardAt; set => _hazardAt = value; }

        /// <summary>현재 장판 세트에서 아직 설치할 장판 수다. 0이 되면 세트 간격 후 새 세트를 시작한다.</summary>
        internal int HazardRemaining { get => _hazardRemaining; set => _hazardRemaining = value; }
        internal bool IsNextBossBurst { get => _isNextBossBurst; set => _isNextBossBurst = value; }
        internal SimVector ShotOrigin { get => _attackOrigin; set => _attackOrigin = value; }
        internal double AimEndsAt { get => _aimUntil; set => _aimUntil = value; }
        internal double BehaviorRemaining { get => _behaviorRemaining; set => _behaviorRemaining = value; }
        internal bool IsBehaviorStopping { get => _isStopping; set => _isStopping = value; }
        internal int SpawnCursor { get => _spawnCursor; set => _spawnCursor = value; }
        internal int SpawnRemaining { get => _spawnRemaining; set => _spawnRemaining = value; }
        internal double SpawnRemainingSeconds { get => _spawnRemainingSeconds; set => _spawnRemainingSeconds = value; }
        internal int OrbitSign => (_id & 1) == 0 ? 1 : -1;

        /// <summary>공유 적 설정과 보스 설정을 변경하지 않고 이동 종류, 위치, 개별 체력·피해·이동 속도 배율, 보상, 무반격, 엘리트 여부와 엘리트 정의, 목표 처치 수 집계 대상 여부, 캐리어 소유자와 소환한 Hexa ID로 실행 상태를 생성한다. 보스가 아니면 보스 설정은 null이다.</summary>
        internal SimulationEnemy(int id, EnemyDefinition definition, EnemyMovementType movementType, BossDefinition boss, SimVector position, bool isDummy, double hp, double hitFlashSeconds, double hpMultiplier = 1, double damageMultiplier = 1, int reward = -1, bool isElite = false, double speedMultiplier = 1, EliteSpawnDefinition elite = null, bool isKillTarget = false, int carrierOwnerId = 0, int summonerId = 0)
        { _id = id; _definition = definition; _movementType = movementType; _boss = boss; _position = position; _isDummy = isDummy; _isElite = isElite; _isKillTarget = isKillTarget; _speedMultiplier = speedMultiplier; _hitFlashSeconds = hitFlashSeconds; _maxHp = hp > 0 ? hp : definition.Hp * hpMultiplier; _hp = _maxHp; _damageMultiplier = damageMultiplier; _damage = definition.Damage * damageMultiplier; _reward = reward >= 0 ? reward : definition.Reward; _attackAt = definition.AttackInterval; _elite = elite; _carrierOwnerId = carrierOwnerId; _summonerId = summonerId; _behaviorRemaining = definition.MoveSeconds; _shieldHp = definition.HasTrait(EnemyDefinition.TRAIT_CIRCLE_SHIELD) ? _maxHp * CIRCLE_SHIELD_HP_MULTIPLIER : 0; }

        /// <summary>예고, 패치 및 피격 표시 판정에 사용할 관측 시각을 갱신한다.</summary>
        internal void Observe(double time) { _observedTime = time; }

        /// <summary>보스 정의에 맞춰 생성 시각·최대 체력을 사용하는 Hydra 상태 또는 레골라스 공격 상태를 만든다.</summary>
        internal void InitializeBossState(double time)
        {
            if (_boss != null && _boss.IsHydraPattern) _hydra = new HydraBossState(_boss.Hydra, time, _maxHp);
            if (_boss != null && _boss.IsLegolasPattern) _legolas = new LegolasBossState(_boss.Legolas);
        }

        /// <summary>Hexa 정의가 있는 개체에 새 비공격 보스 상태를 생성한다.</summary>
        internal void InitializeHexa()
        {
            if (_boss != null && _boss.IsHexaPattern) _hexa = new HexaBossState();
        }

        /// <summary>적 위치와 바라볼 방향을 갱신한다. 방향 전환 시간이 설정된 적은 이번 틱 최대 회전각만큼 목표 방향으로 돌고, 없으면 즉시 향한다.</summary>
        internal void Move(SimVector position, SimVector facing)
        {
            _position = position;
            if (facing.LengthSquared <= 0.000001) return;
            double turnSeconds = _definition.TurnSeconds;
            if (turnSeconds <= 0) { _facing = facing.Normalized(); return; }
            _facing = RotateToward(_facing, facing.Normalized(), Math.PI / turnSeconds * RuneSimulation.STEP_SECONDS);
        }

        /// <summary>현재 방향을 목표 방향으로 최단 호로 돌린 방향을 반환하고, 각도 차가 maxRadians보다 크면 maxRadians만큼만 돌린다.</summary>
        private static SimVector RotateToward(SimVector current, SimVector target, double maxRadians)
        {
            double cross = current.X * target.Y - current.Y * target.X;
            double angle = Math.Atan2(cross, SimVector.Dot(current, target));
            if (Math.Abs(angle) <= maxRadians) return target;
            return current.Rotated(Math.Sign(angle) * maxRadians);
        }

        /// <summary>지정 틱의 강제 이동 벡터를 합산하고 최대 단일 거리와 유지 시간을 저장한다. 실제 위치는 변경하지 않는다.</summary>
        internal void QueueDisplacement(SimVector direction, double distance, double seconds, int tick)
        {
            if (direction.Length <= RuneSimulation.COLLISION_EPSILON || distance <= 0 || seconds <= 0) return;
            if (_pendingDisplacementTick != tick)
            {
                if (_pendingDisplacementTick >= 0) CommitDisplacement();
                _pendingDisplacement = SimVector.Zero;
                _pendingDisplacementLimit = 0;
                _pendingDisplacementSeconds = 0;
                _pendingDisplacementTick = tick;
            }
            _pendingDisplacement += direction.Normalized() * distance;
            _pendingDisplacementLimit = Math.Max(_pendingDisplacementLimit, distance);
            _pendingDisplacementSeconds = Math.Max(_pendingDisplacementSeconds, seconds);
        }

        /// <summary>지난 틱의 합성 벡터로 기존 밀기를 교체하고 이번 시간 간격의 이동량을 반환한다. 같은 틱의 예약은 이동시키지 않는다.</summary>
        internal bool TryAdvanceDisplacement(int tick, double seconds, out SimVector displacement)
        {
            if (_pendingDisplacementTick >= 0 && _pendingDisplacementTick < tick)
                CommitDisplacement();
            displacement = _displacementVelocity * Math.Min(seconds, _displacementRemaining);
            bool hasDisplacement = _displacementRemaining > 0;
            _displacementRemaining = Math.Max(0, _displacementRemaining - seconds);
            if (_displacementRemaining == 0) _displacementVelocity = SimVector.Zero;
            return hasDisplacement;
        }

        /// <summary>예약 벡터를 최대 단일 거리로 제한해 현재 강제 이동을 교체하고 예약을 비운다.</summary>
        private void CommitDisplacement()
        {
            double length = _pendingDisplacement.Length;
            SimVector vector = length > _pendingDisplacementLimit
                ? _pendingDisplacement / length * _pendingDisplacementLimit : _pendingDisplacement;
            _displacementVelocity = vector / _pendingDisplacementSeconds;
            _displacementRemaining = length > RuneSimulation.COLLISION_EPSILON ? _pendingDisplacementSeconds : 0;
            _pendingDisplacementTick = -1;
            _pendingDisplacement = SimVector.Zero;
            _pendingDisplacementLimit = 0;
            _pendingDisplacementSeconds = 0;
        }

        /// <summary>진행 중인 강제 이동과 아직 적용하지 않은 합성 예약을 모두 취소한다.</summary>
        internal void CancelDisplacement()
        {
            _pendingDisplacement = SimVector.Zero;
            _pendingDisplacementLimit = 0;
            _pendingDisplacementSeconds = 0;
            _pendingDisplacementTick = -1;
            _displacementVelocity = SimVector.Zero;
            _displacementRemaining = 0;
        }

        /// <summary>입력 피해만큼 개별 보호막 체력을 줄이고 실제 감소량을 반환한다. 파괴된 보호막은 재생하지 않는다.</summary>
        internal double HurtShield(double damage)
        { double actual = Math.Min(_shieldHp, Math.Max(0, damage)); _shieldHp -= actual; return actual; }

        /// <summary>피해를 체력에 적용하고 실제 감소량을 반환하며 피격 표시 시간을 설정한다.</summary>
        internal double Hurt(double damage, double time) { double actual = Math.Min(_hp, Math.Max(0, damage)); _hp -= actual; _flashUntil = time + _hitFlashSeconds; return actual; }

        /// <summary>체력을 회복량만큼 올리고 최대 체력을 넘지 않게 하며 실제 회복량을 반환한다.</summary>
        internal double Heal(double amount) { double actual = Math.Min(_maxHp - _hp, Math.Max(0, amount)); _hp += actual; return actual; }

        /// <summary>보스 페이즈를 변경하고 패치 무적 만료 시간을 설정한다.</summary>
        internal void Patch(int phase, double time, double duration) { _phase = phase; _patchUntil = time + duration; }

        /// <summary>실제로 받은 최근 피해의 속성과 형태를 기록하여 해당 적의 내성 아이콘에 사용한다.</summary>
        internal void SetDamageTags(string element, string form) { _lastDamageElement = element; _lastDamageForm = form; }

        /// <summary>적의 체력, 이동, 공격 예약, 표시 만료 및 페이즈를 결정성 해시 버퍼에 기록한다. 상태 이상은 시뮬레이션이 따로 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        {
            state.Append(_id).Append('|').Append(Kind).Append('|').Append(_isDummy).Append('|').Append(_isElite).Append('|').Append(_speedMultiplier);
            _hydra?.WriteState(state);
            _hexa?.WriteState(state);
            if (_hexa != null && (_hexa.HasStartedSniper || _hexa.HasStartedComposite))
                state.Append(FormattableString.Invariant($"|hexaAim:{_attackOrigin.X:R}:{_attackOrigin.Y:R}:{_attackDirection.X:R}:{_attackDirection.Y:R}:{_aimUntil:R}:{_warningUntil:R}"));
            if (_boss != null && _boss.IsMiniGovernorPattern) state.Append('|').Append(_isNextBossBurst);
            _legolas?.WriteState(state);
            if (_pendingDisplacementTick >= 0 || _displacementRemaining > 0)
                state.Append(FormattableString.Invariant($"|displacement:{_pendingDisplacementTick}:{_pendingDisplacement.X:R}:{_pendingDisplacement.Y:R}:{_pendingDisplacementLimit:R}:{_pendingDisplacementSeconds:R}:{_displacementVelocity.X:R}:{_displacementVelocity.Y:R}:{_displacementRemaining:R}"));
            state.Append(FormattableString.Invariant($"|{_damage:R}|{_damageMultiplier:R}|{_reward}"));
            if (_summonerId != 0) state.Append("|summoner:").Append(_summonerId);
            state.Append('|').Append(_lastDamageElement).Append('|').Append(_lastDamageForm);
            state.Append(FormattableString.Invariant($"|{_position.X:R}|{_position.Y:R}|{_facing.X:R}|{_facing.Y:R}|{_hp:R}|{_maxHp:R}|{_shieldHp:R}|{_attackAt:R}|{_warningUntil:R}|{_contactGraceUntil:R}|{_dashUntil:R}|{_attackDirection.X:R}|{_attackDirection.Y:R}|{_flashUntil:R}|{_phase}|{_patchUntil:R}|{_reinforcementAt:R}|{_hazardAt:R}|{_observedTime:R}"));
            if (_definition.Attack == EnemyDefinition.ATTACK_SNIPER || _definition.Attack == EnemyDefinition.ATTACK_INTERCEPTOR || _definition.HasTrait(EnemyDefinition.TRAIT_CARRIER) || _carrierOwnerId != 0)
                state.Append(FormattableString.Invariant($"|extra:{_attackOrigin.X:R}:{_attackOrigin.Y:R}:{_aimUntil:R}:{_behaviorRemaining:R}:{_isStopping}:{_carrierOwnerId}:{_spawnCursor}:{_spawnRemaining}:{_spawnRemainingSeconds:R}"));
        }
    }

    public sealed class SimulationSpellEntity
    {
        private static readonly object[] NO_MODIFIER_STATES = new object[0];
        private static readonly int[] NO_MOTION_ORDER = new int[0];

        private readonly int _id;
        private readonly SpellAction _action;
        private readonly SpellStats _stats;
        private readonly string _element;
        private readonly string _castNoiseElement;
        private readonly bool _fromEvent;
        private readonly double _visualSeconds;
        private readonly double _warmupSeconds;
        private readonly SimVector _anchor;
        private readonly double _angle;
        private readonly SpellModifierValues _modifiers;
        private readonly double _costMultiplier;
        private readonly double _castDamageMultiplier;
        private readonly SimulationPlayer _caster;
        private readonly SpellBehaviorRuntime _behavior;
        private readonly object _behaviorState;
        private readonly SpellModifierRuntime[] _modifierRuntimes;
        private readonly object[] _modifierStates;
        private readonly int[] _motionOrder;
        private readonly bool _hasModifierPath;
        private readonly Dictionary<int, double> _hitTimes = new Dictionary<int, double>();
        private SimVector _position;
        private SimVector _previousPosition;
        private SimVector _previousDirection;
        private bool _isPassingWalls;
        private SimVector _direction;
        private double _age;
        private int _hits;
        private bool _hasExpired;
        private bool _hasFiredFirstEvent;
        private bool _hasDirectHit;
        public int Id => _id;
        public string Kind => _action.Form;
        public string Element => _element;
        public SimVector Position => _position;
        public SimVector Direction => _direction;
        /// <summary>현재 판정 반경이다. 행동 실행 동작이 정한다(Burst는 확장 시간 동안 0에서 최종 반경까지 커진다).</summary>
        public double Radius => _behavior.GetRadius(this);
        /// <summary>과충전 진행률을 반영한 빔 판정·표시 폭이다. 다른 행동은 원래 폭을 유지한다.</summary>
        public double BoxWidth => _stats.BoxWidth * (1 + (_action.BeamEmpowerSizeMultiplier - 1) * BeamChargeProgress);

        /// <summary>Beam 범위의 현재 길이다. 행동의 확장 진행률을 곱한다(Burst는 확장 시간 동안 0에서 최종 길이까지 늘어난다).</summary>
        public double BoxLength => _stats.BoxLength * _behavior.GetExpansionProgress(this);

        /// <summary>Beam 범위의 중심이다. 뒤쪽 면을 개체 위치에 두는 행동(Burst·Persist)은 진행 방향으로 현재 길이의 절반 앞이고, 그 외는 개체 위치다.</summary>
        public SimVector BoxCenter => _behavior.PlacesBoxBackAtPosition
            ? _position + _direction.Normalized() * (BoxLength * 0.5) : _position;

        /// <summary>활성화 전 예고 중인지 나타낸다(Persist). 예고 중에는 효과·적중 판정을 하지 않는다.</summary>
        public bool IsWarning => _behavior.IsWarning(this);

        /// <summary>예고 진행률(0~1)이다. 예고가 없거나 끝났으면 1이다.</summary>
        public double WarnProgress => _behavior.GetWarnProgress(this);

        /// <summary>범위가 아직 확장 중이라 새 대상을 판정하는지 나타낸다(Burst).</summary>
        internal bool IsExpanding => _behavior.IsExpanding(this);
        public bool IsBox => _action.MagicType == SpellGrammar.MAGIC_TYPE_BOX;

        /// <summary>부채꼴인지 나타낸다. 판정·그리기는 꼭짓점(ConeApex)에서 진행 방향으로 펼친 부채꼴이다.</summary>
        public bool IsCone => _action.MagicType == SpellGrammar.MAGIC_TYPE_CONE;

        /// <summary>생성 위치를 중심으로 원형 파동이 확장하는 폭발 투사체인지 반환한다.</summary>
        public bool IsExpandingRing => _action.Rules?.DisplayType == SpellDisplayType.ExpandingRing;

        /// <summary>중심을 따라 회전하는 참격 개체인지 반환한다.</summary>
        public bool IsSlash => _action.Form == SlashBehaviorRules.KIND;

        /// <summary>참격의 판정과 표시가 공유하는 벽으로 제한된 현재 칼날 중심선을 반환한다.</summary>
        public IReadOnlyList<SimVector> SlashPath => _behaviorState is SlashBehaviorRuntime.SlashState slash
            ? slash.Path : Array.Empty<SimVector>();

        /// <summary>참격의 마지막 판정 회전 진행률을 반환한다. 표시는 아직 지나가지 않은 범위에 잔상을 만들지 않는다.</summary>
        public double SlashProgress => _behaviorState is SlashBehaviorRuntime.SlashState slash ? slash.Progress : 0;

        /// <summary>폭발의 생성 후 시간과 확장·속도 배율을 반영한 파동 반경이다.</summary>
        public double ExplosionRadius => _stats.Speed * _age;

        /// <summary>확장 배율에 영향받지 않는 폭발의 고정 전체 각도다.</summary>
        public double ExplosionAngle => _action.ConeAngle;

        /// <summary>호 중심선의 두께다. 기본 발사체 지름에 기존 확장 배율을 적용한다.</summary>
        public double ExplosionThickness => _stats.Radius * 2;

        /// <summary>현재 호의 원 중심이다. 투사체 위치는 호의 정면 중앙점이다.</summary>
        public SimVector ExplosionCenter => _position - _direction * ExplosionRadius;

        /// <summary>호의 왼쪽 끝 0부터 오른쪽 끝 1까지의 중심선 위치를 현재 표시 시점 기준으로 반환한다.</summary>
        public SimVector GetExplosionPoint(double fraction)
            => ExplosionCenter + _direction.Rotated((Math.Max(0, Math.Min(1, fraction)) - 0.5) * ExplosionAngle * Math.PI / 180) * ExplosionRadius;

        /// <summary>부채꼴의 전체 각도(도)다.</summary>
        public double ConeAngle => _action.ConeAngle;

        /// <summary>부채꼴의 꼭짓점이다. 발사체는 중심에서 진행 반대쪽으로 반경만큼 뒤, 범위는 개체 위치다.</summary>
        public SimVector ConeApex => _behavior.GetConeApex(this);

        /// <summary>부채꼴의 반경이다. 발사체는 지름(중심 앞뒤로 반경씩), 범위는 개체 반경이다.</summary>
        public double ConeReach => _behavior.GetConeReach(this);
        public double Age => _age;
        /// <summary>
        /// 개체 수명이다. 행동 실행 동작이 정한다(Burst는 확장 시간 + 잔상 시간, Persist는 예고 시간 + 유지 시간, 그 외는 유지 시간).
        /// </summary>
        public double Lifetime => _behavior.GetLifetime(this);
        public string NodeId => _action.NodeId;
        internal SpellAction Action => _action;
        internal SpellStats Stats => _stats;

        /// <summary>이 개체의 행동 실행 동작이다. 등록되지 않은 종류는 기본 동작이다.</summary>
        internal SpellBehaviorRuntime Behavior => _behavior;

        /// <summary>행동 실행 동작이 생성 시 만든 행동 전용 상태다. 없으면 null이다.</summary>
        internal object BehaviorState => _behaviorState;

        /// <summary>장착된 동작 모드의 실행 동작 목록이다. 등록 순서이며 동작 모드가 없으면 비어 있다.</summary>
        internal IReadOnlyList<SpellModifierRuntime> ModifierRuntimes => _modifierRuntimes;

        /// <summary>ModifierRuntimes의 index번째 동작 모드가 생성 시 만든 전용 상태를 반환한다. 상태가 없는 모드는 null이다.</summary>
        internal object GetModifierState(int index) => _modifierStates[index];

        /// <summary>이번 틱 이동 전 위치다. 매 틱 이동 단계 직전에 기록하며 판정이 이동 구간의 시작점으로 쓴다.</summary>
        internal SimVector PreviousPosition => _previousPosition;
        internal SimVector PreviousDirection => _previousDirection;

        /// <summary>이번 틱 이동이 벽을 무시하는 경로(공전 궤도·진입)였는지 나타낸다. 발사 판정이 벽 충돌을 건너뛴다. 매 틱 이동 전에 꺼진다.</summary>
        internal bool IsPassingWalls => _isPassingWalls;

        /// <summary>행동의 기본 경로를 대신하는 경로 모드(공전)가 장착되어 있는지 나타낸다.</summary>
        internal bool HasModifierPath => _hasModifierPath;

        /// <summary>빔 표시와 충돌이 공유하는 중심선이다. 빔이 아닌 개체는 빈 목록을 반환한다.</summary>
        public IReadOnlyList<SimVector> BeamPath => _behaviorState is BeamBehaviorRuntime.BeamState beam
            ? beam.Path : Array.Empty<SimVector>();

        /// <summary>같은 빔 채널의 강화 시작부터 최대 충전까지의 진행률이다. 빔이 아니거나 종료되면 0이다.</summary>
        public double BeamChargeProgress => _behaviorState is BeamBehaviorRuntime.BeamState beam ? beam.Channel.EmpowerProgress : 0;

        public bool IsBeamEmpowered => BeamChargeProgress > 0;
        public double BeamDamageMultiplier => 1 + (_action.BeamEmpowerMultiplier - 1) * BeamChargeProgress;

        /// <summary>판정 전 대기 시간이다. Burst의 확장 시간 또는 Persist의 예고 시간이며 그 외 행동은 0이다.</summary>
        internal double WarmupSeconds => _warmupSeconds;

        /// <summary>판정이 끝난 뒤 남는 잔상 표시 시간이다.</summary>
        internal double VisualSeconds => _visualSeconds;
        internal bool FromEvent => _fromEvent;
        internal string CastNoiseElement => _castNoiseElement;
        internal SpellModifierValues Modifiers => _modifiers;

        /// <summary>이 개체의 이벤트 분기 노드 비용에 곱하는 배율(개체를 만든 문맥의 배율)이다.</summary>
        internal double CostMultiplier => _costMultiplier;

        /// <summary>생성한 시전 문맥의 시전 시점 피해 배율이다. 이벤트 분기 문맥에 그대로 넘긴다.</summary>
        internal double CastDamageMultiplier => _castDamageMultiplier;

        /// <summary>이 개체를 만든 시전자다. 개체 이벤트로 시작한 후속 실행에 전달한다.</summary>
        internal SimulationPlayer Caster => _caster;
        internal SimVector Anchor => _anchor;
        internal Dictionary<int, double> HitTimes => _hitTimes;
        internal int Hits { get => _hits; set => _hits = value; }
        /// <summary>같은 실행에서 생성된 개체 중 순번에 따른 각도(순번 × 360° ÷ 개수, 라디안)다. 공전 모드가 궤도 자리를 등각으로 나누는 데 쓴다.</summary>
        internal double Angle => _angle;
        internal bool HasExpired { get => _hasExpired; set => _hasExpired = value; }

        /// <summary>발사체가 적·벽·지형에 한 번이라도 직접 충돌했는지 나타낸다. 직접 충돌한 발사체는 OnExpire를 내지 않는다.</summary>
        internal bool HasDirectHit { get => _hasDirectHit; set => _hasDirectHit = value; }

        /// <summary>부채꼴 발사체 중심(center)과 진행 방향, 반경으로 꼭짓점(중심에서 반경만큼 뒤)을 반환한다.</summary>
        internal static SimVector GetConeApex(SimVector center, SimVector direction, double radius) => center - direction.Normalized() * radius;

        /// <summary>
        /// 컴파일된 Form 수치와 이벤트 분기의 비용 배율을 가진 독립 마법 개체를 생성한다.
        /// warmupSeconds는 Burst의 확장 시간 또는 Persist의 예고 시간이며 그 외 형태는 0이다.
        /// </summary>
        internal SimulationSpellEntity(int id, SpellAction action, SpellStats stats, string element, SimVector position,
            SimVector direction, bool fromEvent, SimVector anchor, double angle, string castNoiseElement,
            double visualSeconds, double warmupSeconds, SpellModifierValues modifiers, double costMultiplier, SimulationPlayer caster,
            double castDamageMultiplier = 1d)
        {
            _warmupSeconds = warmupSeconds;
            _costMultiplier = costMultiplier;
            _castDamageMultiplier = castDamageMultiplier;
            _caster = caster;
            _id = id;
            _action = action;
            _stats = stats;
            _element = element;
            _castNoiseElement = castNoiseElement;
            _position = position;
            _direction = direction;
            _fromEvent = fromEvent;
            _visualSeconds = visualSeconds;
            _anchor = anchor;
            _angle = angle;
            _modifiers = modifiers ?? SpellModifierValues.None;
            _behavior = SpellBehaviorRuntimeRegistry.Get(action.Form);
            _behaviorState = _behavior.CreateState(this);
            _modifierRuntimes = SpellModifierRuntimeRegistry.Resolve(action.ModifierRules);
            _modifierStates = _modifierRuntimes.Length == 0 ? NO_MODIFIER_STATES : new object[_modifierRuntimes.Length];
            for (int index = 0; index < _modifierRuntimes.Length; index++) _modifierStates[index] = _modifierRuntimes[index].CreateState(this);
            _motionOrder = BuildMotionOrder(action.ModifierRules);
            foreach (int index in _motionOrder) _hasModifierPath |= _modifierRuntimes[index].ReplacesBasePath;
            _previousPosition = position;
            _previousDirection = direction;
        }

        /// <summary>스펠 개체의 위치와 진행 방향을 갱신한다.</summary>
        internal void Move(SimVector position, SimVector direction) { _position = position; _direction = direction; }

        /// <summary>이번 틱 이동 전 위치를 현재 위치로 기록하고 벽 무시 표시를 끈다. 이동 단계 직전에 한 번 호출한다.</summary>
        internal void MarkPreviousPosition() { _previousPosition = _position; _previousDirection = _direction; _isPassingWalls = false; }

        /// <summary>이번 틱 이동이 벽을 무시하는 경로였음을 표시한다. 이동 모드가 이동할 때 호출한다.</summary>
        internal void MarkPassingWalls() { _isPassingWalls = true; }

        /// <summary>
        /// 장착된 경로 모드가 모두 반응 모드에 이동을 넘길 수 있는 상태인지 반환한다(공전 진입 완료).
        /// 경로 모드가 없으면 true다.
        /// </summary>
        internal bool CanLeaveModifierPath()
        {
            foreach (int index in _motionOrder)
            {
                SpellModifierRuntime runtime = _modifierRuntimes[index];
                if (runtime.ReplacesBasePath && !runtime.CanYieldMove(this, _modifierStates[index])) return false;
            }
            return true;
        }

        /// <summary>
        /// 이동 모드를 우선순위가 높은 순서로 돌며 처음 이동을 맡은 모드만 개체를 옮기게 하고, 나머지 이동 모드는 상태만 갱신한다.
        /// 어느 이동 모드가 이동을 맡았으면 true를 반환하며, 이 경우 행동의 기본 경로 이동을 건너뛴다.
        /// </summary>
        internal bool MoveByModifiers(SpellRuntimeContext context)
        {
            bool hasMoved = false;
            foreach (int index in _motionOrder)
            {
                SpellModifierRuntime runtime = _modifierRuntimes[index];
                if (!hasMoved && runtime.TryMove(context, this, _modifierStates[index])) hasMoved = true;
                else runtime.UpdatePassive(context, this, _modifierStates[index]);
            }
            return hasMoved;
        }

        /// <summary>
        /// 적 적중 한 번에 장착된 동작 모드의 적중 개입을 등록 순서로 적용한다. damage·isConsumed는 행동이 넣은 기본값에서 시작한다.
        /// </summary>
        internal void ApplyHitModifiers(SpellRuntimeContext context, SimulationEnemy enemy, ref double damage, ref bool isConsumed)
        {
            for (int index = 0; index < _modifierRuntimes.Length; index++)
                _modifierRuntimes[index].OnHit(context, this, _modifierStates[index], enemy, ref damage, ref isConsumed);
        }

        /// <summary>경로형 행동이 이번 틱 경로를 새로 그리기 시작할 때 이동 모드들에 알린다(우선순위 순).</summary>
        internal void BeginPath(SpellRuntimeContext context)
        {
            foreach (int index in _motionOrder) _modifierRuntimes[index].BeginPath(context, this, _modifierStates[index]);
        }

        /// <summary>
        /// 경로형 행동이 경로 한 칸을 그리기 전에 이동 모드를 우선순위 순으로 물어 처음 맡은 모드만 그 칸의 진행 방향을 고치게 한다.
        /// point는 칸 시작점, step은 칸 길이, pathSpeed는 경로 기준 속도, halfWidth는 경로 반폭이다.
        /// </summary>
        internal void SteerPathStep(SpellRuntimeContext context, SimVector point, ref SimVector direction, double step,
            double pathSpeed, double halfWidth)
        {
            foreach (int index in _motionOrder)
                if (_modifierRuntimes[index].SteerPathStep(context, this, _modifierStates[index], point, ref direction, step, pathSpeed, halfWidth))
                    return;
        }

        /// <summary>
        /// 동작 모드 규칙 목록(실행 목록과 같은 순서)에서 이동 모드의 순번만 골라 우선순위가 높은 순서로 정렬한 배열을 반환한다.
        /// 우선순위가 같으면 등록 순서를 유지한다. 이동 모드가 없으면 공유 빈 배열을 반환한다.
        /// </summary>
        private static int[] BuildMotionOrder(IReadOnlyList<SpellModifierRules> rules)
        {
            if (rules.Count == 0) return NO_MOTION_ORDER;
            var order = new List<int>();
            for (int index = 0; index < rules.Count; index++)
            {
                if (rules[index].Category != SpellModifierCategory.Motion) continue;
                int position = order.Count;
                while (position > 0 && rules[order[position - 1]].MotionPriority < rules[index].MotionPriority) position--;
                order.Insert(position, index);
            }
            return order.Count == 0 ? NO_MOTION_ORDER : order.ToArray();
        }

        /// <summary>
        /// 적중·소멸 중 첫 이벤트인지 확인하고 기록한다. 처음 호출이면 true, 이후 호출은 false를 반환한다.
        /// onFirstHitOrExpire 분기를 개체당 한 번만 실행하는 데 사용한다.
        /// </summary>
        internal bool TryMarkFirstEvent()
        {
            if (_hasFiredFirstEvent)
            {
                return false;
            }
            _hasFiredFirstEvent = true;
            return true;
        }

        /// <summary>스펠 개체의 수명 경과를 고정 시간만큼 증가시킨다.</summary>
        internal void Advance(double dt) { _age += dt; }

        /// <summary>마법의 이동, 앵커, 수명, 적중 대상 및 이벤트 실행 상태를 결정성 해시 버퍼에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        {
            state.Append(_id).Append('|').Append(_action.NodeId).Append('|').Append(_element).Append('|').Append(_castNoiseElement).Append('|').Append(_fromEvent);
            state.Append(FormattableString.Invariant($"|{_anchor.X:R}|{_anchor.Y:R}|{_position.X:R}|{_position.Y:R}|{_direction.X:R}|{_direction.Y:R}|{_age:R}|{_angle:R}|{_hits}|{_hasExpired}|{_hasFiredFirstEvent}|{_hasDirectHit}|{_costMultiplier:R}|{_castDamageMultiplier:R}"));
            state.Append(FormattableString.Invariant($"|{_stats.Damage:R}|{_stats.Speed:R}|{_stats.Radius:R}|{_stats.Lifetime:R}|{_stats.Pierce}|{_stats.HomingTurn:R}|{_stats.HomingRange:R}|{_stats.OrbitTargetCount}"));
            if (_stats.KnockbackDistance > 0)
                state.Append(FormattableString.Invariant($"|knockback:{_stats.KnockbackDistance:R}:{_stats.KnockbackSeconds:R}"));
            _modifiers.AppendState(state);
            var ids = new List<int>(_hitTimes.Keys); ids.Sort();
            foreach (int id in ids) state.Append(FormattableString.Invariant($"|{id}:{_hitTimes[id]:R}"));
            _behavior.WriteState(this, state);
            if (IsExpandingRing) state.Append(FormattableString.Invariant($"|explosionPrevious:{_previousPosition.X:R}:{_previousPosition.Y:R}:{_previousDirection.X:R}:{_previousDirection.Y:R}:{ExplosionAngle:R}:{_stats.RadiusMultiplier:R}"));
            for (int index = 0; index < _modifierRuntimes.Length; index++) _modifierRuntimes[index].WriteState(this, _modifierStates[index], state);
        }
    }

    /// <summary>적 탄환의 표시 색상 구분이다. 기본은 기존 탄환 색이며 Hexa의 유도·Sniper 탄환만 별도 색을 사용한다.</summary>
    public enum HostileProjectileStyle { Default, Homing, Sniper }

    public sealed class SimulationProjectile
    {
        private readonly int _sourceEnemyId;
        private readonly double _speed;
        private readonly double _damage;
        private readonly bool _isExplosion;
        private readonly double _radius;
        private readonly double _lifetime;
        private readonly bool _isHazard;
        private readonly double _warning;
        private readonly double _homingTurnRadiansPerSecond;
        private readonly double _homingSeconds;
        private readonly HostileProjectileStyle _style;
        private readonly SimulationEnemy _owner;
        private readonly string _sourcePattern;
        private SimVector _direction;
        private SimVector _position;
        private double _age;
        public SimVector Position => _position;
        public SimVector Direction => _direction;
        public double Radius => _radius;
        public bool IsExplosion => _isExplosion;
        /// <summary>이 탄환을 발사한 보스 정의다. 소유자가 없거나 일반 적이면 null이다.</summary>
        public BossDefinition OwnerBoss => _owner?.Boss;
        public double Age => _age;
        public bool IsHazard => _isHazard;
        public bool IsWarning => _isHazard && _age < _warning;
        public double WarningProgress => _warning <= 0 ? 1 : Math.Min(1, _age / _warning);
        internal string SourcePattern => _sourcePattern;
        internal int SourceEnemyId => _sourceEnemyId;
        internal double Damage => _damage;
        internal double Lifetime => _lifetime;
        internal bool IsHoming => _homingTurnRadiansPerSecond > 0 && _homingSeconds > 0;
        public HostileProjectileStyle Style => _style;
        internal bool HasDeadOwner => _owner != null && !_owner.IsAlive;

        /// <summary>위치·공격 설정과 피해 귀속용 적 ID로 탄환 또는 예고 장판을 만들며, 유도 속도·지속 시간·표시 스타일과 선택적 소유자·원래 보스 패턴을 함께 보관한다. 소유자가 있으면 해당 ID를 사용하고 사망 시 정리한다.</summary>
        internal SimulationProjectile(SimVector position, SimVector direction, double speed, double damage, double radius, double lifetime, bool isHazard = false, double warning = 0, bool isExplosion = false, int sourceEnemyId = 0, double homingTurnDegreesPerSecond = 0, double homingSeconds = 0, HostileProjectileStyle style = HostileProjectileStyle.Default, SimulationEnemy owner = null, string sourcePattern = null)
        { _sourcePattern = sourcePattern; _sourceEnemyId = owner != null ? owner.Id : sourceEnemyId; _position = position; _direction = direction; _speed = speed; _damage = damage; _radius = radius; _lifetime = lifetime; _isHazard = isHazard; _warning = warning; _isExplosion = isExplosion; _homingTurnRadiansPerSecond = homingTurnDegreesPerSecond * RuneSimulation.DEGREES_TO_RADIANS; _homingSeconds = Math.Max(0, homingSeconds); _style = style; _owner = owner; }

        /// <summary>유도 탄환이면 유도 시간 안에서 목표 방향으로 최대 회전량만큼 최단 호로 방향을 보정한 뒤 위치와 수명을 고정 시간만큼 진행한다.</summary>
        internal void Advance(double dt, SimVector? homingTarget = null)
        {
            if (homingTarget.HasValue) SteerToward(homingTarget.Value, dt);
            _age += dt;
            _position += _direction * (_speed * dt);
        }

        /// <summary>현재 방향을 목표 위치 방향으로 남은 유도 시간과 초당 회전 상한 안에서 최단 호로 돌린다. 방향이나 목표 오프셋이 0이면 유지한다.</summary>
        private void SteerToward(SimVector target, double dt)
        {
            if (!IsHoming || dt <= 0) return;
            double remaining = _homingSeconds - _age;
            if (remaining <= 0) return;
            SimVector offset = target - _position;
            if (offset.LengthSquared <= 0.000001) return;
            SimVector targetDirection = offset.Normalized();
            SimVector currentDirection = _direction.LengthSquared > 0.000001 ? _direction.Normalized() : targetDirection;
            double maxRadians = _homingTurnRadiansPerSecond * Math.Min(dt, remaining);
            double cross = currentDirection.X * targetDirection.Y - currentDirection.Y * targetDirection.X;
            double angle = Math.Atan2(cross, SimVector.Dot(currentDirection, targetDirection));
            _direction = Math.Abs(angle) <= maxRadians ? targetDirection : currentDirection.Rotated(Math.Sign(angle) * maxRadians);
        }

        /// <summary>적 탄환의 이동, 예고, 피해, 수명과 사망 정리에 사용하는 소유자 ID 및 유도 설정을 결정성 해시 버퍼에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        { state.Append(FormattableString.Invariant($"|{_position.X:R}|{_position.Y:R}|{_direction.X:R}|{_direction.Y:R}|{_speed:R}|{_damage:R}|{_radius:R}|{_lifetime:R}|{_isHazard}|{_warning:R}|{_age:R}|source:{_sourceEnemyId}")); if (IsHoming) state.Append(FormattableString.Invariant($"|homing:{_homingTurnRadiansPerSecond:R}:{_homingSeconds:R}")); if (_isExplosion) state.Append("|explosion"); if (_owner != null) state.Append(FormattableString.Invariant($"|owner:{_owner.Id}:{_owner.IsAlive}")); }
    }

    public struct FragmentOrb
    {
        private SimVector _position;
        private readonly int _amount;
        private readonly bool _isBigScrap;
        private bool _isAttracted;
        public SimVector Position => _position;
        public int Amount => _amount;
        public bool IsBigScrap => _isBigScrap;
        public bool IsAttracted => _isAttracted;
        /// <summary>처치 위치·스크랩 양과 강력한 적 드롭 여부를 보관한 회수 오브를 생성한다.</summary>
        internal FragmentOrb(SimVector position, int amount, bool isBigScrap = false)
        { _position = position; _amount = amount; _isBigScrap = isBigScrap; _isAttracted = false; }

        /// <summary>자석 이동이 시작된 오브의 위치를 갱신하고 추적 상태를 유지한다.</summary>
        internal void AttractTo(SimVector position) { _position = position; _isAttracted = true; }
    }

    /// <summary>바닥에 떨어져 플레이어가 줍기를 기다리는 Modifier 드롭 개체다. 등급은 C·B·A·S 문자열이며 등급 없이 드롭된 항목은 null이다.</summary>
    public sealed class SimulationItemDrop
    {
        private readonly string _runeId;
        private readonly string _grade;
        private readonly int _count;
        private readonly int _spawnTick;
        private SimVector _position;
        private bool _isAttracted;
        public string RuneId => _runeId;
        public string Grade => _grade;
        public int Count => _count;
        public int SpawnTick => _spawnTick;
        public SimVector Position => _position;
        public bool IsAttracted => _isAttracted;

        /// <summary>Modifier 룬 ID, 등급 문자, 수량, 생성 시점 틱과 바닥 위치로 드롭 개체를 생성한다.</summary>
        internal SimulationItemDrop(string runeId, string grade, int count, int spawnTick, SimVector position)
        { _runeId = runeId; _grade = grade; _count = count; _spawnTick = spawnTick; _position = position; }

        /// <summary>자석 이동이 시작된 Modifier의 위치를 갱신하고 추적 상태를 유지한다.</summary>
        internal void AttractTo(SimVector position) { _position = position; _isAttracted = true; }
    }

    /// <summary>실제 공격 수명과 분리해 빔의 최초 판정 경로·폭·속성·생성 틱을 보관한다.</summary>
    public sealed class SimulationBeamFlash
    {
        private readonly int _entityId;
        private readonly int _tick;
        private readonly double _width;
        private readonly string _element;
        private readonly SimVector[] _path;

        public int EntityId => _entityId;
        public int Tick => _tick;
        public double Width => _width;
        public string Element => _element;
        public IReadOnlyList<SimVector> Path => _path;

        /// <summary>빔의 현재 경로를 복사해 개체 소멸·다음 경로 갱신과 무관한 표시 기록을 만든다.</summary>
        internal SimulationBeamFlash(SimulationSpellEntity entity, IReadOnlyList<SimVector> path, int tick)
        {
            _entityId = entity.Id;
            _tick = tick;
            _width = entity.BoxWidth;
            _element = entity.Element;
            _path = new SimVector[path.Count];
            for (int index = 0; index < path.Count; index++) _path[index] = path[index];
        }
    }

    public readonly struct DamageNumber
    {
        private readonly SimVector _position;
        private readonly double _amount;
        private readonly string _element;
        private readonly int _tick;
        public SimVector Position => _position;
        public double Amount => _amount;
        public string Element => _element;
        public int Tick => _tick;
        /// <summary>피해 발생 위치, 수치, 속성 및 틱을 표시용 기록으로 보관한다.</summary>
        internal DamageNumber(SimVector position, double amount, string element, int tick) { _position = position; _amount = amount; _element = element; _tick = tick; }
    }

    public readonly struct ArcLink
    {
        private readonly SimVector _from;
        private readonly SimVector _to;
        private readonly int _tick;
        public SimVector From => _from;
        public SimVector To => _to;
        public int Tick => _tick;
        /// <summary>전격 연쇄가 이어진 두 위치와 틱을 표시용 기록으로 보관한다.</summary>
        internal ArcLink(SimVector from, SimVector to, int tick) { _from = from; _to = to; _tick = tick; }
    }

    public readonly struct HealPulse
    {
        private readonly SimVector _position;
        private readonly int _tick;
        public SimVector Position => _position;
        public int Tick => _tick;
        /// <summary>적이 실제로 체력을 회복한 위치와 틱을 표시용 기록으로 보관한다.</summary>
        internal HealPulse(SimVector position, int tick) { _position = position; _tick = tick; }
    }

    public readonly struct SpawnWarning
    {
        private readonly SimVector _position;
        private readonly double _radius;
        private readonly double _progress;
        private readonly bool _isElite;
        public SimVector Position => _position;
        public double Radius => _radius;

        /// <summary>예고 시작 0에서 생성 직전 1까지의 진행도다.</summary>
        public double Progress => _progress;
        public bool IsElite => _isElite;

        /// <summary>맵 좌표 중심, 표시 반경, 진행도(0~1), 엘리트 포함 여부로 스폰 예고 표시 정보를 만든다.</summary>
        internal SpawnWarning(SimVector position, double radius, double progress, bool isElite)
        { _position = position; _radius = radius; _progress = progress; _isElite = isElite; }
    }

    public readonly struct NodeExecutionEvent
    {
        private readonly string _nodeId;
        private readonly int _tick;
        public string NodeId => _nodeId;
        public int Tick => _tick;
        /// <summary>실행된 노드 ID와 시뮬레이션 틱을 하이라이트 및 통계용으로 보관한다.</summary>
        internal NodeExecutionEvent(string nodeId, int tick) { _nodeId = nodeId; _tick = tick; }
    }
}
