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
        private const int MAX_SPAWN_WARNINGS = 32;
        private const float WALL_EDGE_WIDTH = 1f;
        private const float GRID_LINE_WIDTH = 1f;
        private const int GRID_ORDER = 0;
        private const int WALL_ORDER = 1;
        private const int WALL_EDGE_ORDER = 2;
        private const int SPAWN_WARNING_ORDER = 10;
        // 포털은 바닥 표시(스폰 예고 10)보다 위, 플레이어 몸체(59~63)보다 아래에 깔린다.
        private const int PORTAL_ORDER = 20;
        private const int ROUND_BREAK_DPS_ORDER = 70;
        private const float ROUND_BREAK_DPS_GAP_PIXELS = 40f;
        private const int RANGE_SEGMENTS = 96;
        private const float RANGE_LINE_WIDTH = 1.8f;
        private const int RANGE_ORDER = 65;
        private const int DEFEAT_FRAGMENT_COUNT = 24;
        private const int CLOCK_SEGMENTS = 40;

        private static readonly Color WallColor = new Color(0.13f, 0.20f, 0.27f);
        private static readonly Color WallEdgeColor = new Color(0.23f, 0.40f, 0.47f);
        private static readonly Color GridColor = new Color(0.15f, 0.22f, 0.29f);
        private static readonly Color HealPulseColor = new Color(0.45f, 0.95f, 0.55f);
        private static readonly Color SpawnWarningColor = new Color(0.95f, 0.2f, 0.2f);
        private static readonly Color EliteSpawnWarningColor = new Color(1f, 0.72f, 0.15f);
        private static readonly Color PortalColor = new Color(0.45f, 0.75f, 1f);

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
        [SerializeField, Min(0.1f)] private float _levelUpSeconds = 1.15f;
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
        private readonly List<SpawnWarningView> _spawnWarningViews = new List<SpawnWarningView>();
        private readonly List<ArcLinkView> _arcLinkViews = new List<ArcLinkView>();
        private readonly List<SpriteRenderer> _rangeLines = new List<SpriteRenderer>();
        private readonly List<SpellEntityView> _rangePreviews = new List<SpellEntityView>();
        private Func<RuneSimulation> _simulation;
        private Func<bool> _isShakeEnabled;
        private Func<bool> _isHitStopEnabled;
        private Func<bool> _isPortalActive;
        private Func<bool> _isPortalVisible;
        private Func<int> _level;
        private int _observedLevel;
        private bool _hasLevelUp;
        private RuneSimulation _observedSimulation;
        private Transform _mapRoot;
        private PlayerView _playerView;
        private PortalView _portalView;
        private RoundBreakDpsView _roundBreakDpsView;
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
        private bool _hasStartedClearEnding;

        /// <summary>월드를 따라가는 미션 카메라다. 화면 View가 조준 좌표 변환에 쓴다.</summary>
        public MissionCamera Camera => _camera;

        /// <summary>현재 시뮬레이션의 기존 레벨업 연출이 끝났는지 반환한다.</summary>
        public bool HasFinishedLevelUp(RuneSimulation sim)
            => _observedSimulation == sim && _hasLevelUp && _endingElapsed >= _victoryEndingSeconds + _levelUpSeconds;

        /// <summary>시뮬레이션·효과 설정·포털 상태·레벨 조회를 연결해 클리어 흡수 뒤 레벨 상승을 표시한다.</summary>
        public void Initialize(Func<RuneSimulation> simulation, Func<bool> isShakeEnabled, Func<bool> isHitStopEnabled,
            Func<bool> isPortalActive, Func<bool> isPortalVisible, Func<int> level)
        {
            _simulation = simulation;
            _isShakeEnabled = isShakeEnabled;
            _isHitStopEnabled = isHitStopEnabled;
            _isPortalActive = isPortalActive;
            _isPortalVisible = isPortalVisible;
            _level = level;
        }

        /// <summary>승리·패배·라운드 대기 상태에서 시뮬레이션을 진행하지 않고 연출과 라운드 대기 시간을 늘리며, 지정 시간을 채우면 true를 반환한다.</summary>
        public bool AdvanceEnding(RuneSimulation sim, float deltaTime)
        {
            if (!sim.Completed && !sim.IsFailed && !sim.IsWaitingForNextRound) return true;
            if (_observedSimulation != sim) BeginSimulation(sim);
            if (sim.Completed || sim.IsWaitingForNextRound)
            {
                if (sim.HasPendingClearPickups) return false;
                if (!_hasStartedClearEnding)
                {
                    _hasStartedClearEnding = true;
                    _endingElapsed = _victoryEndingSeconds;
                }
            }
            int level = _level.Invoke();
            if (level > _observedLevel) _hasLevelUp = true;
            _observedLevel = level;
            _endingElapsed += Mathf.Max(0, deltaTime);
            float duration = sim.Completed || sim.IsWaitingForNextRound ? _victoryEndingSeconds
                : _defeatEndingSeconds + (sim.IsTimedOut ? _timeoutClockSeconds : 0);
            if (_hasLevelUp && (sim.Completed || sim.IsWaitingForNextRound)) duration += _levelUpSeconds;
            return _endingElapsed >= duration;
        }

        void LateUpdate()
        {
            RuneSimulation sim = _simulation?.Invoke();
            if (sim == null)
            {
                if (_playerView != null) _playerView.ApplyLevelUp(-1f);
                HideFrom(_beamFlashViews, 0);
                HideFrom(_rangeLines, 0);
                HideFrom(_rangePreviews, 0);
                HidePortal();
                HideRoundBreakDps();
                return;
            }
            if (_observedSimulation != sim) BeginSimulation(sim);
            _beamFlashPresentation.Update(sim, Time.unscaledTime);
            if (_hasRenderedEnding)
            {
                if (sim.Completed || sim.IsFailed)
                {
                    RenderEnding(sim);
                    return;
                }
                _endingElapsed = 0;
                _hasRenderedEnding = false;
                foreach (OrbView view in _orbViews) view.transform.localScale = Vector3.one;
                foreach (ItemDropView view in _itemDropViews) view.transform.localScale = Vector3.one;
            }
            UpdateRangePreview(sim);
            DetectImpact(sim);
            // 전투 중에는 종료 연출 시간을 되돌려 다음 라운드 종료 연출이 0부터 진행되게 한다.
            if (!sim.IsWaitingForNextRound && !sim.Completed && !sim.IsFailed)
            {
                _endingElapsed = 0;
                _hasLevelUp = false;
                _hasStartedClearEnding = false;
                _observedLevel = _level.Invoke();
            }
            bool isHitStop = !sim.Completed && !sim.IsFailed && _hitStopUntil > Time.unscaledTime && (_isHitStopEnabled?.Invoke() ?? false);
            RenderBeamFlashes(sim, isHitStop);
            if (isHitStop) return;
            SimVector origin = sim.MapCenter;
            if (_playerView == null) _playerView = Instantiate(_playerPrefab, transform);
            _playerView.Apply(sim.Player, origin, sim.HasPlayerAura, sim.Time);
            _playerView.ApplyLevelUp(_hasLevelUp && sim.IsWaitingForNextRound
                ? (_endingElapsed - _victoryEndingSeconds) / _levelUpSeconds : -1f,
                _camera.GetAscensionHeight(_playerView.transform.position));
            UpdatePortal(sim, origin);
            int count = 0;
            foreach (string id in _additionalEnemyPrefabs.Keys) _additionalEnemyCounts[id] = 0;
            foreach (SimulationEnemy enemy in sim.Enemies)
            {
                string viewId = enemy.Definition.PatternSourceId;
                if (_additionalEnemyPrefabs.TryGetValue(viewId, out EnemyView prefab))
                {
                    int index = _additionalEnemyCounts[viewId]++;
                    GetView(_additionalEnemyViews[viewId], prefab, index).Apply(enemy, sim, origin);
                }
                else GetView(_enemyViews, _enemyPrefab, count++).Apply(enemy, sim, origin);
            }
            HideFrom(_enemyViews, count);
            foreach (string id in _additionalEnemyPrefabs.Keys) HideFrom(_additionalEnemyViews[id], _additionalEnemyCounts[id]);
            UpdateRoundBreakDps(sim, origin);
            count = 0;
            foreach (SimulationSpellEntity spell in sim.SpellEntities) GetView(_spellViews, _spellPrefab, count++).Apply(spell, origin);
            HideFrom(_spellViews, count);
            count = 0;
            foreach (SimulationProjectile projectile in sim.EnemyProjectiles)
            {
                Color? sourceTint = null;
                if (projectile.Style == HostileProjectileStyle.Sniper)
                    sourceTint = _enemyPrefab.DefaultTint; // Hexa는 전용 프리팹 없이 기본 적 프리팹의 몸체 색을 쓴다.
                else if (projectile.OwnerBoss != null && projectile.OwnerBoss.IsLegolasPattern
                    && _additionalEnemyPrefabs.TryGetValue(projectile.OwnerBoss.EnemyId, out EnemyView sourcePrefab))
                    sourceTint = sourcePrefab.DefaultTint;
                GetView(_projectileViews, _projectilePrefab, count++).Apply(projectile, origin, sourceTint);
            }
            HideFrom(_projectileViews, count);
            count = 0;
            foreach (FragmentOrb orb in sim.Orbs) GetView(_orbViews, _orbPrefab, count++).Apply(orb, origin);
            HideFrom(_orbViews, count);
            count = 0;
            foreach (SimulationItemDrop drop in sim.ItemDrops) GetView(_itemDropViews, _itemDropPrefab, count++).Apply(drop, sim.Tick - drop.SpawnTick, origin);
            HideFrom(_itemDropViews, count);
            // 라운드 대기 중에는 남은 오브·드롭이 ClearOrbs/ClearItemDrops 로 옮겨져 일반 루프가 비므로 같은 뷰 풀에 흡수 연출을 덧그린다.
            if (sim.IsWaitingForNextRound) RenderClearAbsorb(sim);
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
            foreach (SpawnWarning warning in sim.SpawnWarnings)
            {
                if (count >= MAX_SPAWN_WARNINGS) break;
                GetSpawnWarningView(count++).Apply(warning, origin, Time.unscaledTime);
            }
            HideFrom(_spawnWarningViews, count);
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

        /// <summary>최종 승리·패배 시 정지된 전장 위에서 흡수 연출 또는 패배 시계·붉은 몸체·파편만 표시한다. 보상과 전투 상태는 변경하지 않는다.</summary>
        private void RenderEnding(RuneSimulation sim)
        {
            HidePortal();
            HideRoundBreakDps();
            HideFrom(_beamFlashViews, 0);
            HideFrom(_rangeLines, 0);
            HideFrom(_rangePreviews, 0);
            HideFrom(_spellViews, 0);
            HideFrom(_projectileViews, 0);
            HideFrom(_damageNumberViews, 0);
            HideFrom(_healPulseViews, 0);
            HideFrom(_arcLinkViews, 0);
            if (sim.Completed || sim.IsWaitingForNextRound)
            {
                RenderClearAbsorb(sim);
                _playerView.ApplyLevelUp(_hasLevelUp ? (_endingElapsed - _victoryEndingSeconds) / _levelUpSeconds : -1f,
                    _camera.GetAscensionHeight(_playerView.transform.position));
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

        /// <summary>시뮬레이션에서 실제 이동·회수 중인 클리어 스크랩과 룬만 표시한다. 별도 시간 보간이나 도착 추정은 하지 않는다.</summary>
        private void RenderClearAbsorb(RuneSimulation sim)
        {
            int count = 0;
            foreach (FragmentOrb orb in sim.ClearOrbs)
            {
                OrbView view = GetView(_orbViews, _orbPrefab, count++);
                view.Apply(orb, sim.MapCenter);
                view.transform.localScale = Vector3.one;
                MissionWorldSpace.SetVisible(view, true);
            }
            HideFrom(_orbViews, count);
            count = 0;
            foreach (SimulationItemDrop drop in sim.ClearItemDrops)
            {
                ItemDropView view = GetView(_itemDropViews, _itemDropPrefab, count++);
                view.Apply(drop, sim.Tick - drop.SpawnTick, sim.MapCenter);
                view.transform.localScale = Vector3.one;
                MissionWorldSpace.SetVisible(view, true);
            }
            HideFrom(_itemDropViews, count);
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

        /// <summary>
        /// 시작 노드 직결 범위의 최대 사거리를 플레이어 중심 흰 점선으로, 생성 위치와 모양은 반투명 채움으로 표시한다.
        /// 근거리·원거리 피해 강화가 있으면 같은 거리의 점선을 항상 표시해 두 구간의 경계를 보여준다.
        /// </summary>
        private void UpdateRangePreview(RuneSimulation sim)
        {
            int lineCount = 0;
            int previewCount = 0;
            bool isRangeDrawn = false;
            Vector2 center = MissionWorldSpace.ToWorld(sim.Player.Position, sim.MapCenter) * MissionWorldSpace.PIXELS_PER_UNIT;
            float radius = (float)sim.GetAreaPlacementRange();
            Mouse mouse = Mouse.current;
            if (mouse != null && _camera.TryGetSimPoint(mouse.position.ReadValue(), out SimVector pointer))
            {
                foreach (CompiledSpell spell in sim.Loadout)
                {
                    if (!RuneSimulation.CanShowAreaPreview(spell)) continue;
                    foreach (SpellAction action in spell.Root)
                    {
                        if (!RuneSimulation.CanShowAreaPreview(action)) continue;
                        bool isSlash = action.Form == SlashBehaviorRules.KIND;
                        int count = isSlash ? action.Count : 1;
                        for (int index = 0; index < count; index++)
                        {
                            sim.GetRangePreview(action, pointer, index, out SimVector position, out SimVector direction);
                            GetView(_rangePreviews, _spellPrefab, previewCount++).ApplyPreview(action, position, direction, sim.MapCenter);
                        }
                        if (isSlash || isRangeDrawn) continue;
                        lineCount = PlaceRangeCircle(center, radius, lineCount);
                        isRangeDrawn = true;
                    }
                }
            }
            if (!isRangeDrawn && sim.HasRangeDamageBonus) lineCount = PlaceRangeCircle(center, radius, lineCount);
            HideFrom(_rangeLines, lineCount);
            HideFrom(_rangePreviews, previewCount);
        }

        /// <summary>점선 조각을 lineCount 번째부터 재사용·생성해 center 중심 radius 반경의 흰 점선 원을 그리고 다음 조각 번호를 반환한다.</summary>
        private int PlaceRangeCircle(Vector2 center, float radius, int lineCount)
        {
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
            return lineCount;
        }

        /// <summary>새 시뮬레이션의 맵을 다시 만들고 피해 감지 기준값을 현재 값으로 맞춘다.</summary>
        private void BeginSimulation(RuneSimulation sim)
        {
            _endingElapsed = 0;
            _hasRenderedEnding = false;
            _hasStartedClearEnding = false;
            _hasLevelUp = false;
            _observedLevel = _level.Invoke();
            if (_playerView != null) _playerView.ApplyLevelUp(-1f);
            HideFrom(_endingFragments, 0);
            HideFrom(_clockLines, 0);
            HidePortal();
            HideRoundBreakDps();
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

        /// <summary>목록의 index번째 스폰 예고 표시를 반환하며 없으면 맵 스프라이트로 X를 새로 만든다. 색은 표시마다 엘리트 여부로 View가 고른다.</summary>
        private SpawnWarningView GetSpawnWarningView(int index)
        {
            if (index == _spawnWarningViews.Count)
            {
                var view = new GameObject("SpawnWarning", typeof(SpawnWarningView)).GetComponent<SpawnWarningView>();
                view.Initialize(transform, _squareSprite, _spriteMaterial, SpawnWarningColor, EliteSpawnWarningColor, SPAWN_WARNING_ORDER);
                _spawnWarningViews.Add(view);
            }
            SpawnWarningView warningView = _spawnWarningViews[index];
            MissionWorldSpace.SetVisible(warningView, true);
            return warningView;
        }

        /// <summary>대기 포털이 준비되고 표시가 허용됐으면 위치·반경과 진입 가능 여부를 반영하고, 그 외에는 숨긴다.</summary>
        private void UpdatePortal(RuneSimulation sim, SimVector origin)
        {
            if (!sim.IsRoundBreakPortalOpen || !_isPortalVisible()) { HidePortal(); return; }
            PortalView portal = GetPortalView();
            MissionWorldSpace.SetVisible(portal, true);
            portal.Apply(sim.RoundBreakPortalPosition, sim.RoundBreakPortalRadius, _isPortalActive?.Invoke() ?? false, origin, Time.unscaledTime);
        }

        /// <summary>라운드 대기 포털 뷰를 숨긴다. 아직 만들지 않았으면 아무것도 하지 않는다.</summary>
        private void HidePortal()
        {
            if (_portalView != null) MissionWorldSpace.SetVisible(_portalView, false);
        }

        /// <summary>라운드 대기 포털 뷰를 반환하며 없으면 맵 스프라이트로 고리와 코어를 새로 만든다.</summary>
        private PortalView GetPortalView()
        {
            if (_portalView == null)
            {
                _portalView = new GameObject("RoundBreakPortal", typeof(PortalView)).GetComponent<PortalView>();
                _portalView.Initialize(transform, _squareSprite, _spriteMaterial, PortalColor, PORTAL_ORDER);
            }
            return _portalView;
        }

        /// <summary>라운드 대기 시험장 허수아비 6기의 아래쪽 가운데에 합산 DPS 문구를 표시하고, 시험장이 준비되지 않았거나 허수아비가 없으면 숨긴다.</summary>
        private void UpdateRoundBreakDps(RuneSimulation sim, SimVector origin)
        {
            if (!sim.IsRoundBreakArenaReady) { HideRoundBreakDps(); return; }
            bool hasDummy = false;
            double minX = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (SimulationEnemy enemy in sim.Enemies)
            {
                if (!enemy.IsDummy) continue;
                hasDummy = true;
                double radius = enemy.Radius;
                minX = Math.Min(minX, enemy.Position.X - radius);
                maxX = Math.Max(maxX, enemy.Position.X + radius);
                maxY = Math.Max(maxY, enemy.Position.Y + radius);
            }
            if (!hasDummy) { HideRoundBreakDps(); return; }
            SimVector anchor = new SimVector((minX + maxX) * 0.5, maxY + ROUND_BREAK_DPS_GAP_PIXELS);
            RoundBreakDpsView view = GetRoundBreakDpsView();
            MissionWorldSpace.SetVisible(view, true);
            view.Apply(sim.RoundBreakDps, anchor, origin);
        }

        /// <summary>허수아비 DPS 문구를 숨긴다. 아직 만들지 않았으면 아무것도 하지 않는다.</summary>
        private void HideRoundBreakDps()
        {
            if (_roundBreakDpsView != null) MissionWorldSpace.SetVisible(_roundBreakDpsView, false);
        }

        /// <summary>허수아비 DPS 문구 뷰를 반환하며 없으면 TMP 문구를 새로 만든다.</summary>
        private RoundBreakDpsView GetRoundBreakDpsView()
        {
            if (_roundBreakDpsView == null)
            {
                _roundBreakDpsView = new GameObject("RoundBreakDps", typeof(RoundBreakDpsView)).GetComponent<RoundBreakDpsView>();
                _roundBreakDpsView.Initialize(transform, ROUND_BREAK_DPS_ORDER);
            }
            return _roundBreakDpsView;
        }

        /// <summary>목록에서 count번째 이후 뷰를 숨긴다.</summary>
        private static void HideFrom<T>(List<T> views, int count) where T : Component
        {
            for (int i = count; i < views.Count; i++) MissionWorldSpace.SetVisible(views[i], false);
        }
    }
}
