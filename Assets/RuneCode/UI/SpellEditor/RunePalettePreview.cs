using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>독립된 표시용 그래프로 행동 노드 또는 장착 슬롯을 그리는 팔레트 드래그 미리보기다.</summary>
    internal sealed class RunePalettePreview : IDisposable
    {
        private readonly RectTransform _root;
        private readonly RectTransform _parent;
        private readonly Camera _camera;

        public Vector2 ScreenPosition => RectTransformUtility.WorldToScreenPoint(_camera, _root.position);

        /// <summary>별도 그래프와 기존 노드 그리기를 사용해 룬 미리보기를 만들고 속성·모드는 슬롯 한 칸만 표시한다.</summary>
        public RunePalettePreview(Canvas canvas, RectTransform source, float zoom, RuneDefinition rune, string grade,
            TMP_FontAsset font, IEnumerable<SpellGraph> library, Func<string, bool> isRuneUnlocked)
        {
            _parent = (RectTransform)canvas.transform;
            _camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            _root = CreateRect("RunePalettePreview", _parent);
            CanvasGroup group = _root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            Vector3 sourceScale = source.lossyScale;
            Vector3 parentScale = _parent.lossyScale;
            _root.localScale = new Vector3(zoom * sourceScale.x / parentScale.x,
                zoom * sourceScale.y / parentScale.y, 1f);

            bool isSlot = rune.Category == SpellGrammar.CATEGORY_ELEMENT || rune.Category == SpellGrammar.CATEGORY_MODIFIER;
            string holderId = isSlot ? SpellGrammar.BehaviorRune("launch") : rune.Id;
            var node = new GraphNode("preview", holderId);
            node.SetText(SpellGrammar.MODIFIER_GRADE_PARAM, isSlot ? "S"
                : string.IsNullOrEmpty(grade) ? SpellGrammar.DEFAULT_BEHAVIOR_GRADE : grade);
            var graph = new SpellGraph("preview", "preview");
            graph.AddNode(node);

            RectTransform content = CreateRect("PreviewNode", _root);
            var viewport = new GraphViewport(content);
            viewport.SetPan(Vector2.zero);
            var layout = new GraphNodeLayout(viewport, () => graph);
            RuneDefinition holder = GameData.Runes.Get(holderId);
            content.sizeDelta = new Vector2(GraphNodeLayout.NODE_WIDTH, layout.NodeHeight(node, holder));
            _root.sizeDelta = content.sizeDelta;
            if (isSlot)
            {
                bool isElement = rune.Category == SpellGrammar.CATEGORY_ELEMENT;
                string slotKey = isElement ? SpellGrammar.ElementSlotKey(0) : SpellGrammar.ModifierSlotKey(0);
                string value = isElement && GameData.Runes.TryGetRuneElement(rune.Id, out ElementDefinition element)
                    ? element.Id : SpellGrammar.ModifierStockKey(rune.Id, grade);
                node.SetText(slotKey, value);
                layout.TryGetSlotRect(node, holder, slotKey, out Rect slot);
                _root.sizeDelta = slot.size;
                content.anchoredPosition = new Vector2(-slot.x, -slot.yMax);
                _root.gameObject.AddComponent<RectMask2D>();
            }

            var painter = new GraphMeshPainter(viewport, layout, new GraphPointerState(),
                Resources.Load<GraphVisualStyle>("RuneCode/UI/Elements/GraphVisualStyle"), isRuneUnlocked);
            var labels = new GraphNodeLabels(content, font, viewport, layout, painter);
            labels.Refresh(graph, library, null);
            labels.Layout(graph);
            _root.SetAsLastSibling();
        }

        /// <summary>지정한 부모 아래에 좌상단 기준의 표시용 사각형을 생성한다.</summary>
        private static RectTransform CreateRect(string name, RectTransform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        /// <summary>화면 포인터 좌표에 배율이 반영된 미리보기 사각형의 중앙을 맞춘다.</summary>
        public void Move(Vector2 screenPosition)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, screenPosition, _camera, out Vector2 local)) return;
            _root.position = _parent.TransformPoint(local) - _root.TransformVector(_root.rect.center);
        }

        /// <summary>미리보기를 즉시 숨기고 표시용 그래프 오브젝트를 파괴한다.</summary>
        public void Dispose()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(_root.gameObject);
        }
    }
}
