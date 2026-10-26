using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 조합 하나 × 조건 하나의 측정 결과다. 값은 측정 실행기만 채우고 외부에서는 읽기만 한다.
    /// 반복 측정한 경우 피해·전력 지표는 반복 평균이고 DPS 최소·최대를 따로 기록한다.
    /// </summary>
    [Serializable]
    public sealed class MeasurementResult
    {
        [Header("식별")]
        [SerializeField] private string _buildId;
        [SerializeField] private string _conditionId;
        [SerializeField] private string _grade;
        [SerializeField] private int _chainLength;
        [SerializeField] private string _status = MeasurementCodes.STATUS_MEASURED;
        [SerializeField] private List<string> _compileErrors = new List<string>();
        [SerializeField] private List<string> _warnings = new List<string>();

        [Header("마법 정보")]
        [SerializeField] private double _castCost;
        [SerializeField] private double _cooldownSeconds;
        [SerializeField] private double _castRange;

        [Header("피해")]
        [SerializeField] private double _totalDamage;
        [SerializeField] private double _dps;
        [SerializeField] private double _burstDps;
        [SerializeField] private double _sustainedDps;
        [SerializeField] private double _statusDamage;
        [SerializeField] private long _hitCount;

        [Header("전력과 시전")]
        [SerializeField] private double _energySpent;
        [SerializeField] private double _damagePerEnergy;
        [SerializeField] private int _castAttempts;
        [SerializeField] private int _castSuccesses;
        [SerializeField] private int _castFailures;
        [SerializeField, Tooltip("첫 전력 부족 실패 시각(초). 실패가 없으면 -1")] private double _firstFailureSeconds = -1;
        [SerializeField] private string _bottleneck = MeasurementCodes.BOTTLENECK_NONE;

        [Header("대상 분포")]
        [SerializeField] private int _damagedEnemyCount;
        [SerializeField] private double _enemyDamageMin;
        [SerializeField] private double _enemyDamageMax;
        [SerializeField] private List<BehaviorDamageEntry> _behaviorDamages = new List<BehaviorDamageEntry>();

        [Header("한도와 반복")]
        [SerializeField] private int _peakSpellEntities;
        [SerializeField] private int _droppedExecutions;
        [SerializeField] private int _runCount = 1;
        [SerializeField] private double _dpsMin;
        [SerializeField] private double _dpsMax;

        public string BuildId { get => _buildId; internal set => _buildId = value; }
        public string ConditionId { get => _conditionId; internal set => _conditionId = value; }
        public string Grade { get => _grade; internal set => _grade = value; }
        public int ChainLength { get => _chainLength; internal set => _chainLength = value; }
        public string Status { get => _status; internal set => _status = value; }
        public List<string> CompileErrors => _compileErrors;
        public List<string> Warnings => _warnings;

        public double CastCost { get => _castCost; internal set => _castCost = value; }
        public double CooldownSeconds { get => _cooldownSeconds; internal set => _cooldownSeconds = value; }
        public double CastRange { get => _castRange; internal set => _castRange = value; }

        public double TotalDamage { get => _totalDamage; internal set => _totalDamage = value; }
        public double Dps { get => _dps; internal set => _dps = value; }
        public double BurstDps { get => _burstDps; internal set => _burstDps = value; }
        public double SustainedDps { get => _sustainedDps; internal set => _sustainedDps = value; }
        public double StatusDamage { get => _statusDamage; internal set => _statusDamage = value; }
        public long HitCount { get => _hitCount; internal set => _hitCount = value; }

        public double EnergySpent { get => _energySpent; internal set => _energySpent = value; }
        public double DamagePerEnergy { get => _damagePerEnergy; internal set => _damagePerEnergy = value; }
        public int CastAttempts { get => _castAttempts; internal set => _castAttempts = value; }
        public int CastSuccesses { get => _castSuccesses; internal set => _castSuccesses = value; }
        public int CastFailures { get => _castFailures; internal set => _castFailures = value; }
        public double FirstFailureSeconds { get => _firstFailureSeconds; internal set => _firstFailureSeconds = value; }
        public string Bottleneck { get => _bottleneck; internal set => _bottleneck = value; }

        public int DamagedEnemyCount { get => _damagedEnemyCount; internal set => _damagedEnemyCount = value; }
        public double EnemyDamageMin { get => _enemyDamageMin; internal set => _enemyDamageMin = value; }
        public double EnemyDamageMax { get => _enemyDamageMax; internal set => _enemyDamageMax = value; }
        public List<BehaviorDamageEntry> BehaviorDamages => _behaviorDamages;

        public int PeakSpellEntities { get => _peakSpellEntities; internal set => _peakSpellEntities = value; }
        public int DroppedExecutions { get => _droppedExecutions; internal set => _droppedExecutions = value; }
        public int RunCount { get => _runCount; internal set => _runCount = value; }
        public double DpsMin { get => _dpsMin; internal set => _dpsMin = value; }
        public double DpsMax { get => _dpsMax; internal set => _dpsMax = value; }
    }
}
