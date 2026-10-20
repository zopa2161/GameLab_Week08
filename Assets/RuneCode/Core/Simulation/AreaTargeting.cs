namespace RuneCode
{
    /// <summary>범위 마법의 미리보기 위치·방향 계산을 모은 보조다. 시뮬레이션 상태는 매개변수로 받는다.</summary>
    internal static class AreaTargeting
    {
        /// <summary>지면에 배치하는 폭발·잔류 명령인지 반환한다. 궤도 생성 등 다른 모드의 생성 규칙은 보존한다.</summary>
        internal static bool IsAreaPreviewAction(SpellAction action)
            => action.Kind == "spawn" && action.SpawnOrigin == SpellSpawnOrigin.Ground
                && action.Form == SpellGrammar.FORM_ZONE;

        /// <summary>지면 범위 명령 중 공전 모듈이 없는 경우에만 미리보기를 표시할 수 있는지 반환한다.</summary>
        internal static bool CanShowAreaPreview(SpellAction action)
        {
            if (!IsAreaPreviewAction(action)) return false;
            foreach (SpellModifierRules rule in action.ModifierRules)
                if (rule.RuneId == OrbitModifierRules.RUNE_ID) return false;
            return true;
        }

        /// <summary>시전자 위치를 중심으로 포인터를 공통 최대 사거리 안에 제한하여 반환한다. 최소 거리는 없다.</summary>
        internal static SimVector ClampAreaTargetPosition(SimVector pointer, SimVector origin, double placementRange)
        {
            SimVector delta = pointer - origin;
            return delta.Length > placementRange ? origin + delta.Normalized() * placementRange : pointer;
        }

        /// <summary>다중 분산을 제외하고 포인터를 최대 사거리로만 제한한 기본 범위 미리보기 위치를 반환한다.</summary>
        internal static SimVector GetAreaPreviewPosition(SimVector pointer, SimulationPlayer player, double placementRange)
            => ClampAreaTargetPosition(pointer, player.Position, placementRange);

        /// <summary>범위 조준 미리보기의 위치와 방향을 반환한다. 참격은 시전자 중심과 다중 퍼짐 방향을 사용한다.</summary>
        internal static void GetRangePreview(RuneSimulation simulation, SpellAction action, SimVector pointer, int index, out SimVector position, out SimVector direction)
        {
            SimVector delta = pointer - simulation.Player.Position;
            direction = delta.LengthSquared > 0.000001 ? delta.Normalized() : simulation.Player.AimDirection;
            position = GetAreaPreviewPosition(pointer, simulation.Player, simulation.GetAreaPlacementRange());
            if (action.Form != SlashBehaviorRules.KIND) return;
            position = simulation.Player.Position;
            foreach (SpellModifierRules rule in action.ModifierRules)
                if (SpellModifierRuntimeRegistry.TryGet(rule.RuneId, out SpellModifierRuntime modifier))
                    modifier.BeforePlaceSpawn(simulation, action, action.Stats, index, ref position, ref direction);
        }

        /// <summary>포인터를 시전자 중심 최대 사거리로만 제한하고 index번째 모드 배치를 적용한 생성 위치를 반환한다. 최소 거리는 없다.</summary>
        internal static SimVector GetAreaTargetPosition(RuneSimulation simulation, SpellAction action, SimVector pointer, int index, SimVector? playerPosition)
        {
            SimVector origin = playerPosition ?? simulation.Player.Position;
            SimVector delta = pointer - origin;
            SimVector direction = delta.LengthSquared > 0.000001 ? delta.Normalized() : simulation.Player.AimDirection;
            SimVector position = ClampAreaTargetPosition(pointer, origin, simulation.GetAreaPlacementRange());
            foreach (SpellModifierRules rule in action.ModifierRules)
                if (SpellModifierRuntimeRegistry.TryGet(rule.RuneId, out SpellModifierRuntime modifier))
                    modifier.AfterPlaceSpawn(simulation, action, action.Stats, index, ref position, ref direction);
            return position;
        }
    }
}
