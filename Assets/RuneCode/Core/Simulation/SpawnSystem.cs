using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>미션 스폰 진행, 진형·맵형 배치, 분열 대기, 보스 증원과 적 생성을 담당한다. 난수는 월드의 공유 난수를 같은 순서로 쓴다.</summary>
    internal sealed class SpawnSystem
    {
        internal const double SAME_SPAWN_DISTANCE = 0.5;
        private const int ARENA_PICK_ROUNDS = 2;

        private readonly SimulationWorld _world;
        private readonly SimulationStats _stats;
        private readonly SimulationFeedback _feedback;
        private readonly List<PendingSplit> _pendingSplits = new List<PendingSplit>();
        private readonly List<PendingSpawn> _pendingSpawns = new List<PendingSpawn>();
        private readonly List<SimVector> _tickSpawnPositions = new List<SimVector>();
        private readonly SpawnSlots _spawnSlots;
        private readonly ArenaSpawnPoints _arenaSpawnPoints;
        private readonly Func<SimVector, bool> _isArenaPointReserved;
        private SpawnDirector _director;
        private double _spawnRadius;
        private int _tickSpawnPositionsTick = -1;
        private int _spawnedEnemies;

        /// <summary>분열 대기 목록이다. 상태 해시와 전투 취소가 같은 인스턴스를 쓴다.</summary>
        internal List<PendingSplit> PendingSplits => _pendingSplits;

        /// <summary>맵형 스폰 예고 대기 목록이다. 상태 해시와 전투 취소가 같은 인스턴스를 쓴다.</summary>
        internal List<PendingSpawn> PendingSpawns => _pendingSpawns;

        /// <summary>현재 미션의 스폰 진행기다. 전투 시작 시 설정한다.</summary>
        internal SpawnDirector Director { get => _director; set => _director = value; }

        internal bool IsWaveSpawnFinished => _director != null && _director.IsFinished && _pendingSpawns.Count == 0 && _pendingSplits.Count == 0;

        /// <summary>현재 유한 웨이브의 미배출 편성·스폰 예고·분열 예약과 살아 있는 적 수를 합산한다.</summary>
        internal int RemainingWaveEnemyCount => _director.RemainingWaveSpawns + _pendingSpawns.Count + _pendingSplits.Count + _world.Enemies.Count;

        /// <summary>현재 편성의 웨이브만 시작하고 첫 틱 스폰과 예고 표시를 준비한다.</summary>
        internal void StartWave(WaveDefinition wave)
        {
            _director = new SpawnDirector(wave, _world.Formations, RuneSimulation.TICK_RATE, _world.Tick);
            AdvanceSpawns();
            RefreshSpawnWarnings();
        }

        /// <summary>플레이어 중심 스폰 링 반경이다. 전투 시작 시 설정한다.</summary>
        internal double SpawnRadius { get => _spawnRadius; set => _spawnRadius = value; }

        /// <summary>이번 미션에서 생성한 적 수다. 시험 도크 초기화 때 0으로 되돌린다.</summary>
        internal int SpawnedEnemies => _spawnedEnemies;

        /// <summary>시험 도크 초기화 시 생성한 적 수를 0으로 되돌린다.</summary>
        internal void ResetForBench() => _spawnedEnemies = 0;

        /// <summary>월드·표시 버퍼·생성 통계 참조를 보관하고 공유 난수와 적 목록을 쓰는 슬롯·맵 배치 보조를 준비한다.</summary>
        internal SpawnSystem(SimulationWorld world, SimulationFeedback feedback, SimulationStats stats)
        {
            _world = world;
            _stats = stats;
            _feedback = feedback;
            _spawnSlots = new SpawnSlots(_world.Random.Next, _world.Enemies);
            _arenaSpawnPoints = new ArenaSpawnPoints(_world.Random.Next, _world.Enemies);
            _isArenaPointReserved = IsArenaPointReserved;
        }
        /// <summary>분열형 적이 미션 중 처치되면 자식 수만큼 대기 항목을 만든다. 위치는 부모 진행 방향의 수직선 위에서 자식 반경 간격으로 균등 분배하고, 엘리트 정의는 그대로 물려준다.</summary>
        internal void EnqueueSplits(SimulationEnemy parent)
        {
            EnemyDefinition definition = parent.Definition;
            int delayTicks = Math.Max(1, (int)Math.Round(definition.SplitDelay * RuneSimulation.TICK_RATE));
            SimVector side = new SimVector(-parent.Facing.Y, parent.Facing.X);
            double childRadius = _world.EnemyCatalog.Get(definition.SplitInto).Radius * (parent.Elite != null ? parent.Elite.RadiusMultiplier : 1);
            for (int i = 0; i < definition.SplitCount; i++)
            {
                double offsetRatio = definition.SplitCount == 1 ? 0 : i / (double)(definition.SplitCount - 1) * 2 - 1;
                _pendingSplits.Add(new PendingSplit(_world.Tick + delayTicks, definition.SplitInto, parent.Position + side * (childRadius * offsetRatio), parent.Elite, parent.CarrierOwnerId));
            }
        }

        /// <summary>
        /// 현재 틱까지 도달한 웨이브 스폰을 순서대로 처리한다. 엘리트 유닛은 현재 연속 스테이지의 엘리트 정의로 생성한다.
        /// 살아 있는 적과 예고 대기 스폰을 합친 수가 상한이면 이번 틱 처리를 멈춘다.
        /// </summary>
        internal void AdvanceSpawns()
        {
            while (_director.TryPeek(_world.Tick, out SpawnOrder order))
            {
                if (_world.Enemies.Count + _pendingSpawns.Count >= _world.EnemyLimit) return;
                _director.Consume(_world.Tick);
                EliteSpawnDefinition elite = null;
                if (order.IsElite) _world.Stages.GetProgressionStage(_world.StageNumber).TryGetElite(order.EnemyId, out elite);
                SpawnByFormation(order, order.EnemyId, elite, isKillTarget: true);
            }
        }

        /// <summary>분열 대기 항목 중 생성 틱에 도달한 것을 목록 순서대로 생성한다. 적 수가 상한이면 남은 항목을 다음 틱에 다시 시도한다.</summary>
        internal void AdvanceSplits()
        {
            for (int i = 0; i < _pendingSplits.Count;)
            {
                if (_world.Enemies.Count >= _world.EnemyLimit) return;
                PendingSplit split = _pendingSplits[i];
                if (split.Tick > _world.Tick) { i++; continue; }
                _pendingSplits.RemoveAt(i);
                SpawnEnemy(split.EnemyId, split.Position, false, 0, split.Elite, carrierOwnerId: split.CarrierOwnerId);
            }
        }

        /// <summary>
        /// 예약의 진형으로 플레이어 주변 원형 슬롯 중 위치를 정해 적을 생성한다. elite가 있으면 엘리트 배율로 생성하고,
        /// isKillTarget은 웨이브 스폰에 true로 받아 목표 처치 수 집계 대상으로 만든다.
        /// 웨이브의 첫 스폰에서 슬롯 시작 각도(무작위, 진행 방향 또는 고정 0도)와 기준 슬롯을 정하고, 위치는 스폰 순간의 플레이어 위치 기준으로 계산한다.
        /// 맵형 진형은 원형 슬롯 대신 EnqueueArenaSpawn으로 예고 대기 스폰을 등록한다.
        /// </summary>
        private void SpawnByFormation(SpawnOrder order, string enemyId, EliteSpawnDefinition elite, bool isKillTarget)
        {
            FormationSettings settings = _director.Settings;
            SpawnFormation formation = SpawnFormations.Get(settings.Shape);
            double radius = _world.EnemyCatalog.Get(enemyId).Radius * (elite != null ? elite.RadiusMultiplier : 1);
            if (formation.IsArena) { EnqueueArenaSpawn(order, enemyId, elite, isKillTarget, formation, settings, radius); return; }
            int slotCount = settings.Slots > 0 ? settings.Slots : _world.Stages.Spawn.RingSlots;
            double spawnRadius = _spawnRadius * settings.RadiusScale;
            if (!_director.IsStarted)
            {
                double startAngle = settings.Angle == SpawnFormations.ANGLE_AHEAD ? GetAheadAngle()
                    : settings.Angle == SpawnFormations.ANGLE_FIXED ? 0 : _world.Random.Next() * 2 * Math.PI / slotCount;
                _spawnSlots.Refresh(_world.Map, _world.Player.Position, spawnRadius, slotCount, startAngle, radius);
                _director.Start(startAngle, formation.ChooseAnchor(_spawnSlots, settings));
            }
            else _spawnSlots.Refresh(_world.Map, _world.Player.Position, spawnRadius, slotCount, _director.StartAngle, radius);
            SpawnPlacement placement = formation.Place(order.Index, order.Count, _director.Anchor, _spawnSlots, settings);
            SpawnEnemy(enemyId, ResolvePlacement(placement, radius), false, 0, elite, isKillTarget);
        }

        /// <summary>
        /// 맵형 진형 예약의 생성 위치를 정해 예고 시간 뒤에 생성할 대기 스폰으로 등록한다. 무리를 공유하는 진형은 웨이브의 첫 스폰에서 무리 중심을 정하고
        /// 각 기체를 중심 기준 오프셋에 두며, 그 위치가 맵 밖이거나 플레이어 최소 거리 안이면 무리 중심을 쓴다. 예고 표시는 무리 중심과 무리 반경을 쓴다.
        /// 그 외 맵형 진형은 기체마다 맵 전체에서 위치를 고른다.
        /// </summary>
        private void EnqueueArenaSpawn(SpawnOrder order, string enemyId, EliteSpawnDefinition elite, bool isKillTarget,
            SpawnFormation formation, in FormationSettings settings, double radius)
        {
            SimVector position;
            SimVector warningCenter;
            double warningRadius = radius;
            if (formation.SharesArenaAnchor)
            {
                if (!_director.IsStarted) _director.StartAt(PickArenaPoint(radius));
                warningCenter = _director.AnchorPoint;
                warningRadius += formation.GetArenaSpread(order.Count, settings);
                position = warningCenter + formation.GetArenaOffset(order.Index, settings);
                if (!_world.Map.CanOccupy(position, radius) || (position - _world.Player.Position).Length < _world.Stages.Spawn.MinPlayerDistance) position = warningCenter;
            }
            else
            {
                position = PickArenaPoint(radius);
                warningCenter = position;
            }
            int warningTicks = (int)Math.Round(_world.Stages.Spawn.WarningSeconds * RuneSimulation.TICK_RATE);
            _pendingSpawns.Add(new PendingSpawn(_world.Tick + warningTicks, enemyId, position, elite, isKillTarget, warningCenter, warningRadius));
        }

        /// <summary>
        /// 맵 전체에서 플레이어 거리 띠와 벽, 다른 예고 위치를 피한 스폰 위치를 고른다. 두 번 추첨해도 후보가 없으면 맵 안에서 플레이어로부터 가장 먼 지점을 반환한다.
        /// </summary>
        private SimVector PickArenaPoint(double radius)
        {
            StageSpawnSettings spawn = _world.Stages.Spawn;
            for (int round = 0; round < ARENA_PICK_ROUNDS; round++)
                if (_arenaSpawnPoints.TryPick(_world.Map, _world.Player.Position, radius, spawn.MinPlayerDistance, spawn.MaxPlayerDistance, _isArenaPointReserved, out SimVector point))
                    return point;
            return GetFarthestPoint(_world.Player.Position, radius);
        }

        /// <summary>맵 좌표가 대기 중인 맵형 스폰의 예고 영역(예고 반경 + 여유) 안에 있는지 반환한다.</summary>
        private bool IsArenaPointReserved(SimVector position)
        {
            foreach (PendingSpawn spawn in _pendingSpawns)
            {
                double gap = spawn.WarningRadius * 2;
                if ((spawn.WarningCenter - position).LengthSquared < gap * gap) return true;
            }
            return false;
        }

        /// <summary>
        /// 생성 틱에 도달한 대기 스폰을 등록 순서대로 생성하고 접촉 피해 유예를 준다. 적 수가 상한이라 생성하지 못하면 남은 항목을 다음 틱에 다시 시도한다.
        /// </summary>
        internal void AdvancePendingSpawns()
        {
            for (int i = 0; i < _pendingSpawns.Count;)
            {
                PendingSpawn spawn = _pendingSpawns[i];
                if (spawn.Tick > _world.Tick) { i++; continue; }
                SimulationEnemy enemy = SpawnEnemy(spawn.EnemyId, spawn.Position, false, 0, spawn.Elite, spawn.IsKillTarget);
                if (enemy == null) return;
                enemy.ContactGraceUntil = _world.Time + _world.Stages.Spawn.SpawnGraceSeconds;
                _pendingSpawns.RemoveAt(i);
            }
        }

        /// <summary>
        /// 대기 스폰으로 예고 표시 목록을 다시 만든다. 같은 예고 중심을 쓰는 무리는 하나로 묶고 반경은 가장 큰 값, 진행도는 가장 앞선 값, 엘리트는 하나라도 있으면 표시한다.
        /// </summary>
        internal void RefreshSpawnWarnings()
        {
            _feedback.SpawnWarnings.Clear();
            int warningTicks = (int)Math.Round(_world.Stages.Spawn.WarningSeconds * RuneSimulation.TICK_RATE);
            foreach (PendingSpawn spawn in _pendingSpawns)
            {
                double progress = warningTicks <= 0 ? 1 : Math.Max(0, Math.Min(1, 1 - (spawn.Tick - _world.Tick) / (double)warningTicks));
                int merged = -1;
                for (int i = 0; i < _feedback.SpawnWarnings.Count; i++)
                    if (_feedback.SpawnWarnings[i].Position.X == spawn.WarningCenter.X && _feedback.SpawnWarnings[i].Position.Y == spawn.WarningCenter.Y) { merged = i; break; }
                if (merged < 0) { _feedback.SpawnWarnings.Add(new SpawnWarning(spawn.WarningCenter, spawn.WarningRadius, progress, spawn.Elite != null)); continue; }
                SpawnWarning current = _feedback.SpawnWarnings[merged];
                _feedback.SpawnWarnings[merged] = new SpawnWarning(current.Position, Math.Max(current.Radius, spawn.WarningRadius),
                    Math.Max(current.Progress, progress), current.IsElite || spawn.Elite != null);
            }
        }

        /// <summary>보스 증원 설정으로 보스 주변 원 위에 같은 간격으로 증원을 생성한다. 시작 각도는 보스가 바라보는 방향이며 맵 밖 위치는 원형 슬롯과 같이 제외한다.</summary>
        internal void SpawnReinforcements(SimulationEnemy boss, BossReinforcement reinforcement)
        {
            int count = reinforcement.Count;
            double radius = _world.EnemyCatalog.Get(reinforcement.EnemyId).Radius;
            _spawnSlots.Refresh(_world.Map, boss.Position, boss.Radius + _world.Stages.Spawn.ReinforcementGap, count, Math.Atan2(boss.Facing.Y, boss.Facing.X), radius);
            SpawnFormation formation = SpawnFormations.Get(_world.Formations.Get(reinforcement.Formation).Shape);
            for (int i = 0; i < count; i++)
                SpawnEnemy(reinforcement.EnemyId, ResolvePlacement(formation.Place(i, count, -1, _spawnSlots, default), radius), false, 0);
        }

        /// <summary>
        /// 배치 결과를 마지막으로 Refresh한 슬롯 기준 좌표로 바꾼다. 쓸 슬롯이 없으면 맵 안에서 플레이어로부터 가장 먼 지점을 쓰고,
        /// 호 이동·바깥 오프셋을 반영한 위치가 맵 밖이면 슬롯 위치를 쓰며, 같은 틱에 같은 위치로 나온 적이 있으면 흔들어 겹치지 않게 한다.
        /// </summary>
        private SimVector ResolvePlacement(SpawnPlacement placement, double radius)
        {
            if (!placement.HasSlot) return GetFarthestPoint(_world.Player.Position, radius);
            SimVector position = _spawnSlots.GetPosition(placement.Slot);
            if (placement.Arc != 0)
            {
                SimVector moved = _spawnSlots.GetArcPosition(placement.Slot, placement.Arc, placement.Outward);
                if (_world.Map.CanOccupy(moved, radius)) position = moved;
            }
            else if (placement.Outward > 0)
            {
                SimVector pushed = position + _spawnSlots.GetDirection(placement.Slot) * placement.Outward;
                if (_world.Map.CanOccupy(pushed, radius)) position = pushed;
            }
            return SpreadSameTickSpawn(position, radius);
        }

        /// <summary>이번 틱에 같은 위치로 스폰된 적이 있으면 ±(반경 × 흔들림 배율) 안의 맵 안 위치로 옮겨 반환한다.</summary>
        private SimVector SpreadSameTickSpawn(SimVector position, double radius)
        {
            if (_tickSpawnPositionsTick != _world.Tick) { _tickSpawnPositionsTick = _world.Tick; _tickSpawnPositions.Clear(); }
            bool isTaken = false;
            foreach (SimVector taken in _tickSpawnPositions)
                if ((taken - position).LengthSquared < SAME_SPAWN_DISTANCE * SAME_SPAWN_DISTANCE) { isTaken = true; break; }
            _tickSpawnPositions.Add(position);
            if (!isTaken) return position;
            double range = radius * _world.Stages.Spawn.JitterScale;
            SimVector spread = position + new SimVector((_world.Random.Next() * 2 - 1) * range, (_world.Random.Next() * 2 - 1) * range);
            return _world.Map.CanOccupy(spread, radius) ? spread : position;
        }

        /// <summary>외곽 벽 안쪽으로 반경만큼 들어온 맵 네 모서리 중 기준점에서 가장 먼 곳을 반환한다.</summary>
        private SimVector GetFarthestPoint(SimVector origin, double radius)
        {
            double inset = _world.Map.TileSize + radius;
            SimVector best = origin; double bestDistance = -1;
            for (int corner = 0; corner < 4; corner++)
            {
                var candidate = new SimVector(corner % 2 == 0 ? inset : _world.Map.Width - inset, corner < 2 ? inset : _world.Map.Height - inset);
                double distance = (candidate - origin).LengthSquared;
                if (distance > bestDistance) { best = candidate; bestDistance = distance; }
            }
            return best;
        }

        /// <summary>마지막 이동 입력 방향의 각도를 반환하며 이동한 적이 없으면 조준 방향 각도를 반환한다.</summary>
        private double GetAheadAngle()
        {
            SimVector direction = _world.HasMoveDirection ? _world.MoveDirection : _world.Player.AimDirection;
            return Math.Atan2(direction.Y, direction.X);
        }

        /// <summary>무반격 더미 적을 지정한 위치와 체력으로 생성한다.</summary>
        internal SimulationEnemy SpawnBenchDummy(SimVector position, double hp) => SpawnEnemy("enemy.scout", position, true, hp);

        /// <summary>설정 ID, 위치, 더미 여부, 체력, 엘리트 정의, 목표 집계 여부와 캐리어 소유자 ID로 적을 생성하고 공격 일정을 설정한다. 엘리트는 체력·보상·이동 속도·반경 배율을 적용하며 캐리어 자식의 보상은 0으로 설정한다.</summary>
        internal SimulationEnemy SpawnEnemy(string id, SimVector position, bool isDummy = false, double hp = 0, EliteSpawnDefinition elite = null, bool isKillTarget = false, int carrierOwnerId = 0)
        {
            if (_world.IsMission && _world.Stage != MissionStage.Combat || _world.Enemies.Count >= _world.EnemyLimit) return null;
            EnemyDefinition definition = _world.EnemyCatalog.Get(id);
            double hpMultiplier = _world.IsMission ? _world.Stages.GetHpMultiplier(_world.StageNumber) : 1;
            if (elite != null) hpMultiplier *= elite.HpMultiplier;
            double damageMultiplier = _world.IsMission ? _world.Stages.Growth.GetDamageMultiplier(_world.StageNumber) : 1;
            int reward = _world.IsMission ? _world.Stages.GetReward(id, definition.Reward) : definition.Reward;
            if (elite != null) reward = (int)Math.Round(reward * elite.RewardMultiplier, MidpointRounding.AwayFromZero);
            _world.Bosses.TryGet(id, out BossDefinition boss);
            double radius = definition.Radius * (elite != null ? elite.RadiusMultiplier : 1);
            var enemy = new SimulationEnemy(_world.AllocateEntityId(), definition, EnemyMovements.Resolve(definition.Movement), boss, _world.Map.NearestFree(position, radius), isDummy, hp, _world.Balance.Sim.HitFlashSeconds, hpMultiplier, damageMultiplier, carrierOwnerId == 0 ? reward : 0, elite != null, elite != null ? elite.SpeedMultiplier : 1, elite, isKillTarget, carrierOwnerId);
            enemy.AttackAt = _world.Time + definition.AttackInterval;
            enemy.InitializeBossState(_world.Time);
            if (definition.Attack == EnemyDefinition.ATTACK_SNIPER) enemy.AttackAt = _world.Time;
            if (definition.HasTrait(EnemyDefinition.TRAIT_CARRIER)) enemy.SpawnRemainingSeconds = definition.CarrierInterval;
            if (boss != null && boss.IsGovernorPattern && !boss.IsMiniBoss)
            { BossHazard hazard = boss.GetHazard(2); enemy.ReinforcementAt = _world.Time + boss.Reinforcement.Interval; enemy.HazardAt = _world.Time + hazard.Interval; enemy.HazardRemaining = hazard.Count; }
            enemy.Observe(_world.Time); _world.Enemies.Add(enemy);
            _stats.RecordEnemySpawn(_world, enemy); if (_world.IsMission) _spawnedEnemies++; return enemy;
        }
    }
}
