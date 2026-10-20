using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>처치 정리, 스크랩·드롭 회수, 줍기와 클리어 보상 추첨을 관리하는 전리품 시스템이다.</summary>
    internal sealed class LootSystem
    {
        private const double MIN_KILL_ENERGY = 1;
        private readonly SimulationWorld _world;
        private readonly SimulationStats _stats;
        private readonly EnemyStatusSystem _statuses;
        private readonly BattleRules _rules;
        private readonly RuneSimulation _simulation;
        private readonly Queue<SimulationEnemy> _deadEnemies = new Queue<SimulationEnemy>();
        private readonly List<FragmentOrb> _orbs = new List<FragmentOrb>();
        private readonly List<SimulationItemDrop> _itemDrops = new List<SimulationItemDrop>();
        private readonly List<FragmentOrb> _clearOrbs = new List<FragmentOrb>();
        private readonly List<SimulationItemDrop> _clearItemDrops = new List<SimulationItemDrop>();
        private readonly List<SimulationItemDrop> _pickedUpItems = new List<SimulationItemDrop>();
        private readonly List<SimulationItemDrop> _newPickedItems = new List<SimulationItemDrop>();
        private int _killCount;
        private int _collectedFragments;
        private int _normalScrapPickupEventCount;
        private int _bigScrapPickupEventCount;
        private int _bigScrapSpawnEventCount;
        private double _scrapRemainder;

        /// <summary>바닥의 스크랩 오브 목록이다. 같은 리스트 인스턴스를 반환한다.</summary>
        internal IReadOnlyList<FragmentOrb> Orbs => _orbs;
        /// <summary>바닥에 떨어져 줍기를 기다리는 Modifier 드롭 목록이다. 같은 리스트 인스턴스를 반환한다.</summary>
        internal IReadOnlyList<SimulationItemDrop> ItemDrops => _itemDrops;
        /// <summary>클리어 연출용으로 보존한 잔여 스크랩 목록이다. 같은 리스트 인스턴스를 반환한다.</summary>
        internal IReadOnlyList<FragmentOrb> ClearOrbs => _clearOrbs;
        /// <summary>클리어 연출용으로 보존한 잔여 룬 목록이다. 같은 리스트 인스턴스를 반환한다.</summary>
        internal IReadOnlyList<SimulationItemDrop> ClearItemDrops => _clearItemDrops;
        /// <summary>이번 전투에서 주워 확정한 Modifier 드롭의 누적 목록이다. 같은 리스트 인스턴스를 반환한다.</summary>
        internal IReadOnlyList<SimulationItemDrop> PickedUpItems => _pickedUpItems;
        /// <summary>이번 전투의 처치 수다.</summary>
        internal int KillCount => _killCount;
        /// <summary>플레이어가 직접 줍거나 클리어 시 회수한 스크랩 누계다.</summary>
        internal int CollectedFragments => _collectedFragments;
        /// <summary>스크랩 소수 보너스를 다음 드롭으로 이월하는 누적값이다.</summary>
        internal double ScrapRemainder => _scrapRemainder;
        /// <summary>일반 스크랩 줍기 횟수다.</summary>
        internal int NormalScrapPickupEventCount => _normalScrapPickupEventCount;
        /// <summary>대형 스크랩 줍기 횟수다.</summary>
        internal int BigScrapPickupEventCount => _bigScrapPickupEventCount;
        /// <summary>대형 스크랩 생성 횟수다.</summary>
        internal int BigScrapSpawnEventCount => _bigScrapSpawnEventCount;
        /// <summary>적 처치 스크랩에 곱하는 획득 배율이다.</summary>
        internal double ScrapGainMultiplier { get; }
        /// <summary>줍기 반경에 곱하는 배율이다.</summary>
        internal double ScrapPickupRangeMultiplier { get; }
        /// <summary>줍은 스크랩량의 최대 체력 비율 회복치다.</summary>
        internal double ScrapHealRatio { get; }
        /// <summary>처치 전력 회복에 더하는 고정 보너스다.</summary>
        internal double KillEnergyBonus { get; }

        /// <summary>
        /// 공유 월드·통계·상태이상 시스템, 처치 집계를 기록할 전투 규칙과 시뮬레이션, 스크랩 배율·보너스 값을 받아 전리품 시스템을 만든다.
        /// 회복 비율은 0~1로, 처치 전력 보너스는 0 이상으로 제한한다.
        /// </summary>
        internal LootSystem(SimulationWorld world, SimulationStats stats, EnemyStatusSystem statuses, BattleRules rules, RuneSimulation simulation,
            double scrapGainMultiplier, double scrapPickupRangeMultiplier, double scrapHealRatio, double killEnergyBonus)
        {
            _world = world;
            _stats = stats;
            _statuses = statuses;
            _rules = rules;
            _simulation = simulation;
            ScrapGainMultiplier = scrapGainMultiplier;
            ScrapPickupRangeMultiplier = scrapPickupRangeMultiplier;
            ScrapHealRatio = Math.Max(0, Math.Min(1, scrapHealRatio));
            KillEnergyBonus = Math.Max(0, killEnergyBonus);
        }

        /// <summary>
        /// 보스 스테이지 클리어 Modifier 선택 정보와 결정적 난수로 후보와 후보별 등급을 뽑아 반환한다. 선택 보상이 없으면 false를 반환한다.
        /// 슬롯별 등급 확률이 있으면 슬롯마다 등급을 따로 굴리고, 없으면 모든 슬롯이 설정 등급을 쓴다. 같은 Modifier는 다시 뽑지 않는다.
        /// </summary>
        internal bool TryDrawClearModifierChoices(out EliteModifierClearChoice choice, out List<string> candidates, out List<string> grades)
        {
            choice = _world.EliteDrops.GetClearChoice(_world.StageNumber);
            candidates = new List<string>();
            grades = new List<string>();
            if (choice == null || choice.ChoiceCount <= 0) return false;
            for (int i = 0; i < choice.ChoiceCount; i++)
            {
                string grade = choice.SlotGradeChances != null ? RollSlotGrade(choice.SlotGradeChances) : choice.Grade;
                var pool = new List<string>();
                foreach (string runeId in _world.EliteDrops.GetModifiersWithGrade(grade))
                    if (!candidates.Contains(runeId)) pool.Add(runeId);
                // 이 등급에 남은 후보가 없으면 슬롯을 비운다.
                if (pool.Count == 0) continue;
                candidates.Add(pool[Math.Min(pool.Count - 1, (int)(_world.Random.Next() * pool.Count))]);
                grades.Add(grade);
            }
            return candidates.Count > 0;
        }

        /// <summary>등급 확률 목록(퍼센트)으로 등급 하나를 굴려 반환한다. 확률 0인 등급은 뽑지 않는다.</summary>
        private string RollSlotGrade(IReadOnlyList<EliteModifierGradeChance> chances)
        {
            double roll = _world.Random.Next() * 100;
            string last = null;
            foreach (EliteModifierGradeChance chance in chances)
            {
                if (chance.Percent <= 0) continue;
                last = chance.Grade;
                roll -= chance.Percent;
                if (roll < 0) return chance.Grade;
            }
            return last;
        }

        /// <summary>적의 처치 재화에 설정 비율을 곱해 내림한 기본 회복량을 최소 1로 보정하고 트리의 추가 전력을 더해 회복한다.</summary>
        private void GrantKillEnergy(int droppedCredits)
        {
            double amount = Math.Floor(droppedCredits * _world.Balance.Economy.KillEnergyCreditRatio);
            _world.Player.RestoreEnergy(Math.Max(MIN_KILL_ENERGY, amount) + KillEnergyBonus);
        }

        /// <summary>처치된 적과 그 상태 이상을 제거하고 시간제 전투의 처치 통계, 목표 처치 수 집계, 보스 처치 판정과 RAM 오브를 생성한다. 분열형은 미션 중 처치되면 자식 생성을 예약한다.</summary>
        internal void CleanupEnemies(bool canExplode = true)
        {
            CollectDeadEnemies();
            while (_deadEnemies.Count > 0)
            {
                SimulationEnemy enemy = _deadEnemies.Dequeue();
                int droppedCredits = 0;
                if (_world.IsMission)
                {
                    _rules.RecordKillProgress(enemy);
                    if (enemy.Definition.CanSplit) _simulation.EnqueueSplits(enemy);
                    if (enemy.Reward > 0)
                    {
                        // 오브에 실제로 담기는 크레딧을 한 번만 산출해 나머지 누적이 이중 반영되지 않게 한다.
                        droppedCredits = ScaleKillScrap(enemy.Reward);
                        bool isBigScrap = enemy.IsElite || enemy.Boss != null;
                        _orbs.Add(new FragmentOrb(enemy.Position, droppedCredits, isBigScrap));
                        if (isBigScrap) _bigScrapSpawnEventCount++;
                    }
                    string dropTableId = enemy.CarrierOwnerId == 0 ? GetDropTableId(enemy) : null;
                    if (dropTableId != null) RollDropTable(_world.Drops.Get(dropTableId), enemy.Position);
                    _stats.KillCounts.TryGetValue(enemy.Kind, out int count); _stats.KillCounts[enemy.Kind] = count + 1; _killCount++;
                }
                GrantKillEnergy(droppedCredits);
                _statuses.RemoveEnemyStatuses(enemy.Id);
                if (canExplode && enemy.Definition.HasTrait(EnemyDefinition.TRAIT_DEATH_EXPLOSION))
                { _simulation.ExplodeEnemy(enemy); CollectDeadEnemies(); }
            }
        }

        /// <summary>죽은 적을 역순으로 목록에서 분리해 사망 처리 큐에 넣는다. 큐에 넣은 적은 다시 발견되지 않는다.</summary>
        private void CollectDeadEnemies()
        {
            for (int i = _world.Enemies.Count - 1; i >= 0; i--)
            {
                if (_world.Enemies[i].IsAlive) continue;
                _deadEnemies.Enqueue(_world.Enemies[i]); _world.Enemies.RemoveAt(i);
            }
        }

        /// <summary>스크랩을 매 틱 끌어당기고 도착한 오브의 재화·획득 횟수를 더하며 살아 있는 플레이어의 최대 체력 비율만큼 회복한다.</summary>
        internal void CollectNearbyOrbs()
        {
            for (int i = _orbs.Count - 1; i >= 0; i--)
            {
                FragmentOrb orb = _orbs[i];
                if (!TryAttractPickup(orb.Position, orb.IsAttracted, out SimVector nextPosition, out bool hasArrived)) continue;
                if (hasArrived)
                {
                    _collectedFragments += orb.Amount;
                    if (ScrapHealRatio > 0 && _world.Player.Hp > 0) _world.Player.Heal(_world.Player.MaxHp * ScrapHealRatio);
                    if (orb.IsBigScrap) _bigScrapPickupEventCount++;
                    else _normalScrapPickupEventCount++;
                    _orbs.RemoveAt(i);
                }
                else
                {
                    orb.AttractTo(nextPosition);
                    _orbs[i] = orb;
                }
            }
        }

        /// <summary>공통 줍기 반경과 추적 상태로 자석 이동 여부를 반환하고 다음 위치와 플레이어 도착 여부를 계산한다.</summary>
        private bool TryAttractPickup(SimVector position, bool isAttracted, out SimVector nextPosition, out bool hasArrived)
        {
            SimVector offset = _world.Player.Position - position;
            double distance = offset.Length;
            nextPosition = position;
            hasArrived = false;
            if (!isAttracted && distance > _world.Balance.Economy.OrbAbsorbRadius * ScrapPickupRangeMultiplier) return false;
            double step = _world.Balance.Economy.PickupAttractSpeed * RuneSimulation.STEP_SECONDS;
            hasArrived = distance <= step;
            nextPosition = hasArrived ? _world.Player.Position : position + offset * (step / distance);
            return true;
        }

        /// <summary>전투 종료 시 획득하지 않은 스크랩 오브를 보상에 더하지 않고 정리한다.</summary>
        internal void DiscardUncollectedOrbs() { _orbs.Clear(); }

        /// <summary>클리어 시 잔여 드롭을 연출용으로 보존하고 스크랩과 룬을 한 번 회수한 뒤 바닥 드롭 목록을 비운다.</summary>
        internal void CollectRemainingDrops()
        {
            _clearOrbs.AddRange(_orbs);
            _clearItemDrops.AddRange(_itemDrops);
            foreach (FragmentOrb orb in _orbs) _collectedFragments += orb.Amount;
            _orbs.Clear();
            _pickedUpItems.AddRange(_itemDrops);
            _newPickedItems.AddRange(_itemDrops);
            _itemDrops.Clear();
        }

        /// <summary>
        /// 처치된 적의 드롭 테이블 ID를 반환하고 없으면 null을 반환한다. 엘리트는 엘리트 정의의 드롭 확률로 판정해 당첨되면 그 테이블을 쓴다.
        /// 엘리트·보스가 아닌 일반 적은 보스 스테이지를 포함한 모든 스테이지에서 그 스테이지의 일반 적 드롭 확률로 1회 판정해 당첨되면 설정된 테이블을 쓴다.
        /// </summary>
        private string GetDropTableId(SimulationEnemy enemy)
        {
            if (enemy.Elite != null)
            {
                EliteSpawnDefinition elite = enemy.Elite;
                if (string.IsNullOrEmpty(elite.DropTable)) return null;
                // 확률이 100%면 난수를 쓰지 않아 기존 판정 순서를 유지한다.
                return elite.DropChancePercent >= 100 || _world.Random.Next() * 100 < elite.DropChancePercent ? elite.DropTable : null;
            }
            NormalEnemyDropDefinition normalDrop = _world.Drops.NormalEnemyDrop;
            if (enemy.Boss != null || normalDrop == null || !normalDrop.IsEnabled(_world.StageNumber)) return null;
            return _world.Random.Next() * 100 < normalDrop.GetChancePercent(_world.StageNumber) ? normalDrop.DropTable : null;
        }

        /// <summary>적 처치 스크랩에 획득 배율을 적용하고 소수 보너스를 다음 드롭으로 이월해 정수 스크랩량을 반환한다.</summary>
        private int ScaleKillScrap(int amount)
        {
            double scaled = amount * ScrapGainMultiplier + _scrapRemainder;
            int result = (int)Math.Floor(scaled + 0.000001);
            _scrapRemainder = Math.Max(0, scaled - result);
            return result;
        }

        /// <summary>처치 드롭과 같은 테이블·결정적 난수로 완료 스테이지의 클리어 룬 1개를 추첨한다.</summary>
        internal DropRoll DrawClearRuneReward()
        {
            return RuneRewardRoller.DrawRune(_world.Drops.Get(_world.Drops.NormalEnemyDrop.DropTable), _world.StageNumber, _world.EliteDrops, _world.Random.Next);
        }

        /// <summary>처치 위치에 놓을 룬의 분류·종류·등급을 추첨해 드롭 개체 1개를 생성한다.</summary>
        private void RollDropTable(DropTableDefinition table, SimVector position)
        {
            DropRoll roll = RuneRewardRoller.DrawRune(table, _world.StageNumber, _world.EliteDrops, _world.Random.Next);
            _itemDrops.Add(new SimulationItemDrop(roll.RuneId, roll.Grade, roll.Count, _world.Tick, position));
        }

        /// <summary>스크랩과 같은 반경에서 Modifier를 끌어당기고 플레이어에게 도착하면 누적 목록과 미지급 목록에 넣는다.</summary>
        internal void CollectNearbyItemDrops()
        {
            for (int i = _itemDrops.Count - 1; i >= 0; i--)
            {
                SimulationItemDrop drop = _itemDrops[i];
                if (!TryAttractPickup(drop.Position, drop.IsAttracted, out SimVector nextPosition, out bool hasArrived)) continue;
                drop.AttractTo(nextPosition);
                if (!hasArrived) continue;
                _itemDrops.RemoveAt(i);
                _pickedUpItems.Add(drop);
                _newPickedItems.Add(drop);
            }
        }

        /// <summary>아직 지급하지 않은 새로 주운 Modifier 드롭 목록을 반환하고 미지급 목록을 비운다.</summary>
        internal List<SimulationItemDrop> TakeNewPickedItems()
        {
            var items = new List<SimulationItemDrop>(_newPickedItems);
            _newPickedItems.Clear();
            return items;
        }

        /// <summary>시험 도크 초기화에 맞춰 클리어 연출용 잔여 목록을 비운다.</summary>
        internal void ClearClearFeedback()
        {
            _clearOrbs.Clear();
            _clearItemDrops.Clear();
        }

        /// <summary>시험 도크 초기화에 맞춰 바닥 오브·드롭과 줍기 누적 목록을 비운다.</summary>
        internal void ClearForBench()
        {
            _orbs.Clear();
            _itemDrops.Clear();
            _pickedUpItems.Clear();
            _newPickedItems.Clear();
        }

        /// <summary>시험 도크 초기화에 맞춰 획득 누계, 스크랩 나머지, 처치 수와 스크랩 카운터를 0으로 되돌린다.</summary>
        internal void ResetForBench()
        {
            _collectedFragments = 0;
            _scrapRemainder = 0;
            _killCount = 0;
            _normalScrapPickupEventCount = 0;
            _bigScrapPickupEventCount = 0;
            _bigScrapSpawnEventCount = 0;
        }

        /// <summary>전투 취소 시 사망 처리 대기 큐를 비운다.</summary>
        internal void ClearDeadEnemies() { _deadEnemies.Clear(); }
    }
}
