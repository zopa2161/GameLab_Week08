using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>
    /// runes.json 파일 하나다. JsonUtility로 읽어 RuneCatalog.FromTable에서 검증한다.
    /// 순수 C# 어셈블리(RuneCode.Grammar)에서는 [SerializeField] private를 쓸 수 없으므로
    /// public 필드 이름이 그대로 JSON 키가 된다. 프로젝트의 다른 JSON과 같게 키는 _camelCase를 쓴다.
    /// </summary>
    [Serializable]
    public sealed class RuneTableData
    {
        public List<RuneRowData> _runes = new List<RuneRowData>();
        public List<BehaviorGradeData> _behaviorGrades;
    }

    /// <summary>행동 등급 하나의 슬롯 수다. 모든 행동 룬이 같은 표를 쓴다.</summary>
    [Serializable]
    public sealed class BehaviorGradeData
    {
        public string _grade;
        public int _elementSlots;
        public int _modifierSlots;
    }

    /// <summary>룬 한 개의 데이터다. 기본 정보와 수치·포트·파라미터·태그, 속성·효과 대상을 함께 가진다.</summary>
    [Serializable]
    public sealed class RuneRowData
    {
        public string _id;
        public string _name;
        public string _category;
        public string _shape;
        public bool _disabled;
        public int _ram;
        public float _energy;
        public float _energyMult = 1f;
        public float _executionSeconds;
        public float _executionSecondsPerArea;
        public int _unlockCost;
        public List<RuneResourceCostData> _resourceCosts;
        public List<RuneShapeCostRateData> _shapeCostRates;
        public string _unlockType;
        public RuneStatsData _stats;
        public List<RunePortData> _ports;
        public List<RuneParamData> _params;
        public List<RuneTagData> _tags;
        public RuneElementData _element;
        public List<string> _modifierTargets;
        public List<RuneHomingTierData> _homingTiers;
    }

    /// <summary>자원별 기본 비용 하나를 나타내는 runes.json 행이다.</summary>
    [Serializable]
    public sealed class RuneResourceCostData
    {
        public string _resource;
        public float _amount;
    }

    /// <summary>Shape의 넓이 단위당 자원 비용률 하나를 나타내는 runes.json 행이다.</summary>
    [Serializable]
    public sealed class RuneShapeCostRateData
    {
        public string _resource;
        public float _rate;
    }

    /// <summary>룬의 전투 수치다. JSON에 없는 배율 필드는 1, 나머지는 0이 기본값이다.</summary>
    [Serializable]
    public sealed class RuneStatsData
    {
        public float _damage;
        public float _speed;
        public float _radius;
        public float _lifetime;
        public float _minimumLifetime;
        public float _offset;
        public int _count;
        public float _orbitRadius;
        public float _angularSpeed;
        public float _entrySpeed;
        public float _hitInterval;
        public float _tickInterval;
        public float _damageMultiplier = 1f;
        public float _radiusMultiplier = 1f;
        public float _speedMultiplier = 1f;
        public float _durationMultiplier = 1f;
        public int _pierce;
        public float _pierceLoss;
        public float _homingTurn;
        public float _homingRange;
        public float _arcRange;
        public int _arcTargets;
        public float _arcMultiplier;
        public float _learningMultiplier = 1f;
        public float _spreadAngle;
        public float _shieldAmount;
        public float _shieldSeconds;
        public float _coneAngle;
        public float _expandSeconds;
        public float _warnSeconds;
        public float _beamWidth;
        public float _beamLength;
    }

    /// <summary>유도 Modifier의 하위 유도 단계와 적용 수치다.</summary>
    [Serializable]
    public sealed class RuneHomingTierData
    {
        public string _tier;
        public float _homingTurn;
        public float _homingRange;
    }

    /// <summary>룬 포트 하나의 데이터다. status는 빈 문자열이면 active다.</summary>
    [Serializable]
    public sealed class RunePortData
    {
        public int _order;
        public string _portId;
        public string _kind;
        public string _direction;
        public int _max;
        public string _status;
    }

    /// <summary>룬 파라미터 하나의 데이터다. enum 종류는 options에 선택지를 둔다.</summary>
    [Serializable]
    public sealed class RuneParamData
    {
        public int _order;
        public string _paramId;
        public string _kind;
        public float _min;
        public float _max;
        public float _step;
        public float _defaultNumber;
        public string _defaultText;
        public List<RuneParamOptionData> _options;
    }

    /// <summary>enum 파라미터 선택지 하나의 데이터다.</summary>
    [Serializable]
    public sealed class RuneParamOptionData
    {
        public int _order;
        public string _option;
    }

    /// <summary>룬 태그 하나의 데이터다.</summary>
    [Serializable]
    public sealed class RuneTagData
    {
        public int _order;
        public string _tag;
    }

    /// <summary>element 카테고리 룬이 제공하는 속성 데이터다. 소속 룬 ID가 속성의 룬 참조다.</summary>
    [Serializable]
    public sealed class RuneElementData
    {
        public string _id;
        public string _category;
        public string _internalValue;
        public string _runtimeTag;

        /// <summary>속성 데이터에 값이 하나라도 있는지 반환한다. JsonUtility는 없는 필드도 빈 인스턴스로 만들므로 빈 인스턴스는 속성 없음으로 본다.</summary>
        public bool HasAnyValue => !string.IsNullOrEmpty(_id) || !string.IsNullOrEmpty(_category)
            || !string.IsNullOrEmpty(_internalValue) || !string.IsNullOrEmpty(_runtimeTag);
    }
}
