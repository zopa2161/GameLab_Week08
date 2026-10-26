using System;

namespace RuneCode
{
    /// <summary>세션의 강화 진행을 트리 노드와 호버 정보에 표시하고 노드 구매를 처리한다.</summary>
    public sealed class UpgradeTreePresenter
    {
        private readonly RuneCodeSession _session;
        private readonly UpgradeTreePanel _view;
        private readonly UpgradeTreeDefinition _definition;
        private readonly Action _onPurchased;
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
            _view.NodeRefundRequested += Refund;
            _view.NodeHovered += OnNodeHovered;
        }

        /// <summary>현재 강화 포인트, 노드 레벨과 연결선, 열린 툴팁을 세션 상태로 갱신한다.</summary>
        public void Refresh()
        {
            _view.SetSummary("Lv. " + _session.Level + "  ·  " + GameData.L("ui.tree.summary") + "  ·  "
                + string.Format(GameData.L("ui.tree.points"), _session.Save.UpgradePoints)
                + "  ·  " + GameData.L("ui.tree.controls"));
            foreach (UpgradeTreeNodeDefinition node in _definition.Nodes)
            {
                if (node != null) RefreshNode(node);
            }
            _view.SetConnectionStates(nodeId => _session.GetUpgradeNodeLevel(nodeId) > 0);
            if (!string.IsNullOrEmpty(_hoveredNodeId)) RefreshTooltip(_hoveredNodeId);
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
            _view.ShowTooltip(nodeId, title, GameData.L(node.DescriptionKey), level,
                NextIncreaseText(node, currentLevel), CostText(node, currentLevel));
        }

        /// <summary>다음 구매로 증가하는 효과량을 노드 종류의 단위와 함께 반환한다.</summary>
        private static string NextIncreaseText(UpgradeTreeNodeDefinition node, int currentLevel)
        {
            if (currentLevel >= node.MaxLevel) return GameData.L("tree.maxed");
            if (IsUnlockStyleEffect(node.EffectType)) return GameData.L("ui.tree.unlockEffect");
            if (node.EffectType == UpgradeEffectType.CastSpeed) return GameData.L("ui.tree.pendingEffect");
            if (node.EffectType == UpgradeEffectType.ElementCostReduction)
                return GameData.L("ui.tree.nextIncrease") + " -" + node.Levels[currentLevel].Amount.ToString("0.##") + " 마나";
            if (node.EffectType == UpgradeEffectType.StationaryRegen)
                return GameData.L("ui.tree.nextIncrease") + " ×" + PlayerUpgradeStats.STATIONARY_REGEN_MULTIPLIER.ToString("0.##");
            float amount = node.Levels[currentLevel].Amount;
            string unit;
            switch (node.EffectType)
            {
                case UpgradeEffectType.EnergyRegen: unit = "/s"; break;
                case UpgradeEffectType.MaxEnergy: unit = " 마나"; break;
                case UpgradeEffectType.KillEnergy: unit = " 마나/처치"; break;
                case UpgradeEffectType.MaxHp: unit = " HP"; break;
                case UpgradeEffectType.DashInvulnerability: unit = "초"; break;
                case UpgradeEffectType.Damage:
                case UpgradeEffectType.ScrapHeal:
                case UpgradeEffectType.ScrapGain:
                case UpgradeEffectType.ScrapPickupRange:
                case UpgradeEffectType.MoveSpeed:
                case UpgradeEffectType.ElementDamage:
                case UpgradeEffectType.StatusDuration:
                case UpgradeEffectType.DashCooldownReduction:
                case UpgradeEffectType.Evasion:
                case UpgradeEffectType.NearDamage:
                case UpgradeEffectType.FarDamage:
                case UpgradeEffectType.FullManaDamage:
                case UpgradeEffectType.PostDashDamage:
                    amount *= 100f;
                    unit = "%";
                    break;
                default: unit = ""; break;
            }
            return GameData.L("ui.tree.nextIncrease") + " +" + amount.ToString("0.##") + unit;
        }

        /// <summary>수치 증가 대신 해금·플래그로 동작하는 효과인지 반환한다. 이 효과는 고정 문구로 표시한다.</summary>
        private static bool IsUnlockStyleEffect(UpgradeEffectType effectType)
        {
            switch (effectType)
            {
                case UpgradeEffectType.RuneUnlock:
                case UpgradeEffectType.None:
                case UpgradeEffectType.DashTeleport:
                case UpgradeEffectType.DashCollisionGuard:
                case UpgradeEffectType.PlayerAura:
                case UpgradeEffectType.FlyingSlash:
                case UpgradeEffectType.ReturningLaunch:
                case UpgradeEffectType.ReturningBurst:
                case UpgradeEffectType.ArcEnhance:
                case UpgradeEffectType.IceEnhance:
                case UpgradeEffectType.FireEnhance:
                case UpgradeEffectType.ElementPick:
                case UpgradeEffectType.StatusStackStart:
                case UpgradeEffectType.DashEndStart:
                case UpgradeEffectType.RightClickStart:
                case UpgradeEffectType.SingleBehavior:
                case UpgradeEffectType.ElementSlot:
                    return true;
                default: return false;
            }
        }

        /// <summary>다음 노드 구매의 실제 포인트 비용을 반환하며 최대 레벨이면 완료 상태를 반환한다.</summary>
        private string CostText(UpgradeTreeNodeDefinition node, int currentLevel)
        {
            if (currentLevel >= node.MaxLevel) return GameData.L("tree.maxed");
            return GameData.L("ui.tree.cost") + " " + _session.GetUpgradeNodeCost(node.Id) + " " + GameData.L("ui.tree.pointUnit");
        }

        /// <summary>우클릭한 노드의 1단계 반환을 요청하고 성공하면 포인트·노드·작업실 표시를 갱신한다.</summary>
        private void Refund(string nodeId)
        {
            bool isRefunded = _session.RefundUpgradeNode(nodeId);
            Refresh();
            if (isRefunded) _onPurchased();
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
