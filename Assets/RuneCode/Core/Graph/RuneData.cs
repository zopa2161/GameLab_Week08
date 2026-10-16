using System;
using System.Collections.Generic;

namespace RuneCode
{
    public sealed class PortDefinition
    {
        private readonly string _id;
        private readonly string _kind;
        private readonly string _direction;
        private readonly int _max;
        private readonly string _status;

        public string Id => _id;
        public string Kind => _kind;
        public string Direction => _direction;
        public int Max => _max;
        public string Status => _status;

        /// <summary>지원 중단 포트인지 반환한다. 새로 연결할 수 없고 기존 연결은 보존하되 실행하지 않는다.</summary>
        public bool IsDeprecated => _status == SpellGrammar.PORT_DEPRECATED;

        /// <summary>룬 데이터의 포트 항목으로 포트를 만든다. status가 비어 있으면 active다.</summary>
        internal PortDefinition(RunePortData row)
        {
            _id = row._portId;
            _kind = row._kind;
            _direction = row._direction;
            _max = row._max;
            _status = string.IsNullOrEmpty(row._status) ? SpellGrammar.PORT_ACTIVE : row._status;
        }
    }

    public sealed class ParameterDefinition
    {
        private readonly string _id;
        private readonly string _kind;
        private readonly float _min;
        private readonly float _max;
        private readonly float _step;
        private readonly float _defaultNumber;
        private readonly string _defaultText;
        private readonly IReadOnlyList<string> _options;

        public string Id => _id;
        public string Kind => _kind;
        public float Min => _min;
        public float Max => _max;
        public float Step => _step;
        public float DefaultNumber => _defaultNumber;
        public string DefaultText => _defaultText;
        public IReadOnlyList<string> Options => _options;

        /// <summary>룬 데이터의 파라미터 항목과 선택지 목록으로 파라미터를 만든다.</summary>
        internal ParameterDefinition(RuneParamData row, IReadOnlyList<string> options)
        {
            _id = row._paramId;
            _kind = row._kind;
            _min = row._min;
            _max = row._max;
            _step = row._step;
            _defaultNumber = row._defaultNumber;
            _defaultText = row._defaultText ?? "";
            _options = options;
        }
    }

    public sealed class RuneStats
    {
        private readonly float _damage;
        private readonly float _speed;
        private readonly float _radius;
        private readonly float _lifetime;
        private readonly float _minimumLifetime;
        private readonly float _offset;
        private readonly int _count;
        private readonly float _orbitRadius;
        private readonly float _angularSpeed;
        private readonly float _entrySpeed;
        private readonly float _hitInterval;
        private readonly float _tickInterval;
        private readonly float _damageMultiplier;
        private readonly float _radiusMultiplier;
        private readonly float _speedMultiplier;
        private readonly float _durationMultiplier;
        private readonly int _pierce;
        private readonly float _pierceLoss;
        private readonly float _homingTurn;
        private readonly float _homingRange;
        private readonly float _arcRange;
        private readonly int _arcTargets;
        private readonly float _arcMultiplier;
        private readonly float _learningMultiplier;
        private readonly float _spreadAngle;
        private readonly float _shieldAmount;
        private readonly float _shieldSeconds;
        private readonly float _coneAngle;
        private readonly float _expandSeconds;
        private readonly float _warnSeconds;
        private readonly float _beamWidth;
        private readonly float _beamLength;

        public float Damage => _damage;
        public float Speed => _speed;
        public float Radius => _radius;
        public float Lifetime => _lifetime;
        public float MinimumLifetime => _minimumLifetime;
        public float Offset => _offset;
        public int Count => _count;
        public float OrbitRadius => _orbitRadius;
        public float AngularSpeed => _angularSpeed;

        /// <summary>공전 모드가 생성 위치에서 궤도 자리로 날아가 진입하는 속도(px/s)다. 개체를 직접 옮기지 않는 행동(잔류)에 쓴다.</summary>
        public float EntrySpeed => _entrySpeed;
        public float HitInterval => _hitInterval;
        public float TickInterval => _tickInterval;
        public float DamageMultiplier => _damageMultiplier;
        public float RadiusMultiplier => _radiusMultiplier;
        public float SpeedMultiplier => _speedMultiplier;
        public float DurationMultiplier => _durationMultiplier;
        public int Pierce => _pierce;
        public float PierceLoss => _pierceLoss;
        public float HomingTurn => _homingTurn;
        public float HomingRange => _homingRange;
        public float ArcRange => _arcRange;
        public int ArcTargets => _arcTargets;
        public float ArcMultiplier => _arcMultiplier;
        public float LearningMultiplier => _learningMultiplier;
        public float SpreadAngle => _spreadAngle;
        public float ShieldAmount => _shieldAmount;
        public float ShieldSeconds => _shieldSeconds;

        /// <summary>부채꼴(Cone) Shape의 전체 각도(도)다. Cone이 아닌 룬은 0이다.</summary>
        public float ConeAngle => _coneAngle;

        /// <summary>Burst 범위가 0에서 최종 크기까지 커지는 시간(초)이다. Burst가 아닌 룬은 0이다.</summary>
        public float ExpandSeconds => _expandSeconds;

        /// <summary>Persist 영역이 활성화되기 전 예고 시간(초)이다. Persist가 아닌 룬은 0이다.</summary>
        public float WarnSeconds => _warnSeconds;

        /// <summary>Beam Shape의 고정 폭(px)이다. Beam Shape가 아닌 룬은 0이다.</summary>
        public float BeamWidth => _beamWidth;

        /// <summary>Beam Shape의 고정 길이(px)다. Beam Shape가 아닌 룬은 0이다.</summary>
        public float BeamLength => _beamLength;

        /// <summary>룬 데이터의 수치 항목으로 효과 수치를 만든다. JSON에 없는 필드는 배율이면 1, 그 외는 0이다.</summary>
        internal RuneStats(RuneStatsData row)
        {
            _damage = row._damage;
            _speed = row._speed;
            _radius = row._radius;
            _lifetime = row._lifetime;
            _minimumLifetime = row._minimumLifetime;
            _offset = row._offset;
            _count = row._count;
            _orbitRadius = row._orbitRadius;
            _angularSpeed = row._angularSpeed;
            _entrySpeed = row._entrySpeed;
            _hitInterval = row._hitInterval;
            _tickInterval = row._tickInterval;
            _damageMultiplier = row._damageMultiplier;
            _radiusMultiplier = row._radiusMultiplier;
            _speedMultiplier = row._speedMultiplier;
            _durationMultiplier = row._durationMultiplier;
            _pierce = row._pierce;
            _pierceLoss = row._pierceLoss;
            _homingTurn = row._homingTurn;
            _homingRange = row._homingRange;
            _arcRange = row._arcRange;
            _arcTargets = row._arcTargets;
            _arcMultiplier = row._arcMultiplier;
            _learningMultiplier = row._learningMultiplier;
            _spreadAngle = row._spreadAngle;
            _shieldAmount = row._shieldAmount;
            _shieldSeconds = row._shieldSeconds;
            _coneAngle = row._coneAngle;
            _expandSeconds = row._expandSeconds;
            _warnSeconds = row._warnSeconds;
            _beamWidth = row._beamWidth;
            _beamLength = row._beamLength;
        }
    }

    /// <summary>Shape 넓이에 곱할 자원별 단가다.</summary>
    public sealed class ShapeCostRateDefinition
    {
        private readonly string _resource;
        private readonly float _rate;

        public string Resource => _resource;
        public float Rate => _rate;

        /// <summary>Shape 비용률 행의 자원 ID와 단가를 보관한다.</summary>
        internal ShapeCostRateDefinition(RuneShapeCostRateData row)
        {
            _resource = row._resource;
            _rate = row._rate;
        }
    }

    /// <summary>Modifier의 이름 있는 유도 단계와 고정 유도 수치를 보관한다.</summary>
    public sealed class HomingTierDefinition
    {
        private readonly string _tier;
        private readonly float _homingTurn;
        private readonly float _homingRange;

        public string Tier => _tier;
        public float HomingTurn => _homingTurn;
        public float HomingRange => _homingRange;

        /// <summary>runes.json의 유도 단계 설정으로 실행 수치를 만든다.</summary>
        internal HomingTierDefinition(RuneHomingTierData row)
        {
            _tier = row._tier;
            _homingTurn = row._homingTurn;
            _homingRange = row._homingRange;
        }
    }

    public sealed class RuneDefinition
    {
        private readonly string _id;
        private readonly string _name;
        private readonly string _category;
        private readonly bool _isDisabled;
        private readonly int _ram;
        private readonly float _energy;
        private readonly float _energyMult;
        private readonly float _executionSeconds;
        private readonly float _executionSecondsPerArea;
        private readonly string _unlockType;
        private readonly int _unlockCost;
        private readonly ResourceCostSet _costs;
        private readonly IReadOnlyList<string> _tags;
        private readonly IReadOnlyList<PortDefinition> _ports;
        private readonly IReadOnlyList<ParameterDefinition> _params;
        private readonly RuneStats _stats;
        private readonly IReadOnlyList<HomingTierDefinition> _homingTiers;
        private readonly IReadOnlyList<ShapeCostRateDefinition> _shapeCostRates;
        private readonly string _fixedShape;

        public string Id => _id;
        public string Name => _name;
        public string Category => _category;

        /// <summary>팔레트·획득·컴파일에서 제외되는 비활성 룬인지 반환한다.</summary>
        public bool IsDisabled => _isDisabled;

        public int Ram => _ram;
        public float Energy => _energy;
        public float EnergyMult => _energyMult;
        public float ExecutionSeconds => _executionSeconds;
        public float ExecutionSecondsPerArea => _executionSecondsPerArea;
        public string UnlockType => _unlockType;
        public int UnlockCost => _unlockCost;
        public ResourceCostSet Costs => _costs;
        public IReadOnlyList<string> Tags => _tags;
        public IReadOnlyList<PortDefinition> Ports => _ports;
        public IReadOnlyList<ParameterDefinition> Params => _params;
        public RuneStats Stats => _stats;
        public IReadOnlyList<ShapeCostRateDefinition> ShapeCostRates => _shapeCostRates;

        /// <summary>행동 룬의 고정 형태(sphere 등)다. 형태 프리셋 룬 shape.{값}의 수치를 쓴다. 행동이 아니면 빈 문자열이다.</summary>
        public string FixedShape => _fixedShape;

        /// <summary>룬 데이터와 자식 목록에서 모은 태그·포트·파라미터·유도 단계 수치로 룬 정의를 만든다.</summary>
        internal RuneDefinition(RuneRowData row, IReadOnlyList<string> tags, IReadOnlyList<PortDefinition> ports,
            IReadOnlyList<ParameterDefinition> parameters, IReadOnlyList<HomingTierDefinition> homingTiers,
            ResourceCostSet costs, IReadOnlyList<ShapeCostRateDefinition> shapeCostRates)
        {
            _id = row._id;
            _name = row._name ?? "";
            _category = row._category ?? "";
            _isDisabled = row._disabled;
            _ram = row._ram;
            _costs = costs;
            _energy = (float)costs.GetAmount("mana");
            _energyMult = row._energyMult;
            _executionSeconds = row._executionSeconds;
            _executionSecondsPerArea = row._executionSecondsPerArea;
            _unlockType = row._unlockType ?? "";
            _unlockCost = row._unlockCost;
            _tags = tags;
            _ports = ports;
            _params = parameters;
            _stats = new RuneStats(row._stats ?? new RuneStatsData());
            _homingTiers = homingTiers;
            _shapeCostRates = shapeCostRates;
            _fixedShape = row._shape ?? "";
        }

        /// <summary>이름이 일치하는 유도 단계 설정을 반환하고 없으면 null을 반환한다.</summary>
        public HomingTierDefinition FindHomingTier(string tier)
        {
            foreach (HomingTierDefinition homingTier in _homingTiers)
                if (homingTier.Tier == tier) return homingTier;
            return null;
        }

        /// <summary>포트 ID와 방향으로 해당 룬의 포트 정의를 찾거나 null을 반환한다.</summary>
        public PortDefinition FindPort(string id, string direction)
        {
            foreach (PortDefinition port in Ports)
            {
                if (port.Id == id && port.Direction == direction) return port;
            }
            return null;
        }
    }

    /// <summary>
    /// 속성(Element) 정의다. 속성은 그래프 노드가 아니라 Shape·Apply 노드의 드롭다운 값이며,
    /// RAM·해금은 같은 ID의 속성 룬(runes.json의 element 카테고리 항목)이 가진다.
    /// </summary>
    public sealed class ElementDefinition
    {
        private readonly string _id;
        private readonly string _runeId;
        private readonly string _category;
        private readonly string _internalValue;
        private readonly string _runtimeTag;

        public string Id => _id;
        public string RuneId => _runeId;
        public string Category => _category;
        public bool IsFunctional => _category == SpellGrammar.ELEMENT_CATEGORY_FUNCTIONAL;
        public string InternalValue => _internalValue;
        public string RuntimeTag => _runtimeTag;

        /// <summary>룬 데이터의 속성 항목과 소속 룬 ID로 속성을 만든다. 런타임 태그가 없으면 빈 문자열이다.</summary>
        internal ElementDefinition(RuneElementData row, string runeId)
        {
            _id = row._id;
            _runeId = runeId;
            _category = row._category;
            _internalValue = row._internalValue;
            _runtimeTag = row._runtimeTag ?? "";
        }
    }

    /// <summary>
    /// 룬 정의 레지스트리다. runes.json의 룬 항목(수치·포트·파라미터·태그·속성·효과 대상 포함)을 룬 ID로 합쳐 만들며,
    /// 테이블 기반 데이터 로딩의 참조 구현이다. 규칙은 docs/DATA_TABLES.md를 따른다.
    /// </summary>
    public sealed class RuneCatalog
    {
        private static readonly HashSet<string> CATEGORIES = new HashSet<string>(StringComparer.Ordinal)
        {
            "core", "form", "element", "modifier", "flow", "action", "magic", "shape", "behavior", "method", "internal"
        };
        private static readonly HashSet<string> ELEMENT_CATEGORIES = new HashSet<string>(StringComparer.Ordinal)
        {
            SpellGrammar.ELEMENT_CATEGORY_ELEMENTAL, SpellGrammar.ELEMENT_CATEGORY_FUNCTIONAL
        };
        private static readonly HashSet<string> STARTER_RUNE_IDS = new HashSet<string>(StringComparer.Ordinal)
        {
            "shape.sphere", "behavior.launch", "element.neutral"
        };
        private static readonly HashSet<string> UNLOCK_TYPES = new HashSet<string>(StringComparer.Ordinal) { "start", "tree", "bench" };

        private readonly IReadOnlyList<RuneDefinition> _runes;
        private readonly Dictionary<string, RuneDefinition> _byId = new Dictionary<string, RuneDefinition>(StringComparer.Ordinal);
        private readonly List<string> _startRunes = new List<string>();
        private readonly IReadOnlyList<ElementDefinition> _elements;
        private readonly Dictionary<string, ElementDefinition> _elementsById = new Dictionary<string, ElementDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _modifierTargets;
        private readonly List<string> _behaviorGradeOrder;
        private readonly Dictionary<string, (int ElementSlots, int ModifierSlots)> _behaviorSlots;
        private ModifierGradeTable _modifierGrades;

        public IReadOnlyList<RuneDefinition> All => _runes;
        public IReadOnlyList<string> StartRunes => _startRunes;
        public IReadOnlyList<ElementDefinition> Elements => _elements;

        /// <summary>행동 등급 표의 등급을 C→B→A→S 순서로 반환한다.</summary>
        public IReadOnlyList<string> BehaviorGradeOrder => _behaviorGradeOrder;

        public ModifierGradeTable ModifierGrades => _modifierGrades;

        /// <summary>검증된 룬·속성 목록과 효과 대상·행동 등급 조회표로 ID 조회표와 시작 해금 목록을 만든다.</summary>
        private RuneCatalog(IReadOnlyList<RuneDefinition> runes, IReadOnlyList<ElementDefinition> elements,
            Dictionary<string, HashSet<string>> modifierTargets, List<string> behaviorGradeOrder,
            Dictionary<string, (int ElementSlots, int ModifierSlots)> behaviorSlots)
        {
            _runes = runes;
            _elements = elements;
            _modifierTargets = modifierTargets;
            _behaviorGradeOrder = behaviorGradeOrder;
            _behaviorSlots = behaviorSlots;
            foreach (ElementDefinition element in elements)
            {
                _elementsById.Add(element.Id, element);
            }
            foreach (RuneDefinition rune in runes)
            {
                _byId.Add(rune.Id, rune);
                if (rune.UnlockType == "start") _startRunes.Add(rune.Id);
            }
        }

        /// <summary>
        /// runes.json 데이터에서 룬 테이블을 읽어 룬·속성·효과 대상 정의를 만들고 형식·값 규칙을 검증한 레지스트리를 반환한다.
        /// 오류는 모두 모은 뒤 TableLoadException(FormatException)으로 한 번에 보고한다.
        /// </summary>
        public static RuneCatalog FromTable(RuneTableData table)
        {
            var log = new TableErrorLog();
            if (table == null || table._runes == null || table._runes.Count == 0)
            {
                log.Add(null, null, "룬 정의가 비어 있습니다.");
                log.ThrowIfAny();
            }

            var definitions = new List<RuneDefinition>();
            var elements = new List<ElementDefinition>();
            var elementIds = new HashSet<string>(StringComparer.Ordinal);
            var runeIds = new HashSet<string>(StringComparer.Ordinal);
            var modifiers = new List<(RuneDefinition rune, List<string> targets)>();
            var behaviorGradeOrder = new List<string>();
            var behaviorSlots = new Dictionary<string, (int ElementSlots, int ModifierSlots)>(StringComparer.Ordinal);
            BuildBehaviorGrades(table._behaviorGrades, behaviorGradeOrder, behaviorSlots, log);
            foreach (RuneRowData row in table._runes)
            {
                if (row == null || string.IsNullOrEmpty(row._id) || !runeIds.Add(row._id))
                {
                    log.Add(row != null ? row._id : null, "id", "룬 ID가 비어 있거나 중복입니다.");
                    continue;
                }
                ResourceCostSet costs = BuildCosts(row, log);
                IReadOnlyList<ShapeCostRateDefinition> shapeCostRates = BuildShapeCostRates(row, log);
                ValidateExecutionTime(row, log);
                var rune = new RuneDefinition(row, BuildTags(row, log), BuildPorts(row, log), BuildParameters(row, log),
                    BuildHomingTiers(row, log), costs, shapeCostRates);
                RequireText(rune, row, log);
                ValidateRune(rune, log);
                ValidateStats(rune, rune.Stats, log, "stats");
                definitions.Add(rune);

                if (row._element != null)
                {
                    AddElement(elements, elementIds, rune, row._element, log);
                }
                if (row._modifierTargets != null && row._modifierTargets.Count > 0)
                {
                    modifiers.Add((rune, row._modifierTargets));
                }
            }

            ValidateModifierTargets(modifiers, definitions, log);
            ValidateBehaviorShapes(definitions, runeIds, log);
            log.ThrowIfAny();
            return new RuneCatalog(definitions, elements, BuildModifierTargets(modifiers), behaviorGradeOrder, behaviorSlots);
        }

        /// <summary>활성 행동 룬의 고정 형태(_shape)가 비어 있거나 형태 프리셋 룬(shape.*)을 가리키지 않으면 오류를 추가한다.</summary>
        private static void ValidateBehaviorShapes(List<RuneDefinition> definitions, HashSet<string> runeIds, TableErrorLog log)
        {
            foreach (RuneDefinition rune in definitions)
            {
                if (rune.Category != SpellGrammar.CATEGORY_BEHAVIOR || rune.IsDisabled) continue;
                if (string.IsNullOrEmpty(rune.FixedShape) || !runeIds.Contains(SpellGrammar.ShapeRune(rune.FixedShape)))
                    log.Add(rune.Id, "shape", "행동의 고정 형태가 비어 있거나 형태 프리셋(shape.*)에 없습니다.");
            }
        }

        /// <summary>행동 등급의 속성·모디 슬롯 수를 반환한다. 없는 등급이면 false를 반환한다.</summary>
        public bool TryGetBehaviorSlots(string grade, out int elementSlots, out int modifierSlots)
        {
            if (!string.IsNullOrEmpty(grade) && _behaviorSlots.TryGetValue(grade, out (int ElementSlots, int ModifierSlots) slots))
            {
                elementSlots = slots.ElementSlots;
                modifierSlots = slots.ModifierSlots;
                return true;
            }
            elementSlots = 0;
            modifierSlots = 0;
            return false;
        }

        /// <summary>행동 등급 표를 만들고 등급 빈 값·중복과 슬롯 수 범위를 검증한다.</summary>
        private static void BuildBehaviorGrades(List<BehaviorGradeData> rows, List<string> order,
            Dictionary<string, (int ElementSlots, int ModifierSlots)> slots, TableErrorLog log)
        {
            if (rows == null) return;
            foreach (BehaviorGradeData row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row._grade) || slots.ContainsKey(row._grade))
                {
                    log.Add(null, "behaviorGrades", "등급이 비어 있거나 중복입니다.");
                    continue;
                }
                if (row._elementSlots < 0 || row._elementSlots > SpellGrammar.MAX_ELEMENT_SLOTS
                    || row._modifierSlots < 0 || row._modifierSlots > SpellGrammar.MAX_MODIFIER_SLOTS)
                {
                    log.Add(null, "behaviorGrades:" + row._grade, "슬롯 수가 범위를 벗어났습니다 (속성 0~"
                        + SpellGrammar.MAX_ELEMENT_SLOTS + ", 모디 0~" + SpellGrammar.MAX_MODIFIER_SLOTS + ").");
                    continue;
                }
                order.Add(row._grade);
                slots.Add(row._grade, (row._elementSlots, row._modifierSlots));
            }
        }

        /// <summary>룬 ID로 정의를 반환하며 알 수 없는 ID이면 예외를 발생시킨다.</summary>
        public RuneDefinition Get(string id) => _byId[id];

        /// <summary>룬 ID의 존재 여부와 정의를 반환한다.</summary>
        public bool TryGet(string id, out RuneDefinition rune)
        {
            rune = null;
            return !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out rune);
        }

        /// <summary>
        /// 그래프 노드 하나가 차지하는 RAM을 반환한다. behavior 노드는 행동·장착 속성·장착 모디 룬 RAM의 합이며
        /// 슬롯 값이 잘못됐으면 해당 항목만 0으로 뺀다. 다른 노드는 룬 자체 RAM에 속성 드롭다운을 가진 노드(Shape·Apply)는
        /// 선택한 속성 룬의 RAM을 더한다. 알 수 없는 룬이면 0을 반환한다.
        /// </summary>
        public int NodeRam(GraphNode node)
        {
            if (!TryGet(node.RuneId, out RuneDefinition rune)) return 0;
            if (rune.Category != SpellGrammar.CATEGORY_BEHAVIOR)
            {
                int baseRam = rune.Ram;
                if (TryGetNodeElement(node, out ElementDefinition element) && TryGet(element.RuneId, out RuneDefinition elementRune)) baseRam += elementRune.Ram;
                return baseRam;
            }
            int ram = rune.Ram;
            for (int index = 0; index < SpellGrammar.MAX_ELEMENT_SLOTS; index++)
            {
                string value = node.GetText(SpellGrammar.ElementSlotKey(index), null);
                if (!string.IsNullOrEmpty(value) && TryGetElement(value, out ElementDefinition attached)
                    && TryGet(attached.RuneId, out RuneDefinition attachedRune)) ram += attachedRune.Ram;
            }
            for (int index = 0; index < SpellGrammar.MAX_MODIFIER_SLOTS; index++)
            {
                string value = node.GetText(SpellGrammar.ModifierSlotKey(index), null);
                if (!string.IsNullOrEmpty(value) && SpellGrammar.TryParseStockKey(value, out string modifierId, out _)
                    && TryGet(modifierId, out RuneDefinition modifierRune)) ram += modifierRune.Ram;
            }
            return ram;
        }

        /// <summary>노드의 element 파라미터(없으면 룬 기본값)에 해당하는 속성을 반환한다. 속성 드롭다운이 없는 노드이거나 값이 없으면 false를 반환한다.</summary>
        public bool TryGetNodeElement(GraphNode node, out ElementDefinition element)
        {
            element = null;
            if (!TryGet(node.RuneId, out RuneDefinition rune)) return false;
            foreach (ParameterDefinition param in rune.Params)
            {
                if (param.Id == SpellGrammar.ELEMENT_PARAM) return TryGetElement(node.GetText(SpellGrammar.ELEMENT_PARAM, param.DefaultText), out element);
            }
            return false;
        }

        /// <summary>
        /// 효과 룬이 대상 룬(Behavior 또는 프리셋 호출)에 효과를 내는지 반환한다. 효과 대상(_modifierTargets)에 있어야 하고,
        /// 동작 모드면 모드 규칙이 행동 규칙의 성질로도 허용해야 한다. 효과 대상에 없는 조합은 부착은 허용하되 실행·비용 계산에서 무시한다.
        /// </summary>
        public bool IsModifierCompatible(string modifierId, string targetId)
        {
            if (!_modifierTargets.TryGetValue(modifierId, out HashSet<string> targets) || !targets.Contains(targetId)) return false;
            if (!SpellModifierRuleRegistry.TryGetByRuneId(modifierId, out SpellModifierRules modifierRules)) return true;
            return !SpellBehaviorRuleRegistry.TryGetByRuneId(targetId, out SpellBehaviorRules behaviorRules)
                || modifierRules.CanAttachTo(behaviorRules);
        }

        /// <summary>속성 식별자(neutral, fire 등)의 존재 여부와 정의를 반환한다.</summary>
        public bool TryGetElement(string id, out ElementDefinition element)
        {
            element = null;
            return !string.IsNullOrEmpty(id) && _elementsById.TryGetValue(id, out element);
        }

        /// <summary>속성 룬 ID(예: element.fire)가 정의하는 속성을 찾아 반환하며 속성 룬이 아니면 false를 반환한다.</summary>
        public bool TryGetRuneElement(string runeId, out ElementDefinition element)
        {
            element = null;
            foreach (ElementDefinition candidate in _elements)
            {
                if (candidate.RuneId != runeId) continue;
                element = candidate;
                return true;
            }
            return false;
        }

        /// <summary>룬 데이터의 태그 목록을 순서대로 만들고 비어 있거나 중복된 순서를 오류로 기록한다.</summary>
        private static List<string> BuildTags(RuneRowData row, TableErrorLog log)
        {
            var result = new List<string>();
            if (row._tags == null)
            {
                return result;
            }
            var orders = new HashSet<int>();
            foreach (RuneTagData tagRow in row._tags)
            {
                if (tagRow == null || string.IsNullOrEmpty(tagRow._tag))
                {
                    log.Add(row._id, "tags", "비어 있거나 값이 없는 태그 행이 있습니다.");
                    continue;
                }
                if (!orders.Add(tagRow._order))
                {
                    log.Add(row._id, "tags:order", "태그 순서가 중복입니다: " + tagRow._order);
                }
                result.Add(tagRow._tag);
            }
            return result;
        }

        /// <summary>룬 데이터의 포트 목록을 순서대로 포트 정의로 만들고 종류·방향·최대 연결 수·상태·중복 규칙을 검증한다.</summary>
        private static List<PortDefinition> BuildPorts(RuneRowData row, TableErrorLog log)
        {
            var result = new List<PortDefinition>();
            if (row._ports == null)
            {
                return result;
            }
            var orders = new HashSet<int>();
            var portKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (RunePortData portRow in row._ports)
            {
                if (portRow == null)
                {
                    log.Add(row._id, "ports", "비어 있는 포트 항목이 있습니다.");
                    continue;
                }
                var port = new PortDefinition(portRow);
                if (!orders.Add(portRow._order))
                {
                    log.Add(row._id, "ports:order", "포트 표시 순서가 중복입니다: " + portRow._order);
                }
                if (!portKeys.Add(port.Direction + " " + port.Id))
                {
                    log.Add(row._id, "ports", "중복된 포트입니다: " + port.Direction + " " + port.Id);
                }
                if ((port.Kind != "exec" && port.Kind != "mod" && port.Kind != "chain" && port.Kind != SpellGrammar.ELEMENT_KIND)
                    || (port.Direction != "in" && port.Direction != "out") || port.Max < 0
                    || ((port.Kind == "chain" || port.Kind == SpellGrammar.ELEMENT_KIND) && port.Max != 1)
                    || (port.Direction == "in" && port.Kind == "exec" && port.Max != 1))
                {
                    log.Add(row._id, "ports:" + port.Id, "유효하지 않은 룬 포트입니다 (종류 exec/mod/chain/element, 방향 in/out, chain·element와 exec 입력은 최대 1).");
                }
                if ((port.Status != SpellGrammar.PORT_ACTIVE && !port.IsDeprecated)
                    || (port.IsDeprecated && (port.Kind != "exec" || port.Direction != "out")))
                {
                    log.Add(row._id, "ports:" + port.Id + ":status", "active(빈 칸) 또는 deprecated여야 하며, deprecated는 exec 출력 포트에만 쓸 수 있습니다.");
                }
                result.Add(port);
            }
            return result;
        }

        /// <summary>룬 데이터의 파라미터 목록과 선택지를 순서대로 파라미터 정의로 만들고 종류별 값 규칙·중복을 검증한다.</summary>
        private static List<ParameterDefinition> BuildParameters(RuneRowData row, TableErrorLog log)
        {
            var result = new List<ParameterDefinition>();
            if (row._params == null)
            {
                return result;
            }
            var orders = new HashSet<int>();
            var paramIds = new HashSet<string>(StringComparer.Ordinal);
            var optionKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (RuneParamData paramRow in row._params)
            {
                if (paramRow == null)
                {
                    log.Add(row._id, "params", "비어 있는 파라미터 항목이 있습니다.");
                    continue;
                }
                if (!orders.Add(paramRow._order))
                {
                    log.Add(row._id, "params:order", "파라미터 표시 순서가 중복입니다: " + paramRow._order);
                }
                if (string.IsNullOrEmpty(paramRow._paramId) || !paramIds.Add(paramRow._paramId))
                {
                    log.Add(row._id, "params:paramId", "비어 있거나 중복된 파라미터 ID입니다: " + paramRow._paramId);
                }

                var options = new List<string>();
                if (paramRow._options != null)
                {
                    foreach (RuneParamOptionData optionRow in paramRow._options)
                    {
                        if (optionRow == null || string.IsNullOrEmpty(optionRow._option))
                        {
                            log.Add(row._id, "params:" + paramRow._paramId + ":options", "비어 있거나 값이 없는 선택지 항목이 있습니다.");
                            continue;
                        }
                        if (!optionKeys.Add(paramRow._paramId + " " + optionRow._order))
                        {
                            log.Add(row._id, "params:" + paramRow._paramId + ":options:order", "선택지 순서가 중복입니다: " + optionRow._order);
                        }
                        options.Add(optionRow._option);
                    }
                }

                var param = new ParameterDefinition(paramRow, options);
                bool isInvalid = (param.Kind != "number" && param.Kind != "enum" && param.Kind != "text")
                    || (param.Kind == "number" && (param.Min > param.Max || param.DefaultNumber < param.Min
                        || param.DefaultNumber > param.Max || param.Step <= 0f))
                    || (param.Kind == "enum" && !options.Contains(param.DefaultText))
                    || (param.Kind == "text" && param.DefaultText.Length > 80);
                if (isInvalid)
                {
                    log.Add(row._id, "params:" + param.Id, "유효하지 않은 룬 파라미터입니다 (number는 min≤기본값≤max·step>0, enum은 기본값이 선택지에 있어야 함).");
                }
                result.Add(param);
            }
            return result;
        }

        /// <summary>JSON 원본에서 로드한 Modifier 등급 효과·스테이지 표를 카탈로그에 연결한다.</summary>
        public void AttachModifierGrades(ModifierGradeTable modifierGrades)
        {
            _modifierGrades = modifierGrades;
        }

        /// <summary>유도 단계 수치를 읽고 mod.homing 전용·이름 중복 없음 규칙을 검증한다.</summary>
        private static List<HomingTierDefinition> BuildHomingTiers(RuneRowData row, TableErrorLog log)
        {
            var result = new List<HomingTierDefinition>();
            bool isHomingModifier = row._id == "mod.homing";
            if (!isHomingModifier)
            {
                if (row._homingTiers != null && row._homingTiers.Count > 0)
                    log.Add(row._id, "homingTiers", "유도 단계는 mod.homing에만 정의할 수 있습니다.");
                return result;
            }
            if (row._homingTiers == null || row._homingTiers.Count == 0)
            {
                log.Add(row._id, "homingTiers", "유도 단계 수치가 비어 있습니다.");
                return result;
            }
            var tiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (RuneHomingTierData tierRow in row._homingTiers)
            {
                if (tierRow == null || string.IsNullOrEmpty(tierRow._tier))
                {
                    log.Add(row._id, "homingTiers", "비어 있거나 이름이 없는 유도 단계가 있습니다.");
                    continue;
                }
                if (!tiers.Add(tierRow._tier))
                {
                    log.Add(row._id, "homingTiers:" + tierRow._tier, "유도 단계 이름이 중복되었습니다.");
                    continue;
                }
                if (tierRow._homingTurn <= 0f || tierRow._homingRange <= 0f)
                {
                    log.Add(row._id, "homingTiers:" + tierRow._tier, "유도 회전과 사거리는 양수여야 합니다.");
                }
                result.Add(new HomingTierDefinition(tierRow));
            }
            return result;
        }

        /// <summary>룬 데이터의 속성 정의를 만들고 분류·카테고리·식별자 중복을 검증한다. 값이 전부 비어 있으면 속성 없음으로 무시한다.</summary>
        private static void AddElement(List<ElementDefinition> elements, HashSet<string> elementIds,
            RuneDefinition rune, RuneElementData row, TableErrorLog log)
        {
            if (!row.HasAnyValue) return; // JsonUtility는 JSON에 없는 클래스 필드를 빈 인스턴스로 만든다.
            var element = new ElementDefinition(row, rune.Id);
            if (!ELEMENT_CATEGORIES.Contains(element.Category))
            {
                log.Add(rune.Id, "element:category", "elemental 또는 functional이어야 합니다: " + element.Category);
            }
            if (rune.Category != SpellGrammar.CATEGORY_ELEMENT)
            {
                log.Add(rune.Id, "element", "element 카테고리 룬에만 속성을 정의할 수 있습니다.");
            }
            if (string.IsNullOrEmpty(row._internalValue))
            {
                log.Add(rune.Id, "element:internalValue", "값이 비어 있습니다.");
            }
            if (string.IsNullOrEmpty(element.Id) || !elementIds.Add(element.Id))
            {
                log.Add(rune.Id, "element:id", "비어 있거나 중복된 속성 식별자입니다: " + element.Id);
            }
            elements.Add(element);
        }

        /// <summary>
        /// 효과 대상이 존재하는 Behavior·프리셋 호출 룬인지, 중복이 없는지, 모든 효과 룬에 대상이 있는지 검증한다.
        /// </summary>
        private static void ValidateModifierTargets(List<(RuneDefinition rune, List<string> targets)> modifiers,
            List<RuneDefinition> definitions, TableErrorLog log)
        {
            var withTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach ((RuneDefinition rune, List<string> targets) in modifiers)
            {
                if (rune.Category != SpellGrammar.CATEGORY_MODIFIER)
                {
                    log.Add(rune.Id, "modifierTargets", "modifier 카테고리 룬에만 효과 대상을 정의할 수 있습니다.");
                    continue;
                }
                withTargets.Add(rune.Id);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string target in targets)
                {
                    if (string.IsNullOrEmpty(target) || !seen.Add(target))
                    {
                        log.Add(rune.Id, "modifierTargets", "비어 있거나 중복된 효과 대상입니다: " + target);
                        continue;
                    }
                    RuneDefinition targetRune = definitions.Find(candidate => candidate.Id == target);
                    if (targetRune == null)
                    {
                        log.Add(rune.Id, "modifierTargets", "없는 룬을 참조합니다: " + target);
                    }
                    else if (targetRune.Category != SpellGrammar.CATEGORY_BEHAVIOR && targetRune.Id != SpellGrammar.CALL_RUNE)
                    {
                        log.Add(rune.Id, "modifierTargets", "Behavior 또는 프리셋 호출 룬이어야 합니다: " + target);
                    }
                }
            }
            foreach (RuneDefinition rune in definitions)
            {
                if (rune.Category == SpellGrammar.CATEGORY_MODIFIER && !withTargets.Contains(rune.Id))
                {
                    log.Add(rune.Id, "modifierTargets", "효과 룬의 적용 대상이 없습니다.");
                }
            }
        }

        /// <summary>효과 룬 ID가 키이고 대상 룬 ID 집합이 값인 조회표를 만든다.</summary>
        private static Dictionary<string, HashSet<string>> BuildModifierTargets(
            List<(RuneDefinition rune, List<string> targets)> modifiers)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach ((RuneDefinition rune, List<string> targets) in modifiers)
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (string target in targets)
                {
                    set.Add(target);
                }
                result[rune.Id] = set;
            }
            return result;
        }

        /// <summary>룬의 표시 이름·카테고리·해금 방식이 비어 있지 않은지 확인하고 오류를 기록한다.</summary>
        private static void RequireText(RuneDefinition rune, RuneRowData row, TableErrorLog log)
        {
            if (string.IsNullOrEmpty(row._name)) log.Add(rune.Id, "name", "값이 비어 있습니다.");
            if (string.IsNullOrEmpty(row._category)) log.Add(rune.Id, "category", "값이 비어 있습니다.");
            if (string.IsNullOrEmpty(row._unlockType)) log.Add(rune.Id, "unlockType", "값이 비어 있습니다.");
        }

        /// <summary>룬의 카테고리·해금 방식·RAM·에너지 값 규칙을 검증하고 위반을 오류 로그에 기록한다.</summary>
        private static void ValidateRune(RuneDefinition rune, TableErrorLog log)
        {
            if (!CATEGORIES.Contains(rune.Category)) log.Add(rune.Id, "category", "알 수 없는 카테고리입니다: " + rune.Category);
            if (!UNLOCK_TYPES.Contains(rune.UnlockType)) log.Add(rune.Id, "unlockType", "알 수 없는 해금 방식입니다: " + rune.UnlockType);
            if (rune.Ram < 0) log.Add(rune.Id, "ram", "음수일 수 없습니다.");
            if (rune.Energy < 0f) log.Add(rune.Id, "energy", "음수일 수 없습니다.");
            if (rune.UnlockCost < 0) log.Add(rune.Id, "unlockCost", "음수일 수 없습니다.");
            foreach (ResourceAmount cost in rune.Costs.Amounts)
                if (cost.Amount < 0) log.Add(rune.Id, "resourceCosts:" + cost.Resource, "음수일 수 없습니다.");
            if (rune.EnergyMult <= 0f) log.Add(rune.Id, "energyMult", "0보다 커야 합니다.");
            bool isImplementationRune = rune.Category == SpellGrammar.CATEGORY_CORE || rune.Category == SpellGrammar.CATEGORY_INTERNAL;
            bool isStarterRune = STARTER_RUNE_IDS.Contains(rune.Id);
            bool hasValidUnlockType = isImplementationRune || isStarterRune
                ? rune.UnlockType == "start"
                : rune.UnlockType == "tree" || rune.UnlockType == "bench";
            if (!hasValidUnlockType)
                log.Add(rune.Id, "unlockType", "시작 룬은 start, 그 외 플레이어 룬은 tree 또는 bench로 설정해야 합니다.");
        }

        /// <summary>룬의 자원별 기본 비용을 만들고 빈 ID·음수·중복을 검증한다. 기존 energy는 mana 비용으로 읽는다.</summary>
        private static ResourceCostSet BuildCosts(RuneRowData row, TableErrorLog log)
        {
            var costs = new List<ResourceAmount>();
            if (row._resourceCosts != null && row._resourceCosts.Count > 0)
            {
                if (row._energy != 0f)
                    log.Add(row._id, "energy", "자원별 비용을 쓰면 energy는 0이어야 합니다.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (RuneResourceCostData cost in row._resourceCosts)
                {
                    if (cost == null || string.IsNullOrWhiteSpace(cost._resource) || !seen.Add(cost._resource))
                    {
                        log.Add(row._id, "resourceCosts", "자원 ID가 비어 있거나 중복되었습니다.");
                        continue;
                    }
                    if (cost._amount < 0f || float.IsNaN(cost._amount) || float.IsInfinity(cost._amount))
                        log.Add(row._id, "resourceCosts:" + cost._resource, "비용은 유한한 0 이상이어야 합니다.");
                    costs.Add(new ResourceAmount(cost._resource, cost._amount));
                }
            }
            else if (row._energy != 0f)
            {
                costs.Add(new ResourceAmount("mana", row._energy));
            }
            return new ResourceCostSet(costs);
        }

        /// <summary>Shape 비용률을 만들고 비Shape 사용·빈 자원·음수 단가·중복 자원을 검증한다.</summary>
        private static IReadOnlyList<ShapeCostRateDefinition> BuildShapeCostRates(RuneRowData row, TableErrorLog log)
        {
            var rates = new List<ShapeCostRateDefinition>();
            if (row._shapeCostRates == null) return rates;
            if (row._shapeCostRates.Count > 0 && row._category != SpellGrammar.CATEGORY_SHAPE)
                log.Add(row._id, "shapeCostRates", "Shape 카테고리에만 비용률을 정의할 수 있습니다.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (RuneShapeCostRateData rate in row._shapeCostRates)
            {
                if (rate == null || string.IsNullOrWhiteSpace(rate._resource) || !seen.Add(rate._resource))
                {
                    log.Add(row._id, "shapeCostRates", "자원 ID가 비어 있거나 중복되었습니다.");
                    continue;
                }
                if (rate._rate < 0f || float.IsNaN(rate._rate) || float.IsInfinity(rate._rate))
                    log.Add(row._id, "shapeCostRates:" + rate._resource, "단가는 유한한 0 이상이어야 합니다.");
                rates.Add(new ShapeCostRateDefinition(rate));
            }
            return rates;
        }

        /// <summary>노드 기본 시간과 Shape 면적 단가를 검증하여 유한한 0 이상 값인지 확인한다.</summary>
        private static void ValidateExecutionTime(RuneRowData row, TableErrorLog log)
        {
            if (row._executionSeconds < 0f || float.IsNaN(row._executionSeconds) || float.IsInfinity(row._executionSeconds))
                log.Add(row._id, "executionSeconds", "실행 시간은 유한한 0 이상이어야 합니다.");
            if (row._executionSecondsPerArea < 0f || float.IsNaN(row._executionSecondsPerArea) || float.IsInfinity(row._executionSecondsPerArea))
                log.Add(row._id, "executionSecondsPerArea", "면적 단가는 유한한 0 이상이어야 합니다.");
            if (row._executionSecondsPerArea > 0f && row._category != SpellGrammar.CATEGORY_SHAPE)
                log.Add(row._id, "executionSecondsPerArea", "면적 단가는 Shape 카테고리에만 정의할 수 있습니다.");
        }

        /// <summary>룬 효과 수치가 음수가 아니고 형태 룬에 필수 수치가 있는지 검증하고 위반을 오류 로그에 기록한다.</summary>
        private static void ValidateStats(RuneDefinition rune, RuneStats stats, TableErrorLog log, string field)
        {
            float[] numbers = { stats.Damage, stats.Speed, stats.Radius, stats.Lifetime, stats.Offset, stats.OrbitRadius,
                stats.AngularSpeed, stats.EntrySpeed, stats.HitInterval, stats.TickInterval, stats.DamageMultiplier, stats.RadiusMultiplier,
                stats.SpeedMultiplier, stats.DurationMultiplier, stats.PierceLoss, stats.HomingTurn, stats.HomingRange, stats.ArcRange, stats.ArcMultiplier,
                stats.LearningMultiplier, stats.SpreadAngle, stats.ShieldAmount, stats.ShieldSeconds, stats.ConeAngle, stats.ExpandSeconds, stats.WarnSeconds,
                stats.BeamWidth, stats.BeamLength, stats.MinimumLifetime };
            foreach (float value in numbers)
            {
                if (value < 0f)
                {
                    log.Add(rune.Id, field, "효과 수치는 음수일 수 없습니다.");
                    break;
                }
            }
            if (stats.Count < 0 || stats.Pierce < 0 || stats.ArcTargets < 0
                || (rune.Category == "form" && (stats.Count <= 0 || stats.Damage <= 0f || stats.Radius <= 0f)))
                log.Add(rune.Id, field, "유효하지 않은 형태 룬 수치입니다 (form은 count·damage·radius가 0보다 커야 함).");
            if (rune.Id == SpellGrammar.CONE_RUNE && (stats.ConeAngle <= 0f || stats.ConeAngle > 360f))
                log.Add(rune.Id, field + ":coneAngle", "부채꼴 각도는 0보다 크고 360 이하여야 합니다.");
        }
    }
}
