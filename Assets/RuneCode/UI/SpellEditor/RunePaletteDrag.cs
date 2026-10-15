using UnityEngine;

using UnityEngine.EventSystems;

namespace RuneCode
{
    public sealed class RunePaletteDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private RuneGraphCanvas _graph;
        private string _runeId;
        private string _grade;
        private bool _canPlace;
        private SpellEditorPanel _panel;
        private bool _isHovered;

        /// <summary>팔레트 항목의 룬 ID·등급과 대상 그래프 및 배치 가능 여부를 연결한다.</summary>
        public void Initialize(RuneGraphCanvas graph, string runeId, string grade, bool canPlace, SpellEditorPanel panel)
        { _graph = graph; _runeId = runeId; _grade = grade; _canPlace = canPlace; _panel = panel; }

        /// <summary>팔레트 룬의 효과와 등급 수치를 호버 안내에 표시한다.</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
            _panel.ShowRuneInfo(GameData.Runes.Get(_runeId), _grade);
        }

        /// <summary>포인터 이탈 시 룬 효과 안내를 종료한다.</summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            ClearHover();
        }

        void OnDisable()
        {
            ClearHover();
        }

        /// <summary>활성 호버 안내를 종료하고 기본 편집 안내를 복구한다.</summary>
        private void ClearHover()
        {
            if (!_isHovered) return;
            _isHovered = false;
            _panel.ShowRuneInfo(null, null);
        }

        /// <summary>룬 항목의 드래그 입력을 Unity 이벤트 시스템에서 활성화한다.</summary>
        public void OnBeginDrag(PointerEventData eventData) { }

        /// <summary>팔레트에서 그래프까지 이어지는 드래그 입력을 수신한다.</summary>
        public void OnDrag(PointerEventData eventData) { }

        /// <summary>속성·모디는 그래프의 행동 노드 슬롯에 장착하고, 행동은 그래프 안에 드롭한 좌표에 배치한다.</summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_canPlace || _graph == null || !GameData.Runes.TryGet(_runeId, out RuneDefinition rune)) return;
            if (rune.Category == SpellGrammar.CATEGORY_ELEMENT || rune.Category == SpellGrammar.CATEGORY_MODIFIER)
            {
                _graph.DropSlotItem(_runeId, _grade, eventData.position);
                return;
            }
            if (_graph.TryGetGraphPoint(eventData.position, out Vector2 point)) _graph.PlaceRune(_runeId, point, _grade);
        }
    }
}
