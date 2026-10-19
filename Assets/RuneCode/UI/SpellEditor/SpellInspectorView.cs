using System;
using TMPro;
using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 인스펙터 영역의 표시 도우미 View다. 위에서부터 차례로 제목·파라미터 행·안내 문구·버튼·검증 목록을 쌓는다.
    /// 무엇을 어떤 동작과 함께 표시할지는 SpellInspectorPresenter가 정한다.
    /// </summary>
    public sealed class SpellInspectorView
    {
        private const float LEFT = 14f;
        private const float WIDTH = 222f;
        private const float PARAMETER_ROW_HEIGHT = 64f;
        private const float SPELL_PARAMETER_ROW_HEIGHT = 76f;
        private const float VALIDATION_BOTTOM = 460f;
        private const float MIN_VALIDATION_HEIGHT = 24f;

        private readonly RectTransform _panel;
        private readonly UiFactory _ui;
        private readonly SpellParameterRow _parameterRowPrefab;
        private readonly UiRow _issueRowPrefab;
        private float _top;

        /// <summary>인스펙터 패널, UI 생성기, 파라미터 행·검증 행 Prefab으로 도우미를 만든다.</summary>
        public SpellInspectorView(RectTransform panel, UiFactory ui, SpellParameterRow parameterRowPrefab, UiRow issueRowPrefab)
        {
            _panel = panel;
            _ui = ui;
            _parameterRowPrefab = parameterRowPrefab;
            _issueRowPrefab = issueRowPrefab;
        }

        /// <summary>인스펙터 내용을 모두 지우고 머리글을 다시 그린다.</summary>
        public void Begin()
        {
            UiFactory.ClearChildren(_panel);
            _ui.Text(_panel, LEFT, 12, WIDTH, 27, GameData.L("ui.inspector"), 17, FontStyles.Bold, prefabName: "InspectorHeading");
            _top = 50;
        }

        /// <summary>노드 이름 줄을 추가한다.</summary>
        public void AddTitle(string text)
        {
            _ui.Text(_panel, LEFT, _top, WIDTH, 28, text, 22, prefabName: "InspectorTitle");
            _top += 37;
        }

        /// <summary>안내·정보 문구를 내용이 들어갈 높이로 추가하고 실제 높이와 간격만큼 아래로 내려간다.</summary>
        public void AddNote(string text, float height, float spacing)
        {
            TextMeshProUGUI note = _ui.Text(_panel, LEFT, _top, WIDTH, height, text, 16, prefabName: "InspectorNote");
            float contentHeight = note.GetPreferredValues(text, WIDTH, Mathf.Infinity).y;
            float actualHeight = Mathf.Max(height, Mathf.Ceil(contentHeight) + 2f);
            ((RectTransform)note.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, actualHeight);
            _top += actualHeight + spacing;
        }

        /// <summary>노드 미선택 안내 문구를 추가한다.</summary>
        public void AddEmptyHint(string text)
        {
            _ui.Text(_panel, LEFT, _top, 224, 62, text, 16, prefabName: "InspectorHint");
            _top += 72;
        }

        /// <summary>숫자 파라미터 행을 추가한다. 배율이면 증감 버튼을 표시하고, 입력 확정·증감 클릭을 동작으로 연결한다.</summary>
        public void AddNumberRow(string label, string value, bool isScale, Action<string> onEdit, Action onMinus, Action onPlus)
        {
            SpellParameterRow row = CreateRow();
            row.ConfigureNumber(label, value, isScale);
            row.Input.contentType = TMP_InputField.ContentType.DecimalNumber;
            row.Input.onEndEdit.AddListener(text => onEdit(text));
            if (isScale)
            {
                row.MinusButton.onClick.AddListener(() => onMinus());
                row.PlusButton.onClick.AddListener(() => onPlus());
            }
            _top += PARAMETER_ROW_HEIGHT;
        }

        /// <summary>문자열 파라미터 행을 추가하고 입력 확정을 동작으로 연결한다.</summary>
        public void AddTextRow(string label, string value, Action<string> onEdit)
        {
            SpellParameterRow row = CreateRow();
            row.ConfigureText(label, value);
            row.Input.onEndEdit.AddListener(text => onEdit(text));
            _top += PARAMETER_ROW_HEIGHT;
        }

        /// <summary>선택지 파라미터 행을 추가하고 클릭(다음 선택지)을 동작으로 연결한다.</summary>
        public void AddEnumRow(string label, string valueLabel, Action onClick)
        {
            SpellParameterRow row = CreateRow();
            row.ConfigureEnum(label, valueLabel);
            row.OptionButton.onClick.AddListener(() => onClick());
            _top += PARAMETER_ROW_HEIGHT;
        }

        /// <summary>프리셋 호출 대상 행을 추가하고 클릭(대상 선택 팝업)을 동작으로 연결한다.</summary>
        public void AddSpellRow(string label, string buttonLabel, Action onClick)
        {
            SpellParameterRow row = CreateRow();
            row.ConfigureSpell(label, buttonLabel);
            row.SpellButton.onClick.AddListener(() => onClick());
            _top += SPELL_PARAMETER_ROW_HEIGHT;
        }

        /// <summary>프리팹 버튼을 추가하고 클릭을 동작으로 연결한다.</summary>
        public void AddButton(string label, string prefabName, Action onClick)
        {
            _ui.Button(_panel, LEFT, _top, WIDTH, 29, label, onClick, 12, prefabName: prefabName);
            _top += 42;
        }

        /// <summary>검증 제목과 남은 높이를 채우는 스크롤 목록을 추가하고, 이어서 AddIssue로 채울 목록을 반환한다.</summary>
        public RectTransform BeginValidation()
        {
            _ui.Text(_panel, LEFT, _top, WIDTH, 25, GameData.L("ui.validation"), 15, FontStyles.Bold, prefabName: "InspectorHeading");
            _top += 31;
            float availableHeight = _panel.rect.height - _top - 12f;
            float height = Mathf.Max(MIN_VALIDATION_HEIGHT, Mathf.Min(VALIDATION_BOTTOM - _top, availableHeight));
            return UiFactory.ScrollList(_panel, "Validation", 12, _top, 228, height);
        }

        /// <summary>검증 목록에 문구 행을 추가하고 클릭 동작을 연결한다.</summary>
        public void AddIssue(RectTransform list, string label, string prefabName, Action onClick)
        {
            UiRow prefab = prefabName == null ? _issueRowPrefab : Resources.Load<UiRow>("RuneCode/UI/Elements/" + prefabName);
            UnityEngine.Object.Instantiate(prefab, list).Configure(label, 56, onClick, 11);
        }

        /// <summary>현재 위치에 파라미터 행 Prefab을 만든다.</summary>
        private SpellParameterRow CreateRow()
        {
            SpellParameterRow row = UnityEngine.Object.Instantiate(_parameterRowPrefab, _panel);
            ((RectTransform)row.transform).anchoredPosition = new Vector2(LEFT, -_top);
            return row;
        }
    }
}
