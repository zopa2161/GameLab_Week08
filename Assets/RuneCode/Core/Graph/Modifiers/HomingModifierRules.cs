namespace RuneCode
{
    /// <summary>
    /// 유도 모드 규칙이다. 유도 범위 안에 대상이 있을 때 이동을 맡는 이동 모드이며 공전보다 우선한다.
    /// 회전각·유도 범위(등급별 유도 단계)는 수치로 SpellStats가 처리한다.
    /// </summary>
    public sealed class HomingModifierRules : SpellModifierRules
    {
        public const string RUNE_ID = "mod.homing";
        public const int PRIORITY = 100;

        public override string RuneId => RUNE_ID;
        public override SpellModifierCategory Category => SpellModifierCategory.Motion;
        public override int MotionPriority => PRIORITY;

        /// <summary>즉시 범위 행동(폭발)이 아니면 장착할 수 있다.</summary>
        public override bool CanAttachTo(SpellBehaviorRules behavior) => behavior.DisplayType != SpellDisplayType.InstantArea;
    }
}
