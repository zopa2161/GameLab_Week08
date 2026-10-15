namespace RuneCode
{
    /// <summary>
    /// 행동 실행 등록 목록이다. 새 행동은 RUNTIMES에 한 줄 추가한다. 컴파일 규칙은 SpellBehaviorRuleRegistry에 같은 Kind로 등록한다.
    /// 등록되지 않은 종류(공전 등)는 기본 동작을 받고, 공전의 이동·접촉 판정은 RuneSimulation의 기존 분기가 처리한다.
    /// </summary>
    internal static class SpellBehaviorRuntimeRegistry
    {
        private static readonly SpellBehaviorRuntime[] RUNTIMES =
        {
            new LaunchBehaviorRuntime(),
            new BurstBehaviorRuntime(),
            new PersistBehaviorRuntime(),
            new BeamBehaviorRuntime()
        };

        private static readonly SpellBehaviorRuntime UNREGISTERED = new UnregisteredBehaviorRuntime();

        /// <summary>런타임 종류(bolt 등)의 실행 동작을 반환한다. 등록되지 않았으면 기본 동작을 반환한다.</summary>
        public static SpellBehaviorRuntime Get(string kind)
        {
            foreach (SpellBehaviorRuntime runtime in RUNTIMES)
                if (runtime.Kind == kind) return runtime;
            return UNREGISTERED;
        }

        /// <summary>런타임 종류에 등록된 실행 동작이 있는지 반환한다.</summary>
        public static bool IsRegistered(string kind)
        {
            foreach (SpellBehaviorRuntime runtime in RUNTIMES)
                if (runtime.Kind == kind) return true;
            return false;
        }

        /// <summary>등록되지 않은 종류에 쓰는 기본 동작이다.</summary>
        private sealed class UnregisteredBehaviorRuntime : SpellBehaviorRuntime
        {
            public override string Kind => string.Empty;
        }
    }
}
