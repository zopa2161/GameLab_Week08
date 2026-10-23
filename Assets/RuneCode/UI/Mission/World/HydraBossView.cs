using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>Hydra의 도형 머리·목·공격 예고·독 구체·잔류 지대·고리 파동을 시뮬레이션 상태로 표시한다.</summary>
    public sealed class HydraBossView : MonoBehaviour
    {
        private const float HEAD_RADIUS = 13f;
        private const float REST_NECK_WIDTH = 12f;
        private const int WAVE_SEGMENTS = 96;
        private const float GEOMETRY_EPSILON = 0.000001f;

        [Header("도형과 색")]
        [SerializeField] private Sprite _circle;
        [SerializeField] private Sprite _square;
        [SerializeField] private Sprite _ring;
        [SerializeField] private Color _tint = new Color(0.08f, 0.78f, 0.72f);
        [SerializeField] private Color _warningTint = new Color(1f, 0.72f, 0.18f);
        [SerializeField] private Color _poisonTint = new Color(0.5f, 0.95f, 0.2f);

        [Header("도약 연출")]
        [SerializeField] private float _jumpHeight = 110f;
        [SerializeField] private float _landingSquashSeconds = 0.18f;

        private readonly SpriteRenderer[] _heads = new SpriteRenderer[HydraBossState.HEAD_COUNT];
        private readonly SpriteRenderer[] _necks = new SpriteRenderer[HydraBossState.HEAD_COUNT];
        private readonly SpriteRenderer[] _neckWarnings = new SpriteRenderer[HydraBossState.HEAD_COUNT];
        private readonly List<SpriteRenderer> _poisonFills = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _poisonRings = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _poisonOrbs = new List<SpriteRenderer>();
        private readonly List<LineRenderer> _waveRings = new List<LineRenderer>();
        private readonly Vector3[] _wavePoints = new Vector3[WAVE_SEGMENTS];
        private SpriteRenderer _spinArea;
        private SpriteRenderer _targetFill;
        private SpriteRenderer _targetRing;
        private SpriteRenderer _rangeRing;
        private SpriteRenderer _shadow;
        private SpriteRenderer _chargeOrb;
        private SpriteRenderer _poisonAim;
        private bool _isInitialized;

        /// <summary>시뮬레이션의 벽 판정과 몸체 재질·정렬을 재사용해 목·돌진 예고·잔류물을 표시하고 도약 높이를 적용한다.</summary>
        public void Apply(SimulationEnemy enemy, RuneSimulation simulation, SpriteRenderer body, Transform bodyRoot)
        {
            if (!_isInitialized) Initialize(body);
            HydraBossState state = enemy.Hydra;
            HydraDefinition settings = state.Settings;
            float jumpArc = Mathf.Sin((float)state.JumpProgress * Mathf.PI);
            Vector2 lift = Vector2.up * (jumpArc * _jumpHeight);
            bodyRoot.localPosition = (Vector3)(lift / MissionWorldSpace.PIXELS_PER_UNIT);
            MissionWorldSpace.SetVisible(_shadow, state.Attack == HydraAttack.Stomp);
            MissionWorldSpace.Place(_shadow, Vector2.zero, (float)enemy.Radius * 2 * (1f - 0.25f * jumpArc), new Color(0, 0, 0, 0.25f));
            bool isSpin = state.Attack == HydraAttack.Spin;
            for (int index = 0; index < _heads.Length; index++)
            {
                Vector2 end = LocalPoint(state.GetNeckEnd(enemy, index, simulation), enemy.Position);
                bool isAttackNeck = state.Attack == HydraAttack.Stretch && index < HydraBossState.STRETCH_HEAD_COUNT
                    || isSpin && index < HydraBossState.SPIN_HEAD_COUNT;
                MissionWorldSpace.PlaceLine(_necks[index], lift, end + lift,
                    isAttackNeck ? (float)settings.NeckWidth : REST_NECK_WIDTH, enemy.IsFlashing ? Color.white : _tint);
                MissionWorldSpace.Place(_heads[index], end + lift, HEAD_RADIUS * 2, enemy.IsFlashing ? Color.white : _tint);
                bool isWarning = state.IsNeckWarning && isAttackNeck && !isSpin;
                MissionWorldSpace.SetVisible(_neckWarnings[index], isWarning);
                if (isWarning)
                    MissionWorldSpace.PlaceLine(_neckWarnings[index], Vector2.zero,
                        LocalPoint(state.GetNeckEnd(enemy, index, simulation, true), enemy.Position), (float)settings.NeckWidth, WithAlpha(_warningTint, 0.05f));
            }
            RenderSpinWarning(enemy);
            bool hasRange = state.IsStompWarning;
            MissionWorldSpace.SetVisible(_rangeRing, hasRange);
            if (hasRange) MissionWorldSpace.Place(_rangeRing, LocalPoint(state.JumpTarget, enemy.Position),
                (float)settings.WaveRadius * 2, WithAlpha(_warningTint, 0.4f));
            bool hasTarget = state.IsStompWarning;
            MissionWorldSpace.SetVisible(_targetFill, hasTarget);
            MissionWorldSpace.SetVisible(_targetRing, hasTarget);
            if (hasTarget)
            {
                Vector2 point = LocalPoint(state.JumpTarget, enemy.Position);
                float radius = (float)enemy.Radius;
                MissionWorldSpace.Place(_targetFill, point, radius * 2, WithAlpha(_warningTint, 0.1f));
                MissionWorldSpace.Place(_targetRing, point, radius * 2, _warningTint);
            }
            MissionWorldSpace.SetVisible(_chargeOrb, state.IsPoisonWarning);
            MissionWorldSpace.SetVisible(_poisonAim, state.IsPoisonWarning);
            if (state.IsPoisonWarning)
            {
                Vector2 origin = LocalPoint(state.PoisonOrigin, enemy.Position);
                MissionWorldSpace.Place(_chargeOrb, origin,
                    (float)(settings.PoisonOrbRadius * 2 * state.PoisonChargeProgress), _poisonTint);
                MissionWorldSpace.PlaceLine(_poisonAim, origin, LocalPoint(state.PoisonTarget, enemy.Position),
                    (float)settings.PoisonOrbRadius * 2, WithAlpha(_warningTint, 0.05f));
            }
            RenderPoisons(enemy, body);
            RenderWaves(enemy, body);
            if (state.Attack == HydraAttack.Stomp)
            {
                double local = state.StompLocalTime;
                float squash = 1f;
                if (local < settings.StompWindup)
                    squash = 1f - 0.2f * (float)(local / settings.StompWindup);
                else if (state.IsAirborne) squash = 1f + 0.12f * jumpArc;
                else
                {
                    float landingAge = (float)(local - settings.StompWindup - settings.StompApproachSeconds);
                    squash = 1f - 0.3f * (1f - Mathf.Clamp01(landingAge / _landingSquashSeconds));
                }
                body.transform.localScale = new Vector3(body.transform.localScale.x / squash, body.transform.localScale.y * squash, 1);
            }
        }

        /// <summary>최초 표시 때 고정 개수의 머리·목·예고 렌더러를 생성해 이후 프레임에서 재사용한다.</summary>
        private void Initialize(SpriteRenderer body)
        {
            for (int index = 0; index < _heads.Length; index++)
            {
                _heads[index] = CreateRenderer("Head" + index, _circle, body, 2);
                _necks[index] = CreateRenderer("Neck" + index, _square, body, -1);
                _neckWarnings[index] = CreateRenderer("NeckWarning" + index, _square, body, -3);
            }
            _targetFill = CreateRenderer("PoisonTargetFill", _circle, body, -4);
            _targetRing = CreateRenderer("PoisonTargetRing", _ring, body, -2);
            _rangeRing = CreateRenderer("AttackRange", _ring, body, -2);
            _shadow = CreateRenderer("JumpShadow", _circle, body, -5);
            _chargeOrb = CreateRenderer("PoisonCharge", _circle, body, 3);
            _poisonAim = CreateRenderer("PoisonAim", _square, body, -3);
            _spinArea = CreateRenderer("SpinDashArea", _square, body, -3);
            _isInitialized = true;
        }

        /// <summary>준비 중에만 돌진 전체 경로를 직사각형으로 예고하고 돌진 시작 시 범위 표시를 숨긴다.</summary>
        private void RenderSpinWarning(SimulationEnemy enemy)
        {
            HydraBossState state = enemy.Hydra;
            bool isSpin = state.Attack == HydraAttack.Spin;
            bool isWarning = isSpin && state.IsNeckWarning && state.SpinDashDuration > GEOMETRY_EPSILON;
            MissionWorldSpace.SetVisible(_spinArea, isWarning);
            if (!isWarning) return;
            float width = (float)state.GetSpinHalfExtent(enemy) * 2;
            float length = width + (float)(state.SpinTarget - state.SpinOrigin).Length;
            Vector2 center = LocalPoint((state.SpinOrigin + state.SpinTarget) * 0.5, enemy.Position);
            MissionWorldSpace.Place(_spinArea, center, new Vector2(length, width), WithAlpha(_warningTint, 0.05f));
            _spinArea.transform.localRotation = Quaternion.Euler(0, 0, MissionWorldSpace.ToWorldAngle(state.SpinDirection));
        }

        /// <summary>반사 구체를 시뮬레이션 위치와 판정 반경으로 표시하고 명중 후에는 같은 위치의 독 장판으로 전환한다.</summary>
        private void RenderPoisons(SimulationEnemy enemy, SpriteRenderer body)
        {
            HydraBossState state = enemy.Hydra;
            HydraDefinition settings = state.Settings;
            for (int index = 0; index < state.Poisons.Count; index++)
            {
                HydraPoison poison = state.Poisons[index];
                SpriteRenderer fill = GetRenderer(_poisonFills, index, "PoisonFill", _circle, body, -4);
                SpriteRenderer ring = GetRenderer(_poisonRings, index, "PoisonRing", _ring, body, -2);
                SpriteRenderer orb = GetRenderer(_poisonOrbs, index, "PoisonOrb", _circle, body, 3);
                MissionWorldSpace.SetVisible(fill, poison.HasLanded);
                MissionWorldSpace.SetVisible(ring, poison.HasLanded);
                MissionWorldSpace.SetVisible(orb, !poison.HasLanded);
                Vector2 point = LocalPoint(poison.Position, enemy.Position);
                if (poison.HasLanded)
                {
                    MissionWorldSpace.Place(fill, point, (float)settings.PoisonRadius * 2, WithAlpha(_poisonTint, 0.22f));
                    MissionWorldSpace.Place(ring, point, (float)settings.PoisonRadius * 2, _poisonTint);
                }
                else
                {
                    MissionWorldSpace.Place(orb, point, (float)settings.PoisonOrbRadius * 2, _poisonTint);
                }
            }
            HideFrom(_poisonFills, state.Poisons.Count);
            HideFrom(_poisonRings, state.Poisons.Count);
            HideFrom(_poisonOrbs, state.Poisons.Count);
        }

        /// <summary>착지 위치에 고정된 확장 파동을 현재 판정 반경과 같은 크기의 고리로 표시한다.</summary>
        private void RenderWaves(SimulationEnemy enemy, SpriteRenderer body)
        {
            HydraBossState state = enemy.Hydra;
            for (int index = 0; index < state.Waves.Count; index++)
            {
                HydraWave wave = state.Waves[index];
                LineRenderer ring = GetWaveRenderer(index, body);
                Vector2 center = LocalPoint(wave.Center, enemy.Position);
                float radius = (float)wave.GetRadius(state.Settings, enemy.Radius);
                for (int pointIndex = 0; pointIndex < WAVE_SEGMENTS; pointIndex++)
                {
                    float angle = pointIndex * Mathf.PI * 2 / WAVE_SEGMENTS;
                    _wavePoints[pointIndex] = (center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius) / MissionWorldSpace.PIXELS_PER_UNIT;
                }
                ring.widthMultiplier = (float)state.Settings.WaveWidth / MissionWorldSpace.PIXELS_PER_UNIT;
                ring.startColor = ring.endColor = _warningTint;
                ring.SetPositions(_wavePoints);
            }
            HideFrom(_waveRings, state.Waves.Count);
        }

        /// <summary>기준 렌더러의 재질과 정렬층을 공유하는 자식 도형 렌더러를 생성한다.</summary>
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

        /// <summary>목록의 기존 렌더러를 활성화해 반환하고 필요한 개수만 추가 생성한다.</summary>
        private SpriteRenderer GetRenderer(List<SpriteRenderer> views, int index, string label, Sprite sprite, SpriteRenderer body, int orderOffset)
        {
            while (views.Count <= index) views.Add(CreateRenderer(label + views.Count, sprite, body, orderOffset));
            MissionWorldSpace.SetVisible(views[index], true);
            return views[index];
        }

        /// <summary>판정 두께와 일치하는 고리 파동용 선 렌더러를 필요한 수만 생성하고 재사용한다.</summary>
        private LineRenderer GetWaveRenderer(int index, SpriteRenderer body)
        {
            while (_waveRings.Count <= index)
            {
                GameObject child = new GameObject("StompWave" + _waveRings.Count);
                child.transform.SetParent(transform, false);
                LineRenderer ring = child.AddComponent<LineRenderer>();
                ring.sharedMaterial = body.sharedMaterial;
                ring.sortingLayerID = body.sortingLayerID;
                ring.sortingOrder = body.sortingOrder + 1;
                ring.useWorldSpace = false;
                ring.loop = true;
                ring.positionCount = WAVE_SEGMENTS;
                _waveRings.Add(ring);
            }
            MissionWorldSpace.SetVisible(_waveRings[index], true);
            return _waveRings[index];
        }

        /// <summary>현재 사용하지 않는 목록 뒤쪽 렌더러를 숨긴다.</summary>
        private static void HideFrom<T>(List<T> views, int count) where T : Component
        { for (int index = count; index < views.Count; index++) MissionWorldSpace.SetVisible(views[index], false); }

        /// <summary>월드 시뮬레이션 위치를 보스 기준 표시 픽셀 좌표로 변환한다.</summary>
        private static Vector2 LocalPoint(SimVector point, SimVector origin)
            => new Vector2((float)(point.X - origin.X), -(float)(point.Y - origin.Y));

        /// <summary>원본 색의 알파만 교체한 표시 색을 반환한다.</summary>
        private static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }
    }
}
