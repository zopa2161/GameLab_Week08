using UnityEngine;

namespace RuneCode
{
    /// <summary>레골라스의 도약 높이·그림자와 고정 목표 세 곳의 예고·투사체·착탄을 표시한다.</summary>
    public sealed class LegolasBossView : MonoBehaviour
    {
        [Header("도형과 색")]
        [SerializeField] private Sprite _circle;
        [SerializeField] private Sprite _ring;
        [SerializeField] private Sprite _projectileShape;
        [SerializeField] private Color _warningTint = new Color(1f, 0.72f, 0.18f);
        [SerializeField] private Color _attackTint = new Color(0.7f, 1f, 0.35f);

        [Header("도약과 투사체")]
        [SerializeField, Min(0f)] private float _jumpHeight = 65f;
        [SerializeField, Min(0f)] private float _projectileArcHeight = 90f;
        [SerializeField, Min(1f)] private float _projectileRadius = 7f;
        [SerializeField, Min(1f)] private float _projectileStretch = 3f;

        private readonly SpriteRenderer[] _fills = new SpriteRenderer[LegolasBossState.VOLLEY_COUNT];
        private readonly SpriteRenderer[] _rings = new SpriteRenderer[LegolasBossState.VOLLEY_COUNT];
        private readonly SpriteRenderer[] _projectiles = new SpriteRenderer[LegolasBossState.VOLLEY_COUNT];
        private SpriteRenderer _shadow;

        /// <summary>개체의 시뮬레이션 목표·진행률로 도약 몸체와 원형 예고·비행·착탄 위치를 갱신한다.</summary>
        public void Apply(SimulationEnemy enemy, SpriteRenderer body, Transform bodyRoot)
        {
            if (_shadow == null) Initialize(body);
            LegolasBossState state = enemy.Legolas;
            float jumpArc = Mathf.Sin((float)state.JumpProgress * Mathf.PI);
            bodyRoot.localPosition = Vector3.up * (jumpArc * _jumpHeight / MissionWorldSpace.PIXELS_PER_UNIT);
            MissionWorldSpace.SetVisible(_shadow, state.IsAirborne);
            if (state.IsAirborne)
                MissionWorldSpace.Place(_shadow, Vector2.zero, (float)enemy.Radius * 2, new Color(0, 0, 0, 0.25f));

            for (int index = 0; index < LegolasBossState.VOLLEY_COUNT; index++)
            {
                bool hasTarget = state.HasAreaTargets;
                MissionWorldSpace.SetVisible(_fills[index], hasTarget);
                MissionWorldSpace.SetVisible(_rings[index], hasTarget);
                MissionWorldSpace.SetVisible(_projectiles[index], state.IsVolleyFlying);
                if (!hasTarget) continue;
                Vector2 point = LocalPoint(state.Targets[index], enemy.Position);
                Color tint = state.HasImpacted ? _attackTint : _warningTint;
                Color fillTint = tint;
                fillTint.a = state.HasImpacted ? 0.4f : 0.15f;
                float diameter = (float)state.Settings.AreaRadius * 2;
                MissionWorldSpace.Place(_fills[index], point, diameter, fillTint);
                MissionWorldSpace.Place(_rings[index], point, diameter, tint);
                if (!state.IsVolleyFlying) continue;
                float progress = (float)state.FlightProgress;
                float launchHeight = Mathf.Sin((float)state.VolleyLaunchJumpProgress * Mathf.PI) * _jumpHeight;
                Vector2 from = LocalPoint(state.VolleyOrigin, enemy.Position) + Vector2.up * launchHeight;
                Vector2 position = Vector2.Lerp(from, point, progress)
                    + Vector2.up * (Mathf.Sin(progress * Mathf.PI) * _projectileArcHeight);
                Vector2 tangent = point - from + Vector2.up * (Mathf.Cos(progress * Mathf.PI) * Mathf.PI * _projectileArcHeight);
                MissionWorldSpace.Place(_projectiles[index], position,
                    new Vector2(_projectileRadius * 2 * _projectileStretch, _projectileRadius * 2), _attackTint);
                _projectiles[index].transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg);
            }
        }

        /// <summary>원형 공격 세 곳과 그림자의 렌더러를 한 번 생성하고 몸체 재질·정렬을 적용한다.</summary>
        private void Initialize(SpriteRenderer body)
        {
            _shadow = CreateRenderer("JumpShadow", _circle, body, -5);
            for (int index = 0; index < LegolasBossState.VOLLEY_COUNT; index++)
            {
                _fills[index] = CreateRenderer("AreaFill" + index, _circle, body, -4);
                _rings[index] = CreateRenderer("AreaRing" + index, _ring, body, -2);
                _projectiles[index] = CreateRenderer("AreaProjectile" + index, _projectileShape, body, 3);
            }
        }

        /// <summary>이 개체 아래에 지정 도형·몸체 재질·상대 정렬 순서의 렌더러를 만들어 반환한다.</summary>
        private SpriteRenderer CreateRenderer(string label, Sprite sprite, SpriteRenderer body, int orderOffset)
        {
            GameObject child = new GameObject(label);
            child.transform.SetParent(transform, false);
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = body.sharedMaterial;
            renderer.sortingLayerID = body.sortingLayerID;
            renderer.sortingOrder = body.sortingOrder + orderOffset;
            return renderer;
        }

        /// <summary>시뮬레이션 좌표를 개체 기준 픽셀 표시 좌표로 변환한다.</summary>
        private static Vector2 LocalPoint(SimVector point, SimVector origin)
            => MissionWorldSpace.ToWorld(point, origin) * MissionWorldSpace.PIXELS_PER_UNIT;
    }
}
