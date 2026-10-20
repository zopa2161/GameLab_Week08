using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RuneCode
{
    /// <summary>
    /// 잔류 행동의 실행 동작이다. 예고 시간 뒤 TickInterval마다 지속 피해를 주고 대상별 첫 타격과 이후 두 타격마다 OnHit을 발생시킨다.
    /// 기본 경로는 제자리이며(유도 이동은 이동 모드가 처리) 실제 피해는 잔류 피해 통계에 누적한다.
    /// </summary>
    internal sealed class PersistBehaviorRuntime : SpellBehaviorRuntime
    {
        private const string STATS_RUNE_ID = "form.zone";
        private const int HITS_PER_EVENT = 2;
        private const double TIME_EPSILON = 0.000001;

        public override string Kind => SpellGrammar.FORM_ZONE;
        public override bool PlacesBoxBackAtPosition => true;
        public override bool IsDamageOverTime => true;
        public override bool AccumulatesPersistDamage => true;

        /// <summary>잔류 개체별로 대상의 누적 피해 타격 횟수를 보관할 상태를 생성한다.</summary>
        public override object CreateState(SimulationSpellEntity entity) => new SortedDictionary<int, int>();

        /// <summary>대상의 피해 타격 횟수를 증가시키고 첫 타격 및 이후 두 타격마다 OnHit 실행 여부를 반환한다.</summary>
        public override bool RecordPeriodicHit(SimulationSpellEntity entity, int targetId)
        {
            var hitCounts = (SortedDictionary<int, int>)entity.BehaviorState;
            hitCounts.TryGetValue(targetId, out int count);
            hitCounts[targetId] = ++count;
            return (count - 1) % HITS_PER_EVENT == 0;
        }

        /// <summary>대상별 피해 타격 횟수를 고정 순서와 불변 형식으로 결정성 해시에 기록한다.</summary>
        public override void WriteState(SimulationSpellEntity entity, StringBuilder state)
        {
            state.Append("|persist");
            foreach (KeyValuePair<int, int> pair in (SortedDictionary<int, int>)entity.BehaviorState)
                state.Append('|').Append(pair.Key.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(pair.Value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>잔류 수치 룬의 예고 시간을 속도 수식 배율로 나눈 값을 반환한다. 속도 수식만큼 예고가 빨리 끝난다.</summary>
        public override double GetWarmupSeconds(SpellStats stats)
            => GameData.Runes.Get(STATS_RUNE_ID).Stats.WarnSeconds / (double)stats.SpeedMultiplier;

        /// <summary>범위형 생성 위치 규칙으로 배치한다.</summary>
        public override void PlaceSpawn(SpellRuntimeContext context, SpellAction action, SpellStats stats, int index,
            bool isFromEvent, SimulationPlayer caster, ref SimVector position, ref SimVector direction)
            => PlaceAreaSpawn(context, action, stats, index, isFromEvent, caster, ref position, direction);

        /// <summary>예고가 없으면 생성 즉시 판정한다.</summary>
        public override void OnSpawned(SpellRuntimeContext context, SimulationSpellEntity entity)
        {
            Judge(context, entity);
        }

        /// <summary>예고가 끝난 뒤 수명 종료 시점 전까지만 매 틱 범위 판정을 처리한다.</summary>
        public override void Judge(SpellRuntimeContext context, SimulationSpellEntity entity)
        {
            if (entity.IsWarning || entity.Age + TIME_EPSILON >= entity.Lifetime) return;
            context.HitArea(entity, false);
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
