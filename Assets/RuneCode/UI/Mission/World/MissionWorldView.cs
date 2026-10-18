using System;
using System.Collections.Generic;

using UnityEngine;

using UnityEngine.InputSystem;

namespace RuneCode
{
    /// <summary>
    /// 미션 시뮬레이션을 월드 오브젝트로 표시한다. 전투 시작 시 맵(벽·격자선)을 한 번 만들고,
    /// 매 프레임 플레이어·적·마법·적 탄·오브·피해 숫자 뷰를 시뮬레이션 목록 순서대로 재사용해 갱신한다.
    /// 피해가 생기면 카메라 흔들림과 짧은 갱신 정지(히트 스톱)를 설정에 따라 적용한다.
    /// </summary>
    public sealed class MissionWorldView : MonoBehaviour
    {
        private const float SHAKE_SECONDS = 0.08f;
        private const float HIT_STOP_SECONDS = 0.025f;
        // 히트 스톱 시작 후 이 시간 동안은 다시 시작하지 않는다. 폭발 확장처럼 피해가 연속될 때 화면이 계속 멈추지 않게 한다.
        private const float HIT_STOP_COOLDOWN_SECONDS = 0.15f;
        private const double DAMAGE_EPSILON = 0.000001;
        private const int MAX_DAMAGE_NUMBERS = 64;
        private const int DAMAGE_NUMBER_TICKS = 40;
        private const int MAX_HEAL_PULSES = 32;
        private const int HEAL_PULSE_TICKS = 40;
        private const int HEAL_PULSE_ORDER = 70;
        private const int MAX_ARC_LINKS = 32;
        private const float WALL_EDGE_WIDTH = 1f;
        private const float GRID_LINE_WIDTH = 1f;
        private const int GRID_ORDER = 0;
        private const int WALL_ORDER = 1;
        private const int WALL_EDGE_ORDER = 2;
        private const int RANGE_SEGMENTS = 96;
        private const float RANGE_LINE_WIDTH = 1.8f;
        private const int RANGE_ORDER = 65;
        private const int DEFEAT_FRAGMENT_COUNT = 24;
        private const int CLOCK_SEGMENTS = 40;

        private static readonly Color WallColor = new Color(0.13f, 0.20f, 0.27f);
        private static readonly Color WallEdgeColor = new Color(0.23f, 0.40f, 0.47f);
        private static readonly Color GridColor = new Color(0.15f, 0.22f, 0.29f);
        private static readonly Color HealPulseColor = new Color(0.45f, 0.95f, 0.55f);

        [Header("연결")]
        [SerializeField] private MissionCamera _camera;

        [Header("Prefab")]
        [SerializeField] private PlayerView _playerPrefab;
        [SerializeField] private EnemyView _enemyPrefab;
        [SerializeField] private SpellEntityView _spellPrefab;
        [SerializeField] private HostileProjectileView _projectilePrefab;
        [SerializeField] private OrbView _orbPrefab;
        [SerializeField] private ItemDropView _itemDropPrefab;
        [SerializeField] private DamageNumberView _damageNumberPrefab;
        [SerializeField] private ArcLinkView _arcLinkPrefab;

        [Header("맵")]
        [SerializeField] private Sprite _squareSprite;
        [SerializeField] private Material _spriteMaterial;
        [SerializeField] private float _gridSpacing = 64f;

        [Header("종료 연출")]
        [SerializeField, Min(0.1f)] private float _victoryEndingSeconds = 1.2f;
        [SerializeField, Min(0.1f)] private float _defeatEndingSeconds = 1.2f;
        [SerializeField, Min(0.1f)] private float _timeoutClockSeconds = 0.65f;

        private readonly List<EnemyView> _enemyViews = new List<EnemyView>();
        private readonly Dictionary<string, List<EnemyView>> _additionalEnemyViews = new Dictionary<string, List<EnemyView>>();
        private readonly Dictionary<string, EnemyView> _additionalEnemyPrefabs = new Dictionary<string, EnemyView>();
        private readonly Dictionary<string, int> _additionalEnemyCounts = new Dictionary<string, int>();
        private readonly List<SpellEntityView> _spellViews = new List<SpellEntityView>();
        private readonly List<SpellEntityView> _beamFlashViews = new List<SpellEntityView>();
        private readonly BeamFlashPresentation _beamFlashPresentation = new BeamFlashPresentation();
        private readonly List<HostileProjectileView> _projectileViews = new List<HostileProjectileView>();
        private readonly List<OrbView> _orbViews = new List<OrbView>();
        private readonly List<ItemDropView> _itemDropViews = new List<ItemDropView>();
        private readonly List<DamageNumberView> _damageNumberViews = new List<DamageNumberView>();
        private readonly List<HealPulseView> _healPulseViews = new List<HealPulseView>();
        private readonly List<ArcLinkView> _arcLinkViews = new List<ArcLinkView>();
        private readonly List<SpriteRenderer> _rangeLines = new List<SpriteRenderer>();
        private readonly List<SpellEntityView> _rangePreviews = new List<SpellEntityView>();
        private Func<RuneSimulation> _simulation;
        private Func<bool> _isShakeEnabled;
        private Func<bool> _isHitStopEnabled;
        private RuneSimulation _observedSimulation;
        private Transform _mapRoot;
        private PlayerView _playerView;
        private double _lastDamage;
        private double _lastStatusDamage;
        private double _lastPersistDamage;
        private double _lastHp;
        private float _hitStopUntil;
        private float _hitStopReadyAt;
        private readonly List<SpriteRenderer> _endingFragments = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _clockLines = new List<SpriteRenderer>();
        private float _endingElapsed;
        private bool _hasRenderedEnding;

        /// <summary>월드를 따라가는 미션 카메라다. 화면 View가 조준 좌표 변환에 쓴다.</summary>
        public MissionCamera Camera => _camera;

        /// <summary>표시할 시뮬레이션 조회 함수와 화면 흔들림·히트 스톱 설정 조회 함수를 연결한다.</summary>
        public void Initialize(Func<RuneSimulation> simulation, Func<bool> isShakeEnabled, Func<bool> isHitStopEnabled)
        {
            _simulation = simulation;
            _isShakeEnabled = isShakeEnabled;
            _isHitStopEnabled = isHitStopEnabled;
        }

        /// <summary>시뮬레이션을 진행하지 않고 종료 연출 시간을 늘리며 승리·사망·시간 초과별 완료 여부를 반환한다.</summary>
        public bool AdvanceEnding(RuneSimulation sim, float deltaTime)
        {
            if (!sim.Completed && !sim.IsFailed) return true;
            if (_observedSimulation != sim) BeginSimulation(sim);
            _endingElapsed += Mathf.Max(0, deltaTime);
            float duration = sim.Completed ? _victoryEndingSeconds
                : _defeatEndingSeconds + (sim.IsTimedOut ? _timeoutClockSeconds : 0);
            return _endingElapsed >= duration;
        }

        void LateUpdate()
        {
            RuneSimulation sim = _simulation?.Invoke();
            if (sim == null)
            {
                HideFrom(_beamFlashViews, 0);
                HideFrom(_rangeLines, 0);
                HideFrom(_rangePreviews, 0);
                return;
            }
            if (_observedSimulation != sim) BeginSimulation(sim);
            _beamFlashPresentation.Update(sim, Time.unscaledTime);
            if (_hasRenderedEnding)
            {
                RenderEnding(sim);
                return;
            }
            UpdateRangePreview(sim);
            DetectImpact(sim);
            bool isHitStop = !sim.Completed && !sim.IsFailed && _hitStopUntil > Time.unscaledTime && (_isHitStopEnabled?.Invoke() ?? false);
            RenderBeamFlashes(sim, isHitStop);
            if (isHitStop) return;
            SimVector origin = sim.MapCenter;
            if (_playerView == null) _playerView = Instantiate(_playerPrefab, transform);
            _playerView.Apply(sim.Player, origin);
            int count = 0;
            foreach (string id in _additionalEnemyPrefabs.Keys) _additionalEnemyCounts[id] = 0;
            foreach (SimulationEnemy enemy in sim.Enemies)
            {
                if (_additionalEnemyPrefabs.TryGetValue(enemy.Kind, out EnemyView prefab))
                {
                    int index = _additionalEnemyCounts[enemy.Kind]++;
                    GetView(_additionalEnemyViews[enemy.Kind], prefab, index).Apply(enemy, sim, origin);
                }
                else GetView(_enemyViews, _enemyPrefab, count++).Apply(enemy, sim, origin);
            }
            HideFrom(_enemyViews, count);
            foreach (string id in _additionalEnemyPrefabs.Keys) HideFrom(_additionalEnemyViews[id], _additionalEnemyCounts[id]);
            count = 0;
            foreach (SimulationSpellEntity spell in sim.SpellEntities) GetView(_spellViews, _spellPrefab, count++).Apply(spell, origin);
            HideFrom(_spellViews, count);
            count = 0;
            foreach (SimulationProjectile projectile in sim.EnemyProjectiles) GetView(_projectileViews, _projectilePrefab, count++).Apply(projectile, origin);
            HideFrom(_projectileViews, count);
            count = 0;
            foreach (FragmentOrb orb in sim.Orbs) GetView(_orbViews, _orbPrefab, count++).Apply(orb, origin);
            HideFrom(_orbViews, count);
            count = 0;
            foreach (SimulationItemDrop drop in sim.ItemDrops) GetView(_itemDropViews, _itemDropPrefab, count++).Apply(drop, sim.Tick - drop.SpawnTick, origin);
            HideFrom(_itemDropViews, count);
            count = 0;
            foreach (DamageNumber number in sim.DamageNumbers)
            {
                int elapsed = sim.Tick - number.Tick;
                if (elapsed > DAMAGE_NUMBER_TICKS || count >= MAX_DAMAGE_NUMBERS) continue;
                GetView(_damageNumberViews, _damageNumberPrefab, count++).Apply(number, elapsed, origin);
            }
            HideFrom(_damageNumberViews, count);
            count = 0;
            foreach (HealPulse pulse in sim.HealPulses)
            {
                int elapsed = sim.Tick - pulse.Tick;
                if (elapsed > HEAL_PULSE_TICKS || count >= MAX_HEAL_PULSES) continue;
                GetHealPulseView(count++).Apply(pulse, elapsed, origin);
            }
            HideFrom(_healPulseViews, count);
            count = 0;
            foreach (ArcLink link in sim.ArcLinks)
            {
                if (count >= MAX_ARC_LINKS) break;
                GetView(_arcLinkViews, _arcLinkPrefab, count++).Apply(link, sim.Tick - link.Tick, origin);
            }
            HideFrom(_arcLinkViews, count);
            if (sim.Completed || sim.IsFailed)
            {
                _hasRenderedEnding = true;
                RenderEnding(sim);
            }
        }

        /// <summary>히트 스톱 중이거나 실제 개체가 소멸한 최초 빔의 표시 기록을 갱신하고 나머지 뷰를 숨긴다.</summary>
        private void RenderBeamFlashes(RuneSimulation sim, bool isHitStop)
        {
            int count = 0;
            foreach (SimulationBeamFlash flash in _beamFlashPresentation.Flashes)
            {
                if (!isHitStop && BeamFlashPresentation.HasLiveBeam(sim, flash.EntityId)) continue;
                GetView(_beamFlashViews, _spellPrefab, count++).ApplyBeamFlash(flash, sim.MapCenter);
            }
            HideFrom(_beamFlashViews, count);
        }

        /// <summary>정지된 전장 위에서 승리 드롭 흡수 또는 패배 시계·붉은 몸체·파편만 표시한다. 보상과 전투 상태는 변경하지 않는다.</summary>
        private void RenderEnding(RuneSimulation sim)
        {
            HideFrom(_beamFlashViews, 0);
            HideFrom(_rangeLines, 0);
            HideFrom(_rangePreviews, 0);
            HideFrom(_spellViews, 0);
            HideFrom(_projectileViews, 0);
            HideFrom(_damageNumberViews, 0);
            HideFrom(_healPulseViews, 0);
            HideFrom(_arcLinkViews, 0);
            if (sim.Completed)
            {
                float progress = Mathf.Clamp01(_endingElapsed / _victoryEndingSeconds);
                double blend = progress * progress;
                int count = 0;
                foreach (FragmentOrb orb in sim.ClearOrbs)
                {
                    OrbView view = GetView(_orbViews, _orbPrefab, count++);
                    view.Apply(orb, sim.MapCenter);
                    view.transform.localPosition = MissionWorldSpace.ToWorld(orb.Position + (sim.Player.Position - orb.Position) * blend, sim.MapCenter);
                    view.transform.localScale = Vector3.one;
                    MissionWorldSpace.SetVisible(view, progress < 1f);
                }
                HideFrom(_orbViews, count);
                count = 0;
                foreach (SimulationItemDrop drop in sim.ClearItemDrops)
                {
                    ItemDropView view = GetView(_itemDropViews, _itemDropPrefab, count++);
                    view.Apply(drop, sim.Tick - drop.SpawnTick, sim.MapCenter);
                    view.transform.localPosition = MissionWorldSpace.ToWorld(drop.Position + (sim.Player.Position - drop.Position) * blend, sim.MapCenter);
                    view.transform.localScale = Vector3.one;
                    MissionWorldSpace.SetVisible(view, progress < 1f);
                }
                HideFrom(_itemDropViews, count);
                return;
            }
            HideFrom(_orbViews, 0);
            HideFrom(_itemDropViews, 0);
            float delay = sim.IsTimedOut ? _timeoutClockSeconds : 0;
            RenderTimeoutClock(sim.IsTimedOut && _endingElapsed < delay);
            float defeat = Mathf.Clamp01((_endingElapsed - delay) / _defeatEndingSeconds);
            _playerView.ApplyDefeat(defeat);
            float scatter = Mathf.Clamp01((defeat - 0.35f) / 0.65f);
            for (int index = 0; index < DEFEAT_FRAGMENT_COUNT; index++)
            {
                SpriteRenderer fragment = GetEndingSprite(_endingFragments, index, "DefeatFragment");
                MissionWorldSpace.SetVisible(fragment, scatter > 0 && scatter < 1);
                float angle = index * Mathf.PI * 2f / DEFEAT_FRAGMENT_COUNT;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 center = MissionWorldSpace.ToWorld(sim.Player.Position, sim.MapCenter) * MissionWorldSpace.PIXELS_PER_UNIT;
                MissionWorldSpace.Place(fragment, center + direction * (8f + scatter * (28f + index % 4 * 9f)),
                    4f * (1f - scatter), new Color(1f, 0.12f, 0.15f, 1f - scatter));
                fragment.transform.localRotation = Quaternion.Euler(0, 0, index * 37f + scatter * 140f);
            }
        }

        /// <summary>시간 초과일 때 카메라 중앙에 원형 시계와 두 바늘을 표시하거나 숨긴다.</summary>
        private void RenderTimeoutClock(bool isVisible)
        {
            if (!isVisible) { HideFrom(_clockLines, 0); return; }
            Vector2 center = transform.InverseTransformPoint(_camera.transform.position);
            center *= MissionWorldSpace.PIXELS_PER_UNIT;
            Color tint = new Color(1f, 0.25f, 0.22f);
            for (int index = 0; index < CLOCK_SEGMENTS + 2; index++)
            {
                SpriteRenderer line = GetEndingSprite(_clockLines, index, "TimeoutClock");
                float angle = index * Mathf.PI * 2 / CLOCK_SEGMENTS;
                Vector2 from = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 46f;
                Vector2 to = center + new Vector2(Mathf.Cos(angle + Mathf.PI * 2 / CLOCK_SEGMENTS), Mathf.Sin(angle + Mathf.PI * 2 / CLOCK_SEGMENTS)) * 46f;
                if (index >= CLOCK_SEGMENTS) { from = center; to = center + (index == CLOCK_SEGMENTS ? Vector2.up * 31f : Vector2.right * 23f); }
                MissionWorldSpace.PlaceLine(line, from, to, 4f, tint);
            }
        }

        /// <summary>종료 연출의 index번째 스프라이트를 기존 월드 재질·정사각 자산으로 생성하거나 재사용한다.</summary>
        private SpriteRenderer GetEndingSprite(List<SpriteRenderer> views, int index, string label)
        {
            if (index == views.Count)
            {
                var sprite = new GameObject(label, typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
                sprite.transform.SetParent(transform, false);
                sprite.sprite = _squareSprite;
                sprite.sharedMaterial = _spriteMaterial;
                sprite.sortingOrder = 100;
                views.Add(sprite);
            }
            MissionWorldSpace.SetVisible(views[index], true);
            return views[index];
        }

        /// <summary>시작 노드 직결 범위의 최대 사거리를 플레이어 중심 흰 점선으로, 생성 위치와 모양은 반투명 채움으로 표시한다.</summary>
        private void UpdateRangePreview(RuneSimulation sim)
        {
            int lineCount = 0;
            int previewCount = 0;
            Mouse mouse = Mouse.current;
            if (mouse != null && _camera.TryGetSimPoint(mouse.position.ReadValue(), out SimVector pointer))
            {
                Vector2 center = MissionWorldSpace.ToWorld(sim.Player.Position, sim.MapCenter) * MissionWorldSpace.PIXELS_PER_UNIT;
                SimVector delta = pointer - sim.Player.Position;
                SimVector direction = delta.LengthSquared > 0.000001 ? delta.Normalized() : sim.Player.AimDirection;
                foreach (CompiledSpell spell in sim.Loadout)
                {
                    if (spell == null) continue;
                    foreach (SpellAction action in spell.Root)
                    {
                        if (!RuneSimulation.CanShowAreaPreview(action)) continue;
                        GetView(_rangePreviews, _spellPrefab, previewCount++).ApplyPreview(action,
                            sim.GetAreaPreviewPosition(pointer), direction, sim.MapCenter);
                        float radius = (float)sim.GetAreaPlacementRange();
                        for (int segment = 0; segment < RANGE_SEGMENTS; segment += 2)
                        {
                            if (lineCount == _rangeLines.Count)
                            {
                                var line = new GameObject("RangePreview", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
                                line.transform.SetParent(transform, false);
                                line.sprite = _squareSprite;
                                line.sharedMaterial = _spriteMaterial;
                                line.sortingOrder = RANGE_ORDER;
                                _rangeLines.Add(line);
                            }
                            SpriteRenderer target = _rangeLines[lineCount++];
                            MissionWorldSpace.SetVisible(target, true);
                            float from = segment * Mathf.PI * 2 / RANGE_SEGMENTS;
                            float to = (segment + 1) * Mathf.PI * 2 / RANGE_SEGMENTS;
                            MissionWorldSpace.PlaceLine(target, center + new Vector2(Mathf.Cos(from), Mathf.Sin(from)) * radius,
                                center + new Vector2(Mathf.Cos(to), Mathf.Sin(to)) * radius, RANGE_LINE_WIDTH, Color.white);
                        }
                    }
                }
            }
            HideFrom(_rangeLines, lineCount);
            HideFrom(_rangePreviews, previewCount);
        }

        /// <summary>새 시뮬레이션의 맵을 다시 만들고 피해 감지 기준값을 현재 값으로 맞춘다.</summary>
        private void BeginSimulation(RuneSimulation sim)
        {
            _endingElapsed = 0;
            _hasRenderedEnding = false;
            HideFrom(_endingFragments, 0);
            HideFrom(_clockLines, 0);
            foreach (OrbView view in _orbViews) view.transform.localScale = Vector3.one;
            foreach (ItemDropView view in _itemDropViews) view.transform.localScale = Vector3.one;
            _observedSimulation = sim;
            if (_additionalEnemyPrefabs.Count == 0)
            foreach (EnemyView prefab in Resources.LoadAll<EnemyView>("RuneCode/enemies"))
                { _additionalEnemyPrefabs.Add(prefab.EnemyId, prefab); _additionalEnemyViews.Add(prefab.EnemyId, new List<EnemyView>()); _additionalEnemyCounts.Add(prefab.EnemyId, 0); }
            _lastDamage = sim.TotalDamage;
            _lastStatusDamage = sim.StatusDamage;
            _lastPersistDamage = sim.PersistDamage;
            _lastHp = sim.Player.Hp;
            BuildMap(sim.Map, sim.MapCenter);
        }

        /// <summary>
        /// 적에게 준 직접 피해나 플레이어 체력 감소가 있으면 설정에 따라 카메라를 흔들고 히트 스톱을 시작한다.
        /// 상태 이상 피해(화염)나 잔류(Persist) 피해만으로는 카메라가 흔들리지 않고 히트 스톱도 시작하지 않는다.
        /// 히트 스톱은 시작 후 재발동 간격 동안 다시 시작하지 않는다.
        /// </summary>
        private void DetectImpact(RuneSimulation sim)
        {
            bool isHpLost = sim.Player.Hp < _lastHp;
            double directDelta = (sim.TotalDamage - _lastDamage) - (sim.StatusDamage - _lastStatusDamage) - (sim.PersistDamage - _lastPersistDamage);
            bool isImpact = directDelta > DAMAGE_EPSILON || isHpLost;
            if (isImpact && (_isShakeEnabled?.Invoke() ?? false)) _camera.Shake(SHAKE_SECONDS);
            if (isImpact && Time.unscaledTime >= _hitStopReadyAt)
            {
                _hitStopUntil = Time.unscaledTime + HIT_STOP_SECONDS;
                _hitStopReadyAt = Time.unscaledTime + HIT_STOP_COOLDOWN_SECONDS;
            }
            _lastDamage = sim.TotalDamage;
            _lastStatusDamage = sim.StatusDamage;
            _lastPersistDamage = sim.PersistDamage;
            _lastHp = sim.Player.Hp;
        }

        /// <summary>
        /// 기존 맵 표시를 지우고 벽 타일을 줄 단위로 묶어 사각형과 위쪽 경계선으로, 바닥 전체에 일정 간격의 격자선을 만든다.
        /// 맵은 전투 중 바뀌지 않으므로 시뮬레이션마다 한 번만 만든다.
        /// </summary>
        private void BuildMap(MissionMap map, SimVector origin)
        {
            if (_mapRoot != null) Destroy(_mapRoot.gameObject);
            _mapRoot = new GameObject("Map").transform;
            _mapRoot.SetParent(transform, false);
            int size = map.TileSize;
            for (float x = _gridSpacing; x < map.Width; x += _gridSpacing)
                AddLine(origin, new SimVector(x, 0), new SimVector(x, map.Height), GRID_LINE_WIDTH, GridColor, GRID_ORDER);
            for (float y = _gridSpacing; y < map.Height; y += _gridSpacing)
                AddLine(origin, new SimVector(0, y), new SimVector(map.Width, y), GRID_LINE_WIDTH, GridColor, GRID_ORDER);
            for (int row = 0; row < map.Tiles.Length; row++)
            {
                string tiles = map.Tiles[row];
                for (int start = 0; start < tiles.Length; start++)
                {
                    if (tiles[start] != '#') continue;
                    int end = start;
                    while (end + 1 < tiles.Length && tiles[end + 1] == '#') end++;
                    double left = start * size, right = (end + 1) * size, top = row * size;
                    AddLine(origin, new SimVector(left, top + size * 0.5), new SimVector(right, top + size * 0.5), size, WallColor, WALL_ORDER);
                    AddLine(origin, new SimVector(left, top), new SimVector(right, top), WALL_EDGE_WIDTH, WallEdgeColor, WALL_EDGE_ORDER);
                    start = end;
                }
            }
        }

        /// <summary>맵 표시용 정사각 스프라이트를 시뮬레이션 두 점 사이의 지정 두께(px) 선분으로 만들어 맵 루트에 둔다.</summary>
        private void AddLine(SimVector origin, SimVector from, SimVector to, float widthPixels, Color color, int order)
        {
            var line = new GameObject("Line", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            line.transform.SetParent(_mapRoot, false);
            line.sprite = _squareSprite;
            line.sharedMaterial = _spriteMaterial;
            line.sortingOrder = order;
            MissionWorldSpace.PlaceLine(line, MissionWorldSpace.ToWorld(from, origin) * MissionWorldSpace.PIXELS_PER_UNIT,
                MissionWorldSpace.ToWorld(to, origin) * MissionWorldSpace.PIXELS_PER_UNIT, widthPixels, color);
        }

        /// <summary>목록의 index번째 뷰를 반환하며 없으면 Prefab에서 만들고, 숨겨져 있으면 다시 표시한다.</summary>
        private T GetView<T>(List<T> views, T prefab, int index) where T : Component
        {
            if (index == views.Count) views.Add(Instantiate(prefab, transform));
            T view = views[index];
            MissionWorldSpace.SetVisible(view, true);
            return view;
        }

        /// <summary>목록의 index번째 체력 회복 표시를 반환하며 없으면 맵 스프라이트로 십자가를 새로 만든다.</summary>
        private HealPulseView GetHealPulseView(int index)
        {
            if (index == _healPulseViews.Count)
            {
                var view = new GameObject("HealPulse", typeof(HealPulseView)).GetComponent<HealPulseView>();
                view.Initialize(transform, _squareSprite, _spriteMaterial, HealPulseColor, HEAL_PULSE_ORDER);
                _healPulseViews.Add(view);
            }
            HealPulseView pulse = _healPulseViews[index];
            MissionWorldSpace.SetVisible(pulse, true);
            return pulse;
        }

        /// <summary>목록에서 count번째 이후 뷰를 숨긴다.</summary>
        private static void HideFrom<T>(List<T> views, int count) where T : Component
        {
            for (int i = count; i < views.Count; i++) MissionWorldSpace.SetVisible(views[i], false);
        }
    }
}
