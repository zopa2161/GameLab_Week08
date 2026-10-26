using UnityEngine;

namespace RuneCode
{
    /// <summary>미션 플레이어의 몸체, 조준선, 대시 잔상, 보호막과 강화 장판을 시뮬레이션 상태에 맞춰 표시한다. 루트는 조준 방향으로 회전한다.</summary>
    public sealed class PlayerView : MonoBehaviour
    {
        private const float BODY_RADIUS = 12f;
        private const float RING_RADIUS = 17f;
        private const float SHIELD_RADIUS = 23f;
        private const float AIM_LENGTH = 27f;
        private const float AIM_WIDTH = 3f;
        private const float DASH_TRAIL_LENGTH = 44f;
        private const float DASH_TRAIL_WIDTH = 14f;
        private const int AURA_FILL_ORDER = 6;
        private const int AURA_EDGE_ORDER = 7;
        private const float AURA_FILL_ALPHA = 0.07f;
        private const float AURA_FILL_PULSE_ALPHA = 0.05f;
        private const float AURA_EDGE_ALPHA = 0.35f;
        private const float AURA_EDGE_PULSE_ALPHA = 0.35f;
        private const float AURA_PULSE_SHRINK = 0.04f;
        private const int LEVEL_UP_GLOW_LAYERS = 5;
        private const int LEVEL_UP_RINGS = 3;
        private const int LEVEL_UP_RING_SEGMENTS = 40;
        private const int LEVEL_UP_SPARKS = 16;

        private static readonly Color BodyColor = new Color(0.35f, 0.91f, 0.99f);
        private static readonly Color DashColor = new Color(0.75f, 1f, 1f);
        private static readonly Color RingColor = new Color(0.2f, 0.75f, 0.93f, 0.5f);
        private static readonly Color AimColor = new Color(0.92f, 0.83f, 0.49f);
        private static readonly Color DashTrailColor = new Color(0.3f, 0.9f, 1f, 0.25f);
        private static readonly Color ShieldColor = new Color(0.65f, 0.53f, 1f);
        private static readonly Color AuraColor = new Color(0.75f, 1f, 1f);

        [Header("표시")]
        [SerializeField] private SpriteRenderer _body;
        [SerializeField] private SpriteRenderer _ring;
        [SerializeField] private SpriteRenderer _aimLine;
        [SerializeField] private SpriteRenderer _dashTrail;
        [SerializeField] private SpriteRenderer _shieldRing;

        [Header("강화 장판")]
        [SerializeField] private Sprite _auraFillSprite;
        [SerializeField] private Sprite _auraEdgeSprite;

        private SpriteRenderer _auraFill;
        private SpriteRenderer _auraEdge;
        private Transform _levelUpRoot;
        private Mesh _levelUpMesh;
        private UnityEngine.UI.VertexHelper _levelUpVertices;

        void OnDestroy()
        {
            _levelUpVertices?.Dispose();
            if (_levelUpMesh != null) Destroy(_levelUpMesh);
        }

        void OnDisable()
        {
            if (_levelUpRoot != null) _levelUpRoot.gameObject.SetActive(false);
        }

        /// <summary>0~1 진행률과 픽셀 높이로 화면 위까지 솟는 황금 광선·고리·입자를 그린다. 범위 밖이면 숨긴다.</summary>
        public void ApplyLevelUp(float progress, float ascentHeight = 720f)
        {
            bool isVisible = progress >= 0f && progress < 1f;
            if (!isVisible)
            {
                if (_levelUpRoot != null) _levelUpRoot.gameObject.SetActive(false);
                return;
            }
            if (_levelUpRoot == null) CreateLevelUpEffect();
            _levelUpRoot.gameObject.SetActive(true);
            _levelUpRoot.rotation = Quaternion.identity;
            float fade = Mathf.Sin(progress * Mathf.PI);
            Color gold = new Color(1f, 0.73f, 0.16f, fade);
            Color light = new Color(1f, 0.96f, 0.65f, fade);
            float height = Mathf.Max(240f, ascentHeight);
            float beamRise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.18f));
            _levelUpVertices.Clear();
            for (int index = 0; index < LEVEL_UP_GLOW_LAYERS; index++)
            {
                float width = 84f - index * 14f;
                Color glow = gold;
                glow.a = fade * (0.06f + index * 0.02f);
                RuneMesh.Rect(_levelUpVertices, new Rect(-width * 0.5f, -8f, width, height * beamRise), glow);
            }
            for (int index = 0; index < LEVEL_UP_RINGS; index++)
            {
                float rise = Mathf.Clamp01(progress * 1.4f - index * 0.15f);
                Color ring = gold;
                ring.a *= 1f - rise;
                Vector2 center = new Vector2(0f, -12f + rise * height * 0.85f);
                float radius = 24f + rise * 24f;
                for (int segment = 0; segment < LEVEL_UP_RING_SEGMENTS; segment++)
                {
                    float angle = segment * Mathf.PI * 2f / LEVEL_UP_RING_SEGMENTS;
                    float next = (segment + 1) * Mathf.PI * 2f / LEVEL_UP_RING_SEGMENTS;
                    Vector2 from = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.35f);
                    Vector2 to = center + new Vector2(Mathf.Cos(next) * radius, Mathf.Sin(next) * radius * 0.35f);
                    RuneMesh.Line(_levelUpVertices, from, to, 1.5f, ring);
                }
            }
            for (int index = 0; index < LEVEL_UP_SPARKS; index++)
            {
                float rise = Mathf.Repeat(progress * 1.25f + index * 0.173f, 1f);
                float x = Mathf.Sin(index * 2.4f + progress * 2f) * (24f + index % 4 * 6f);
                Vector2 point = new Vector2(x, -15f + rise * height);
                Color spark = light;
                spark.a *= Mathf.Sin(rise * Mathf.PI);
                RuneMesh.Line(_levelUpVertices, point - Vector2.up * (16f + index % 3 * 8f), point, 1.5f, spark);
                RuneMesh.Polygon(_levelUpVertices, point, 1.3f + index % 3 * 0.4f, spark, 4);
            }
            RuneMesh.Ring(_levelUpVertices, Vector2.zero, 18f + progress * 46f, 2.5f, new Color(1f, 0.86f, 0.32f, fade * (1f - progress)));
            _levelUpVertices.FillMesh(_levelUpMesh);
        }

        /// <summary>몸체 재질을 공유하는 황금빛 연출용 메시를 한 번 생성하고 픽셀 좌표 크기로 표시한다.</summary>
        private void CreateLevelUpEffect()
        {
            var effect = new GameObject("PlayerLevelUp", typeof(MeshFilter), typeof(MeshRenderer));
            _levelUpRoot = effect.transform;
            _levelUpRoot.SetParent(transform, false);
            _levelUpRoot.localScale = Vector3.one / MissionWorldSpace.PIXELS_PER_UNIT;
            _levelUpMesh = new Mesh { name = "PlayerLevelUp" };
            _levelUpMesh.MarkDynamic();
            _levelUpVertices = new UnityEngine.UI.VertexHelper();
            effect.GetComponent<MeshFilter>().sharedMesh = _levelUpMesh;
            MeshRenderer renderer = effect.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _body.sharedMaterial;
            renderer.sortingLayerID = _body.sortingLayerID;
            renderer.sortingOrder = _shieldRing.sortingOrder + 1;
            var block = new MaterialPropertyBlock();
            block.SetTexture("_MainTex", Texture2D.whiteTexture);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>플레이어 위치·조준 방향·대시·보호막과 장판(hasAura, 시뮬레이션 시각 time)을 맵 중앙(origin) 기준 월드 표시로 갱신한다.</summary>
        public void Apply(SimulationPlayer player, SimVector origin, bool hasAura, double time)
        {
            _body.enabled = true;
            _ring.enabled = true;
            _aimLine.enabled = true;
            _dashTrail.enabled = true;
            _shieldRing.enabled = true;
            transform.localPosition = MissionWorldSpace.ToWorld(player.Position, origin);
            transform.localRotation = Quaternion.Euler(0, 0, MissionWorldSpace.ToWorldAngle(player.AimDirection));
            bool isDashing = player.DashRemaining > 0;
            MissionWorldSpace.Place(_body, Vector2.zero, BODY_RADIUS * 2, isDashing ? DashColor : BodyColor);
            MissionWorldSpace.Place(_ring, Vector2.zero, RING_RADIUS * 2, RingColor);
            MissionWorldSpace.PlaceLine(_aimLine, Vector2.zero, new Vector2(AIM_LENGTH, 0), AIM_WIDTH, AimColor);
            MissionWorldSpace.SetVisible(_dashTrail, isDashing);
            if (isDashing) MissionWorldSpace.PlaceLine(_dashTrail, new Vector2(-DASH_TRAIL_LENGTH, 0), Vector2.zero, DASH_TRAIL_WIDTH, DashTrailColor);
            MissionWorldSpace.SetVisible(_shieldRing, player.Shield > 0);
            if (player.Shield > 0) MissionWorldSpace.Place(_shieldRing, Vector2.zero, SHIELD_RADIUS * 2, ShieldColor);
            ApplyAura(hasAura, time);
        }

        /// <summary>장판이 켜져 있으면 반경을 옅게 채우고 가장자리 링을 그리며, 피해 주기마다 링이 밝아졌다가 바깥으로 퍼지게 한다.</summary>
        private void ApplyAura(bool hasAura, double time)
        {
            if (!hasAura)
            {
                if (_auraFill != null) SetAuraVisible(false);
                return;
            }
            if (_auraFill == null)
            {
                _auraFill = CreateAuraRenderer("AuraFill", _auraFillSprite, AURA_FILL_ORDER);
                _auraEdge = CreateAuraRenderer("AuraEdge", _auraEdgeSprite, AURA_EDGE_ORDER);
            }
            SetAuraVisible(true);
            CombatBalance combat = GameData.Balance.Combat;
            // 시뮬레이션 시각을 피해 주기로 나눈 위상으로 맥동을 근사한다. 주기 직후 1에서 다음 주기 직전 0으로 줄어든다.
            float pulse = 1f - (float)(time % combat.AuraTickInterval / combat.AuraTickInterval);
            pulse *= pulse;
            float diameter = combat.AuraRadius * 2;
            Color fill = AuraColor; fill.a = AURA_FILL_ALPHA + AURA_FILL_PULSE_ALPHA * pulse;
            Color edge = AuraColor; edge.a = AURA_EDGE_ALPHA + AURA_EDGE_PULSE_ALPHA * pulse;
            MissionWorldSpace.Place(_auraFill, Vector2.zero, diameter, fill);
            MissionWorldSpace.Place(_auraEdge, Vector2.zero, diameter * (1f - AURA_PULSE_SHRINK * pulse), edge);
        }

        /// <summary>몸체 렌더러의 재질과 정렬층을 공유하는 장판용 자식 렌더러를 지정 정렬 순서로 만든다.</summary>
        private SpriteRenderer CreateAuraRenderer(string label, Sprite sprite, int sortingOrder)
        {
            GameObject child = new GameObject(label);
            child.transform.SetParent(transform, false);
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = _body.sharedMaterial;
            renderer.sortingLayerID = _body.sortingLayerID;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        /// <summary>장판 채움과 가장자리 렌더러의 표시 여부를 함께 바꾼다.</summary>
        private void SetAuraVisible(bool isVisible)
        {
            MissionWorldSpace.SetVisible(_auraFill, isVisible);
            MissionWorldSpace.SetVisible(_auraEdge, isVisible);
        }

        /// <summary>패배 진행률에 따라 몸체를 붉게 물들이고 소멸시키며 조준선·보호막·대시 표시를 숨긴다.</summary>
        public void ApplyDefeat(float progress)
        {
            Color tint = Color.Lerp(BodyColor, Color.red, Mathf.Clamp01(progress * 3f));
            tint.a = 1f - Mathf.Clamp01((progress - 0.35f) / 0.25f);
            _body.color = tint;
            _ring.enabled = false;
            _aimLine.enabled = false;
            _dashTrail.enabled = false;
            _shieldRing.enabled = false;
            if (_auraFill != null) SetAuraVisible(false);
        }
    }
}
