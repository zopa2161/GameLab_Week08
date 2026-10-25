using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    public enum UpgradeEffectType
    {
        EnergyRegen,
        MaxEnergy,
        RamCapacity,
        CastSpeed,
        Damage,
        ScrapGain,
        MaxHp,
        MoveSpeed,
        RuneUnlock,
        ScrapPickupRange,
        KillEnergy,
        ScrapHeal,
        None,
        ElementDamage,
        ElementCostReduction,
        StatusDuration,
        DashCooldownReduction,
        DashInvulnerability,
        Evasion,
        NearDamage,
        FarDamage,
        FullManaDamage,
        PostDashDamage,
        StationaryRegen,
        DashTeleport,
        DashCollisionGuard,
        PlayerAura,
        ArcEnhance,
        IceEnhance,
        FireEnhance,
        ElementPick,
        StatusStackStart,
        DashEndStart,
        RightClickStart,
        SingleBehavior,
        ElementSlot
    }

    [Serializable]
    public sealed class UpgradeTreeLevel
    {
        [SerializeField] private int _cost;
        [SerializeField] private float _amount;

        public int Cost => _cost;
        public float Amount => _amount;
    }

    [Serializable]
    public sealed class UpgradeTreePrerequisite
    {
        [SerializeField] private string _nodeId;
        [SerializeField] private int _requiredLevel = 1;

        public string NodeId => _nodeId;
        public int RequiredLevel => _requiredLevel;
    }

    [Serializable]
    public sealed class UpgradeTreeNodeDefinition
    {
        [Header("식별 및 표시")]
        [SerializeField] private string _id;
        [SerializeField] private int _requiredStage = 1;
        [SerializeField] private string _titleKey;
        [SerializeField] private string _descriptionKey;
        [SerializeField] private Sprite _icon;
        [SerializeField, Tooltip("트리 편집기와 노드 자동 생성이 사용하는 격자 셀. 미배치 노드는 사용하지 않는다.")]
        private Vector2Int _layoutCell;

        [Header("강화 효과")]
        [SerializeField] private UpgradeEffectType _effectType;
        [SerializeField] private string _runeId;
        [SerializeField, Min(0), Tooltip("구매 기록이 없어도 적용되는 시작 레벨. 룬 해금 노드는 0을 사용한다.")] private int _initialLevel;
        [SerializeField] private List<UpgradeTreePrerequisite> _prerequisites = new List<UpgradeTreePrerequisite>();
        [SerializeField, Tooltip("같은 값을 가진 노드끼리 하나만 구매할 수 있다. 비우면 배타 세트에 속하지 않는다.")] private string _exclusiveGroupId;
        [SerializeField] private List<UpgradeTreeLevel> _levels = new List<UpgradeTreeLevel>();

        public string Id => _id;
        public int RequiredStage => _requiredStage;
        public string TitleKey => _titleKey;
        public string DescriptionKey => _descriptionKey;
        public Sprite Icon => _icon;
        public Vector2Int LayoutCell => _layoutCell;
        public UpgradeEffectType EffectType => _effectType;
        public string RuneId => _runeId;
        public int InitialLevel => _initialLevel;
        public IReadOnlyList<UpgradeTreePrerequisite> Prerequisites => _prerequisites;
        public string ExclusiveGroupId => _exclusiveGroupId;
        public IReadOnlyList<UpgradeTreeLevel> Levels => _levels;
        public int MaxLevel => _levels == null ? 0 : _levels.Count;

        /// <summary>다음 레벨에 필요한 재화량을 반환하고 더 강화할 수 없으면 -1을 반환한다.</summary>
        public int GetNextCost(int currentLevel)
        {
            return _levels != null && currentLevel >= 0 && currentLevel < _levels.Count
                ? _levels[currentLevel].Cost
                : -1;
        }

        /// <summary>현재 레벨까지 누적되는 효과량을 반환한다.</summary>
        public float GetTotalAmount(int currentLevel)
        {
            if (_levels == null) return 0;
            float total = 0;
            for (int index = 0; index < Mathf.Min(currentLevel, _levels.Count); index++)
                total += _levels[index].Amount;
            return total;
        }
    }

    [CreateAssetMenu(fileName = "UpgradeTree", menuName = "Rune Code/Upgrade Tree")]
    public sealed class UpgradeTreeDefinition : ScriptableObject
    {
        private const string MAINBOARD_NODE_ID = "upgrade.mainboard";
        private const int MAX_UPGRADE_LEVEL = 99;
        private static readonly HashSet<string> _starterRuneIds = new HashSet<string>
        {
            "shape.sphere", "behavior.launch", "element.neutral", "mod.knockback"
        };

        [SerializeField, HideInInspector] private int _layoutVersion = 1;
        [Header("강화 트리에 배치할 노드")]
        [SerializeField, InspectorName("배치할 노드")] private List<UpgradeTreeNodeDefinition> _nodes = new List<UpgradeTreeNodeDefinition>();

        public int LayoutVersion => _layoutVersion;
        public IReadOnlyList<UpgradeTreeNodeDefinition> Nodes => _nodes;

        /// <summary>ID가 일치하는 강화 노드를 반환하고 찾지 못하면 null을 반환한다.</summary>
        public UpgradeTreeNodeDefinition FindNode(string nodeId)
        {
            if (_nodes == null) return null;
            foreach (UpgradeTreeNodeDefinition node in _nodes)
                if (node != null && node.Id == nodeId) return node;
            return null;
        }

        /// <summary>룬 ID를 해금하는 트리 노드를 반환하고 등록되지 않았으면 null을 반환한다.</summary>
        public UpgradeTreeNodeDefinition FindRuneUnlock(string runeId)
        {
            if (_nodes == null) return null;
            foreach (UpgradeTreeNodeDefinition node in _nodes)
                if (node != null && node.EffectType == UpgradeEffectType.RuneUnlock && node.RuneId == runeId) return node;
            return null;
        }

        /// <summary>배치 노드의 데이터, 다중 선행 관계, 순환, 배타 세트를 검사해 오류를 반환한다.</summary>
        public bool Validate(out string error)
        {
            error = null;
            if (_nodes == null || _nodes.Count == 0)
            {
                error = "배치할 노드가 없거나 노드 목록이 초기화되지 않았습니다.";
                return false;
            }
            HashSet<string> nodeIds = new HashSet<string>();
            HashSet<string> runeIds = new HashSet<string>();
            foreach (UpgradeTreeNodeDefinition node in _nodes)
                if (!ValidateNodeData(node, nodeIds, runeIds, out error)) return false;

            HashSet<Vector2Int> layoutCells = new HashSet<Vector2Int>();
            foreach (UpgradeTreeNodeDefinition node in _nodes)
                if (node != null && !layoutCells.Add(node.LayoutCell))
                {
                    error = "배치 노드의 트리 셀이 중복됩니다: " + node.Id;
                    return false;
                }

            UpgradeTreeNodeDefinition mainboard = FindNode(MAINBOARD_NODE_ID);
            if (mainboard == null || (mainboard.EffectType != UpgradeEffectType.EnergyRegen && mainboard.EffectType != UpgradeEffectType.None)
                || mainboard.Prerequisites.Count != 0)
            {
                error = "메인보드(효과 없음 또는 전력 회복) 노드가 유일한 시작 노드여야 합니다.";
                return false;
            }

            // 룬 해금 노드를 폐지해 트리 밖에서 쓰는 플레이어 룬은 start도 허용한다.
            foreach (RuneDefinition rune in GameData.Runes.All)
            {
                bool isPlayerRune = IsPlayerRune(rune);
                bool isStarterRune = _starterRuneIds.Contains(rune.Id);
                if ((isPlayerRune && isStarterRune && rune.UnlockType != "start") ||
                    (isPlayerRune && !isStarterRune && rune.UnlockType != "start" && rune.UnlockType != "tree" && rune.UnlockType != "bench") ||
                    (!isPlayerRune && rune.UnlockType != "start"))
                {
                    error = "룬 시작·트리·작업대 해금 분류가 지정된 시작 룬과 일치하지 않습니다: " + rune.Id;
                    return false;
                }
            }

            foreach (UpgradeTreeNodeDefinition node in _nodes)
            {
                if ((node.Id == MAINBOARD_NODE_ID && node.Prerequisites.Count != 0) ||
                    (node.Id != MAINBOARD_NODE_ID && node.Prerequisites.Count < 1))
                {
                    error = "메인보드 외의 각 노드는 선행 노드를 하나 이상 가져야 합니다: " + node.Id;
                    return false;
                }
                HashSet<string> parentIds = new HashSet<string>();
                foreach (UpgradeTreePrerequisite prerequisite in node.Prerequisites)
                {
                    if (prerequisite != null && !parentIds.Add(prerequisite.NodeId))
                    {
                        error = "선행 노드가 중복되었습니다: " + node.Id;
                        return false;
                    }
                    if (prerequisite != null && prerequisite.NodeId == node.Id)
                    {
                        error = "노드가 자기 자신을 선행 노드로 둘 수 없습니다: " + node.Id;
                        return false;
                    }
                    UpgradeTreeNodeDefinition parent = prerequisite == null ? null : FindNode(prerequisite.NodeId);
                    if (parent == null || prerequisite.RequiredLevel != 1 || prerequisite.RequiredLevel > parent.MaxLevel)
                    {
                        error = "배치 노드의 선행 조건은 배치된 노드의 1레벨이어야 합니다: " + node.Id;
                        return false;
                    }
                }
            }
            HashSet<string> visiting = new HashSet<string>();
            HashSet<string> visited = new HashSet<string>();
            foreach (UpgradeTreeNodeDefinition node in _nodes)
                if (!VisitPrerequisites(node, visiting, visited))
                {
                    error = "업그레이드 노드 선행 조건에 순환이 있습니다: " + node.Id;
                    return false;
            }
            return ValidateExclusiveGroups(_nodes, out error);
        }

        /// <summary>배타 세트 노드의 구성(노드 2개 이상, 선행 노드 1개·동일 부모, 시작 레벨 0, 메인보드 제외)을 검사해 오류를 반환한다.</summary>
        private static bool ValidateExclusiveGroups(IReadOnlyList<UpgradeTreeNodeDefinition> nodes, out string error)
        {
            error = null;
            Dictionary<string, List<UpgradeTreeNodeDefinition>> groups = new Dictionary<string, List<UpgradeTreeNodeDefinition>>();
            foreach (UpgradeTreeNodeDefinition node in nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.ExclusiveGroupId)) continue;
                if (node.Id == MAINBOARD_NODE_ID)
                {
                    error = "메인보드 노드는 배타 세트에 속할 수 없습니다: " + node.Id;
                    return false;
                }
                if (node.InitialLevel != 0)
                {
                    error = "배타 세트 노드의 시작 레벨은 0이어야 합니다: " + node.Id;
                    return false;
                }
                if (node.Prerequisites.Count != 1)
                {
                    error = "배타 세트 노드는 선행 노드를 하나만 가져야 합니다: " + node.Id;
                    return false;
                }
                if (!groups.TryGetValue(node.ExclusiveGroupId, out List<UpgradeTreeNodeDefinition> groupNodes))
                {
                    groupNodes = new List<UpgradeTreeNodeDefinition>();
                    groups[node.ExclusiveGroupId] = groupNodes;
                }
                groupNodes.Add(node);
            }
            foreach (KeyValuePair<string, List<UpgradeTreeNodeDefinition>> pair in groups)
            {
                if (pair.Value.Count < 2)
                {
                    error = "배타 세트에는 노드가 두 개 이상 있어야 합니다: " + pair.Key;
                    return false;
                }
                string parentId = pair.Value[0].Prerequisites[0].NodeId;
                for (int index = 1; index < pair.Value.Count; index++)
                    if (pair.Value[index].Prerequisites[0].NodeId != parentId)
                    {
                        error = "배타 세트 노드의 선행 노드는 모두 같아야 합니다: " + pair.Key;
                        return false;
                    }
            }
            return true;
        }

        /// <summary>노드 ID·효과·시작 레벨·룬 해금 데이터를 검사하고 등록된 ID를 누적한다.</summary>
        private static bool ValidateNodeData(UpgradeTreeNodeDefinition node, HashSet<string> nodeIds, HashSet<string> runeIds, out string error)
        {
            error = null;
            if (node == null || string.IsNullOrWhiteSpace(node.Id) || !nodeIds.Add(node.Id) || node.RequiredStage != 1 ||
                string.IsNullOrWhiteSpace(node.TitleKey) || string.IsNullOrWhiteSpace(node.DescriptionKey) ||
                node.Levels == null || node.Levels.Count == 0 ||
                node.Prerequisites == null || !Enum.IsDefined(typeof(UpgradeEffectType), node.EffectType) ||
                node.InitialLevel < 0 || node.InitialLevel > node.MaxLevel ||
                (node.EffectType == UpgradeEffectType.RuneUnlock && node.InitialLevel != 0))
            {
                error = "업그레이드 노드 식별·레벨·시작 레벨 데이터가 유효하지 않거나 스테이지 잠금이 설정되어 있습니다.";
                return false;
            }
            foreach (UpgradeTreeLevel level in node.Levels)
                if (level == null || level.Cost < 0 || level.Amount < 0 || float.IsNaN(level.Amount) || float.IsInfinity(level.Amount))
                {
                    error = "업그레이드 노드의 비용 또는 효과량이 유효하지 않습니다: " + node.Id;
                    return false;
                }
            if (node.EffectType == UpgradeEffectType.RamCapacity)
                foreach (UpgradeTreeLevel level in node.Levels)
                    if (Mathf.Abs(level.Amount - Mathf.Round(level.Amount)) > 0.001f)
                    {
                        error = "RAM 용량 강화량은 정수여야 합니다: " + node.Id;
                        return false;
                    }
            if (node.EffectType == UpgradeEffectType.RuneUnlock &&
                (node.Levels.Count != 1 || !GameData.Runes.TryGet(node.RuneId, out RuneDefinition rune) ||
                    !IsPlayerRune(rune) || (rune.UnlockType != "tree" && rune.UnlockType != "bench") || !runeIds.Add(node.RuneId)))
            {
                error = "룬 해금 노드의 룬 참조가 없거나 중복되었습니다: " + node.Id;
                return false;
            }
            if (node.EffectType != UpgradeEffectType.RuneUnlock &&
                (node.Levels.Count > MAX_UPGRADE_LEVEL || !string.IsNullOrEmpty(node.RuneId)))
            {
                error = "강화 노드는 최대 99레벨이며 룬 ID를 가질 수 없습니다: " + node.Id;
                return false;
            }
            return true;
        }

        /// <summary>Core·Internal 구현 룬을 제외한 룬이 플레이어용 해금 대상인지 반환한다.</summary>
        private static bool IsPlayerRune(RuneDefinition rune)
        {
            return rune.Category != SpellGrammar.CATEGORY_CORE && rune.Category != SpellGrammar.CATEGORY_INTERNAL;
        }

        /// <summary>선행 노드 연결을 재귀 검사해 순환이 없으면 현재 노드 ID를 완료 집합에 추가한다.</summary>
        private bool VisitPrerequisites(UpgradeTreeNodeDefinition node, HashSet<string> visiting, HashSet<string> visited)
        {
            if (visited.Contains(node.Id)) return true;
            if (!visiting.Add(node.Id)) return false;
            foreach (UpgradeTreePrerequisite prerequisite in node.Prerequisites)
                if (!VisitPrerequisites(FindNode(prerequisite.NodeId), visiting, visited)) return false;
            visiting.Remove(node.Id);
            visited.Add(node.Id);
            return true;
        }
    }
}
