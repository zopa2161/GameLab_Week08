using System;

using UnityEngine;

namespace RuneCode
{
    /// <summary>레골라스의 공격 대기, 후방 도약과 세 발 범위 공격 설정을 보관한다.</summary>
    [Serializable]
    public sealed class LegolasDefinition
    {
        [Header("공격 주기")]
        [SerializeField] private double _initialDelay;
        [SerializeField] private double _attackCooldown;
        public double InitialDelay => _initialDelay;
        public double AttackCooldown => _attackCooldown;

        [Header("후방 도약")]
        [SerializeField] private double _jumpWindup;
        [SerializeField] private double _jumpDistance;
        [SerializeField] private double _jumpSeconds;
        public double JumpWindup => _jumpWindup;
        public double JumpDistance => _jumpDistance;
        public double JumpSeconds => _jumpSeconds;

        [Header("벽 탈출 도약")]
        [SerializeField] private double _wallEscapeMargin;
        [SerializeField] private double _escapeJumpDistance;
        [SerializeField] private double _playerClearance;
        public double WallEscapeMargin => _wallEscapeMargin;
        public double EscapeJumpDistance => _escapeJumpDistance;
        public double PlayerClearance => _playerClearance;

        [Header("범위 공격")]
        [SerializeField] private double _areaWarning;
        [SerializeField] private double _areaFlight;
        [SerializeField] private double _areaRadius;
        [SerializeField] private double _areaDamage;
        [SerializeField] private double _scatterRadius;
        [SerializeField] private double _minimumTargetDistance;
        public double AreaWarning => _areaWarning;
        public double AreaFlight => _areaFlight;
        public double AreaRadius => _areaRadius;
        public double AreaDamage => _areaDamage;
        public double ScatterRadius => _scatterRadius;
        public double MinimumTargetDistance => _minimumTargetDistance;

        /// <summary>시간·거리·범위·피해와 분산 간격이 유효하고 예고 종료 시점이 도약 비행 중에 있는지 검증한다.</summary>
        internal void Validate()
        {
            double[] values = { _initialDelay, _attackCooldown, _jumpWindup, _jumpDistance, _jumpSeconds,
                _areaWarning, _areaFlight, _areaRadius, _areaDamage, _scatterRadius, _minimumTargetDistance,
                _wallEscapeMargin, _escapeJumpDistance, _playerClearance };
            foreach (double value in values)
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                    throw new FormatException("레골라스 설정은 유한한 양수여야 합니다.");
            if (_minimumTargetDistance > _scatterRadius)
                throw new FormatException("레골라스 공격 지점의 최소 간격은 분산 반경 이하여야 합니다.");
            if (_areaWarning <= _jumpWindup || _areaWarning >= _jumpWindup + _jumpSeconds)
                throw new FormatException("레골라스 범위 공격 예고는 이륙 후 착지 전에 끝나야 합니다.");
        }
    }
}
