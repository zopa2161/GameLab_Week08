namespace RuneCode
{
    /// <summary>
    /// 행동 하나의 컴파일 규칙이다. 행동 식별값(룬 ID, Inline Magic form 값, 런타임 종류), 수식 적용 방식,
    /// 컴파일 추정 방식과 표시 방식을 정한다. 실행 동작은 RuneCode.Core의 SpellBehaviorRuntime이 같은 Kind로 맡는다.
    /// 새 행동은 이 클래스를 상속하고 SpellBehaviorRuleRegistry에 등록한다.
    /// </summary>
    public abstract class SpellBehaviorRules
    {
        private const string STATS_RUNE_PREFIX = "form.";

        /// <summary>그래프에 배치하는 행동 룬 ID다. 예: behavior.launch</summary>
        public abstract string RuneId { get; }

        /// <summary>Inline Magic 노드의 form 파라미터 값이다. 예: launch</summary>
        public abstract string FormValue { get; }

        /// <summary>SpellAction.Form과 시뮬레이션 개체 종류로 쓰는 런타임 종류다. 예: bolt</summary>
        public abstract string Kind { get; }

        /// <summary>피해·속도·반경 등 기본 수치를 가진 내부 룬 ID다. 예: form.bolt</summary>
        public string StatsRuneId => STATS_RUNE_PREFIX + Kind;

        /// <summary>화면에 그리는 방식이다.</summary>
        public virtual SpellDisplayType DisplayType => SpellDisplayType.Projectile;

        /// <summary>개체가 생성되는 위치의 유형이다. 기본은 지팡이 앞이다. 다중 모드는 지팡이 앞이면 발사 전용 개수·퍼짐 값을 쓴다.</summary>
        public virtual SpellSpawnOrigin SpawnOrigin => SpellSpawnOrigin.Wand;

        /// <summary>개체의 이동 성격이다. 이동 모드가 적용 방식을 고르고, 표시가 사거리·속도 줄을 고를 때 쓴다. 기본은 이동형이다.</summary>
        public virtual SpellMotionKind MotionKind => SpellMotionKind.Moving;

        /// <summary>
        /// 명중 경로(OnHit)와 미명중 소멸 경로(OnExpire)가 서로 배타적인지 나타낸다.
        /// true이면 비용·작업량 추정에서 두 경로 중 큰 쪽만, false이면 둘을 합산한다.
        /// </summary>
        public virtual bool HasExclusiveHitAndExpire => false;

        /// <summary>
        /// 기본 수명과 사거리·속도·지속 배율로 최종 수명을 반환한다. 기본은 지속 배율만 곱한다.
        /// </summary>
        public virtual float GetLifetime(float lifetime, float rangeMultiplier, float speedMultiplier, float durationMultiplier)
            => lifetime * durationMultiplier;

        /// <summary>
        /// 개체 하나가 낼 수 있는 최대 OnHit 횟수를 count로 돌려주고 상한이 있으면 true를 반환한다. 기본은 상한이 없다.
        /// </summary>
        public virtual bool TryGetMaxHitEvents(SpellStats stats, out int count)
        {
            count = 0;
            return false;
        }

        /// <summary>시전 위치에서 효과가 닿는 최대 거리를 반환한다. 기본은 생성 거리다.</summary>
        public virtual float GetReach(SpellStats stats) => stats.Offset;
    }
}
