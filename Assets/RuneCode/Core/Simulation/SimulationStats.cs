using System;
using System.Collections.Generic;
using System.Globalization;

namespace RuneCode
{
    /// <summary>시뮬레이션의 피해 누계, 이벤트 카운터, 자원 소비와 행동별 전투 로그를 기록한다.</summary>
    internal sealed class SimulationStats
    {
        private const int ROUND_BREAK_DPS_WINDOW_TICKS = 5 * RuneSimulation.TICK_RATE;

        private readonly Dictionary<string, Dictionary<string, object>> _bossPatternRows = new Dictionary<string, Dictionary<string, object>>();

        private readonly Dictionary<int, Dictionary<string, object>> _enemyLogRows = new Dictionary<int, Dictionary<string, object>>();
        private readonly HashSet<int> _dirtyEnemyRows = new HashSet<int>();

        private readonly Queue<DamageSample> _damageWindow = new Queue<DamageSample>();
        private readonly Queue<DamageSample> _roundBreakDamageWindow = new Queue<DamageSample>();
        private readonly List<double> _damageBySecond = new List<double>();
        private readonly Dictionary<int, double> _damageByEnemyId = new Dictionary<int, double>();
        private readonly Dictionary<string, double> _resourceCostsSpent = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _killCounts = new Dictionary<string, int>();
        private readonly Dictionary<SpellAction, ActionCombatStats> _actionCombatStats = new Dictionary<SpellAction, ActionCombatStats>();
        private readonly Dictionary<SpellAction, int> _actionLogSlots = new Dictionary<SpellAction, int>();

        private double _roundBreakDamage;

        /// <summary>전투 전체의 실제 피해 누적값이다.</summary>
        internal double TotalDamage { get; set; }
        /// <summary>상태 이상(화염) 틱으로 입힌 실제 피해의 누적값이다.</summary>
        internal double StatusDamage { get; set; }
        /// <summary>잔류(Persist) 판정 틱으로 입힌 실제 피해의 누적값이다.</summary>
        internal double PersistDamage { get; set; }
        /// <summary>5초 롤링 DPS 계산에 쓰는 실제 피해 누적값이다.</summary>
        internal double RollingDamage { get; set; }
        /// <summary>동시 존재 마법 개체 수의 최댓값이다.</summary>
        internal int PeakSpellEntities { get; set; }
        /// <summary>노드 실행 누적 횟수다.</summary>
        internal int NodeExecutionCount { get; set; }
        /// <summary>적에게 실제 피해를 준 이벤트 횟수다.</summary>
        internal int EnemyDamageEventCount { get; set; }
        /// <summary>발사형 마법 개체 생성 횟수다.</summary>
        internal int ShotLaunchEventCount { get; set; }
        /// <summary>폭발형 마법 개체 생성 횟수다.</summary>
        internal int ShotBurstEventCount { get; set; }
        /// <summary>잔류형 마법 개체 생성 횟수다.</summary>
        internal int ShotPersistEventCount { get; set; }
        /// <summary>참격형 마법 개체 생성 횟수다.</summary>
        internal int ShotSlashEventCount { get; set; }
        /// <summary>화염 지속 피해 틱 횟수다.</summary>
        internal int BurnTickDamageEventCount { get; set; }
        /// <summary>전격 연쇄 피해 이벤트 횟수다.</summary>
        internal int ArcDamageEventCount { get; set; }
        /// <summary>빙결 부여 횟수다.</summary>
        internal int FreezeStartEventCount { get; set; }
        /// <summary>루트 시전 시도 횟수다. 쿨다운을 시작해 true를 반환한 시전만 센다.</summary>
        internal int RootCastAttempts { get; set; }
        /// <summary>루트 첫 행동의 자원 지불에 성공한 시전 횟수다. 비용이 0이라 항상 성공하는 경우도 포함한다.</summary>
        internal int RootCastSuccesses { get; set; }
        /// <summary>루트 첫 행동의 자원 지불이 자원 부족으로 실패한 시전 횟수다.</summary>
        internal int RootCastFailures { get; set; }
        /// <summary>루트 첫 행동 지불이 처음 실패한 틱이다. 실패가 없으면 -1이다.</summary>
        internal int FirstRootCastFailureTick { get; set; } = -1;

        /// <summary>처치한 적 종류별 횟수다. 처치 정리가 인덱스로 갱신하므로 같은 딕셔너리 인스턴스를 노출한다.</summary>
        internal Dictionary<string, int> KillCounts => _killCounts;
        /// <summary>자원별 소비 누계다. 같은 딕셔너리 인스턴스를 반환한다.</summary>
        internal IReadOnlyDictionary<string, double> ResourceCostsSpent => _resourceCostsSpent;
        /// <summary>초 버킷별 실제 피해 누적값이다. 같은 리스트 인스턴스를 읽기 전용으로 노출한다.</summary>
        internal IReadOnlyList<double> DamageBySecond => _damageBySecond;
        /// <summary>적 ID별 실제 피해 누적값이다. 같은 딕셔너리 인스턴스를 읽기 전용으로 노출한다.</summary>
        internal IReadOnlyDictionary<int, double> DamageByEnemyId => _damageByEnemyId;
        /// <summary>마나 자원의 소비 누계다. 기록이 없으면 0이다.</summary>
        internal double EnergySpent => _resourceCostsSpent.TryGetValue("mana", out double amount) ? amount : 0;

        /// <summary>미션에서 생성한 적의 유형·스테이지·라운드와 전투 시간을 로그 누계에 등록한다.</summary>
        internal void RecordEnemySpawn(SimulationWorld world, SimulationEnemy enemy)
        {
            if (!world.IsMission || enemy.IsDummy) return;
            _enemyLogRows[enemy.Id] = new Dictionary<string, object>
            {
                ["enemy_id"] = enemy.Id, ["enemy_kind"] = enemy.Kind,
                ["is_boss"] = enemy.Boss != null, ["summoner_id"] = enemy.SummonerId,
                ["stage"] = world.StageNumber, ["round"] = world.RoundNumber, ["is_elite"] = enemy.IsElite,
                ["spawn_seconds"] = world.Time, ["observed_seconds"] = world.Time,
                ["lifetime_seconds"] = null, ["end_reason"] = null,
                ["damage_hp"] = 0d, ["shield_absorbed"] = 0d, ["hit_count"] = 0,
            };
            _dirtyEnemyRows.Add(enemy.Id);
        }

        /// <summary>증원 생성 결과에 소환 보스와 패턴을 로그용으로 연결한다. 게임 개체의 소유권은 변경하지 않는다.</summary>
        internal void RecordBossSummon(SimulationEnemy summon, int bossId, string pattern)
        {
            if (summon == null || !_enemyLogRows.TryGetValue(summon.Id, out Dictionary<string, object> row)) return;
            row["summoner_id"] = bossId;
            row["summoner_pattern"] = pattern;
            _dirtyEnemyRows.Add(summon.Id);
        }

        /// <summary>실제 사망 또는 강제 제거 시각을 한 번 기록한다. 사망한 적만 생존시간을 확정한다.</summary>
        internal void RecordEnemyEnd(SimulationEnemy enemy, double time, string reason)
        {
            if (!_enemyLogRows.TryGetValue(enemy.Id, out Dictionary<string, object> row) || row["end_reason"] != null) return;
            row["observed_seconds"] = time;
            row["end_reason"] = reason;
            if (reason == "killed") row["lifetime_seconds"] = Math.Max(0, time - (double)row["spawn_seconds"]);
            _dirtyEnemyRows.Add(enemy.Id);
        }

        /// <summary>실제 HP 감소와 보호막 흡수를 공격한 적의 누계에 더한다. 상태 이상과 출처 불명은 적 통계와 구분한다.</summary>
        internal void RecordPlayerDamage(SimulationWorld world, int sourceId, double hp, double shield, string pattern = null)
        {
            if (!world.IsMission || hp <= 0 && shield <= 0) return;
            int bossId = sourceId;
            if (_enemyLogRows.TryGetValue(sourceId, out Dictionary<string, object> source))
            {
                int summoner = source.TryGetValue("summoner_id", out object parent) ? (int)parent : 0;
                if (summoner > 0)
                { bossId = summoner; pattern = source.TryGetValue("summoner_pattern", out object summonedPattern) ? (string)summonedPattern : "summon"; }
                if (_enemyLogRows.TryGetValue(bossId, out Dictionary<string, object> boss) && boss.TryGetValue("is_boss", out object isBoss) && (bool)isBoss)
                {
                    Dictionary<string, object> patternRow = GetBossPatternRow(world, bossId, pattern ?? "contact");
                    patternRow["damage_hp"] = (double)patternRow["damage_hp"] + hp;
                    patternRow["shield_absorbed"] = (double)patternRow["shield_absorbed"] + shield;
                    patternRow["hit_count"] = (int)patternRow["hit_count"] + 1;
                }
            }
            int key = sourceId;
            if (sourceId <= 0) key = -((world.StageNumber * 1000 + world.RoundNumber) * 2 + (sourceId < 0 ? 1 : 0));
            if (!_enemyLogRows.TryGetValue(key, out Dictionary<string, object> row))
            {
                row = new Dictionary<string, object>
                {
                    ["enemy_id"] = key, ["enemy_kind"] = sourceId < 0 ? "player_status" : "unknown",
                    ["stage"] = world.StageNumber, ["round"] = world.RoundNumber, ["is_elite"] = false,
                    ["spawn_seconds"] = null, ["observed_seconds"] = world.Time,
                    ["lifetime_seconds"] = null, ["end_reason"] = "non_enemy_source",
                    ["damage_hp"] = 0d, ["shield_absorbed"] = 0d, ["hit_count"] = 0,
                };
                _enemyLogRows.Add(key, row);
            }
            row["damage_hp"] = (double)row["damage_hp"] + hp;
            row["shield_absorbed"] = (double)row["shield_absorbed"] + shield;
            row["hit_count"] = (int)row["hit_count"] + 1;
            _dirtyEnemyRows.Add(key);
        }

        /// <summary>보스·현재 스테이지·라운드·패턴별 누계 행을 반환하고 다음 스냅샷 대상으로 표시한다.</summary>
        private Dictionary<string, object> GetBossPatternRow(SimulationWorld world, int enemyId, string pattern)
        {
            string key = enemyId + ":" + world.StageNumber + ":" + world.RoundNumber + ":" + pattern;
            if (!_bossPatternRows.TryGetValue(key, out Dictionary<string, object> row))
            {
                row = new Dictionary<string, object>
                {
                    ["enemy_id"] = enemyId, ["stage"] = world.StageNumber, ["round"] = world.RoundNumber,
                    ["pattern"] = pattern, ["use_count"] = 0, ["damage_hp"] = 0d,
                    ["shield_absorbed"] = 0d, ["hit_count"] = 0,
                };
                _bossPatternRows.Add(key, row);
            }
            _dirtyEnemyRows.Add(enemyId);
            return row;
        }

        /// <summary>미션에서 보스 패턴을 실제 시작한 횟수를 한 번 누적한다. 탄환 수나 피격 여부와 무관하다.</summary>
        internal void RecordBossPatternUse(SimulationWorld world, SimulationEnemy enemy, string pattern)
        {
            if (!world.IsMission) return;
            Dictionary<string, object> row = GetBossPatternRow(world, enemy.Id, pattern);
            row["use_count"] = (int)row["use_count"] + 1;
        }

        /// <summary>변경된 적 누계와 생존 중인 적의 관측 상태를 복사한다. 확정하지 못한 생존시간은 null로 유지한다.</summary>
        internal IReadOnlyList<Dictionary<string, object>> TakeEnemyCombatLogSnapshot(double time)
        {
            var result = new List<Dictionary<string, object>>();
            foreach (KeyValuePair<int, Dictionary<string, object>> entry in _enemyLogRows)
            {
                Dictionary<string, object> row = entry.Value;
                bool isAlive = row["end_reason"] == null;
                if (!isAlive && !_dirtyEnemyRows.Contains(entry.Key)) continue;
                if (isAlive) row["observed_seconds"] = time;
                var snapshot = new Dictionary<string, object>(row);
                if (row.TryGetValue("is_boss", out object isBoss) && (bool)isBoss)
                {
                    var patterns = new List<Dictionary<string, object>>();
                    foreach (Dictionary<string, object> patternRow in _bossPatternRows.Values)
                        if ((int)patternRow["enemy_id"] == entry.Key) patterns.Add(new Dictionary<string, object>(patternRow));
                    if (patterns.Count > 0) snapshot["boss_patterns"] = patterns;
                }
                result.Add(snapshot);
            }
            _dirtyEnemyRows.Clear();
            return result;
        }

        /// <summary>실제 피해를 총 피해와 롤링 DPS 누적, 초 버킷과 적별 누계에 더하고 5초 창에 샘플을 넣는다.</summary>
        internal void RecordEnemyDamage(double actual, int tick, int enemyId)
        {
            TotalDamage += actual; RollingDamage += actual; _damageWindow.Enqueue(new DamageSample(tick, actual));
            int second = tick / RuneSimulation.TICK_RATE;
            while (_damageBySecond.Count <= second) _damageBySecond.Add(0d);
            _damageBySecond[second] += actual;
            _damageByEnemyId.TryGetValue(enemyId, out double byEnemy);
            _damageByEnemyId[enemyId] = byEnemy + actual;
        }

        /// <summary>자원별 소비량을 기존 누계에 더한다.</summary>
        internal void AddResourceCost(ResourceAmount amount)
        {
            _resourceCostsSpent.TryGetValue(amount.Resource, out double spent);
            _resourceCostsSpent[amount.Resource] = spent + amount.Amount;
        }

        /// <summary>5초 창을 벗어난 롤링 DPS 샘플을 제거하고 누적값에서 뺀다.</summary>
        internal void TrimRollingDamage(int tick)
        {
            while (_damageWindow.Count > 0 && tick - _damageWindow.Peek().Tick > 5 * RuneSimulation.TICK_RATE) RollingDamage -= _damageWindow.Dequeue().Amount;
        }

        /// <summary>라운드 대기 허수아비에게 준 실제 피해를 5초 창에 넣는다.</summary>
        internal void RecordRoundBreakDamage(double actual, int tick)
        {
            _roundBreakDamage += actual;
            _roundBreakDamageWindow.Enqueue(new DamageSample(tick, actual));
        }

        /// <summary>5초 창을 벗어난 허수아비 피해 샘플을 제거하고 누적값에서 뺀다.</summary>
        internal void TrimRoundBreakDamage(int tick)
        {
            while (_roundBreakDamageWindow.Count > 0 && tick - _roundBreakDamageWindow.Peek().Tick > ROUND_BREAK_DPS_WINDOW_TICKS)
                _roundBreakDamage -= _roundBreakDamageWindow.Dequeue().Amount;
        }

        /// <summary>허수아비 DPS 누적과 5초 창을 비워 다음 피해부터 새로 측정하게 한다.</summary>
        internal void ResetRoundBreakDamage()
        {
            _roundBreakDamageWindow.Clear();
            _roundBreakDamage = 0;
        }

        /// <summary>최근 5초 허수아비 피해 합을 5로 나눈 DPS를 반환한다.</summary>
        internal double GetRoundBreakDps() => _roundBreakDamage / 5.0;

        /// <summary>시험 도크 초기화에 맞춰 누계·카운터, 자원 소비, 종류별 처치 수와 DPS 창을 모두 되돌린다.</summary>
        internal void ResetForBench()
        {
            _bossPatternRows.Clear();
            _enemyLogRows.Clear();
            _dirtyEnemyRows.Clear();
            _damageWindow.Clear();
            _roundBreakDamageWindow.Clear();
            _damageBySecond.Clear();
            _damageByEnemyId.Clear();
            _killCounts.Clear();
            TotalDamage = 0; StatusDamage = 0; PersistDamage = 0; RollingDamage = 0; _roundBreakDamage = 0;
            _resourceCostsSpent.Clear(); PeakSpellEntities = 0;
            NodeExecutionCount = 0;
            EnemyDamageEventCount = 0;
            ShotLaunchEventCount = 0;
            ShotBurstEventCount = 0;
            ShotPersistEventCount = 0;
            ShotSlashEventCount = 0;
            BurnTickDamageEventCount = 0;
            ArcDamageEventCount = 0;
            FreezeStartEventCount = 0;
            RootCastAttempts = 0;
            RootCastSuccesses = 0;
            RootCastFailures = 0;
            FirstRootCastFailureTick = -1;
        }

        /// <summary>장착된 행동을 순회하고 노드별 실제 피해와 OnHit 후속 생성 누계를 로그용 복사본으로 반환한다.</summary>
        internal IReadOnlyList<Dictionary<string, object>> GetActionCombatLogSnapshot(IReadOnlyList<CompiledSpell> loadout)
        {
            var visited = new HashSet<SpellAction>();
            for (int slot = 0; slot < loadout.Count; slot++)
                if (loadout[slot] != null) RegisterActionLogNodes(loadout[slot].Root, slot, visited);
            var result = new List<Dictionary<string, object>>();
            foreach (KeyValuePair<SpellAction, ActionCombatStats> entry in _actionCombatStats)
            {
                SpellAction action = entry.Key;
                ActionCombatStats stats = entry.Value;
                var targets = new List<Dictionary<string, object>>();
                foreach (KeyValuePair<SpellAction, long> target in stats.OnHitTargets)
                    targets.Add(new Dictionary<string, object>
                    {
                        ["action_key"] = GetActionCombatStats(target.Key).Key,
                        ["node_id"] = target.Key.NodeId,
                        ["spawn_count"] = target.Value,
                    });
                result.Add(new Dictionary<string, object>
                {
                    ["action_key"] = stats.Key,
                    ["node_id"] = action.NodeId,
                    ["loadout_slot"] = _actionLogSlots.TryGetValue(action, out int slot) ? slot : -1,
                    ["action_type"] = action.Form ?? action.Kind,
                    ["element"] = action.Element,
                    ["mods"] = action.Mods,
                    ["attached_node_ids"] = action.AttachedNodeIds,
                    ["spawn_count"] = stats.SpawnCount,
                    ["damage_dealt"] = stats.Damage,
                    ["status_damage"] = stats.StatusDamage,
                    ["direct_damage_by_element"] = new Dictionary<string, double>(stats.DirectDamageByElement),
                    ["status_damage_by_type"] = new Dictionary<string, double>(stats.StatusDamageByType),
                    ["damage_count"] = stats.DamageCount,
                    ["hit_count"] = stats.HitCount,
                    ["on_hit_events"] = stats.OnHitEvents,
                    ["on_hit_spawn_count"] = stats.OnHitSpawnCount,
                    ["triggered_by_on_hit_count"] = stats.TriggeredByOnHitCount,
                    ["on_hit_targets"] = targets,
                });
            }
            return result;
        }

        /// <summary>장착 슬롯의 연결 분기를 순회해 미발동 생성 행동도 0으로 등록하고 노드의 슬롯을 연결한다.</summary>
        private void RegisterActionLogNodes(IReadOnlyList<SpellAction> actions, int slot, HashSet<SpellAction> visited)
        {
            foreach (SpellAction action in actions)
            {
                if (!visited.Add(action)) continue;
                _actionLogSlots[action] = slot;
                if (action.Kind == "spawn") GetActionCombatStats(action);
                foreach (IReadOnlyList<SpellAction> branch in action.Branches) RegisterActionLogNodes(branch, slot, visited);
                if (action.CalledSpell != null) RegisterActionLogNodes(action.CalledSpell.Root, slot, visited);
            }
        }

        /// <summary>컴파일 행동 인스턴스의 전투 누계를 반환하고 처음 관측하면 로그 내 고유 키로 생성한다.</summary>
        internal ActionCombatStats GetActionCombatStats(SpellAction action)
        {
            if (!_actionCombatStats.TryGetValue(action, out ActionCombatStats stats))
            {
                stats = new ActionCombatStats("action-" + (_actionCombatStats.Count + 1).ToString(CultureInfo.InvariantCulture));
                _actionCombatStats.Add(action, stats);
            }
            return stats;
        }

        /// <summary>노드 ID별 실제 피해 합계(직접 및 상태이상)를 반환한다. 같은 노드 ID의 행동은 합산하고 기록이 없으면 0이다.</summary>
        internal double GetNodeDamage(string nodeId)
        {
            double total = 0;
            foreach (KeyValuePair<SpellAction, ActionCombatStats> entry in _actionCombatStats)
                if (entry.Key.NodeId == nodeId) total += entry.Value.Damage;
            return total;
        }

        /// <summary>실제로 생성된 개체만 세고 OnHit 문맥이면 원본 행동과 후속 행동의 연결 누계를 함께 갱신한다.</summary>
        internal void RecordActionSpawn(SpellAction action, SpellContext context)
        {
            ActionCombatStats stats = GetActionCombatStats(action);
            stats.RecordSpawn(context.OnHitSource != null);
            if (context.OnHitSource != null) GetActionCombatStats(context.OnHitSource).RecordOnHitTarget(action);
        }
    }
}
