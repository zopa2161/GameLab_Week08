using System;

using UnityEngine;

using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 좌상단 기준 좌표로 uGUI 요소를 생성하는 공용 헬퍼다.
    /// 에디터 레이아웃 빌더(씬·Prefab 베이크)와 런타임 목록 생성이 같은 모양을 쓰도록 공유한다.
    /// </summary>
    public sealed class UiFactory
    {
        private const float DESTRUCTIVE_BUTTON_PIXELS_PER_UNIT = 2f;
        private const float MOUSE_HINT_WIDTH = 520f;
        private const float MOUSE_HINT_ROW_HEIGHT = 26f;
        private const float MOUSE_HINT_ROW_GAP = 4f;
        private const float MOUSE_HINT_RIGHT_MARGIN = 24f;
        private const float MOUSE_HINT_BOTTOM_MARGIN = 15f;
        private const float MOUSE_HINT_ICON_SIZE = 22f;
        private const float MOUSE_HINT_ICON_SPACING = 8f;
        private const float MOUSE_HINT_FONT_SIZE = 17f;
        private static readonly Color MOUSE_HINT_COLOR = new Color(0.54f, 0.67f, 0.77f, 1f);

        private readonly TMP_FontAsset _font;
        private readonly GameObject _panelPrefab;
        private readonly GameObject _buttonPrefab;
        private readonly Func<GameObject, Transform, GameObject> _prefabInstantiator;

        public TMP_FontAsset Font => _font;
        public GameObject PanelPrefab => _panelPrefab;
        public GameObject ButtonPrefab => _buttonPrefab;

        /// <summary>공통 패널·버튼 Prefab과 텍스트 글꼴로 UI 생성기를 만든다.</summary>
        public UiFactory(TMP_FontAsset font, GameObject panelPrefab, GameObject buttonPrefab,
            Func<GameObject, Transform, GameObject> prefabInstantiator = null)
        {
            _font = font;
            _panelPrefab = panelPrefab;
            _buttonPrefab = buttonPrefab;
            _prefabInstantiator = prefabInstantiator;
        }

        /// <summary>1280×720 기준 Overlay Canvas와 중앙 정렬된 논리 화면 루트를 만들고 루트를 반환한다.</summary>
        public static RectTransform CreateCanvas(Transform parent, string name)
        {
            GameObject canvasTarget = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasTarget.transform.SetParent(parent, false);
            canvasTarget.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasTarget.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UiTheme.SCREEN_WIDTH, UiTheme.SCREEN_HEIGHT);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform root = Rect(canvasTarget.transform, "LogicalScreen", 0, 0, UiTheme.SCREEN_WIDTH, UiTheme.SCREEN_HEIGHT);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            return root;
        }

        /// <summary>주어진 위치와 크기의 좌상단 기준 RectTransform을 생성한다.</summary>
        public static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            GameObject target = new GameObject(name, typeof(RectTransform));
            target.transform.SetParent(parent, false);
            RectTransform rect = target.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        /// <summary>
        /// 지정한 부모의 우측 하단에 마우스 조작 안내 세 줄을 만들고 새 루트를 반환한다.
        /// 첫 줄은 uniqueText와 leftIcon을 쓰고, 둘째·셋째 줄은 휠·우클릭 공통 현지화 문구와 아이콘을 쓴다.
        /// 아이콘 스프라이트가 없으면 그 아이콘만 숨기며 텍스트는 한 줄로 유지한다.
        /// </summary>
        public static RectTransform CreateMouseControls(RectTransform parent, TMP_FontAsset font, Sprite wheelIcon, Sprite leftIcon,
            Sprite rightIcon, string uniqueText)
        {
            if (font == null) font = TMP_Settings.defaultFontAsset;
            float height = MOUSE_HINT_ROW_HEIGHT * 3f + MOUSE_HINT_ROW_GAP * 2f;
            RectTransform root = Rect(parent, "MouseControls", 0f, 0f, MOUSE_HINT_WIDTH, height);
            root.anchorMin = root.anchorMax = new Vector2(1f, 0f);
            root.pivot = new Vector2(1f, 0f);
            root.anchoredPosition = new Vector2(-MOUSE_HINT_RIGHT_MARGIN, MOUSE_HINT_BOTTOM_MARGIN);

            CreateMouseControlRow(root, "Primary", leftIcon, uniqueText, 2, font);
            CreateMouseControlRow(root, "Zoom", wheelIcon, GameData.L("ui.mouseControls.zoom"), 1, font);
            CreateMouseControlRow(root, "Pan", rightIcon, GameData.L("ui.mouseControls.pan"), 0, font);
            return root;
        }

        /// <summary>안내 한 줄을 우측 정렬로 만들고 아이콘과 문구를 수평 배치한다. rowIndex는 아래에서부터의 줄 위치다.</summary>
        private static void CreateMouseControlRow(RectTransform parent, string name, Sprite icon, string text, int rowIndex,
            TMP_FontAsset font)
        {
            RectTransform row = Rect(parent, name, 0f, 0f, MOUSE_HINT_WIDTH, MOUSE_HINT_ROW_HEIGHT);
            row.anchorMin = row.anchorMax = new Vector2(1f, 0f);
            row.pivot = new Vector2(1f, 0f);
            row.anchoredPosition = new Vector2(0f, rowIndex * (MOUSE_HINT_ROW_HEIGHT + MOUSE_HINT_ROW_GAP));

            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.spacing = MOUSE_HINT_ICON_SPACING;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            if (icon != null)
            {
                RectTransform iconRect = Rect(row, "Icon", 0f, 0f, MOUSE_HINT_ICON_SIZE, MOUSE_HINT_ICON_SIZE);
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                Image image = iconRect.gameObject.AddComponent<Image>();
                image.sprite = icon;
                image.preserveAspect = true;
                image.raycastTarget = false;
                LayoutElement iconLayout = iconRect.gameObject.AddComponent<LayoutElement>();
                iconLayout.preferredWidth = MOUSE_HINT_ICON_SIZE;
                iconLayout.preferredHeight = MOUSE_HINT_ICON_SIZE;
            }

            TextMeshProUGUI label = UnityEngine.Object.Instantiate(
                Resources.Load<TextMeshProUGUI>("RuneCode/UI/Elements/UiText"), row, false);
            label.name = "Text";
            label.font = font;
            label.fontSize = MOUSE_HINT_FONT_SIZE;
            label.fontStyle = FontStyles.Normal;
            label.color = MOUSE_HINT_COLOR;
            label.text = text;
            label.raycastTarget = false;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.MidlineRight;
        }

        /// <summary>공통 패널 Prefab을 지정 위치에 배치하고 포인터 차단 여부를 설정한다.</summary>
        public RectTransform Panel(Transform parent, float x, float y, float width, float height, string name = "Panel")
        {
            GameObject instance = InstantiatePrefab(_panelPrefab, parent, name);
            RectTransform rect = Place(instance, x, y, width, height);
            Image image = instance.GetComponent<Image>();
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>지정 위치에 글꼴, 크기와 표시 내용을 설정한 TMP 텍스트를 생성한다.</summary>
        public TextMeshProUGUI Text(Transform parent, float x, float y, float width, float height, string value, float size,
            FontStyles style = FontStyles.Normal, string name = "Label", string prefabName = "UiText")
        {
            RectTransform rect = Place(InstantiatePrefab(Resources.Load<GameObject>("RuneCode/UI/Elements/" + prefabName), parent, name), x, y, width, height);
            TextMeshProUGUI text = rect.GetComponent<TextMeshProUGUI>();
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.text = value;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        /// <summary>button의 공통 클릭음 리스너만 교체해 중복 없이 한 번 연결하고 다른 클릭 동작은 유지한다.</summary>
        public static void BindClickSound(Button button)
        {
            button.onClick.RemoveListener(PlayClickSound);
            button.onClick.AddListener(PlayClickSound);
        }

        /// <summary>공통 UI 버튼 클릭 효과음을 한 번 재생한다.</summary>
        private static void PlayClickSound() => SoundManager.Instance.PlayClickButton();

        /// <summary>버튼 프리팹을 배치하고 문구·공통 클릭음·action 콜백을 연결한다. 표시 스타일은 프리팹에서 관리한다.</summary>
        public Button Button(Transform parent, float x, float y, float width, float height, string label, Action action = null,
            float size = 14, string name = "Button", string prefabName = null)
        {
            GameObject instance = InstantiatePrefab(prefabName == null ? _buttonPrefab : Resources.Load<GameObject>("RuneCode/UI/Elements/" + prefabName), parent, name);
            RectTransform rect = Place(instance, x, y, width, height);
            Image image = instance.GetComponent<Image>();
            image.raycastTarget = true;
            Button button = instance.GetComponent<Button>();
            BindClickSound(button);
            if (action != null) button.onClick.AddListener(() => action());
            TextMeshProUGUI text = instance.GetComponentInChildren<TextMeshProUGUI>(true);
            text.font = _font;
            text.fontSize = size;
            text.text = label;
            text.richText = true;
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.Midline;
            return button;
        }

        /// <summary>문자 입력 필드를 TMP 뷰포트, 표시 문자와 안내 문구에 연결한다.</summary>
        public TMP_InputField Input(Transform parent, float x, float y, float width, float height, string value, string placeholder, string name = "Input")
        {
            RectTransform rect = Place(InstantiatePrefab(Resources.Load<GameObject>("RuneCode/UI/Elements/UiInput"), parent, name), x, y, width, height);
            TMP_InputField field = rect.GetComponent<TMP_InputField>();
            field.text = value;
            ((TMP_Text)field.placeholder).text = placeholder;
            field.fontAsset = _font;
            return field;
        }

        /// <summary>마스킹된 스크롤 영역과 자동 높이의 세로 목록을 생성하고 콘텐츠를 반환한다.</summary>
        public static RectTransform ScrollList(Transform parent, string name, float x, float y, float width, float height)
        {
            GameObject instance = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("RuneCode/UI/Elements/UiScrollList"), parent, false);
            instance.name = name;
            Place(instance, x, y, width, height);
            return instance.GetComponent<ScrollRect>().content;
        }

        /// <summary>지정된 부모 아래 공통 UI Prefab 인스턴스를 만들고 이름을 지정한다.</summary>
        private GameObject InstantiatePrefab(GameObject prefab, Transform parent, string name)
        {
            GameObject instance = _prefabInstantiator == null
                ? UnityEngine.Object.Instantiate(prefab, parent, false)
                : _prefabInstantiator(prefab, parent);
            instance.name = name;
            return instance;
        }

        /// <summary>새 Prefab 인스턴스를 좌상단 기준 좌표와 크기에 맞춰 배치한다.</summary>
        private static RectTransform Place(GameObject instance, float x, float y, float width, float height)
        {
            RectTransform rect = (RectTransform)instance.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        /// <summary>버튼에 기존 빨간 스프라이트와 테두리 표시 배율을 함께 적용한다.</summary>
        public static void ApplyDestructiveButtonStyle(Button button)
        {
            Image image = (Image)button.targetGraphic;
            image.sprite = Resources.Load<Sprite>("RuneCode/UI/Skins/ButtonDestructive");
            image.pixelsPerUnitMultiplier = DESTRUCTIVE_BUTTON_PIXELS_PER_UNIT;
        }

        /// <summary>세로 목록 요소의 최소 높이와 원하는 높이를 고정한다.</summary>
        public static void Layout(GameObject target, float height)
        {
            LayoutElement layout = target.GetComponent<LayoutElement>();
            if (layout == null) layout = target.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
        }

        /// <summary>패널의 기존 자식을 비활성화한 뒤 제거한다. 편집 모드에서는 즉시 제거한다.</summary>
        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }

        /// <summary>현재 선택된 UI가 TMP 입력 필드이면 true를 반환해 단축키 처리를 막는다.</summary>
        public static bool IsTyping()
        {
            return EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
                && EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        }
    }
}
