using System;
using System.Collections.Generic;

namespace RuneCode
{
    internal readonly struct SpawnPlacement
    {
        private readonly int _slot;
        private readonly double _outward;
        private readonly double _arc;
        public int Slot => _slot;
        public double Outward => _outward;

        /// <summary>슬롯에서 슬롯 원을 따라 반시계 방향으로 옮길 호 길이다. 음수면 시계 방향으로 옮긴다.</summary>
        public double Arc => _arc;
        public bool HasSlot => _slot >= 0;
        public static SpawnPlacement None => new SpawnPlacement(-1, 0);

        /// <summary>배치할 슬롯 번호(-1이면 쓸 수 있는 슬롯 없음), 슬롯에서 바깥쪽으로 밀어낼 거리, 원을 따라 옮길 호 길이를 보관한다.</summary>
        public SpawnPlacement(int slot, double outward, double arc = 0) { _slot = slot; _outward = outward; _arc = arc; }
    }

    /// <summary>
    /// 기준점을 둘러싼 원 위의 같은 간격 슬롯과, 현재 기준점에서 맵 안에 들어가는 슬롯 목록을 계산한다.
    /// 시뮬레이션이 하나를 소유하고 배치마다 Refresh로 다시 계산해 재사용한다.
    /// </summary>
    internal sealed class SpawnSlots
    {
        private readonly List<int> _validSlots = new List<int>();
        private readonly List<int> _arcCandidates = new List<int>();
        private readonly List<int> _crowdCandidates = new List<int>();
        private readonly Func<double> _random;
        private readonly IReadOnlyList<SimulationEnemy> _enemies;
        private SimVector _center;
        private double _radius;
        private int _count;
        private double _startAngle;
        private int[] _sectorEnemyCounts = new int[0];
        private int _leastCrowdedSlot;
        private bool _hasLeastCrowdedSlot;

        public int ValidCount => _validSlots.Count;
        public int Count => _count;

        /// <summary>슬롯 무작위 선택에 쓸 시뮬레이션의 결정적 난수 함수와 방향별 적 수 측정에 쓸 시뮬레이션 적 목록을 연결한다.</summary>
        public SpawnSlots(Func<double> random, IReadOnlyList<SimulationEnemy> enemies) { _random = random; _enemies = enemies; }

        /// <summary>
        /// 기준점, 반경, 슬롯 수, 시작 각도로 슬롯을 다시 계산하고 지정 반경의 개체가 맵 안에 들어가는 슬롯만 후보로 남긴다.
        /// 이전 기준으로 고른 적이 가장 적은 방향도 함께 무효로 한다.
        /// </summary>
        public void Refresh(MissionMap map, SimVector center, double radius, int count, double startAngle, double occupantRadius)
        {
            _center = center; _radius = radius; _count = count; _startAngle = startAngle;
            _hasLeastCrowdedSlot = false;
            _validSlots.Clear();
            for (int slot = 0; slot < count; slot++)
                if (map.CanOccupy(GetPosition(slot), occupantRadius)) _validSlots.Add(slot);
        }

        /// <summary>슬롯 번호의 각도(라디안)를 반환한다.</summary>
        public double GetAngle(int slot) => _startAngle + slot * 2 * Math.PI / _count;

        /// <summary>슬롯 번호의 바깥 방향 단위 벡터를 반환한다.</summary>
        public SimVector GetDirection(int slot) { double angle = GetAngle(slot); return new SimVector(Math.Cos(angle), Math.Sin(angle)); }

        /// <summary>슬롯 번호의 위치를 반환한다.</summary>
        public SimVector GetPosition(int slot) => _center + GetDirection(slot) * _radius;

        /// <summary>슬롯에서 원을 따라 호 길이 arc만큼 돌고 반경을 outward만큼 늘린 위치를 반환한다. arc가 양수면 반시계 방향이다.</summary>
        public SimVector GetArcPosition(int slot, double arc, double outward)
        {
            double angle = GetAngle(slot) + arc / _radius;
            return _center + new SimVector(Math.Cos(angle), Math.Sin(angle)) * (_radius + outward);
        }

        /// <summary>슬롯이 맵 안 후보인지 반환한다.</summary>
        public bool IsValid(int slot) => _validSlots.Contains(slot);

        /// <summary>맵 안 후보 중 순서 번호에 해당하는 슬롯을 반환한다.</summary>
        public int GetValid(int index) => _validSlots[index];

        /// <summary>맵 안 후보 중 하나를 난수로 고르며 후보가 없으면 -1을 반환하고 난수를 소비하지 않는다.</summary>
        public int PickRandomValid() => _validSlots.Count == 0 ? -1 : _validSlots[Math.Min(_validSlots.Count - 1, (int)(_random() * _validSlots.Count))];

        /// <summary>지정 슬롯과 각도 차이가 가장 작은 맵 안 후보를 반환하며 후보가 없으면 -1을 반환한다.</summary>
        public int NearestValid(int slot)
        {
            int best = -1; int bestGap = int.MaxValue;
            foreach (int candidate in _validSlots)
            {
                int gap = Math.Abs(candidate - slot); gap = Math.Min(gap, _count - gap);
                if (gap < bestGap) { best = candidate; bestGap = gap; }
            }
            return best;
        }

        /// <summary>범위를 벗어난 슬롯 번호를 원을 따라 0 ~ 슬롯 수-1 범위로 감아 반환한다.</summary>
        public int Wrap(int slot) => (slot % _count + _count) % _count;

        /// <summary>anchor를 가운데로 양쪽 halfSpan 슬롯까지의 원호에서 맵 안 후보 슬롯 수를 반환한다.</summary>
        public int CountValidInArc(int anchor, int halfSpan)
        {
            int total = 0;
            for (int offset = -halfSpan; offset <= halfSpan; offset++) if (IsValid(Wrap(anchor + offset))) total++;
            return total;
        }

        /// <summary>
        /// 양쪽 halfSpan 슬롯 원호에 맵 안 후보가 가장 많은 가운데 슬롯을 고른다. 원호 전체가 맵 안인 슬롯이 있으면 그중에서 고르게 된다.
        /// isNearestToFront가 true면 0번 슬롯과 가장 가까운 것을, false면 난수로 하나를 고른다. 맵 안 후보가 없으면 -1을 반환하고 난수를 소비하지 않는다.
        /// </summary>
        public int PickArcAnchor(int halfSpan, bool isNearestToFront)
        {
            _arcCandidates.Clear();
            int bestCount = 0;
            for (int slot = 0; slot < _count; slot++)
            {
                int valid = CountValidInArc(slot, halfSpan);
                if (valid == 0 || valid < bestCount) continue;
                if (valid > bestCount) { bestCount = valid; _arcCandidates.Clear(); }
                _arcCandidates.Add(slot);
            }
            if (_arcCandidates.Count == 0) return -1;
            if (!isNearestToFront) return _arcCandidates[Math.Min(_arcCandidates.Count - 1, (int)(_random() * _arcCandidates.Count))];
            int best = _arcCandidates[0]; int bestGap = int.MaxValue;
            foreach (int candidate in _arcCandidates)
            {
                int gap = Math.Min(candidate, _count - candidate);
                if (gap < bestGap) { best = candidate; bestGap = gap; }
            }
            return best;
        }

        /// <summary>
        /// 각 슬롯 방향을 가운데로 슬롯 간격만큼 벌어진 부채꼴에서 기준점으로부터 반경 이내의 살아 있는 적(더미 제외) 수를 세고,
        /// 맵 안 후보 중 적이 가장 적은 슬롯을 반환한다. 같은 수가 여럿이면 난수로 고르며 맵 안 후보가 없으면 -1을 반환한다.
        /// 결과는 다음 Refresh까지 유지하므로 같은 배치 묶음에서 여러 번 불러도 방향과 난수 소비가 바뀌지 않는다.
        /// </summary>
        public int PickLeastCrowdedSlot()
        {
            if (_hasLeastCrowdedSlot) return _leastCrowdedSlot;
            _hasLeastCrowdedSlot = true;
            _leastCrowdedSlot = -1;
            if (_validSlots.Count == 0) return _leastCrowdedSlot;
            if (_sectorEnemyCounts.Length < _count) _sectorEnemyCounts = new int[_count];
            SpawnCrowding.CountBySector(_enemies, _center, _radius, _startAngle, _count, _sectorEnemyCounts);
            int fewest = int.MaxValue;
            _crowdCandidates.Clear();
            foreach (int slot in _validSlots)
            {
                int enemies = _sectorEnemyCounts[slot];
                if (enemies > fewest) continue;
                if (enemies < fewest) { fewest = enemies; _crowdCandidates.Clear(); }
                _crowdCandidates.Add(slot);
            }
            _leastCrowdedSlot = _crowdCandidates[Math.Min(_crowdCandidates.Count - 1, (int)(_random() * _crowdCandidates.Count))];
            return _leastCrowdedSlot;
        }

        /// <summary>anchor를 가운데로 양쪽 halfSpan 슬롯 원호 안의 맵 안 후보 중 slot과 각도 차이가 가장 작은 슬롯을 반환하며 없으면 -1을 반환한다.</summary>
        public int NearestValidInArc(int slot, int anchor, int halfSpan)
        {
            int best = -1; int bestGap = int.MaxValue;
            for (int offset = -halfSpan; offset <= halfSpan; offset++)
            {
                int candidate = Wrap(anchor + offset);
                if (!IsValid(candidate)) continue;
                int gap = Math.Abs(candidate - slot); gap = Math.Min(gap, _count - gap);
                if (gap < bestGap) { best = candidate; bestGap = gap; }
            }
            return best;
        }
    }

    /// <summary>기준점 주변 방향별 적 밀집도를 계산하는 공용 규칙이다. 원형 슬롯 진형과 맵형 스폰 위치 선택이 함께 쓴다.</summary>
    internal static class SpawnCrowding
    {
        /// <summary>
        /// 기준점에서 startAngle을 0번 방향으로 같은 각도로 나눈 sectorCount개 부채꼴(각 방향이 가운데)에 대해,
        /// 반경 radius 이내의 살아 있는 적(더미 제외) 수를 counts 앞쪽 sectorCount칸에 기록한다. counts는 sectorCount 이상 길이여야 한다.
        /// </summary>
        public static void CountBySector(IReadOnlyList<SimulationEnemy> enemies, SimVector center, double radius, double startAngle, int sectorCount, int[] counts)
        {
            Array.Clear(counts, 0, sectorCount);
            foreach (SimulationEnemy enemy in enemies)
            {
                if (!enemy.IsAlive || enemy.IsDummy) continue;
                SimVector offset = enemy.Position - center;
                if (offset.LengthSquared > radius * radius) continue;
                counts[SectorOf(offset, startAngle, sectorCount)]++;
            }
        }

        /// <summary>기준점에서 offset 방향이 속한 부채꼴 번호(0 ~ sectorCount-1)를 반환한다. 방향이 부채꼴 가운데가 되도록 반 칸을 더해 내림한다.</summary>
        public static int SectorOf(SimVector offset, double startAngle, int sectorCount)
        {
            int sector = (int)Math.Floor((Math.Atan2(offset.Y, offset.X) - startAngle) / (2 * Math.PI / sectorCount) + 0.5);
            return (sector % sectorCount + sectorCount) % sectorCount;
        }
    }

    /// <summary>
    /// 맵형 스폰 위치를 고른다. 맵 바닥 전체에서 후보를 뽑아 플레이어 거리 띠, 벽, 다른 예고 위치와의 겹침으로 거르고,
    /// 남은 후보 중 플레이어 기준 고정 방향별 적 수가 가장 적은 방향의 후보를 고른다. 시뮬레이션이 하나를 소유해 재사용한다.
    /// </summary>
    internal sealed class ArenaSpawnPoints
    {
        private const int SAMPLE_COUNT = 6;
        private const int SECTOR_COUNT = 8;

        private readonly Func<double> _random;
        private readonly IReadOnlyList<SimulationEnemy> _enemies;
        private readonly List<SimVector> _candidates = new List<SimVector>();
        private readonly List<SimVector> _bestCandidates = new List<SimVector>();
        private readonly int[] _sectorEnemyCounts = new int[SECTOR_COUNT];

        /// <summary>후보 추첨에 쓸 시뮬레이션의 결정적 난수 함수와 방향별 적 수 측정에 쓸 적 목록을 연결한다.</summary>
        public ArenaSpawnPoints(Func<double> random, IReadOnlyList<SimulationEnemy> enemies) { _random = random; _enemies = enemies; }

        /// <summary>
        /// 외곽 벽 두께와 occupantRadius만큼 안쪽 사각형에서 후보를 SAMPLE_COUNT개 뽑아, 플레이어 거리가 minDistance 미만이거나 maxDistance(0이면 제한 없음) 초과인 것,
        /// 맵 안에 들어가지 않는 것, isReserved가 true인 것을 거른다. 남은 후보 중 적이 가장 적은 방향의 후보를 난수로 하나 골라 point로 내보내고 true를 반환하며,
        /// 남은 후보가 없으면 false를 반환한다. 방향별 적 수는 maxDistance(0이면 맵 전체) 이내만 센다.
        /// </summary>
        public bool TryPick(MissionMap map, SimVector player, double occupantRadius, double minDistance, double maxDistance,
            Func<SimVector, bool> isReserved, out SimVector point)
        {
            point = player;
            double inset = map.TileSize + occupantRadius;
            double width = map.Width - inset * 2; double height = map.Height - inset * 2;
            if (width <= 0 || height <= 0) return false;
            _candidates.Clear();
            for (int i = 0; i < SAMPLE_COUNT; i++)
            {
                var candidate = new SimVector(inset + _random() * width, inset + _random() * height);
                double distance = (candidate - player).Length;
                if (distance < minDistance || (maxDistance > 0 && distance > maxDistance)) continue;
                if (!map.CanOccupy(candidate, occupantRadius) || isReserved(candidate)) continue;
                _candidates.Add(candidate);
            }
            if (_candidates.Count == 0) return false;
            double countRadius = maxDistance > 0 ? maxDistance : map.Width + map.Height;
            SpawnCrowding.CountBySector(_enemies, player, countRadius, 0, SECTOR_COUNT, _sectorEnemyCounts);
            int fewest = int.MaxValue;
            _bestCandidates.Clear();
            foreach (SimVector candidate in _candidates)
            {
                int enemies = _sectorEnemyCounts[SpawnCrowding.SectorOf(candidate - player, 0, SECTOR_COUNT)];
                if (enemies > fewest) continue;
                if (enemies < fewest) { fewest = enemies; _bestCandidates.Clear(); }
                _bestCandidates.Add(candidate);
            }
            point = _bestCandidates[Math.Min(_bestCandidates.Count - 1, (int)(_random() * _bestCandidates.Count))];
            return true;
        }
    }

    /// <summary>진형 모양 하나의 배치 규칙이다. 상태 없는 공유 인스턴스이며 값만 반환한다.</summary>
    internal abstract class SpawnFormation
    {
        /// <summary>원형 슬롯 대신 맵 전체에서 위치를 고르고 예고를 거쳐 생성하는 맵형 진형인지 반환한다. 맵형은 ChooseAnchor·Place를 쓰지 않는다.</summary>
        public virtual bool IsArena => false;

        /// <summary>맵형 진형에서 묶음 전체가 하나의 무리 중심을 공유하는지 반환한다. false면 기체마다 위치를 따로 고른다.</summary>
        public virtual bool SharesArenaAnchor => false;

        /// <summary>무리 중심을 공유하는 맵형 진형에서 index번째 기체의 무리 중심 기준 오프셋을 반환한다.</summary>
        public virtual SimVector GetArenaOffset(int index, in FormationSettings settings) => SimVector.Zero;

        /// <summary>무리 중심을 공유하는 맵형 진형에서 count기 무리의 표시 반경(무리 중심에서 가장 먼 기체까지 거리)을 반환한다.</summary>
        public virtual double GetArenaSpread(int count, in FormationSettings settings) => 0;

        /// <summary>웨이브 시작 시 각도 방식에 맞춰 기준 슬롯을 하나 고른다. 기준 슬롯을 쓰지 않는 모양은 -1을 반환한다.</summary>
        public abstract int ChooseAnchor(SpawnSlots slots, in FormationSettings settings);

        /// <summary>웨이브의 index번째(전체 count기) 기체가 나올 슬롯과 바깥 오프셋을 반환한다. 맵 안 슬롯이 없으면 None을 반환한다.</summary>
        public abstract SpawnPlacement Place(int index, int count, int anchor, SpawnSlots slots, in FormationSettings settings);
    }

    internal sealed class RingRandomSlotFormation : SpawnFormation
    {
        /// <summary>기준 슬롯을 쓰지 않으므로 -1을 반환한다.</summary>
        public override int ChooseAnchor(SpawnSlots slots, in FormationSettings settings) => -1;

        /// <summary>맵 안 슬롯 중 하나를 무작위로 고른다(흩뿌림).</summary>
        public override SpawnPlacement Place(int index, int count, int anchor, SpawnSlots slots, in FormationSettings settings)
        {
            int slot = slots.PickRandomValid();
            return slot < 0 ? SpawnPlacement.None : new SpawnPlacement(slot, 0);
        }
    }

    internal sealed class RingAllSlotsFormation : SpawnFormation
    {
        /// <summary>기준 슬롯을 쓰지 않으므로 -1을 반환한다.</summary>
        public override int ChooseAnchor(SpawnSlots slots, in FormationSettings settings) => -1;

        /// <summary>맵 안 슬롯 전체에 기체를 고르게 나눈다(포위). 기체가 슬롯보다 많으면 다시 처음 슬롯부터 채운다.</summary>
        public override SpawnPlacement Place(int index, int count, int anchor, SpawnSlots slots, in FormationSettings settings)
        {
            int valid = slots.ValidCount;
            if (valid == 0) return SpawnPlacement.None;
            int order = count <= valid ? (int)((long)index * valid / count) : index % valid;
            return new SpawnPlacement(slots.GetValid(order), 0);
        }
    }

    internal sealed class RingDoubleFormation : SpawnFormation
    {
        /// <summary>기준 슬롯을 쓰지 않으므로 -1을 반환한다.</summary>
        public override int ChooseAnchor(SpawnSlots slots, in FormationSettings settings) => -1;

        /// <summary>
        /// 기체를 안쪽 원(앞 절반, 홀수면 1기 더)과 바깥 원으로 나눈다(이중 포위). 안쪽 원은 짝수 슬롯, 바깥 원은 홀수 슬롯에 고르게
        /// 나눠 엇갈리게 두고, 바깥 원은 간격만큼 바깥쪽으로 민다. 해당 짝수·홀수 슬롯이 맵 안에 없으면 맵 안 슬롯 전체를 쓴다.
        /// </summary>
        public override SpawnPlacement Place(int index, int count, int anchor, SpawnSlots slots, in FormationSettings settings)
        {
            if (slots.ValidCount == 0) return SpawnPlacement.None;
            int innerCount = (count + 1) / 2;
            bool isOuter = index >= innerCount;
            int ringIndex = isOuter ? index - innerCount : index;
            int ringCount = isOuter ? count - innerCount : innerCount;
            int parity = isOuter ? 1 : 0;
            int matching = CountParity(slots, parity);
            int slot;
            if (matching == 0)
            {
                int valid = slots.ValidCount;
                slot = slots.GetValid(ringCount <= valid ? (int)((long)ringIndex * valid / ringCount) : ringIndex % valid);
            }
            else slot = GetParitySlot(slots, parity, ringCount <= matching ? (int)((long)ringIndex * matching / ringCount) : ringIndex % matching);
            return new SpawnPlacement(slot, isOuter ? settings.Spacing : 0);
        }

        /// <summary>맵 안 슬롯 중 번호의 짝수·홀수가 parity와 같은 슬롯 수를 반환한다.</summary>
        private static int CountParity(SpawnSlots slots, int parity)
        {
            int total = 0;
            for (int i = 0; i < slots.ValidCount; i++) if (slots.GetValid(i) % 2 == parity) total++;
            return total;
        }

        /// <summary>맵 안 슬롯 중 번호의 짝수·홀수가 parity와 같은 order번째 슬롯을 반환한다. 호출부가 order를 개수보다 작게 맞춘다.</summary>
        private static int GetParitySlot(SpawnSlots slots, int parity, int order)
        {
            for (int i = 0; i < slots.ValidCount; i++)
            {
                int slot = slots.GetValid(i);
                if (slot % 2 != parity) continue;
                if (order-- == 0) return slot;
            }
            return slots.GetValid(0);
        }
    }

    internal sealed class PointSequenceFormation : SpawnFormation
    {
        /// <summary>
        /// 무작위 각도면 맵 안 슬롯 중 하나를, 진행 방향 각도면 진행 방향에 맞춘 0번 슬롯(맵 밖이면 가장 가까운 후보)을 기준으로 고른다.
        /// 진행 방향 각도에서는 시뮬레이션이 시작 각도를 진행 방향으로 맞춰 둔다.
        /// </summary>
        public override int ChooseAnchor(SpawnSlots slots, in FormationSettings settings)
            => settings.Angle == SpawnFormations.ANGLE_AHEAD ? slots.NearestValid(0) : slots.PickRandomValid();

        /// <summary>기준 슬롯(맵 밖이 되었으면 가장 가까운 후보)에서 순서마다 간격만큼 바깥쪽으로 물러난 위치를 반환한다(일렬).</summary>
        public override SpawnPlacement Place(int index, int count, int anchor, SpawnSlots slots, in FormationSettings settings)
        {
            int slot = anchor >= 0 && slots.IsValid(anchor) ? anchor : slots.NearestValid(Math.Max(0, anchor));
            return slot < 0 ? SpawnPlacement.None : new SpawnPlacement(slot, index * settings.Spacing);
        }
    }

    internal sealed class RingHalfFormation : SpawnFormation
    {
        /// <summary>
        /// 원 둘레 4분의 1 슬롯 수를 양쪽 폭으로 하는 반원의 가운데 슬롯을 고른다. 반원 전체가 맵 안인 각도를 우선하고 없으면 맵 안 슬롯이 가장 많은 각도를 쓴다.
        /// 무작위 각도면 후보 중 난수로, 진행 방향 각도면 진행 방향에 맞춘 0번 슬롯과 가장 가까운 후보를 고른다.
        /// </summary>
        public override int ChooseAnchor(SpawnSlots slots, in FormationSettings settings)
            => slots.PickArcAnchor(slots.Count / SpawnFormations.HALF_RING_SLOT_STEP, settings.Angle == SpawnFormations.ANGLE_AHEAD);

        /// <summary>
        /// 기준 슬롯을 가운데로 한 반원 위에 양 끝을 포함해 기체를 고르게 나눈다(반원). 반원 지점 수를 넘는 기체는 다음 줄로 넘기고 간격이 있으면 줄마다 그만큼 바깥으로 민다.
        /// 기준 슬롯이 없으면(보스 증원 등) 난수 없이 0번 슬롯 쪽 반원을 고르고, 배정 슬롯이 맵 밖이면 반원 안, 그다음 원 전체에서 가장 가까운 후보를 쓴다.
        /// </summary>
        public override SpawnPlacement Place(int index, int count, int anchor, SpawnSlots slots, in FormationSettings settings)
        {
            if (slots.ValidCount == 0) return SpawnPlacement.None;
            int halfSpan = slots.Count / SpawnFormations.HALF_RING_SLOT_STEP;
            if (anchor < 0) anchor = slots.PickArcAnchor(halfSpan, true);
            int arcSize = halfSpan * 2 + 1;
            int row = index / arcSize;
            int rowIndex = index % arcSize;
            int rowCount = Math.Min(arcSize, count - row * arcSize);
            // 가운데 기준 오프셋을 0에서 먼 쪽으로 반올림해 양쪽 배치가 대칭이 되게 한다.
            int offset = rowCount <= 1 ? 0
                : (int)Math.Round((double)rowIndex * (arcSize - 1) / (rowCount - 1) - halfSpan, MidpointRounding.AwayFromZero);
            int target = slots.Wrap(anchor + offset);
            int slot = slots.IsValid(target) ? target : slots.NearestValidInArc(target, anchor, halfSpan);
            if (slot < 0) slot = slots.NearestValid(target);
            return new SpawnPlacement(slot, settings.Spacing > 0 ? row * settings.Spacing : 0);
        }
    }

    internal sealed class RingSectorBlockFormation : SpawnFormation
    {
        /// <summary>슬롯 방향별 부채꼴 안의 적 수를 세어 맵 안 방향 중 적이 가장 적은 슬롯을 기준으로 고른다. 같은 수면 난수로 고른다.</summary>
        public override int ChooseAnchor(SpawnSlots slots, in FormationSettings settings) => slots.PickLeastCrowdedSlot();

        /// <summary>
        /// 기준 슬롯 위치를 가운데로, 기준점을 중심으로 하는 슬롯 원을 따라 기체를 호 길이 간격만큼 나란히 놓는다(방향 차단).
        /// 기준 슬롯이 없으면(보스 증원 등) 이번 슬롯 기준으로 적이 가장 적은 방향을 고르고, 기준 슬롯이 맵 밖이 되었으면 가장 가까운 후보를 쓴다.
        /// </summary>
        public override SpawnPlacement Place(int index, int count, int anchor, SpawnSlots slots, in FormationSettings settings)
        {
            if (slots.ValidCount == 0) return SpawnPlacement.None;
            if (anchor < 0) anchor = slots.PickLeastCrowdedSlot();
            int slot = slots.IsValid(anchor) ? anchor : slots.NearestValid(anchor);
            return new SpawnPlacement(slot, 0, (index - (count - 1) * 0.5) * settings.Spacing);
        }
    }

    internal sealed class ArenaRandomFormation : SpawnFormation
    {
        public override bool IsArena => true;

        /// <summary>맵형 진형은 원형 슬롯 기준을 쓰지 않으므로 -1을 반환한다.</summary>
        public override int ChooseAnchor(SpawnSlots slots, in FormationSettings settings) => -1;

        /// <summary>맵형 진형은 원형 슬롯 배치를 쓰지 않으므로 None을 반환한다. 위치는 시뮬레이션이 기체마다 맵 전체에서 고른다(흩뿌림 매복).</summary>
        public override SpawnPlacement Place(int index, int count, int anchor, SpawnSlots slots, in FormationSettings settings) => SpawnPlacement.None;
    }

    internal sealed class ArenaClusterFormation : SpawnFormation
    {
        // 해바라기 배치에 쓰는 황금각 π(3 - √5)이다.
        private const double GOLDEN_ANGLE = 2.399963229728653;

        public override bool IsArena => true;
        public override bool SharesArenaAnchor => true;

        /// <summary>index번째 기체를 무리 중심에서 간격 × √index 거리, index × 황금각 방향에 두는 해바라기 배치 오프셋을 반환한다(무리).</summary>
        public override SimVector GetArenaOffset(int index, in FormationSettings settings)
        {
            double distance = settings.Spacing * Math.Sqrt(index);
            double angle = index * GOLDEN_ANGLE;
            return new SimVector(Math.Cos(angle), Math.Sin(angle)) * distance;
        }

        /// <summary>count기 해바라기 배치에서 무리 중심으로부터 가장 먼 기체까지의 거리(간격 × √(count-1))를 반환한다.</summary>
        public override double GetArenaSpread(int count, in FormationSettings settings) => settings.Spacing * Math.Sqrt(Math.Max(0, count - 1));

        /// <summary>맵형 진형은 원형 슬롯 기준을 쓰지 않으므로 -1을 반환한다.</summary>
        public override int ChooseAnchor(SpawnSlots slots, in FormationSettings settings) => -1;

        /// <summary>맵형 진형은 원형 슬롯 배치를 쓰지 않으므로 None을 반환한다. 위치는 시뮬레이션이 무리 중심과 GetArenaOffset으로 정한다.</summary>
        public override SpawnPlacement Place(int index, int count, int anchor, SpawnSlots slots, in FormationSettings settings) => SpawnPlacement.None;
    }

    internal static class SpawnFormations
    {
        public const string ANGLE_RANDOM = "random";
        public const string ANGLE_AHEAD = "ahead";
        public const string ANGLE_FIXED = "fixed";
        public const string SHAPE_RING_HALF = "ringHalf";

        // 반원 진형은 원 둘레의 4분의 1 슬롯을 양쪽 폭으로 쓰므로 슬롯 수가 이 값의 배수여야 정확히 180도가 된다.
        public const int HALF_RING_SLOT_STEP = 4;

        private static readonly Dictionary<string, SpawnFormation> _formations = new Dictionary<string, SpawnFormation>
        {
            { "ringRandomSlot", new RingRandomSlotFormation() },
            { "ringAllSlots", new RingAllSlotsFormation() },
            { "ringDouble", new RingDoubleFormation() },
            { "pointSequence", new PointSequenceFormation() },
            { SHAPE_RING_HALF, new RingHalfFormation() },
            { "ringSectorBlock", new RingSectorBlockFormation() },
            { "arenaRandom", new ArenaRandomFormation() },
            { "arenaCluster", new ArenaClusterFormation() }
        };

        /// <summary>진형 모양 이름이 구현되어 있는지 반환한다.</summary>
        public static bool IsKnownShape(string shape) => shape != null && _formations.ContainsKey(shape);

        /// <summary>진형 모양이 실제 슬롯 수 slots로 배치할 수 있는지 반환한다. 반원 진형만 HALF_RING_SLOT_STEP의 배수를 요구한다.</summary>
        public static bool IsValidSlotCount(string shape, int slots) => shape != SHAPE_RING_HALF || slots % HALF_RING_SLOT_STEP == 0;

        /// <summary>진형 모양 이름에 해당하는 공유 배치 규칙을 반환한다.</summary>
        public static SpawnFormation Get(string shape) => _formations[shape];
    }
}
