using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 마법 개체 하나를 형태에 맞춰 표시한다. 발사·공전은 잔상과 속성별 도형, 폭발·잔류는 채움·외곽선·안쪽 선을 쓴다.
    /// Beam Shape 개체는 진행 방향으로 긴 사각형으로 그리며, 폭발·잔류 Beam은 뒤쪽 면을 개체 위치에 둔다.
    /// 부채꼴(범위·발사체)은 Prefab의 Cone 자식에 실행 중 갱신하는 부채꼴 메시로 채움과 앞쪽 호를 그린다.
    /// 예고 중인 Persist는 최종 범위를 빨간 외곽선으로 두고 진행률만큼 안쪽을 채운다.
    /// </summary>
    public sealed class SpellEntityView : MonoBehaviour
    {
        private const float TRAIL_LENGTH = 24f;
        private const float MIN_SHAPE_RADIUS = 2.5f;
        private const float MIN_TRAIL_WIDTH = 2f;
        private const float INNER_SCALE = 0.6f;
        private const float ZONE_FILL_ALPHA = 0.12f;
        private const float BURST_FILL_ALPHA = 0.22f;
        private const float TRAIL_ALPHA = 0.25f;
        private const float LAUNCH_CONE_FILL_ALPHA = 0.6f;
        private const float CONE_EDGE_WIDTH = 2f;
        private const int CONE_SEGMENTS = 24;
        private const float WARNING_FILL_ALPHA = 0.25f;

        private static readonly Color WarningColor = new Color(1f, 0.25f, 0.25f);
        private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");

        [Header("모양")]
        [SerializeField] private Sprite _fireShape;
        [SerializeField] private Sprite _iceShape;
        [SerializeField] private Sprite _arcShape;
        [SerializeField] private Sprite _defaultShape;
        [SerializeField] private Sprite _squareShape;
        [SerializeField] private Sprite _ringShape;
        [SerializeField] private Sprite _squareOutlineShape;

        [Header("표시")]
        [SerializeField] private SpriteRenderer _trail;
        [SerializeField] private SpriteRenderer _shape;
        [SerializeField] private SpriteRenderer _fill;
        [SerializeField] private SpriteRenderer _outline;
        [SerializeField] private SpriteRenderer _inner;
        [SerializeField] private MeshFilter _coneFilter;
        [SerializeField] private MeshRenderer _coneRenderer;

        private readonly List<Vector3> _coneVertices = new List<Vector3>();
        private readonly List<Color> _coneColors = new List<Color>();
        private readonly List<int> _coneTriangles = new List<int>();
        private Mesh _coneMesh;

        void Awake()
        {
            _coneMesh = new Mesh { name = "SpellCone" };
            _coneMesh.MarkDynamic();
            _coneFilter.sharedMesh = _coneMesh;
            // 스프라이트 셰이더는 렌더러별 텍스처를 받는다. 메시는 지정하지 않으면 앞서 그린 스프라이트 텍스처가 남아 투명하게 그려질 수 있다.
            var block = new MaterialPropertyBlock();
            block.SetTexture(MainTextureId, Texture2D.whiteTexture);
            _coneRenderer.SetPropertyBlock(block);
        }

        void OnDestroy()
        {
            if (_coneMesh != null) Destroy(_coneMesh);
        }

        /// <summary>마법 개체의 형태·속성·크기·방향으로 맵 중앙(origin) 기준 월드 표시를 갱신한다.</summary>
        public void Apply(SimulationSpellEntity spell, SimVector origin)
        {
            transform.localPosition = MissionWorldSpace.ToWorld(spell.Position, origin);
            Color tint = RuneMesh.ElementColor(spell.Element);
            if (spell.IsExpandingRing || SpellBehaviorRuleRegistry.GetDisplayType(spell.Kind) == SpellDisplayType.Beam)
            {
                MissionWorldSpace.SetVisible(_trail, false);
                MissionWorldSpace.SetVisible(_shape, false);
                MissionWorldSpace.SetVisible(_fill, false);
                MissionWorldSpace.SetVisible(_outline, false);
                MissionWorldSpace.SetVisible(_inner, false);
                MissionWorldSpace.SetVisible(_coneRenderer, true);
                if (spell.IsExpandingRing) ApplyExplosionRing(spell, tint, origin);
                else ApplyBeam(spell.BeamPath, spell.Position, spell.BoxWidth, tint, origin);
                return;
            }
            float radius = (float)spell.Radius;
            float angle = MissionWorldSpace.ToWorldAngle(spell.Direction);
            // 행동 규칙이 없는 종류는 발사체로 그리고 부채꼴로 그리지 않는다.
            bool hasRules = SpellBehaviorRuleRegistry.TryGetByKind(spell.Kind, out SpellBehaviorRules rules);
            bool isArea = hasRules && rules.DisplayType != SpellDisplayType.Projectile;
            bool isCone = spell.IsCone && hasRules && (isArea || rules.MotionKind == SpellMotionKind.Moving);
            MissionWorldSpace.SetVisible(_trail, !isArea);
            MissionWorldSpace.SetVisible(_shape, !isArea && !isCone);
            MissionWorldSpace.SetVisible(_fill, isArea && !isCone);
            MissionWorldSpace.SetVisible(_outline, isArea && !isCone);
            MissionWorldSpace.SetVisible(_inner, isArea && !isCone);
            MissionWorldSpace.SetVisible(_coneRenderer, isCone);
            if (!isArea) PlaceTrail(spell, tint, radius);
            if (isCone) ApplyCone(spell, tint, angle, origin);
            else if (isArea) ApplyArea(spell, tint, radius, angle, origin);
            else ApplyProjectile(spell, tint, radius, angle);
        }

        /// <summary>명령의 최종 모양·크기를 생성 위치와 방향에 반투명 채움만으로 표시한다. 피해 개체는 생성하지 않는다.</summary>
        public void ApplyPreview(SpellAction action, SimVector position, SimVector direction, SimVector origin)
        {
            transform.localPosition = MissionWorldSpace.ToWorld(position, origin);
            bool isBox = action.MagicType == SpellGrammar.MAGIC_TYPE_BOX;
            bool isCone = action.MagicType == SpellGrammar.MAGIC_TYPE_CONE;
            MissionWorldSpace.SetVisible(_trail, false);
            MissionWorldSpace.SetVisible(_shape, false);
            MissionWorldSpace.SetVisible(_fill, !isCone);
            MissionWorldSpace.SetVisible(_outline, false);
            MissionWorldSpace.SetVisible(_inner, false);
            MissionWorldSpace.SetVisible(_coneRenderer, isCone);
            Color fill = RuneMesh.ElementColor(action.Element);
            fill.a = ZONE_FILL_ALPHA;
            float angle = MissionWorldSpace.ToWorldAngle(direction);
            if (isCone)
            {
                ApplyConeMesh(Vector2.zero, action.Stats.Radius, action.Stats.Radius, angle, action.ConeAngle, Color.clear, fill);
                return;
            }
            _fill.sprite = isBox ? _squareShape : _defaultShape;
            Vector2 center = isBox ? MissionWorldSpace.ToWorldDirection(direction).normalized * action.Stats.BoxLength * 0.5f : Vector2.zero;
            Vector2 dimensions = isBox ? new Vector2(action.Stats.BoxLength, action.Stats.BoxWidth) : Vector2.one * action.Stats.Radius * 2;
            MissionWorldSpace.Place(_fill, center, dimensions, fill);
            _fill.transform.localRotation = Quaternion.Euler(0, 0, isBox ? angle : 0);
        }

        /// <summary>
        /// 폭발·잔류를 원 또는 Beam 사각형의 채움, 외곽선, 안쪽 선으로 표시한다. Beam은 진행 방향(angle)으로 길이를 두고 BoxCenter에 둔다.
        /// 예고 중인 잔류는 빨간 외곽선과 진행률만큼의 채움만 그린다.
        /// </summary>
        private void ApplyArea(SimulationSpellEntity spell, Color tint, float radius, float angle, SimVector origin)
        {
            bool isWarning = spell.IsWarning;
            Color fill = isWarning ? WarningColor : tint;
            fill.a = isWarning ? WARNING_FILL_ALPHA
                : SpellBehaviorRuleRegistry.GetDisplayType(spell.Kind) == SpellDisplayType.LastingArea ? ZONE_FILL_ALPHA : BURST_FILL_ALPHA;
            _fill.sprite = spell.IsBox ? _squareShape : spell.Element == "ice" && !isWarning ? _iceShape : _defaultShape;
            _outline.sprite = _inner.sprite = spell.IsBox ? _squareOutlineShape : _ringShape;
            Quaternion rotation = Quaternion.Euler(0, 0, spell.IsBox ? angle : 0);
            if (spell.IsBox)
            {
                Vector2 center = (MissionWorldSpace.ToWorld(spell.BoxCenter, origin) - MissionWorldSpace.ToWorld(spell.Position, origin))
                    * MissionWorldSpace.PIXELS_PER_UNIT;
                Vector2 dimensions = new Vector2((float)spell.BoxLength, (float)spell.BoxWidth);
                MissionWorldSpace.Place(_fill, center, dimensions * (float)spell.WarnProgress, fill);
                MissionWorldSpace.Place(_outline, center, dimensions, isWarning ? WarningColor : tint);
                MissionWorldSpace.Place(_inner, center, dimensions * INNER_SCALE, fill * 2);
            }
            else
            {
                MissionWorldSpace.Place(_fill, Vector2.zero, radius * 2 * (float)spell.WarnProgress, fill);
                MissionWorldSpace.Place(_outline, Vector2.zero, radius * 2, isWarning ? WarningColor : tint);
                MissionWorldSpace.Place(_inner, Vector2.zero, radius * 2 * INNER_SCALE, fill * 2);
            }
            MissionWorldSpace.SetVisible(_inner, !isWarning);
            _fill.transform.localRotation = _outline.transform.localRotation = _inner.transform.localRotation = rotation;
        }

        /// <summary>발사·공전의 진행 방향 반대쪽에 속성 색 잔상을 둔다.</summary>
        private void PlaceTrail(SimulationSpellEntity spell, Color tint, float radius)
        {
            Vector2 direction = MissionWorldSpace.ToWorldDirection(spell.Direction);
            Color trail = tint; trail.a = TRAIL_ALPHA;
            MissionWorldSpace.PlaceLine(_trail, -direction * TRAIL_LENGTH, Vector2.zero, Mathf.Max(MIN_TRAIL_WIDTH, radius), trail);
        }

        /// <summary>공격 개체 없이 최초 빔의 복사된 경로·폭·속성을 표시한다.</summary>
        public void ApplyBeamFlash(SimulationBeamFlash flash, SimVector origin)
        {
            transform.localPosition = MissionWorldSpace.ToWorld(flash.Path[0], origin);
            MissionWorldSpace.SetVisible(_trail, false);
            MissionWorldSpace.SetVisible(_shape, false);
            MissionWorldSpace.SetVisible(_fill, false);
            MissionWorldSpace.SetVisible(_outline, false);
            MissionWorldSpace.SetVisible(_inner, false);
            MissionWorldSpace.SetVisible(_coneRenderer, true);
            ApplyBeam(flash.Path, flash.Path[0], flash.Width, RuneMesh.ElementColor(flash.Element), origin);
        }

        /// <summary>지정 중심선과 기준 위치·폭·색상으로 실제 빔 또는 표시용 빔의 선분 메시를 갱신한다.</summary>
        private void ApplyBeam(IReadOnlyList<SimVector> path, SimVector position, double width, Color tint, SimVector origin)
        {
            _coneVertices.Clear(); _coneColors.Clear(); _coneTriangles.Clear();
            Vector2 offset = MissionWorldSpace.ToWorld(position, origin);
            float halfWidth = MissionWorldSpace.ToWorldLength(width * 0.5);
            for (int index = 1; index < path.Count; index++)
            {
                Vector3 start = MissionWorldSpace.ToWorld(path[index - 1], origin) - offset;
                Vector3 end = MissionWorldSpace.ToWorld(path[index], origin) - offset;
                Vector3 direction = (end - start).normalized;
                Vector3 normal = new Vector3(-direction.y, direction.x, 0) * halfWidth;
                int vertex = _coneVertices.Count;
                _coneVertices.Add(start - normal); _coneVertices.Add(start + normal);
                _coneVertices.Add(end + normal); _coneVertices.Add(end - normal);
                for (int corner = 0; corner < 4; corner++) _coneColors.Add(tint);
                _coneTriangles.Add(vertex); _coneTriangles.Add(vertex + 1); _coneTriangles.Add(vertex + 2);
                _coneTriangles.Add(vertex); _coneTriangles.Add(vertex + 2); _coneTriangles.Add(vertex + 3);
            }
            _coneMesh.Clear();
            _coneMesh.SetVertices(_coneVertices);
            _coneMesh.SetColors(_coneColors);
            _coneMesh.SetTriangles(_coneTriangles, 0);
        }

        /// <summary>실제 파동 반경·두께로 띠를 표시하고 완전한 원은 끝점 마감 없이 닫는다.</summary>
        private void ApplyExplosionRing(SimulationSpellEntity spell, Color tint, SimVector origin)
        {
            _coneVertices.Clear(); _coneColors.Clear(); _coneTriangles.Clear();
            Vector2 offset = MissionWorldSpace.ToWorld(spell.Position, origin);
            float halfWidth = MissionWorldSpace.ToWorldLength(spell.ExplosionThickness * 0.5);
            int segments = RuneMesh.ExplosionSegmentCount(spell.ExplosionAngle);
            Vector3 center = MissionWorldSpace.ToWorld(spell.ExplosionCenter, origin) - offset;
            for (int index = 0; index <= segments; index++)
            {
                Vector3 point = MissionWorldSpace.ToWorld(spell.GetExplosionPoint(index / (double)segments), origin) - offset;
                Vector3 radial = point - center;
                Vector3 normal = radial.sqrMagnitude > 0 ? radial.normalized : (Vector3)MissionWorldSpace.ToWorldDirection(spell.Direction);
                _coneVertices.Add(point - normal * Mathf.Min(halfWidth, radial.magnitude));
                _coneVertices.Add(point + normal * halfWidth);
                _coneColors.Add(tint); _coneColors.Add(tint);
                if (index == 0) continue;
                int vertex = index * 2;
                _coneTriangles.Add(vertex - 2); _coneTriangles.Add(vertex - 1); _coneTriangles.Add(vertex + 1);
                _coneTriangles.Add(vertex - 2); _coneTriangles.Add(vertex + 1); _coneTriangles.Add(vertex);
            }
            if (spell.ExplosionAngle < 360)
            {
                AddExplosionCap(MissionWorldSpace.ToWorld(spell.GetExplosionPoint(0), origin) - offset, halfWidth, tint);
                AddExplosionCap(MissionWorldSpace.ToWorld(spell.GetExplosionPoint(1), origin) - offset, halfWidth, tint);
            }
            _coneMesh.Clear();
            _coneMesh.SetVertices(_coneVertices);
            _coneMesh.SetColors(_coneColors);
            _coneMesh.SetTriangles(_coneTriangles, 0);
        }

        /// <summary>호 끝점에 판정 반폭과 같은 반경의 원형 마감을 메시로 추가한다.</summary>
        private void AddExplosionCap(Vector3 center, float radius, Color tint)
        {
            int vertex = _coneVertices.Count;
            _coneVertices.Add(center); _coneColors.Add(tint);
            for (int index = 0; index <= CONE_SEGMENTS; index++)
            {
                float angle = index * 2 * Mathf.PI / CONE_SEGMENTS;
                _coneVertices.Add(center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius);
                _coneColors.Add(tint);
                if (index == 0) continue;
                _coneTriangles.Add(vertex); _coneTriangles.Add(vertex + index); _coneTriangles.Add(vertex + index + 1);
            }
        }

        /// <summary>발사·공전을 진행 방향의 속성별 도형(Beam이면 진행 방향으로 긴 사각형)으로 표시한다.</summary>
        private void ApplyProjectile(SimulationSpellEntity spell, Color tint, float radius, float angle)
        {
            _shape.sprite = spell.IsBox ? _squareShape : GetElementShape(spell.Element);
            if (spell.IsBox)
                MissionWorldSpace.Place(_shape, Vector2.zero, new Vector2((float)spell.BoxLength, (float)spell.BoxWidth), tint);
            else
                MissionWorldSpace.Place(_shape, Vector2.zero, Mathf.Max(MIN_SHAPE_RADIUS, radius) * 2, tint);
            _shape.transform.localRotation = Quaternion.Euler(0, 0, angle);
        }

        /// <summary>
        /// 부채꼴을 꼭짓점(ConeApex)에서 진행 방향(월드 각도, 도)으로 반경(ConeReach)만큼 펼친 채움과 앞쪽 호로 표시한다.
        /// 예고 중인 Persist는 빨간 호와 진행률만큼의 채움으로 표시한다.
        /// 발사체는 꼭짓점이 중심보다 뒤에 있어 개체 위치 기준 오프셋을 둔다.
        /// </summary>
        private void ApplyCone(SimulationSpellEntity spell, Color tint, float angle, SimVector origin)
        {
            bool isWarning = spell.IsWarning;
            if (isWarning) tint = WarningColor;
            Color fill = tint;
            SpellDisplayType displayType = SpellBehaviorRuleRegistry.GetDisplayType(spell.Kind);
            fill.a = isWarning ? WARNING_FILL_ALPHA : displayType == SpellDisplayType.LastingArea ? ZONE_FILL_ALPHA
                : displayType == SpellDisplayType.InstantArea ? BURST_FILL_ALPHA : LAUNCH_CONE_FILL_ALPHA;
            Vector2 apex = MissionWorldSpace.ToWorld(spell.ConeApex, origin) - MissionWorldSpace.ToWorld(spell.Position, origin);
            float reach = MissionWorldSpace.ToWorldLength(spell.ConeReach);
            float fillReach = reach * (float)spell.WarnProgress;
            ApplyConeMesh(apex, reach * MissionWorldSpace.PIXELS_PER_UNIT, fillReach * MissionWorldSpace.PIXELS_PER_UNIT,
                angle, (float)spell.ConeAngle, tint, fill);
        }

        /// <summary>꼭짓점과 픽셀 단위 반경·채움 반경, 방향·각도·색으로 부채꼴 메시를 갱신한다.</summary>
        private void ApplyConeMesh(Vector2 apex, float radius, float fillRadius, float angle, float coneAngle, Color tint, Color fill)
        {
            float reach = MissionWorldSpace.ToWorldLength(radius);
            float fillReach = MissionWorldSpace.ToWorldLength(fillRadius);
            float edge = Mathf.Min(reach, MissionWorldSpace.ToWorldLength(CONE_EDGE_WIDTH));
            float half = coneAngle * 0.5f;
            _coneVertices.Clear(); _coneColors.Clear(); _coneTriangles.Clear();
            _coneVertices.Add(apex); _coneColors.Add(fill);
            for (int i = 0; i <= CONE_SEGMENTS; i++)
            {
                float radians = (angle - half + coneAngle * i / CONE_SEGMENTS) * Mathf.Deg2Rad;
                Vector2 unit = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                _coneVertices.Add(apex + unit * fillReach); _coneColors.Add(fill);
                _coneVertices.Add(apex + unit * (reach - edge)); _coneColors.Add(tint);
                _coneVertices.Add(apex + unit * reach); _coneColors.Add(tint);
                if (i == 0) continue;
                int current = 1 + i * 3, previous = current - 3;
                _coneTriangles.Add(0); _coneTriangles.Add(previous); _coneTriangles.Add(current);
                _coneTriangles.Add(previous + 1); _coneTriangles.Add(previous + 2); _coneTriangles.Add(current + 2);
                _coneTriangles.Add(previous + 1); _coneTriangles.Add(current + 2); _coneTriangles.Add(current + 1);
            }
            _coneMesh.Clear();
            _coneMesh.SetVertices(_coneVertices);
            _coneMesh.SetColors(_coneColors);
            _coneMesh.SetTriangles(_coneTriangles, 0);
        }

        /// <summary>속성별 투사체 도형을 반환한다. 화염은 삼각형, 냉기는 육각형, 전격은 마름모, 그 외는 원이다.</summary>
        private Sprite GetElementShape(string element)
        {
            switch (element)
            {
                case "fire": return _fireShape;
                case "ice": return _iceShape;
                case "arc": return _arcShape;
                default: return _defaultShape;
            }
        }
    }
}
