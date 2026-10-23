using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 측정 입력 파일 하나의 내용이다. 플레이어 상태, 행동 프리셋, 측정 조건, 측정에서 쓸 등급을 함께 보관한다.
    /// </summary>
    [Serializable]
    public sealed class MeasurementSuite
    {
        [Header("버전")]
        [SerializeField] private int _schemaVersion = MeasurementCodes.SCHEMA_VERSION;

        [Header("측정 입력")]
        [SerializeField] private MeasurementPlayerProfile _profile = new MeasurementPlayerProfile();
        [SerializeField] private List<string> _grades = new List<string>();
        [SerializeField] private List<int> _chainLengths = new List<int>();
        [SerializeField] private List<BehaviorPreset> _presets = new List<BehaviorPreset>();
        [SerializeField] private List<MeasurementCondition> _conditions = new List<MeasurementCondition>();
        [SerializeField, Tooltip("프리셋에 들어 있으면 안 되는 룬 ID. 조합 생성기가 검사한다.")] private List<string> _excludedRuneIds = new List<string>();

        public int SchemaVersion => _schemaVersion;
        public MeasurementPlayerProfile Profile => _profile;
        public IReadOnlyList<string> Grades => _grades;
        public IReadOnlyList<int> ChainLengths => _chainLengths;
        public IReadOnlyList<BehaviorPreset> Presets => _presets;
        public IReadOnlyList<MeasurementCondition> Conditions => _conditions;
        public IReadOnlyList<string> ExcludedRuneIds => _excludedRuneIds;

        /// <summary>프리셋 ID로 프리셋을 찾고 없으면 false를 반환한다.</summary>
        public bool TryGetPreset(string presetId, out BehaviorPreset preset)
        {
            foreach (BehaviorPreset candidate in _presets)
            {
                if (candidate.PresetId != presetId) continue;
                preset = candidate;
                return true;
            }
            preset = null;
            return false;
        }
    }
}
