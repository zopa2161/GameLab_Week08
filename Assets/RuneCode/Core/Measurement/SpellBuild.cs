using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 측정할 마법 조합이다. 프리셋을 순서대로 앞 행동의 onHit에 연결하고, 모든 행동과 모드에 같은 등급을 쓴다.
    /// </summary>
    [Serializable]
    public sealed class SpellBuild
    {
        private const string GRADE_SEPARATOR = ":";
        private const string PRESET_SEPARATOR = ">";

        [Header("조합")]
        [SerializeField] private string _buildId;
        [SerializeField] private string _grade;
        [SerializeField] private List<string> _presetIds = new List<string>();

        public string BuildId => _buildId;
        public string Grade => _grade;
        public IReadOnlyList<string> PresetIds => _presetIds;
        public int ChainLength => _presetIds.Count;

        /// <summary>JSON 역직렬화용 빈 조합을 만든다.</summary>
        public SpellBuild() { }

        /// <summary>등급과 프리셋 ID 순서로 조합을 만들고 같은 입력이면 항상 같은 조합 ID를 부여한다.</summary>
        public SpellBuild(string grade, IReadOnlyList<string> presetIds)
        {
            _grade = grade;
            _presetIds = new List<string>(presetIds);
            _buildId = CreateId(grade, presetIds);
        }

        /// <summary>등급과 프리셋 ID 순서로 "등급:프리셋>프리셋" 형식의 조합 ID를 반환한다. 예: A:L2>X2>P1</summary>
        public static string CreateId(string grade, IReadOnlyList<string> presetIds)
        {
            return grade + GRADE_SEPARATOR + string.Join(PRESET_SEPARATOR, presetIds);
        }
    }
}
