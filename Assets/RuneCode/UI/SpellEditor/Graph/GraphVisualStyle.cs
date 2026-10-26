using System;

using UnityEngine;

using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>그래프 메시가 사용할 표시 설정을 프리팹의 Image에서 읽는다. Image는 화면에 직접 표시하지 않는다.</summary>
    public sealed class GraphVisualStyle : MonoBehaviour
    {
        private const int NODE_ACCENT_COLOR = 16;

        [Header("메시 표시 템플릿")]
        [SerializeField] private Image[] _templates;

        [Header("분류 강조색 (팔레트 행 프리팹)")]
        [SerializeField] private RunePaletteRowBinding[] _categoryRowPrefabs = Array.Empty<RunePaletteRowBinding>();

        [Header("행동 노드 슬롯")]
        [SerializeField] private Image _elementSlotPrefab;
        [SerializeField] private Image _modifierSlotPrefab;
        [SerializeField] private Sprite _slotOutlineSprite;
        [SerializeField, Min(0f)] private float _slotOutlineWidth = 2f;

        /// <summary>속성 슬롯에 사용할 표시 프리팹을 반환한다.</summary>
        public Image ElementSlotPrefab => _elementSlotPrefab;

        /// <summary>모디파이어 슬롯에 사용할 표시 프리팹을 반환한다.</summary>
        public Image ModifierSlotPrefab => _modifierSlotPrefab;

        /// <summary>슬롯 본체 뒤에 확대해 표시할 흰색 테두리 스프라이트를 반환한다.</summary>
        public Sprite SlotOutlineSprite => _slotOutlineSprite;

        /// <summary>그래프 배율 적용 전 슬롯 바깥으로 확장할 테두리 너비를 반환한다.</summary>
        public float SlotOutlineWidth => _slotOutlineWidth;

        /// <summary>index에 해당하는 프리팹 템플릿의 색을 반환한다.</summary>
        public Color GetColor(int index) => _templates[index].color;

        /// <summary>category 팔레트의 Image 색상과 읽기 가능한 Sprite 중앙색을 합성해 반환하며 미연결 분류는 기존 강조색을 사용한다.</summary>
        public Color GetCategoryColor(string category)
        {
            UiRow row = RunePaletteRowBinding.Resolve(_categoryRowPrefabs, category, null);
            if (row == null) return GetColor(NODE_ACCENT_COLOR);
            var image = row.Button.targetGraphic as Image;
            Color color = row.Button.targetGraphic.color;
            if (image != null && image.sprite != null && image.sprite.texture.isReadable)
            {
                Rect rect = image.sprite.textureRect;
                color *= image.sprite.texture.GetPixel((int)rect.center.x, (int)rect.center.y);
            }
            return color;
        }
    }
}
