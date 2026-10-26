using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Serialization;

namespace RuneCode
{
    /// <summary>
    /// 미션 적 하나의 몸체, 체력 막대, 엘리트 외곽선, 공격 예고, 릴레이 오라, 이지스 방패, 보스 패치, 상태 이상·내성 표시를 갱신한다.
    /// 몸체와 방패는 회전 자식에 두고, 엘리트 외곽선은 몸체의 모양과 회전을 따른다. 나머지 표시는 회전하지 않는다.
    /// </summary>
    public sealed class EnemyView : MonoBehaviour
    {
        private const float CORE_SCALE = 0.48f;
        private const float WARNING_GAP = 6f;
        private const float WARNING_PULSE = 3f;
        private const float WARNING_PULSE_SPEED = 16f;
        private const float SHIELD_GAP = 5f;
        private const float PATCH_GAP = 12f;
        private const float ICON_GAP = 8f;
        private const float ICON_RADIUS = 4f;
        private const float HP_BAR_MIN_WIDTH = 28f;
        private const float HP_BAR_HEIGHT = 3f;
        private const int SNIPER_BLINK_TICKS = 6;

        private const float HEXA_OPPOSITE_ANGLE = 180f;
        private const int RADIATE_WAVE_SEGMENTS = 24;
        private const int RADIATE_WARNING_ORDER_OFFSET = -6;
        private const int RADIATE_WAVE_ORDER_OFFSET = 1;
        private const float RADIATE_WARNING_MIN_ALPHA = 0.05f;
        private const float RADIATE_WARNING_MAX_ALPHA = 0.3f;

        private static readonly Color CoreColor = new Color(0.04f, 0.09f, 0.13f);
        private static readonly Color DummyColor = new Color(0.52f, 0.69f, 0.74f);
        private static readonly Color WarningColor = new Color(1f, 0.78f, 0.23f);
        private static readonly Color AuraColor = new Color(0.36f, 0.84f, 0.44f, 0.22f);
        private static readonly Color ShieldColor = new Color(0.95f, 0.77f, 0.40f);
        private static readonly Color PatchColor = new Color(0.83f, 0.57f, 1f);
        private static readonly Color ResistColor = new Color(0.77f, 0.69f, 1f);
        private static readonly Color HpBackColor = new Color(0.24f, 0.16f, 0.20f);
        private static readonly Color RadiateWaveColor = new Color(1f, 0.62f, 0.16f);

        [Header("Prefab 식별")]
        [SerializeField] private string _enemyId = "enemy.scout";
        public string EnemyId => _enemyId;

        [Header("종류별 모양")]
        [SerializeField] private EnemyAppearance[] _appearances;
        [SerializeField] private Sprite _defaultShape;
        [SerializeField] private Color _defaultTint = new Color(0.94f, 0.34f, 0.40f);
        public Color DefaultTint => _defaultTint;

        [Header("엘리트 외곽선")]
        [SerializeField] private Color _eliteOutlineColor = new Color(0.35f, 0.75f, 1f);
        [SerializeField, Min(0f)] private float _eliteOutlinePadding = 2f;
        [SerializeField, Min(0f)] private float _eliteOutlinePulseAmount = 2f;
        [SerializeField, Min(0.1f)] private float _eliteOutlinePulsePeriod = 1f;

        [Header("표시")]
        [SerializeField] private Transform _rotator;
        [SerializeField] private SpriteRenderer _body;
        [SerializeField] private SpriteRenderer _core;
        [SerializeField] private SpriteRenderer _shieldArc;
        [SerializeField] private SpriteRenderer _warningRing;
        [SerializeField] private SpriteRenderer _auraRing;
        [SerializeField] private SpriteRenderer _patchRing;
        [FormerlySerializedAs("_eliteRing")]
        [SerializeField] private SpriteRenderer _eliteOutline;
        [SerializeField] private SpriteRenderer _burnIcon;
        [SerializeField] private SpriteRenderer _chillIcon;
        [SerializeField] private SpriteRenderer _empIcon;
        [SerializeField] private SpriteRenderer _resistRing;
        [SerializeField] private SpriteRenderer _hpBack;
        [SerializeField] private SpriteRenderer _hpFill;
        [SerializeField] private SpriteRenderer _circleShield;
        [SerializeField] private SpriteRenderer _sniperLine;
        [SerializeField] private HydraBossView _hydraView;
        [SerializeField] private LegolasBossView _legolasView;

        private SpriteRenderer _hexaFirstTriangle;
        private SpriteRenderer _hexaSecondTriangle;
        private SpriteRenderer[] _freezeOutline;
        private Transform _radiateWarningRoot;
        private Mesh _radiateWarningMesh;
        private UnityEngine.UI.VertexHelper _radiateWarningVertices;
        private readonly List<LineRenderer> _radiateWaveLines = new List<LineRenderer>();
        private readonly Vector3[] _radiateWavePoints = new Vector3[RADIATE_WAVE_SEGMENTS];

        void OnDestroy()
        {
            _radiateWarningVertices?.Dispose();
            if (_radiateWarningMesh != null) Destroy(_radiateWarningMesh);
        }

        /// <summary>적 상태와 시뮬레이션의 시간·상태 이상·적응 정보로 맵 중앙(origin) 기준 월드 표시와 엘리트 외곽선의 맥동을 갱신한다.</summary>
        public void Apply(SimulationEnemy enemy, RuneSimulation sim, SimVector origin)
        {
            float radius = (float)enemy.Radius;
            transform.localPosition = MissionWorldSpace.ToWorld(enemy.Position, origin);
            _rotator.localRotation = Quaternion.Euler(0, 0, MissionWorldSpace.ToWorldAngle(enemy.Facing));
            EnemyAppearance appearance = FindAppearance(enemy.Definition.PatternSourceId);
            Color tint = appearance != null ? appearance.Tint : _defaultTint;
            if (enemy.IsDummy) tint = DummyColor;
            if (enemy.IsFlashing) tint = Color.white;
            bool isFrozen = sim.HasEnemyStatus(enemy, EnemyStatusType.Freeze);
            if (isFrozen) tint = RuneMesh.FrozenBodyColor;
            _body.sprite = _core.sprite = appearance != null ? appearance.Shape : _defaultShape;
            MissionWorldSpace.Place(_body, Vector2.zero, radius * 2, tint);
            MissionWorldSpace.Place(_core, Vector2.zero, radius * 2 * CORE_SCALE, CoreColor);
            ApplyHexaBody(enemy.Hexa != null, radius, tint);
            MissionWorldSpace.SetVisible(_eliteOutline, enemy.IsElite);
            if (enemy.IsElite)
            {
                _eliteOutline.sprite = _body.sprite;
                _eliteOutline.transform.rotation = _body.transform.rotation;
                float pulse = (Mathf.Sin((float)sim.Time * 2f * Mathf.PI / _eliteOutlinePulsePeriod) + 1f) * 0.5f;
                float padding = _eliteOutlinePadding + pulse * _eliteOutlinePulseAmount;
                MissionWorldSpace.Place(_eliteOutline, Vector2.zero, (radius + padding) * 2, _eliteOutlineColor);
            }

            MissionWorldSpace.SetVisible(_warningRing, enemy.IsWarning);
            if (enemy.IsWarning)
                MissionWorldSpace.Place(_warningRing, Vector2.zero, (radius + WARNING_GAP + Mathf.Sin(Time.unscaledTime * WARNING_PULSE_SPEED) * WARNING_PULSE) * 2, WarningColor);
            bool isRelay = enemy.Definition.HasTrait(EnemyDefinition.TRAIT_RELAY_AURA);
            MissionWorldSpace.SetVisible(_auraRing, isRelay);
            if (isRelay) MissionWorldSpace.Place(_auraRing, Vector2.zero, (float)GameData.Balance.Combat.RelayRadius * 2, AuraColor);
            bool hasShield = enemy.Definition.HasTrait(EnemyDefinition.TRAIT_AEGIS_SHIELD) && !sim.HasEnemyStatus(enemy, EnemyStatusType.Emp);
            MissionWorldSpace.SetVisible(_shieldArc, hasShield);
            if (hasShield) MissionWorldSpace.Place(_shieldArc, Vector2.zero, (radius + SHIELD_GAP) * 2, ShieldColor);
            MissionWorldSpace.SetVisible(_patchRing, enemy.IsPatching);
            if (enemy.IsPatching) MissionWorldSpace.Place(_patchRing, Vector2.zero, (radius + PATCH_GAP) * 2, PatchColor);

            // 전용 아이콘 참조가 없어 화상 아이콘을 찜통 색으로 재사용한다.
            bool hasBurn = sim.HasEnemyStatus(enemy, EnemyStatusType.Burn);
            bool hasSteam = sim.HasEnemyStatus(enemy, EnemyStatusType.Steam);
            ApplyIcon(_burnIcon, hasBurn || hasSteam, new Vector2(-radius, radius + ICON_GAP),
                hasSteam ? SpellGrammar.ELEMENT_STEAM : "fire");
            ApplyIcon(_chillIcon, sim.GetChillStacks(enemy) > 0 || sim.HasEnemyStatus(enemy, EnemyStatusType.Freeze), new Vector2(0, radius + ICON_GAP), "ice");
            ApplyIcon(_empIcon, sim.HasEnemyStatus(enemy, EnemyStatusType.Emp) || sim.HasEnemyStatus(enemy, EnemyStatusType.Shock), new Vector2(radius, radius + ICON_GAP), "arc");
            double threshold = GameData.Balance.Adaptation.ResistanceThreshold;
            bool isResistant = enemy.Hydra == null && sim.Adaptation.Enabled && (sim.Adaptation.GetValue(enemy.LastDamageElement) >= threshold || sim.Adaptation.GetValue(enemy.LastDamageForm) >= threshold);
            MissionWorldSpace.SetVisible(_resistRing, isResistant);
            if (isResistant) MissionWorldSpace.Place(_resistRing, new Vector2(radius + ICON_GAP, radius), ICON_RADIUS * 2, ResistColor);

            float width = Mathf.Max(HP_BAR_MIN_WIDTH, radius * 2);
            float barY = -radius - ICON_GAP + HP_BAR_HEIGHT * 0.5f;
            float ratio = Mathf.Clamp01((float)(enemy.Hp / enemy.MaxHp));
            MissionWorldSpace.PlaceLine(_hpBack, new Vector2(-width * 0.5f, barY), new Vector2(width * 0.5f, barY), HP_BAR_HEIGHT, HpBackColor);
            MissionWorldSpace.SetVisible(_hpFill, ratio > 0);
            if (ratio > 0) MissionWorldSpace.PlaceLine(_hpFill, new Vector2(-width * 0.5f, barY), new Vector2(-width * 0.5f + width * ratio, barY), HP_BAR_HEIGHT, tint);
            ApplyAdditionalIndicators(enemy, sim);
            if (_hydraView != null && enemy.Hydra != null) _hydraView.Apply(enemy, sim, _body, _rotator);
            if (_legolasView != null && enemy.Legolas != null) _legolasView.Apply(enemy, _body, _rotator);
            ApplyFreezeOutline(isFrozen, radius);
            ApplyRadiate(enemy, sim);
        }

        /// <summary>빙결 중에만 몸체 바깥의 육각 얼음 윤곽을 표시하고, 최초 생성한 선들을 해제·재사용 시 숨기거나 다시 쓴다.</summary>
        private void ApplyFreezeOutline(bool isFrozen, float radius)
        {
            if (isFrozen && _freezeOutline == null)
            {
                _freezeOutline = new SpriteRenderer[RuneMesh.FREEZE_OUTLINE_SIDES];
                for (int index = 0; index < _freezeOutline.Length; index++)
                {
                    SpriteRenderer edge = Instantiate(_hpBack, transform, false);
                    edge.name = "FreezeOutline" + index;
                    edge.sortingLayerID = _body.sortingLayerID;
                    edge.sortingOrder = _core.sortingOrder + 1;
                    _freezeOutline[index] = edge;
                }
            }
            if (_freezeOutline == null) return;
            float outlineRadius = radius * 1.5f + RuneMesh.FREEZE_OUTLINE_PADDING;
            for (int index = 0; index < _freezeOutline.Length; index++)
            {
                SpriteRenderer edge = _freezeOutline[index];
                MissionWorldSpace.SetVisible(edge, isFrozen);
                if (!isFrozen) continue;
                float angle = index * Mathf.PI * 2f / RuneMesh.FREEZE_OUTLINE_SIDES;
                float nextAngle = (index + 1) * Mathf.PI * 2f / RuneMesh.FREEZE_OUTLINE_SIDES;
                Vector2 from = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * outlineRadius;
                Vector2 to = new Vector2(Mathf.Cos(nextAngle), Mathf.Sin(nextAngle)) * outlineRadius;
                MissionWorldSpace.PlaceLine(edge, from, to, RuneMesh.FREEZE_OUTLINE_WIDTH, RuneMesh.FrozenOutlineColor);
            }
        }

        /// <summary>Hexa일 때 기존 삼각형 두 개로 육각성을 표시하고, 일반 적 재사용 시 기본 몸체와 코어를 복원한다.</summary>
        private void ApplyHexaBody(bool isHexa, float radius, Color tint)
        {
            if (isHexa && _hexaFirstTriangle == null)
            {
                Sprite triangle = FindAppearance("enemy.scout").Shape;
                _hexaFirstTriangle = Instantiate(_body, _rotator, false);
                _hexaFirstTriangle.name = "HexaFirstTriangle";
                _hexaFirstTriangle.sprite = triangle;
                _hexaFirstTriangle.transform.localRotation = Quaternion.identity;
                _hexaSecondTriangle = Instantiate(_body, _rotator, false);
                _hexaSecondTriangle.name = "HexaSecondTriangle";
                _hexaSecondTriangle.sprite = triangle;
                _hexaSecondTriangle.transform.localRotation = Quaternion.Euler(0, 0, HEXA_OPPOSITE_ANGLE);
            }
            MissionWorldSpace.SetVisible(_body, !isHexa);
            MissionWorldSpace.SetVisible(_core, !isHexa);
            if (_hexaFirstTriangle == null) return;
            MissionWorldSpace.SetVisible(_hexaFirstTriangle, isHexa);
            MissionWorldSpace.SetVisible(_hexaSecondTriangle, isHexa);
            if (!isHexa) return;
            MissionWorldSpace.Place(_hexaFirstTriangle, Vector2.zero, radius * 2, tint);
            MissionWorldSpace.Place(_hexaSecondTriangle, Vector2.zero, radius * 2, tint);
        }

        /// <summary>Hexa의 방사 경고 구역과 발사된 부채꼴 파동을 표시한다. 경고는 다음 발사 구역이 남아 있을 때만, 파동은 방사 중에만 보이고 나머지는 숨긴다. 런타임 자식 오브젝트를 재사용한다.</summary>
        private void ApplyRadiate(SimulationEnemy enemy, RuneSimulation sim)
        {
            HexaBossState hexa = enemy.Hexa;
            bool isRadiating = hexa != null && !hexa.IsCancelled && hexa.IsRadiating;
            if (!isRadiating)
            {
                if (_radiateWarningRoot != null) _radiateWarningRoot.gameObject.SetActive(false);
                HideRadiateWaves(0);
                return;
            }
            // 네 번째 발사 뒤에는 파동이 사라질 때까지 방사 중이지만 경고할 구역은 없다.
            if (hexa.IsRadiateWarning) RenderRadiateWarning(enemy, sim, hexa);
            else if (_radiateWarningRoot != null) _radiateWarningRoot.gameObject.SetActive(false);
            RenderRadiateWaves(enemy, hexa);
        }

        /// <summary>경고 중인 구역을 Hexa 중심과 맵 경계점 다각형으로 채우고 경고 진행에 따라 알파를 높인다.</summary>
        private void RenderRadiateWarning(SimulationEnemy enemy, RuneSimulation sim, HexaBossState hexa)
        {
            if (_radiateWarningRoot == null) CreateRadiateWarning();
            _radiateWarningRoot.gameObject.SetActive(true);
            SimVector[] polygon = HexaRadiateWave.GetSectorPolygon(enemy.Position, hexa.RadiateWarningDirection,
                sim.Map.Width, sim.Map.Height);
            Color color = WarningColor;
            color.a = Mathf.Lerp(RADIATE_WARNING_MIN_ALPHA, RADIATE_WARNING_MAX_ALPHA, (float)hexa.RadiateWarningProgress);
            _radiateWarningVertices.Clear();
            for (int index = 0; index < polygon.Length; index++)
                _radiateWarningVertices.AddVert(RadiateLocalPoint(polygon[index], enemy.Position), color, Vector2.zero);
            for (int index = 1; index + 1 < polygon.Length; index++)
                _radiateWarningVertices.AddTriangle(0, index, index + 1);
            _radiateWarningVertices.FillMesh(_radiateWarningMesh);
        }

        /// <summary>몸체 재질과 정렬층을 공유하는 경고 구역 메시를 한 번 생성한다. 회전하지 않는 적 루트 아래 몸체 뒤에 깔린다.</summary>
        private void CreateRadiateWarning()
        {
            GameObject warning = new GameObject("HexaRadiateWarning", typeof(MeshFilter), typeof(MeshRenderer));
            _radiateWarningRoot = warning.transform;
            _radiateWarningRoot.SetParent(transform, false);
            _radiateWarningMesh = new Mesh { name = "HexaRadiateWarning" };
            _radiateWarningMesh.MarkDynamic();
            _radiateWarningVertices = new UnityEngine.UI.VertexHelper();
            warning.GetComponent<MeshFilter>().sharedMesh = _radiateWarningMesh;
            MeshRenderer renderer = warning.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _body.sharedMaterial;
            renderer.sortingLayerID = _body.sortingLayerID;
            renderer.sortingOrder = _body.sortingOrder + RADIATE_WARNING_ORDER_OFFSET;
            var block = new MaterialPropertyBlock();
            block.SetTexture("_MainTex", Texture2D.whiteTexture);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>발사된 파동을 현재 판정 반경과 두께의 90° 호 선 렌더러로 표시하고 남은 렌더러는 숨긴다.</summary>
        private void RenderRadiateWaves(SimulationEnemy enemy, HexaBossState hexa)
        {
            double halfRadians = HexaDefinition.RADIATE_SECTOR_HALF_DEGREES * Mathf.Deg2Rad;
            for (int index = 0; index < hexa.RadiateWaves.Count; index++)
            {
                HexaRadiateWave wave = hexa.RadiateWaves[index];
                LineRenderer line = GetRadiateWaveRenderer(index);
                float radius = (float)wave.GetRadius();
                double centerAngle = Math.Atan2(wave.Direction.Y, wave.Direction.X);
                for (int pointIndex = 0; pointIndex < RADIATE_WAVE_SEGMENTS; pointIndex++)
                {
                    double angle = centerAngle - halfRadians
                        + halfRadians * 2 * pointIndex / (RADIATE_WAVE_SEGMENTS - 1);
                    SimVector point = wave.Center + new SimVector(Math.Cos(angle), Math.Sin(angle)) * radius;
                    _radiateWavePoints[pointIndex] = RadiateLocalPoint(point, enemy.Position);
                }
                line.widthMultiplier = (float)HexaDefinition.RADIATE_WIDTH / MissionWorldSpace.PIXELS_PER_UNIT;
                line.startColor = line.endColor = RadiateWaveColor;
                line.SetPositions(_radiateWavePoints);
            }
            HideRadiateWaves(hexa.RadiateWaves.Count);
        }

        /// <summary>몸체 재질과 정렬층을 공유하는 파동 호 선 렌더러를 필요한 수만 생성해 재사용한다.</summary>
        private LineRenderer GetRadiateWaveRenderer(int index)
        {
            while (_radiateWaveLines.Count <= index)
            {
                GameObject child = new GameObject("RadiateWave" + _radiateWaveLines.Count);
                child.transform.SetParent(transform, false);
                LineRenderer line = child.AddComponent<LineRenderer>();
                line.sharedMaterial = _body.sharedMaterial;
                line.sortingLayerID = _body.sortingLayerID;
                line.sortingOrder = _body.sortingOrder + RADIATE_WAVE_ORDER_OFFSET;
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = RADIATE_WAVE_SEGMENTS;
                _radiateWaveLines.Add(line);
            }
            MissionWorldSpace.SetVisible(_radiateWaveLines[index], true);
            return _radiateWaveLines[index];
        }

        /// <summary>현재 사용하지 않는 파동 선 렌더러 뒤쪽을 숨긴다.</summary>
        private void HideRadiateWaves(int count)
        {
            for (int index = count; index < _radiateWaveLines.Count; index++)
                MissionWorldSpace.SetVisible(_radiateWaveLines[index], false);
        }

        /// <summary>시뮬레이션 위치를 적 기준 표시 단위 좌표(y 반전)로 변환한다.</summary>
        private static Vector2 RadiateLocalPoint(SimVector point, SimVector origin)
            => new Vector2((float)(point.X - origin.X), -(float)(point.Y - origin.Y)) / MissionWorldSpace.PIXELS_PER_UNIT;

        /// <summary>원형 보호막과 저격 경로를 시뮬레이션의 반경·저장 조준선으로 갱신한다.</summary>
        private void ApplyAdditionalIndicators(SimulationEnemy enemy, RuneSimulation sim)
        {
            if (_circleShield != null)
            {
                bool hasShield = enemy.HasCircleShield;
                MissionWorldSpace.SetVisible(_circleShield, hasShield);
                if (hasShield) MissionWorldSpace.Place(_circleShield, Vector2.zero, (float)enemy.Definition.ShieldRadius * 2, new Color(0.3f, 0.95f, 0.85f, 0.7f));
            }
            if (_sniperLine != null)
            {
                MissionWorldSpace.SetVisible(_sniperLine, enemy.IsAiming);
                if (!enemy.IsAiming) return;
                Vector2 start = MissionWorldSpace.ToWorld(enemy.AttackOrigin, enemy.Position) * MissionWorldSpace.PIXELS_PER_UNIT;
                Vector2 end = MissionWorldSpace.ToWorld(sim.GetSniperEnd(enemy), enemy.Position) * MissionWorldSpace.PIXELS_PER_UNIT;
                Color color = new Color(1, 0.12f, 0.2f, (sim.Tick / SNIPER_BLINK_TICKS) % 2 == 0 ? 0.6f : 0.2f);
                MissionWorldSpace.PlaceLine(_sniperLine, start, end, 2, color);
            }
        }

        /// <summary>상태 아이콘을 표시 여부에 따라 켜고 적 기준 위치(px)에 속성 색으로 둔다.</summary>
        private void ApplyIcon(SpriteRenderer icon, bool isVisible, Vector2 localPixels, string element)
        {
            MissionWorldSpace.SetVisible(icon, isVisible);
            if (isVisible) MissionWorldSpace.Place(icon, localPixels, ICON_RADIUS * 2, RuneMesh.ElementColor(element));
        }

        /// <summary>적 ID에 해당하는 모양 설정을 반환하며 없으면 null을 반환한다.</summary>
        private EnemyAppearance FindAppearance(string enemyId)
        {
            for (int i = 0; i < _appearances.Length; i++) if (_appearances[i].EnemyId == enemyId) return _appearances[i];
            return null;
        }

        [Serializable]
        public sealed class EnemyAppearance
        {
            [SerializeField] private string _enemyId;
            [SerializeField] private Sprite _shape;
            [SerializeField] private Color _tint = Color.white;
            public string EnemyId => _enemyId;
            public Sprite Shape => _shape;
            public Color Tint => _tint;
        }
    }
}
