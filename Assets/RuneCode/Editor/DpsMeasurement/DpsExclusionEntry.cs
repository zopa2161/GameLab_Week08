using System;

using UnityEngine;

namespace RuneCode.Editor
{
    /// <summary>측정 조합 수동 제외 규칙 한 항목이다. 패턴에 일치하는 조합 ID를 사유와 함께 측정에서 뺀다.</summary>
    [Serializable]
    public sealed class DpsExclusionEntry
    {
        [Header("규칙")]
        [SerializeField, Tooltip("조합 ID 또는 '*' 와일드카드 패턴. 예: S:P2>*")] private string _pattern;
        [SerializeField, Tooltip("제외 사유. excluded.csv에는 manual:<사유>로 기록된다.")] private string _reason;

        public string Pattern => _pattern;
        public string Reason => _reason;
    }
}
