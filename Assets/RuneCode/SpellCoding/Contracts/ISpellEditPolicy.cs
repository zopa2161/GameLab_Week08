namespace RuneCode
{
    /// <summary>
    /// 마법 편집에 적용되는 게임 진행 규칙(해금, RAM 용량, 튜토리얼, 상태 표시)을 주입하는 경계다.
    /// 마법 코딩 영역은 성장·경제 규칙을 직접 계산하지 않고 이 인터페이스에 묻는다.
    /// </summary>
    public interface ISpellEditPolicy
    {
        int MaxLibrary { get; }

        /// <summary>현재 진행 상태로 컴파일 문맥을 만든다.</summary>
        SpellCompileContext GetCompileContext();

        bool IsRuneUnlocked(string runeId);

        /// <summary>후보 그래프가 장착 RAM 규칙을 지키는지 반환한다.</summary>
        bool IsWithinRam(SpellGraph candidate);

        /// <summary>현재 그래프(새 마법이면 null)를 후보로 바꿨을 때 늘어나는 소지 아이템(모디·속성·행동)이 보관함 전체 소지량 안에 드는지 반환한다.</summary>
        bool IsWithinModifierStock(SpellGraph current, SpellGraph candidate);

        /// <summary>편집 그래프를 포함한 보관함 전체에서 지정 (룬 ID, 등급)의 배치 수(used)와 소지량(owned)을 구한다.</summary>
        bool TryGetModifierUsage(string runeId, string grade, SpellGraph editing, out int used, out int owned);

        /// <summary>룬 배치 후 튜토리얼 등 진행 반응을 처리한다.</summary>
        void OnRunePlaced(string runeId);

        /// <summary>Shape·Apply 노드에서 속성(element)을 고른 뒤 속성 식별자로 진행 반응을 처리한다.</summary>
        void OnElementSelected(string elementId);

        /// <summary>문자열 키에 해당하는 상태 메시지를 표시한다.</summary>
        void ReportStatus(string key);

        /// <summary>이미 번역된 상태 문구를 표시한다.</summary>
        void ReportStatusText(string text);
    }
}
