using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode.Editor
{
    /// <summary>dps_exclusions.json 파일 하나의 내용이다. 조합 생성기가 쓰는 수동 제외 규칙 목록을 보관한다.</summary>
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
