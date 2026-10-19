using System;

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

        private static readonly Color CoreColor = new Color(0.04f, 0.09f, 0.13f);
        private static readonly Color DummyColor = new Color(0.52f, 0.69f, 0.74f);
        private static readonly Color WarningColor = new Color(1f, 0.78f, 0.23f);
        private static readonly Color AuraColor = new Color(0.36f, 0.84f, 0.44f, 0.22f);
        private static readonly Color ShieldColor = new Color(0.95f, 0.77f, 0.40f);
        private static readonly Color PatchColor = new Color(0.83f, 0.57f, 1f);
        private static readonly Color ResistColor = new Color(0.77f, 0.69f, 1f);
        private static readonly Color HpBackColor = new Color(0.24f, 0.16f, 0.20f);

        [Header("Prefab 식별")]
        [SerializeField] private string _enemyId = "enemy.scout";
        public string EnemyId => _enemyId;

        [Header("종류별 모양")]
        [SerializeField] private EnemyAppearance[] _appearances;
        [SerializeField] private Sprite _defaultShape;
        [SerializeField] private Color _defaultTint = new Color(0.94f, 0.34f, 0.40f);

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

        /// <summary>적 상태와 시뮬레이션의 시간·상태 이상·적응 정보로 맵 중앙(origin) 기준 월드 표시와 엘리트 외곽선의 맥동을 갱신한다.</summary>
        public void Apply(SimulationEnemy enemy, RuneSimulation sim, SimVector origin)
        {
            float radius = (float)enemy.Radius;
            transform.localPosition = MissionWorldSpace.ToWorld(enemy.Position, origin);
            _rotator.localRotation = Quaternion.Euler(0, 0, MissionWorldSpace.ToWorldAngle(enemy.Facing));
            EnemyAppearance appearance = FindAppearance(enemy.Kind);
            Color tint = appearance != null ? appearance.Tint : _defaultTint;
            if (enemy.IsDummy) tint = DummyColor;
            if (enemy.IsFlashing) tint = Color.white;
            _body.sprite = _core.sprite = appearance != null ? appearance.Shape : _defaultShape;
            MissionWorldSpace.Place(_body, Vector2.zero, radius * 2, tint);
            MissionWorldSpace.Place(_core, Vector2.zero, radius * 2 * CORE_SCALE, CoreColor);
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

            ApplyIcon(_burnIcon, sim.HasEnemyStatus(enemy, EnemyStatusType.Burn), new Vector2(-radius, radius + ICON_GAP), "fire");
            ApplyIcon(_chillIcon, sim.GetChillStacks(enemy) > 0 || sim.HasEnemyStatus(enemy, EnemyStatusType.Freeze), new Vector2(0, radius + ICON_GAP), "ice");
            ApplyIcon(_empIcon, sim.HasEnemyStatus(enemy, EnemyStatusType.Emp), new Vector2(radius, radius + ICON_GAP), "arc");
            double threshold = GameData.Balance.Adaptation.ResistanceThreshold;
            bool isResistant = sim.Adaptation.Enabled && (sim.Adaptation.GetValue(enemy.LastDamageElement) >= threshold || sim.Adaptation.GetValue(enemy.LastDamageForm) >= threshold);
            MissionWorldSpace.SetVisible(_resistRing, isResistant);
            if (isResistant) MissionWorldSpace.Place(_resistRing, new Vector2(radius + ICON_GAP, radius), ICON_RADIUS * 2, ResistColor);

            float width = Mathf.Max(HP_BAR_MIN_WIDTH, radius * 2);
            float barY = -radius - ICON_GAP + HP_BAR_HEIGHT * 0.5f;
            float ratio = Mathf.Clamp01((float)(enemy.Hp / enemy.MaxHp));
            MissionWorldSpace.PlaceLine(_hpBack, new Vector2(-width * 0.5f, barY), new Vector2(width * 0.5f, barY), HP_BAR_HEIGHT, HpBackColor);
            MissionWorldSpace.SetVisible(_hpFill, ratio > 0);
            if (ratio > 0) MissionWorldSpace.PlaceLine(_hpFill, new Vector2(-width * 0.5f, barY), new Vector2(-width * 0.5f + width * ratio, barY), HP_BAR_HEIGHT, tint);
            ApplyAdditionalIndicators(enemy, sim);
        }

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
