using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>벽·보호막을 통과하는 원형 파동으로 적마다 한 번 피해·방사형 밀어내기·후속 사건을 처리한다.</summary>
    internal sealed class RadiateBehaviorRuntime : LaunchBehaviorRuntime
    {
        public override string Kind => RadiateBehaviorRules.RADIATE_KIND;

        /// <summary>생성 위치를 파동 중심으로 사용하므로 발사의 사각형 위치 보정을 생략한다.</summary>
        public override void PlaceSpawn(RuneSimulation simulation, SpellAction action, SpellStats stats, int index,
            bool isFromEvent, SimulationPlayer caster, ref SimVector position, ref SimVector direction)
        {
        }

        /// <summary>이번 틱에 원형 띠와 처음 접촉한 적을 시간·ID 순서로 타격한다. 적중·장애물로 파동을 소멸시키지 않는다.</summary>
        public override void Judge(RuneSimulation simulation, SimulationSpellEntity entity)
        {
            SimVector previous = entity.PreviousPosition;
            SimVector next = entity.Position;
            SimVector center = previous - entity.PreviousDirection * entity.ArcRadius;
            var contacts = new List<WaveContact>();
            IReadOnlyList<SimulationEnemy> enemies = simulation.Enemies;
            for (int index = 0; index < enemies.Count; index++)
            {
                SimulationEnemy enemy = enemies[index];
                if (!enemy.IsAlive || entity.HitTimes.ContainsKey(enemy.Id)) continue;
                double fraction = SpellGeometry.ArcContactFraction(entity, previous, next, enemy.Position, enemy.Radius);
                if (!double.IsPositiveInfinity(fraction)) contacts.Add(new WaveContact(enemy, fraction));
            }
            contacts.Sort((a, b) =>
            {
                int order = a.Fraction.CompareTo(b.Fraction);
                return order != 0 ? order : a.Enemy.Id.CompareTo(b.Enemy.Id);
            });
            foreach (WaveContact contact in contacts)
            {
                SimulationEnemy enemy = contact.Enemy;
                if (!enemy.IsAlive) continue;
                SimVector radial = enemy.Position - center;
                if (radial.Length <= RuneSimulation.COLLISION_EPSILON) radial = entity.Direction;
                double radius = entity.Stats.Speed * (entity.Age + RuneSimulation.STEP_SECONDS * contact.Fraction);
                SimVector point = center + radial.Normalized() * radius;
                entity.HitTimes[enemy.Id] = simulation.Time;
                simulation.Hit(entity, enemy, entity.Stats.Damage, false, radial);
                entity.Hits++;
                simulation.RaiseHitEvent(entity, point, enemy);
            }
        }

        /// <summary>이번 틱의 대상과 최초 접촉 비율을 보관해 정렬 중 충돌 계산을 반복하지 않는다.</summary>
        private readonly struct WaveContact
        {
            private readonly SimulationEnemy _enemy;
            private readonly double _fraction;
            public SimulationEnemy Enemy => _enemy;
            public double Fraction => _fraction;

            /// <summary>적과 원형 띠의 최초 접촉 비율을 저장한다.</summary>
            public WaveContact(SimulationEnemy enemy, double fraction)
            {
                _enemy = enemy;
                _fraction = fraction;
            }
        }
    }
}
