using System;
using System.Text;

namespace RuneCode
{
    public enum EnemyStatusType { Burn, Chill, Freeze, Emp, ArcHitCounter }

    public sealed class EnemyStatusEffect
    {
        private readonly int _enemyId;
        private readonly EnemyStatusType _type;
        private double _until;

        private double _nextTickTime;
        private double _tickDamage;
        private string _sourceForm = "bolt";
        private bool _isNoise;
        private SpellAction _damageSourceAction;
        internal SpellAction DamageSourceAction { get => _damageSourceAction; set => _damageSourceAction = value; }

        private int _stacks;

        private double _immuneUntil;
        private bool _hasTriggered;
        public bool HasTriggered { get => _hasTriggered; internal set => _hasTriggered = value; }
        public int EnemyId => _enemyId;
        public EnemyStatusType Type => _type;
        public double Until { get => _until; internal set => _until = value; }
        public double NextTickTime { get => _nextTickTime; internal set => _nextTickTime = value; }
        public double TickDamage { get => _tickDamage; internal set => _tickDamage = value; }
        public string SourceForm { get => _sourceForm; internal set => _sourceForm = value; }
        public bool IsNoise { get => _isNoise; internal set => _isNoise = value; }
        public int Stacks { get => _stacks; internal set => _stacks = value; }
        public double ImmuneUntil { get => _immuneUntil; internal set => _immuneUntil = value; }

        /// <summary>대상 적 ID와 상태 이상 종류로 빈 상태 이상 데이터를 생성한다. 수치는 시뮬레이션이 부여 시 설정한다.</summary>
        internal EnemyStatusEffect(int enemyId, EnemyStatusType type) { _enemyId = enemyId; _type = type; }

        /// <summary>대상 ID, 종류, 만료 시각, 틱 피해와 종류별 진행 값을 결정성 해시 버퍼에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        { state.Append(FormattableString.Invariant($"|{_enemyId}|{_type}|{_until:R}|{_nextTickTime:R}|{_tickDamage:R}|{_sourceForm}|{_isNoise}|{_stacks}|{_immuneUntil:R}|{_hasTriggered}")); }
    }
}
