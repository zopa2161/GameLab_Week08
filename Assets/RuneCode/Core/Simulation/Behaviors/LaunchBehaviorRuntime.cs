using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>
    /// 발사 행동의 실행 동작이다. 매 틱 직진하고(유도는 이동 모드, 퍼짐은 배치 모드가 처리) 벽·원형 쉴드·적 순서로 적중을 판정한다.
    /// 적에게 맞으면 기본적으로 소멸하며, 관통은 적중 모드가 처리한다.
    /// 첫 적중·소멸 이벤트를 추적하며 직접 충돌한 발사체는 소멸 이벤트를 내지 않는다.
    /// </summary>
    internal class LaunchBehaviorRuntime : SpellBehaviorRuntime
    {
        public override string Kind => SpellGrammar.FORM_BOLT;
        public override bool TracksDirectHit => true;

        /// <summary>
        /// 사각형(Beam) 발사체는 뒤쪽 면을 실행 위치에 두도록 길이의 절반만큼 앞으로 옮긴다(백서 v7).
        /// 여러 발의 퍼짐 방향은 이 처리보다 먼저 다중 모드(MultiModifierRuntime)가 정한다.
        /// </summary>
        public override void PlaceSpawn(RuneSimulation simulation, SpellAction action, SpellStats stats, int index,
            bool isFromEvent, SimulationPlayer caster, ref SimVector position, ref SimVector direction)
        {
            if (action.MagicType == SpellGrammar.MAGIC_TYPE_BOX)
                position += direction.Normalized() * (stats.BoxLength * 0.5);
        }

        /// <summary>진행 방향으로 이동 속도만큼 직진한다. 유도는 이동 모드(HomingModifierRuntime)가 이동을 맡아 처리한다.</summary>
        public override void MoveAlongPath(RuneSimulation simulation, SimulationSpellEntity entity)
            => entity.Move(entity.Position + entity.Direction * (entity.Stats.Speed * RuneSimulation.STEP_SECONDS), entity.Direction);

        /// <summary>
        /// 이번 틱 이동 구간(PreviousPosition → Position)에서 벽·원형 쉴드 충돌과 가장 가까운 순서의 관통 적중을 처리한다.
        /// 충돌하면 접촉 위치로 옮기고 소멸 표시한다. 벽을 무시하는 경로로 움직인 틱(IsPassingWalls)은 벽을 판정하지 않는다.
        /// </summary>
        public override void Judge(RuneSimulation simulation, SimulationSpellEntity entity)
        {
            IReadOnlyList<SimulationEnemy> enemies = simulation.Enemies;
            SimVector direction = entity.Direction;
            SpellStats stats = entity.Stats;
            SimVector previous = entity.PreviousPosition;
            SimVector next = entity.Position;
            // 사각형 발사체는 앞면 중심과 폭으로 벽을 검사하고 보호막은 전체 모양의 이동 경로로 검사한다.
            // 공전 궤도·진입처럼 벽을 무시하는 경로로 움직인 틱에는 벽 충돌을 판정하지 않는다.
            double wallFraction = entity.IsPassingWalls ? double.PositiveInfinity
                : GetWallContactFraction(simulation, entity, previous, next, direction);
            double shieldFraction = double.PositiveInfinity;
            SimulationEnemy shieldOwner = null;
            for (int index = 0; index < enemies.Count; index++)
            {
                SimulationEnemy shield = enemies[index];
                if (!shield.IsAlive || !shield.Definition.HasTrait(EnemyDefinition.TRAIT_CIRCLE_SHIELD)) continue;
                if (!CanHitShield(entity, previous, next, shield)) continue;
                double fraction = GetContactFraction(entity, previous, next, direction, shield.Position, shield.Definition.ShieldRadius);
                if (fraction < shieldFraction) { shieldFraction = fraction; shieldOwner = shield; }
            }
            double stopFraction = Math.Min(wallFraction, shieldFraction);
            var targets = new List<ProjectileContact>();
            for (int index = 0; index < enemies.Count; index++)
            {
                SimulationEnemy enemy = enemies[index];
                if (!enemy.IsAlive || entity.HitTimes.ContainsKey(enemy.Id)) continue;
                double fraction = GetContactFraction(entity, previous, next, direction, enemy.Position, enemy.Radius);
                if (!double.IsPositiveInfinity(fraction)) targets.Add(new ProjectileContact(enemy, fraction));
            }
            targets.Sort((a, b) =>
            {
                int distanceOrder = a.Fraction.CompareTo(b.Fraction);
                return distanceOrder != 0 ? distanceOrder : a.Enemy.Id.CompareTo(b.Enemy.Id);
            });
            foreach (ProjectileContact target in targets)
            {
                SimulationEnemy enemy = target.Enemy;
                if (!enemy.IsAlive) continue;
                if (target.Fraction + RuneSimulation.COLLISION_EPSILON >= stopFraction) break;
                // 발사체는 기본적으로 첫 적중에 소멸한다. 관통 등 적중 모드가 피해와 소멸 여부를 고친다.
                double damage = stats.Damage;
                bool isConsumed = true;
                entity.ApplyHitModifiers(simulation, enemy, ref damage, ref isConsumed);
                SimVector pushDirection = next - previous;
                if (pushDirection.Length <= RuneSimulation.COLLISION_EPSILON) pushDirection = direction;
                entity.HitTimes[enemy.Id] = simulation.Time; simulation.Hit(entity, enemy, damage, false, pushDirection); entity.Hits++;
                SimVector contact = GetContactPoint(entity, previous, next, direction, target.Fraction, enemy.Position);
                simulation.RaiseHitEvent(entity, contact, enemy);
                if (isConsumed) { entity.Move(contact, direction); entity.HasExpired = true; break; }
            }
            if (!entity.HasExpired && !double.IsPositiveInfinity(stopFraction))
            {
                SimVector contact = previous + (next - previous) * stopFraction;
                entity.Move(contact, direction); entity.HasDirectHit = true; entity.HasExpired = true;
                SimVector eventPoint = GetObstacleContactPoint(simulation, entity, previous, next, direction, stopFraction,
                    wallFraction < shieldFraction ? null : shieldOwner);
                if (wallFraction < shieldFraction) simulation.RaiseHitEvent(entity, eventPoint, null);
                else simulation.RaiseShieldHitEvent(entity, eventPoint, shieldOwner);
            }
        }

        /// <summary>발사체 부채꼴의 꼭짓점이다. 중심에서 진행 반대쪽으로 반경만큼 뒤다.</summary>
        public override SimVector GetConeApex(SimulationSpellEntity entity)
            => SimulationSpellEntity.GetConeApex(entity.Position, entity.Direction, entity.Stats.Radius);

        /// <summary>발사체 부채꼴의 반경이다. 중심 앞뒤로 반경씩인 지름이다.</summary>
        public override double GetConeReach(SimulationSpellEntity entity) => entity.Stats.Radius * 2;

        /// <summary>투사체의 기본 원형 반폭 또는 사각 앞면으로 첫 벽 접촉 비율을 반환한다.</summary>
        protected virtual double GetWallContactFraction(RuneSimulation simulation, SimulationSpellEntity entity,
            SimVector previous, SimVector next, SimVector direction)
        {
            SimVector front = entity.IsBox ? direction.Normalized() * (entity.Stats.BoxLength * 0.5) : SimVector.Zero;
            double radius = entity.IsBox ? entity.Stats.BoxWidth * 0.5 : entity.Radius;
            return simulation.FirstWallFraction(previous + front, next + front, radius);
        }

        /// <summary>기존 투사체 모양과 대상 원의 첫 접촉 비율을 반환한다.</summary>
        protected virtual double GetContactFraction(SimulationSpellEntity entity, SimVector previous,
            SimVector next, SimVector direction, SimVector target, double targetRadius)
            => SpellGeometry.SpellContactFraction(entity, previous, next, direction, target, targetRadius);

        /// <summary>기존 발사체는 적 중심을 적중 사건 위치로 사용한다.</summary>
        protected virtual SimVector GetContactPoint(SimulationSpellEntity entity, SimVector previous,
            SimVector next, SimVector direction, double fraction, SimVector target) => target;

        /// <summary>보호막 밖에서 보호막을 향해 이동하는 경우에만 차단을 검사한다.</summary>
        protected virtual bool CanHitShield(SimulationSpellEntity entity, SimVector previous, SimVector next, SimulationEnemy shield)
        {
            SimVector offset = previous - shield.Position;
            return offset.Length >= shield.Definition.ShieldRadius - RuneSimulation.COLLISION_EPSILON
                && SimVector.Dot(next - previous, offset) < 0;
        }

        /// <summary>기존 발사체는 벽에서 사각 앞면, 보호막에서 투사체 중심을 사건 위치로 사용한다.</summary>
        protected virtual SimVector GetObstacleContactPoint(RuneSimulation simulation, SimulationSpellEntity entity,
            SimVector previous, SimVector next, SimVector direction, double fraction, SimulationEnemy shield)
        {
            SimVector contact = previous + (next - previous) * fraction;
            return shield == null && entity.IsBox ? contact + direction.Normalized() * (entity.Stats.BoxLength * 0.5) : contact;
        }

        /// <summary>이번 틱에 한 번 계산한 적 접촉 비율을 정렬과 피해 처리에서 공유한다.</summary>
        private readonly struct ProjectileContact
        {
            private readonly SimulationEnemy _enemy;
            private readonly double _fraction;
            public SimulationEnemy Enemy => _enemy;
            public double Fraction => _fraction;

            /// <summary>대상 적과 이동 구간의 최초 접촉 비율을 보관한다.</summary>
            public ProjectileContact(SimulationEnemy enemy, double fraction) { _enemy = enemy; _fraction = fraction; }
        }
    }
}
