using UnityEngine;

namespace RuneCode
{
    /// <summary>미션 플레이어의 몸체, 조준선, 대시 잔상과 보호막을 시뮬레이션 상태에 맞춰 표시한다. 루트는 조준 방향으로 회전한다.</summary>
    public sealed class PlayerView : MonoBehaviour
    {
        private const float BODY_RADIUS = 12f;
        private const float RING_RADIUS = 17f;
        private const float SHIELD_RADIUS = 23f;
        private const float AIM_LENGTH = 27f;
        private const float AIM_WIDTH = 3f;
        private const float DASH_TRAIL_LENGTH = 44f;
        private const float DASH_TRAIL_WIDTH = 14f;

        private static readonly Color BodyColor = new Color(0.35f, 0.91f, 0.99f);
        private static readonly Color DashColor = new Color(0.75f, 1f, 1f);
        private static readonly Color RingColor = new Color(0.2f, 0.75f, 0.93f, 0.5f);
        private static readonly Color AimColor = new Color(0.92f, 0.83f, 0.49f);
        private static readonly Color DashTrailColor = new Color(0.3f, 0.9f, 1f, 0.25f);
        private static readonly Color ShieldColor = new Color(0.65f, 0.53f, 1f);

        [Header("표시")]
        [SerializeField] private SpriteRenderer _body;
        [SerializeField] private SpriteRenderer _ring;
        [SerializeField] private SpriteRenderer _aimLine;
        [SerializeField] private SpriteRenderer _dashTrail;
        [SerializeField] private SpriteRenderer _shieldRing;

        /// <summary>플레이어 위치·조준 방향·대시·보호막을 맵 중앙(origin) 기준 월드 표시로 갱신한다.</summary>
        public void Apply(SimulationPlayer player, SimVector origin)
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
        }
    }
}
