using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>적 상태 이상(화염·냉기·빙결·EMP)의 부여, 틱 진행과 만료를 관리한다.</summary>
    internal sealed class EnemyStatusSystem
    {
        private const int STATUS_TYPE_COUNT = 5;
        private const int ARC_HITS_PER_TRIGGER = 5;
        private readonly SimulationWorld _world;
        private readonly SimulationStats _stats;
        private readonly RuneSimulation _simulation;
        private readonly List<EnemyStatusEffect> _enemyStatuses = new List<EnemyStatusEffect>();
        private readonly Dictionary<int, EnemyStatusEffect> _statusIndex = new Dictionary<int, EnemyStatusEffect>();
        private double _statusTime;

        /// <summary>현재 관리 중인 상태 이상 목록이다. 상태 해시가 같은 인스턴스를 순회한다.</summary>
        internal IReadOnlyList<EnemyStatusEffect> Statuses => _enemyStatuses;

        /// <summary>
        /// 공유 월드와 통계, 피해 판정을 호출할 시뮬레이션을 받아 상태 이상 시스템을 만든다.
        /// 피해 판정기·적 시스템이 이 시스템을 참조하므로 화상 피해(ApplyDamage)와 플레이어 피해(HurtPlayerByStatus)는 시뮬레이션 위임으로 부른다.
        /// </summary>
        internal EnemyStatusSystem(SimulationWorld world, SimulationStats stats, RuneSimulation simulation)
        {
            _world = world;
            _stats = stats;
            _simulation = simulation;
        }

        /// <summary>
        /// 상태 판정 시각을 현재 틱으로 갱신하고, 적 목록 순서로 화염의 마지막 만료 틱까지 지속 피해를 적용한 뒤 만료된 상태 이상을 제거한다.
        /// </summary>
        internal void AdvanceStatuses()
        {
            _statusTime = _world.Time;
            while (_world.Player.Hp > 0 && _world.Player.TryTakeBurnTick(_world.Time, _world.Balance.Combat.BurnInterval))
                _simulation.HurtPlayerByStatus(_world.Balance.Combat.BurnDps * _world.Balance.Combat.BurnInterval);
            foreach (SimulationEnemy enemy in _world.Enemies)
            {
                enemy.Observe(_world.Time);
                EnemyStatusEffect burn = FindStatus(enemy.Id, EnemyStatusType.Burn);
                if (burn == null) continue;
                while (enemy.IsAlive && burn.NextTickTime <= burn.Until + 0.000001 && _world.Time + 0.000001 >= burn.NextTickTime)
                {
                    double actual = _simulation.ApplyDamage(enemy, burn.TickDamage, "fire", burn.SourceForm, burn.IsNoise, true, SimVector.Zero, false, sourceAction: burn.DamageSourceAction, damageStatus: "burn");
                    _stats.StatusDamage += actual;
                    if (actual > 0) _stats.BurnTickDamageEventCount++;
                    if (actual > 0) _simulation.RaiseElementTrigger(burn.DamageSourceAction, enemy, SpellGrammar.ON_BURN_PORT);
                    burn.NextTickTime += _world.Balance.Combat.BurnInterval / (1 + _world.BurnSpeedBonus);
                }
            }
            for (int i = _enemyStatuses.Count - 1; i >= 0; i--)
                if (IsStatusExpired(_enemyStatuses[i])) { RemoveStatusIndex(_enemyStatuses[i]); _enemyStatuses.RemoveAt(i); }
        }

        /// <summary>
        /// 상태 이상이 더 이상 판정·진행에 쓰이지 않는지 반환한다.
        /// 화염은 마지막 지속 피해 틱까지 처리된 뒤, 빙결은 빙결 면역까지 끝난 뒤 만료된다.
        /// </summary>
        private bool IsStatusExpired(EnemyStatusEffect status)
        {
            switch (status.Type)
            {
                case EnemyStatusType.Burn: return status.NextTickTime > status.Until + 0.000001 && _world.Time >= status.Until;
                case EnemyStatusType.Freeze: return _world.Time >= status.ImmuneUntil;
                default: return _world.Time >= status.Until;
            }
        }

        /// <summary>피해 속성에 맞는 상태 이상을 부여한다. 화염은 전투 공통 보정 전 공격 피해 sourceDamage를 틱 피해 계산에 전달한다.</summary>
        internal void ApplyStatus(SimulationEnemy enemy, string element, string form, bool noise, double sourceDamage, SpellAction sourceAction)
        {
            if (enemy.Hydra != null && element != "fire") return;
            if (element == "fire") ApplyBurn(enemy, form, noise, sourceDamage, sourceAction);
            else if (element == "ice") ApplyChill(enemy, sourceAction);
            else if (element == "arc") GetOrAddStatus(enemy.Id, EnemyStatusType.Emp).Until = _world.Time + _world.Balance.Combat.EmpSeconds;
        }

        /// <summary>공격 피해 비율로 화상을 부여한다. 최종 효과가 있으면 틱 피해를 중첩하고 주변에 이번 화상을 복사하며, 복사본은 재전이하지 않는다.</summary>
        private void ApplyBurn(SimulationEnemy enemy, string form, bool noise, double sourceDamage, SpellAction sourceAction, bool canSpread = true)
        {
            EnemyStatusEffect burn = FindStatus(enemy.Id, EnemyStatusType.Burn);
            bool isNewBurn = burn == null || _world.Time >= burn.Until;
            if (isNewBurn)
            {
                burn ??= AddStatus(enemy.Id, EnemyStatusType.Burn);
                burn.NextTickTime = _world.Time + _world.Balance.Combat.BurnInterval / (1 + _world.BurnSpeedBonus);
            }
            burn.Until = _world.Time + _world.Balance.Combat.BurnSeconds;
            double tickDamage = sourceDamage * _world.Balance.Combat.BurnDamageRatio;
            if (_world.CanStackAndSpreadBurn && !isNewBurn) tickDamage += burn.TickDamage;
            burn.Stacks = isNewBurn ? 1 : _world.CanStackAndSpreadBurn ? burn.Stacks + 1 : 1;
            if (isNewBurn || tickDamage > burn.TickDamage)
            {
                burn.TickDamage = tickDamage;
                burn.DamageSourceAction = sourceAction;
                burn.SourceForm = form;
                burn.IsNoise = noise;
            }
            if (!_world.CanStackAndSpreadBurn || !canSpread) return;
            foreach (SimulationEnemy other in _world.Enemies)
                if (other != enemy && other.IsAlive && SimVector.Distance(enemy.Position, other.Position) <= _world.BurnSpreadRadius)
                {
                    ApplyBurn(other, form, noise, sourceDamage, sourceAction, canSpread: false);
                    _simulation.RecordMasteryEffect(MasteryEffectKind.BurnSpread, enemy.Position, other.Position, other.Radius * 2);
                }
        }

        /// <summary>
        /// 냉기 스택과 지속시간을 갱신하고, 최대 스택에서 빙결 면역이 아니면 빙결을 부여하며 냉기를 제거한다.
        /// 빙결 면역 중에는 냉기 스택이 면역 중 상한을 넘지 않으며 새 빙결은 sourceAction의 해금된 무료 후속을 실행한다.
        /// </summary>
        private void ApplyChill(SimulationEnemy enemy, SpellAction sourceAction)
        {
            CombatBalance combat = _world.Balance.Combat;
            EnemyStatusEffect freeze = FindStatus(enemy.Id, EnemyStatusType.Freeze);
            bool isImmune = freeze != null && _world.Time < freeze.ImmuneUntil;
            EnemyStatusEffect chill = GetOrAddStatus(enemy.Id, EnemyStatusType.Chill);
            int maxStacks = isImmune ? combat.ChillImmuneMaxStacks : combat.ChillMaxStacks;
            chill.Stacks = Math.Min(maxStacks, chill.Stacks + 1); chill.Until = _world.Time + combat.ChillSeconds;
            if (isImmune || chill.Stacks < combat.ChillMaxStacks) return;
            freeze ??= AddStatus(enemy.Id, EnemyStatusType.Freeze);
            freeze.Until = _world.Time + combat.FreezeSeconds; freeze.ImmuneUntil = freeze.Until + combat.FreezeImmunity;
            freeze.HasTriggered = false;
            _stats.FreezeStartEventCount++;
            RemoveStatusIndex(chill); _enemyStatuses.Remove(chill);
            _simulation.RaiseElementTrigger(sourceAction, enemy, SpellGrammar.ON_FREEZE_PORT);
        }

        /// <summary>적의 상태 이상이 현재 상태 판정 시각에 활성인지 반환한다. 냉기는 스택 보유 여부로 판정한다.</summary>
        internal bool HasEnemyStatus(SimulationEnemy enemy, EnemyStatusType type)
        {
            EnemyStatusEffect status = FindStatus(enemy.Id, type);
            if (status == null) return false;
            return type == EnemyStatusType.Chill ? status.Stacks > 0 : _statusTime < status.Until;
        }

        /// <summary>적의 현재 냉기 스택을 반환하며 냉기가 없으면 0을 반환한다.</summary>
        internal int GetChillStacks(SimulationEnemy enemy) => FindStatus(enemy.Id, EnemyStatusType.Chill)?.Stacks ?? 0;

        /// <summary>최종 노드를 해금했고 타격 직전 빙결 상태가 아직 유지 중이면 빙결 폭발 발동 가능 여부를 반환한다.</summary>
        internal bool CanTriggerFreezeBurst(SimulationEnemy enemy)
        {
            EnemyStatusEffect freeze = FindStatus(enemy.Id, EnemyStatusType.Freeze);
            return _world.FreezeBurstRatio > 0 && freeze != null && _world.Time < freeze.Until;
        }

        /// <summary>적별 전기 피해 횟수를 누적하고 매 5회째 무료 트리거와 해금된 기본 피해 번개를 발동한다. 번개 자체는 횟수에 포함하지 않는다.</summary>
        internal void RecordArcHit(SimulationEnemy enemy, SpellAction sourceAction)
        {
            if (!_world.HasArcTrigger && _world.LightningStrikeDamage <= 0) return;
            EnemyStatusEffect counter = GetOrAddStatus(enemy.Id, EnemyStatusType.ArcHitCounter);
            counter.Until = double.MaxValue;
            counter.Stacks++;
            if (counter.Stacks < ARC_HITS_PER_TRIGGER) return;
            counter.Stacks = 0;
            _simulation.RaiseElementTrigger(sourceAction, enemy, SpellGrammar.ON_ARC_HIT_PORT);
            if (_world.LightningStrikeDamage <= 0) return;
            _simulation.ShowLightningStrike(enemy);
            double actual = _simulation.ApplyDamage(enemy, _world.LightningStrikeDamage, "arc", "lightning", false, false,
                SimVector.Zero, false, sourceAction: sourceAction, damageStatus: "mastery_lightning");
            _stats.StatusDamage += actual;
            if (actual > 0) _stats.ArcDamageEventCount++;
        }

        /// <summary>적 ID와 종류로 상태 이상을 찾고 없으면 null을 반환한다.</summary>
        private EnemyStatusEffect FindStatus(int enemyId, EnemyStatusType type)
        { _statusIndex.TryGetValue(GetStatusKey(enemyId, type), out EnemyStatusEffect status); return status; }

        /// <summary>적 ID와 종류의 상태 이상을 찾고 없으면 새로 추가하여 반환한다.</summary>
        private EnemyStatusEffect GetOrAddStatus(int enemyId, EnemyStatusType type) => FindStatus(enemyId, type) ?? AddStatus(enemyId, type);

        /// <summary>새 상태 이상을 목록 끝과 조회 인덱스에 추가하여 반환한다.</summary>
        private EnemyStatusEffect AddStatus(int enemyId, EnemyStatusType type)
        { var status = new EnemyStatusEffect(enemyId, type); _enemyStatuses.Add(status); _statusIndex.Add(GetStatusKey(enemyId, type), status); return status; }

        /// <summary>제거된 적의 모든 상태 이상을 목록과 조회 인덱스에서 삭제한다.</summary>
        internal void RemoveEnemyStatuses(int enemyId)
        {
            for (int i = _enemyStatuses.Count - 1; i >= 0; i--)
                if (_enemyStatuses[i].EnemyId == enemyId) { RemoveStatusIndex(_enemyStatuses[i]); _enemyStatuses.RemoveAt(i); }
        }

        /// <summary>상태 이상을 조회 인덱스에서 삭제한다.</summary>
        private void RemoveStatusIndex(EnemyStatusEffect status) { _statusIndex.Remove(GetStatusKey(status.EnemyId, status.Type)); }

        /// <summary>적 ID와 상태 이상 종류를 하나의 조회 키로 합친다.</summary>
        private static int GetStatusKey(int enemyId, EnemyStatusType type) => enemyId * STATUS_TYPE_COUNT + (int)type;

        /// <summary>시험 도크 초기화에 맞춰 상태 이상 목록과 조회 인덱스를 비우고 판정 시각을 0으로 되돌린다.</summary>
        internal void ClearAll()
        {
            _enemyStatuses.Clear(); _statusIndex.Clear(); _statusTime = 0;
        }
    }
}
