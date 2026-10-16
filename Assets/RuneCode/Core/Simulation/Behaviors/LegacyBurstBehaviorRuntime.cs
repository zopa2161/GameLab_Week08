using System;

namespace RuneCode
{
    /// <summary>
    /// 레거시 폭발 행동의 실행 동작이다. 생성 위치에서 크기 0부터 확장 시간 동안 커지며, 확장 중 처음 겹친 대상마다 한 번 적중하고 OnHit을 낸다.
    /// 확장이 끝나면 잔상 시간 뒤 사라지며 소멸 이벤트는 내지 않는다.
    /// </summary>
    internal sealed class LegacyBurstBehaviorRuntime : SpellBehaviorRuntime
    {
        private const string STATS_RUNE_ID = "form.legacy_burst";
        private const double TIME_EPSILON = 0.000001;

        public override string Kind => SpellGrammar.FORM_LEGACY_BURST;
        public override bool RemovesWhenExpired => false;
        public override bool PlacesBoxBackAtPosition => true;
        public override bool RaisesExpireEvent => false;

        /// <summary>레거시 폭발 수치 룬의 확장 시간을 반환한다.</summary>
        public override double GetWarmupSeconds(SpellStats stats) => GameData.Runes.Get(STATS_RUNE_ID).Stats.ExpandSeconds;

        /// <summary>범위형 생성 위치 규칙으로 배치한다.</summary>
        public override void PlaceSpawn(RuneSimulation simulation, SpellAction action, SpellStats stats, int index,
            bool isFromEvent, SimulationPlayer caster, ref SimVector position, ref SimVector direction)
            => PlaceAreaSpawn(simulation, action, stats, index, isFromEvent, caster, ref position, direction);

        /// <summary>Burst는 크기 0에서 확장을 시작하므로 생성 즉시 한 번 판정한다(백서 v3).</summary>
        public override void OnSpawned(RuneSimulation simulation, SimulationSpellEntity entity) => simulation.HitArea(entity, true);

        /// <summary>커진 반경으로 판정하도록 수명이 늘어난 뒤 확장이 끝날 때까지 새 대상을 판정한다.</summary>
        public override void AfterAdvance(RuneSimulation simulation, SimulationSpellEntity entity)
        {
            if (entity.IsExpanding) simulation.HitArea(entity, true);
        }

        /// <summary>확장 시간 동안 0에서 최종 반경까지 커지는 현재 반경을 반환한다.</summary>
        public override double GetRadius(SimulationSpellEntity entity) => entity.Stats.Radius * GetExpansionProgress(entity);

        /// <summary>확장 진행률(0~1)을 반환한다. 확장 시간이 없으면 1이다.</summary>
        public override double GetExpansionProgress(SimulationSpellEntity entity)
            => entity.WarmupSeconds > 0 ? Math.Min(1, entity.Age / entity.WarmupSeconds) : 1;

        /// <summary>확장 시간 안이면 true를 반환한다.</summary>
        public override bool IsExpanding(SimulationSpellEntity entity) => entity.Age <= entity.WarmupSeconds + TIME_EPSILON;

        /// <summary>확장 시간 + 잔상 시간을 수명으로 반환한다.</summary>
        public override double GetLifetime(SimulationSpellEntity entity) => entity.WarmupSeconds + entity.VisualSeconds;
    }
}
