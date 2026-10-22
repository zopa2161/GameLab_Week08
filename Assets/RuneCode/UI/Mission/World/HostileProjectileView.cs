using UnityEngine;

namespace RuneCode
{
    /// <summary>적 탄환, 레골라스의 길쭉한 삼각 탄환과 보스 위험 장판(채움, 외곽 링, 사선 표시)을 표시한다.
    /// 예고 중에는 노란색으로 채우고 링이 장판 경계로 수축하며, 활성화되면 빨간색으로 바꾼다.</summary>
    public sealed class HostileProjectileView : MonoBehaviour
    {
        private const float TRAIL_LENGTH = 14f;
        private const float HAZARD_WARNING_ALPHA = 0.10f;
        private const float HAZARD_ACTIVE_ALPHA = 0.30f;
        private const float HAZARD_MARK_SCALE = 0.3f;
        private const float HAZARD_LINE_WIDTH = 2f;
        private const float HAZARD_RING_START_SCALE = 1.6f;

        private static readonly Color Tint = new Color(1f, 0.33f, 0.42f);
        private static readonly Color TrailColor = new Color(1f, 0.25f, 0.3f, 0.25f);
        private static readonly Color HazardWarningColor = new Color(1f, 0.8f, 0.25f);

        [Header("탄환")]
        [SerializeField] private SpriteRenderer _bullet;
        [SerializeField] private SpriteRenderer _trail;

        [Header("레골라스 저격 탄환")]
        [SerializeField] private Sprite _legolasProjectileShape;
        [SerializeField, Min(1f)] private float _legolasProjectileStretch = 3f;

        private Sprite _defaultBulletSprite;

        [Header("장판")]
        [SerializeField] private SpriteRenderer _hazardFill;
        [SerializeField] private SpriteRenderer _hazardRing;
        [SerializeField] private SpriteRenderer _hazardMark;

        void Awake()
        {
            _defaultBulletSprite = _bullet.sprite;
        }

        /// <summary>적 탄환 또는 장판의 위치·크기·예고 상태로 맵 중앙(origin) 기준 월드 표시를 갱신한다.</summary>
        public void Apply(SimulationProjectile projectile, SimVector origin)
        {
            transform.localPosition = MissionWorldSpace.ToWorld(projectile.Position, origin);
            float radius = (float)projectile.Radius;
            bool isHazard = projectile.IsHazard;
            MissionWorldSpace.SetVisible(_bullet, !isHazard);
            MissionWorldSpace.SetVisible(_trail, !isHazard);
            MissionWorldSpace.SetVisible(_hazardFill, isHazard);
            MissionWorldSpace.SetVisible(_hazardRing, isHazard);
            MissionWorldSpace.SetVisible(_hazardMark, isHazard);
            if (isHazard)
            {
                bool isWarning = projectile.IsWarning;
                Color hazardColor = isWarning ? HazardWarningColor : Tint;
                Color fill = hazardColor; fill.a = isWarning ? HAZARD_WARNING_ALPHA : HAZARD_ACTIVE_ALPHA;
                // 예고 중에는 링이 바깥에서 장판 경계로 수축해 피해 시작 시점을 알린다.
                float ringScale = isWarning ? Mathf.Lerp(HAZARD_RING_START_SCALE, 1f, (float)projectile.WarningProgress) : 1f;
                MissionWorldSpace.Place(_hazardFill, Vector2.zero, radius * 2, fill);
                MissionWorldSpace.Place(_hazardRing, Vector2.zero, radius * 2 * ringScale, hazardColor);
                MissionWorldSpace.PlaceLine(_hazardMark, -Vector2.one * radius * HAZARD_MARK_SCALE, Vector2.one * radius * HAZARD_MARK_SCALE, HAZARD_LINE_WIDTH, hazardColor);
                return;
            }
            bool isLegolasShot = projectile.OwnerBoss != null && projectile.OwnerBoss.IsLegolasPattern;
            _bullet.sprite = isLegolasShot ? _legolasProjectileShape : _defaultBulletSprite;
            _bullet.transform.localRotation = isLegolasShot
                ? Quaternion.Euler(0, 0, MissionWorldSpace.ToWorldAngle(projectile.Direction)) : Quaternion.identity;
            MissionWorldSpace.Place(_bullet, Vector2.zero,
                new Vector2(radius * 2 * (isLegolasShot ? _legolasProjectileStretch : 1), radius * 2), Tint);
            MissionWorldSpace.PlaceLine(_trail, -MissionWorldSpace.ToWorldDirection(projectile.Direction) * TRAIL_LENGTH, Vector2.zero, radius, TrailColor);
        }
    }
}
