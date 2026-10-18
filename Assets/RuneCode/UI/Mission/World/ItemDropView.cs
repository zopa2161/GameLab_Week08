using UnityEngine;

namespace RuneCode
{
    /// <summary>바닥에 떨어진 소지 아이템(Modifier·속성·행동) 드롭을 정사각 코어와 흐린 링으로 표시한다. A·S 등급은 등장 순간 화면 밖까지 뻗는 등급색 삼각형 빛이 번쩍이고, 바닥에 있는 동안 짧은 삼각형 빛이 계속 회전하며 링이 퍼졌다 사라지기를 반복하고 빛 조각이 떠오른다.</summary>
    public sealed class ItemDropView : MonoBehaviour
    {
        private const float CORE_RADIUS = 7f;
        private const float RING_RADIUS = 10f;
        private const float RING_ALPHA = 0.35f;
        private const float RING_DARKEN = 0.8f;

        private const int RING_CYCLE_TICKS = 60;
        private const float RING_START_DIAMETER = 32f;
        private const float RING_END_DIAMETER = 128f;
        private const float RING_START_ALPHA = 0.5f;

        private const int RAY_TICKS = 144;
        private const float RAY_LENGTH_PX = 1800f;
        private const float RAY_IDLE_LENGTH_PX = 300f;
        private const float RAY_IDLE_ALPHA_SCALE = 0.2f;
        private const float RAY_MIN_ALPHA = 0.35f;
        private const float RAY_MAX_ALPHA = 0.55f;
        private const float RAY_HUE_VARIATION = 0.1f;
        private const float RAY_MIN_ANGLE = 5f;
        private const float RAY_MAX_ANGLE = 20f;
        private const float RAY_MAX_TOTAL_ANGLE = 150f;
        private const float RAY_ROTATION_PER_TICK = 0.75f;
        private const float RAY_WHITE_BLEND = 0.4f;
        private const float RAY_SPRITE_CANVAS_PX = 128f;
        private const float RAY_SPRITE_DEPTH_PX = 95f;
        private const float RAY_SPRITE_PIVOT_TO_APEX_PX = 63f;
        private const float RAY_SPRITE_BASE_HALF_PX = 55.5f;

        private const int MOTE_LIFETIME_TICKS = 90;
        private const int MOTE_PHASE_TICKS = 18;
        private const float MOTE_START_OFFSET = 6f;
        private const float MOTE_BASE_SIZE = 3.5f;
        private const float MOTE_X_SPACING = 3f;
        private const float S_GRADE_SATURATION_BOOST = 1.1f;
        private const float S_GRADE_VALUE_BOOST = 1.05f;

        private static readonly Color CoreColor = new Color(0.78f, 0.55f, 1f);
        private static readonly Color RingColor = new Color(0.62f, 0.4f, 0.95f, 0.45f);

        [Header("표시")]
        [SerializeField] private SpriteRenderer _core;
        [SerializeField] private SpriteRenderer _ring;

        [Header("이펙트")]
        [SerializeField] private SpriteRenderer _beam;
        [SerializeField] private SpriteRenderer _beamHighlight;
        [SerializeField] private SpriteRenderer[] _rays;
        [SerializeField] private SpriteRenderer[] _motes;

        /// <summary>
        /// 드롭 위치를 맵 중앙(origin) 기준 월드 위치로 옮기고 코어·링 크기와 색을 갱신한다.
        /// Modifier는 기존 보라색, 속성·행동은 룬 계열 색과 그 색을 어둡게 한 반투명 링을 쓴다.
        /// A·S 등급은 등장 순간 삼각형 빛이 번쩍이고 항상 링 순환과 빛 조각을 띄운다.
        /// C·B 등급은 모든 이펙트를 숨긴다.
        /// </summary>
        public void Apply(SimulationItemDrop drop, int elapsedTicks, SimVector origin)
        {
            transform.localPosition = MissionWorldSpace.ToWorld(drop.Position, origin);
            Color core = CoreColor;
            Color ring = RingColor;
            if (GameData.Runes.TryGet(drop.RuneId, out RuneDefinition rune)
                && (rune.Category == SpellGrammar.CATEGORY_ELEMENT || rune.Category == SpellGrammar.CATEGORY_BEHAVIOR))
            {
                core = RuneMesh.CategoryColor(rune.Category);
                ring = new Color(core.r * RING_DARKEN, core.g * RING_DARKEN, core.b * RING_DARKEN, RING_ALPHA);
            }
            MissionWorldSpace.Place(_core, Vector2.zero, CORE_RADIUS * 2, core);
            MissionWorldSpace.Place(_ring, Vector2.zero, RING_RADIUS * 2, ring);
            int gradeRank = drop.Grade == "A" ? 2 : drop.Grade == "S" ? 3 : -1;
            Color effectColor = gradeRank < 0 ? Color.white : RuneMesh.GradeColor(drop.Grade);
            if (gradeRank == 3)
            {
                // 드롭 이펙트에서만 S등급 황금색의 채도와 명도를 살짝 올린다. 값 상한은 1로 제한된다.
                Color.RGBToHSV(effectColor, out float goldenHue, out float goldenSaturation, out float goldenValue);
                effectColor = Color.HSVToRGB(goldenHue, Mathf.Clamp01(goldenSaturation * S_GRADE_SATURATION_BOOST),
                    Mathf.Clamp01(goldenValue * S_GRADE_VALUE_BOOST));
            }
            ApplyRingCycle(gradeRank, effectColor, elapsedTicks);
            ApplyRays(drop.SpawnTick, gradeRank, effectColor, elapsedTicks);
            ApplyMotes(gradeRank, effectColor, elapsedTicks);
        }

        /// <summary>바닥에 있는 동안 두 링이 반주기 어긋난 주기로 생성·확대·소멸을 반복하도록 크기와 알파를 갱신한다. A·S가 아니면 두 링을 숨긴다.</summary>
        private void ApplyRingCycle(int gradeRank, Color effectColor, int elapsedTicks)
        {
            if (gradeRank < 0)
            {
                MissionWorldSpace.SetVisible(_beam, false);
                MissionWorldSpace.SetVisible(_beamHighlight, false);
                return;
            }
            SpriteRenderer[] rings = { _beam, _beamHighlight };
            for (int index = 0; index < rings.Length; index++)
            {
                int phase = (elapsedTicks + index * (RING_CYCLE_TICKS / 2)) % RING_CYCLE_TICKS;
                float progress = phase / (float)RING_CYCLE_TICKS;
                Color tint = Color.Lerp(effectColor, Color.white, 0.35f);
                tint.a = RING_START_ALPHA * (1f - progress);
                MissionWorldSpace.SetVisible(rings[index], true);
                MissionWorldSpace.Place(rings[index], Vector2.zero, Mathf.Lerp(RING_START_DIAMETER, RING_END_DIAMETER, progress), tint);
            }
        }

        /// <summary>
        /// 등장 순간 아이템 중심을 한 꼭짓점으로 화면 밖까지 뻗는 삼각형 빛을 펼치고 서서히 줄어들며 옅아진 뒤,
        /// 이후에도 짧은 삼각형 무리가 계속 회전한다.
        /// 꼭짓각은 드롭 생성 틱에서 결정적으로 추출해 5~20도 범위에서 정하고 합이 상한(150도)을 넘으면 그 삼각형은 숨긴다.
        /// 시작 알파와 색조는 삼각형마다 범위 내 무작위로, 등급색(A 보라·S 황금) 계열을 벗어나지 않게 한다.
        /// A·S가 아니면 모두 숨긴다.
        /// </summary>
        private void ApplyRays(int spawnTick, int gradeRank, Color effectColor, int elapsedTicks)
        {
            if (gradeRank < 0)
            {
                foreach (SpriteRenderer ray in _rays) MissionWorldSpace.SetVisible(ray, false);
                return;
            }
            float burstBlend = Mathf.Clamp01(1f - elapsedTicks / (float)RAY_TICKS);
            float alphaScale = Mathf.Lerp(RAY_IDLE_ALPHA_SCALE, 1f, burstBlend);
            float lengthPx = Mathf.Lerp(RAY_IDLE_LENGTH_PX, RAY_LENGTH_PX, burstBlend);
            Color.RGBToHSV(effectColor, out float hue, out float saturation, out float value);
            float scaleX = lengthPx / RAY_SPRITE_DEPTH_PX;
            float rotation = elapsedTicks * RAY_ROTATION_PER_TICK;
            float totalAngle = 0f;
            for (int index = 0; index < _rays.Length; index++)
            {
                float apexAngle = Mathf.Lerp(RAY_MIN_ANGLE, RAY_MAX_ANGLE, PseudoRandom(spawnTick + index * 4));
                if (totalAngle + apexAngle > RAY_MAX_TOTAL_ANGLE)
                { MissionWorldSpace.SetVisible(_rays[index], false); continue; }
                totalAngle += apexAngle;
                Color tint = Color.HSVToRGB(Mathf.Repeat(hue + (PseudoRandom(spawnTick + index * 4 + 3) - 0.5f) * RAY_HUE_VARIATION, 1f), saturation, value);
                tint = Color.Lerp(tint, Color.white, RAY_WHITE_BLEND);
                tint.a = Mathf.Lerp(RAY_MIN_ALPHA, RAY_MAX_ALPHA, PseudoRandom(spawnTick + index * 4 + 1)) * alphaScale;
                float angle = 360f * PseudoRandom(spawnTick + index * 4 + 2) + rotation;
                float scaleY = scaleX * (RAY_SPRITE_DEPTH_PX / RAY_SPRITE_BASE_HALF_PX) * Mathf.Tan(apexAngle * 0.5f * Mathf.Deg2Rad);
                MissionWorldSpace.SetVisible(_rays[index], true);
                MissionWorldSpace.Place(_rays[index], Direction(angle) * (RAY_SPRITE_PIVOT_TO_APEX_PX * scaleX),
                    new Vector2(RAY_SPRITE_CANVAS_PX * scaleX, RAY_SPRITE_CANVAS_PX * scaleY), tint);
                _rays[index].transform.localRotation = Quaternion.Euler(0, 0, angle + 180f);
            }
        }

        /// <summary>등급색 빛 조각을 위상을 엇갈려 띄워 위로 천천히 떠오렸다 사라지는 것을 반복한다. S가 A보다 조각이 하나 많고 높고 진하게 뜬다. A·S가 아니면 모든 조각을 숨긴다.</summary>
        private void ApplyMotes(int gradeRank, Color effectColor, int elapsedTicks)
        {
            if (gradeRank < 0)
            {
                foreach (SpriteRenderer mote in _motes) MissionWorldSpace.SetVisible(mote, false);
                return;
            }
            bool isSuper = gradeRank == 3;
            int visibleCount = isSuper || _motes.Length < 2 ? _motes.Length : _motes.Length - 1;
            float rise = isSuper ? 26f : 22f;
            float maxAlpha = isSuper ? 0.65f : 0.55f;
            for (int index = 0; index < _motes.Length; index++)
            {
                SpriteRenderer mote = _motes[index];
                if (index >= visibleCount)
                { MissionWorldSpace.SetVisible(mote, false); continue; }
                int phase = (elapsedTicks + index * MOTE_PHASE_TICKS) % MOTE_LIFETIME_TICKS;
                float progress = phase / (float)MOTE_LIFETIME_TICKS;
                float x = (index % 2 == 0 ? -1f : 1f) * (2f + index * MOTE_X_SPACING);
                float y = MOTE_START_OFFSET + progress * rise;
                float size = MOTE_BASE_SIZE + index % 2;
                Color tint = Color.Lerp(effectColor, Color.white, 0.35f);
                tint.a = Mathf.Sin(Mathf.PI * progress) * maxAlpha;
                MissionWorldSpace.SetVisible(mote, true);
                MissionWorldSpace.Place(mote, new Vector2(x, y), size, tint);
            }
        }

        /// <summary>정수 시드에서 0 이상 1 미만의 결정적 값을 만든다. 삼각형 꼭짓각과 시작 각도를 드롭 생성 틱에서 고정하기 위해 쓴다.</summary>
        private static float PseudoRandom(int seed)
        {
            uint state = (uint)seed * 2654435761u;
            state ^= state >> 13;
            state *= 1274126177u;
            state ^= state >> 16;
            return (state & 0xFFFFFF) / (float)0x1000000;
        }

        /// <summary>각도(도)에 대응하는 단위 방향 벡터를 반환한다.</summary>
        private static Vector2 Direction(float degrees)
            => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
    }
}
