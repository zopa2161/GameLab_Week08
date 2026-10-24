using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode.Editor
{
    /// <summary>조합 생성 한 번의 결과다. 측정 대상, 제외 항목과 열거한 조합 총수를 보관한다.</summary>
    [Serializable]
    public sealed class DpsGenerationResult
    {
        [SerializeField] private List<SpellBuild> _builds = new List<SpellBuild>();
        [SerializeField] private List<DpsExcludedBuild> _excludedBuilds = new List<DpsExcludedBuild>();
        [SerializeField] private int _totalCount;

        public IReadOnlyList<SpellBuild> Builds => _builds;
        public IReadOnlyList<DpsExcludedBuild> ExcludedBuilds => _excludedBuilds;
        public int TotalCount => _totalCount;

        /// <summary>측정 대상 목록, 제외 항목 목록과 생성 총수로 결과를 만든다.</summary>
        public DpsGenerationResult(List<SpellBuild> builds, List<DpsExcludedBuild> excludedBuilds, int totalCount)
        {
            _builds = builds;
            _excludedBuilds = excludedBuilds;
            _totalCount = totalCount;
        }
    }
}
