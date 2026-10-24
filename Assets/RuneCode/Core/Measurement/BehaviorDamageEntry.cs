using System;

using UnityEngine;

namespace RuneCode
{
    /// <summary>체인 안 행동 하나가 낸 피해와 전체 피해 대비 비중이다.</summary>
    [Serializable]
    public sealed class BehaviorDamageEntry
    {
        [Header("행동")]
        [SerializeField] private int _chainIndex;
        [SerializeField] private string _presetId;

        [Header("피해")]
        [SerializeField] private double _damage;
        [SerializeField] private double _ratio;

        public int ChainIndex => _chainIndex;
        public string PresetId => _presetId;
        public double Damage => _damage;
        public double Ratio => _ratio;

        /// <summary>JSON 역직렬화용 빈 항목을 만든다.</summary>
        public BehaviorDamageEntry() { }

        /// <summary>체인 위치, 프리셋 ID, 피해량, 전체 피해 대비 비중으로 항목을 만든다.</summary>
        public BehaviorDamageEntry(int chainIndex, string presetId, double damage, double ratio)
        {
            _chainIndex = chainIndex;
            _presetId = presetId;
            _damage = damage;
            _ratio = ratio;
        }
    }
}
