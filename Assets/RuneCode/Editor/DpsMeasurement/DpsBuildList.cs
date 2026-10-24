using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode.Editor
{
    /// <summary>builds.json 파일 하나의 내용이다. 측정 실행기가 읽을 측정 대상 조합 목록을 보관한다.</summary>
    [Serializable]
    public sealed class DpsBuildList
    {
        [Header("버전")]
        [SerializeField] private int _schemaVersion = MeasurementCodes.SCHEMA_VERSION;

        [Header("조합")]
        [SerializeField] private List<SpellBuild> _builds = new List<SpellBuild>();

        public int SchemaVersion => _schemaVersion;
        public List<SpellBuild> Builds => _builds;

        /// <summary>JSON 역직렬화용 빈 조합 목록을 만든다.</summary>
        public DpsBuildList() { }

        /// <summary>측정 대상 조합 목록을 복사해 저장용 객체를 만든다.</summary>
        public DpsBuildList(IReadOnlyList<SpellBuild> builds)
        {
            _builds = new List<SpellBuild>(builds);
        }
    }
}
