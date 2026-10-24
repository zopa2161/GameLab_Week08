using System;
using System.Text;

namespace RuneCode
{
    /// <summary>세션의 강화 진행을 트리 노드와 호버 정보에 표시하고 노드 구매를 처리한다.</summary>
    public sealed class UpgradeTreePresenter
    {
        private readonly RuneCodeSession _session;
        private readonly UpgradeTreePanel _view;
        private readonly UpgradeTreeDefinition _definition;
        private readonly Action _onPurchased;
        private readonly StringBuilder _statsBuilder = new StringBuilder();
        private string _hoveredNodeId;

        /// <summary>세션과 트리 화면을 받아 배치된 노드의 구매·호버 이벤트를 연결한다.</summary>
        public UpgradeTreePresenter(RuneCodeSession session, UpgradeTreePanel view, Action onPurchased)
        {
            _session = session;
            _view = view;
            _definition = view.Definition;
            _onPurchased = onPurchased;
            _view.Build();
            _view.NodeClicked += Purchase;
            _view.NodeHovered += OnNodeHovered;
            _view.ResetClicked += Reset;
            _view.NodeRefundClicked += Refund;
        }

        /// <summary>현재 스크랩, 노드 레벨과 연결선, 열린 툴팁을 세션 상태로 갱신한다.</summary>
        public void Refresh()
        {
            _view.SetSummary(GameData.L("ui.tree.summary") + "  ·  스킬 " + _session.PurchasedSkillNodeCount + "/" + _session.MaxPurchasedSkillNodes
                + "  ·  " + _session.Save.Currency + " " + GameData.L("ui.fragments")
                + "  ·  " + GameData.L("ui.tree.controls"));
            _view.SetResetState(_session.PurchasedSkillNodeCount, _session.MaxPurchasedSkillNodes);
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
            {
                if (node != null) RefreshNode(node);
            }
            _view.SetConnectionStates(nodeId => _session.GetUpgradeNodeLevel(nodeId) > 0);
            RefreshStatsSummary();
            if (!string.IsNullOrEmpty(_hoveredNodeId)) RefreshTooltip(_hoveredNodeId);
        }

        /// <summary>현재 네 갈래에서 획득한 능력치의 누적 증가량을 좌측 상단 표시로 갱신한다.</summary>
        private void RefreshStatsSummary()
        {
            _statsBuilder.Clear();
            AppendStat(UpgradeEffectType.Damage, "마법 피해량 증가", "%", true);
            AppendStat(UpgradeEffectType.FireDamage, "화염 대미지", "%", true);
            AppendStat(UpgradeEffectType.IceDamage, "냉기 대미지", "%", true);
            AppendStat(UpgradeEffectType.ArcDamage, "전기 대미지", "%", true);
            AppendStat(UpgradeEffectType.BurnSpeed, "화상 가속", "%", true);
            AppendStat(UpgradeEffectType.ChillEffect, "냉기 감속", "%", true);
            AppendStat(UpgradeEffectType.ArcTargets, "전기 연쇄 대상", "명");
            AppendStat(UpgradeEffectType.EventCapacity, "온 트리거", "개");
            AppendStat(UpgradeEffectType.CooldownReduction, "쿨타임 감소", "%", true);
            _view.SetStatsSummary(_statsBuilder.ToString());
        }

        /// <summary>지정 효과의 트리 합계를 표시 단위로 변환해 통계 문구에 한 줄 추가한다.</summary>
        private void AppendStat(UpgradeEffectType effectType, string titleKey, string unit, bool isPercent = false)
        {
            if (_statsBuilder.Length > 0) _statsBuilder.Append('\n');
            float amount = _session.GetUpgradeTreeEffectTotal(effectType) * (isPercent ? 100f : 1f);
            _statsBuilder.Append(GameData.L(titleKey)).Append(" +").Append(amount.ToString("0.##")).Append(unit);
        }

        /// <summary>노드의 잠금·비용·레벨 상태를 계산해 아이콘에 적용한다.</summary>
        private void RefreshNode(UpgradeTreeNodeDefinition node)
        {
            int level = _session.GetUpgradeNodeLevel(node.Id);
            bool isComplete = level >= node.MaxLevel;
            bool canPurchase = _session.CanPurchaseUpgradeNode(node.Id, out string reasonKey);
            bool isAvailable = isComplete || (reasonKey != "tree.stageLocked" && reasonKey != "tree.prerequisiteLocked"
                && reasonKey != "tree.invalidNode" && reasonKey != "tree.invalidRune" && reasonKey != "tree.exclusiveLocked");
            string runeCategory = null;
            if (node.EffectType == UpgradeEffectType.RuneUnlock && GameData.Runes.TryGet(node.RuneId, out RuneDefinition rune))
                runeCategory = rune.Category;
            _view.SetNodeState(node.Id, node.EffectType, runeCategory, node.Icon, level, node.MaxLevel,
                _session.GetUpgradeNodeCost(node.Id), isAvailable, isComplete, canPurchase, node.RuneId);
        }

        /// <summary>호버 중인 노드 ID를 저장하고 그 노드의 툴팁을 표시하거나 닫는다.</summary>
        private void OnNodeHovered(string nodeId, bool isHovered)
        {
            if (isHovered)
            {
                _hoveredNodeId = nodeId;
                RefreshTooltip(nodeId);
                return;
            }
            if (_hoveredNodeId != nodeId) return;
            _hoveredNodeId = null;
            _view.HideTooltip();
        }

        /// <summary>호버 노드의 이름·설명·다음 상승량·비용과 강화 노드의 현재 레벨을 표시한다.</summary>
        private void RefreshTooltip(string nodeId)
        {
            UpgradeTreeNodeDefinition node = _definition.FindNode(nodeId);
            if (node == null) return;
            string title = GameData.L(node.TitleKey);
            if (node.EffectType == UpgradeEffectType.RuneUnlock && GameData.Runes.TryGet(node.RuneId, out RuneDefinition rune))
                title = rune.Name;
            int currentLevel = _session.GetUpgradeNodeLevel(nodeId);
            string level = node.EffectType == UpgradeEffectType.RuneUnlock ? null :
                GameData.L("ui.tree.level") + " " + currentLevel + "/" + node.MaxLevel;
            string description = GameData.L(node.DescriptionKey);
            if (currentLevel > node.InitialLevel) description += "\n우클릭: 반환 · 선행 조건이 끊기는 후속 노드도 함께 반환";
            _view.ShowTooltip(nodeId, title, description, level,
                NextIncreaseText(node, currentLevel), CostText(node, currentLevel));
        }

        /// <summary>다음 구매로 증가하는 효과량을 노드 종류의 단위와 함께 반환한다.</summary>
        private static string NextIncreaseText(UpgradeTreeNodeDefinition node, int currentLevel)
        {
            if (currentLevel >= node.MaxLevel) return GameData.L("tree.maxed");
            if (node.EffectType == UpgradeEffectType.RuneUnlock) return GameData.L("ui.tree.unlockEffect");
            if (node.EffectType == UpgradeEffectType.CastSpeed) return GameData.L("ui.tree.pendingEffect");
            if (node.EffectType == UpgradeEffectType.FireTrigger || node.EffectType == UpgradeEffectType.IceTrigger
                || node.EffectType == UpgradeEffectType.ArcTrigger || node.EffectType == UpgradeEffectType.BurnSpread)
                return GameData.L("ui.tree.unlockEffect");
            float amount = node.Levels[currentLevel].Amount;
            string unit;
            switch (node.EffectType)
            {
                case UpgradeEffectType.EnergyRegen: unit = "/s"; break;
                case UpgradeEffectType.MaxEnergy: unit = " 전력"; break;
                case UpgradeEffectType.KillEnergy: unit = " 전력/처치"; break;
                case UpgradeEffectType.RamCapacity: unit = " RAM"; break;
                case UpgradeEffectType.LightningStrike: unit = " 기본 피해"; break;
                case UpgradeEffectType.MaxHp: unit = " HP"; break;
                case UpgradeEffectType.Damage:
                case UpgradeEffectType.FireDamage:
                case UpgradeEffectType.IceDamage:
                case UpgradeEffectType.ArcDamage:
                case UpgradeEffectType.BurnSpeed:
                case UpgradeEffectType.ChillEffect:
                case UpgradeEffectType.FreezeBurst:
                case UpgradeEffectType.CooldownReduction:
                case UpgradeEffectType.ScrapHeal:
                case UpgradeEffectType.ScrapGain:
                case UpgradeEffectType.ScrapPickupRange:
                case UpgradeEffectType.MoveSpeed:
                    amount *= 100f;
                    unit = "%";
                    break;
                default: unit = ""; break;
            }
            return GameData.L("ui.tree.nextIncrease") + " +" + amount.ToString("0.##") + unit;
        }

        /// <summary>다음 노드 구매의 실제 스크랩 비용을 반환하며 최대 레벨이면 완료 상태를 반환한다.</summary>
        private string CostText(UpgradeTreeNodeDefinition node, int currentLevel)
        {
            if (currentLevel >= node.MaxLevel) return GameData.L("tree.maxed");
            return GameData.L("ui.tree.cost") + " " + _session.GetUpgradeNodeCost(node.Id) + " " + GameData.L("ui.fragments");
        }

        /// <summary>구매한 스킬을 초기화하고 노드·능력치·도크와 작업실 표시를 갱신한다.</summary>
        private void Reset()
        {
            if (!_session.ResetSkillTree()) return;
            Refresh();
            _onPurchased();
        }

        /// <summary>지정 노드와 선행 조건을 잃은 후속 노드를 반환하고 트리·능력치·작업실 표시를 갱신한다.</summary>
        private void Refund(string nodeId)
        {
            if (!_session.RefundSkillNode(nodeId)) return;
            Refresh();
            _onPurchased();
        }

        /// <summary>노드 구매에 성공하면 이전 레벨에 따라 최초 해금 또는 강화 효과음을 재생하고 작업실 표시를 갱신한다.</summary>
        private void Purchase(string nodeId)
        {
            int previousLevel = _session.GetUpgradeNodeLevel(nodeId);
            bool isPurchased = _session.BuyUpgradeNode(nodeId);
            Refresh();
            if (!isPurchased) return;
            if (previousLevel == 0) SoundManager.Instance.PlayUnlockSkillTree();
            else SoundManager.Instance.PlayUpgradeSkillTree();
            _onPurchased();
        }
    }
}
