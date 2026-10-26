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
