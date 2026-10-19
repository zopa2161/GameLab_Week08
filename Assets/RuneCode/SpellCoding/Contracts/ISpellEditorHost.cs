using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>
    /// 비주얼 스크립팅 편집 패널이 작업실(도크·진행 상태)에서 받아야 하는 최소 정보와 요청이다.
    /// 편집 패널은 도크 시뮬레이션이나 세이브 구조를 직접 참조하지 않는다.
    /// </summary>
    public interface ISpellEditorHost
    {
        /// <summary>장착 마법 전체의 RAM 사용량이다.</summary>
        int EquippedRam { get; }

        /// <summary>현재 RAM 용량이다.</summary>
        int Capacity { get; }

        /// <summary>성장 효과까지 반영한 현재 최대 전력값이다. 편집 지표의 시전 비용 nn/max 표기에 쓴다.</summary>
        float MaxEnergy { get; }

        /// <summary>성장 효과까지 반영한 초당 전력 회복량이다. 편집 지표의 전력 회복 nn/s 표기에 쓴다.</summary>
        float EnergyRegen { get; }

        /// <summary>현재 세션에서 시작 지급 수량 고정 없이 룬 합성을 사용할 수 있는지 나타낸다.</summary>
        bool CanSynthesizeRunes { get; }

        /// <summary>속성 조합의 등급 확률에 사용할 현재 선택 출격 스테이지다.</summary>
        int SynthesisStage { get; }

        /// <summary>합성 비용 지불에 사용할 현재 보유 스크랩 수다.</summary>
        int SynthesisScrap { get; }

        /// <summary>재료 룬 ID 3개와 등급으로 합성을 요청하고 성공 시 결과 룬·등급, 실패 시 오류를 반환한다.</summary>
        bool TrySynthesizeRunes(IReadOnlyList<string> materials, string grade,
            out string resultId, out string resultGrade, out string error);

        /// <summary>현재 마법을 시험 도크에서 한 번 시전한다.</summary>
        void FireTest();
    }
}
