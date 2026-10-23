using System;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 측정에 쓰는 플레이어 상태다. 강화 트리를 계산하지 않고 측정 조건에 고정한 값을 그대로 쓴다.
    /// </summary>
    [Serializable]
    public sealed class MeasurementPlayerProfile
    {
        [Header("식별")]
        [SerializeField] private string _profileId = "max_tree";

        [Header("자원")]
        [SerializeField] private int _ramCapacity = 21;
        [SerializeField] private float _maxEnergy = 250f;
        [SerializeField] private float _energyRegen = 55f;
        [SerializeField, Range(0f, 1f)] private float _startEnergyRatio = 1f;

        [Header("전투")]
        [SerializeField] private float _damageMultiplier = 1f;
        [SerializeField] private bool _isInvulnerable = true;

        public string ProfileId => _profileId;
        public int RamCapacity => _ramCapacity;
        public float MaxEnergy => _maxEnergy;
        public float EnergyRegen => _energyRegen;
        public float StartEnergyRatio => _startEnergyRatio;
        public float DamageMultiplier => _damageMultiplier;
        public bool IsInvulnerable => _isInvulnerable;
    }
}
