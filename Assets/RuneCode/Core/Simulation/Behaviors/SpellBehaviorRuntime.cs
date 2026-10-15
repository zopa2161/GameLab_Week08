using System;
using System.Text;

namespace RuneCode
{
    /// <summary>
    /// 행동 하나의 시뮬레이션 동작이다. 생성 배치, 매 틱 진행, 판정 반경·수명 같은 개체 상태, 적중·소멸 이벤트 방식을 정한다.
    /// 컴파일 규칙은 RuneCode.Grammar의 SpellBehaviorRules가 같은 Kind로 맡는다.
    /// 새 행동은 이 클래스를 상속하고 SpellBehaviorRuntimeRegistry에 등록한다. 기본 구현은 제자리에 머무는 원형 개체다.
    /// </summary>
    internal abstract class SpellBehaviorRuntime
    {
        /// <summary>SpellAction.Form과 같은 런타임 종류다. 예: bolt</summary>
        public abstract string Kind { get; }

        /// <summary>판정 전 대기 시간(확장·예고 시간)을 반환한다. 기본은 0이다.</summary>
        public virtual double GetWarmupSeconds(SpellStats stats) => 0;

        /// <summary>
        /// 개체 index번째의 생성 위치와 방향을 정한다. position·direction은 실행 위치·방향으로 초기화되어 들어온다.
        /// isFromEvent는 적중 이벤트에서 시작한 실행인지, caster는 시전자다. 기본은 실행 위치·방향을 그대로 쓴다.
        /// </summary>
        public virtual void PlaceSpawn(RuneSimulation simulation, SpellAction action, SpellStats stats, int index,
            bool isFromEvent, SimulationPlayer caster, ref SimVector position, ref SimVector direction)
        {
        }

        /// <summary>개체를 목록에 추가한 직후 처리(생성 즉시 판정 등)를 한다. 기본은 없다.</summary>
        public virtual void OnSpawned(RuneSimulation simulation, SimulationSpellEntity entity)
        {
        }

        /// <summary>개체 생성 시 행동 전용 상태 객체를 만든다. 개체의 BehaviorState에 보관된다. 기본은 null이다.</summary>
        public virtual object CreateState(SimulationSpellEntity entity) => null;

        /// <summary>수명이 늘기 전 매 틱 이동·판정을 처리한다. 기본은 없다.</summary>
        public virtual void Advance(RuneSimulation simulation, SimulationSpellEntity entity)
        {
        }

        /// <summary>수명이 늘어난 뒤 매 틱 처리(확장 중 판정 등)를 한다. 기본은 없다.</summary>
        public virtual void AfterAdvance(RuneSimulation simulation, SimulationSpellEntity entity)
        {
        }

        /// <summary>개체의 HasExpired가 켜지면 수명 전이라도 제거하는지 나타낸다. 기본은 true다.</summary>
        public virtual bool RemovesWhenExpired => true;

        /// <summary>현재 판정 반경을 반환한다. 기본은 최종 반경이다.</summary>
        public virtual double GetRadius(SimulationSpellEntity entity) => entity.Stats.Radius;

        /// <summary>사각형 범위 길이의 현재 진행률(0~1)을 반환한다. 기본은 1이다.</summary>
        public virtual double GetExpansionProgress(SimulationSpellEntity entity) => 1;

        /// <summary>사각형 범위의 뒤쪽 면을 개체 위치에 두는지 나타낸다. false이면 개체 위치가 사각형 중심이다.</summary>
        public virtual bool PlacesBoxBackAtPosition => false;

        /// <summary>판정 전 예고 중인지 반환한다. 예고 중에는 효과·적중 판정을 하지 않는다. 기본은 false다.</summary>
        public virtual bool IsWarning(SimulationSpellEntity entity) => false;

        /// <summary>예고 진행률(0~1)을 반환한다. 예고가 없거나 끝났으면 1이다.</summary>
        public virtual double GetWarnProgress(SimulationSpellEntity entity) => 1;

        /// <summary>범위가 아직 확장 중이라 새 대상을 판정하는지 반환한다. 기본은 false다.</summary>
        public virtual bool IsExpanding(SimulationSpellEntity entity) => false;

        /// <summary>부채꼴의 꼭짓점을 반환한다. 기본은 개체 위치다.</summary>
        public virtual SimVector GetConeApex(SimulationSpellEntity entity) => entity.Position;

        /// <summary>부채꼴의 반경을 반환한다. 기본은 개체 반경이다.</summary>
        public virtual double GetConeReach(SimulationSpellEntity entity) => entity.Stats.Radius;

        /// <summary>개체 수명을 반환한다. 기본은 유지 시간이다.</summary>
        public virtual double GetLifetime(SimulationSpellEntity entity) => entity.Stats.Lifetime;

        /// <summary>범위 판정에서 같은 대상을 다시 때리는 주기를 반환한다. 기본은 HitInterval이다.</summary>
        public virtual double GetAreaHitInterval(SpellStats stats) => stats.HitInterval;

        /// <summary>범위 피해를 지속 피해(DoT)로 기록하는지 나타낸다.</summary>
        public virtual bool IsDamageOverTime => false;

        /// <summary>실제 피해를 잔류 피해 통계(PersistDamage)에 누적하는지 나타낸다.</summary>
        public virtual bool AccumulatesPersistDamage => false;

        /// <summary>
        /// 적중을 직접 충돌로 기록하고 첫 적중·소멸 이벤트(OnFirstHitOrExpire)를 추적하는지 나타낸다.
        /// 직접 충돌한 개체는 소멸 이벤트를 내지 않는다.
        /// </summary>
        public virtual bool TracksDirectHit => false;

        /// <summary>자연 소멸 시 OnExpire 이벤트를 내는지 나타낸다. 기본은 true다.</summary>
        public virtual bool RaisesExpireEvent => true;

        /// <summary>행동 전용 상태를 결정성 해시 버퍼에 기록한다. 기본은 기록하지 않는다.</summary>
        public virtual void WriteState(SimulationSpellEntity entity, StringBuilder state)
        {
        }

        /// <summary>
        /// 범위형 행동의 생성 위치를 정한다. 직접 시전한 일반 범위는 진행 방향으로 생성 거리만큼, 부채꼴·사각형은 실행 위치에 두고,
        /// 직접 시전한 회복·보호 사각형은 시전자 중심에 둔다. 개체가 여럿이면 원형으로 흩어 놓는다.
        /// </summary>
        protected static void PlaceAreaSpawn(RuneSimulation simulation, SpellAction action, SpellStats stats, int index,
            bool isFromEvent, SimulationPlayer caster, ref SimVector position, SimVector direction)
        {
            // 회복·보호 범위는 시전자에게 적용되므로 생성 거리 없이 현재 실행 위치에 만든다.
            // 부채꼴은 실행 위치를 꼭짓점으로, Beam은 실행 위치를 뒤쪽 면으로 진행 방향에 펼친다(결정 Q17, 백서 v7).
            bool isFunctional = action.SourceElement == "heal" || action.SourceElement == "protection";
            bool isAnchored = action.MagicType == SpellGrammar.MAGIC_TYPE_CONE || action.MagicType == SpellGrammar.MAGIC_TYPE_BOX;
            if (!isFromEvent && !isFunctional && !isAnchored) position += direction * stats.Offset;
            // 지팡이 끝은 시전자 몸 밖이므로 직접 시전한 회복·보호 Beam은 뒤쪽 면을 시전자 중심에 둬 자신을 포함한다.
            if (!isFromEvent && isFunctional && action.MagicType == SpellGrammar.MAGIC_TYPE_BOX) position = caster.Position;
            if (action.Count > 1)
                position += new SimVector(Math.Cos(index * 2 * Math.PI / action.Count), Math.Sin(index * 2 * Math.PI / action.Count))
                    * simulation.Balance.Sim.MultiOffset;
        }
    }
}
