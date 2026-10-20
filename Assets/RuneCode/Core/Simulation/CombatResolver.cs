using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>마법 개체의 범위·직접 피해, 전격 연쇄와 밀어내기, 적중·보호막·소멸 이벤트 실행을 처리한다. 자체 상태는 없다.</summary>
    internal sealed class CombatResolver
    {
        private readonly SimulationWorld _world;
        private readonly SimulationFeedback _feedback;
        private readonly SimulationStats _stats;
        private readonly EnemyStatusSystem _statuses;
        private readonly EnemySystem _enemy;
        private readonly SpellExecutor _executor;
        private readonly RuneSimulation _simulation;

        /// <summary>월드·표시 버퍼·통계·상태 이상·적 시스템, 이벤트 후속을 실행할 스펠 실행기와 시뮬레이션 참조로 만든다.</summary>
        internal CombatResolver(SimulationWorld world, SimulationFeedback feedback, SimulationStats stats, EnemyStatusSystem statuses, EnemySystem enemy, SpellExecutor executor, RuneSimulation simulation)
        {
            _world = world;
            _feedback = feedback;
            _stats = stats;
            _statuses = statuses;
            _enemy = enemy;
            _executor = executor;
            _simulation = simulation;
        }

        /// <summary>
        /// 범위 마법의 가까운 적부터 대상별 주기에 맞춰 피해를 적용하고, 실제 피해 타격 횟수에 따른 행동별 규칙으로 OnHit을 낸다.
        /// isBurst이면 대상별 한 번만 적중하고 확장 범위가 대상과 처음 겹친 접촉 위치로 OnHit을 낸다(Burst).
        /// </summary>
        internal void HitArea(SimulationSpellEntity entity, bool isBurst)
        {
            var targets = new List<SimulationEnemy>();
            foreach (SimulationEnemy enemy in _world.Enemies)
            {
                if (enemy.IsAlive && SpellGeometry.IsInsideArea(entity, enemy.Position, enemy.Radius)) targets.Add(enemy);
            }
            targets.Sort((a, b) =>
            {
                int distanceOrder = (a.Position - entity.Position).LengthSquared.CompareTo((b.Position - entity.Position).LengthSquared);
                return distanceOrder != 0 ? distanceOrder : a.Id.CompareTo(b.Id);
            });
            double interval = entity.Behavior.GetAreaHitInterval(entity.Stats);
            foreach (SimulationEnemy enemy in targets)
            {
                if (!enemy.IsAlive) continue;
                bool hasHit = entity.HitTimes.TryGetValue(enemy.Id, out double lastTime);
                if (!hasHit || (!isBurst && _simulation.Time - lastTime + 0.000001 >= interval))
                {
                    entity.HitTimes[enemy.Id] = _simulation.Time;
                    SimVector pushDirection = enemy.Position - entity.Position;
                    if (pushDirection.Length <= RuneSimulation.COLLISION_EPSILON) pushDirection = entity.Direction;
                    double previousHp = enemy.Hp;
                    Hit(entity, enemy, entity.Stats.Damage, entity.Behavior.IsDamageOverTime, pushDirection);
                    // 폭발은 대상별 한 번, 잔류는 실제 피해 타격 횟수에 따라 접촉 위치에서 후속 행동을 실행한다.
                    if (isBurst || (enemy.Hp < previousHp && entity.Behavior.RecordPeriodicHit(entity, enemy.Id)))
                        RaiseHitEvent(entity, SpellGeometry.GetContactPoint(entity, enemy.Position), enemy);
                }
            }
        }

        /// <summary>스펠 피해·상태와 전격 연쇄를 처리하며 관통 직접 피해는 보정 후에도 최소 1을 유지한다. 직접 실제 피해에 비례한 밀어내기를 pushDirection으로 예약하고 잔류 피해를 누적한다.</summary>
        internal void Hit(SimulationSpellEntity entity, SimulationEnemy enemy, double damage, bool isDot, SimVector pushDirection)
        {
            double minimumDamage = entity.Behavior.TracksDirectHit && entity.Stats.Pierce > 0 && !isDot ? 1d : 0d;
            double actual = ApplyDamage(enemy, damage, entity.Element, entity.Kind, entity.Action.Noise, isDot, entity.Direction, true, minimumDamage, entity.Action);
            if (_enemy.IsSeparationFixed(enemy)) enemy.CancelDisplacement();
            else if (actual > 0 && enemy.IsAlive && entity.Stats.KnockbackDistance > 0)
            {
                double referenceDamage = GameData.Runes.Get("mod.knockback").Stats.KnockbackReferenceDamage;
                double distance = entity.Stats.KnockbackDistance * actual / referenceDamage;
                enemy.QueueDisplacement(pushDirection, distance, entity.Stats.KnockbackSeconds, _world.Tick);
            }
            if (entity.Element == "arc")
            {
                RuneStats arc = GameData.Runes.Get("elem.arc").Stats;
                var chain = new List<SimulationEnemy>();
                foreach (SimulationEnemy other in _world.Enemies)
                    if (other != enemy && other.IsAlive && SimVector.Distance(other.Position, enemy.Position) <= arc.ArcRange) chain.Add(other);
                chain.Sort((a, b) =>
                {
                    int distanceOrder = (a.Position - enemy.Position).LengthSquared.CompareTo((b.Position - enemy.Position).LengthSquared);
                    return distanceOrder != 0 ? distanceOrder : a.Id.CompareTo(b.Id);
                });
                for (int i = 0; i < Math.Min(arc.ArcTargets, chain.Count); i++)
                {
                    actual += ApplyDamage(chain[i], damage * arc.ArcMultiplier, "arc", entity.Kind, entity.Action.Noise, isDot, (chain[i].Position - enemy.Position).Normalized(), true, sourceAction: entity.Action, damageStatus: "arc_chain");
                    _feedback.AddArcLink(enemy.Position, chain[i].Position);
                }
                if (actual > 0) _stats.ArcDamageEventCount++;
            }
            if (entity.Behavior.AccumulatesPersistDamage) _stats.PersistDamage += actual;
        }

        /// <summary>
        /// 개체의 OnHit 이벤트를 지정 위치·대상(벽이면 null)으로 실행한다. 직접 충돌을 추적하는 행동(발사)은 직접 충돌로 기록하고
        /// 첫 이벤트이면 OnFirstHitOrExpire도 실행한다.
        /// </summary>
        internal void RaiseHitEvent(SimulationSpellEntity entity, SimVector position, SimulationEnemy target)
        {
            _stats.GetActionCombatStats(entity.Action).RecordOnHit();
            bool tracksDirectHit = entity.Behavior.TracksDirectHit;
            if (tracksDirectHit) entity.HasDirectHit = true;
            SpellContext context = new SpellContext(position, GetEventDirection(entity, position, target), target, true,
                entity.CastNoiseElement, entity.Modifiers, entity.CostMultiplier, entity.Caster, entity.Id, onHitSource: entity.Action);
            bool isFirstEvent = entity.Behavior.TracksFirstEvent && entity.TryMarkFirstEvent();
            _executor.Execute(entity.Action.OnHit, context.WithManaPrepaid(true));
            if (isFirstEvent) _executor.Execute(entity.Action.OnFirstHitOrExpire, context);
        }

        /// <summary>
        /// 발사체의 적중 보정과 피해 배율을 보호막 체력에 적용하고 명중·소멸 이벤트를 실행한다.
        /// OnHit과 첫 이벤트는 접촉 위치와 쉴드 소유자 대상으로, OnExpire는 개체 위치와 대상 없이 낸다.
        /// </summary>
        internal void RaiseShieldHitEvent(SimulationSpellEntity entity, SimVector contact, SimulationEnemy shieldOwner)
        {
            double damage = entity.Stats.Damage;
            bool isConsumed = true;
            entity.ApplyHitModifiers(_simulation, shieldOwner, ref damage, ref isConsumed);
            shieldOwner.HurtShield(damage * _world.DamageMultiplier * _world.Adaptation.GetMultiplier(entity.Element, entity.Kind));
            RaiseHitEvent(entity, contact, shieldOwner);
            RaiseExpireEvent(entity);
        }

        /// <summary>
        /// 적중 후속 실행의 방향을 접촉 위치(시작점)에서 적중 대상의 위치(도착점)로 반환한다(백서 v7 6장).
        /// 대상이 없거나 두 위치가 같으면 개체의 진행 방향을 쓴다.
        /// </summary>
        private static SimVector GetEventDirection(SimulationSpellEntity entity, SimVector contact, SimulationEnemy target)
        {
            if (target == null) return entity.Direction;
            SimVector offset = target.Position - contact;
            return offset.Length > 0.000001 ? offset.Normalized() : entity.Direction;
        }

        /// <summary>피해에 적응·이지스 방패와 minimumDamage 하한을 반영하고 보스 무적은 유지하며 실제 피해와 학습을 기록한다.</summary>
        internal double ApplyDamage(SimulationEnemy enemy, double damage, string element, string form, bool noise, bool isDot, SimVector direction, bool applyStatus,
            double minimumDamage = 0d, SpellAction sourceAction = null, string damageStatus = null)
        {
            if (!enemy.IsAlive || enemy.IsPatching) return 0;
            double actual = damage * _world.DamageMultiplier * _world.Adaptation.GetMultiplier(element, form);
            if (enemy.Definition.HasTrait(EnemyDefinition.TRAIT_AEGIS_SHIELD) && !_statuses.HasEnemyStatus(enemy, EnemyStatusType.Emp) && form == "bolt" && !isDot && SimVector.Dot(enemy.Facing, direction * -1) >= Math.Cos(_world.Balance.Combat.AegisAngle * 0.5 * RuneSimulation.DEGREES_TO_RADIANS)) actual *= 1 - _world.Balance.Combat.AegisReduction;
            bool relayAlive = false;
            foreach (SimulationEnemy relay in _world.Enemies)
            {
                if (!relay.Definition.HasTrait(EnemyDefinition.TRAIT_RELAY_AURA) || !relay.IsAlive) continue;
                relayAlive = true;
                break;
            }
            actual = enemy.Hurt(Math.Max(minimumDamage, actual), _simulation.Time);
            if (applyStatus) _statuses.ApplyStatus(enemy, element, form, noise, damage, sourceAction);
            if (actual > 0)
            {
                if (sourceAction != null) _stats.GetActionCombatStats(sourceAction).RecordDamage(actual, element, damageStatus);
                _stats.EnemyDamageEventCount++;
                enemy.SetDamageTags(element, form);
                _stats.RecordEnemyDamage(actual, _world.Tick);
                _feedback.AddDamageNumber(enemy.Position, actual, element);
                _world.Adaptation.Learn(element, form, _simulation.Time, actual, isDot, noise, relayAlive);
            }
            if (enemy.Boss != null && enemy.Boss.IsGovernorPattern && enemy.IsAlive) _enemy.CheckBossPatch(enemy, enemy.Boss);
            return actual;
        }

        /// <summary>소멸 이벤트를 개체 위치와 대상 없이 실행한다. OnExpire 다음 첫 이벤트 순서다.</summary>
        internal void RaiseExpireEvent(SimulationSpellEntity entity)
        {
            SpellContext context = new SpellContext(entity.Position, entity.Direction, null, true,
                entity.CastNoiseElement, entity.Modifiers, entity.CostMultiplier, entity.Caster, entity.Id);
            bool isFirstEvent = entity.Behavior.TracksFirstEvent && entity.TryMarkFirstEvent();
            _executor.Execute(entity.Action.OnExpire, context);
            if (isFirstEvent) _executor.Execute(entity.Action.OnFirstHitOrExpire, context);
        }
    }
}
