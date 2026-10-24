using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 일괄 측정 한 번의 결과 파일 내용이다. 결과를 다시 해석할 수 있도록 측정 입력과 데이터 버전을 함께 기록한다.
    /// </summary>
    [Serializable]
    public sealed class MeasurementResultSet
    {
        [Header("버전")]
        [SerializeField] private int _schemaVersion = MeasurementCodes.SCHEMA_VERSION;
        [SerializeField, Tooltip("측정에 쓴 게임 데이터 파일의 해시")] private string _dataVersion;
        [SerializeField, Tooltip("측정 시작 시각(ISO 8601)")] private string _createdAt;

        [Header("측정 입력")]
        [SerializeField] private MeasurementSuite _suite;

        [Header("결과")]
        [SerializeField] private List<MeasurementResult> _results = new List<MeasurementResult>();

        public int SchemaVersion => _schemaVersion;
        public string DataVersion => _dataVersion;
        public string CreatedAt => _createdAt;
        public MeasurementSuite Suite => _suite;
        public List<MeasurementResult> Results => _results;

        /// <summary>JSON 역직렬화용 빈 결과 묶음을 만든다.</summary>
        public MeasurementResultSet() { }

        /// <summary>데이터 버전, 측정 시작 시각, 측정 입력으로 빈 결과 묶음을 만든다.</summary>
        public MeasurementResultSet(string dataVersion, string createdAt, MeasurementSuite suite)
        {
            _dataVersion = dataVersion;
            _createdAt = createdAt;
            _suite = suite;
        }
    }
}
