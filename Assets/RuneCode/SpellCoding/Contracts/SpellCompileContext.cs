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

        /// <summary>행동 하나 고정 강화 여부다. 켜지면 시작 노드마다 루트 행동을 하나로 제한하고 통과한 행동에 자기 복제를 넣는다.</summary>
        public bool IsSingleBehavior { get; }

        /// <summary>전투 속성(fire·ice·arc) 노드 비용에서 뺄 감소량이다. 0이면 감소하지 않는다.</summary>
        public float ElementCostReduction { get; }

        /// <summary>
        /// 해금 룬, RAM 용량, 최대 에너지, 참조 가능한 마법 보관함, Modifier 소지량, 행동 하나 고정 여부와
        /// 전투 속성 비용 감소량으로 컴파일 문맥을 만든다.
        /// </summary>
        public SpellCompileContext(IEnumerable<string> unlockedRunes, int capacity, float maxEnergy, IEnumerable<SpellGraph> library,
            IReadOnlyDictionary<string, int> modifierStock = null, IReadOnlyDictionary<string, float> maxResourceCosts = null,
            bool isSingleBehavior = false, float elementCostReduction = 0f)
        {
            UnlockedRunes = unlockedRunes;
            Capacity = capacity;
            MaxEnergy = maxEnergy;
            Library = library;
            ModifierStock = modifierStock;
            MaxResourceCosts = maxResourceCosts;
            IsSingleBehavior = isSingleBehavior;
            ElementCostReduction = elementCostReduction;
        }
    }
}
