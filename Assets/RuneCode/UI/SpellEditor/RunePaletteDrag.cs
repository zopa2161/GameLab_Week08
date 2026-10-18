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
        private RunePalettePreview _preview;

        /// <summary>팔레트 항목의 룬 ID·등급과 대상 그래프 및 배치 가능 여부를 연결한다.</summary>
        public void Initialize(RuneGraphCanvas graph, string runeId, string grade, bool canPlace, SpellEditorPanel panel)
        {
            ClearPreview();
            _graph = graph; _runeId = runeId; _grade = grade; _canPlace = canPlace; _panel = panel;
        }

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
            ClearPreview();
            ClearHover();
        }

        /// <summary>활성 호버 안내를 종료하고 기본 편집 안내를 복구한다.</summary>
        private void ClearHover()
        {
            if (!_isHovered) return;
            _isHovered = false;
            _panel.ShowRuneInfo(null, null);
        }

        /// <summary>배치 가능한 룬의 별도 미리보기를 생성하고 실제 그래프 변경 없이 포인터를 중심으로 표시한다.</summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            ClearPreview();
            if (eventData.button != PointerEventData.InputButton.Left || !_canPlace || _graph == null
                || !GameData.Runes.TryGet(_runeId, out RuneDefinition rune)) return;
            ClearHover();
            _preview = _graph.CreatePalettePreview(rune, _grade);
            _preview?.Move(eventData.position);
        }

        /// <summary>미리보기를 현재 포인터 위치로 옮긴다.</summary>
        public void OnDrag(PointerEventData eventData)
        {
            _preview?.Move(eventData.position);
        }

        /// <summary>드래그 종료·항목 재사용·비활성화 시 미리보기를 제거해 취소 상태로 되돌린다.</summary>
        private void ClearPreview()
        {
            _preview?.Dispose();
            _preview = null;
        }

        /// <summary>속성·모디는 그래프의 행동 노드 슬롯에 장착하고, 행동은 그래프 안에 드롭한 좌표에 배치한다.</summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            if (_preview == null) return;
            _preview.Move(eventData.position);
            Vector2 previewPosition = _preview.ScreenPosition;
            ClearPreview();
            if (!_canPlace || _graph == null || !GameData.Runes.TryGet(_runeId, out RuneDefinition rune)) return;
            if (rune.Category == SpellGrammar.CATEGORY_ELEMENT || rune.Category == SpellGrammar.CATEGORY_MODIFIER)
            {
                _graph.DropSlotItem(_runeId, _grade, eventData.position);
                return;
            }
            if (!_graph.TryGetGraphPoint(eventData.position, out _)) return;
            _graph.TryGetGraphPoint(previewPosition, out Vector2 point);
            _graph.PlaceRune(_runeId, point, _grade);
        }
    }
}
