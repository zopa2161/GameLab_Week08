using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>
    /// 마법 코딩 편집 명령의 경계다. SpellCoding/View(비주얼 스크립팅 UI)만 이 인터페이스를 사용한다.
    /// 다른 기능은 ISpellProvider로만 마법을 읽는다.
    /// </summary>
    public interface ISpellEditor : ISpellProvider
    {
        SpellGraph Graph { get; }
        IReadOnlyList<SpellGraph> Library { get; }
        string ActiveSpellId { get; }
        bool IsEditable { get; }

        /// <summary>노드·엣지·파라미터가 바뀌어 그래프 표시만 갱신하면 될 때 호출된다.</summary>
        event Action GraphChanged;

        /// <summary>편집 대상 마법이 바뀌거나 이름 변경·가져오기처럼 패널 전체 갱신이 필요할 때 호출된다.</summary>
        event Action SpellSwitched;

        /// <summary>작업실 에디터 탭 활성 여부에 따라 편집 허용 상태를 바꾼다.</summary>
        void SetEditable(bool isEditable);

        /// <summary>디바운스 예약된 컴파일과 저장을 현재 시간 기준으로 처리한다.</summary>
        void Tick(float unscaledTime);

        /// <summary>룬이 해금되어 팔레트에서 배치 가능한지 반환한다.</summary>
        bool IsRuneUnlocked(string runeId);

        /// <summary>편집 중인 프리셋에서 지정 (룬 ID, 등급)의 행동·속성·모디 배치 수와 소지량을 구한다. 다른 마법 배치는 세지 않는다.</summary>
        bool TryGetModifierUsage(string runeId, string grade, out int used, out int owned);

        /// <summary>보관함 전체 마법에 배치된 수를 제외한 미사용 수량과 소지량을 구한다. 합성 재료 계산에 쓴다.</summary>
        bool TryGetUnusedStock(string runeId, string grade, out int unused, out int owned);

        /// <summary>지정 좌표에 행동 노드를 지정 등급(비면 기본 등급)으로 배치한다. 행동 외 룬은 배치하지 않는다.</summary>
        void AddRune(string runeId, float x, float y, string grade = null);

        /// <summary>행동 노드에서 지정 속성·모디를 장착할 수 있는 첫 빈 슬롯 키를 찾는다. 없으면 false를 반환한다.</summary>
        bool TryFindEquipSlot(string nodeId, string runeId, string grade, out string slotKey);

        /// <summary>행동 노드의 지정 슬롯에 속성·모디를 장착할 수 있는지 반환한다.</summary>
        bool CanEquipToSlot(string nodeId, string slotKey, string runeId, string grade);

        /// <summary>행동 노드의 슬롯에 지정 속성·모디를 장착(교체 포함)하고 성공하면 true를 반환한다. 실패하면 상태 문구를 표시한다.</summary>
        bool Equip(string nodeId, string slotKey, string runeId, string grade);

        /// <summary>행동 노드의 지정 슬롯을 비우고 비웠으면 true를 반환한다.</summary>
        bool ClearSlot(string nodeId, string slotKey);

        /// <summary>지정 슬롯이 행동 노드 등급의 슬롯 한도 밖이라 잠겨 있는지 반환한다.</summary>
        bool IsSlotLocked(string nodeId, string slotKey);

        /// <summary>문자열 키에 해당하는 상태 문구를 표시한다.</summary>
        void ReportStatus(string key);

        /// <summary>포트 연결을 검증하고 성공하면 엣지를 추가한다. replaceExisting이면 단일 포트의 기존 연결을 교체한다.</summary>
        bool Connect(string fromNode, string fromPort, string toNode, string toPort, bool replaceExisting = false);

        /// <summary>기존 연결 교체 옵션을 반영하여 드래그 중 후보 연결이 규칙상 가능한지 반환한다.</summary>
        bool CanConnectPorts(string fromNode, string fromPort, string toNode, string toPort, bool replaceExisting = false);

        /// <summary>Core가 아닌 노드와 연결된 엣지를 제거한다.</summary>
        void RemoveNode(string nodeId);

        /// <summary>엣지를 제거한다.</summary>
        void RemoveEdge(string edgeId);

        /// <summary>노드의 숫자 파라미터를 저장한다.</summary>
        void SetNodeNumber(string nodeId, string key, float value);

        /// <summary>노드의 문자열 파라미터를 저장한다.</summary>
        void SetNodeText(string nodeId, string key, string value);

        /// <summary>노드 드래그 시작 시 되돌리기 기준 스냅샷을 기록한다.</summary>
        void BeginEdit();

        /// <summary>그래프가 직접 변경되었음을 알리고 되돌리기 기록과 컴파일을 예약한다.</summary>
        void MarkChanged();

        void Undo();
        void Redo();

        /// <summary>선택 노드와 내부 연결을 복사 버퍼에 기록한다.</summary>
        void CopyNodes(IEnumerable<string> nodeIds);

        /// <summary>복사 버퍼를 새 ID와 좌표 오프셋으로 붙여넣는다.</summary>
        void PasteNodes();

        /// <summary>그래프 노드를 실행 행으로 정렬한다.</summary>
        void AutoArrange();

        void Rename(string name);

        /// <summary>현재 마법의 공유 코드를 반환한다.</summary>
        string Export();

        /// <summary>공유 코드를 현재 마법에 반영하고 실패 시 사용자 메시지를, 성공 시 null을 반환한다.</summary>
        string Import(string code);

        void SelectSpell(string spellId);
        void NewSpell();
        void DuplicateSpell();
        void DeleteSpell();

        /// <summary>편집 사본을 보관함에 반영하고 저장한다.</summary>
        void Save();
    }
}
