using System.Text;

namespace RuneCode
{
    /// <summary>
    /// 동작 모드 하나의 시뮬레이션 동작이다. 장착된 개체마다 모드 상태를 만들어 개체가 보관하며,
    /// 배치 모드는 생성 위치, 이동 모드는 매 틱 우선순위로 이동, 적중 모드는 적중 처리에 개입한다. 컴파일 규칙은 SpellModifierRules가 같은 RuneId로 맡는다.
    /// 새 동작 모드는 이 클래스를 상속하고 SpellModifierRuntimeRegistry에 등록한다.
    /// </summary>
    internal abstract class SpellModifierRuntime
    {
        /// <summary>모드 룬 ID다. 규칙의 RuneId와 같다.</summary>
        public abstract string RuneId { get; }

        /// <summary>
        /// 배치 모드가 행동의 PlaceSpawn보다 먼저 index번째 개체의 생성 위치·방향을 고친다(예: 퍼짐 방향).
        /// 기본은 바꾸지 않는다.
        /// </summary>
        public virtual void BeforePlaceSpawn(RuneSimulation simulation, SpellAction action, SpellStats stats, int index,
            ref SimVector position, ref SimVector direction)
        {
        }

        /// <summary>
        /// 배치 모드가 행동의 PlaceSpawn 다음에 index번째 개체의 생성 위치·방향을 고친다(예: 원형 배치).
        /// 기본은 바꾸지 않는다.
        /// </summary>
        public virtual void AfterPlaceSpawn(RuneSimulation simulation, SpellAction action, SpellStats stats, int index,
            ref SimVector position, ref SimVector direction)
        {
        }

        /// <summary>개체 생성 시 이 모드의 전용 상태 객체를 만든다. 개체가 모드 순서대로 보관한다. 기본은 null이다.</summary>
        public virtual object CreateState(SimulationSpellEntity entity) => null;

        /// <summary>
        /// 이동 모드가 이번 틱 개체의 이동을 맡을지 판단하고, 맡으면 개체를 옮기고 true를 반환한다.
        /// 우선순위가 높은 이동 모드부터 묻고 처음 true를 반환한 모드만 이동한다. 기본은 맡지 않는다.
        /// </summary>
        public virtual bool TryMove(RuneSimulation simulation, SimulationSpellEntity entity, object state) => false;

        /// <summary>
        /// 이번 틱 이동을 맡지 않은 이동 모드의 상태를 갱신한다(예: 공전 궤도 자리 회전). 다른 모드가 이동을 맡았거나
        /// TryMove가 false를 반환한 경우에 호출된다. 기본은 없다.
        /// </summary>
        public virtual void UpdatePassive(RuneSimulation simulation, SimulationSpellEntity entity, object state)
        {
        }

        /// <summary>
        /// 행동의 기본 경로를 대신하는 경로 모드인지 나타낸다(예: 공전). 반응 모드(유도)는 경로 모드가 있으면
        /// 경로를 떠나 대상을 직선 추적하는 방식으로 반응한다. 기본은 false다.
        /// </summary>
        public virtual bool ReplacesBasePath => false;

        /// <summary>
        /// 경로 모드가 지금 더 높은 우선순위의 반응 모드에 이동을 넘길 수 있는지 반환한다(예: 공전 진입 완료).
        /// 반응 모드가 새 대상을 고를 때만 묻는다. 기본은 true다.
        /// </summary>
        public virtual bool CanYieldMove(SimulationSpellEntity entity, object state) => true;

        /// <summary>
        /// 경로형 행동(빔)이 이번 틱 경로를 새로 그리기 시작할 때 호출한다. 경로 한 번에만 유효한 상태(지나친 대상 등)를 비운다.
        /// 기본은 없다.
        /// </summary>
        public virtual void BeginPath(RuneSimulation simulation, SimulationSpellEntity entity, object state)
        {
        }

        /// <summary>
        /// 경로형 행동이 경로를 한 칸(step) 그리기 전에 그 칸의 진행 방향을 고치고, 고쳤으면 true를 반환한다.
        /// 우선순위가 높은 이동 모드부터 묻고 처음 true를 반환한 모드만 그 칸을 맡는다. point는 칸의 시작점,
        /// pathSpeed는 경로가 한 칸을 그리는 데 쓰는 기준 속도, halfWidth는 경로 반폭이다. 기본은 맡지 않는다.
        /// </summary>
        public virtual bool SteerPathStep(RuneSimulation simulation, SimulationSpellEntity entity, object state,
            SimVector point, ref SimVector direction, double step, double pathSpeed, double halfWidth)
            => false;

        /// <summary>
        /// 적중 모드가 개체의 적 적중 한 번을 고친다. damage는 이번 적중 피해, isConsumed는 이 적중 뒤 개체가 소멸하는지다.
        /// 행동이 기본값(발사 = 첫 적중에 소멸)을 넣어 부르며, 적중 횟수(entity.Hits)는 이 호출 뒤에 늘어난다. 기본은 바꾸지 않는다.
        /// </summary>
        public virtual void OnHit(RuneSimulation simulation, SimulationSpellEntity entity, object state, SimulationEnemy enemy,
            ref double damage, ref bool isConsumed)
        {
        }

        /// <summary>모드 전용 상태를 결정성 해시 버퍼에 기록한다. 기본은 기록하지 않는다.</summary>
        public virtual void WriteState(SimulationSpellEntity entity, object state, StringBuilder buffer)
        {
        }
    }
}
