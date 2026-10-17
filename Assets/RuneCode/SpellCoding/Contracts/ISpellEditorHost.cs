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

        /// <summary>시험 도크에서 지금까지 실행된 노드 수로, 튜토리얼 시험 완료 판정에 쓴다.</summary>
        int TestExecutionCount { get; }

        /// <summary>현재 세션에서 시작 지급 수량 고정 없이 룬 합성을 사용할 수 있는지 나타낸다.</summary>
        bool CanSynthesizeRunes { get; }

        /// <summary>재료 룬 ID 3개와 등급으로 합성을 요청하고 성공 시 결과 룬·등급, 실패 시 오류를 반환한다.</summary>
        bool TrySynthesizeRunes(IReadOnlyList<string> materials, string grade,
            out string resultId, out string resultGrade, out string error);

        /// <summary>현재 마법을 시험 도크에서 한 번 시전한다.</summary>
        void FireTest();

        /// <summary>튜토리얼 완료 단계를 저장한다.</summary>
        void CompleteTutorial();
    }
}
