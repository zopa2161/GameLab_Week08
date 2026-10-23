using System;
using System.Collections.Generic;

using UnityEngine;

using UnityEditor;
using UnityEditorInternal;

namespace RuneCode.Editor
{
    /// <summary>
    /// DPS 측정 조합 셋(Data/Sets/*.json)을 만들고 편집하는 창이다. 플레이어 프로필·등급·체인 길이·프리셋·제외 패턴을 고치고
    /// 생성될 조합을 미리보기에서 확인하며, 조합을 체크 해제하면 그 조합 ID가 개별 제외 규칙으로 셋에 들어간다.
    /// 저장한 셋으로 바로 측정을 실행할 수 있다.
    /// </summary>
    public sealed class DpsBuildSetWindow : EditorWindow
    {
        private const string MENU_PATH = "Tools/RuneCode/DPS Measurement/Build Set Editor";
        private const string WINDOW_TITLE = "DPS Build Set";
        private const string BEHAVIOR_CATEGORY = "behavior";
        private const string MODIFIER_CATEGORY = "modifier";
        private const string INDIVIDUAL_EXCLUSION_REASON = "개별 제외";
        private const string MANUAL_REASON_PREFIX = "manual:";
        private const string UNKNOWN_LABEL_PREFIX = "(알 수 없음) ";
        private const char WILDCARD = '*';
        private const char REASON_SEPARATOR = ':';
        private const int MAX_CHAIN_LENGTH = 3;
        private const int SAMPLE_BUILD_COUNT = 20;
        private const int PREVIEW_FILTER_ALL = 0;
        private const int PREVIEW_FILTER_INCLUDED = 1;
        private const int PREVIEW_FILTER_EXCLUDED = 2;
        private const float EDIT_PANEL_WIDTH = 470f;
        private const float PRESET_ID_WIDTH = 60f;
        private const float FIELD_GAP = 4f;
        private const float ROW_PADDING = 2f;
        private const float PREVIEW_ROW_HEIGHT = 18f;
        private const float TOGGLE_WIDTH = 18f;
        private const float BUILD_ID_WIDTH = 170f;
        private const float SET_POPUP_WIDTH = 160f;
        private const float NEW_NAME_WIDTH = 140f;

        private const string PROFILE_PATH = "_set._suite._profile";
        private const string GRADES_PATH = "_set._suite._grades";
        private const string CHAIN_LENGTHS_PATH = "_set._suite._chainLengths";
        private const string PRESETS_PATH = "_set._suite._presets";
        private const string EXCLUSIONS_PATH = "_set._exclusions._entries";

        private static readonly string[] PREVIEW_FILTER_LABELS = { "전체", "측정 대상", "제외" };
        private static readonly Dictionary<string, string> REASON_LABELS = new Dictionary<string, string>
        {
            { "excludedRune", "금지 룬" },
            { "manual", "수동 제외" },
            { "compileError", "컴파일 오류" },
            { "overEnergy", "전력 초과" }
        };

        private BuildSetHost _host;
        private SerializedObject _serialized;
        private ReorderableList _presetList;
        private ReorderableList _exclusionList;

        private List<string> _setNames = new List<string>();
        private string _currentSetName;
        private string _newSetName = "";
        private string _statusMessage = "";
        private bool _statusIsError;

        private string[] _behaviorIds = Array.Empty<string>();
        private string[] _behaviorLabels = Array.Empty<string>();
        private string[] _modifierIds = Array.Empty<string>();
        private string[] _modifierLabels = Array.Empty<string>();
        private string[] _gradeIds = Array.Empty<string>();

        private readonly List<PreviewRow> _previewRows = new List<PreviewRow>();
        private readonly List<int> _visibleRowIndexes = new List<int>();
        private bool _hasPreview;
        private bool _isPreviewStale;
        private int _previewFilter;
        private string _previewSearch = "";
        private Vector2 _editScroll;
        private Vector2 _previewScroll;

        /// <summary>조합 셋 편집 창을 연다.</summary>
        [MenuItem(MENU_PATH)]
        public static void Open()
        {
            GetWindow<DpsBuildSetWindow>(WINDOW_TITLE);
        }

        void OnEnable()
        {
            saveChangesMessage = "저장하지 않은 조합 셋 변경이 있습니다. 저장할까요?";
            GameData.Load();
            LoadCatalogOptions();
            RefreshSetNames();
            string initialName = _setNames.Contains(DpsBuildSetStore.DEFAULT_SET_NAME) || _setNames.Count == 0
                ? DpsBuildSetStore.DEFAULT_SET_NAME
                : _setNames[0];
            if (DpsBuildSetStore.Exists(initialName)) LoadSet(initialName);
            else CreateEmptySet(initialName);
        }

        void OnDisable()
        {
            if (_host != null) DestroyImmediate(_host);
            _host = null;
            _serialized = null;
        }

        void OnGUI()
        {
            if (_serialized == null) return;
            _serialized.Update();
            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawEditPanel();
            DrawPreviewPanel();
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_statusMessage))
                EditorGUILayout.HelpBox(_statusMessage, _statusIsError ? MessageType.Error : MessageType.Info);
        }

        /// <summary>저장하지 않은 변경을 창을 닫을 때 저장한다. 검증에 실패하면 저장하지 않고 상태 메시지를 남긴다.</summary>
        public override void SaveChanges()
        {
            if (TrySaveCurrentSet()) base.SaveChanges();
        }

        /// <summary>룬 데이터에서 행동·모드 룬과 행동 등급 목록을 읽어 드롭다운 선택지를 만든다. 비활성 룬은 뺀다.</summary>
        private void LoadCatalogOptions()
        {
            var behaviorIds = new List<string>();
            var behaviorLabels = new List<string>();
            var modifierIds = new List<string>();
            var modifierLabels = new List<string>();
            foreach (RuneDefinition rune in GameData.Runes.All)
            {
                if (rune.IsDisabled) continue;
                if (rune.Category == BEHAVIOR_CATEGORY)
                {
                    behaviorIds.Add(rune.Id);
                    behaviorLabels.Add(rune.Name + " (" + rune.Id + ")");
                }
                else if (rune.Category == MODIFIER_CATEGORY)
                {
                    modifierIds.Add(rune.Id);
                    modifierLabels.Add(rune.Name + " (" + rune.Id + ")");
                }
            }
            _behaviorIds = behaviorIds.ToArray();
            _behaviorLabels = behaviorLabels.ToArray();
            _modifierIds = modifierIds.ToArray();
            _modifierLabels = modifierLabels.ToArray();
            _gradeIds = new List<string>(GameData.Runes.BehaviorGradeOrder).ToArray();
        }

        /// <summary>셋 폴더의 셋 이름 목록을 다시 읽는다.</summary>
        private void RefreshSetNames()
        {
            _setNames = DpsBuildSetStore.ListNames();
        }

        /// <summary>셋 파일을 읽어 편집 대상으로 바꾸고 미리보기와 변경 표시를 초기화한다.</summary>
        private void LoadSet(string name)
        {
            BindSet(name, DpsBuildSetStore.Load(name));
            SetStatus(name + " 셋을 불러왔습니다.", false);
        }

        /// <summary>셋 파일이 하나도 없을 때 빈 셋을 편집 대상으로 만든다. 저장하기 전까지 파일은 생기지 않는다.</summary>
        private void CreateEmptySet(string name)
        {
            BindSet(name, new DpsBuildSet());
            hasUnsavedChanges = true;
            SetStatus("셋 파일이 없어 빈 " + name + " 셋을 만들었습니다. 저장하면 파일이 생깁니다.", false);
        }

        /// <summary>셋 데이터를 편집용 호스트에 넣고 SerializedObject와 목록 UI를 다시 만든다.</summary>
        private void BindSet(string name, DpsBuildSet set)
        {
            if (_host == null)
            {
                _host = CreateInstance<BuildSetHost>();
                _host.hideFlags = HideFlags.HideAndDontSave;
            }
            _host.Set = set;
            _currentSetName = name;
            _serialized = new SerializedObject(_host);
            _serialized.FindProperty(PROFILE_PATH).isExpanded = true;
            _presetList = CreatePresetList();
            _exclusionList = CreateExclusionList();
            _previewRows.Clear();
            _visibleRowIndexes.Clear();
            _hasPreview = false;
            _isPreviewStale = false;
            hasUnsavedChanges = false;
            titleContent = new GUIContent(WINDOW_TITLE);
        }

        /// <summary>셋 선택, 저장, 되돌리기, 복제, 삭제 버튼을 그린다. 셋을 바꾸기 전에 저장하지 않은 변경을 확인한다.</summary>
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("셋", GUILayout.ExpandWidth(false));
            int currentIndex = _setNames.IndexOf(_currentSetName);
            string[] options = _setNames.ToArray();
            int nextIndex = EditorGUILayout.Popup(currentIndex, options, EditorStyles.toolbarPopup, GUILayout.Width(SET_POPUP_WIDTH));
            if (nextIndex != currentIndex && nextIndex >= 0 && ConfirmDiscardChanges()) LoadSet(options[nextIndex]);
            if (hasUnsavedChanges) GUILayout.Label("(저장 안 됨)", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));

            using (new EditorGUI.DisabledScope(!hasUnsavedChanges))
            {
                if (GUILayout.Button("저장", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false))) TrySaveCurrentSet();
                if (GUILayout.Button("되돌리기", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false))
                    && DpsBuildSetStore.Exists(_currentSetName)) LoadSet(_currentSetName);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("새 셋 이름", GUILayout.ExpandWidth(false));
            _newSetName = EditorGUILayout.TextField(_newSetName, EditorStyles.toolbarTextField, GUILayout.Width(NEW_NAME_WIDTH));
            if (GUILayout.Button("현재 셋 복제", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false))) DuplicateCurrentSet();
            using (new EditorGUI.DisabledScope(_currentSetName == DpsBuildSetStore.DEFAULT_SET_NAME))
            {
                if (GUILayout.Button("삭제", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false))) DeleteCurrentSet();
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 왼쪽 편집 영역을 그린다. 프로필·등급·체인 길이·프리셋·제외 패턴을 바꾸면 셋에 반영하고 저장 안 됨과 미리보기 갱신 필요를 표시한다.
        /// </summary>
        private void DrawEditPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(EDIT_PANEL_WIDTH));
            _editScroll = EditorGUILayout.BeginScrollView(_editScroll);
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("플레이어 프로필", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serialized.FindProperty(PROFILE_PATH), new GUIContent("프로필"), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("등급", EditorStyles.boldLabel);
            DrawGradeToggles(_serialized.FindProperty(GRADES_PATH));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("체인 길이", EditorStyles.boldLabel);
            DrawChainLengthToggles(_serialized.FindProperty(CHAIN_LENGTHS_PATH));

            EditorGUILayout.Space();
            _presetList.DoLayoutList();

            EditorGUILayout.Space();
            _exclusionList.DoLayoutList();

            EditorGUILayout.HelpBox("측정 조건(단일·무리 × 거리)과 금지 룬 목록은 셋 JSON 파일에서만 편집합니다. 현재 조건 "
                + _host.Set.Suite.Conditions.Count + "개.", MessageType.None);

            if (EditorGUI.EndChangeCheck())
            {
                _serialized.ApplyModifiedProperties();
                MarkChanged(true);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>룬 데이터의 등급 순서대로 토글을 그리고 켜진 등급만 같은 순서로 셋의 등급 목록에 다시 쓴다.</summary>
        private void DrawGradeToggles(SerializedProperty gradesProperty)
        {
            var selected = new HashSet<string>(_host.Set.Suite.Grades);
            var next = new List<string>();
            EditorGUILayout.BeginHorizontal();
            foreach (string grade in _gradeIds)
                if (GUILayout.Toggle(selected.Contains(grade), grade, EditorStyles.miniButton)) next.Add(grade);
            EditorGUILayout.EndHorizontal();
            if (next.Count == selected.Count && selected.SetEquals(next)) return;
            gradesProperty.ClearArray();
            for (int index = 0; index < next.Count; index++)
            {
                gradesProperty.InsertArrayElementAtIndex(index);
                gradesProperty.GetArrayElementAtIndex(index).stringValue = next[index];
            }
        }

        /// <summary>1부터 최대 체인 길이까지 토글을 그리고 켜진 길이만 오름차순으로 셋의 체인 길이 목록에 다시 쓴다.</summary>
        private void DrawChainLengthToggles(SerializedProperty chainLengthsProperty)
        {
            var selected = new HashSet<int>(_host.Set.Suite.ChainLengths);
            var next = new List<int>();
            EditorGUILayout.BeginHorizontal();
            for (int length = 1; length <= MAX_CHAIN_LENGTH; length++)
                if (GUILayout.Toggle(selected.Contains(length), length + "행동", EditorStyles.miniButton)) next.Add(length);
            EditorGUILayout.EndHorizontal();
            if (next.Count == selected.Count && selected.SetEquals(next)) return;
            chainLengthsProperty.ClearArray();
            for (int index = 0; index < next.Count; index++)
            {
                chainLengthsProperty.InsertArrayElementAtIndex(index);
                chainLengthsProperty.GetArrayElementAtIndex(index).intValue = next[index];
            }
        }

        /// <summary>프리셋 목록 UI를 만든다. 한 항목은 ID·행동 드롭다운 한 줄과 모드 다중 선택 한 줄로 그린다.</summary>
        private ReorderableList CreatePresetList()
        {
            SerializedProperty presets = _serialized.FindProperty(PRESETS_PATH);
            var list = new ReorderableList(_serialized, presets, true, true, true, true);
            float lineHeight = EditorGUIUtility.singleLineHeight;
            list.elementHeight = lineHeight * 2 + ROW_PADDING * 3;
            list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "프리셋 (" + presets.arraySize + "개, 순서 = 조합 열거 순서)");
            list.drawElementCallback = (rect, index, isActive, isFocused) => DrawPresetElement(rect, presets.GetArrayElementAtIndex(index));
            list.onAddCallback = target =>
            {
                int index = presets.arraySize;
                presets.InsertArrayElementAtIndex(index);
                SerializedProperty added = presets.GetArrayElementAtIndex(index);
                added.FindPropertyRelative("_presetId").stringValue = "P" + (index + 1);
                added.FindPropertyRelative("_behaviorId").stringValue = _behaviorIds.Length > 0 ? _behaviorIds[0] : "";
                added.FindPropertyRelative("_modifierIds").ClearArray();
                added.FindPropertyRelative("_elementId").stringValue = "";
            };
            return list;
        }

        /// <summary>프리셋 한 항목의 ID, 행동, 모드를 그린다. 룬 데이터에 없는 행동·모드 ID는 지우지 않고 알 수 없음으로 표시한다.</summary>
        private void DrawPresetElement(Rect rect, SerializedProperty preset)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            var firstLine = new Rect(rect.x, rect.y + ROW_PADDING, rect.width, lineHeight);
            var secondLine = new Rect(rect.x, firstLine.yMax + ROW_PADDING, rect.width, lineHeight);

            SerializedProperty presetId = preset.FindPropertyRelative("_presetId");
            presetId.stringValue = EditorGUI.TextField(new Rect(firstLine.x, firstLine.y, PRESET_ID_WIDTH, lineHeight), presetId.stringValue);

            SerializedProperty behaviorId = preset.FindPropertyRelative("_behaviorId");
            int behaviorIndex = Array.IndexOf(_behaviorIds, behaviorId.stringValue);
            string[] behaviorLabels = _behaviorLabels;
            if (behaviorIndex < 0)
            {
                behaviorLabels = new string[_behaviorLabels.Length + 1];
                Array.Copy(_behaviorLabels, behaviorLabels, _behaviorLabels.Length);
                behaviorLabels[_behaviorLabels.Length] = UNKNOWN_LABEL_PREFIX + behaviorId.stringValue;
                behaviorIndex = _behaviorLabels.Length;
            }
            var behaviorRect = new Rect(firstLine.x + PRESET_ID_WIDTH + FIELD_GAP, firstLine.y, firstLine.width - PRESET_ID_WIDTH - FIELD_GAP, lineHeight);
            int nextBehaviorIndex = EditorGUI.Popup(behaviorRect, behaviorIndex, behaviorLabels);
            if (nextBehaviorIndex != behaviorIndex && nextBehaviorIndex < _behaviorIds.Length) behaviorId.stringValue = _behaviorIds[nextBehaviorIndex];

            DrawModifierMask(secondLine, preset.FindPropertyRelative("_modifierIds"));
        }

        /// <summary>
        /// 모드 목록을 다중 선택 드롭다운으로 그린다. 선택이 바뀌면 룬 데이터 순서대로 모드 목록을 다시 쓰고
        /// 룬 데이터에 없는 기존 모드 ID는 뒤에 그대로 남긴다.
        /// </summary>
        private void DrawModifierMask(Rect rect, SerializedProperty modifierIds)
        {
            int mask = 0;
            var unknownIds = new List<string>();
            for (int index = 0; index < modifierIds.arraySize; index++)
            {
                string modifierId = modifierIds.GetArrayElementAtIndex(index).stringValue;
                int known = Array.IndexOf(_modifierIds, modifierId);
                if (known >= 0) mask |= 1 << known;
                else unknownIds.Add(modifierId);
            }
            string label = "모드 " + modifierIds.arraySize + "개" + (unknownIds.Count > 0 ? " (알 수 없음 " + unknownIds.Count + ")" : "");
            int nextMask = EditorGUI.MaskField(rect, label, mask, _modifierLabels);
            if (nextMask == mask) return;

            var next = new List<string>();
            for (int known = 0; known < _modifierIds.Length; known++)
                if ((nextMask & (1 << known)) != 0) next.Add(_modifierIds[known]);
            next.AddRange(unknownIds);
            modifierIds.ClearArray();
            for (int index = 0; index < next.Count; index++)
            {
                modifierIds.InsertArrayElementAtIndex(index);
                modifierIds.GetArrayElementAtIndex(index).stringValue = next[index];
            }
        }

        /// <summary>제외 규칙 목록 UI를 만든다. 한 항목은 조합 ID 패턴과 사유를 한 줄에 그린다.</summary>
        private ReorderableList CreateExclusionList()
        {
            SerializedProperty entries = _serialized.FindProperty(EXCLUSIONS_PATH);
            var list = new ReorderableList(_serialized, entries, true, true, true, true);
            float lineHeight = EditorGUIUtility.singleLineHeight;
            list.elementHeight = lineHeight + ROW_PADDING * 2;
            list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "제외 규칙 (조합 ID 또는 '*' 패턴, 예: S:P2>*)");
            list.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                float halfWidth = (rect.width - FIELD_GAP) * 0.5f;
                var patternRect = new Rect(rect.x, rect.y + ROW_PADDING, halfWidth, lineHeight);
                var reasonRect = new Rect(patternRect.xMax + FIELD_GAP, patternRect.y, halfWidth, lineHeight);
                SerializedProperty pattern = entry.FindPropertyRelative("_pattern");
                SerializedProperty reason = entry.FindPropertyRelative("_reason");
                pattern.stringValue = EditorGUI.TextField(patternRect, pattern.stringValue);
                reason.stringValue = EditorGUI.TextField(reasonRect, reason.stringValue);
            };
            return list;
        }

        /// <summary>오른쪽 미리보기 영역을 그린다. 갱신 버튼, 개수 요약, 필터·검색, 조합 목록, 측정 실행 버튼을 둔다.</summary>
        private void DrawPreviewPanel()
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("미리보기 갱신", GUILayout.ExpandWidth(false))) RefreshPreview();
            if (_isPreviewStale) GUILayout.Label("규칙이 바뀌었습니다. 미리보기를 갱신하세요.", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (!_hasPreview)
            {
                EditorGUILayout.HelpBox("미리보기 갱신을 누르면 이 셋으로 만들어지는 조합과 제외 사유를 보여 줍니다.", MessageType.Info);
            }
            else
            {
                DrawPreviewSummary();
                DrawPreviewFilter();
                DrawPreviewRows();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("이 셋으로 측정")) RunCurrentSet(0);
            if (GUILayout.Button("앞 " + SAMPLE_BUILD_COUNT + "개만 측정")) RunCurrentSet(SAMPLE_BUILD_COUNT);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        /// <summary>미리보기 행으로 전체·측정 대상·제외 수, 제외 사유별 수, 예상 측정 시간을 그린다.</summary>
        private void DrawPreviewSummary()
        {
            int includedCount = 0;
            var reasonCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (PreviewRow row in _previewRows)
            {
                string reason = row.EffectiveReason;
                if (reason == null)
                {
                    includedCount++;
                    continue;
                }
                string category = ReasonCategory(reason);
                reasonCounts.TryGetValue(category, out int count);
                reasonCounts[category] = count + 1;
            }
            int conditionCount = _host.Set.Suite.Conditions.Count;
            EditorGUILayout.LabelField("전체 " + _previewRows.Count + "개 · 측정 대상 " + includedCount + "개 · 제외 "
                + (_previewRows.Count - includedCount) + "개", EditorStyles.boldLabel);
            var reasonText = new List<string>();
            foreach (KeyValuePair<string, int> pair in reasonCounts) reasonText.Add(ReasonLabel(pair.Key) + " " + pair.Value);
            if (reasonText.Count > 0) EditorGUILayout.LabelField("제외 사유: " + string.Join(", ", reasonText));
            EditorGUILayout.LabelField("측정 " + includedCount + " × 조건 " + conditionCount + " = " + includedCount * conditionCount
                + "회, 예상 약 " + DpsMeasurementCli.EstimateDuration(includedCount, conditionCount));
        }

        /// <summary>미리보기 필터(전체·측정 대상·제외)와 조합 ID 검색 칸을 그리고 바뀌면 보이는 행 목록을 다시 만든다.</summary>
        private void DrawPreviewFilter()
        {
            EditorGUILayout.BeginHorizontal();
            int nextFilter = GUILayout.Toolbar(_previewFilter, PREVIEW_FILTER_LABELS, GUILayout.ExpandWidth(false));
            string nextSearch = EditorGUILayout.TextField(_previewSearch, EditorStyles.toolbarSearchField);
            EditorGUILayout.EndHorizontal();
            if (nextFilter == _previewFilter && nextSearch == _previewSearch) return;
            _previewFilter = nextFilter;
            _previewSearch = nextSearch;
            RebuildVisibleRows();
        }

        /// <summary>
        /// 보이는 행만 그리는 가상 스크롤 목록이다. 체크를 해제하면 개별 제외 규칙을 추가하고, 개별 제외된 조합을 다시 체크하면 그 규칙을 지운다.
        /// 금지 룬·패턴·컴파일 오류·전력 초과로 빠진 조합은 체크할 수 없다.
        /// </summary>
        private void DrawPreviewRows()
        {
            EditorGUILayout.LabelField("보이는 조합 " + _visibleRowIndexes.Count + "개", EditorStyles.miniLabel);
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.ExpandHeight(true));
            Rect totalRect = GUILayoutUtility.GetRect(0f, _visibleRowIndexes.Count * PREVIEW_ROW_HEIGHT, GUILayout.ExpandWidth(true));
            float viewHeight = position.height;
            int first = Mathf.Max(0, Mathf.FloorToInt(_previewScroll.y / PREVIEW_ROW_HEIGHT));
            int last = Mathf.Min(_visibleRowIndexes.Count, first + Mathf.CeilToInt(viewHeight / PREVIEW_ROW_HEIGHT) + 1);
            for (int visible = first; visible < last; visible++)
            {
                PreviewRow row = _previewRows[_visibleRowIndexes[visible]];
                var rowRect = new Rect(totalRect.x, totalRect.y + visible * PREVIEW_ROW_HEIGHT, totalRect.width, PREVIEW_ROW_HEIGHT);
                bool isIncluded = row.EffectiveReason == null;
                using (new EditorGUI.DisabledScope(row.NaturalReason != null))
                {
                    bool nextIncluded = EditorGUI.Toggle(new Rect(rowRect.x, rowRect.y, TOGGLE_WIDTH, rowRect.height), isIncluded);
                    if (nextIncluded != isIncluded) SetIndividuallyExcluded(row, !nextIncluded);
                }
                EditorGUI.LabelField(new Rect(rowRect.x + TOGGLE_WIDTH, rowRect.y, BUILD_ID_WIDTH, rowRect.height), row.BuildId);
                if (!isIncluded)
                    EditorGUI.LabelField(new Rect(rowRect.x + TOGGLE_WIDTH + BUILD_ID_WIDTH, rowRect.y,
                        rowRect.width - TOGGLE_WIDTH - BUILD_ID_WIDTH, rowRect.height), row.EffectiveReason, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 현재 편집 중인 셋으로 조합을 생성해 미리보기 행을 만든다. 와일드카드가 없는 제외 규칙(개별 제외)은 생성에서 빼고
        /// 각 조합의 본래 판정(금지 룬·패턴·컴파일 오류·전력 초과) 위에 덧씌워 체크로 켜고 끌 수 있게 한다.
        /// </summary>
        private void RefreshPreview()
        {
            _serialized.ApplyModifiedProperties();
            string error = Validate(_host.Set);
            if (error != null)
            {
                SetStatus(error, true);
                return;
            }
            var patternExclusions = new DpsExclusionList();
            var individualReasons = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DpsExclusionEntry entry in _host.Set.Exclusions.Entries)
            {
                if (IsIndividualPattern(entry.Pattern)) individualReasons[entry.Pattern] = entry.Reason;
                else patternExclusions.Entries.Add(entry);
            }

            DpsGenerationResult generated;
            try
            {
                EditorUtility.DisplayProgressBar(WINDOW_TITLE, "조합을 생성하고 컴파일하는 중", 0.5f);
                GameData.Load();
                generated = DpsBuildGenerator.Generate(_host.Set.Suite, patternExclusions);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            _previewRows.Clear();
            foreach (SpellBuild build in generated.Builds)
            {
                individualReasons.TryGetValue(build.BuildId, out string individualReason);
                _previewRows.Add(new PreviewRow(build.BuildId, null, individualReason));
            }
            foreach (DpsExcludedBuild excluded in generated.ExcludedBuilds)
                _previewRows.Add(new PreviewRow(excluded.BuildId, excluded.Reason, null));
            _previewRows.Sort((first, second) => string.CompareOrdinal(first.BuildId, second.BuildId));
            _hasPreview = true;
            _isPreviewStale = false;
            RebuildVisibleRows();
            SetStatus("미리보기를 갱신했습니다. 조합 " + _previewRows.Count + "개.", false);
        }

        /// <summary>필터와 검색어에 맞는 미리보기 행 번호 목록을 다시 만든다. 검색은 조합 ID 부분 일치(대소문자 무시)다.</summary>
        private void RebuildVisibleRows()
        {
            _visibleRowIndexes.Clear();
            for (int index = 0; index < _previewRows.Count; index++)
            {
                PreviewRow row = _previewRows[index];
                bool isIncluded = row.EffectiveReason == null;
                if (_previewFilter == PREVIEW_FILTER_INCLUDED && !isIncluded) continue;
                if (_previewFilter == PREVIEW_FILTER_EXCLUDED && isIncluded) continue;
                if (!string.IsNullOrEmpty(_previewSearch)
                    && row.BuildId.IndexOf(_previewSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                _visibleRowIndexes.Add(index);
            }
        }

        /// <summary>
        /// 조합 하나를 개별 제외하거나 개별 제외를 푼다. 제외하면 조합 ID와 같은 제외 규칙을 추가하고, 풀면 조합 ID와 같은 규칙을 모두 지운다.
        /// 미리보기는 다시 생성하지 않고 그 행만 바꾼다.
        /// </summary>
        private void SetIndividuallyExcluded(PreviewRow row, bool isExcluded)
        {
            SerializedProperty entries = _serialized.FindProperty(EXCLUSIONS_PATH);
            if (isExcluded)
            {
                int index = entries.arraySize;
                entries.InsertArrayElementAtIndex(index);
                SerializedProperty added = entries.GetArrayElementAtIndex(index);
                added.FindPropertyRelative("_pattern").stringValue = row.BuildId;
                added.FindPropertyRelative("_reason").stringValue = INDIVIDUAL_EXCLUSION_REASON;
                row.IndividualReason = INDIVIDUAL_EXCLUSION_REASON;
            }
            else
            {
                for (int index = entries.arraySize - 1; index >= 0; index--)
                    if (entries.GetArrayElementAtIndex(index).FindPropertyRelative("_pattern").stringValue == row.BuildId)
                        entries.DeleteArrayElementAtIndex(index);
                row.IndividualReason = null;
            }
            _serialized.ApplyModifiedProperties();
            MarkChanged(false);
            RebuildVisibleRows();
        }

        /// <summary>셋 내용을 검사해 첫 오류 메시지를 반환한다. 등급·체인 길이·프리셋이 비었거나 프리셋 ID가 비었거나 중복이면 오류다.</summary>
        private static string Validate(DpsBuildSet set)
        {
            if (set.Suite.Grades.Count == 0) return "등급을 하나 이상 고르세요.";
            if (set.Suite.ChainLengths.Count == 0) return "체인 길이를 하나 이상 고르세요.";
            if (set.Suite.Presets.Count == 0) return "프리셋을 하나 이상 추가하세요.";
            var presetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BehaviorPreset preset in set.Suite.Presets)
            {
                if (string.IsNullOrEmpty(preset.PresetId)) return "ID가 빈 프리셋이 있습니다.";
                if (preset.PresetId.IndexOfAny(new[] { WILDCARD, '>', REASON_SEPARATOR }) >= 0)
                    return "프리셋 ID에 '*', '>', ':'는 쓸 수 없습니다: " + preset.PresetId;
                if (!presetIds.Add(preset.PresetId)) return "프리셋 ID가 중복됩니다: " + preset.PresetId;
                if (string.IsNullOrEmpty(preset.BehaviorId)) return "행동이 빈 프리셋이 있습니다: " + preset.PresetId;
            }
            return null;
        }

        /// <summary>현재 셋을 검증한 뒤 파일로 저장한다. 저장했으면 true를 반환하고 실패하면 상태 메시지에 이유를 남긴다.</summary>
        private bool TrySaveCurrentSet()
        {
            _serialized.ApplyModifiedProperties();
            string error = Validate(_host.Set);
            if (error != null)
            {
                SetStatus("저장하지 못했습니다. " + error, true);
                return false;
            }
            DpsBuildSetStore.Save(_currentSetName, _host.Set);
            RefreshSetNames();
            hasUnsavedChanges = false;
            SetStatus(_currentSetName + " 셋을 저장했습니다.", false);
            return true;
        }

        /// <summary>현재 편집 내용을 새 이름의 셋으로 저장하고 그 셋으로 전환한다. 이름이 규칙에 맞지 않거나 이미 있으면 상태 메시지만 남긴다.</summary>
        private void DuplicateCurrentSet()
        {
            string name = _newSetName.Trim();
            if (!DpsBuildSetStore.IsValidName(name))
            {
                SetStatus("셋 이름은 글자·숫자·'_'·'-'만 쓸 수 있습니다.", true);
                return;
            }
            if (DpsBuildSetStore.Exists(name))
            {
                SetStatus("이미 있는 셋 이름입니다: " + name, true);
                return;
            }
            _serialized.ApplyModifiedProperties();
            DpsBuildSet copy = JsonUtility.FromJson<DpsBuildSet>(JsonUtility.ToJson(_host.Set));
            DpsBuildSetStore.Save(name, copy);
            RefreshSetNames();
            _newSetName = "";
            LoadSet(name);
            SetStatus(name + " 셋을 만들었습니다.", false);
        }

        /// <summary>현재 셋 파일을 확인 후 지우고 기본 셋으로 전환한다. 기본 셋은 툴바에서 지울 수 없다.</summary>
        private void DeleteCurrentSet()
        {
            if (!EditorUtility.DisplayDialog(WINDOW_TITLE, _currentSetName + " 셋을 삭제할까요? 되돌릴 수 없습니다.", "삭제", "취소")) return;
            string deletedName = _currentSetName;
            DpsBuildSetStore.Delete(deletedName);
            RefreshSetNames();
            LoadSet(DpsBuildSetStore.DEFAULT_SET_NAME);
            SetStatus(deletedName + " 셋을 삭제했습니다.", false);
        }

        /// <summary>저장하지 않은 변경이 있으면 저장 후 실행할지 묻고, 저장된 현재 셋으로 측정을 실행한다. limit이 0이면 전체 조합이다.</summary>
        private void RunCurrentSet(int limit)
        {
            if (hasUnsavedChanges)
            {
                if (!EditorUtility.DisplayDialog(WINDOW_TITLE, "측정은 저장된 셋으로 실행합니다. 저장하고 실행할까요?", "저장 후 실행", "취소")) return;
                if (!TrySaveCurrentSet()) return;
            }
            DpsMeasurementCli.RunSetFromEditor(_currentSetName, limit);
            GUIUtility.ExitGUI();
        }

        /// <summary>저장하지 않은 변경이 있으면 버릴지 묻는다. 변경이 없거나 버리기로 하면 true를 반환한다.</summary>
        private bool ConfirmDiscardChanges()
        {
            return !hasUnsavedChanges
                || EditorUtility.DisplayDialog(WINDOW_TITLE, "저장하지 않은 변경을 버릴까요?", "버리기", "취소");
        }

        /// <summary>셋이 바뀌었음을 표시한다. isRuleChange면 미리보기가 현재 규칙과 달라졌다고 표시한다.</summary>
        private void MarkChanged(bool isRuleChange)
        {
            hasUnsavedChanges = true;
            if (isRuleChange && _hasPreview) _isPreviewStale = true;
        }

        /// <summary>창 아래쪽 상태 메시지를 바꾼다.</summary>
        private void SetStatus(string message, bool isError)
        {
            _statusMessage = message;
            _statusIsError = isError;
        }

        /// <summary>와일드카드가 없는 제외 패턴, 즉 조합 하나를 가리키는 개별 제외 규칙인지 반환한다.</summary>
        private static bool IsIndividualPattern(string pattern)
        {
            return !string.IsNullOrEmpty(pattern) && pattern.IndexOf(WILDCARD) < 0;
        }

        /// <summary>제외 사유 문자열에서 ':' 앞의 분류 코드를 반환한다. ':'가 없으면 전체를 반환한다.</summary>
        private static string ReasonCategory(string reason)
        {
            int separator = reason.IndexOf(REASON_SEPARATOR);
            return separator < 0 ? reason : reason.Substring(0, separator);
        }

        /// <summary>제외 분류 코드의 한국어 이름을 반환한다. 모르는 코드는 그대로 반환한다.</summary>
        private static string ReasonLabel(string category)
        {
            return REASON_LABELS.TryGetValue(category, out string label) ? label : category;
        }

        /// <summary>
        /// 미리보기 한 행이다. NaturalReason은 개별 제외를 뺀 생성 판정의 제외 사유(측정 대상이면 null),
        /// IndividualReason은 조합 ID와 같은 개별 제외 규칙의 사유(없으면 null)다.
        /// </summary>
        private sealed class PreviewRow
        {
            private readonly string _buildId;
            private readonly string _naturalReason;

            public string BuildId => _buildId;
            public string NaturalReason => _naturalReason;
            public string IndividualReason { get; set; }

            /// <summary>개별 제외까지 반영한 최종 제외 사유를 반환한다. 측정 대상이면 null이다.</summary>
            public string EffectiveReason => _naturalReason ?? (IndividualReason == null ? null : MANUAL_REASON_PREFIX + IndividualReason);

            /// <summary>조합 ID와 두 판정 사유로 행을 만든다.</summary>
            public PreviewRow(string buildId, string naturalReason, string individualReason)
            {
                _buildId = buildId;
                _naturalReason = naturalReason;
                IndividualReason = individualReason;
            }
        }

        /// <summary>SerializedObject로 조합 셋을 편집하기 위한 저장되지 않는 임시 객체다.</summary>
        private sealed class BuildSetHost : ScriptableObject
        {
            [SerializeField] private DpsBuildSet _set = new DpsBuildSet();

            public DpsBuildSet Set
            {
                get => _set;
                set => _set = value;
            }
        }
    }
}
