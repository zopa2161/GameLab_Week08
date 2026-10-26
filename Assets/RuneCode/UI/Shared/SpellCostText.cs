namespace RuneCode
{
    /// <summary>
    /// 마법·노드 비용을 화면 문구로 만든다. 전력 단독 비용은 "N 전력", 여러 자원은 자원 이름별 수치, 상한이 없으면 산정 불가로 표시한다.
    /// 편집기 지표·노드 안내·전투 HUD가 같은 표기를 쓴다.
    /// </summary>
    public static class SpellCostText
    {
        /// <summary>비용 묶음을 표시 문구로 반환한다. 비어 있으면 0 전력, 무한·NaN이 있으면 산정 불가를 반환한다.</summary>
        public static string Format(ResourceCostSet costs)
        {
            if (IsUnbounded(costs)) return GameData.L("ui.costUnbounded");
            if (costs.Amounts.Count == 0) return "0 마나";
            if (costs.Amounts.Count == 1 && costs.Amounts[0].Resource == "mana")
                return costs.Amounts[0].Amount.ToString("0.#") + " 마나";
            return costs.Format(ResourceName);
        }

        /// <summary>
        /// 시전만으로 차감되는 비용과 이벤트 후속까지 포함한 최대 비용을 함께 표시한다. 두 값이 같으면 시전 비용만 표시한다.
        /// </summary>
        public static string FormatCastAndMax(ResourceCostSet castCosts, ResourceCostSet maxCosts)
        {
            string cast = Format(castCosts);
            string max = Format(maxCosts);
            string text = GameData.L("ui.castCost") + " " + cast;
            return cast == max ? text : text + "  ·  " + GameData.L("ui.cost") + " " + max;
        }

        /// <summary>
        /// 시전 비용을 최대 전력값에 대한 nn/max 표기로 반환한다. maxEnergy는 세션이 성장을 반영해 넘기는 최대 전력 데이터다.
        /// 산정 불가 비용이거나 전력 외 자원이 섞였으면 nn/max 표기를 쓸 수 없어 기존 문구를 반환한다.
        /// </summary>
        public static string FormatWithMaxEnergy(ResourceCostSet costs, float maxEnergy)
        {
            if (IsUnbounded(costs)) return Format(costs);
            foreach (ResourceAmount amount in costs.Amounts)
                if (amount.Resource != "mana") return Format(costs);
            return costs.GetAmount("mana").ToString("0.#") + " / " + maxEnergy.ToString("0.#") + " 마나" ;
        }

        /// <summary>시전 비용이 최대 전력값을 넘어 시전할 수 없는 상태인지 반환한다. 산정 불가 비용도 시전할 수 없으므로 초과로 본다.</summary>
        public static bool ExceedsMaxEnergy(ResourceCostSet costs, float maxEnergy)
        {
            if (IsUnbounded(costs)) return true;
            return costs.GetAmount("mana") > maxEnergy;
        }

        /// <summary>비용 묶음에 무한 또는 NaN 비용이 있는지 반환한다.</summary>
        public static bool IsUnbounded(ResourceCostSet costs)
        {
            foreach (ResourceAmount amount in costs.Amounts)
                if (double.IsInfinity(amount.Amount) || double.IsNaN(amount.Amount)) return true;
            return false;
        }

        /// <summary>자원 ID의 현지화 이름을 반환하고 번역이 없으면 ID를 반환한다.</summary>
        private static string ResourceName(string resource)
        {
            if (resource == "mana") return GameData.L("ui.energy");
            return GameData.TryL("resource." + resource, out string value) ? value : resource;
        }
    }
}
