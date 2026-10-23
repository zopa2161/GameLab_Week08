using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RuneCode
{
    /// <summary>
    /// 컴파일 검증 결과 하나다. 문법 엔진은 표시 문구를 만들지 않고 코드·노드·상세값·원인만 전달하며,
    /// 사용자 문구는 UI 계층의 CompileIssueText가 현지화해 만든다.
    /// </summary>
    public sealed class CompileIssue
    {
        private readonly string _code;
        private readonly string _nodeId;
        private readonly string _detail;
        private readonly CompileIssue _cause;
        public string Code => _code;
        public string NodeId => _nodeId;
        public string Detail => _detail;
        public CompileIssue Cause => _cause;

        /// <summary>검증 코드, 관련 노드, 문구에 덧붙일 상세값과 하위 원인 진단을 저장한다.</summary>
        public CompileIssue(string code, string nodeId = null, string detail = null, CompileIssue cause = null)
        {
            _code = code;
            _nodeId = nodeId;
            _detail = detail;
            _cause = cause;
        }
    }

    public sealed class SpellModifierValues
    {
        private static readonly SpellModifierValues _none = new SpellModifierValues(1f, 1f, 1f, 1f, 1f, 0, 0f, 0f, 0, 0f);
        private readonly float _damageMultiplier;
        private readonly float _radiusMultiplier;
        private readonly float _speedMultiplier;
        private readonly float _durationMultiplier;
        private readonly float _rangeMultiplier;
        private readonly int _pierce;
        private readonly float _homingTurn;
        private readonly float _homingRange;
        private readonly int _orbitTargetCount;
        private readonly float _spreadAngle;
        private readonly float _knockbackDistance;
        private readonly float _knockbackSeconds;
        public static SpellModifierValues None => _none;
        public float DamageMultiplier => _damageMultiplier;
        /// <summary>확장 효과의 면적 배율이다. 실제 길이 배율 변환은 행동 수치를 만들 때 수행한다.</summary>
        public float RadiusMultiplier => _radiusMultiplier;
        public float SpeedMultiplier => _speedMultiplier;
        public float DurationMultiplier => _durationMultiplier;
        public float RangeMultiplier => _rangeMultiplier;
        public int Pierce => _pierce;
        public float HomingTurn => _homingTurn;
        public float HomingRange => _homingRange;

        /// <summary>유도 단계가 공전 경로에 함께 돌 대상 수다. 유도 단계가 없으면 0이다.</summary>
        public int OrbitTargetCount => _orbitTargetCount;
        public float SpreadAngle => _spreadAngle;
        public float KnockbackDistance => _knockbackDistance;
        public float KnockbackSeconds => _knockbackSeconds;

        /// <summary>시전 시 적용할 수식 배율과 관통·유도 값을 보관한다.</summary>
        public SpellModifierValues(float damageMultiplier, float radiusMultiplier, float speedMultiplier,
            float durationMultiplier, float rangeMultiplier, int pierce, float homingTurn, float homingRange,
            int orbitTargetCount, float spreadAngle, float knockbackDistance = 0f, float knockbackSeconds = 0f)
        {
            _damageMultiplier = damageMultiplier;
            _radiusMultiplier = radiusMultiplier;
            _speedMultiplier = speedMultiplier;
            _durationMultiplier = durationMultiplier;
            _rangeMultiplier = rangeMultiplier;
            _pierce = pierce;
            _homingTurn = homingTurn;
            _homingRange = homingRange;
            _orbitTargetCount = orbitTargetCount;
            _spreadAngle = spreadAngle;
            _knockbackDistance = knockbackDistance;
            _knockbackSeconds = knockbackSeconds;
        }

        /// <summary>수식 컨텍스트의 수치 값을 결정성 상태 해시에 기록한다.</summary>
        public void AppendState(StringBuilder state)
        {
            state.Append(_damageMultiplier.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(_radiusMultiplier.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(_speedMultiplier.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(_durationMultiplier.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(_rangeMultiplier.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(_pierce).Append('|').Append(_homingTurn.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(_homingRange.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(_orbitTargetCount).Append('|')
                .Append(_spreadAngle.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (_knockbackDistance > 0f)
                state.Append(FormattableString.Invariant($"|knockback:{_knockbackDistance:R}:{_knockbackSeconds:R}"));
        }

        /// <summary>중첩 SpellCall의 수식 효과를 곱셈·가산 규칙에 따라 결합한다.</summary>
        public SpellModifierValues Combine(SpellModifierValues other)
        {
            return new SpellModifierValues(_damageMultiplier * other._damageMultiplier,
                _radiusMultiplier * other._radiusMultiplier, _speedMultiplier * other._speedMultiplier,
                _durationMultiplier * other._durationMultiplier, _rangeMultiplier * other._rangeMultiplier,
                AddPierce(_pierce, other._pierce), Math.Max(_homingTurn, other._homingTurn), Math.Max(_homingRange, other._homingRange),
                Math.Max(_orbitTargetCount, other._orbitTargetCount), Math.Max(_spreadAngle, other._spreadAngle),
                Math.Max(_knockbackDistance, other._knockbackDistance), Math.Max(_knockbackSeconds, other._knockbackSeconds));
        }

        /// <summary>연결된 Modifier와 문자열 등급 수치로 호출부 수식 효과를 계산한다.</summary>
        public static SpellModifierValues From(IReadOnlyList<RuneDefinition> modifiers, IReadOnlyList<GraphNode> modifierNodes,
            ModifierGradeTable modifierGrades = null)
        {
            float damage = 1f;
            float radius = 1f;
            float speed = 1f;
            float duration = 1f;
            float range = 1f;
            int pierce = 0;
            float homingTurn = 0f;
            float homingRange = 0f;
            int orbitTargetCount = 0;
            float spreadAngle = 0f;
            float knockbackDistance = 0f;
            float knockbackSeconds = 0f;
            for (int index = 0; index < modifiers.Count; index++)
            {
                RuneDefinition modifier = modifiers[index];
                GraphNode node = modifierNodes == null ? null : modifierNodes[index];
                RuneStats stats = modifier.Stats;
                string grade = SpellGrammar.GetModifierGrade(modifier, node, modifierGrades);
                damage *= GradeNumber(modifierGrades, modifier.Id, grade, ModifierGradeTable.EFFECT_MULTIPLIER_EFFECT, stats.DamageMultiplier);
                radius *= GradeNumber(modifierGrades, modifier.Id, grade, ModifierGradeTable.RADIUS_MULTIPLIER_EFFECT, stats.RadiusMultiplier);
                speed *= GradeNumber(modifierGrades, modifier.Id, grade, ModifierGradeTable.SPEED_MULTIPLIER_EFFECT, stats.SpeedMultiplier);
                speed *= modifierGrades?.GetAmplifySpeedMultiplier(modifier.Id, grade) ?? 1f;
                duration *= GradeNumber(modifierGrades, modifier.Id, grade, ModifierGradeTable.DURATION_MULTIPLIER_EFFECT, stats.DurationMultiplier);
                int pierceCount = modifierGrades != null && modifierGrades.GetEffectType(modifier.Id) == ModifierGradeTable.PIERCE_COUNT_EFFECT
                    ? modifierGrades.GetPierceCount(modifier.Id, grade, stats.Pierce) : stats.Pierce;
                pierce = AddPierce(pierce, pierceCount);
                if (modifierGrades != null && modifierGrades.GetEffectType(modifier.Id) == ModifierGradeTable.HOMING_TIER_EFFECT)
                {
                    string tier = modifierGrades.GetGradeValue(modifier.Id, grade) as string;
                    HomingTierDefinition homing = modifier.FindHomingTier(tier);
                    if (homing != null)
                    {
                        homingTurn = Math.Max(homingTurn, homing.HomingTurn);
                        homingRange = Math.Max(homingRange, homing.HomingRange);
                        orbitTargetCount = Math.Max(orbitTargetCount, homing.OrbitTargetCount);
                    }
                }
                else
                {
                    homingTurn = Math.Max(homingTurn, stats.HomingTurn);
                    homingRange = Math.Max(homingRange, stats.HomingRange);
                }
                spreadAngle = Math.Max(spreadAngle, stats.SpreadAngle);
                knockbackDistance = Math.Max(knockbackDistance,
                    GradeNumber(modifierGrades, modifier.Id, grade, ModifierGradeTable.KNOCKBACK_DISTANCE_EFFECT, stats.KnockbackDistance));
                knockbackSeconds = Math.Max(knockbackSeconds, stats.KnockbackSeconds);
            }
            return new SpellModifierValues(damage, radius, speed, duration, range, pierce, homingTurn, homingRange,
                orbitTargetCount, spreadAngle, knockbackDistance, knockbackSeconds);
        }

        /// <summary>해당 효과 종류의 등급 숫자를 가져오거나 Modifier의 고정 기본값을 반환한다.</summary>
        internal static float GradeNumber(ModifierGradeTable modifierGrades, string modifierId, string grade,
            string effectType, float fallback)
        {
            return modifierGrades != null && modifierGrades.GetEffectType(modifierId) == effectType
                && modifierGrades.TryGetNumberValue(modifierId, grade, out float value) ? value : fallback;
        }

        /// <summary>관통 수를 합산하고 무제한 상한보다 커지면 상한에서 멈춘다.</summary>
        internal static int AddPierce(int left, int right)
        {
            if (right > 0 && left > SpellGrammar.UNLIMITED_PIERCE_COUNT - right) return SpellGrammar.UNLIMITED_PIERCE_COUNT;
            int sum = left + right;
            return sum >= SpellGrammar.UNLIMITED_PIERCE_THRESHOLD ? SpellGrammar.UNLIMITED_PIERCE_COUNT : sum;
        }
    }

    public sealed class SpellEventScope
    {
        private readonly IReadOnlyList<SpellAction> _onHit;
        private readonly IReadOnlyList<SpellAction> _onExpire;
        private readonly IReadOnlyList<SpellAction> _onFirstHitOrExpire;
        private readonly SpellEventScope _parent;
        private readonly SpellModifierValues _parentModifiers;
        private readonly double _parentCostMultiplier;
        public IReadOnlyList<SpellAction> OnHit => _onHit;
        public IReadOnlyList<SpellAction> OnExpire => _onExpire;
        public IReadOnlyList<SpellAction> OnFirstHitOrExpire => _onFirstHitOrExpire;
        public SpellEventScope Parent => _parent;
        public SpellModifierValues ParentModifiers => _parentModifiers;

        /// <summary>호출 노드 쪽 이벤트 분기를 실행할 때 노드 비용에 곱하는 배율(호출 노드가 속한 문맥의 배율)이다.</summary>
        public double ParentCostMultiplier => _parentCostMultiplier;

        /// <summary>
        /// 호출 노드의 적중·소멸·첫 적중 또는 소멸(onFirstHitOrExpire) 분기와 외부 호출 문맥(효과 값, 비용 배율)을 불변 범위로 묶는다.
        /// </summary>
        public SpellEventScope(IReadOnlyList<SpellAction> onHit, IReadOnlyList<SpellAction> onExpire,
            IReadOnlyList<SpellAction> onFirstHitOrExpire, SpellEventScope parent, SpellModifierValues parentModifiers,
            double parentCostMultiplier = 1d)
        {
            _onHit = onHit;
            _onExpire = onExpire;
            _onFirstHitOrExpire = onFirstHitOrExpire;
            _parent = parent;
            _parentModifiers = parentModifiers ?? SpellModifierValues.None;
            _parentCostMultiplier = parentCostMultiplier;
        }

        /// <summary>호출 범위 체인의 이벤트 노드 ID를 상태 해시 버퍼에 기록한다.</summary>
        public void AppendState(StringBuilder state)
        {
            for (SpellEventScope scope = this; scope != null; scope = scope.Parent)
            {
                foreach (SpellAction action in scope.OnHit) state.Append("|h:").Append(action.NodeId);
                foreach (SpellAction action in scope.OnExpire) state.Append("|x:").Append(action.NodeId);
                foreach (SpellAction action in scope.OnFirstHitOrExpire) state.Append("|c:").Append(action.NodeId);
                state.Append('|').Append(scope._parentCostMultiplier.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|');
                scope.ParentModifiers.AppendState(state);
            }
        }
    }

    public sealed class SpellStats
    {
        private readonly float _damage;
        private readonly float _speed;
        private readonly float _speedMultiplier;
        private readonly float _radiusMultiplier;
        private readonly bool _isExpandingRing;
        private readonly bool _isSlash;
        private readonly float _radius;
        private readonly float _shapeRadius;
        private readonly float _boxWidth;
        private readonly float _boxLength;
        private readonly float _lifetime;
        private readonly float _minimumLifetime;
        private readonly float _offset;
        private readonly float _orbitRadius;
        private readonly float _hitInterval;
        private readonly float _tickInterval;
        private readonly int _pierce;
        private readonly float _pierceLoss;
        private readonly float _homingTurn;
        private readonly float _homingRange;
        private readonly int _orbitTargetCount;
        private readonly float _arcRange;
        private readonly int _arcTargets;
        private readonly float _arcMultiplier;
        private readonly float _learningMultiplier;
        private readonly float _spreadAngle;
        private readonly float _knockbackDistance;
        private readonly float _knockbackSeconds;
        public float Damage => _damage;
        public float Speed => _speed;

        /// <summary>속도 수식의 누적 배율이다. 잔류는 예고 시간을 이 배율로 나눈다.</summary>
        public float SpeedMultiplier => _speedMultiplier;
        /// <summary>면적 배율의 제곱근으로 계산한 실제 길이 배율이다.</summary>
        public float RadiusMultiplier => _radiusMultiplier;
        public float Radius => _radius;
        public float ShapeRadius => _shapeRadius;
        public float BoxWidth => _boxWidth;
        public float BoxLength => _boxLength;
        public float Lifetime => _lifetime;
        public float MinimumLifetime => _minimumLifetime;
        public float Offset => _offset;
        public float OrbitRadius => _orbitRadius;
        public float HitInterval => _hitInterval;
        public float TickInterval => _tickInterval;
        public int Pierce => _pierce;
        public float PierceLoss => _pierceLoss;
        public float HomingTurn => _homingTurn;
        public float HomingRange => _homingRange;

        /// <summary>유도 단계가 공전 경로에 함께 돌 대상 수다. 유도 단계가 없으면 0이다.</summary>
        public int OrbitTargetCount => _orbitTargetCount;
        public float ArcRange => _arcRange;
        public int ArcTargets => _arcTargets;
        public float ArcMultiplier => _arcMultiplier;
        public float LearningMultiplier => _learningMultiplier;
        public float SpreadAngle => _spreadAngle;
        public float KnockbackDistance => _knockbackDistance;
        public float KnockbackSeconds => _knockbackSeconds;

        /// <summary>형태·속성·수식을 결합하고 확장 면적 배율을 길이 배율로 변환해 효과 수치를 만든다. 고정 길이 빔은 폭만 면적 배율로 늘린다.</summary>
        public SpellStats(RuneStats form, RuneStats element, IReadOnlyList<RuneDefinition> mods, string formKind,
            IReadOnlyList<GraphNode> modifierNodes = null, ModifierGradeTable modifierGrades = null,
            string magicType = SpellGrammar.MAGIC_TYPE_SPHERE, float shapeRadius = -1f, float boxWidth = -1f,
            float boxLength = -1f)
        {
            float damageMult = 1f;
            float radiusMult = 1f;
            float rangeMult = 1f;
            float orbitRadiusMult = 1f;
            float orbitRadius = 0f;
            float speedMult = 1f;
            float durationMult = 1f;
            _learningMultiplier = 1f;
            SpellBehaviorRuleRegistry.TryGetByKind(formKind, out SpellBehaviorRules rules);
            _isExpandingRing = rules?.DisplayType == SpellDisplayType.ExpandingRing;
            _isSlash = formKind == SlashBehaviorRules.KIND;
            // 다중의 발사 전용 개수·퍼짐 값은 지팡이 앞에서 생성되는 행동(발사·빔)이 쓴다.
            bool usesLaunchGradeValues = rules != null && rules.SpawnOrigin == SpellSpawnOrigin.Wand;
            for (int index = 0; index < mods.Count; index++)
            {
                RuneDefinition mod = mods[index];
                GraphNode modifierNode = modifierNodes == null ? null : modifierNodes[index];
                RuneStats stats = mod.Stats;
                string grade = SpellGrammar.GetModifierGrade(mod, modifierNode, modifierGrades);
                if (mod.Id == OrbitModifierRules.RUNE_ID) orbitRadius = stats.OrbitRadius;
                orbitRadiusMult *= SpellModifierValues.GradeNumber(modifierGrades, mod.Id, grade, ModifierGradeTable.ORBIT_PATH_EFFECT, 1f);
                damageMult *= SpellModifierValues.GradeNumber(modifierGrades, mod.Id, grade, ModifierGradeTable.EFFECT_MULTIPLIER_EFFECT, stats.DamageMultiplier);
                radiusMult *= SpellModifierValues.GradeNumber(modifierGrades, mod.Id, grade, ModifierGradeTable.RADIUS_MULTIPLIER_EFFECT, stats.RadiusMultiplier);
                speedMult *= SpellModifierValues.GradeNumber(modifierGrades, mod.Id, grade, ModifierGradeTable.SPEED_MULTIPLIER_EFFECT, stats.SpeedMultiplier);
                speedMult *= modifierGrades?.GetAmplifySpeedMultiplier(mod.Id, grade) ?? 1f;
                durationMult *= SpellModifierValues.GradeNumber(modifierGrades, mod.Id, grade, ModifierGradeTable.DURATION_MULTIPLIER_EFFECT, stats.DurationMultiplier);
                // 관통·유도 수치는 장착 가능 여부(_modifierTargets, 모드 규칙 CanAttachTo)가 허용한 행동에만 들어온다.
                int pierceCount = modifierGrades != null && modifierGrades.GetEffectType(mod.Id) == ModifierGradeTable.PIERCE_COUNT_EFFECT
                    ? modifierGrades.GetPierceCount(mod.Id, grade, stats.Pierce) : stats.Pierce;
                _pierce = SpellModifierValues.AddPierce(_pierce, pierceCount);
                if (stats.PierceLoss > 0f) _pierceLoss = stats.PierceLoss;
                if (modifierGrades != null && modifierGrades.TryGetPierceDamageMultiplier(mod.Id, grade, out float pierceMultiplier))
                    _pierceLoss = 1f - pierceMultiplier;
                // 유도는 발사체의 방향 전환, 잔류의 대상 추적 이동, 빔 경로 휨, 공전 위 직선 추적에 쓴다.
                if (modifierGrades != null && modifierGrades.GetEffectType(mod.Id) == ModifierGradeTable.HOMING_TIER_EFFECT)
                {
                    string tier = modifierGrades.GetGradeValue(mod.Id, grade) as string;
                    HomingTierDefinition homing = mod.FindHomingTier(tier);
                    if (homing != null)
                    {
                        _homingTurn = homing.HomingTurn;
                        _homingRange = homing.HomingRange;
                        _orbitTargetCount = homing.OrbitTargetCount;
                    }
                }
                else
                {
                    if (stats.HomingTurn > 0f) _homingTurn = stats.HomingTurn;
                    if (stats.HomingRange > 0f) _homingRange = stats.HomingRange;
                }
                _learningMultiplier *= stats.LearningMultiplier;
                _knockbackDistance = Math.Max(_knockbackDistance,
                    SpellModifierValues.GradeNumber(modifierGrades, mod.Id, grade, ModifierGradeTable.KNOCKBACK_DISTANCE_EFFECT, stats.KnockbackDistance));
                _knockbackSeconds = Math.Max(_knockbackSeconds, stats.KnockbackSeconds);
                // 지팡이 앞 행동(발사·빔)은 등급표의 발사 전용 퍼짐 각도가 있으면 그 값을 쓴다.
                if (usesLaunchGradeValues && modifierGrades != null
                    && modifierGrades.TryGetLaunchSpreadAngle(mod.Id, grade, out float launchSpread)) _spreadAngle = launchSpread;
                else if (stats.SpreadAngle > 0f) _spreadAngle = stats.SpreadAngle;
            }
            float lengthMult = (float)Math.Sqrt(radiusMult);
            _damage = form.Damage * damageMult;
            _speed = form.Speed * speedMult;
            if (_isExpandingRing) _speed *= lengthMult;
            _speedMultiplier = speedMult;
            _radiusMultiplier = lengthMult;
            _shapeRadius = (shapeRadius >= 0f ? shapeRadius : form.Radius) * lengthMult;
            _boxWidth = (boxWidth >= 0f ? boxWidth : form.Radius * 2f)
                * (_isSlash ? 1f : formKind == BeamBehaviorRules.BEAM_KIND ? radiusMult : lengthMult);
            _boxLength = (boxLength >= 0f ? boxLength : form.Radius * 2f) * lengthMult;
            if (formKind == BeamBehaviorRules.BEAM_KIND) _boxLength = form.BeamLength;
            _radius = magicType == SpellGrammar.MAGIC_TYPE_SPHERE || magicType == SpellGrammar.MAGIC_TYPE_CONE
                ? _shapeRadius : form.Radius * lengthMult;
            _lifetime = rules != null ? rules.GetLifetime(form.Lifetime, rangeMult, speedMult, durationMult) : form.Lifetime * durationMult;
            if (rules != null)
            {
                // 동작 모드가 행동의 수명 규칙을 바꿀 수 있다(발사 + 공전은 사거리 대신 기본 수명).
                for (int index = 0; index < mods.Count; index++)
                    if (SpellModifierRuleRegistry.TryGetByRuneId(mods[index].Id, out SpellModifierRules modifierRules))
                        _lifetime = modifierRules.ModifyLifetime(mods[index], rules, _lifetime, form.Lifetime, durationMult);
            }
            _minimumLifetime = form.MinimumLifetime;
            _offset = form.Offset * rangeMult;
            // 공전 룬의 공통 반경에 등급 배율을 적용한다. 확장 배율은 적용하지 않는다.
            _orbitRadius = orbitRadius * rangeMult * orbitRadiusMult;
            _hitInterval = form.HitInterval;
            _tickInterval = form.TickInterval;
            if (element != null)
            {
                _arcRange = element.ArcRange;
                _arcTargets = element.ArcTargets;
                _arcMultiplier = element.ArcMultiplier;
            }
        }

        /// <summary>SpellCall의 상속 수식을 기본 효과 수치에 적용한 새 읽기 전용 통계를 반환한다.</summary>
        public SpellStats Apply(SpellModifierValues modifiers) => new SpellStats(this, modifiers);

        /// <summary>호출부의 확장 면적 배율을 길이로 변환해 기존 수치에 곱하고 관통·유도 값을 적용한다. 참격은 두께를 유지하고 고정 길이 빔은 폭만 늘린다.</summary>
        private SpellStats(SpellStats source, SpellModifierValues modifiers)
        {
            float lengthMult = (float)Math.Sqrt(modifiers.RadiusMultiplier);
            _damage = source._damage * modifiers.DamageMultiplier;
            _isExpandingRing = source._isExpandingRing;
            _isSlash = source._isSlash;
            _speed = source._speed * modifiers.SpeedMultiplier;
            if (_isExpandingRing) _speed *= lengthMult;
            _speedMultiplier = source._speedMultiplier * modifiers.SpeedMultiplier;
            _radiusMultiplier = source._radiusMultiplier * lengthMult;
            _shapeRadius = source._shapeRadius * lengthMult;
            _boxWidth = _isSlash ? source._boxWidth : source._boxWidth * lengthMult;
            _boxLength = source._boxLength * lengthMult;
            _radius = source._radius * lengthMult;
            _lifetime = source._lifetime * modifiers.DurationMultiplier * modifiers.RangeMultiplier;
            if (_isExpandingRing) _lifetime /= modifiers.SpeedMultiplier;
            if (_isSlash) _lifetime = source._lifetime / modifiers.SpeedMultiplier;
            _minimumLifetime = source._minimumLifetime;
            if (_minimumLifetime > 0)
            {
                _lifetime = source._lifetime;
                _boxWidth = source._boxWidth * modifiers.RadiusMultiplier;
                _boxLength = source._boxLength;
            }
            _offset = source._offset * modifiers.RangeMultiplier;
            _orbitRadius = source._orbitRadius * modifiers.RangeMultiplier;
            _hitInterval = source._hitInterval;
            _tickInterval = source._tickInterval;
            _pierce = SpellModifierValues.AddPierce(source._pierce, modifiers.Pierce);
            _pierceLoss = source._pierceLoss;
            _homingTurn = Math.Max(source._homingTurn, modifiers.HomingTurn);
            _homingRange = Math.Max(source._homingRange, modifiers.HomingRange);
            _orbitTargetCount = Math.Max(source._orbitTargetCount, modifiers.OrbitTargetCount);
            _arcRange = source._arcRange;
            _arcTargets = source._arcTargets;
            _arcMultiplier = source._arcMultiplier;
            _learningMultiplier = source._learningMultiplier;
            _spreadAngle = source._spreadAngle;
            _knockbackDistance = Math.Max(source._knockbackDistance, modifiers.KnockbackDistance);
            _knockbackSeconds = Math.Max(source._knockbackSeconds, modifiers.KnockbackSeconds);
        }

    }

    public sealed class SpellCondition
    {
        private readonly string _type;
        private readonly float _pct;
        private readonly string _status;
        private readonly float _px;
        public string Type => _type;
        public float Pct => _pct;
        public string Status => _status;
        public float Px => _px;

        /// <summary>조건 노드의 기본값과 편집 값을 읽어 런타임 조건으로 변환한다.</summary>
        public SpellCondition(GraphNode node, RuneDefinition rune)
        {
            foreach (ParameterDefinition param in rune.Params)
            {
                switch (param.Id)
                {
                    case "type": _type = node.GetText("type", param.DefaultText); break;
                    case "pct": _pct = node.GetNumber("pct", param.DefaultNumber); break;
                    case "status": _status = node.GetText("status", param.DefaultText); break;
                    case "px": _px = node.GetNumber("px", param.DefaultNumber); break;
                }
            }
        }
    }

    public sealed class SpellAction
    {
        private const double ON_HIT_ENERGY_MULTIPLIER = 1.5d;

        private readonly string _kind;
        private readonly string _nodeId;
        private readonly string _magicType;
        private readonly string _form;
        private readonly string _element;
        private readonly string _sourceElement;
        private readonly string _calledSpellId;
        private readonly bool _isNoise;
        private readonly int _count;
        private readonly int _waveCount = 1;
        private readonly float _waveInterval;
        private readonly float _beamEmpowerMultiplier = 1f;
        private readonly float _beamEmpowerManaMultiplier = 1f;
        private readonly float _beamEmpowerSizeMultiplier = 1f;
        private readonly SpellStats _stats;
        private readonly IReadOnlyList<string> _mods;
        private readonly IReadOnlyList<SpellModifierRules> _modifierRules;
        private readonly float _seconds;
        private readonly int _times;
        private readonly float _interval;
        private readonly SpellCondition _condition;
        private readonly float _shieldAmount;
        private readonly float _shieldSeconds;
        private readonly float _power;
        private readonly float _buffDuration;
        private readonly float _executionSeconds;
        private readonly RuneDefinition _shapeDefinition;
        private readonly ResourceCostSet _baseNodeCosts;
        private readonly ResourceCostSet _ownCosts;
        private readonly ResourceCostSet _nodeCosts;
        private readonly float _energyMultiplier;
        private readonly float _coneAngle;
        private readonly SpellModifierValues _modifierValues;
        private readonly CompiledSpell _calledSpell;
        private readonly SpellBehaviorRules _rules;
        private readonly SpellSpawnOrigin _spawnOrigin;

        private IReadOnlyList<SpellAction> _onHit = new List<SpellAction>();
        private IReadOnlyList<SpellAction> _onExpire = new List<SpellAction>();
        private IReadOnlyList<SpellAction> _onFirstHitOrExpire = new List<SpellAction>();
        private IReadOnlyList<SpellAction> _then = new List<SpellAction>();
        private IReadOnlyList<SpellAction> _else = new List<SpellAction>();
        private IReadOnlyList<SpellAction> _body = new List<SpellAction>();
        private IReadOnlyList<SpellAction> _onComplete = new List<SpellAction>();
        private IReadOnlyList<SpellAction> _next = new List<SpellAction>();
        private IReadOnlyList<string> _attachedNodeIds = new List<string>();
        public string Kind => _kind;
        public string NodeId => _nodeId;
        public string MagicType => _magicType;
        public string Form => _form;
        public string Element => _element;
        public string SourceElement => _sourceElement;
        public string CalledSpellId => _calledSpellId;
        public bool Noise => _isNoise;
        public int Count => _count;

        /// <summary>연속 모드의 실행 회차 수다. 연속 모드가 없으면 1이다.</summary>
        public int WaveCount => _waveCount;

        /// <summary>연속 모드의 회차 사이 간격(초)이다. 회차가 하나면 0이다.</summary>
        public float WaveInterval => _waveInterval;

        public SpellStats Stats => _stats;
        public IReadOnlyList<string> Mods => _mods;

        /// <summary>장착된 동작 모드의 규칙 목록이다. 등록 순서로 정렬되며 수치 모드는 들어가지 않는다.</summary>
        public IReadOnlyList<SpellModifierRules> ModifierRules => _modifierRules;
        public float Seconds => _seconds;
        public int Times => _times;
        public float Interval => _interval;
        public SpellCondition Condition => _condition;
        public float ShieldAmount => _shieldAmount;
        public float ShieldSeconds => _shieldSeconds;
        /// <summary>과충전 모드의 최대 충전 피해 배율이다. 모드가 없으면 1이다.</summary>
        public float BeamEmpowerMultiplier => _beamEmpowerMultiplier;
        public float BeamEmpowerManaMultiplier => _beamEmpowerManaMultiplier;
        public float BeamEmpowerSizeMultiplier => _beamEmpowerSizeMultiplier;
        public float Power => _power;
        public float BuffDuration => _buffDuration;
        public float ExecutionSeconds => _executionSeconds;
        /// <summary>이 명령의 최대 비용 계산용 자원별 비용이다. 프리셋 호출은 호출 대상 비용 × 개수 × 효과 배율을 포함한다.</summary>
        public ResourceCostSet OwnCosts => _ownCosts;

        /// <summary>실행 중 이 노드에 도달했을 때 차감하는 자원별 비용이다. 프리셋 호출 대상 비용은 포함하지 않는다.</summary>
        public ResourceCostSet NodeCosts => _nodeCosts;

        /// <summary>기존 마나 표시와 호출부를 위한 자체 마나 비용이다.</summary>
        public float OwnEnergy => (float)_ownCosts.GetAmount("mana");

        /// <summary>기존 마나 표시와 호출부를 위한 노드 마나 비용이다.</summary>
        public float NodeEnergy => (float)_nodeCosts.GetAmount("mana");

        /// <summary>부착된 유효 효과들의 비용 배율 곱이다. 프리셋 호출에서는 호출된 노드의 비용에 곱한다.</summary>
        public float EnergyMultiplier => _energyMultiplier;

        /// <summary>부채꼴 Shape의 전체 각도(도)다. 부채꼴이 아니면 0이다.</summary>
        public float ConeAngle => _coneAngle;
        public SpellModifierValues ModifierValues => _modifierValues;
        public CompiledSpell CalledSpell => _calledSpell;

        /// <summary>Form에 해당하는 행동 규칙이다. 행동 규칙이 없는 종류(비생성 명령)는 null이다.</summary>
        public SpellBehaviorRules Rules => _rules;

        /// <summary>개체 생성 위치 유형이다. 행동 규칙의 값을 장착된 동작 모드가 바꾼 결과다(공전이면 궤도). 행동 규칙이 없으면 지팡이 앞이다.</summary>
        public SpellSpawnOrigin SpawnOrigin => _spawnOrigin;
        public IReadOnlyList<SpellAction> OnHit => _onHit;
        public IReadOnlyList<SpellAction> OnExpire => _onExpire;
        public IReadOnlyList<SpellAction> OnFirstHitOrExpire => _onFirstHitOrExpire;
        public IReadOnlyList<SpellAction> Then => _then;
        public IReadOnlyList<SpellAction> Else => _else;
        public IReadOnlyList<SpellAction> Body => _body;

        /// <summary>Repeat의 모든 회차(내부 Delay 포함)가 정상 종료된 뒤 한 번 실행하는 분기다.</summary>
        public IReadOnlyList<SpellAction> OnComplete => _onComplete;
        public IReadOnlyList<SpellAction> Next => _next;

        /// <summary>적중·소멸·첫 이벤트·조건·반복·반복 완료·후속 실행의 모든 하위 분기를 반환한다. 분기를 추가하면 이곳에도 추가한다.</summary>
        public IEnumerable<IReadOnlyList<SpellAction>> Branches
        {
            get
            {
                yield return _onHit;
                yield return _onExpire;
                yield return _onFirstHitOrExpire;
                yield return _then;
                yield return _else;
                yield return _body;
                yield return _onComplete;
                yield return _next;
            }
        }
        public IReadOnlyList<string> AttachedNodeIds => _attachedNodeIds;

        /// <summary>원본 노드와 효과·Shape·Behavior 정의, 호출 대상과 연결 수식으로 실행 명령을 초기화한다.</summary>
        public SpellAction(GraphNode node, RuneDefinition rune, RuneDefinition effectForm, RuneDefinition element,
            IReadOnlyList<RuneDefinition> mods, IReadOnlyList<GraphNode> modifierNodes = null, CompiledSpell calledSpell = null,
            RuneDefinition shape = null, float coneAngle = 0f, ModifierGradeTable modifierGrades = null,
            RuneDefinition behavior = null, float beamWidth = -1f)
        {
            _nodeId = node.Id;
            _shapeDefinition = shape;
            float behaviorSeconds = behavior == null || behavior == rune ? 0f : behavior.ExecutionSeconds;
            _executionSeconds = rune.ExecutionSeconds + behaviorSeconds + (shape?.ExecutionSeconds ?? 0f);
            _magicType = rune.Id == "magic.inline" ? node.GetText("magicType", "sphere") : rune.Category == "form" ? "sphere" : "";
            _form = rune.Id == "magic.inline" ? SpellGrammar.KindOf(node.GetText("form", "launch")) : rune.Category == "form" ? rune.Id.Substring(5) : "";
            SpellBehaviorRuleRegistry.TryGetByKind(_form, out _rules);
            _coneAngle = _magicType == SpellGrammar.MAGIC_TYPE_CONE ? shape?.Stats.ConeAngle ?? coneAngle : 0f;
            if (_rules?.DisplayType == SpellDisplayType.ExpandingRing || _form == SlashBehaviorRules.KIND)
                _coneAngle = effectForm.Stats.ConeAngle;
            _sourceElement = rune.Id == "magic.inline" ? node.GetText("element", "normal") : element == null ? "normal" : SourceElementFromRune(element.Id);
            _element = rune.Id == "magic.inline" ? RuntimeElement(_sourceElement) : element == null ? "raw" : element.Id.Substring(5);
            _calledSpellId = rune.Id == "spell.call" ? node.GetText("spellId").Trim() : "";
            _calledSpell = calledSpell;
            _kind = rune.Id == "spell.call" ? "call" : rune.Id == "magic.inline"
                ? _magicType == "buff" ? "buff" : "spawn" : rune.Category == "form" ? "spawn" : rune.Id.Substring(rune.Id.IndexOf('.') + 1);
            _count = rune.Category == "form" ? rune.Stats.Count : rune.Id == "magic.inline" ? effectForm.Stats.Count : rune.Id == "spell.call" ? 1 : 0;
            float energyMultiplier = 1f;
            List<string> ids = new List<string>();
            for (int index = 0; index < mods.Count; index++)
            {
                RuneDefinition mod = mods[index];
                GraphNode modifierNode = modifierNodes == null ? null : modifierNodes[index];
                RuneStats modifierStats = mod.Stats;
                string grade = SpellGrammar.GetModifierGrade(mod, modifierNode, modifierGrades);
                ids.Add(mod.Id);
                if (mod.Id == BeamEmpowerModifierRules.RUNE_ID)
                {
                    _beamEmpowerMultiplier = SpellModifierValues.GradeNumber(modifierGrades, mod.Id, grade, ModifierGradeTable.BEAM_EMPOWER_MULTIPLIER_EFFECT, 2f);
                    _beamEmpowerManaMultiplier = modifierGrades?.GetBeamEmpowerManaMultiplier(mod.Id, grade) ?? 2f;
                    _beamEmpowerSizeMultiplier = modifierGrades?.GetBeamEmpowerSizeMultiplier(mod.Id, grade) ?? 3f;
                }
                energyMultiplier *= modifierGrades == null
                    ? ModifierGradeTable.DEFAULT_ENERGY_MULTIPLIER
                    : modifierGrades.GetEnergyMultiplier(mod.Id, grade);
                if (SpellModifierRuleRegistry.TryGetByRuneId(mod.Id, out SpellModifierRules modifierRules))
                {
                    _count = modifierRules.ModifyCount(_count, _rules, grade, modifierStats, modifierGrades);
                    _waveCount = modifierRules.ModifyWaveCount(_waveCount, _rules, grade, modifierStats, modifierGrades);
                }
                if (mod.Id == SequenceModifierRules.RUNE_ID) _waveInterval = modifierStats.WaveInterval;
                if (mod.Id == "mod.noise") _isNoise = true;
            }
            if (_waveCount < 1) _waveCount = 1;
            if (_waveCount == 1) _waveInterval = 0f;
            _mods = ids;
            _modifierRules = SpellModifierRuleRegistry.SelectInRegistryOrder(ids);
            _spawnOrigin = _rules != null ? _rules.SpawnOrigin : SpellSpawnOrigin.Wand;
            if (_rules != null)
                foreach (SpellModifierRules modifierRules in _modifierRules) _spawnOrigin = modifierRules.ModifySpawnOrigin(_rules, _spawnOrigin);
            _energyMultiplier = energyMultiplier;
            _modifierValues = SpellModifierValues.From(mods, modifierNodes, modifierGrades);
            if (_kind == "spawn" || _kind == "buff")
            {
                // Sphere·Cone의 기본 반경은 Behavior에서 가져오고 Cone에는 Shape 반지름 배율을 적용한다. Beam은 Shape 정의의 고정 폭·길이를 쓴다.
                float behaviorRadius = effectForm.Stats.Radius;
                float coneRadius = _magicType == SpellGrammar.MAGIC_TYPE_CONE && shape != null
                    ? behaviorRadius * shape.Stats.RadiusMultiplier : behaviorRadius;
                if (_rules?.DisplayType == SpellDisplayType.ExpandingRing || _form == SlashBehaviorRules.KIND)
                    coneRadius = behaviorRadius;
                float boxWidth = shape != null && shape.Stats.BeamWidth > 0f ? shape.Stats.BeamWidth : behaviorRadius * 2f;
                float boxLength = shape != null && shape.Stats.BeamLength > 0f ? shape.Stats.BeamLength : behaviorRadius * 2f;
                if (_form == SlashBehaviorRules.KIND) boxWidth = effectForm.Stats.BeamWidth;
                if (_form == BeamBehaviorRules.BEAM_KIND)
                {
                    boxWidth = beamWidth;
                    boxLength = effectForm.Stats.BeamLength;
                }
                _stats = new SpellStats(effectForm.Stats, element?.Stats, mods, _form, modifierNodes, modifierGrades,
                    _magicType, coneRadius, boxWidth, boxLength);
            }
            ResourceCostSet baseCosts = rune.Costs;
            if (rune.Id == "magic.inline") baseCosts = baseCosts.Add(effectForm.Costs);
            baseCosts = baseCosts.Multiply(energyMultiplier);
            if (element != null) baseCosts = baseCosts.Add(element.Costs);
            _baseNodeCosts = baseCosts;
            ResourceCostSet shapeCosts = _kind == "spawn" && shape != null ? CalculateShapeCosts(shape, _magicType, _stats, _coneAngle) : ResourceCostSet.Empty;
            _nodeCosts = baseCosts.Add(shapeCosts);
            _ownCosts = _nodeCosts.Add(rune.Id == "spell.call" && calledSpell != null
                ? calledSpell.ResourceCosts.Multiply(_count * energyMultiplier) : ResourceCostSet.Empty);
            _seconds = node.GetNumber("seconds", DefaultNumber(rune, "seconds"));
            _times = (int)node.GetNumber("times", DefaultNumber(rune, "times"));
            _interval = node.GetNumber("interval", DefaultNumber(rune, "interval"));
            _power = node.GetNumber("power", DefaultNumber(rune, "power"));
            _buffDuration = node.GetNumber("buffDuration", DefaultNumber(rune, "buffDuration"));
            _shieldAmount = rune.Stats.ShieldAmount;
            _shieldSeconds = rune.Stats.ShieldSeconds;
            if (_kind == "if") _condition = new SpellCondition(node, rune);
        }

        /// <summary>
        /// 시전 위치에서 효과가 닿는 최대 거리를 반환한다. 행동 규칙의 사거리를 장착된 동작 모드가 고친 값이며(공전이면 궤도 반경),
        /// 행동 규칙이 없으면 생성 거리다. stats는 호출 문맥이 적용된 수치다.
        /// </summary>
        public float GetReach(SpellStats stats)
        {
            if (_rules == null) return stats.Offset;
            float reach = _rules.GetReach(stats);
            foreach (SpellModifierRules modifierRules in _modifierRules) reach = modifierRules.ModifyReach(_rules, stats, reach);
            if (_rules.DisplayType == SpellDisplayType.ExpandingRing && _spawnOrigin == SpellSpawnOrigin.Orbit)
            {
                double angle = _coneAngle;
                reach += (float)(2 * stats.Speed * stats.Lifetime * Math.Sin(angle * Math.PI / 720));
            }
            return reach;
        }

        /// <summary>Shape의 effective 넓이에 자원별 단가를 곱해 자원 비용을 소수 첫째 자리로 반올림한다.</summary>
        private static ResourceCostSet CalculateShapeCosts(RuneDefinition shape, string magicType, SpellStats stats, float coneAngle)
        {
            double measure;
            if (magicType == SpellGrammar.MAGIC_TYPE_BOX)
                measure = stats.BoxWidth * stats.BoxLength;
            else if (magicType == SpellGrammar.MAGIC_TYPE_CONE)
                measure = 0.5d * stats.ShapeRadius * stats.ShapeRadius * coneAngle * Math.PI / 180d;
            else
                measure = Math.PI * stats.ShapeRadius * stats.ShapeRadius;
            measure = Math.Round(measure, 1, MidpointRounding.AwayFromZero);
            var costs = new List<ResourceAmount>();
            foreach (ShapeCostRateDefinition rate in shape.ShapeCostRates)
            {
                double amount = Math.Round(rate.Rate * measure, 1, MidpointRounding.AwayFromZero);
                costs.Add(new ResourceAmount(rate.Resource, amount));
            }
            return new ResourceCostSet(costs);
        }

        /// <summary>호출 문맥의 추가 Modifier를 Shape에 적용해 이번 실행 노드의 자원별 비용을 반환한다.</summary>
        public ResourceCostSet GetNodeCosts(SpellModifierValues inheritedModifiers)
        {
            if (_shapeDefinition == null || _shapeDefinition.ShapeCostRates.Count == 0 || _stats == null) return _baseNodeCosts;
            SpellStats effectiveStats = _stats.Apply(inheritedModifiers ?? SpellModifierValues.None);
            return _baseNodeCosts.Add(CalculateShapeCosts(_shapeDefinition, _magicType, effectiveStats, _coneAngle));
        }

        /// <summary>문맥 배율과 onHit 체인 선결제를 반영한 실행 비용을 반환한다. 선결제 문맥에서는 전력만 제외한다.</summary>
        public ResourceCostSet GetExecutionCosts(SpellModifierValues inheritedModifiers, double costMultiplier = 1d,
            bool isManaPrepaid = false, SpellEventScope callEvents = null)
        {
            ResourceCostSet costs = GetNodeCosts(inheritedModifiers).Multiply(costMultiplier);
            if (isManaPrepaid) return costs.WithAmount("mana", 0d);
            if (_kind != "spawn" && _kind != "buff") return costs;
            if (_onHit.Count == 0 && callEvents == null) return costs;
            double energy = 0d;
            int behaviorCount = 0;
            AccumulateOnHitCosts(this, inheritedModifiers ?? SpellModifierValues.None, costMultiplier, callEvents,
                new HashSet<SpellEventScope>(), ref energy, ref behaviorCount);
            return costs.WithAmount("mana", energy * Math.Pow(ON_HIT_ENERGY_MULTIPLIER, Math.Max(0, behaviorCount - 1)));
        }

        /// <summary>한 명령과 onHit·일반 실행 후속의 전력과 행동 노드 수를 한 번씩 더한다. 생성·적중·반복 횟수는 곱하지 않는다.</summary>
        private static void AccumulateOnHitCosts(SpellAction action, SpellModifierValues modifiers, double costMultiplier,
            SpellEventScope callEvents, HashSet<SpellEventScope> visitedScopes, ref double energy, ref int behaviorCount)
        {
            energy += action.GetNodeCosts(modifiers).Multiply(costMultiplier).GetAmount("mana");
            if (action.Kind == "spawn" || action.Kind == "buff") behaviorCount++;
            if (action.Kind == "call")
            {
                var events = new SpellEventScope(action.OnHit, action.OnExpire, action.OnFirstHitOrExpire,
                    callEvents, modifiers, costMultiplier);
                AccumulateOnHitBranches(action.CalledSpell.Root, modifiers.Combine(action.ModifierValues),
                    costMultiplier * action.EnergyMultiplier, events, visitedScopes, ref energy, ref behaviorCount);
            }
            else
            {
                AccumulateOnHitBranches(action.OnHit, modifiers, costMultiplier, callEvents, visitedScopes, ref energy, ref behaviorCount);
            }
            if (action.Kind == "spawn")
            {
                for (SpellEventScope scope = callEvents; scope != null; scope = scope.Parent)
                    if (visitedScopes.Add(scope))
                        AccumulateOnHitBranches(scope.OnHit, scope.ParentModifiers, scope.ParentCostMultiplier,
                            scope.Parent, visitedScopes, ref energy, ref behaviorCount);
            }
            AccumulateOnHitBranches(action.Then, modifiers, costMultiplier, callEvents, visitedScopes, ref energy, ref behaviorCount);
            AccumulateOnHitBranches(action.Else, modifiers, costMultiplier, callEvents, visitedScopes, ref energy, ref behaviorCount);
            AccumulateOnHitBranches(action.Body, modifiers, costMultiplier, callEvents, visitedScopes, ref energy, ref behaviorCount);
            AccumulateOnHitBranches(action.OnComplete, modifiers, costMultiplier, callEvents, visitedScopes, ref energy, ref behaviorCount);
            AccumulateOnHitBranches(action.Next, modifiers, costMultiplier, callEvents, visitedScopes, ref energy, ref behaviorCount);
        }

        /// <summary>연결된 명령 목록을 순회하며 각 노드의 전력과 행동 수를 선결제 합계에 더한다.</summary>
        private static void AccumulateOnHitBranches(IReadOnlyList<SpellAction> actions, SpellModifierValues modifiers,
            double costMultiplier, SpellEventScope callEvents, HashSet<SpellEventScope> visitedScopes,
            ref double energy, ref int behaviorCount)
        {
            foreach (SpellAction action in actions)
                AccumulateOnHitCosts(action, modifiers, costMultiplier, callEvents, visitedScopes, ref energy, ref behaviorCount);
        }

        /// <summary>연결 수식까지 반영한 이 노드의 기본·면적 실행 시간을 반환한다.</summary>
        public double GetExecutionSeconds(SpellModifierValues inheritedModifiers)
        {
            if (_shapeDefinition == null || _shapeDefinition.ExecutionSecondsPerArea <= 0f || _stats == null)
                return _executionSeconds;
            SpellStats effectiveStats = _stats.Apply(inheritedModifiers ?? SpellModifierValues.None);
            double area = MeasureShape(_magicType, effectiveStats, _coneAngle);
            return _executionSeconds + _shapeDefinition.ExecutionSecondsPerArea * area;
        }

        /// <summary>Sphere·Cone 넓이 또는 Box 폭×길이를 W1의 Shape 비용과 같은 기준으로 반환한다.</summary>
        private static double MeasureShape(string magicType, SpellStats stats, float coneAngle)
        {
            double area = magicType == SpellGrammar.MAGIC_TYPE_BOX ? stats.BoxWidth * stats.BoxLength
                : magicType == SpellGrammar.MAGIC_TYPE_CONE
                    ? 0.5d * stats.ShapeRadius * stats.ShapeRadius * coneAngle * Math.PI / 180d
                    : Math.PI * stats.ShapeRadius * stats.ShapeRadius;
            return Math.Round(area, 1, MidpointRounding.AwayFromZero);
        }

        /// <summary>마법 문법의 원소를 기존 시뮬레이션 원소 이름으로 연결한다.</summary>
        private static string RuntimeElement(string element)
        {
            switch (element)
            {
                case "fire": return "fire";
                case "electric": return "arc";
                case "ice": return "ice";
                default: return "raw";
            }
        }

        /// <summary>기존 원소 룬 ID를 마법 문법의 원소 이름으로 반환한다.</summary>
        private static string SourceElementFromRune(string runeId)
        {
            switch (runeId)
            {
                case "elem.fire": return "fire";
                case "elem.arc": return "electric";
                case "elem.ice": return "ice";
                default: return "normal";
            }
        }

        /// <summary>룬 파라미터 목록에서 숫자 기본값을 찾고 없으면 0을 반환한다.</summary>
        private static float DefaultNumber(RuneDefinition rune, string key)
        {
            foreach (ParameterDefinition param in rune.Params)
                if (param.Id == key) return param.DefaultNumber;
            return 0f;
        }

        /// <summary>포트 이름에 맞는 실행 후속 명령 목록을 연결한다.</summary>
        public void SetBranch(string port, IReadOnlyList<SpellAction> actions)
        {
            switch (port)
            {
                case SpellGrammar.ON_HIT_PORT: _onHit = actions; break;
                case SpellGrammar.ON_EXPIRE_PORT: _onExpire = actions; break;
                case SpellGrammar.ON_FIRST_HIT_OR_EXPIRE_PORT: _onFirstHitOrExpire = actions; break;
                case "then": _then = actions; break;
                case "else": _else = actions; break;
                case "body": _body = actions; break;
                case SpellGrammar.ON_COMPLETE_PORT: _onComplete = actions; break;
                case "next": _next = actions; break;
            }
        }

        /// <summary>형태 실행 시 함께 강조할 속성·수식 원본 노드 ID를 저장한다.</summary>
        public void SetAttachedNodes(IReadOnlyList<string> nodeIds) => _attachedNodeIds = nodeIds;
    }

    public sealed class CompiledSpell
    {
        public const int SIMULATION_TICK_RATE = 60;
        public const double ROOT_BRANCH_INTERVAL_SECONDS = 0.05d;
        private const int MAX_ESTIMATED_EXECUTION_TICKS = int.MaxValue / 4;
        private readonly string _id;
        private readonly string _name;
        private readonly string _signature;
        private readonly string _coreNodeId;
        private readonly string _trigger;
        private readonly int _ramUsed;
        private readonly ResourceCostSet _resourceCosts;
        private readonly ResourceCostSet _castCosts;
        private readonly float _cooldown;
        private readonly int _worstCaseEntities;
        private readonly IReadOnlyList<string> _tags;
        private readonly IReadOnlyList<SpellAction> _root;
        private ResourceCostSet _slotCastCosts;
        private float _slotCastTimeScale = float.NaN;
        public string Id => _id;
        public string Name => _name;
        public string Signature => _signature;
        public string CoreNodeId => _coreNodeId;
        public string Trigger => _trigger;
        public int RamUsed => _ramUsed;
        public ResourceCostSet ResourceCosts => _resourceCosts;

        /// <summary>OnHit 선결제를 포함한 원본 구간 비용이다. 슬롯 주기 빔의 실제 시전 비용은 GetCastCosts에서 산정한다.</summary>
        public ResourceCostSet CastCosts => _castCosts;

        /// <summary>실행 배율에 따른 공통 슬롯 쿨다운을 시뮬레이션 틱으로 올림해 반환한다.</summary>
        public int GetSlotCycleTicks(float executionTimeScale)
            => Math.Max(1, GetExecutionDelayTicks(GetCooldownSeconds(executionTimeScale)));

        /// <summary>공격 시작에 직접 연결되어 슬롯 주기로 유지·결제할 빔인지 반환한다.</summary>
        public bool IsSlotCycleBeam(SpellAction action)
        {
            if (_trigger != SpellGrammar.TRIGGER_ON_ATTACK || action.Form != BeamBehaviorRules.BEAM_KIND) return false;
            foreach (SpellAction root in _root)
                if (ReferenceEquals(root, action)) return true;
            return false;
        }

        /// <summary>직접 공격 빔의 초기 비용을 슬롯 한 구간 길이에 비례해 산정한다. 다른 행동은 기존 실행 비용을 반환한다.</summary>
        public ResourceCostSet GetBeamCycleCosts(SpellAction action, float executionTimeScale,
            SpellModifierValues modifiers = null, double costMultiplier = 1d, bool isManaPrepaid = false, SpellEventScope callEvents = null)
        {
            ResourceCostSet costs = action.GetExecutionCosts(modifiers ?? SpellModifierValues.None, costMultiplier, isManaPrepaid, callEvents);
            if (!IsSlotCycleBeam(action)) return costs;
            double minimumSeconds = Math.Max(1d / SIMULATION_TICK_RATE, action.Stats.MinimumLifetime);
            return costs.Multiply(GetSlotCycleTicks(executionTimeScale) / (double)SIMULATION_TICK_RATE / minimumSeconds);
        }

        /// <summary>직접 빔의 슬롯 구간 비용을 반영한 최초 시전 합계를 실행 배율별로 캐싱해 반환한다.</summary>
        public ResourceCostSet GetCastCosts(float executionTimeScale)
        {
            if (_slotCastCosts != null && _slotCastTimeScale == executionTimeScale) return _slotCastCosts;
            var amounts = new List<ResourceAmount>(_castCosts.Amounts);
            foreach (SpellAction action in _root)
            {
                if (!IsSlotCycleBeam(action)) continue;
                foreach (ResourceAmount amount in action.GetExecutionCosts(SpellModifierValues.None).Amounts)
                    amounts.Add(new ResourceAmount(amount.Resource, -amount.Amount));
                amounts.AddRange(GetBeamCycleCosts(action, executionTimeScale).Amounts);
            }
            _slotCastTimeScale = executionTimeScale;
            _slotCastCosts = new ResourceCostSet(amounts);
            return _slotCastCosts;
        }
        public float EnergyCost => (float)_resourceCosts.GetAmount("mana");
        public float Cooldown => _cooldown;
        public double EstimatedExecutionSeconds => GetEstimatedExecutionSeconds(1f);
        public int WorstCaseEntities => _worstCaseEntities;
        public IReadOnlyList<string> Tags => _tags;
        public IReadOnlyList<SpellAction> Root => _root;

        /// <summary>Returns the longest root path in simulation ticks, including staggered root branches, as seconds.</summary>
        public double GetEstimatedExecutionSeconds(float executionTimeScale, SpellModifierValues inheritedModifiers = null)
        {
            int ticks = EstimateRootExecutionTicks(Math.Max(0f, executionTimeScale), inheritedModifiers ?? SpellModifierValues.None);
            return ticks / (double)SIMULATION_TICK_RATE;
        }

        /// <summary>Returns the base RAM cooldown plus the estimated execution time at the supplied scale.</summary>
        public double GetCooldownSeconds(float executionTimeScale)
        {
            return _cooldown + GetEstimatedExecutionSeconds(executionTimeScale);
        }

        /// <summary>검증된 그래프의 자원별 최대 비용·실행 루트·적응 태그를 읽기 전용 마법으로 저장한다.</summary>
        public CompiledSpell(string name, string signature, string coreNodeId, int ramUsed, ResourceCostSet resourceCosts,
            float cooldown, int worstCaseEntities, IReadOnlyList<string> tags, IReadOnlyList<SpellAction> root,
            string id = "", string trigger = SpellGrammar.TRIGGER_ON_ATTACK, ResourceCostSet castCosts = null)
        {
            _id = id;
            _name = name;
            _signature = signature;
            _coreNodeId = coreNodeId;
            _trigger = trigger;
            _ramUsed = ramUsed;
            _resourceCosts = resourceCosts ?? ResourceCostSet.Empty;
            _castCosts = castCosts ?? _resourceCosts;
            _cooldown = cooldown;
            _worstCaseEntities = worstCaseEntities;
            _tags = tags;
            _root = root;
        }

        /// <summary>Estimates the latest completion tick after applying the root start interval to each connected branch.</summary>
        private int EstimateRootExecutionTicks(float executionTimeScale, SpellModifierValues inheritedModifiers)
        {
            int longest = 0;
            int intervalTicks = GetExecutionDelayTicks(ROOT_BRANCH_INTERVAL_SECONDS);
            for (int index = 0; index < _root.Count; index++)
            {
                int branchStartTicks = MultiplyTicks(intervalTicks, index);
                int branchTicks = EstimateActionTicks(_root[index], executionTimeScale, inheritedModifiers);
                longest = Math.Max(longest, AddTicks(branchStartTicks, branchTicks));
            }
            return longest;
        }

        /// <summary>Returns the longest completion tick among parallel actions on one output.</summary>
        private static int EstimateExecutionTicks(IReadOnlyList<SpellAction> actions, float executionTimeScale,
            SpellModifierValues inheritedModifiers)
        {
            int longest = 0;
            foreach (SpellAction action in actions)
                longest = Math.Max(longest, EstimateActionTicks(action, executionTimeScale, inheritedModifiers));
            return longest;
        }

        /// <summary>Adds a node's tick-rounded duration to the longest sequential path or selected condition branch.</summary>
        private static int EstimateActionTicks(SpellAction action, float executionTimeScale,
            SpellModifierValues inheritedModifiers)
        {
            double nodeSeconds = action.Kind == "delay" ? action.Seconds : action.GetExecutionSeconds(inheritedModifiers);
            int nodeTicks = GetExecutionDelayTicks(nodeSeconds * executionTimeScale);
            // 연속 모드의 회차 사이 간격을 노드 실행 시간에 더한다. 마지막 회차 뒤에는 간격이 없다.
            if (action.Kind == "spawn" && action.WaveCount > 1)
                nodeTicks = AddTicks(nodeTicks,
                    MultiplyTicks(GetExecutionDelayTicks(action.WaveInterval * executionTimeScale), action.WaveCount - 1));
            int nextTicks;
            switch (action.Kind)
            {
                case "call":
                    SpellModifierValues callModifiers = inheritedModifiers.Combine(action.ModifierValues);
                    nextTicks = action.CalledSpell == null ? 0
                        : action.CalledSpell.EstimateRootExecutionTicks(executionTimeScale, callModifiers);
                    break;
                case "delay":
                    nextTicks = EstimateExecutionTicks(action.Then, executionTimeScale, inheritedModifiers);
                    break;
                case "repeat":
                    int bodyTicks = EstimateExecutionTicks(action.Body, executionTimeScale, inheritedModifiers);
                    int lastRepeatStartTicks = action.Times > 1
                        ? GetRepeatStartDelayTicks((action.Times - 1) * action.Interval * executionTimeScale)
                        : 0;
                    int repeatTicks = action.Times > 0 ? AddTicks(lastRepeatStartTicks, bodyTicks) : 0;
                    nextTicks = AddTicks(repeatTicks, EstimateExecutionTicks(action.OnComplete, executionTimeScale, inheritedModifiers));
                    break;
                case "if":
                    nextTicks = Math.Max(EstimateExecutionTicks(action.Then, executionTimeScale, inheritedModifiers),
                        EstimateExecutionTicks(action.Else, executionTimeScale, inheritedModifiers));
                    break;
                case "buff":
                    nextTicks = EstimateExecutionTicks(action.OnFirstHitOrExpire, executionTimeScale, inheritedModifiers);
                    break;
                default:
                    nextTicks = EstimateExecutionTicks(action.Next, executionTimeScale, inheritedModifiers);
                    break;
            }
            return AddTicks(nodeTicks, nextTicks);
        }

        /// <summary>Rounds execution seconds up to the same bounded 60 Hz duration used by the simulation scheduler.</summary>
        private static int GetExecutionDelayTicks(double seconds)
        {
            if (seconds <= 0) return 0;
            double ticks = Math.Ceiling(seconds * SIMULATION_TICK_RATE - 0.000000001);
            return (int)Math.Min(MAX_ESTIMATED_EXECUTION_TICKS, Math.Max(1, ticks));
        }

        /// <summary>Matches the rounded interval used when a Repeat node schedules its final iteration.</summary>
        private static int GetRepeatStartDelayTicks(double seconds)
        {
            if (seconds <= 0) return 0;
            double ticks = Math.Round(seconds * SIMULATION_TICK_RATE);
            return (int)Math.Min(MAX_ESTIMATED_EXECUTION_TICKS, Math.Max(1, ticks));
        }

        /// <summary>Adds tick durations while keeping recursive estimates inside a safe integer range.</summary>
        private static int AddTicks(int first, int second)
        {
            return (int)Math.Min(MAX_ESTIMATED_EXECUTION_TICKS, (long)first + second);
        }

        /// <summary>Multiplies a root branch offset while keeping recursive estimates inside a safe integer range.</summary>
        private static int MultiplyTicks(int ticks, int multiplier)
        {
            return (int)Math.Min(MAX_ESTIMATED_EXECUTION_TICKS, (long)ticks * multiplier);
        }

        /// <summary>실행 트리 전체에서 지정 노드 ID로 컴파일된 첫 명령을 찾아 반환하며 없으면 null을 반환한다.</summary>
        public SpellAction FindAction(string nodeId)
        {
            return FindAction(_root, nodeId);
        }

        /// <summary>
        /// 편집 그래프의 노드가 속한 컴파일 명령을 반환한다. 속성 노드는 붙은 Shape로, Shape 노드는 체인 출력을 따라 합쳐진 Behavior 명령을,
        /// Modifier 노드는 자신이 붙은 명령을 찾는다. 없으면 null을 반환한다.
        /// </summary>
        public SpellAction FindNodeAction(SpellGraph graph, string nodeId)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string current = nodeId;
            while (current != null && visited.Add(current))
            {
                SpellAction found = FindAction(_root, current) ?? FindAttachedAction(_root, current);
                if (found != null) return found;
                string next = null;
                foreach (GraphEdge edge in graph.Edges)
                {
                    if (edge.FromNode == current && (edge.FromPort == SpellGrammar.CHAIN_OUT || edge.FromPort == SpellGrammar.ELEMENT_PORT))
                    {
                        next = edge.ToNode;
                        break;
                    }
                }
                current = next;
            }
            return null;
        }

        /// <summary>노드 ID의 명령이 적중·소멸 이벤트 분기 안에 있어 사건 위치에서 실행되는지 반환한다.</summary>
        public bool IsEventAction(string nodeId) => IsEventAction(_root, nodeId, false);

        /// <summary>이벤트 분기 안인지(isEvent)를 이어 받으며 하위 분기를 깊이 우선으로 탐색해 노드의 이벤트 소속 여부를 반환한다.</summary>
        private static bool IsEventAction(IReadOnlyList<SpellAction> actions, string nodeId, bool isEvent)
        {
            foreach (SpellAction action in actions)
            {
                if (action.NodeId == nodeId) return isEvent;
                if (IsEventAction(action.OnHit, nodeId, true) || IsEventAction(action.OnExpire, nodeId, true)
                    || IsEventAction(action.OnFirstHitOrExpire, nodeId, true)) return true;
                if (IsEventAction(action.Then, nodeId, isEvent) || IsEventAction(action.Else, nodeId, isEvent)
                    || IsEventAction(action.Body, nodeId, isEvent) || IsEventAction(action.OnComplete, nodeId, isEvent)
                    || IsEventAction(action.Next, nodeId, isEvent)) return true;
            }
            return false;
        }

        /// <summary>Modifier 노드 ID가 붙은 첫 명령을 깊이 우선으로 찾아 반환하며 없으면 null을 반환한다.</summary>
        private static SpellAction FindAttachedAction(IReadOnlyList<SpellAction> actions, string nodeId)
        {
            foreach (SpellAction action in actions)
            {
                if (action.AttachedNodeIds.Contains(nodeId)) return action;
                foreach (IReadOnlyList<SpellAction> branch in action.Branches)
                {
                    SpellAction found = FindAttachedAction(branch, nodeId);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>명령 목록과 각 명령의 하위 분기를 깊이 우선으로 탐색해 노드 ID와 일치하는 명령을 반환한다.</summary>
        private static SpellAction FindAction(IReadOnlyList<SpellAction> actions, string nodeId)
        {
            foreach (SpellAction action in actions)
            {
                if (action.NodeId == nodeId)
                {
                    return action;
                }
                foreach (IReadOnlyList<SpellAction> branch in action.Branches)
                {
                    SpellAction found = FindAction(branch, nodeId);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            return null;
        }
    }

    public sealed class CompileResult
    {
        private readonly IReadOnlyList<CompileIssue> _errors;
        private readonly IReadOnlyList<CompileIssue> _warnings;
        private readonly CompiledSpell _spell;
        public bool Ok => _errors.Count == 0 && _spell != null;
        public IReadOnlyList<CompileIssue> Errors => _errors;
        public IReadOnlyList<CompileIssue> Warnings => _warnings;
        public CompiledSpell Spell => _spell;

        /// <summary>컴파일의 오류·경고와 성공한 마법을 반환 객체에 저장한다.</summary>
        public CompileResult(IReadOnlyList<CompileIssue> errors, IReadOnlyList<CompileIssue> warnings, CompiledSpell spell)
        {
            _errors = errors;
            _warnings = warnings;
            _spell = spell;
        }
    }
}
