using System;
using System.Collections.Generic;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 측정 조합을 이루는 행동 하나의 구성이다. 등급은 조합(SpellBuild)이 정하므로 프리셋은 등급을 갖지 않는다.
    /// </summary>
    [Serializable]
    public sealed class BehaviorPreset
    {
        [Header("식별")]
        [SerializeField] private string _presetId;

        [Header("구성")]
        [SerializeField] private string _behaviorId;
        [SerializeField] private List<string> _modifierIds = new List<string>();
        [SerializeField, Tooltip("비우면 일반 속성이다.")] private string _elementId;

        public string PresetId => _presetId;
        public string BehaviorId => _behaviorId;
        public IReadOnlyList<string> ModifierIds => _modifierIds;
        public string ElementId => _elementId;
    }
}
