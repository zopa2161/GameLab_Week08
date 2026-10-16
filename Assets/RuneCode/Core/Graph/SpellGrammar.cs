namespace RuneCode
{
    /// <summary>
    /// 마법 비주얼 스크립팅 문법(Trigger → Shape(Element) → Behavior, Trigger → Apply(Element))의 블록 ID·카테고리·포트와
    /// 컴파일 내부 표현인 Inline Magic 파라미터 값 사이의 대응을 정의한다. 규범은 「마법 비주얼 스크립팅 — 문법 구현 백서」다.
    /// </summary>
    public static class SpellGrammar
    {
        public const string CATEGORY_SHAPE = "shape";
        public const string CATEGORY_BEHAVIOR = "behavior";
        public const string CATEGORY_ELEMENT = "element";
        public const string CATEGORY_METHOD = "method";
        public const string CATEGORY_INTERNAL = "internal";
        public const string CATEGORY_CORE = "core";
        public const string CATEGORY_MODIFIER = "modifier";
        public const string CATEGORY_FLOW = "flow";
        public const string CATEGORY_LEGACY_FORM = "form";

        public const string DIRECTION_IN = "in";
        public const string DIRECTION_OUT = "out";

        public const string EXEC_KIND = "exec";
        public const string MODIFIER_KIND = "mod";
        public const string CHAIN_KIND = "chain";
        public const string ELEMENT_KIND = "element";
        public const string ELEMENT_PORT = "element";

        public const string EXEC_PORT = "exec";
        public const string MODIFIER_PORT = "mod";
        public const string CHAIN_IN = "in";
        public const string CHAIN_OUT = "next";
        public const string ON_HIT_PORT = "onHit";
        public const string ON_EXPIRE_PORT = "onExpire";
        public const string ON_FIRST_HIT_OR_EXPIRE_PORT = "onFirstHitOrExpire";
        public const string ON_COMPLETE_PORT = "onComplete";
        public const string LEGACY_COMPLETE_PORT = ON_COMPLETE_PORT;
        public const string BODY_PORT = "body";

        public const string PORT_ACTIVE = "active";
        public const string PORT_DEPRECATED = "deprecated";

        public const string FORM_BOLT = "bolt";
        public const string FORM_BURST = "burst";
        public const string FORM_LEGACY_BURST = "legacy_burst";
        public const string FORM_ZONE = "zone";

        public const string MAGIC_TYPE_SPHERE = "sphere";
        public const string MAGIC_TYPE_BOX = "box";
        public const string MAGIC_TYPE_BUFF = "buff";
        public const string MAGIC_TYPE_CONE = "cone";
        public const string CONE_RUNE = "shape.cone";

        public const string INLINE_RUNE = "magic.inline";
        public const string CALL_RUNE = "spell.call";
        public const string CORE_RUNE = "core.cast";
        public const string APPLY_RUNE = "behavior.apply";
        public const string FORM_VALUE_REMAIN = "remain";

        public const string ELEMENT_PARAM = "element";
        public const string TRIGGER_PARAM = "trigger";
        public const string POWER_PARAM = "power";
        public const string BUFF_DURATION_PARAM = "buffDuration";
        public const string SHAPE_RADIUS_PARAM = "radius";
        public const string BOX_WIDTH_PARAM = "boxWidth";
        public const string BOX_HEIGHT_PARAM = "boxHeight";
        public const string BOX_LENGTH_PARAM = "boxLength";
        public const string CONE_DISTANCE_PARAM = "coneDistance";
        public const string CONE_ANGLE_PARAM = "coneAngle";
        public const string BOX_DIRECTION_PARAM = "boxDirection";
        // 형태를 행동 노드에서 고르던 이전 파라미터 키다. 형태는 행동 정의의 고정값을 쓰며 저장 데이터를 읽을 때 이 키를 지운다.
        public const string LEGACY_SHAPE_PARAM = "shape";
        public const string MODIFIER_GRADE_PARAM = "grade";
        public const string ELEMENT_SLOT_PREFIX = "element";
        public const string MODIFIER_SLOT_PREFIX = "mod";
        public const int MAX_ELEMENT_SLOTS = 2;
        public const int MAX_MODIFIER_SLOTS = 6;
        public const string DEFAULT_BEHAVIOR_GRADE = "C";
        public const int UNLIMITED_PIERCE_COUNT = int.MaxValue;
        // 관통 수가 이 값 이상이면 사실상 무제한으로 보고 무제한 관통과 같게 다룬다(예: 등급표의 999).
        public const int UNLIMITED_PIERCE_THRESHOLD = 100;

        public const string TRIGGER_ON_ATTACK = "onAttack";
        public const string TRIGGER_ON_MOVE = "onMove";
        public const string TRIGGER_ON_DASH_START = "onDashStart";
        public const string TRIGGER_ON_DASH_END = "onDashEnd";
        public const string TRIGGER_ON_HIT_TAKEN = "onHitTaken";

        public const string ELEMENT_NEUTRAL = "neutral";
        public const string ELEMENT_CATEGORY_ELEMENTAL = "elemental";
        public const string ELEMENT_CATEGORY_FUNCTIONAL = "functional";

        private const string SHAPE_PREFIX = "shape.";
        private const string ELEMENT_PREFIX = "element.";

        /// <summary>Inline Magic의 magicType 값(sphere 등)에 해당하는 Shape 룬 ID(shape.sphere 등)를 반환한다.</summary>
        public static string ShapeRune(string magicType) => SHAPE_PREFIX + magicType;

        /// <summary>Inline Magic의 form 값에 해당하는 Behavior 룬 ID를 행동 규칙 등록 목록에서 찾아 반환한다. 없으면 빈 문자열이다.</summary>
        public static string BehaviorRune(string form)
        {
            return SpellBehaviorRuleRegistry.TryGetByFormValue(form, out SpellBehaviorRules rules) ? rules.RuneId : "";
        }

        /// <summary>Shape 블록(shape.sphere 등)이면 Inline Magic의 magicType 값(sphere 등)을, 아니면 null을 반환한다.</summary>
        public static string MagicTypeOf(string runeId) => ValueOf(runeId, SHAPE_PREFIX);

        /// <summary>
        /// Behavior 블록이면 Inline Magic의 form 값을 반환한다. 내부 값은 이전 문법을 유지한다
        /// (Launch=launch, Burst=explosion, Persist·Apply=remain). Behavior가 아니면 null을 반환한다.
        /// </summary>
        public static string FormOf(string runeId)
        {
            if (SpellBehaviorRuleRegistry.TryGetByRuneId(runeId, out SpellBehaviorRules rules)) return rules.FormValue;
            // Apply는 비활성 룬이며 버프로 처리한다.
            if (runeId == APPLY_RUNE) return FORM_VALUE_REMAIN;
            return null;
        }

        /// <summary>Inline Magic의 form 값에 해당하는 런타임 종류(bolt 등)를 반환한다. 알 수 없는 값은 빈 문자열이다.</summary>
        public static string KindOf(string form)
        {
            return SpellBehaviorRuleRegistry.TryGetByFormValue(form, out SpellBehaviorRules rules) ? rules.Kind : "";
        }

        /// <summary>속성 식별자(neutral, fire 등)에 해당하는 속성 정의 룬 ID(element.neutral 등)를 반환한다.</summary>
        public static string ElementRune(string elementId) => ELEMENT_PREFIX + elementId;

        /// <summary>Modifier 소지량 표에서 runeId와 문자열 등급을 결합한 키를 반환한다.</summary>
        public static string ModifierStockKey(string runeId, string grade)
        {
            return runeId + "#" + grade;
        }

        /// <summary>행동 노드의 속성 슬롯 키를 반환한다. 예: ElementSlotKey(0)은 "element0".</summary>
        public static string ElementSlotKey(int index) => ELEMENT_SLOT_PREFIX + index;

        /// <summary>행동 노드의 모디파이어 슬롯 키를 반환한다. 예: ModifierSlotKey(0)은 "mod0".</summary>
        public static string ModifierSlotKey(int index) => MODIFIER_SLOT_PREFIX + index;

        /// <summary>
        /// 슬롯 키(element0·mod0 등)를 종류와 순번으로 나눈다. isElement는 속성 슬롯 여부다.
        /// 최대 슬롯 수 밖이거나 슬롯 키가 아니면 false를 반환한다.
        /// </summary>
        public static bool TryParseSlotKey(string slotKey, out bool isElement, out int index)
        {
            isElement = false;
            index = -1;
            if (string.IsNullOrEmpty(slotKey)) return false;
            for (int i = 0; i < MAX_ELEMENT_SLOTS; i++)
            {
                if (slotKey != ElementSlotKey(i)) continue;
                isElement = true;
                index = i;
                return true;
            }
            for (int i = 0; i < MAX_MODIFIER_SLOTS; i++)
            {
                if (slotKey != ModifierSlotKey(i)) continue;
                index = i;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Modifier 소지량 키를 runeId와 등급으로 나눈다. ModifierStockKey의 역연산이며 마지막 '#'을 기준으로 나눈다.
        /// 키에 '#'이 없거나 룬 ID가 비어 있으면 false를 반환한다. 등급은 빈 문자열일 수 있다.
        /// </summary>
        public static bool TryParseStockKey(string key, out string runeId, out string grade)
        {
            runeId = null;
            grade = null;
            int separator = string.IsNullOrEmpty(key) ? -1 : key.LastIndexOf('#');
            if (separator < 1) return false;
            runeId = key.Substring(0, separator);
            grade = key.Substring(separator + 1);
            return true;
        }

        /// <summary>노드의 문자열 등급을 읽고 없으면 원본 표에서 해당 Modifier의 최저 등급을 반환한다.</summary>
        public static string GetModifierGrade(RuneDefinition rune, GraphNode node, ModifierGradeTable modifierGrades)
        {
            string grade = node == null ? null : node.GetText(MODIFIER_GRADE_PARAM, null);
            return string.IsNullOrEmpty(grade) ? modifierGrades?.GetLowestAvailableGrade(rune.Id) ?? string.Empty : grade;
        }

        /// <summary>Shape의 체인 출력에서 Apply를 제외한 Behavior의 체인 입력으로 가는 연결인지 반환한다.</summary>
        public static bool IsValidChainLink(RuneDefinition from, RuneDefinition to)
        {
            return from.Category == CATEGORY_SHAPE && to.Category == CATEGORY_BEHAVIOR && to.Id != APPLY_RUNE;
        }

        /// <summary>
        /// 개체 이벤트 출력 포트(onHit, onExpire, onFirstHitOrExpire, 이전 Behavior의 onComplete)인지 반환한다.
        /// Repeat의 onComplete도 같은 ID지만 버전 3 이전 그래프에는 없으므로 이전 이벤트 표시 대상이 되지 않는다.
        /// </summary>
        public static bool IsEventPort(string portId)
        {
            return portId == ON_HIT_PORT || portId == ON_EXPIRE_PORT || portId == ON_FIRST_HIT_OR_EXPIRE_PORT || portId == LEGACY_COMPLETE_PORT;
        }

        /// <summary>룬 ID가 지정 접두사로 시작하면 접두사 뒤 값을, 아니면 null을 반환한다.</summary>
        private static string ValueOf(string runeId, string prefix)
        {
            if (string.IsNullOrEmpty(runeId) || !runeId.StartsWith(prefix, System.StringComparison.Ordinal)) return null;
            return runeId.Substring(prefix.Length);
        }
    }
}
