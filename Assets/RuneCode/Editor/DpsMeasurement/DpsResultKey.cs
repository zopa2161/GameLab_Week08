namespace RuneCode.Editor
{
    /// <summary>
    /// 측정 결과의 조합·조건 쌍을 한 문자열 키로 만든다. 부분 실행 파일 재개와 이전 결과 비교에서 같은 작업을 찾는 데 쓴다.
    /// </summary>
    public static class DpsResultKey
    {
        private const string KEY_SEPARATOR = "|";

        /// <summary>조합 ID와 조건 ID를 '|'로 이어 반환한다. 같은 조합·조건이면 항상 같은 키가 나온다.</summary>
        public static string Create(string buildId, string conditionId)
        {
            return buildId + KEY_SEPARATOR + conditionId;
        }
    }
}
