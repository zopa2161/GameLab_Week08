using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode.Editor
{
    /// <summary>조합 셋에 들어 있는 수동 제외 규칙 목록이다. 조합 생성기가 패턴에 일치하는 조합을 측정에서 뺀다.</summary>
    [Serializable]
    public sealed class DpsExclusionList
    {
        [Header("버전")]
        [SerializeField] private int _schemaVersion = MeasurementCodes.SCHEMA_VERSION;

        [Header("규칙")]
        [SerializeField] private List<DpsExclusionEntry> _entries = new List<DpsExclusionEntry>();

        public int SchemaVersion => _schemaVersion;
        public List<DpsExclusionEntry> Entries => _entries;
    }
}
