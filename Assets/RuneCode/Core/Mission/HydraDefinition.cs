using System;

using UnityEngine;

namespace RuneCode
{
    /// <summary>Hydra의 공격 시간·범위·피해와 체력 비례 피해 예산을 보관하는 공유 설정이다.</summary>
    [Serializable]
    public sealed class HydraDefinition
    {
        [Header("전투")]
        [SerializeField] private double _initialDelay;
        [SerializeField] private double _attackCooldown;
        [SerializeField] private double _burnMultiplier;
        [SerializeField] private double _dpsHealthRatio;
        [SerializeField] private double _dpsWindow;
        [SerializeField] private double _excessDamageMultiplier;
        public double InitialDelay => _initialDelay;
        public double AttackCooldown => _attackCooldown;
        public double BurnMultiplier => _burnMultiplier;
        public double DpsHealthRatio => _dpsHealthRatio;
        public double DpsWindow => _dpsWindow;
        public double ExcessDamageMultiplier => _excessDamageMultiplier;

        [Header("목 공격")]
        [SerializeField] private double _neckWidth;
        [SerializeField] private double _neckDamage;
        [SerializeField] private double _stretchWarning;
        [SerializeField] private double _stretchSeconds;
        [SerializeField, Tooltip("목을 최대로 뻗은 뒤 설정한 회전각만큼 회전하는 시간입니다.")] private double _stretchHold;
        [SerializeField] private double _stretchRotationDegrees;
        [SerializeField] private double _stretchRetractSeconds;
        [SerializeField] private double _retractSeconds;
        public double NeckWidth => _neckWidth;
        public double NeckDamage => _neckDamage;
        public double StretchWarning => _stretchWarning;
        public double StretchSeconds => _stretchSeconds;
        public double StretchHold => _stretchHold;
        public double StretchRotationDegrees => _stretchRotationDegrees;
        public double StretchRetractSeconds => _stretchRetractSeconds;
        public double RetractSeconds => _retractSeconds;

        [Header("독 공격")]
        [SerializeField] private double _poisonTrack;
        [SerializeField] private double _poisonLock;
        [SerializeField] private double _poisonOrbSpeed;
        [SerializeField] private double _poisonOrbRadius;
        [SerializeField] private double _poisonOrbLifetime;
        [SerializeField] private double _poisonBounceMissChance;
        [SerializeField] private double _poisonBounceMinOffsetDegrees;
        [SerializeField] private double _poisonBounceMaxOffsetDegrees;
        [SerializeField] private double _poisonRadius;
        [SerializeField] private double _poisonDamage;
        [SerializeField] private double _poisonLifetime;
        [SerializeField] private double _poisonTickDamage;
        [SerializeField] private double _poisonTickInterval;
        public double PoisonTrack => _poisonTrack;
        public double PoisonLock => _poisonLock;
        public double PoisonOrbSpeed => _poisonOrbSpeed;
        public double PoisonOrbRadius => _poisonOrbRadius;
        public double PoisonOrbLifetime => _poisonOrbLifetime;
        public double PoisonBounceMissChance => _poisonBounceMissChance;
        public double PoisonBounceMinOffsetDegrees => _poisonBounceMinOffsetDegrees;
        public double PoisonBounceMaxOffsetDegrees => _poisonBounceMaxOffsetDegrees;
        public double PoisonRadius => _poisonRadius;
        public double PoisonDamage => _poisonDamage;
        public double PoisonLifetime => _poisonLifetime;
        public double PoisonTickDamage => _poisonTickDamage;
        public double PoisonTickInterval => _poisonTickInterval;

        [Header("도약 내려찍기")]
        [SerializeField] private double _stompInterval;
        [SerializeField] private double _stompWindup;
        [SerializeField] private double _stompApproachSeconds;
        [SerializeField] private double _stompSpeed;
        [SerializeField] private double _waveRadius;
        [SerializeField] private double _waveSeconds;
        [SerializeField] private double _waveWidth;
        [SerializeField] private double _waveDamage;
        public double StompInterval => _stompInterval;
        public double StompWindup => _stompWindup;
        public double StompApproachSeconds => _stompApproachSeconds;
        public double StompSpeed => _stompSpeed;
        public double WaveRadius => _waveRadius;
        public double WaveSeconds => _waveSeconds;
        public double WaveWidth => _waveWidth;
        public double WaveDamage => _waveDamage;

        [Header("회전 돌진")]
        [SerializeField] private double _spinWarning;
        [SerializeField] private double _spinLength;
        [SerializeField, Tooltip("목이 한 바퀴 회전하는 데 걸리는 시간입니다.")] private double _spinSeconds;
        [SerializeField] private double _spinDashBodyLengths;
        [SerializeField] private double _spinDashSeconds;
        public double SpinWarning => _spinWarning;
        public double SpinLength => _spinLength;
        public double SpinSeconds => _spinSeconds;
        public double SpinDashBodyLengths => _spinDashBodyLengths;
        public double SpinDashSeconds => _spinDashSeconds;

        /// <summary>시간·범위·피해의 양수, 반사 조준의 확률·각도 범위와 피해 배율·도약 주기를 검증한다.</summary>
        internal void Validate()
        {
            double[] values = { _initialDelay, _attackCooldown, _burnMultiplier, _dpsHealthRatio, _dpsWindow,
                _excessDamageMultiplier, _neckWidth, _neckDamage, _stretchWarning, _stretchSeconds, _stretchHold, _stretchRotationDegrees,
                _stretchRetractSeconds, _retractSeconds, _poisonTrack, _poisonLock, _poisonOrbSpeed, _poisonOrbRadius, _poisonOrbLifetime, _poisonRadius, _poisonDamage,
                _poisonLifetime, _poisonTickDamage, _poisonTickInterval, _stompInterval, _stompWindup, _stompApproachSeconds,
                _stompSpeed, _waveRadius, _waveSeconds, _waveWidth, _waveDamage, _spinWarning, _spinLength, _spinSeconds,
                _poisonBounceMaxOffsetDegrees, _spinDashBodyLengths, _spinDashSeconds };
            foreach (double value in values)
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                    throw new FormatException("Hydra 설정은 유한한 양수여야 합니다.");
            if (_excessDamageMultiplier > 1 || _stompWindup + _stompApproachSeconds >= _stompInterval)
                throw new FormatException("Hydra 피해 배율 또는 도약 주기가 잘못되었습니다.");
            if (double.IsNaN(_poisonBounceMissChance) || double.IsInfinity(_poisonBounceMissChance)
                || _poisonBounceMissChance < 0 || _poisonBounceMissChance > 1
                || double.IsNaN(_poisonBounceMinOffsetDegrees) || double.IsInfinity(_poisonBounceMinOffsetDegrees) || _poisonBounceMinOffsetDegrees < 0
                || _poisonBounceMaxOffsetDegrees < _poisonBounceMinOffsetDegrees || _poisonBounceMaxOffsetDegrees >= 90)
                throw new FormatException("Hydra 독 반사 조준의 빗나감 확률 또는 각도 범위가 잘못되었습니다.");
        }
    }
}
