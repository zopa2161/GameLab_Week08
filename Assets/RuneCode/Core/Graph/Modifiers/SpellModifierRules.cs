namespace RuneCode
{
    /// <summary>
    /// 동작 모드 하나의 컴파일 규칙이다. 모드 룬 ID, 분류, 이동 모드의 우선순위와 장착 가능한 행동을 정한다.
    /// 실행 동작은 RuneCode.Core의 SpellModifierRuntime이 같은 RuneId로 맡는다.
    /// 새 동작 모드는 이 클래스를 상속하고 SpellModifierRuleRegistry에 등록한다. 수치 모드는 등록하지 않는다.
    /// </summary>
    public abstract class SpellModifierRules
    {
        /// <summary>모드 룬 ID다. 예: mod.homing</summary>
        public abstract string RuneId { get; }

        /// <summary>개입 시점에 따른 분류다.</summary>
        public abstract SpellModifierCategory Category { get; }

        /// <summary>
        /// 이동 모드의 우선순위다. 매 틱 값이 큰 이동 모드부터 이동을 맡을지 묻고, 같으면 등록 순서를 따른다.
        /// 이동 모드가 아니면 쓰지 않는다.
        /// </summary>
        public virtual int MotionPriority => 0;

        /// <summary>
        /// 행동 규칙의 성질로 이 모드를 장착할 수 있는지 1차 판단한다. 기본은 모두 허용하며,
        /// runes.json의 _modifierTargets 검사는 이와 별도로 계속 적용된다.
        /// </summary>
        public virtual bool CanAttachTo(SpellBehaviorRules behavior) => true;

        /// <summary>
        /// 배치 모드가 실행 명령의 생성 개수를 정한다. count는 행동 수치 행의 개수(또는 앞선 모드가 바꾼 개수),
        /// behavior는 행동 규칙(프리셋 호출 등 행동이 아니면 null), grade는 장착 등급이다. 기본은 개수를 바꾸지 않는다.
        /// </summary>
        public virtual int ModifyCount(int count, SpellBehaviorRules behavior, string grade, RuneStats modifierStats,
            ModifierGradeTable modifierGrades)
            => count;

        /// <summary>
        /// 회차를 만드는 모드가 실행 명령의 회차 수를 정한다. waveCount는 현재 회차 수(기본 1), behavior는 행동 규칙,
        /// grade는 장착 등급이다. 기본은 회차 수를 바꾸지 않는다.
        /// </summary>
        public virtual int ModifyWaveCount(int waveCount, SpellBehaviorRules behavior, string grade, RuneStats modifierStats,
            ModifierGradeTable modifierGrades)
            => waveCount;

        /// <summary>
        /// 행동이 정한 개체당 최대 OnHit 수(count)를 이 모드에 맞게 고친다. 상한이 없어지면 false를 반환한다.
        /// 비용·개체·실행 수 추정에 쓴다. 기본은 바꾸지 않는다.
        /// </summary>
        public virtual bool TryModifyMaxHitEvents(SpellStats stats, ref int count) => true;

        /// <summary>
        /// 행동의 생성 위치 유형(origin)을 이 모드에 맞게 바꿔 반환한다. 배치 모드가 이 값으로 여러 개체를 나눈다.
        /// 기본은 바꾸지 않는다.
        /// </summary>
        public virtual SpellSpawnOrigin ModifySpawnOrigin(SpellBehaviorRules behavior, SpellSpawnOrigin origin) => origin;

        /// <summary>
        /// 행동 규칙이 정한 수명(lifetime)을 이 모드에 맞게 고쳐 반환한다. modifier는 이 모드의 룬 데이터,
        /// baseLifetime은 행동 수치의 기본 수명, durationMultiplier는 지속 배율이다. 기본은 바꾸지 않는다.
        /// </summary>
        public virtual float ModifyLifetime(RuneDefinition modifier, SpellBehaviorRules behavior, float lifetime, float baseLifetime, float durationMultiplier)
            => lifetime;

        /// <summary>
        /// 행동 규칙이 정한 사거리(reach)를 이 모드에 맞게 고쳐 반환한다. 사거리·자동 시전 범위 표시에 쓴다. 기본은 바꾸지 않는다.
        /// </summary>
        public virtual float ModifyReach(SpellBehaviorRules behavior, SpellStats stats, float reach) => reach;
    }
}
