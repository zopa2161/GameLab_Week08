using System;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 측정 한 번의 전장 조건이다. 상황·입력 방식은 MeasurementCodes의 문자열 코드를 쓴다.
    /// </summary>
    [Serializable]
    public sealed class MeasurementCondition
    {
        [Header("식별")]
        [SerializeField] private string _conditionId;

        [Header("전장")]
        [SerializeField, Tooltip("single, swarm 또는 pentagon")] private string _situation = MeasurementCodes.SITUATION_SINGLE;
        [SerializeField, Tooltip("플레이어와 적(무리는 격자 중심, 오각형은 중앙 적) 사이 거리")] private float _distance = 140f;

        [Header("진행")]
        [SerializeField] private float _durationSeconds = 30f;
        [SerializeField] private string _inputMode = MeasurementCodes.INPUT_HOLD_ATTACK;
        [SerializeField] private int _seed = 1;
        [SerializeField, Tooltip("무작위 요소가 있는 조합만 시드를 바꿔 이 횟수만큼 실행한다.")] private int _repeatCount = 3;

        public string ConditionId => _conditionId;
        public string Situation => _situation;
        public float Distance => _distance;
        public float DurationSeconds => _durationSeconds;
        public string InputMode => _inputMode;
        public int Seed => _seed;
        public int RepeatCount => _repeatCount;
    }
}
