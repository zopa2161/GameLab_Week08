namespace RuneCode
{
    /// <summary>
    /// 공전 모드 규칙이다. 생성된 개체의 경로를 행동 기준점 주위의 원 궤도로 바꾸는 이동 모드이며 유도보다 우선순위가 낮다.
    /// 즉시 범위 행동(폭발)에는 장착할 수 없다. 궤도 반경은 행동 수치, 회전 속도·진입 속도는 모드 수치이며 등급은 C 하나다.
    /// </summary>
    public sealed class OrbitModifierRules : SpellModifierRules
    {
        public const string RUNE_ID = "mod.orbit";
        public const int PRIORITY = 50;

        public override string RuneId => RUNE_ID;
        public override SpellModifierCategory Category => SpellModifierCategory.Motion;
        public override int MotionPriority => PRIORITY;

        /// <summary>즉시 범위 행동(폭발)이 아니면 장착할 수 있다.</summary>
        public override bool CanAttachTo(SpellBehaviorRules behavior) => behavior.DisplayType != SpellDisplayType.InstantArea;

        /// <summary>개체를 옮기는 행동(발사·잔류)은 궤도에서 생성 위치가 정해지므로 궤도로 바꾼다. 경로형(빔)은 그대로 둔다.</summary>
        public override SpellSpawnOrigin ModifySpawnOrigin(SpellBehaviorRules behavior, SpellSpawnOrigin origin)
            => behavior.MotionKind == SpellMotionKind.Path ? origin : SpellSpawnOrigin.Orbit;

        /// <summary>이동형(발사)은 직진 사거리 대신 기본 수명 × 지속 배율을 쓴다. 속도 배율로 줄지 않는다(F17).</summary>
        public override float ModifyLifetime(SpellBehaviorRules behavior, float lifetime, float baseLifetime, float durationMultiplier)
            => behavior.MotionKind == SpellMotionKind.Moving ? baseLifetime * durationMultiplier : lifetime;

        /// <summary>공전 행동은 시전 기준점에서 궤도 반경까지만 닿으므로 stats의 궤도 반경을 반환한다.</summary>
        public override float ModifyReach(SpellBehaviorRules behavior, SpellStats stats, float reach)
            => stats.OrbitRadius;
    }
}
