using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>마법 개체의 이동·판정·수명 진행과 자연 소멸, 빔 중심선 벽 클립, 원형 이동의 첫 벽 접촉, 최근접 적 탐색을 처리한다. 자체 상태는 없다.</summary>
    internal sealed class SpellEntitySystem
    {
        private const double BEAM_CLIP_MIN_STEP = 4;
        private const double BEAM_CLIP_STEP_RATIO = 0.25;
        private const int BEAM_CLIP_REFINEMENT_STEPS = 6;
        private const int WALL_CONTACT_REFINEMENT_STEPS = 12;

        private readonly SimulationWorld _world;
        private readonly CombatResolver _combat;
        private readonly SpellRuntimeContext _runtime;

        /// <summary>월드·전투 판정기와 행동·모드 런타임에 넘길 런타임 컨텍스트로 만든다.</summary>
        internal SpellEntitySystem(SimulationWorld world, CombatResolver combat, SpellRuntimeContext runtime)
        {
            _world = world;
            _combat = combat;
            _runtime = runtime;
        }

        /// <summary>
        /// 마법 개체마다 이동 전 위치를 기록하고, 우선순위가 높은 이동 모드가 이동을 맡거나 없으면 행동의 기본 경로로 옮긴 뒤
        /// 행동의 판정을 처리한다. 이후 수명을 늘리고 소멸 이벤트를 한 번만 발생시킨다. 정지된 빔 채널 정리는 호출자가 바로 뒤에 한다.
        /// </summary>
        internal void AdvanceSpellEntities()
        {
            int initialCount = _world.SpellEntities.Count;
            int processed = 0;
            for (int i = 0; processed < initialCount && i < _world.SpellEntities.Count; processed++)
            {
                SimulationSpellEntity entity = _world.SpellEntities[i];
                entity.MarkPreviousPosition();
                if (!entity.MoveByModifiers(_runtime)) entity.Behavior.MoveAlongPath(_runtime, entity);
                entity.Behavior.Judge(_runtime, entity);
                entity.Advance(RuneSimulation.STEP_SECONDS);
                entity.Behavior.AfterAdvance(_runtime, entity);
                if ((entity.Behavior.RemovesWhenExpired && entity.HasExpired) || entity.Age + 0.000001 >= entity.Lifetime)
                { Expire(entity); _world.SpellEntities.RemoveAt(i); }
                else i++;
            }
        }

        /// <summary>
        /// 개체의 자연 소멸(수명·사거리 종료)을 처리하고 OnExpire를 소멸 위치에서 한 번 실행한다.
        /// 소멸 이벤트를 내지 않는 행동(Burst)과 직접 충돌한 발사체는 OnExpire를 내지 않는다. 명중 없이 사라지는 발사체만 OnFirstHitOrExpire도 실행한다.
        /// </summary>
        private void Expire(SimulationSpellEntity entity)
        {
            if (entity.HasExpired) return;
            entity.HasExpired = true;
            if (!entity.Behavior.RaisesExpireEvent || entity.HasDirectHit) return;
            _combat.RaiseExpireEvent(entity);
        }

        /// <summary>원점 origin과 방향 direction의 중심선을 최대 length까지 검사해 첫 벽 앞의 길이를 반환한다.</summary>
        internal double ClipBeamLength(SimVector origin, SimVector direction, double length)
        {
            double step = Math.Max(BEAM_CLIP_MIN_STEP, _world.Map.TileSize * BEAM_CLIP_STEP_RATIO);
            double free = 0;
            double distance = Math.Min(step, length);
            while (distance <= length + RuneSimulation.COLLISION_EPSILON)
            {
                if (_world.Map.IsWall(origin + direction * distance)) return RefineBeamClip(origin, direction, free, distance);
                free = distance;
                distance += step;
            }
            return length;
        }

        /// <summary>원점 origin과 방향 direction에서 통과 길이 free와 벽 진입 길이 hit 사이를 좁혀 벽 앞 길이를 반환한다.</summary>
        private double RefineBeamClip(SimVector origin, SimVector direction, double free, double hit)
        {
            for (int i = 0; i < BEAM_CLIP_REFINEMENT_STEPS; i++)
            {
                double middle = (free + hit) * 0.5;
                if (_world.Map.IsWall(origin + direction * middle)) hit = middle;
                else free = middle;
            }
            return free;
        }

        /// <summary>원형 개체의 직선 이동을 작은 구간으로 검사하고 첫 벽 접촉 비율을 반환한다.</summary>
        internal double FirstWallFraction(SimVector start, SimVector end, double radius)
        {
            int steps = Math.Max(1, (int)Math.Ceiling((end - start).Length / Math.Max(1, Math.Min(radius, _world.Map.TileSize * 0.25))));
            double last = 0;
            for (int i = 1; i <= steps; i++)
            {
                double high = i / (double)steps;
                if (_world.Map.CanOccupy(start + (end - start) * high, radius)) { last = high; continue; }
                for (int j = 0; j < WALL_CONTACT_REFINEMENT_STEPS; j++)
                {
                    double middle = (last + high) * 0.5;
                    if (_world.Map.CanOccupy(start + (end - start) * middle, radius)) last = middle; else high = middle;
                }
                return high;
            }
            return double.PositiveInfinity;
        }

        /// <summary>이미 적중한 적을 제외하고 탐지 반경 내 가장 가까운 생존 적을 반환한다.</summary>
        internal SimulationEnemy NearestEnemy(SimVector position, double range, Dictionary<int, double> excluded)
        { SimulationEnemy result = null; double best = range * range; foreach (SimulationEnemy enemy in _world.Enemies) { if (!enemy.IsAlive || excluded.ContainsKey(enemy.Id)) continue; double distance = (enemy.Position - position).LengthSquared; if (distance < best) { result = enemy; best = distance; } } return result; }
    }
}
