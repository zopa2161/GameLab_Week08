using System;

namespace RuneCode
{
    /// <summary>
    /// 잔류 행동의 실행 동작이다. 예고 시간 뒤 생성 위치에 남아 TickInterval마다 지속 피해를 주고 적중은 OnHit을 만들지 않는다.
    /// 기본 경로는 제자리이며(유도 이동은 이동 모드가 처리) 실제 피해는 잔류 피해 통계에 누적한다.
    /// </summary>
    internal sealed class PersistBehaviorRuntime : SpellBehaviorRuntime
    {
        private const string STATS_RUNE_ID = "form.zone";
        private const double TIME_EPSILON = 0.000001;

        public override string Kind => SpellGrammar.FORM_ZONE;
        public override bool PlacesBoxBackAtPosition => true;
        public override bool IsDamageOverTime => true;
        public override bool AccumulatesPersistDamage => true;

        /// <summary>잔류 수치 룬의 예고 시간을 속도 수식 배율로 나눈 값을 반환한다. 속도 수식만큼 예고가 빨리 끝난다.</summary>
        public override double GetWarmupSeconds(SpellStats stats)
            => GameData.Runes.Get(STATS_RUNE_ID).Stats.WarnSeconds / (double)stats.SpeedMultiplier;

        /// <summary>범위형 생성 위치 규칙으로 배치한다.</summary>
        public override void PlaceSpawn(RuneSimulation simulation, SpellAction action, SpellStats stats, int index,
            bool isFromEvent, SimulationPlayer caster, ref SimVector position, ref SimVector direction)
            => PlaceAreaSpawn(simulation, action, stats, index, isFromEvent, caster, ref position, direction);

        /// <summary>예고가 없으면 생성 즉시 판정한다. 보호 잔류는 판정하지 않는다(백서 v3).</summary>
        public override void OnSpawned(RuneSimulation simulation, SimulationSpellEntity entity)
        {
            if (entity.Action.SourceElement != "protection" && !entity.IsWarning) simulation.HitArea(entity, false);
        }

        /// <summary>예고가 끝난 뒤 매 틱 범위 판정을 처리한다.</summary>
        public override void Judge(RuneSimulation simulation, SimulationSpellEntity entity)
        {
            if (entity.IsWarning) return;
            simulation.HitArea(entity, false);
        }

        /// <summary>예고 시간 안이면 true를 반환한다.</summary>
        public override bool IsWarning(SimulationSpellEntity entity) => entity.Age + TIME_EPSILON < entity.WarmupSeconds;

        /// <summary>예고 진행률(0~1)을 반환한다. 예고 시간이 없으면 1이다.</summary>
        public override double GetWarnProgress(SimulationSpellEntity entity)
            => entity.WarmupSeconds > 0 ? Math.Min(1, entity.Age / entity.WarmupSeconds) : 1;

        /// <summary>예고 시간 + 유지 시간을 수명으로 반환한다.</summary>
        public override double GetLifetime(SimulationSpellEntity entity) => entity.WarmupSeconds + entity.Stats.Lifetime;

        /// <summary>같은 대상을 TickInterval마다 다시 판정한다.</summary>
        public override double GetAreaHitInterval(SpellStats stats) => stats.TickInterval;
    }
}
