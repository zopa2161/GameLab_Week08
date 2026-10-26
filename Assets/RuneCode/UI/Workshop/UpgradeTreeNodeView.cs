using System;

using UnityEngine;

using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuneCode
{
    public sealed class UpgradeTreeNodeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("정사각형 노드")]
        [SerializeField] private Image _background;
        [SerializeField] private Outline _outline;
        [SerializeField] private UpgradeTreeNodeIconGraphic _icon;
        [SerializeField] private Image _spriteIcon;
        [SerializeField] private Button _button;

        [Header("상태 표시: 잠금 / 개방 / 일부 강화 / MAX")]
        [SerializeField] private UiStateVisual _visual;

        [Header("강화 레벨 표시")]
        [SerializeField] private UpgradeTreeNodeFillGraphic _levelFill;
        [SerializeField] private GameObject _levelBadge;
        [SerializeField] private TextMeshProUGUI _levelText;

        [Header("다음 구매 비용 표시")]
        [SerializeField] private GameObject _costBadge;
        [SerializeField] private Image _costIcon;
        [SerializeField] private TextMeshProUGUI _costText;

        private Action _purchaseAction;
        private Action _refundAction;
        private Action<bool> _hoverAction;
        private string _nodeId;

        public string NodeId => _nodeId;

        /// <summary>런타임 노드 생성 시 데이터 ID를 지정한다. UpgradeTree SO의 노드 ID와 일치해야 한다.</summary>
        public void ConfigureNodeId(string nodeId)
        {
            _nodeId = nodeId;
        }

        /// <summary>정사각형 노드의 클릭을 강화 또는 룬 해금 요청에 연결한다.</summary>
        public void SetPurchaseAction(Action action)
        {
            _purchaseAction = action;
            _button.onClick.RemoveAllListeners();
            UiFactory.BindClickSound(_button);
            if (_purchaseAction != null) _button.onClick.AddListener(() => _purchaseAction());
        }

        /// <summary>우클릭으로 현재 강화 1단계를 반환할 동작을 연결한다. 구매 버튼의 활성 여부와 별개로 처리한다.</summary>
        public void SetRefundAction(Action action)
        {
            _refundAction = action;
        }

        /// <summary>드래그가 아닌 우클릭일 때 강화 반환을 요청한다. 좌클릭 구매는 기존 버튼이 처리한다.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right && !eventData.dragging)
                _refundAction?.Invoke();
        }

        /// <summary>포인터 진입·이탈을 상위 트리의 노드 이름·설명 표시로 전달한다.</summary>
        public void SetHoverAction(Action<bool> action)
        {
            _hoverAction = action;
        }

        /// <summary>아이콘·구매 상태와 레벨 채움·레벨/MAX 문구를 반영하고 개방·일부 강화 상태에 다음 구매 비용 배지를 표시한다.</summary>
        public void SetContent(UpgradeEffectType effectType, string runeCategory, Sprite sprite, int level, int maxLevel, int nextCost, bool isAvailable, bool isComplete, bool canPurchase, string runeId = null)
        {
            _visual.Show(!isAvailable ? 0 : isComplete ? 3 : level > 0 ? 2 : 1);
            _levelFill.gameObject.SetActive(isAvailable && level > 0);
            _levelFill.SetFill(maxLevel > 0 ? (float)level / maxLevel : 0);
            _levelBadge.SetActive(isAvailable);
            _levelText.text = isComplete ? "MAX" : level + "/" + maxLevel;
            _costBadge.SetActive(isAvailable && !isComplete && nextCost >= 0);
            _costText.text = nextCost >= 0 ? nextCost.ToString() : string.Empty;
            _spriteIcon.gameObject.SetActive(sprite != null);
            _icon.gameObject.SetActive(sprite == null);
            if (sprite == null)
            {
                _icon.Configure(effectType, runeCategory, runeId);
            }
            else
            {
                _spriteIcon.sprite = sprite;
            }
            _button.interactable = canPurchase;
        }

        /// <summary>노드 이름과 설명을 보여주도록 포인터 진입을 알린다.</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            _hoverAction?.Invoke(true);
        }

        /// <summary>현재 노드에서 포인터가 벗어나면 이름과 설명 표시를 닫도록 알린다.</summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            _hoverAction?.Invoke(false);
        }

    }
}
