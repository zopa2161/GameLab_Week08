using System;

using UnityEngine;

namespace RuneCode.Editor
{
    /// <summary>측정에서 빠진 조합 하나다. 조합 ID와 처음 걸린 제외 사유를 보관해 excluded.csv로 기록한다.</summary>
    [Serializable]
    public sealed class DpsExcludedBuild
    {
        [Header("제외 항목")]
        [SerializeField] private string _buildId;
        [SerializeField] private string _reason;

        public string BuildId => _buildId;
        public string Reason => _reason;

        /// <summary>조합 ID와 제외 사유로 항목을 만든다.</summary>
        public DpsExcludedBuild(string buildId, string reason)
        {
            _buildId = buildId;
            _reason = reason;
        }
    }
}
