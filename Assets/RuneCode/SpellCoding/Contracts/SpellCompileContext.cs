using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>마법 그래프 컴파일에 필요한 진행 상태 값을 한 번에 전달하는 읽기 전용 묶음이다.</summary>
    public readonly struct SpellCompileContext
    {
        public IEnumerable<string> UnlockedRunes { get; }
        public int Capacity { get; }
        public float MaxEnergy { get; }
        public IEnumerable<SpellGraph> Library { get; }
        public IReadOnlyDictionary<string, float> MaxResourceCosts { get; }

        /// <summary>각 프리셋의 배치 수를 독립적으로 제한하는 모디·속성·행동 소지량 맵이다. null이면 소지량을 검사하지 않는다.</summary>
        public IReadOnlyDictionary<string, int> ModifierStock { get; }

        /// <summary>해금 룬, RAM 용량, 최대 에너지, 참조 가능한 마법 보관함과 Modifier 소지량으로 컴파일 문맥을 만든다.</summary>
        public SpellCompileContext(IEnumerable<string> unlockedRunes, int capacity, float maxEnergy, IEnumerable<SpellGraph> library,
            IReadOnlyDictionary<string, int> modifierStock = null, IReadOnlyDictionary<string, float> maxResourceCosts = null)
        {
            UnlockedRunes = unlockedRunes;
            Capacity = capacity;
            MaxEnergy = maxEnergy;
            Library = library;
            ModifierStock = modifierStock;
            MaxResourceCosts = maxResourceCosts;
        }
    }
}
