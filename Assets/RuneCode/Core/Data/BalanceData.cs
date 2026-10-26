using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    [Serializable]
    public sealed class PlayerBalance
    {
        [Header("플레이어")]
        [SerializeField] private float _maxHp;
        [SerializeField] private float _moveSpeed;
        [SerializeField] private float _maxEnergy;
        [SerializeField, Min(0f)] private float _manaPerLevel = 10f;
        [SerializeField] private float _energyRegen;
        [SerializeField] private float _dashDistance;
        [SerializeField] private float _dashDuration;
        [SerializeField] private float _dashCooldown;
        [SerializeField] private float _hitInvulnerability;

        [Header("강화 트리 대시")]
        [SerializeField] private float _postDashBuffSeconds = 1f;
        [SerializeField] private float _dashEndCostMultiplier = 0.9f;
        [SerializeField] private float _dashGuardInvulnerability = 1f;
        public float MaxHp => _maxHp;
        public float MoveSpeed => _moveSpeed;
        public float MaxEnergy => _maxEnergy;
        public float ManaPerLevel => _manaPerLevel;
        public float EnergyRegen => _energyRegen;
        public float DashDistance => _dashDistance;
        public float DashCooldown => _dashCooldown;
        public float Hp => _maxHp;
        public float Speed => _moveSpeed;
        public float Energy => _maxEnergy;
        public float DashSeconds => _dashDuration;
        public float HurtInvulnerability => _hitInvulnerability;

        /// <summary>대시 시작 후 이 시간 안에 시전한 주문에 대시 후 피해 강화를 적용한다.</summary>
        public float PostDashBuffSeconds => _postDashBuffSeconds;

        /// <summary>대시 종료 시작 노드 주문의 마나 비용 배율이다.</summary>
        public float DashEndCostMultiplier => _dashEndCostMultiplier;

        /// <summary>대시 충돌 보호가 발동할 때 주는 무적 시간이다.</summary>
        public float DashGuardInvulnerability => _dashGuardInvulnerability;
    }

    [Serializable]
    public sealed class LimitBalance
    {
        [Header("제한")]
        [SerializeField] private int _maxLiveSpellEntities;
        [SerializeField] private int _maxFrameSteps;
        [SerializeField] private int _maxGraphNodes;
        [SerializeField] private int _maxGraphEdges;
        [SerializeField] private int _maxEnemies = 300;
        [SerializeField] private int _maxCompiledActions = 2048;
        [SerializeField] private int _maxScheduledExecutions = 2048;
        [SerializeField] private int _maxActionsPerTick = 512;
        [SerializeField] private float _cooldownBase = 0.25f;
        public int MaxLiveSpellEntities => _maxLiveSpellEntities;
        public int MaxFrameSteps => _maxFrameSteps;
        public int MaxGraphNodes => _maxGraphNodes;
        public int MaxGraphEdges => _maxGraphEdges;
        public int MaxEnemies => _maxEnemies;
        public int MaxCompiledActions => _maxCompiledActions;
        public int MaxScheduledExecutions => _maxScheduledExecutions;
        public int MaxActionsPerTick => _maxActionsPerTick;

        /// <summary>모든 마법 Entry에 공통으로 적용되는 기본 쿨다운(초)이다. 실행 시간은 시전 시점에 더한다.</summary>
        public float CooldownBase => _cooldownBase;
    }

    [Serializable]
    public sealed class AdaptationBalance
    {
        [Header("적응")]
        [SerializeField] private float _elementLearning;
        [SerializeField] private float _formLearning;
        [SerializeField] private float _elementCap;
        [SerializeField] private float _formCap;
        [SerializeField] private float _dotWeight;
        [SerializeField] private float _noiseMultiplier;
        [SerializeField] private float _relayMultiplier;
        [SerializeField] private float _decayDelay;
        [SerializeField] private float _decayPerSecond;
        [SerializeField] private float _resistanceThreshold;
        public float ElementLearning => _elementLearning;
        public float FormLearning => _formLearning;
        public float ElementCap => _elementCap;
        public float FormCap => _formCap;
        public float DotWeight => _dotWeight;
        public float NoiseMultiplier => _noiseMultiplier;
        public float RelayMultiplier => _relayMultiplier;
        public float DecayDelay => _decayDelay;
        public float DecayPerSecond => _decayPerSecond;
        public float ResistanceThreshold => _resistanceThreshold;
    }

    [Serializable]
    public sealed class CombatBalance
    {
        [Header("상태와 방어")]
        [SerializeField] private float _burnDps;
        [SerializeField] private float _burnDamageRatio;
        [SerializeField] private float _burnInterval;
        [SerializeField] private float _burnSeconds;
        [SerializeField] private float _chillSlow;
        [SerializeField] private float _chillSeconds;
        [SerializeField] private int _chillMaxStacks;
        [SerializeField] private int _chillImmuneMaxStacks;
        [SerializeField] private float _freezeSeconds;
        [SerializeField] private float _freezeImmunity;
        [SerializeField] private float _empSeconds;
        [SerializeField] private float _aegisAngle;
        [SerializeField] private float _aegisReduction;
        [SerializeField] private float _relayRadius;
        [SerializeField] private float _relayRegen;
        [SerializeField] private float _separationAllowance;
        [SerializeField] private float _separationStrength;
        [SerializeField] private float _separationMaxStep;

        [Header("강화 트리 전투")]
        [SerializeField] private int _burnMaxStacks = 3;
        [SerializeField] private float _auraRadius = 100f;
        [SerializeField] private float _auraTickInterval = 0.5f;
        [SerializeField] private float _auraTickDamage = 1f;
        public float BurnDps => _burnDps;
        public float BurnDamageRatio => _burnDamageRatio;
        public float BurnInterval => _burnInterval;
        public float BurnSeconds => _burnSeconds;
        public float ChillSlow => _chillSlow;
        public float ChillSeconds => _chillSeconds;
        public int ChillMaxStacks => _chillMaxStacks;

        /// <summary>빙결 면역 중 쌓을 수 있는 냉기 스택 상한이다. 빙결을 일으키는 최대 스택보다 작아야 한다.</summary>
        public int ChillImmuneMaxStacks => _chillImmuneMaxStacks;
        public float FreezeSeconds => _freezeSeconds;
        public float FreezeImmunity => _freezeImmunity;
        public float EmpSeconds => _empSeconds;
        public float AegisAngle => _aegisAngle;
        public float AegisReduction => _aegisReduction;
        public float RelayRadius => _relayRadius;

        /// <summary>릴레이 오라가 60틱마다 반경 안의 다른 적을 회복시키는 체력량이다.</summary>
        public float RelayRegen => _relayRegen;
        public float SeparationAllowance => _separationAllowance;
        public float SeparationStrength => _separationStrength;
        public float SeparationMaxStep => _separationMaxStep;

        /// <summary>화염 강화에서 폭발이 일어나는 화상 중첩 수다.</summary>
        public int BurnMaxStacks => _burnMaxStacks;

        /// <summary>플레이어 주위 장판의 반경이다.</summary>
        public float AuraRadius => _auraRadius;

        /// <summary>플레이어 주위 장판의 피해 간격(초)이다.</summary>
        public float AuraTickInterval => _auraTickInterval;

        /// <summary>플레이어 주위 장판의 틱당 피해다.</summary>
        public float AuraTickDamage => _auraTickDamage;
    }

    [Serializable]
    public sealed class EconomyBalance
    {
        [Header("성장")]
        [SerializeField] private int _slotCost;
        [SerializeField] private float _deathRetention;
        [SerializeField] private int _maxLibrary;
        [SerializeField] private int _modifierStartStock;
        [SerializeField] private int _behaviorStartStock;
        [SerializeField] private float _orbAbsorbRadius;
        [SerializeField] private float _pickupAttractSpeed = 480f;
        [SerializeField] private float _killEnergyCreditRatio;

        [Header("런 선택 보상 등급")]
        [SerializeField] private string[] _modifierRewardGrades;
        [SerializeField] private string[] _behaviorRewardGrades;
        public IReadOnlyList<string> ModifierRewardGrades => _modifierRewardGrades;
        public IReadOnlyList<string> BehaviorRewardGrades => _behaviorRewardGrades;

        public int SlotCost => _slotCost;
        public float DeathRetention => _deathRetention;
        public int MaxLibrary => _maxLibrary;

        /// <summary>새 세이브(또는 소지량 기록이 없는 세이브)에서 Modifier 종류마다 주는 시작 소지량이다.</summary>
        public int ModifierStartStock => _modifierStartStock;

        /// <summary>새 세이브(또는 행동 소지량 기록이 없는 세이브)에서 behavior.launch에 주는 시작 소지량이다.</summary>
        public int BehaviorStartStock => _behaviorStartStock;
        /// <summary>스크랩과 Modifier의 자석 이동이 시작되는 공통 반경이다.</summary>
        public float OrbAbsorbRadius => _orbAbsorbRadius;
        /// <summary>자석 이동 중 아이템이 플레이어를 향해 이동하는 초당 거리다.</summary>
        public float PickupAttractSpeed => _pickupAttractSpeed;

        /// <summary>적을 처치했을 때 떨어뜨린 크레딧에서 회복하는 에너지 비율이다.</summary>
        public float KillEnergyCreditRatio => _killEnergyCreditRatio;
    }

    [Serializable]
    public sealed class SimulationBalance
    {
        [Header("시뮬레이션")]
        [SerializeField] private int _tickRate;
        [SerializeField] private float _multiOffset;
        [SerializeField] private float _benchPlayerX;
        [SerializeField] private float _benchPlayerY;
        [SerializeField] private float _benchDummyX;
        [SerializeField] private float _benchDummyY;
        [SerializeField] private float _benchHp;
        [SerializeField] private float _benchLineStartX;
        [SerializeField] private float _benchLineGap;
        [SerializeField] private int _benchLineCount;
        [SerializeField] private float _benchSwarmX;
        [SerializeField] private float _benchSwarmY;
        [SerializeField] private int _benchSwarmColumns;
        [SerializeField] private int _benchSwarmRows;
        [SerializeField] private float _benchSwarmGapX;
        [SerializeField] private float _benchSwarmGapY;
        [SerializeField] private float _roundBreakDummyDistance;
        [SerializeField] private float _roundBreakDummyGapX;
        [SerializeField] private float _roundBreakDummyGapY;
        [SerializeField] private float _roundBreakPortalOffset;
        [SerializeField] private float _roundBreakPortalRadius;
        [SerializeField] private float _spellVisualSeconds;
        [SerializeField] private float _hitFlashSeconds;
        [SerializeField] private float _damageNumberSeconds;
        [SerializeField] private float _wandOffset;
        [SerializeField] private float _areaPlacementRange;
        [SerializeField] private float _executionTimeScale = 1f;
        public int TickRate => _tickRate;
        public float MultiOffset => _multiOffset;
        public float BenchPlayerX => _benchPlayerX;
        public float BenchPlayerY => _benchPlayerY;
        public float BenchDummyX => _benchDummyX;
        public float BenchDummyY => _benchDummyY;
        public float BenchHp => _benchHp;
        public float BenchLineStartX => _benchLineStartX;
        public float BenchLineGap => _benchLineGap;
        public int BenchLineCount => _benchLineCount;
        public float BenchSwarmX => _benchSwarmX;
        public float BenchSwarmY => _benchSwarmY;
        public int BenchSwarmColumns => _benchSwarmColumns;
        public int BenchSwarmRows => _benchSwarmRows;
        public float BenchSwarmGapX => _benchSwarmGapX;
        public float BenchSwarmGapY => _benchSwarmGapY;

        /// <summary>라운드 대기 시험장에서 중앙 플레이어로부터 첫 허수아비 열까지의 x 거리(px)다.</summary>
        public float RoundBreakDummyDistance => _roundBreakDummyDistance;

        /// <summary>라운드 대기 시험장 허수아비의 열 간격(px)이다.</summary>
        public float RoundBreakDummyGapX => _roundBreakDummyGapX;

        /// <summary>라운드 대기 시험장 같은 열 허수아비의 세로 간격(px)이다.</summary>
        public float RoundBreakDummyGapY => _roundBreakDummyGapY;

        /// <summary>라운드 대기 포털이 맵 중앙에서 화면 위쪽으로 떨어진 거리(px)다.</summary>
        public float RoundBreakPortalOffset => _roundBreakPortalOffset;

        /// <summary>플레이어가 들어갔다고 판정하는 라운드 대기 포털의 반경(px)이다.</summary>
        public float RoundBreakPortalRadius => _roundBreakPortalRadius;
        public float SpellVisualSeconds => _spellVisualSeconds;
        public float HitFlashSeconds => _hitFlashSeconds;
        public float DamageNumberSeconds => _damageNumberSeconds;
        public float WandOffset => _wandOffset;

        /// <summary>플레이어 중심에서 폭발·잔류를 배치할 수 있는 공통 최대 거리(px)다.</summary>
        public float AreaPlacementRange => _areaPlacementRange;

        /// <summary>룬 실행 시간 기여분의 전역 배율이며 0이면 Shape·Behavior 노드 시간 기여를 끈다.</summary>
        public float ExecutionTimeScale => _executionTimeScale;
    }

    [Serializable]
    public sealed class BalanceData
    {
        [Header("공용 밸런스")]
        [SerializeField] private PlayerBalance _player;
        [SerializeField] private LimitBalance _limits;
        [SerializeField] private AdaptationBalance _adaptation;
        [SerializeField] private CombatBalance _combat;
        [SerializeField] private EconomyBalance _economy;
        [SerializeField] private SimulationBalance _sim;
        public PlayerBalance Player => _player;
        public LimitBalance Limits => _limits;
        public AdaptationBalance Adaptation => _adaptation;
        public CombatBalance Combat => _combat;
        public EconomyBalance Economy => _economy;
        public SimulationBalance Sim => _sim;

        private GrammarLimits _grammar;

        /// <summary>문법 엔진(컴파일러)에 전달할 한도·기본값을 이 밸런스 값으로 만들어 반환한다. 처음 요청할 때 한 번 만든다.</summary>
        public GrammarLimits Grammar => _grammar ??= new GrammarLimits(_limits.MaxGraphNodes, _limits.MaxGraphEdges,
            _limits.MaxCompiledActions, _limits.MaxLiveSpellEntities, _player.MaxEnergy, _limits.CooldownBase);

        /// <summary>밸런스 JSON을 읽고 필수 설정과 양수 제한값을 검증하여 반환한다.</summary>
        public static BalanceData FromJson(string json)
        {
            BalanceData data = JsonUtility.FromJson<BalanceData>(json);
            if (data == null || data.Player == null || data.Limits == null || data.Adaptation == null
                || data.Combat == null || data.Economy == null || data.Sim == null
                || data.Player.MaxHp <= 0f || data.Player.MaxEnergy <= 0f || data.Sim.TickRate != 60
                || data.Limits.MaxLiveSpellEntities <= 0)
                throw new FormatException("유효하지 않은 밸런스 데이터입니다.");
            if (float.IsNaN(data.Combat.BurnDamageRatio) || float.IsInfinity(data.Combat.BurnDamageRatio)
                || data.Combat.BurnDamageRatio < 0f || data.Combat.BurnDamageRatio > 1f)
                throw new FormatException("화상 공격 피해 비율은 0 이상 1 이하의 유한한 숫자여야 합니다.");
            ValidateRewardGrades(data.Economy.ModifierRewardGrades,
                StageCatalog.PROGRESSION_STAGE_COUNT * (StageCatalog.PROGRESSION_ROUND_COUNT - 2));
            ValidateRewardGrades(data.Economy.BehaviorRewardGrades, StageCatalog.PROGRESSION_STAGE_COUNT * 2 - 1);
            float[] values = { data.Player.MaxHp, data.Player.MoveSpeed, data.Player.MaxEnergy, data.Player.EnergyRegen,
                data.Player.DashDistance, data.Player.DashSeconds, data.Player.DashCooldown, data.Player.HurtInvulnerability,
                data.Adaptation.ElementLearning, data.Adaptation.FormLearning, data.Adaptation.ElementCap, data.Adaptation.FormCap,
                data.Adaptation.DotWeight, data.Adaptation.NoiseMultiplier, data.Adaptation.RelayMultiplier, data.Adaptation.DecayDelay,
                data.Adaptation.DecayPerSecond, data.Adaptation.ResistanceThreshold, data.Combat.BurnDps, data.Combat.BurnInterval,
                data.Combat.BurnSeconds, data.Combat.ChillSlow, data.Combat.ChillSeconds, data.Combat.FreezeSeconds,
                data.Combat.FreezeImmunity, data.Combat.EmpSeconds, data.Combat.AegisAngle, data.Combat.AegisReduction,
                data.Combat.RelayRadius, data.Combat.RelayRegen, data.Economy.DeathRetention, data.Economy.OrbAbsorbRadius,
                data.Economy.KillEnergyCreditRatio, data.Economy.PickupAttractSpeed, data.Limits.CooldownBase, data.Sim.MultiOffset,
                data.Sim.BenchPlayerX, data.Sim.BenchPlayerY, data.Sim.BenchDummyX,
                data.Sim.BenchDummyY, data.Sim.BenchHp, data.Sim.BenchLineStartX, data.Sim.BenchLineGap, data.Sim.BenchSwarmX,
                data.Sim.BenchSwarmY, data.Sim.BenchSwarmGapX, data.Sim.BenchSwarmGapY,
                data.Sim.RoundBreakDummyDistance, data.Sim.RoundBreakDummyGapX, data.Sim.RoundBreakDummyGapY,
                data.Sim.RoundBreakPortalOffset, data.Sim.RoundBreakPortalRadius, data.Sim.SpellVisualSeconds,
                data.Sim.HitFlashSeconds, data.Sim.DamageNumberSeconds, data.Sim.WandOffset, data.Sim.AreaPlacementRange,
                data.Sim.ExecutionTimeScale, data.Combat.SeparationAllowance, data.Combat.SeparationStrength,
                data.Combat.SeparationMaxStep };
            foreach (float value in values)
                //if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                //    throw new FormatException("밸런스 값은 유한한 0 이상이어야 합니다.");
                if (data.Player.MoveSpeed <= 0f || data.Player.DashSeconds <= 0f || data.Combat.BurnInterval <= 0f
                    || data.Combat.ChillMaxStacks <= 0 || data.Combat.ChillImmuneMaxStacks <= 0
                    || data.Combat.ChillImmuneMaxStacks >= data.Combat.ChillMaxStacks
                    || data.Adaptation.ElementCap > 1f || data.Adaptation.FormCap > 1f
                    || data.Combat.AegisReduction > 1f || data.Combat.ChillSlow > 1f
                    || data.Economy.DeathRetention > 1f || data.Economy.MaxLibrary <= 0
                    || data.Economy.ModifierStartStock < 0 || data.Economy.BehaviorStartStock < 0
                    || data.Economy.SlotCost <= 0 || data.Economy.PickupAttractSpeed <= 0f
                    || data.Combat.SeparationAllowance <= 0f || data.Combat.SeparationAllowance > 1f
                    || data.Combat.SeparationStrength <= 0f || data.Combat.SeparationStrength > 1f
                    || data.Combat.SeparationMaxStep <= 0f
                    || data.Limits.MaxFrameSteps <= 0 || data.Limits.MaxGraphNodes <= 0 || data.Limits.MaxGraphEdges <= 0
                    || data.Sim.BenchHp <= 0f || data.Sim.AreaPlacementRange <= 0f
                    || data.Sim.RoundBreakDummyDistance <= 0f || data.Sim.RoundBreakDummyGapX <= 0f || data.Sim.RoundBreakDummyGapY <= 0f
                    || data.Sim.RoundBreakPortalOffset <= 0f || data.Sim.RoundBreakPortalRadius <= 0f
                    || data.Sim.RoundBreakPortalOffset <= data.Sim.RoundBreakPortalRadius
                    || data.Sim.BenchLineCount <= 0 || data.Sim.BenchSwarmColumns <= 0 || data.Sim.BenchSwarmRows <= 0
                    || data.Sim.SpellVisualSeconds <= 0f || data.Sim.DamageNumberSeconds <= 0f
                    || data.Limits.MaxEnemies <= 0 || data.Limits.MaxCompiledActions <= 0
                    || data.Limits.MaxScheduledExecutions <= 0 || data.Limits.MaxActionsPerTick <= 0)
                    throw new FormatException("밸런스 범위가 유효하지 않습니다.");
            if (data.Player.PostDashBuffSeconds < 0f || data.Player.DashEndCostMultiplier <= 0f || data.Player.DashEndCostMultiplier > 1f
                || data.Player.DashGuardInvulnerability < 0f || data.Combat.BurnMaxStacks < 2 || data.Combat.AuraRadius <= 0f
                || data.Combat.AuraTickInterval <= 0f || data.Combat.AuraTickDamage < 0f)
                throw new FormatException("강화 트리 밸런스 범위가 유효하지 않습니다.");
            return data;
        }

        /// <summary>시작 지급을 제외한 등급 목록의 길이와 C·B·A·S 값을 검사한다. 유효하지 않으면 데이터 오류를 발생시킨다.</summary>
        private static void ValidateRewardGrades(IReadOnlyList<string> grades, int count)
        {
            if (grades == null || grades.Count != count)
                throw new FormatException("런 선택 보상 등급 수가 라운드 수와 다릅니다: " + count);
            foreach (string grade in grades)
                if (grade != "C" && grade != "B" && grade != "A" && grade != "S")
                    throw new FormatException("유효하지 않은 런 선택 보상 등급입니다: " + grade);
        }
    }
}
