using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>
    /// 발사 행동의 실행 동작이다. 여러 발이면 퍼짐 각도로 나눠 쏘고, 매 틱 유도·벽·원형 쉴드·관통 순서로 이동과 적중을 처리한다.
    /// 첫 적중·소멸 이벤트를 추적하며 직접 충돌한 발사체는 소멸 이벤트를 내지 않는다.
    /// </summary>
    internal sealed class LaunchBehaviorRuntime : SpellBehaviorRuntime
    {
        private const double DEGREES_TO_RADIANS = Math.PI / 180;

        public override string Kind => SpellGrammar.FORM_BOLT;
        public override bool TracksDirectHit => true;

        /// <summary>
        /// 여러 발이면 index에 따라 좌우 퍼짐 각도 안에서 방향을 나눈다.
        /// 사각형(Beam) 발사체는 뒤쪽 면을 실행 위치에 두도록 길이의 절반만큼 앞으로 옮긴다(백서 v7).
        /// </summary>
        public override void PlaceSpawn(RuneSimulation simulation, SpellAction action, SpellStats stats, int index,
            bool isFromEvent, SimulationPlayer caster, ref SimVector position, ref SimVector direction)
        {
            if (action.Count > 1) direction = direction.Rotated((index / (double)(action.Count - 1) * 2 - 1) * stats.SpreadAngle * DEGREES_TO_RADIANS);
            if (action.MagicType == SpellGrammar.MAGIC_TYPE_BOX)
                position += direction.Normalized() * (stats.BoxLength * 0.5);
        }

        /// <summary>볼트의 유도 방향, 벽 충돌 및 가장 가까운 순서의 관통 적중을 처리한다.</summary>
        public override void Advance(RuneSimulation simulation, SimulationSpellEntity entity)
        {
            IReadOnlyList<SimulationEnemy> enemies = simulation.Enemies;
            SimVector direction = entity.Direction;
            SpellStats stats = entity.Stats;
            if (stats.HomingTurn > 0)
            {
                SimulationEnemy target = simulation.NearestEnemy(entity.Position, stats.HomingRange, entity.HitTimes);
                if (target != null)
                {
                    double current = Math.Atan2(direction.Y, direction.X); SimVector toTarget = target.Position - entity.Position;
                    double wanted = Math.Atan2(toTarget.Y, toTarget.X); double delta = Math.Atan2(Math.Sin(wanted - current), Math.Cos(wanted - current));
                    double turn = stats.HomingTurn * DEGREES_TO_RADIANS * RuneSimulation.STEP_SECONDS;
                    direction = direction.Rotated(Math.Max(-turn, Math.Min(turn, delta)));
                }
            }
            SimVector previous = entity.Position;
            SimVector next = previous + direction * (stats.Speed * RuneSimulation.STEP_SECONDS);
            // 사각형 발사체는 앞면 중심과 폭으로 벽을 검사하고 보호막은 전체 모양의 이동 경로로 검사한다.
            SimVector front = entity.IsBox ? direction.Normalized() * (stats.BoxLength * 0.5) : SimVector.Zero;
            double collisionRadius = entity.IsBox ? stats.BoxWidth * 0.5 : entity.Radius;
            double wallFraction = simulation.FirstWallFraction(previous + front, next + front, collisionRadius);
            double shieldFraction = double.PositiveInfinity;
            SimulationEnemy shieldOwner = null;
            for (int index = 0; index < enemies.Count; index++)
            {
                SimulationEnemy shield = enemies[index];
                if (!shield.IsAlive || !shield.Definition.HasTrait(EnemyDefinition.TRAIT_CIRCLE_SHIELD)) continue;
                SimVector fromShield = previous - shield.Position;
                if (fromShield.Length < shield.Definition.ShieldRadius - RuneSimulation.COLLISION_EPSILON || SimVector.Dot(next - previous, fromShield) >= 0) continue;
                double fraction = SpellGeometry.SpellContactFraction(entity, previous, next, direction, shield.Position, shield.Definition.ShieldRadius);
                if (fraction < shieldFraction) { shieldFraction = fraction; shieldOwner = shield; }
            }
            double stopFraction = Math.Min(wallFraction, shieldFraction);
            entity.Move(next, direction);
            var targets = new List<SimulationEnemy>();
            for (int index = 0; index < enemies.Count; index++)
            {
                SimulationEnemy enemy = enemies[index];
                bool isInside = entity.IsCone
                    ? SpellGeometry.IntersectsConeSweep(previous, next, direction, entity.Radius, entity.ConeAngle, enemy.Position, enemy.Radius)
                    : entity.IsBox
                    ? SpellGeometry.IntersectsBoxSweep(previous, next, direction, entity.Stats.BoxWidth * 0.5,
                        entity.Stats.BoxLength * 0.5, enemy.Position, enemy.Radius)
                    : SpellGeometry.SegmentDistance(previous, next, enemy.Position) <= entity.Radius + enemy.Radius;
                if (enemy.IsAlive && !entity.HitTimes.ContainsKey(enemy.Id) && isInside) targets.Add(enemy);
            }
            targets.Sort((a, b) =>
            {
                int distanceOrder = SpellGeometry.SpellContactFraction(entity, previous, next, direction, a.Position, a.Radius)
                    .CompareTo(SpellGeometry.SpellContactFraction(entity, previous, next, direction, b.Position, b.Radius));
                return distanceOrder != 0 ? distanceOrder : a.Id.CompareTo(b.Id);
            });
            foreach (SimulationEnemy enemy in targets)
            {
                if (!enemy.IsAlive) continue;
                if (SpellGeometry.SpellContactFraction(entity, previous, next, direction, enemy.Position, enemy.Radius) + RuneSimulation.COLLISION_EPSILON >= stopFraction) break;
                double multiplier = Math.Max(0, 1 - entity.Hits * stats.PierceLoss);
                entity.HitTimes[enemy.Id] = simulation.Time; simulation.Hit(entity, enemy, stats.Damage * multiplier, false); entity.Hits++;
                simulation.RaiseHitEvent(entity, enemy.Position, enemy);
                if (entity.Hits > stats.Pierce) { entity.Move(enemy.Position, direction); entity.HasExpired = true; break; }
            }
            if (!entity.HasExpired && !double.IsPositiveInfinity(stopFraction))
            {
                SimVector contact = previous + (next - previous) * stopFraction;
                entity.Move(contact, direction); entity.HasDirectHit = true; entity.HasExpired = true;
                if (wallFraction < shieldFraction) simulation.RaiseHitEvent(entity, contact + front, null);
                else simulation.RaiseShieldHitEvent(entity, contact, shieldOwner);
            }
        }

        /// <summary>발사체 부채꼴의 꼭짓점이다. 중심에서 진행 반대쪽으로 반경만큼 뒤다.</summary>
        public override SimVector GetConeApex(SimulationSpellEntity entity)
            => SimulationSpellEntity.GetConeApex(entity.Position, entity.Direction, entity.Stats.Radius);

        /// <summary>발사체 부채꼴의 반경이다. 중심 앞뒤로 반경씩인 지름이다.</summary>
        public override double GetConeReach(SimulationSpellEntity entity) => entity.Stats.Radius * 2;
    }
}
