using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using UnityEditor;
using UnityEditorInternal;

namespace RuneCode.Editor
{
    /// <summary>
    /// Stage Table Editor의 드롭 테이블 탭이다. drops.json의 테이블별 분류(행동·강화·속성) 확률, 분류별 등급 확률과 후보 룬을 편집하고
    /// 저장 전에 런타임과 같은 검증을 거친다. 로드·저장·변경 상태는 스테이지 탭과 따로 관리한다.
    /// </summary>
    public sealed class DropTableEditorTab
    {
        private const float SIDEBAR_WIDTH = 240f;
        private const float SIDEBAR_ROW_HEIGHT = 22f;
        private const float FIELD_GAP = 6f;
        private const float ELEMENT_LEFT_INSET = 15f;
        private const float ELEMENT_RIGHT_INSET = 22f;
        private const float PERCENT_LABEL_WIDTH = 36f;
        private const float CATEGORY_POPUP_WIDTH = 72f;
        private const float WEIGHT_FIELD_WIDTH = 72f;
        private const string NEW_TABLE_ID = "drop.new";
        private const string COPY_SUFFIX = ".copy";

        private static readonly string[] CATEGORY_LABELS = { "행동", "강화", "속성" };
        private static readonly string[] REWARD_GRADES = { "", "C", "B", "A", "S" };
        private static readonly string[] REWARD_GRADE_LABELS = { "일반 드롭", "C 보상", "B 보상", "A 보상", "S 보상" };

        private static readonly Color SELECTION_BACKGROUND = new Color(0.24f, 0.44f, 0.76f, 0.35f);
        private static readonly Color UNKNOWN_VALUE_BACKGROUND = new Color(0.95f, 0.75f, 0.2f);
        private static readonly Color ERROR_TEXT_COLOR = new Color(0.95f, 0.45f, 0.4f);

        private static readonly float ROW_HEIGHT = EditorGUIUtility.singleLineHeight;

        private readonly Action _onSaved;

        private DropCatalogHost _host;
        private SerializedObject _serialized;
        private string _savePath = "";

        private RuneCatalog _runes;
        private ModifierGradeTable _modifierGrades;
        private readonly Dictionary<string, string[]> _runeIdsByCategory = new Dictionary<string, string[]>();

        private ReorderableList _entriesList;
        private Vector2 _sidebarScroll;
        private Vector2 _detailScroll;
        private int _selectedIndex;
        private bool _needsListRefresh;

        private bool _isDirty;
        private string _statusMessage = "";
        private bool _statusIsError;

        /// <summary>저장하지 않은 변경이 있는지 나타낸다.</summary>
        public bool IsDirty => _isDirty;

        /// <summary>drops.json의 프로젝트 경로다.</summary>
        public string SavePath => _savePath;

        /// <summary>마지막 로드·검증·저장 결과 메시지다.</summary>
        public string StatusMessage => _statusMessage;

        /// <summary>상태 메시지가 오류인지 나타낸다.</summary>
        public bool StatusIsError => _statusIsError;

        /// <summary>저장에 성공할 때 호출할 콜백을 받아 탭을 만든다.</summary>
        public DropTableEditorTab(Action onSaved)
        {
            _onSaved = onSaved;
        }

        /// <summary>룬·Modifier 등급표와 drops.json을 디스크에서 읽어 편집 대상으로 삼고 선택·리스트 캐시를 초기화한다. 검증 오류가 있는 파일도 고칠 수 있게 연다.</summary>
        public void Load()
        {
            Dispose();
            _isDirty = false;
            try
            {
                _runes = RuneCatalog.FromTable(JsonUtility.FromJson<RuneTableData>(StageTableEditorWindow.LoadResourceText(StageTableEditorWindow.RUNES_RESOURCE)));
                _modifierGrades = ModifierGradeTable.FromJson(StageTableEditorWindow.LoadResourceText(StageTableEditorWindow.GRADES_RESOURCE), _runes);
                CacheRuneIds();

                TextAsset asset = Resources.Load<TextAsset>(StageTableEditorWindow.DROPS_RESOURCE);
                if (asset == null) throw new FormatException("누락된 드롭 데이터: " + StageTableEditorWindow.DROPS_RESOURCE);
                DropCatalog catalog = JsonUtility.FromJson<DropCatalog>(asset.text);
                if (catalog == null || catalog.Tables == null) throw new FormatException("드롭 테이블 목록이 없습니다: " + StageTableEditorWindow.DROPS_RESOURCE);
                string path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path)) throw new FormatException("저장 경로를 찾지 못했습니다: " + StageTableEditorWindow.DROPS_RESOURCE);

                _host = ScriptableObject.CreateInstance<DropCatalogHost>();
                _host.hideFlags = HideFlags.HideAndDontSave;
                _host.SetCatalog(catalog);
                _serialized = new SerializedObject(_host);
                _savePath = path;
                NormalizeTables();
                SetStatus("불러오기 완료 - " + path, false);
            }
            catch (Exception exception)
            {
                Dispose();
                SetStatus("불러오기 실패 - " + exception.Message, true);
                return;
            }
            _selectedIndex = 0;
            RebuildEntriesList();
        }

        /// <summary>편집용 호스트를 파괴하고 SerializedObject 참조를 비운다.</summary>
        public void Dispose()
        {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
            _host = null;
            _serialized = null;
            _entriesList = null;
        }

        /// <summary>Undo·Redo 후 후보 리스트 캐시 재구성을 예약한다.</summary>
        public void OnUndoRedo()
        {
            _needsListRefresh = true;
        }

        /// <summary>좌측 테이블 목록과 우측 선택 테이블 편집 영역을 그린다. 데이터를 못 불러왔으면 오류와 다시 로드 버튼을 표시한다.</summary>
        public void Draw()
        {
            if (_serialized == null)
            {
                if (GUILayout.Button("다시 로드")) Load();
                return;
            }
            if (_needsListRefresh)
            {
                _needsListRefresh = false;
                RebuildEntriesList();
            }
            _serialized.Update();

            EditorGUILayout.BeginHorizontal();
            DrawSidebar();
            EditorGUILayout.BeginVertical();
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            DrawDetail();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            ApplyChanges();
        }

        /// <summary>저장 없이 현재 편집 상태를 런타임 검증(drops.json과 이를 참조하는 stages.json)으로 확인하고 결과를 상태 메시지에 남긴다.</summary>
        public void Validate()
        {
            if (_serialized == null) return;
            _serialized.ApplyModifiedProperties();
            try
            {
                ValidateDropJson(JsonUtility.ToJson(_host.Catalog, true));
                SetStatus("검증 통과 - 저장할 수 있습니다.", false);
            }
            catch (Exception exception)
            {
                SetStatus("검증 실패 - " + exception.Message, true);
            }
        }

        /// <summary>편집 상태를 JSON으로 내보내 런타임 검증을 통과하면 drops.json에 쓰고 TextAsset을 재임포트한 뒤 저장 콜백을 호출한다.</summary>
        public void Save()
        {
            if (_serialized == null) return;
            _serialized.ApplyModifiedProperties();
            string json = JsonUtility.ToJson(_host.Catalog, true);
            try
            {
                ValidateDropJson(json);
            }
            catch (Exception exception)
            {
                SetStatus("저장 차단 - " + exception.Message, true);
                return;
            }
            try
            {
                File.WriteAllText(_savePath, json);
                AssetDatabase.ImportAsset(_savePath);
            }
            catch (Exception exception)
            {
                SetStatus("저장 실패 - " + exception.Message, true);
                return;
            }
            _isDirty = false;
            SetStatus("저장 완료 - " + _savePath, false);
            _onSaved?.Invoke();
        }

        /// <summary>드롭 JSON을 디스크의 stages.json과 함께 런타임 로더로 검증하고 실패 시 예외를 던진다.</summary>
        private static void ValidateDropJson(string json)
        {
            StageTableEditorWindow.ValidateTables(StageTableEditorWindow.LoadResourceText(StageTableEditorWindow.STAGES_RESOURCE), json);
        }

        /// <summary>좌측에 테이블 ID 목록과 추가·복제·삭제 버튼을 그린다. 확률 합이 100이 아닌 테이블은 오류색으로 표시한다.</summary>
        private void DrawSidebar()
        {
            SerializedProperty tables = _serialized.FindProperty("_catalog._dropTables");
            EditorGUILayout.BeginVertical(GUILayout.Width(SIDEBAR_WIDTH));
            _sidebarScroll = EditorGUILayout.BeginScrollView(_sidebarScroll);
            for (int i = 0; i < tables.arraySize; i++)
            {
                SerializedProperty table = tables.GetArrayElementAtIndex(i);
                Rect row = EditorGUILayout.GetControlRect(GUILayout.Height(SIDEBAR_ROW_HEIGHT));
                if (i == _selectedIndex) EditorGUI.DrawRect(row, SELECTION_BACKGROUND);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && row.Contains(Event.current.mousePosition))
                {
                    SelectTable(i);
                    Event.current.Use();
                }
                Color previous = GUI.contentColor;
                if (!AreTotalsValid(table)) GUI.contentColor = ERROR_TEXT_COLOR;
                GUI.Label(new Rect(row.x + 4, row.y, row.width - 4, row.height), table.FindPropertyRelative("_id").stringValue);
                GUI.contentColor = previous;
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("추가")) AddTable(tables);
            using (new EditorGUI.DisabledScope(tables.arraySize == 0))
            {
                if (GUILayout.Button("복제")) DuplicateTable(tables);
                if (GUILayout.Button("삭제")) DeleteTable(tables);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        /// <summary>선택 테이블의 ID, 분류 확률, 행동·강화 등급 확률과 후보 룬 목록을 그린다.</summary>
        private void DrawDetail()
        {
            SerializedProperty table = SelectedTable();
            if (table == null)
            {
                EditorGUILayout.HelpBox("드롭 테이블이 없습니다. [추가]로 만드세요.", MessageType.Info);
                return;
            }
            DrawIdField(table.FindPropertyRelative("_id"));
            EditorGUILayout.HelpBox("새 런의 라운드 선택 보상 등급은 balance.json의 고정 목록을 섞어 사용합니다. 아래 등급 확률은 기존 진행과 확률 드롭에 적용되며, 후보 룬과 가중치는 새 런에도 적용됩니다.", MessageType.Info);
            EditorGUILayout.Space(4);
            DrawCategorySection(table.FindPropertyRelative("_categoryChances"));
            EditorGUILayout.Space(4);
            DrawGradeSection(table.FindPropertyRelative("_gradeChances"), DropCatalog.TYPE_BEHAVIOR);
            DrawGradeSection(table.FindPropertyRelative("_gradeChances"), DropCatalog.TYPE_MODIFIER);
            EditorGUILayout.HelpBox("속성 자체에는 등급이 없습니다. 속성 후보의 참여 모드 보상 등급을 지정하면 해당 보상의 모드들과 함께 추첨합니다. 일반 드롭으로 두면 모드 선택지에 참여하지 않습니다.", MessageType.Info);
            EditorGUILayout.Space(4);
            DrawEntriesSection();
        }

        /// <summary>테이블 ID 입력칸을 그리고 다른 테이블과 겹치면 경고한다. 입력을 마칠 때만 값을 반영한다.</summary>
        private void DrawIdField(SerializedProperty id)
        {
            string next = EditorGUILayout.DelayedTextField("테이블 ID", id.stringValue);
            if (next != id.stringValue)
            {
                id.stringValue = next;
                ApplyChanges();
            }
            if (CountId(_serialized.FindProperty("_catalog._dropTables"), id.stringValue) > 1)
                EditorGUILayout.HelpBox("다른 테이블과 ID가 겹칩니다.", MessageType.Warning);
            else
                EditorGUILayout.HelpBox("ID를 바꾸면 이 테이블을 참조하는 stages.json 라운드 보상·엘리트 드롭도 함께 바꿔야 합니다.", MessageType.None);
        }

        /// <summary>행동·강화·속성 확률 입력칸과 합계를 한 줄에 그린다.</summary>
        private void DrawCategorySection(SerializedProperty chances)
        {
            GUILayout.Label("분류 확률 (%)", EditorStyles.boldLabel);
            int total = 0;
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < DropCatalog.Categories.Count; i++)
            {
                SerializedProperty chance = FindRow(chances, DropCatalog.Categories[i], null);
                if (chance == null) continue;
                SerializedProperty percent = chance.FindPropertyRelative("_percent");
                DrawPercentField(CATEGORY_LABELS[i], percent);
                total += percent.intValue;
            }
            EditorGUILayout.EndHorizontal();
            DrawTotal(total);
        }

        /// <summary>분류의 등급 확률을 그린다. 스테이지 등급표 사용을 켜면 그 분류의 등급 행을 지우고, 끄면 등급 순서대로 0% 행을 만든다.</summary>
        private void DrawGradeSection(SerializedProperty grades, string category)
        {
            GUILayout.Label(GetCategoryLabel(category) + " 등급 확률 (%)", EditorStyles.boldLabel);
            bool usesStageGrades = !HasCategoryRows(grades, category);
            bool nextUsesStageGrades = EditorGUILayout.ToggleLeft("스테이지 등급표 사용 (elite_modifier_drops.json)", usesStageGrades);
            if (nextUsesStageGrades != usesStageGrades)
            {
                if (nextUsesStageGrades) RemoveCategoryRows(grades, category);
                else AddMissingGradeRows(grades, category);
                ApplyChanges();
                return;
            }
            if (usesStageGrades) return;

            int total = 0;
            EditorGUILayout.BeginHorizontal();
            foreach (string grade in GetGradeOrder(category))
            {
                SerializedProperty row = FindRow(grades, category, grade);
                if (row == null) continue;
                SerializedProperty percent = row.FindPropertyRelative("_percent");
                DrawPercentField(grade, percent);
                total += percent.intValue;
            }
            EditorGUILayout.EndHorizontal();
            DrawTotal(total);
        }

        /// <summary>후보 룬 목록과 후보 규칙 안내를 그린다.</summary>
        private void DrawEntriesSection()
        {
            GUILayout.Label("후보 룬", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("가중치는 같은 분류 후보 사이의 비율입니다. 분류에 후보가 없으면 그 분류의 활성 룬 전체에서 같은 가중치로 고르고, 굴린 등급을 지원하지 않는 후보는 제외됩니다.", MessageType.Info);
            _entriesList?.DoLayoutList();
        }

        /// <summary>라벨을 붙인 퍼센트 int 입력칸을 레이아웃에 그리고 값이 바뀌면 반영한다.</summary>
        private void DrawPercentField(string label, SerializedProperty percent)
        {
            float previousWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = PERCENT_LABEL_WIDTH;
            int next = EditorGUILayout.IntField(label, percent.intValue);
            EditorGUIUtility.labelWidth = previousWidth;
            if (next == percent.intValue) return;
            percent.intValue = next;
            ApplyChanges();
        }

        /// <summary>확률 합계를 표시하고 100이 아니면 오류색으로 경고한다.</summary>
        private static void DrawTotal(int total)
        {
            bool isValid = total == DropCatalog.TOTAL_PERCENT;
            Color previous = GUI.contentColor;
            if (!isValid) GUI.contentColor = ERROR_TEXT_COLOR;
            GUILayout.Label(isValid ? "합계 " + total + "%" : "합계 " + total + "% - 100이 되어야 합니다.", EditorStyles.miniLabel);
            GUI.contentColor = previous;
        }

        /// <summary>후보의 분류·룬·가중치와 속성의 참여 보상 등급을 편집한다. 분류를 바꾸면 룬과 보상 등급 조건을 초기화한다.</summary>
        private void DrawEntryRow(Rect rect, int index)
        {
            SerializedProperty entry = _entriesList.serializedProperty.GetArrayElementAtIndex(index);
            Rect content = new Rect(rect.x + ELEMENT_LEFT_INSET, rect.y + 1, rect.width - ELEMENT_LEFT_INSET - ELEMENT_RIGHT_INSET, ROW_HEIGHT);
            Rect categoryRect = new Rect(content.x, content.y, CATEGORY_POPUP_WIDTH, ROW_HEIGHT);
            Rect weightRect = new Rect(content.xMax - WEIGHT_FIELD_WIDTH, content.y, WEIGHT_FIELD_WIDTH, ROW_HEIGHT);
            Rect idRect = new Rect(categoryRect.xMax + FIELD_GAP, content.y, weightRect.xMin - FIELD_GAP - categoryRect.xMax - FIELD_GAP, ROW_HEIGHT);

            SerializedProperty type = entry.FindPropertyRelative("_type");
            SerializedProperty id = entry.FindPropertyRelative("_id");
            string previousType = type.stringValue;
            DrawStringPopup(categoryRect, type, ToArray(DropCatalog.Categories), CATEGORY_LABELS);
            if (type.stringValue != previousType)
            {
                string[] ids = GetRuneIds(type.stringValue);
                id.stringValue = ids.Length > 0 ? ids[0] : "";
                entry.FindPropertyRelative("_rewardGrade").stringValue = "";
                ApplyChanges();
            }
            string[] runeIds = GetRuneIds(type.stringValue);
            DrawStringPopup(idRect, id, runeIds, runeIds);

            SerializedProperty weight = entry.FindPropertyRelative("_weight");
            double nextWeight = EditorGUI.DoubleField(weightRect, weight.doubleValue);
            if (nextWeight != weight.doubleValue)
            {
                weight.doubleValue = nextWeight;
                ApplyChanges();
            }
            if (type.stringValue == DropCatalog.TYPE_ELEMENT)
            {
                Rect gradeRect = new Rect(content.x, content.y + ROW_HEIGHT + FIELD_GAP, content.width, ROW_HEIGHT);
                Rect popupRect = EditorGUI.PrefixLabel(gradeRect, new GUIContent("참여 모드 보상 등급"));
                DrawStringPopup(popupRect, entry.FindPropertyRelative("_rewardGrade"), REWARD_GRADES, REWARD_GRADE_LABELS);
            }
        }

        /// <summary>Rect 영역에 문자열 팝업을 그리고 값을 프로퍼티에 반영한다. 목록에 없는 값은 보존하고 배경을 경고색으로 표시한다.</summary>
        private void DrawStringPopup(Rect rect, SerializedProperty property, string[] values, string[] labels)
        {
            int index = Array.IndexOf(values, property.stringValue);
            bool isUnknown = index < 0;
            string[] display = labels;
            if (isUnknown)
            {
                display = new string[labels.Length + 1];
                Array.Copy(labels, display, labels.Length);
                display[labels.Length] = "(알 수 없는 값) " + property.stringValue;
                index = labels.Length;
            }
            Color previous = GUI.backgroundColor;
            if (isUnknown) GUI.backgroundColor = UNKNOWN_VALUE_BACKGROUND;
            int next = EditorGUI.Popup(rect, index, display);
            GUI.backgroundColor = previous;
            if (next == index || next >= values.Length) return;
            property.stringValue = values[next];
            ApplyChanges();
        }

        /// <summary>목록 끝에 기본 분류 확률(강화 100%)만 있는 새 테이블을 추가하고 선택한다.</summary>
        private void AddTable(SerializedProperty tables)
        {
            tables.arraySize++;
            SerializedProperty table = tables.GetArrayElementAtIndex(tables.arraySize - 1);
            table.FindPropertyRelative("_id").stringValue = MakeUniqueId(tables, NEW_TABLE_ID);
            SerializedProperty chances = table.FindPropertyRelative("_categoryChances");
            chances.ClearArray();
            AddMissingCategoryRows(chances);
            FindRow(chances, DropCatalog.TYPE_MODIFIER, null).FindPropertyRelative("_percent").intValue = DropCatalog.TOTAL_PERCENT;
            table.FindPropertyRelative("_gradeChances").ClearArray();
            table.FindPropertyRelative("_entries").ClearArray();
            ApplyChanges();
            SelectTable(tables.arraySize - 1);
        }

        /// <summary>선택 테이블을 바로 뒤에 복제하고 겹치지 않는 ID를 붙여 선택한다.</summary>
        private void DuplicateTable(SerializedProperty tables)
        {
            string sourceId = tables.GetArrayElementAtIndex(_selectedIndex).FindPropertyRelative("_id").stringValue;
            tables.InsertArrayElementAtIndex(_selectedIndex);
            tables.GetArrayElementAtIndex(_selectedIndex + 1).FindPropertyRelative("_id").stringValue = MakeUniqueId(tables, sourceId + COPY_SUFFIX);
            ApplyChanges();
            SelectTable(_selectedIndex + 1);
        }

        /// <summary>확인 후 선택 테이블을 삭제한다. 참조 중인 테이블은 검증에서 걸러진다.</summary>
        private void DeleteTable(SerializedProperty tables)
        {
            string id = tables.GetArrayElementAtIndex(_selectedIndex).FindPropertyRelative("_id").stringValue;
            if (!EditorUtility.DisplayDialog("드롭 테이블 삭제", id + " 테이블을 삭제할까요? stages.json에서 참조 중이면 저장이 차단됩니다.", "삭제", "취소")) return;
            tables.DeleteArrayElementAtIndex(_selectedIndex);
            ApplyChanges();
            SelectTable(Mathf.Clamp(_selectedIndex, 0, Mathf.Max(0, tables.arraySize - 1)));
        }

        /// <summary>선택 테이블을 바꾸고 후보 리스트 캐시를 다시 만든다.</summary>
        private void SelectTable(int index)
        {
            _selectedIndex = index;
            RebuildEntriesList();
        }

        /// <summary>선택 테이블의 프로퍼티를 반환하고 선택이 범위를 벗어나면 보정한다. 테이블이 없으면 null이다.</summary>
        private SerializedProperty SelectedTable()
        {
            SerializedProperty tables = _serialized.FindProperty("_catalog._dropTables");
            if (tables == null || tables.arraySize == 0) return null;
            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, tables.arraySize - 1);
            return tables.GetArrayElementAtIndex(_selectedIndex);
        }

        /// <summary>선택 테이블의 후보 룬 ReorderableList를 다시 만든다. 새 후보는 확률이 있는 첫 분류의 첫 룬, 가중치 1로 추가한다.</summary>
        private void RebuildEntriesList()
        {
            _entriesList = null;
            if (_serialized == null) return;
            _serialized.Update();
            SerializedProperty table = SelectedTable();
            if (table == null) return;
            SerializedProperty entries = table.FindPropertyRelative("_entries");
            var list = new ReorderableList(_serialized, entries, true, true, true, true);
            list.drawHeaderCallback = rect => GUI.Label(rect, "후보 (분류 / 룬 / 가중치)");
            list.drawElementCallback = (rect, index, active, focused) => DrawEntryRow(rect, index);
            list.elementHeightCallback = index => entries.GetArrayElementAtIndex(index).FindPropertyRelative("_type").stringValue == DropCatalog.TYPE_ELEMENT
                ? (ROW_HEIGHT + FIELD_GAP) * 2 : ROW_HEIGHT + FIELD_GAP;
            list.onChangedCallback = target => ApplyChanges();
            list.onAddCallback = target =>
            {
                SerializedProperty property = target.serializedProperty;
                property.arraySize++;
                SerializedProperty entry = property.GetArrayElementAtIndex(property.arraySize - 1);
                string category = GetDefaultEntryCategory(SelectedTable().FindPropertyRelative("_categoryChances"));
                string[] ids = GetRuneIds(category);
                entry.FindPropertyRelative("_type").stringValue = category;
                entry.FindPropertyRelative("_id").stringValue = ids.Length > 0 ? ids[0] : "";
                entry.FindPropertyRelative("_weight").doubleValue = 1;
                entry.FindPropertyRelative("_rewardGrade").stringValue = "";
                ApplyChanges();
            };
            _entriesList = list;
        }

        /// <summary>모든 테이블에 세 분류 확률 행이 있게 하고, 등급 확률이 있는 분류는 등급 순서의 모든 행을 갖게 한다. Undo 기록과 변경 표시는 남기지 않는다.</summary>
        private void NormalizeTables()
        {
            SerializedProperty tables = _serialized.FindProperty("_catalog._dropTables");
            for (int i = 0; i < tables.arraySize; i++)
            {
                SerializedProperty table = tables.GetArrayElementAtIndex(i);
                AddMissingCategoryRows(table.FindPropertyRelative("_categoryChances"));
                SerializedProperty grades = table.FindPropertyRelative("_gradeChances");
                foreach (string category in DropCatalog.Categories)
                    if (HasCategoryRows(grades, category)) AddMissingGradeRows(grades, category);
            }
            _serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>분류 확률 배열에 없는 분류를 0% 행으로 추가한다.</summary>
        private static void AddMissingCategoryRows(SerializedProperty chances)
        {
            foreach (string category in DropCatalog.Categories)
            {
                if (FindRow(chances, category, null) != null) continue;
                chances.arraySize++;
                SerializedProperty row = chances.GetArrayElementAtIndex(chances.arraySize - 1);
                row.FindPropertyRelative("_category").stringValue = category;
                row.FindPropertyRelative("_percent").intValue = 0;
            }
        }

        /// <summary>등급 확률 배열에 분류의 등급 순서 중 없는 등급을 0% 행으로 추가한다.</summary>
        private void AddMissingGradeRows(SerializedProperty grades, string category)
        {
            foreach (string grade in GetGradeOrder(category))
            {
                if (FindRow(grades, category, grade) != null) continue;
                grades.arraySize++;
                SerializedProperty row = grades.GetArrayElementAtIndex(grades.arraySize - 1);
                row.FindPropertyRelative("_category").stringValue = category;
                row.FindPropertyRelative("_grade").stringValue = grade;
                row.FindPropertyRelative("_percent").intValue = 0;
            }
        }

        /// <summary>등급 확률 배열에서 분류의 행을 모두 지운다.</summary>
        private static void RemoveCategoryRows(SerializedProperty grades, string category)
        {
            for (int i = grades.arraySize - 1; i >= 0; i--)
                if (grades.GetArrayElementAtIndex(i).FindPropertyRelative("_category").stringValue == category) grades.DeleteArrayElementAtIndex(i);
        }

        /// <summary>등급 확률 배열에 분류의 행이 하나라도 있는지 반환한다.</summary>
        private static bool HasCategoryRows(SerializedProperty grades, string category)
        {
            for (int i = 0; i < grades.arraySize; i++)
                if (grades.GetArrayElementAtIndex(i).FindPropertyRelative("_category").stringValue == category) return true;
            return false;
        }

        /// <summary>분류(와 grade가 있으면 등급)가 일치하는 확률 행을 반환하고 없으면 null을 반환한다.</summary>
        private static SerializedProperty FindRow(SerializedProperty rows, string category, string grade)
        {
            for (int i = 0; i < rows.arraySize; i++)
            {
                SerializedProperty row = rows.GetArrayElementAtIndex(i);
                if (row.FindPropertyRelative("_category").stringValue != category) continue;
                if (grade == null || row.FindPropertyRelative("_grade").stringValue == grade) return row;
            }
            return null;
        }

        /// <summary>테이블의 분류 확률 합과 직접 지정한 분류별 등급 확률 합이 모두 100인지 반환한다.</summary>
        private bool AreTotalsValid(SerializedProperty table)
        {
            SerializedProperty chances = table.FindPropertyRelative("_categoryChances");
            int total = 0;
            for (int i = 0; i < chances.arraySize; i++) total += chances.GetArrayElementAtIndex(i).FindPropertyRelative("_percent").intValue;
            if (total != DropCatalog.TOTAL_PERCENT) return false;
            SerializedProperty grades = table.FindPropertyRelative("_gradeChances");
            foreach (string category in DropCatalog.Categories)
            {
                if (!HasCategoryRows(grades, category)) continue;
                int gradeTotal = 0;
                for (int i = 0; i < grades.arraySize; i++)
                {
                    SerializedProperty row = grades.GetArrayElementAtIndex(i);
                    if (row.FindPropertyRelative("_category").stringValue == category) gradeTotal += row.FindPropertyRelative("_percent").intValue;
                }
                if (gradeTotal != DropCatalog.TOTAL_PERCENT) return false;
            }
            return true;
        }

        /// <summary>확률이 0보다 큰 첫 분류를 반환하고 없으면 첫 분류를 반환한다.</summary>
        private static string GetDefaultEntryCategory(SerializedProperty chances)
        {
            foreach (string category in DropCatalog.Categories)
            {
                SerializedProperty row = FindRow(chances, category, null);
                if (row != null && row.FindPropertyRelative("_percent").intValue > 0) return category;
            }
            return DropCatalog.Categories[0];
        }

        /// <summary>분류의 등급 순서를 반환한다. 등급표를 못 읽었으면 합성 규칙의 기본 등급 순서를 쓴다.</summary>
        private IReadOnlyList<string> GetGradeOrder(string category)
        {
            IReadOnlyList<string> order = _runes != null && _modifierGrades != null ? DropCatalog.GetGradeOrder(category, _runes, _modifierGrades) : null;
            return order ?? RuneSynthesisRules.GradeOrder;
        }

        /// <summary>분류 값에 해당하는 표시 이름을 반환한다.</summary>
        private static string GetCategoryLabel(string category)
        {
            for (int i = 0; i < DropCatalog.Categories.Count; i++)
                if (DropCatalog.Categories[i] == category) return CATEGORY_LABELS[i];
            return category;
        }

        /// <summary>분류의 후보로 고를 수 있는 룬 ID 목록을 반환한다.</summary>
        private string[] GetRuneIds(string category)
        {
            return _runeIdsByCategory.TryGetValue(category ?? "", out string[] ids) ? ids : Array.Empty<string>();
        }

        /// <summary>분류별로 비활성이 아닌 룬 ID를 모은다. 속성은 보상으로 줄 수 있는 전투 속성만 모은다.</summary>
        private void CacheRuneIds()
        {
            _runeIdsByCategory.Clear();
            foreach (string category in DropCatalog.Categories)
            {
                var ids = new List<string>();
                foreach (RuneDefinition rune in _runes.All)
                {
                    if (rune.Category != category || rune.IsDisabled || ids.Contains(rune.Id)) continue;
                    if (category == DropCatalog.TYPE_ELEMENT && !RuneRewardRoller.IsCandidateGradeAvailable(_runes, _modifierGrades, rune.Id, "")) continue;
                    ids.Add(rune.Id);
                }
                _runeIdsByCategory[category] = ids.ToArray();
            }
        }

        /// <summary>테이블 배열에서 ID가 같은 테이블 수를 반환한다.</summary>
        private static int CountId(SerializedProperty tables, string id)
        {
            int count = 0;
            for (int i = 0; i < tables.arraySize; i++)
                if (tables.GetArrayElementAtIndex(i).FindPropertyRelative("_id").stringValue == id) count++;
            return count;
        }

        /// <summary>baseId가 비어 있으면 그대로, 겹치면 뒤에 번호를 붙여 겹치지 않는 ID를 반환한다.</summary>
        private static string MakeUniqueId(SerializedProperty tables, string baseId)
        {
            string id = baseId;
            for (int suffix = 2; CountId(tables, id) > 0; suffix++) id = baseId + "." + suffix;
            return id;
        }

        /// <summary>읽기 전용 목록을 배열로 복사한다.</summary>
        private static string[] ToArray(IReadOnlyList<string> values)
        {
            string[] result = new string[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = values[i];
            return result;
        }

        /// <summary>SerializedObject의 변경을 적용하고 변경이 있으면 dirty 상태와 상태 메시지를 갱신한다.</summary>
        private void ApplyChanges()
        {
            if (_serialized == null || !_serialized.ApplyModifiedProperties()) return;
            _isDirty = true;
            SetStatus("변경됨 - 저장 전에 [검증]을 실행하세요.", false);
        }

        /// <summary>상태 메시지를 기록한다.</summary>
        private void SetStatus(string message, bool isError)
        {
            _statusMessage = message;
            _statusIsError = isError;
        }

        /// <summary>드롭 카탈로그를 담아 SerializedObject로 편집하기 위한 에디터 세션 전용 컨테이너다.</summary>
        private sealed class DropCatalogHost : ScriptableObject
        {
            [SerializeField] private DropCatalog _catalog;

            /// <summary>편집 중인 카탈로그다.</summary>
            public DropCatalog Catalog => _catalog;

            /// <summary>편집할 카탈로그 인스턴스를 넣는다.</summary>
            public void SetCatalog(DropCatalog value)
            {
                _catalog = value;
            }
        }
    }
}
