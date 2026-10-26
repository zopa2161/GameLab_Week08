using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>적 상태 이상(화염·냉기·빙결·EMP·찜통과 호환용 감전)의 부여, 틱 진행과 만료를 관리한다.</summary>
    internal sealed class EnemyStatusSystem
    {
        private const int STATUS_TYPE_COUNT = 6;
        private readonly SimulationWorld _world;
        private readonly SimulationStats _stats;
        private readonly RuneSimulation _simulation;
        private readonly List<EnemyStatusEffect> _enemyStatuses = new List<EnemyStatusEffect>();
        private readonly Dictionary<int, EnemyStatusEffect> _statusIndex = new Dictionary<int, EnemyStatusEffect>();
        private double _statusTime;
        private Action<SimulationEnemy> _statusStacked;

        /// <summary>현재 관리 중인 상태 이상 목록이다. 상태 해시가 같은 인스턴스를 순회한다.</summary>
        internal IReadOnlyList<EnemyStatusEffect> Statuses => _enemyStatuses;

        /// <summary>상태 이상 지속시간에 곱할 배율이다. 지속시간 강화가 없으면 1이라 기존 지속시간과 같다.</summary>
        private double StatusDurationMultiplier => 1d + _world.Upgrades.StatusDurationBonus;

        /// <summary>
        /// 공유 월드와 통계, 피해 판정을 호출할 시뮬레이션을 받아 상태 이상 시스템을 만든다.
        /// 피해 판정기·적 시스템이 이 시스템을 참조하므로 화상 피해(ApplyDamage)와 플레이어 피해(HurtPlayerByStatus)는 시뮬레이션 위임으로 부른다.
        /// </summary>
        internal EnemyStatusSystem(SimulationWorld world, SimulationStats stats, RuneSimulation simulation)
        {
            _world = world;
            _stats = stats;
            _simulation = simulation;
        }

        /// <summary>다른 종류의 상태 이상이 이미 걸린 적에게 새 상태 이상이 걸릴 때 호출할 콜백을 설정한다. null이면 알리지 않는다.</summary>
        internal void SetStatusStackedHandler(Action<SimulationEnemy> handler) => _statusStacked = handler;

        /// <summary>
        /// 상태 판정 시각을 현재 틱으로 갱신하고, 적 목록 순서로 화염의 마지막 만료 틱까지 지속 피해와 찜통의 마지막 만료 틱까지 주변 전파 피해를 적용한 뒤 만료된 상태 이상을 제거한다.
        /// </summary>
        internal void AdvanceStatuses()
        {
            _statusTime = _world.Time;
            while (_world.Player.Hp > 0 && _world.Player.TryTakeBurnTick(_world.Time, _world.Balance.Combat.BurnInterval))
                _simulation.HurtPlayerByStatus(_world.Balance.Combat.BurnDps * _world.Balance.Combat.BurnInterval);
            foreach (SimulationEnemy enemy in _world.Enemies)
            {
                enemy.Observe(_world.Time);
                EnemyStatusEffect burn = FindStatus(enemy.Id, EnemyStatusType.Burn);
                if (burn != null)
                {
                    while (enemy.IsAlive && burn.NextTickTime <= burn.Until + 0.000001 && _world.Time + 0.000001 >= burn.NextTickTime)
                    {
                        double actual = _simulation.ApplyDamage(enemy, burn.TickDamage, "fire", burn.SourceForm, burn.IsNoise, true, SimVector.Zero, false, sourceAction: burn.DamageSourceAction, damageStatus: "burn");
                        if (!(_world.IsMission && enemy.IsDummy)) _stats.StatusDamage += actual;
                        if (actual > 0) _stats.BurnTickDamageEventCount++;
                        burn.NextTickTime += _world.Balance.Combat.BurnInterval;
                    }
                }
                EnemyStatusEffect steam = FindStatus(enemy.Id, EnemyStatusType.Steam);
                if (steam != null)
                {
                    while (enemy.IsAlive && steam.NextTickTime <= steam.Until + 0.000001 && _world.Time + 0.000001 >= steam.NextTickTime)
                    {
                        ApplySteamTick(enemy, steam);
                        steam.NextTickTime += _world.Balance.Combat.SteamInterval;
                    }
                }
            }
            for (int i = _enemyStatuses.Count - 1; i >= 0; i--)
                if (IsStatusExpired(_enemyStatuses[i])) { RemoveStatusIndex(_enemyStatuses[i]); _enemyStatuses.RemoveAt(i); }
        }

        /// <summary>
        /// 상태 이상이 더 이상 판정·진행에 쓰이지 않는지 반환한다.
        /// 화염과 찜통은 마지막 틱까지 처리된 뒤, 빙결은 빙결 면역까지 끝난 뒤 만료된다.
        /// </summary>
        private bool IsStatusExpired(EnemyStatusEffect status)
        {
            switch (status.Type)
            {
                case EnemyStatusType.Burn:
                case EnemyStatusType.Steam: return status.NextTickTime > status.Until + 0.000001 && _world.Time >= status.Until;
                case EnemyStatusType.Freeze: return _world.Time >= status.ImmuneUntil;
                default: return _world.Time >= status.Until;
            }
        }

        /// <summary>
        /// 피해 속성에 맞는 상태 이상을 부여한다. 전격은 강화 여부와 무관하게 항상 EMP를 부여하고,
        /// 화염·냉기 전파(fire_arc·ice_arc)는 각각 화상·냉기만, 찜통(steam)은 Steam 상태만 부여한다.
        /// 새 상태 이상이 다른 속성의 상태 이상 위에 걸리면 부여 후 상태 중첩 콜백을 호출하며, 중첩 판정에서 화염·냉기 전파는 각각 화염·냉기로 본다.
        /// 화염은 전투 공통 보정 전 공격 피해 sourceDamage를 틱 피해 계산에 전달한다.
        /// </summary>
        internal void ApplyStatus(SimulationEnemy enemy, string element, string form, bool noise, double sourceDamage, SpellAction sourceAction)
        {
            if (enemy.Hydra != null && !IsHydraAcceptedElement(element)) return;
            string stackElement = GetStatusStackElement(element);
            bool isSameElementPresent = HasElementStatus(enemy, stackElement);
            bool isOtherElementPresent = HasOtherElementStatus(enemy, stackElement);
            bool isGranted = false;
            if (element == "fire" || element == SpellGrammar.ELEMENT_FIRE_ARC) { ApplyBurn(enemy, form, noise, sourceDamage, sourceAction); isGranted = !isSameElementPresent; }
            else if (element == "ice" || element == SpellGrammar.ELEMENT_ICE_ARC) { ApplyChill(enemy, sourceAction); isGranted = !isSameElementPresent; }
            else if (element == "arc")
            {
                GetOrAddStatus(enemy.Id, EnemyStatusType.Emp).Until = _world.Time + _world.Balance.Combat.EmpSeconds * StatusDurationMultiplier;
                isGranted = !isSameElementPresent;
            }
            else if (element == SpellGrammar.ELEMENT_STEAM) { ApplySteam(enemy, form, noise, sourceDamage, sourceAction); isGranted = !isSameElementPresent; }
            if (isGranted && isOtherElementPresent) _statusStacked?.Invoke(enemy);
        }

        /// <summary>
        /// 공격 피해 sourceDamage의 설정 비율을 화상 틱 피해로 계산하고, 새 화상이거나 기존보다 클 때만 피해·출처를 갱신하며 지속시간은 강화 배율로 갱신한다.
        /// 재점화 때마다 지속시간은 갱신하며 진행 중인 화상의 다음 틱 시각은 유지한다.
        /// 화염 강화(전역 또는 같은 속성 두 개)가 켜지면 부여 때마다 중첩과 화상 틱 피해를 저장하고 최대 중첩에 도달하면 대상의 상태 이상을 모두 지우고 중첩 틱 피해 합에 배율을 곱한 burn_burst 피해를 한 번 준다.
        /// </summary>
        private void ApplyBurn(SimulationEnemy enemy, string form, bool noise, double sourceDamage, SpellAction sourceAction)
        {
            CombatBalance combat = _world.Balance.Combat;
            EnemyStatusEffect burn = FindStatus(enemy.Id, EnemyStatusType.Burn);
            bool isNewBurn = burn == null || _world.Time >= burn.Until;
            if (isNewBurn)
            {
                burn ??= AddStatus(enemy.Id, EnemyStatusType.Burn);
                // 만료됐지만 아직 정리되지 않은 화상을 다시 쓰는 경우 이전 중첩이 이어지지 않게 비운다.
                burn.ClearFireStacks();
                burn.NextTickTime = _world.Time + combat.BurnInterval;
            }
            burn.Until = _world.Time + combat.BurnSeconds * StatusDurationMultiplier;
            double tickDamage = sourceDamage * combat.BurnDamageRatio;
            if (isNewBurn || tickDamage > burn.TickDamage)
            {
                burn.TickDamage = tickDamage;
                burn.DamageSourceAction = sourceAction;
                burn.SourceForm = form;
                burn.IsNoise = noise;
            }
            if (!IsFireEnhanced(sourceAction)) return;
            burn.AddFireStack(tickDamage);
            if (burn.Stacks < combat.BurnMaxStacks) return;
            double burstDamage = burn.GetFireBurstDamage(combat.BurnBurstMultiplier);
            RemoveEnemyStatuses(enemy.Id);
            _simulation.ApplyDamage(enemy, burstDamage, "fire", form, noise, false, SimVector.Zero, false, sourceAction: sourceAction, damageStatus: "burn_burst");
        }

        /// <summary>
        /// 공격 피해 sourceDamage의 설정 비율을 찜통 틱 피해로 계산하고, 새 찜통이거나 기존보다 클 때만 피해·출처를 갱신하며 지속시간은 강화 배율로 갱신한다.
        /// 재부여 때마다 지속시간은 갱신하며 진행 중인 찜통의 다음 틱 시각은 유지한다.
        /// </summary>
        private void ApplySteam(SimulationEnemy enemy, string form, bool noise, double sourceDamage, SpellAction sourceAction)
        {
            CombatBalance combat = _world.Balance.Combat;
            EnemyStatusEffect steam = FindStatus(enemy.Id, EnemyStatusType.Steam);
            bool isNewSteam = steam == null || _world.Time >= steam.Until;
            if (isNewSteam)
            {
                steam ??= AddStatus(enemy.Id, EnemyStatusType.Steam);
                steam.NextTickTime = _world.Time + combat.SteamInterval;
            }
            steam.Until = _world.Time + combat.SteamSeconds * StatusDurationMultiplier;
            double tickDamage = sourceDamage * combat.SteamDamageRatio;
            if (isNewSteam || tickDamage > steam.TickDamage)
            {
                steam.TickDamage = tickDamage;
                steam.DamageSourceAction = sourceAction;
                steam.SourceForm = form;
                steam.IsNoise = noise;
            }
        }

        /// <summary>찜통 발원지 위치 반경 SteamRadius 안의 다른 살아 있는 적(발원지 제외, 적 목록 순서)에게 찜통 틱 피해를 상태 이상 부여 없이 준다.</summary>
        private void ApplySteamTick(SimulationEnemy source, EnemyStatusEffect steam)
        {
            double steamRadius = _world.Balance.Combat.SteamRadius;
            foreach (SimulationEnemy target in _world.Enemies)
            {
                if (target == source || !target.IsAlive) continue;
                if (SimVector.Distance(target.Position, source.Position) > steamRadius) continue;
                double actual = _simulation.ApplyDamage(target, steam.TickDamage, SpellGrammar.ELEMENT_STEAM, steam.SourceForm, steam.IsNoise, true, SimVector.Zero, false, sourceAction: steam.DamageSourceAction, damageStatus: "steam");
                if (!(_world.IsMission && target.IsDummy)) _stats.StatusDamage += actual;
            }
        }

        /// <summary>
        /// 냉기 스택과 지속시간을 강화 배율로 갱신하고, 최대 스택에서 빙결 면역이 아니면 빙결을 부여하며 냉기를 제거한다.
        /// 빙결 부여는 기본 효과이며, 냉기 강화(전역 또는 같은 속성 두 개)가 켜지면 빙결 지속시간과 빙결 후 면역 시간을 강화 수치로 바꾼다.
        /// 빙결 면역 중에는 냉기 스택이 면역 중 상한(백서 기준 2)을 넘지 않는다.
        /// </summary>
        private void ApplyChill(SimulationEnemy enemy, SpellAction sourceAction)
        {
            CombatBalance combat = _world.Balance.Combat;
            EnemyStatusEffect freeze = FindStatus(enemy.Id, EnemyStatusType.Freeze);
            bool isImmune = freeze != null && _world.Time < freeze.ImmuneUntil;
            EnemyStatusEffect chill = GetOrAddStatus(enemy.Id, EnemyStatusType.Chill);
            int maxStacks = isImmune ? combat.ChillImmuneMaxStacks : combat.ChillMaxStacks;
            chill.Stacks = Math.Min(maxStacks, chill.Stacks + 1); chill.Until = _world.Time + combat.ChillSeconds * StatusDurationMultiplier;
            if (isImmune || chill.Stacks < combat.ChillMaxStacks) return;
            bool isEnhanced = IsIceEnhanced(sourceAction);
            freeze ??= AddStatus(enemy.Id, EnemyStatusType.Freeze);
            freeze.Until = _world.Time + (isEnhanced ? combat.FreezeEnhancedSeconds : combat.FreezeSeconds) * StatusDurationMultiplier;
            freeze.ImmuneUntil = freeze.Until + (isEnhanced ? combat.FreezeEnhancedImmunity : combat.FreezeImmunity);
            _stats.FreezeStartEventCount++;
            RemoveStatusIndex(chill); _enemyStatuses.Remove(chill);
        }

        /// <summary>적의 상태 이상이 현재 상태 판정 시각에 활성인지 반환한다. 냉기는 스택 보유 여부로 판정한다.</summary>
        internal bool HasEnemyStatus(SimulationEnemy enemy, EnemyStatusType type)
        {
            EnemyStatusEffect status = FindStatus(enemy.Id, type);
            if (status == null) return false;
            return type == EnemyStatusType.Chill ? status.Stacks > 0 : _statusTime < status.Until;
        }

        /// <summary>적의 현재 냉기 스택을 반환하며 냉기가 없으면 0을 반환한다.</summary>
        internal int GetChillStacks(SimulationEnemy enemy) => FindStatus(enemy.Id, EnemyStatusType.Chill)?.Stacks ?? 0;

        /// <summary>적에게 해당 속성(fire=Burn, ice=Chill과 Freeze, arc=Emp와 Shock) 또는 찜통(steam=Steam)의 상태 이상이 활성으로 걸려 있는지 반환한다.</summary>
        private bool HasElementStatus(SimulationEnemy enemy, string element)
        {
            switch (element)
            {
                case "fire": return HasEnemyStatus(enemy, EnemyStatusType.Burn);
                case "ice": return HasEnemyStatus(enemy, EnemyStatusType.Chill) || HasEnemyStatus(enemy, EnemyStatusType.Freeze);
                case SpellGrammar.ELEMENT_STEAM: return HasEnemyStatus(enemy, EnemyStatusType.Steam);
                default: return HasEnemyStatus(enemy, EnemyStatusType.Emp) || HasEnemyStatus(enemy, EnemyStatusType.Shock);
            }
        }

        /// <summary>히드라가 허용하는 상태 이상 속성인지 반환한다. 히드라는 화염과 화염 전파(fire_arc)만 받고 찜통·냉기 전파는 무시한다.</summary>
        private static bool IsHydraAcceptedElement(string element)
        {
            return element == "fire" || element == SpellGrammar.ELEMENT_FIRE_ARC;
        }

        /// <summary>상태 중첩 콜백의 속성 판정에 쓸 값을 반환한다. 화염·냉기 전파는 각각 화염·냉기로 본다.</summary>
        private static string GetStatusStackElement(string element)
        {
            if (element == SpellGrammar.ELEMENT_FIRE_ARC) return "fire";
            if (element == SpellGrammar.ELEMENT_ICE_ARC) return "ice";
            return element;
        }

        /// <summary>출처 행동이 같은 속성 두 개로 그 마법에만 속성 강화를 적용하는지 반환한다. 출처 행동이 없으면 false다.</summary>
        private static bool IsActionElementEnhanced(SpellAction sourceAction)
        {
            return sourceAction != null && sourceAction.IsElementEnhanced;
        }

        /// <summary>전역 화염 강화 또는 같은 속성 두 개 강화가 켜져 있는지 반환한다.</summary>
        private bool IsFireEnhanced(SpellAction sourceAction) => _world.Upgrades.HasFireEnhance || IsActionElementEnhanced(sourceAction);

        /// <summary>전역 냉기 강화 또는 같은 속성 두 개 강화가 켜져 있는지 반환한다. 빙결 부여 자체가 아니라 빙결 지속시간·면역 시간 강화에 쓴다.</summary>
        private bool IsIceEnhanced(SpellAction sourceAction) => _world.Upgrades.HasIceEnhance || IsActionElementEnhanced(sourceAction);

        /// <summary>적에게 전달한 속성 외의 다른 속성 상태 이상이 활성으로 걸려 있는지 반환한다.</summary>
        private bool HasOtherElementStatus(SimulationEnemy enemy, string element)
        {
            return (element != "fire" && HasElementStatus(enemy, "fire"))
                || (element != "ice" && HasElementStatus(enemy, "ice"))
                || (element != "arc" && HasElementStatus(enemy, "arc"));
        }

        /// <summary>적 ID와 종류로 상태 이상을 찾고 없으면 null을 반환한다.</summary>
        private EnemyStatusEffect FindStatus(int enemyId, EnemyStatusType type)
        { _statusIndex.TryGetValue(GetStatusKey(enemyId, type), out EnemyStatusEffect status); return status; }

        /// <summary>적 ID와 종류의 상태 이상을 찾고 없으면 새로 추가하여 반환한다.</summary>
        private EnemyStatusEffect GetOrAddStatus(int enemyId, EnemyStatusType type) => FindStatus(enemyId, type) ?? AddStatus(enemyId, type);

        /// <summary>새 상태 이상을 목록 끝과 조회 인덱스에 추가하여 반환한다.</summary>
        private EnemyStatusEffect AddStatus(int enemyId, EnemyStatusType type)
        { var status = new EnemyStatusEffect(enemyId, type); _enemyStatuses.Add(status); _statusIndex.Add(GetStatusKey(enemyId, type), status); return status; }

        /// <summary>제거된 적의 모든 상태 이상을 목록과 조회 인덱스에서 삭제한다.</summary>
        internal void RemoveEnemyStatuses(int enemyId)
        {
            for (int i = _enemyStatuses.Count - 1; i >= 0; i--)
                if (_enemyStatuses[i].EnemyId == enemyId) { RemoveStatusIndex(_enemyStatuses[i]); _enemyStatuses.RemoveAt(i); }
        }

        /// <summary>상태 이상을 조회 인덱스에서 삭제한다.</summary>
        private void RemoveStatusIndex(EnemyStatusEffect status) { _statusIndex.Remove(GetStatusKey(status.EnemyId, status.Type)); }

        /// <summary>적 ID와 상태 이상 종류를 하나의 조회 키로 합친다.</summary>
        private static int GetStatusKey(int enemyId, EnemyStatusType type) => enemyId * STATUS_TYPE_COUNT + (int)type;

        /// <summary>시험 도크 초기화에 맞춰 상태 이상 목록과 조회 인덱스를 비우고 판정 시각을 0으로 되돌린다.</summary>
        internal void ClearAll()
        {
            _enemyStatuses.Clear(); _statusIndex.Clear(); _statusTime = 0;
        }
    }
}
