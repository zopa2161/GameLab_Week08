using System;

using UnityEngine;

namespace RuneCode.Editor
{
    /// <summary>
    /// 이름 붙은 측정 조합 셋 하나의 내용이다. 조합 생성 규칙(측정 입력)과 수동 제외 규칙을 함께 보관하며
    /// Data/Sets/&lt;셋 이름&gt;.json 파일 하나에 저장된다. 셋 이름은 파일 이름이다.
    /// </summary>
    [Serializable]
    public sealed class DpsBuildSet
    {
        [Header("버전")]
        [SerializeField] private int _schemaVersion = MeasurementCodes.SCHEMA_VERSION;

        [Header("생성 규칙")]
        [SerializeField] private MeasurementSuite _suite = new MeasurementSuite();
        [SerializeField] private DpsExclusionList _exclusions = new DpsExclusionList();

        public int SchemaVersion => _schemaVersion;
        public MeasurementSuite Suite => _suite;
        public DpsExclusionList Exclusions => _exclusions;
    }
}
