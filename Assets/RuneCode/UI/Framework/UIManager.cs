using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 앱 전체 UI의 표시 층·화면·팝업을 관리한다. Boot의 RuneCodeApp이 만들어 씬 전환과 무관하게 유지한다.
    /// 층마다 Overlay Canvas를 두어 팝업이 화면 위에, 시스템 UI가 팝업 위에 항상 그려지게 한다.
    /// View는 Resources의 Prefab(RuneCode/UI/타입 이름)에서 처음 한 번만 만들고 이후 숨김·재표시로 재사용한다.
    /// </summary>
    public sealed class UIManager : MonoBehaviour
    {
        public const string VIEW_RESOURCE_FOLDER = "RuneCode/UI/";

        private const int SCREEN_SORTING_ORDER = 0;
        private const int POPUP_SORTING_ORDER = 100;
        private const int SYSTEM_SORTING_ORDER = 200;
        private const float TOAST_DURATION = 3f;

        private readonly Dictionary<UiLayer, RectTransform> _layers = new Dictionary<UiLayer, RectTransform>();
        private readonly Dictionary<Type, UiView> _views = new Dictionary<Type, UiView>();
        private readonly List<UiPopup> _popupStack = new List<UiPopup>();
        private UiView _currentScreen;
        private RectTransform _toast;
        private TextMeshProUGUI _toastLabel;
        private float _toastUntil;

        /// <summary>현재 표시 중인 화면 View다. 없으면 null이다.</summary>
        public UiView CurrentScreen => _currentScreen;

        /// <summary>열린 팝업이 하나라도 있는지 반환한다. 화면 입력(조준, 단축키 등) 차단에 쓴다.</summary>
        public bool IsPopupOpen => _popupStack.Count > 0;

        /// <summary>지정 부모 아래에 UIManager 오브젝트와 화면·팝업·시스템 층 Canvas를 만들어 반환한다.</summary>
        public static UIManager Create(Transform parent)
        {
            GameObject root = new GameObject("UIManager");
            root.transform.SetParent(parent, false);
            UIManager manager = root.AddComponent<UIManager>();
            manager.CreateLayer(UiLayer.Screen, SCREEN_SORTING_ORDER);
            manager.CreateLayer(UiLayer.Popup, POPUP_SORTING_ORDER);
            manager.CreateLayer(UiLayer.System, SYSTEM_SORTING_ORDER);
            return manager;
        }

        void Update()
        {
            if (_toast != null && _toast.gameObject.activeSelf && Time.unscaledTime >= _toastUntil)
                _toast.gameObject.SetActive(false);
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame || UiFactory.IsTyping()) return;
            if (_popupStack.Count > 0) _popupStack[_popupStack.Count - 1].HandleEscape();
            else if (_currentScreen != null) _currentScreen.HandleEscape();
        }

        /// <summary>
        /// 화면 View T를 표시하고 반환한다. 열린 팝업을 모두 닫고 이전 화면은 숨긴다.
        /// 처음이면 Prefab에서 만들고, 이후에는 같은 인스턴스를 다시 표시한다. prepare는 표시 직전에 호출한다(최초 초기화 등).
        /// </summary>
        public T ShowScreen<T>(Action<T> prepare = null) where T : UiView
        {
            if (_toast != null) _toast.gameObject.SetActive(false);
            CloseAllPopups();
            T screen = GetView<T>(UiLayer.Screen);
            if (_currentScreen != null && _currentScreen != screen) _currentScreen.Hide();
            _currentScreen = screen;
            prepare?.Invoke(screen);
            screen.Show();
            return screen;
        }

        /// <summary>
        /// 팝업 View T를 Popup 층 맨 위에 열고 반환한다. 이미 열려 있으면 맨 위로 올린다.
        /// 표시 내용은 반환된 팝업의 설정 함수로 채운다.
        /// </summary>
        public T OpenPopup<T>() where T : UiPopup
        {
            T popup = GetView<T>(UiLayer.Popup);
            popup.Bind(this);
            _popupStack.Remove(popup);
            _popupStack.Add(popup);
            popup.transform.SetAsLastSibling();
            popup.Show();
            return popup;
        }

        /// <summary>지정 팝업을 스택에서 빼고 닫는다. 열려 있지 않으면 무시한다.</summary>
        public void ClosePopup(UiPopup popup)
        {
            if (popup == null || !_popupStack.Remove(popup)) return;
            popup.Hide();
        }

        /// <summary>열린 팝업을 위에서부터 모두 닫는다.</summary>
        public void CloseAllPopups()
        {
            for (int i = _popupStack.Count - 1; i >= 0; i--)
            {
                if (i < _popupStack.Count) ClosePopup(_popupStack[i]);
            }
        }

        /// <summary>제목·내용과 확인·취소 팝업을 열고 isDestructive이면 확인 버튼을 빨간색으로 표시한다. 확인 시 닫은 뒤 onConfirm을 실행한다.</summary>
        public void Confirm(string title, string message, string confirmLabel, Action onConfirm, bool isDestructive = false)
        {
            OpenPopup<ConfirmPopup>().Configure(title, message, confirmLabel, onConfirm, isDestructive);
        }

        /// <summary>제목·내용과 확인 버튼 하나의 공용 알림 팝업을 연다.</summary>
        public void Alert(string title, string message)
        {
            OpenPopup<ConfirmPopup>().Configure(title, message, null, null);
        }

        /// <summary>팝업 위의 시스템 층에 입력을 차단하지 않는 안내를 표시하고 실제 시간 3초 뒤 숨긴다.</summary>
        public void ShowToast(string message)
        {
            if (_toast == null)
            {
                _toast = UiFactory.Rect(_layers[UiLayer.System], "Toast", 190, 632, 900, 64);
                Image background = _toast.gameObject.AddComponent<Image>();
                background.color = new Color(.025f, .045f, .075f, .96f);
                background.raycastTarget = false;
                _toastLabel = Instantiate(Resources.Load<TextMeshProUGUI>("RuneCode/UI/Elements/UiText"), _toast, false);
                RectTransform labelRect = _toastLabel.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(20, 8);
                labelRect.offsetMax = new Vector2(-20, -8);
                _toastLabel.fontSize = 18f;
                _toastLabel.alignment = TextAlignmentOptions.Center;
                _toastLabel.raycastTarget = false;
            }
            _toastLabel.text = message;
            _toastUntil = Time.unscaledTime + TOAST_DURATION;
            _toast.SetAsLastSibling();
            _toast.gameObject.SetActive(true);
        }

        /// <summary>
        /// 층에 붙은 View T를 반환한다. 처음 요청이면 Resources의 RuneCode/UI/T 이름 Prefab에서 만들어 숨긴 채 보관한다.
        /// Prefab이 없으면 기대한 Resources 경로를 포함한 예외를 던진다.
        /// </summary>
        public T GetView<T>(UiLayer layer) where T : UiView
        {
            if (_views.TryGetValue(typeof(T), out UiView cached)) return (T)cached;
            T prefab = Resources.Load<T>(VIEW_RESOURCE_FOLDER + typeof(T).Name);
            if (prefab == null) throw new InvalidOperationException("UI Prefab이 없습니다: Resources/" + VIEW_RESOURCE_FOLDER + typeof(T).Name);
            T view = Instantiate(prefab, _layers[layer], false);
            view.name = typeof(T).Name;
            view.gameObject.SetActive(false);
            foreach (Button button in view.GetComponentsInChildren<Button>(true)) UiFactory.BindClickSound(button);
            _views.Add(typeof(T), view);
            return view;
        }

        /// <summary>정렬 순서를 지정한 Overlay Canvas 층을 만들고 1280×720 논리 화면 루트를 등록한다.</summary>
        private void CreateLayer(UiLayer layer, int sortingOrder)
        {
            RectTransform root = UiFactory.CreateCanvas(transform, layer + "Layer");
            root.parent.GetComponent<Canvas>().sortingOrder = sortingOrder;
            _layers.Add(layer, root);
        }
    }
}
