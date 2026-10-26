using System.Collections.Generic;

namespace RuneCode
{
    internal sealed class ActionCombatStats
    {
        private readonly string _key;
        private readonly Dictionary<SpellAction, long> _onHitTargets = new Dictionary<SpellAction, long>();
        private readonly Dictionary<string, double> _directDamageByElement = new Dictionary<string, double>
        { ["raw"] = 0d, ["fire"] = 0d, ["ice"] = 0d, ["arc"] = 0d, ["steam"] = 0d, ["fire_arc"] = 0d, ["ice_arc"] = 0d };
        private readonly Dictionary<string, double> _statusDamageByType = new Dictionary<string, double>
        { ["burn"] = 0d, ["chill"] = 0d, ["freeze"] = 0d, ["emp"] = 0d, ["arc_chain"] = 0d, ["steam"] = 0d, ["fire_chain"] = 0d, ["ice_chain"] = 0d };
        private long _spawnCount;
        private long _damageCount;
        private long _hitCount;
        private long _onHitEvents;
        private long _onHitSpawnCount;
        private long _triggeredByOnHitCount;
        private double _damage;
        private double _statusDamage;
        public string Key => _key;
        public long SpawnCount => _spawnCount;
        public long DamageCount => _damageCount;
        public long HitCount => _hitCount;
        public long OnHitEvents => _onHitEvents;
        public long OnHitSpawnCount => _onHitSpawnCount;
        public long TriggeredByOnHitCount => _triggeredByOnHitCount;
        public double Damage => _damage;
        public double StatusDamage => _statusDamage;
        public IReadOnlyDictionary<string, double> DirectDamageByElement => _directDamageByElement;
        public IReadOnlyDictionary<string, double> StatusDamageByType => _statusDamageByType;
        public IReadOnlyDictionary<SpellAction, long> OnHitTargets => _onHitTargets;

        /// <summary>로그 내 행동 키를 저장하고 빈 전투 누계를 만든다.</summary>
        public ActionCombatStats(string key) { _key = key; }

        /// <summary>실제 생성 수와 OnHit 유래 생성 수를 증가시킨다.</summary>
        public void RecordSpawn(bool isOnHit) { _spawnCount++; if (isOnHit) _triggeredByOnHitCount++; }

        /// <summary>원본 행동의 실제 OnHit 이벤트 수를 증가시킨다.</summary>
        public void RecordOnHit() { _onHitEvents++; }

        /// <summary>OnHit으로 실제 생성된 후속 행동 개체 수를 대상별로 증가시킨다.</summary>
        public void RecordOnHitTarget(SpellAction target)
        {
            _onHitSpawnCount++;
            _onHitTargets.TryGetValue(target, out long count);
            _onHitTargets[target] = count + 1;
        }

        /// <summary>실제 차감 피해 actual을 누적하고 실제 속성 element와 상태 종류 damageStatus별로 분리한다.</summary>
        public void RecordDamage(double actual, string element, string damageStatus)
        {
            _damage += actual; _damageCount++;
            if (damageStatus != null)
            {
                _statusDamage += actual;
                _statusDamageByType.TryGetValue(damageStatus, out double previous);
                _statusDamageByType[damageStatus] = previous + actual;
                if (damageStatus != "burn") _hitCount++;
            }
            else
            {
                _hitCount++;
                string key = string.IsNullOrEmpty(element) ? "unknown" : element;
                _directDamageByElement.TryGetValue(key, out double previous);
                _directDamageByElement[key] = previous + actual;
            }
        }
    }
}
