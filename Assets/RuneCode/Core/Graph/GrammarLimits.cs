namespace RuneCode
{
    /// <summary>
    /// 문법 엔진(컴파일러)이 사용하는 한도·기본값 묶음이다. 엔진이 게임 밸런스 데이터 형식에 의존하지 않도록
    /// 호출부(Unity 계층)가 밸런스 데이터에서 필요한 값만 채워 전달한다.
    /// </summary>
    public sealed class GrammarLimits
    {
        private readonly int _maxGraphNodes;
        private readonly int _maxGraphEdges;
        private readonly int _maxCompiledActions;
        private readonly int _maxLiveSpellEntities;
        private readonly float _maxEnergy;
        private readonly float _cooldownBase;

        public int MaxGraphNodes => _maxGraphNodes;
        public int MaxGraphEdges => _maxGraphEdges;
        public int MaxCompiledActions => _maxCompiledActions;
        public int MaxLiveSpellEntities => _maxLiveSpellEntities;
        public float MaxEnergy => _maxEnergy;
        public float CooldownBase => _cooldownBase;

        /// <summary>
        /// 그래프 노드·엣지 최대 수, 펼친 실행 수 상한, 동시 마법 개체 상한,
        /// 기본 최대 에너지와 쿨다운 기본값으로 한도를 만든다.
        /// </summary>
        public GrammarLimits(int maxGraphNodes, int maxGraphEdges, int maxCompiledActions,
            int maxLiveSpellEntities, float maxEnergy, float cooldownBase)
        {
            _maxGraphNodes = maxGraphNodes;
            _maxGraphEdges = maxGraphEdges;
            _maxCompiledActions = maxCompiledActions;
            _maxLiveSpellEntities = maxLiveSpellEntities;
            _maxEnergy = maxEnergy;
            _cooldownBase = cooldownBase;
        }
    }
}
