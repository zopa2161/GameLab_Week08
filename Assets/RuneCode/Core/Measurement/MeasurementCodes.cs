namespace RuneCode
{
    /// <summary>
    /// DPS 측정 데이터 계약의 스키마 버전과 문자열 코드 값이다. 측정 입력·결과 JSON을 사람이 읽고 편집할 수 있도록
    /// 상황·입력 방식·상태·병목·경고를 정수 enum 대신 고정 문자열로 기록한다.
    /// </summary>
    public static class MeasurementCodes
    {
        public const int SCHEMA_VERSION = 1;

        public const string SITUATION_SINGLE = "single";
        public const string SITUATION_SWARM = "swarm";
        public const string SITUATION_PENTAGON = "pentagon";

        public const string INPUT_HOLD_ATTACK = "holdAttack";

        public const string STATUS_MEASURED = "measured";
        public const string STATUS_COMPILE_ERROR = "compileError";

        public const string BOTTLENECK_NONE = "none";
        public const string BOTTLENECK_ENERGY = "energy";
        public const string BOTTLENECK_COOLDOWN = "cooldown";

        public const string WARNING_ENTITY_LIMIT = "entityLimit";
        public const string WARNING_EXECUTION_LIMIT = "executionLimit";
        public const string WARNING_OUT_OF_RANGE = "outOfRange";
        public const string WARNING_REPEAT_VARIANCE = "repeatVariance";

        /// <summary>상황 코드가 정의된 값인지 반환한다.</summary>
        public static bool IsSituation(string value) => value == SITUATION_SINGLE || value == SITUATION_SWARM || value == SITUATION_PENTAGON;

        /// <summary>입력 방식 코드가 정의된 값인지 반환한다.</summary>
        public static bool IsInputMode(string value) => value == INPUT_HOLD_ATTACK;
    }
}
