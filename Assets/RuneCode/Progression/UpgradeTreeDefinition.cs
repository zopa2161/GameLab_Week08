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
        KillEnergy
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

        [Header("강화 효과")]
        [SerializeField] private UpgradeEffectType _effectType;
        [SerializeField] private string _runeId;
        [SerializeField, Min(0), Tooltip("구매 기록이 없어도 적용되는 시작 레벨. 룬 해금 노드는 0을 사용한다.")] private int _initialLevel;
        [SerializeField] private List<UpgradeTreePrerequisite> _prerequisites = new List<UpgradeTreePrerequisite>();
        [SerializeField] private List<UpgradeTreeLevel> _levels = new List<UpgradeTreeLevel>();

        public string Id => _id;
        public int RequiredStage => _requiredStage;
        public string TitleKey => _titleKey;
        public string DescriptionKey => _descriptionKey;
        public Sprite Icon => _icon;
        public UpgradeEffectType EffectType => _effectType;
        public string RuneId => _runeId;
        public int InitialLevel => _initialLevel;
        public IReadOnlyList<UpgradeTreePrerequisite> Prerequisites => _prerequisites;
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

        [Header("강화 트리에 배치하지 않을 노드")]
        [SerializeField, InspectorName("배치하지 않을 노드")] private List<UpgradeTreeNodeDefinition> _unplacedNodes = new List<UpgradeTreeNodeDefinition>();

        public int LayoutVersion => _layoutVersion;
        public IReadOnlyList<UpgradeTreeNodeDefinition> Nodes => _nodes;
        public IReadOnlyList<UpgradeTreeNodeDefinition> UnplacedNodes => _unplacedNodes;

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

        /// <summary>두 목록의 노드 데이터와 배치 노드의 선행 관계, 플레이어 룬 커버리지를 검사해 오류를 반환한다.</summary>
        public bool Validate(out string error)
        {
            error = null;
            if (_nodes == null || _nodes.Count == 0 || _unplacedNodes == null)
            {
                error = "배치할 노드가 없거나 노드 목록이 초기화되지 않았습니다.";
                return false;
            }
            HashSet<string> nodeIds = new HashSet<string>();
            HashSet<string> runeIds = new HashSet<string>();
            foreach (UpgradeTreeNodeDefinition node in _nodes)
                if (!ValidateNodeData(node, nodeIds, runeIds, out error)) return false;
            foreach (UpgradeTreeNodeDefinition node in _unplacedNodes)
                if (!ValidateNodeData(node, nodeIds, runeIds, out error)) return false;

            UpgradeTreeNodeDefinition mainboard = FindNode(MAINBOARD_NODE_ID);
            if (mainboard == null || mainboard.EffectType != UpgradeEffectType.EnergyRegen || mainboard.Prerequisites.Count != 0)
            {
                error = "메인보드 전력 회복 노드가 유일한 시작 노드여야 합니다.";
                return false;
            }

            foreach (RuneDefinition rune in GameData.Runes.All)
            {
                bool isPlayerRune = IsPlayerRune(rune);
                bool isStarterRune = _starterRuneIds.Contains(rune.Id);
                if ((isPlayerRune && isStarterRune && rune.UnlockType != "start") ||
                    (isPlayerRune && !isStarterRune && rune.UnlockType != "tree" && rune.UnlockType != "bench") ||
                    (!isPlayerRune && rune.UnlockType != "start"))
                {
                    error = "룬 시작·트리·작업대 해금 분류가 지정된 시작 룬과 일치하지 않습니다: " + rune.Id;
                    return false;
                }
                if (isPlayerRune && !isStarterRune && !runeIds.Contains(rune.Id))
                {
                    error = "트리에 룬 해금 노드가 없습니다: " + rune.Id;
                    return false;
                }
            }

            foreach (UpgradeTreeNodeDefinition node in _nodes)
            {
                if ((node.Id == MAINBOARD_NODE_ID && node.Prerequisites.Count != 0) ||
                    (node.Id != MAINBOARD_NODE_ID && node.Prerequisites.Count != 1))
                {
                    error = "메인보드 외의 각 노드는 선행 노드를 하나만 가져야 합니다: " + node.Id;
                    return false;
                }
                foreach (UpgradeTreePrerequisite prerequisite in node.Prerequisites)
                {
                    UpgradeTreeNodeDefinition parent = prerequisite == null ? null : FindNode(prerequisite.NodeId);
                    if (parent == null || prerequisite.RequiredLevel != 1 || prerequisite.RequiredLevel > parent.MaxLevel)
                    {
                        error = "배치 노드의 선행 조건은 배치된 노드의 1레벨이어야 합니다: " + node.Id;
                        return false;
                    }
                }
            }
            foreach (UpgradeTreeNodeDefinition node in _unplacedNodes)
                if (node.Prerequisites.Count != 0)
                {
                    error = "배치하지 않을 노드에는 선행 노드를 지정할 수 없습니다: " + node.Id;
                    return false;
                }
            HashSet<string> visiting = new HashSet<string>();
            HashSet<string> visited = new HashSet<string>();
            foreach (UpgradeTreeNodeDefinition node in _nodes)
                if (!VisitPrerequisites(node, visiting, visited))
                {
                    error = "업그레이드 노드 선행 조건에 순환이 있습니다: " + node.Id;
                    return false;
            }
            return true;
        }

        /// <summary>두 목록에서 공통으로 쓰는 노드 ID·효과·시작 레벨·룬 해금 데이터를 검사하고 등록된 ID를 누적한다.</summary>
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
