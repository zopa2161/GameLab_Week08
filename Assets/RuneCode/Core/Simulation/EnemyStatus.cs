using System;
using System.Collections.Generic;
using System.Text;

namespace RuneCode
{
    // Shock은 기존 저장·표시 호환을 위해 유지한다. 기본 전격 피해는 EMP를 부여하므로 새로 적용하지 않는다.
    // Steam은 화염+냉기 혼합 속성이 남기는 찜통 상태로, 걸린 적이 주변 적에게 주기 피해를 준다.
    public enum EnemyStatusType { Burn, Chill, Freeze, Emp, Shock, Steam }

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
        private readonly List<double> _fireStackDamages = new List<double>();

        private double _immuneUntil;
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

        /// <summary>화염 강화의 화상 중첩을 1 늘리고 해당 부여의 화상 틱 피해를 중첩 피해 목록에 저장한다.</summary>
        internal void AddFireStack(double tickDamage) { _stacks++; _fireStackDamages.Add(tickDamage); }

        /// <summary>새 화상이 시작될 때 이전 화상의 중첩 수와 중첩 피해를 비운다.</summary>
        internal void ClearFireStacks() { _stacks = 0; _fireStackDamages.Clear(); }

        /// <summary>저장한 화상 중첩 틱 피해를 모두 합한 값에 multiplier를 곱해 폭발 피해를 반환한다. 중첩이 없으면 0을 반환한다.</summary>
        internal double GetFireBurstDamage(double multiplier)
        {
            if (_fireStackDamages.Count == 0) return 0d;
            double damage = 0d;
            for (int i = 0; i < _fireStackDamages.Count; i++) damage += _fireStackDamages[i];
            return damage * multiplier;
        }

        /// <summary>대상 ID, 종류, 만료 시각, 틱 피해, 종류별 진행 값과 화상 중첩 피해를 결정성 해시 버퍼에 기록한다.</summary>
        internal void WriteState(StringBuilder state)
        {
            state.Append(FormattableString.Invariant($"|{_enemyId}|{_type}|{_until:R}|{_nextTickTime:R}|{_tickDamage:R}|{_sourceForm}|{_isNoise}|{_stacks}|{_immuneUntil:R}"));
            for (int i = 0; i < _fireStackDamages.Count; i++) state.Append(FormattableString.Invariant($"|{_fireStackDamages[i]:R}"));
        }
    }
}
