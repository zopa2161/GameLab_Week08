namespace RuneCode
{
    internal readonly struct SpellContext
    {
        private readonly SimVector _origin;
        private readonly SimVector _direction;
        private readonly SimulationEnemy _target;
        private readonly bool _fromEvent;
        private readonly string _noiseElement;
        private readonly SpellModifierValues _modifiers;
        private readonly double _costMultiplier;
        private readonly SimulationPlayer _caster;
        private readonly int _sourceEntityId;
        private readonly double _directionOffset;
        private readonly SpellRandomState _randomState;
        private readonly bool _isManaPrepaid;
        private readonly bool _suppressesTriggers;
        private readonly SpellAction _onHitSource;
        private readonly double _castDamageMultiplier;
        public SpellAction OnHitSource => _onHitSource;
        public SimVector Origin => _origin;
        public SimVector Direction => _direction;
        public SimulationEnemy Target => _target;
        public bool FromEvent => _fromEvent;
        public string NoiseElement => _randomState.NoiseElement ?? _noiseElement;
        public SpellModifierValues Modifiers => _modifiers;
        public double DirectionOffset => _directionOffset;
        public SpellRandomState RandomState => _randomState;
        public bool IsManaPrepaid => _isManaPrepaid;
        public bool SuppressesTriggers => _suppressesTriggers;

        /// <summary>이 경로의 노드 비용에 곱하는 배율이다. 프리셋 호출 안에서는 호출 노드 효과의 비용 배율이 곱해진다.</summary>
        public double CostMultiplier => _costMultiplier;

        /// <summary>시전 시점 조건(최대 전력·대시 직후 등)으로 정한 피해 배율이다. 이벤트 분기와 생성 개체가 그대로 물려받는다.</summary>
        public double CastDamageMultiplier => _castDamageMultiplier;

        /// <summary>마법을 시전한 주체다. Apply 등 자기 적용 효과의 대상이다.</summary>
        public SimulationPlayer Caster => _caster;

        /// <summary>이 경로를 시작한 이벤트의 원본 개체 ID다. 시전·흐름 제어로 시작한 경로는 0이다.</summary>
        public int SourceEntityId => _sourceEntityId;

        /// <summary>
        /// 발생 위치, 방향, 선택 대상, 이벤트 유래 여부, 비용·시전 피해 배율, 시전자와 이벤트 원본을 예약 가능한 스냅샷으로 보관한다.
        /// </summary>
        public SpellContext(SimVector origin, SimVector direction, SimulationEnemy target, bool fromEvent,
            string noiseElement, SpellModifierValues modifiers = null,
            double costMultiplier = 1d, SimulationPlayer caster = null, int sourceEntityId = 0,
            double directionOffset = 0, SpellRandomState randomState = null, bool isManaPrepaid = false, SpellAction onHitSource = null,
            double castDamageMultiplier = 1d, bool suppressesTriggers = false)
        {
            _origin = origin; _direction = direction; _target = target; _fromEvent = fromEvent; _noiseElement = noiseElement;
            _modifiers = modifiers ?? SpellModifierValues.None; _costMultiplier = costMultiplier;
            _caster = caster; _sourceEntityId = sourceEntityId; _directionOffset = directionOffset;
            _randomState = randomState ?? new SpellRandomState(noiseElement);
            _isManaPrepaid = isManaPrepaid;
            _suppressesTriggers = suppressesTriggers;
            _onHitSource = onHitSource;
            _castDamageMultiplier = castDamageMultiplier;
        }

        /// <summary>실행 위치만 바꾼 문맥을 반환한다.</summary>
        public SpellContext WithOrigin(SimVector origin)
            => new SpellContext(origin, _direction, _target, _fromEvent, _noiseElement, _modifiers, _costMultiplier, _caster, _sourceEntityId, _directionOffset, _randomState, _isManaPrepaid, _onHitSource, _castDamageMultiplier, _suppressesTriggers);

        /// <summary>시전 시점 문맥의 원점과 방향을 완료 순간 값으로 바꾼 복사본을 반환한다.</summary>
        public SpellContext WithOriginAndDirection(SimVector origin, SimVector direction)
            => new SpellContext(origin, direction, _target, _fromEvent, _noiseElement, _modifiers, _costMultiplier, _caster, _sourceEntityId, _directionOffset, _randomState, _isManaPrepaid, _onHitSource, _castDamageMultiplier, _suppressesTriggers);

        /// <summary>위치·예약 문맥을 유지하고 onHit 전력 선결제 여부만 바꾼 복사본을 반환한다.</summary>
        public SpellContext WithManaPrepaid(bool isManaPrepaid)
            => new SpellContext(_origin, _direction, _target, _fromEvent, _noiseElement, _modifiers, _costMultiplier, _caster, _sourceEntityId, _directionOffset, _randomState, isManaPrepaid, _onHitSource, _castDamageMultiplier, _suppressesTriggers);
    }
}
