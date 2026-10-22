using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using UnityEditor;
using UnityEditorInternal;

namespace RuneCode.Editor
{
    /// <summary>stages.json을 목록·폼 형태로 편집하고 저장 전에 런타임 검증을 거치는 창이다.</summary>
    public sealed class StageTableEditorWindow : EditorWindow
    {
        private const string MENU_PATH = "Rune Code/Stage Table Editor";
        private const string STAGES_RESOURCE = "RuneCode/stages";
        private const string ENEMIES_RESOURCE = "RuneCode/enemies";
        private const string FORMATIONS_RESOURCE = "RuneCode/formations";
        private const string DROPS_RESOURCE = "RuneCode/drops";
        private const string RUNES_RESOURCE = "RuneCode/tables/runes";
        private const string GRADES_RESOURCE = "RuneCode/tables/elite_modifier_drops";
        private const string BOSSES_RESOURCE = "RuneCode/bosses";
        private const float SIDEBAR_WIDTH = 260f;
        private const float SIDEBAR_ROW_HEIGHT = 24f;
        private const float ROW_GAP = 3f;
        private const float BOX_PADDING = 6f;
        private const float FIELD_GAP = 6f;
        private const float ELEMENT_LEFT_INSET = 15f;
        private const float ELEMENT_RIGHT_INSET = 22f;
        private const float CELL_LABEL_WIDTH = 26f;

        private static readonly string[] TAB_LABELS = { "진행 편성", "스테이지 정의" };
        private static readonly string[] KIND_VALUES = { "normal", "boss" };
        private static readonly string[] ANGLE_VALUES = { "", "random", "ahead", "fixed" };
        private static readonly string[] ANGLE_LABELS = { "(원본 유지)", "random", "ahead", "fixed" };
        private static readonly string[] FALLBACK_GRADES = { "C", "B", "A", "S" };

        private static readonly Color SELECTION_BACKGROUND = new Color(0.24f, 0.44f, 0.76f, 0.35f);
        private static readonly Color BOSS_TAG_COLOR = new Color(0.95f, 0.72f, 0.15f);
        private static readonly Color UNKNOWN_VALUE_BACKGROUND = new Color(0.95f, 0.75f, 0.2f);
        private static readonly Color SECTION_DIVIDER = new Color(0f, 0f, 0f, 0.15f);
        private static readonly Color ERROR_TEXT_COLOR = new Color(0.95f, 0.45f, 0.4f);

        private static readonly float ROW_HEIGHT = EditorGUIUtility.singleLineHeight;
        private static readonly float FORMATION_BOX_HEIGHT = ROW_HEIGHT * 5 + BOX_PADDING * 2 + 4;
        private static readonly float WAVE_COLLAPSED_HEIGHT = ROW_HEIGHT + ROW_GAP;
        // 펼친 웨이브의 기본 높이. 상단 여백 1 + (시각·간격 행 + 반복 행) + 진형 박스 + 행 간격 + 하단 여백.
        private static readonly float WAVE_EXPANDED_BASE = ROW_HEIGHT * 2 + FORMATION_BOX_HEIGHT + ROW_GAP * 3 + 5;

        private StageCatalogHost _host;
        private BossCatalog _bosses;
        private SerializedObject _serialized;
        private string _savePath = "";

        private string[] _enemyIds = Array.Empty<string>();
        private string[] _formationIds = Array.Empty<string>();
        private string[] _dropTableIds = Array.Empty<string>();
        private string[] _runeIds = Array.Empty<string>();
        private string[] _gradeValues = FALLBACK_GRADES;
        private string[] _dropPopupValues = { "" };
        private string[] _dropPopupLabels = { "(없음)" };

        private ReorderableList _rewardsList;
        private ReorderableList _candidatesList;
        private ReorderableList _wavesList;
        private ReorderableList _eliteTypesList;
        private ReorderableList _modifierRewardsList;
        private readonly List<ReorderableList> _waveUnitLists = new List<ReorderableList>();
        private readonly List<ReorderableList> _progressionUnitLists = new List<ReorderableList>();
        private readonly HashSet<int> _expandedWaves = new HashSet<int>();

        private EditorTab _activeTab;
        private Vector2 _sidebarScroll;
        private Vector2 _detailScroll;
        private int _selectedStageIndex;
        private int _selectedProgressionStageIndex;
        private int _selectedProgressionRoundIndex;

        private bool _isDirty;
        private bool _needsStageListRefresh;
        private string _statusMessage = "";
        private bool _statusIsError;

        private bool _foldMap = true;
        private bool _foldGrowth = true;
        private bool _foldSpawn = true;
        private bool _foldMisc = true;
        private bool _foldRewards = true;
        private bool _foldStage = true;
        private bool _foldStream = false;
        private bool _foldWaves = false;
        private bool _foldElite = true;
        private bool _foldModifierRewards = true;

        /// <summary>창에서 편집할 대상을 구분하는 탭이다.</summary>
        private enum EditorTab
        {
            Progression,
            StageDefinition,
        }

        /// <summary>스테이지 테이블 편집기 창을 연다.</summary>
        [MenuItem(MENU_PATH)]
        public static void Open()
        {
            GetWindow<StageTableEditorWindow>("Stage Table");
        }

        void OnEnable()
        {
            UpdateTitle();
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            LoadExternalOptions();
            LoadFromDisk();
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            if (_isDirty) Debug.LogWarning("[StageTableEditor] 미저장된 stages.json 변경이 폐기됩니다.");
            if (_host != null)
            {
                DestroyImmediate(_host);
                _host = null;
                _serialized = null;
            }
        }

        void OnGUI()
        {
            if (_host == null || _serialized == null)
            {
                DrawLoadError();
                return;
            }
            if (_needsStageListRefresh)
            {
                _needsStageListRefresh = false;
                RebuildStageLists();
            }
            _serialized.Update();

            DrawToolbar();
            if (!string.IsNullOrEmpty(_statusMessage))
                EditorGUILayout.HelpBox(_statusMessage, _statusIsError ? MessageType.Error : MessageType.Info);

            DrawTabBar();

            EditorGUILayout.BeginHorizontal();
            if (_activeTab == EditorTab.Progression) DrawProgressionSidebar();
            else DrawSidebar();
            EditorGUILayout.BeginVertical();
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            DrawRootSections();
            DrawDivider();
            if (_activeTab == EditorTab.Progression) DrawProgressionDetail();
            else DrawStageSections();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            DrawBottomStatus();
            ApplyChanges();
        }

        /// <summary>데이터를 못 불러왔을 때 오류와 다시 로드 버튼을 표시한다.</summary>
        private void DrawLoadError()
        {
            EditorGUILayout.HelpBox(_statusMessage, MessageType.Error);
            if (GUILayout.Button("다시 로드")) LoadFromDisk();
        }

        /// <summary>상단 툴바에 다시 로드·검증·저장 버튼과 저장 경로를 그린다.</summary>
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("다시 로드", GUILayout.Width(80))
                && (!_isDirty || EditorUtility.DisplayDialog("다시 로드", "저장하지 않은 변경이 사라집니다. 디스크에서 다시 불러올까요?", "다시 로드", "취소")))
                LoadFromDisk();
            if (GUILayout.Button("검증", GUILayout.Width(56))) RunValidation();
            if (GUILayout.Button("저장", GUILayout.Width(56))) SaveToDisk();
            GUILayout.FlexibleSpace();
            GUILayout.Label(_savePath, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>상태 메시지 아래에 편집 탭을 그리고 탭이 바뀌면 전환한다.</summary>
        private void DrawTabBar()
        {
            EditorTab next = (EditorTab)GUILayout.Toolbar((int)_activeTab, TAB_LABELS);
            if (next != _activeTab) SwitchTab(next);
        }

        /// <summary>
        /// 편집 탭을 바꾸고 리스트 캐시를 다시 만든 뒤 현재 GUI 이벤트를 끝낸다.
        /// 같은 이벤트에서 다른 탭의 컨트롤을 그리면 Layout 단계와 컨트롤 구성이 어긋나므로 다음 프레임에 새 탭을 그린다.
        /// </summary>
        private void SwitchTab(EditorTab tab)
        {
            _activeTab = tab;
            RebuildStageLists();
            Repaint();
            GUIUtility.ExitGUI();
        }

        /// <summary>좌측 스테이지 목록과 추가·복제·삭제·이동 버튼을 그린다.</summary>
        private void DrawSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(SIDEBAR_WIDTH));
            SerializedProperty stages = StageProperties();
            _sidebarScroll = EditorGUILayout.BeginScrollView(_sidebarScroll);
            for (int i = 0; i < stages.arraySize; i++)
                DrawSidebarRow(stages, i);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("추가", EditorStyles.miniButton)) AddStage();
            using (new EditorGUI.DisabledScope(stages.arraySize == 0))
            {
                if (GUILayout.Button("복제", EditorStyles.miniButton)) DuplicateStage();
                if (GUILayout.Button("삭제", EditorStyles.miniButton)) DeleteSelectedStage();
                if (GUILayout.Button("▲", EditorStyles.miniButton, GUILayout.Width(28))) MoveSelectedStage(-1);
                if (GUILayout.Button("▼", EditorStyles.miniButton, GUILayout.Width(28))) MoveSelectedStage(1);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        /// <summary>목록 한 행에 스테이지 번호·종류·웨이브 수를 표시하고 클릭으로 선택한다.</summary>
        private void DrawSidebarRow(SerializedProperty stages, int index)
        {
            SerializedProperty stage = stages.GetArrayElementAtIndex(index);
            string kind = stage.FindPropertyRelative("_kind").stringValue;
            int waveCount = stage.FindPropertyRelative("_waves").arraySize;

            Rect row = EditorGUILayout.GetControlRect(GUILayout.Height(SIDEBAR_ROW_HEIGHT));
            if (index == _selectedStageIndex) EditorGUI.DrawRect(row, SELECTION_BACKGROUND);
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition) && Event.current.button == 0)
            {
                SelectStage(index);
                Event.current.Use();
            }

            Rect labelRect = new Rect(row.x + 4, row.y, row.width * 0.55f, row.height);
            Rect tagRect = new Rect(labelRect.xMax, row.y, row.width - labelRect.width - 4, row.height);
            GUI.Label(labelRect, " " + StageRangeLabel(stage));
            Color previous = GUI.contentColor;
            if (kind == KIND_VALUES[1]) GUI.contentColor = BOSS_TAG_COLOR;
            GUI.Label(tagRect, (kind == KIND_VALUES[1] ? "보스" : "일반") + " · 웨이브 " + waveCount, EditorStyles.miniLabel);
            GUI.contentColor = previous;
        }

        /// <summary>진행 탭 좌측에 스테이지별 라운드 목록을 그린다. 고정 구조라 추가·삭제 버튼은 없다.</summary>
        private void DrawProgressionSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(SIDEBAR_WIDTH));
            _sidebarScroll = EditorGUILayout.BeginScrollView(_sidebarScroll);
            SerializedProperty stages = _serialized.FindProperty("_catalog._progressionStages");
            if (stages == null || stages.arraySize != StageCatalog.PROGRESSION_STAGE_COUNT)
            {
                EditorGUILayout.HelpBox("연속 미션 스테이지 수가 " + StageCatalog.PROGRESSION_STAGE_COUNT + "개가 아닙니다. JSON에서 배열 크기를 고치세요.", MessageType.Warning);
            }
            else
            {
                for (int stageIndex = 0; stageIndex < stages.arraySize; stageIndex++)
                    DrawProgressionStageRows(stages, stageIndex);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>진행 사이드바에 스테이지 헤더와 라운드 행들을 그린다. 라운드 수가 다르면 경고만 표시한다.</summary>
        private void DrawProgressionStageRows(SerializedProperty stages, int stageIndex)
        {
            SerializedProperty stage = stages.GetArrayElementAtIndex(stageIndex);
            int stageNumber = stage.FindPropertyRelative("_stage").intValue;
            Rect header = EditorGUILayout.GetControlRect(GUILayout.Height(SIDEBAR_ROW_HEIGHT));
            GUI.Label(header, " 스테이지 " + stageNumber, EditorStyles.boldLabel);

            SerializedProperty rounds = stage.FindPropertyRelative("_rounds");
            if (rounds == null || rounds.arraySize != StageCatalog.PROGRESSION_ROUND_COUNT)
            {
                EditorGUILayout.HelpBox("스테이지 " + stageNumber + "의 라운드 수가 " + StageCatalog.PROGRESSION_ROUND_COUNT + "개가 아닙니다.", MessageType.Warning);
                return;
            }
            for (int roundIndex = 0; roundIndex < rounds.arraySize; roundIndex++)
                DrawProgressionRoundRow(rounds, stageIndex, roundIndex);
        }

        /// <summary>라운드 번호·유닛 합계와 3라운드 미니 보스 또는 마지막 라운드 보스 태그를 표시하고 클릭으로 선택한다.</summary>
        private void DrawProgressionRoundRow(SerializedProperty rounds, int stageIndex, int roundIndex)
        {
            SerializedProperty waves = rounds.GetArrayElementAtIndex(roundIndex).FindPropertyRelative("_waves");
            if (waves == null || waves.arraySize != StageCatalog.PROGRESSION_WAVE_COUNT)
            {
                EditorGUILayout.HelpBox("라운드 " + (roundIndex + 1) + "의 웨이브 수가 " + StageCatalog.PROGRESSION_WAVE_COUNT + "개가 아닙니다.", MessageType.Warning);
                return;
            }
            bool isSelected = stageIndex == _selectedProgressionStageIndex && roundIndex == _selectedProgressionRoundIndex;
            bool isBossRound = roundIndex == StageCatalog.PROGRESSION_ROUND_COUNT - 1;
            bool isMiniBossRound = roundIndex + 1 == StageCatalog.MINI_BOSS_ROUND;

            Rect row = EditorGUILayout.GetControlRect(GUILayout.Height(SIDEBAR_ROW_HEIGHT));
            if (isSelected) EditorGUI.DrawRect(row, SELECTION_BACKGROUND);
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition) && Event.current.button == 0)
            {
                SelectProgressionRound(stageIndex, roundIndex);
                Event.current.Use();
            }

            Rect labelRect = new Rect(row.x + 4, row.y, row.width * 0.62f, row.height);
            Rect tagRect = new Rect(labelRect.xMax, row.y, row.width - labelRect.width - 4, row.height);
            GUI.Label(labelRect, "  라운드 " + (roundIndex + 1) + " · 유닛 " + CountRoundUnits(waves));
            if (!isBossRound && !isMiniBossRound) return;
            Color previous = GUI.contentColor;
            GUI.contentColor = BOSS_TAG_COLOR;
            GUI.Label(tagRect, isMiniBossRound ? "미니 보스" : "보스", EditorStyles.miniLabel);
            GUI.contentColor = previous;
        }

        /// <summary>라운드 웨이브들의 유닛 수량 합을 반환한다.</summary>
        private static int CountRoundUnits(SerializedProperty waves)
        {
            int total = 0;
            for (int i = 0; i < waves.arraySize; i++)
            {
                SerializedProperty units = waves.GetArrayElementAtIndex(i).FindPropertyRelative("_units");
                if (units == null) continue;
                for (int unitIndex = 0; unitIndex < units.arraySize; unitIndex++)
                    total += units.GetArrayElementAtIndex(unitIndex).FindPropertyRelative("_count").intValue;
            }
            return total;
        }

        /// <summary>루트 공통 설정인 맵·성장·스폰·기타·적 보상표 섹션을 그린다.</summary>
        private void DrawRootSections()
        {
            SerializedProperty root = _serialized.FindProperty("_catalog");

            _foldMap = EditorGUILayout.Foldout(_foldMap, "맵", true);
            if (_foldMap)
            {
                SerializedProperty map = root.FindPropertyRelative("_map");
                EditorGUILayout.BeginHorizontal();
                DrawIntFieldLayout(map.FindPropertyRelative("_roomColumns"), new GUIContent("방 가로 칸"));
                DrawIntFieldLayout(map.FindPropertyRelative("_roomRows"), new GUIContent("방 세로 칸"));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                DrawIntFieldLayout(map.FindPropertyRelative("_roomsX"), new GUIContent("가로 방 수"));
                DrawIntFieldLayout(map.FindPropertyRelative("_roomsY"), new GUIContent("세로 방 수"));
                EditorGUILayout.EndHorizontal();
            }

            _foldGrowth = EditorGUILayout.Foldout(_foldGrowth, "성장", true);
            if (_foldGrowth)
            {
                SerializedProperty growth = root.FindPropertyRelative("_growth");
                DrawDoubleFieldLayout(growth.FindPropertyRelative("_hpStageScale"), new GUIContent("체력 증가율", "스테이지당 체력 배율 증가. 0.15 = 스테이지당 +15%"));
                DrawDoubleFieldLayout(growth.FindPropertyRelative("_damageStageScale"), new GUIContent("피해 증가율", "스테이지당 피해 배율 증가. 0.1 = 스테이지당 +10%"));
                DrawDoubleFieldLayout(growth.FindPropertyRelative("_clearRewardStageScale"), new GUIContent("클리어 보상 증가율", "스테이지당 클리어 보상 증가. 0.15 = 스테이지당 +15%"));
            }

            _foldSpawn = EditorGUILayout.Foldout(_foldSpawn, "스폰", true);
            if (_foldSpawn)
            {
                SerializedProperty spawn = root.FindPropertyRelative("_spawn");
                DrawIntFieldLayout(spawn.FindPropertyRelative("_ringSlots"), new GUIContent("링 슬롯 수"));
                DrawDoubleFieldLayout(spawn.FindPropertyRelative("_ringMargin"), new GUIContent("링 여백"));
                DrawDoubleFieldLayout(spawn.FindPropertyRelative("_jitterScale"), new GUIContent("지터 배율"));
                DrawDoubleFieldLayout(spawn.FindPropertyRelative("_reinforcementGap"), new GUIContent("지원 간격"));
                EditorGUILayout.BeginHorizontal();
                DrawDoubleFieldLayout(spawn.FindPropertyRelative("_warningSeconds"), new GUIContent("예고 시간(초)", "맵형 스폰의 예고 표시부터 실제 생성까지의 시간"));
                DrawDoubleFieldLayout(spawn.FindPropertyRelative("_spawnGraceSeconds"), new GUIContent("생성 유예(초)", "맵형 스폰으로 생성된 적이 접촉 피해를 주지 않는 생성 직후 시간"));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                DrawDoubleFieldLayout(spawn.FindPropertyRelative("_minPlayerDistance"), new GUIContent("최소 거리", "맵형 스폰 위치가 플레이어와 떨어져야 하는 최소 거리"));
                DrawDoubleFieldLayout(spawn.FindPropertyRelative("_maxPlayerDistance"), new GUIContent("최대 거리", "맵형 스폰 위치가 플레이어와 떨어질 수 있는 최대 거리. 0이면 제한하지 않고 0보다 크면 최소 거리 이상이어야 한다"));
                EditorGUILayout.EndHorizontal();
            }

            _foldMisc = EditorGUILayout.Foldout(_foldMisc, "기타", true);
            if (_foldMisc)
                DrawDoubleFieldLayout(root.FindPropertyRelative("_stationaryAdvanceSpeed"), new GUIContent("정지 전진 속도"));

            _foldRewards = EditorGUILayout.Foldout(_foldRewards, "적 보상표", true);
            if (_foldRewards && _rewardsList != null)
                _rewardsList.DoLayoutList();
        }

        /// <summary>선택한 스테이지의 헤더·스트림·웨이브·엘리트·Modifier 보상 섹션을 그린다.</summary>
        private void DrawStageSections()
        {
            SerializedProperty stage = SelectedStageProperty();
            if (stage == null)
            {
                EditorGUILayout.HelpBox("왼쪽 목록에서 스테이지를 선택하세요.", MessageType.Info);
                return;
            }
            DrawStageHeader(stage);
            DrawStreamSection(stage);
            DrawWavesSection();
            DrawEliteSection(stage);
            DrawModifierRewardSection();
        }

        /// <summary>선택한 스테이지의 번호 범위·종류·시간·클리어 보상을 그린다.</summary>
        private void DrawStageHeader(SerializedProperty stage)
        {
            _foldStage = EditorGUILayout.Foldout(_foldStage, "스테이지 " + StageRangeLabel(stage), true);
            if (!_foldStage) return;
            DrawIntFieldLayout(stage.FindPropertyRelative("_fromStage"), new GUIContent("시작 번호"));
            DrawIntFieldLayout(stage.FindPropertyRelative("_toStage"), new GUIContent("종료 번호", "0 = 상한 없음(무한 모드 순환 대상)"));
            DrawStringPopupLayout(stage.FindPropertyRelative("_kind"), "종류", KIND_VALUES, KIND_VALUES, null);
            bool isBoss = stage.FindPropertyRelative("_kind").stringValue == KIND_VALUES[1];
            GUIContent timeLabel = isBoss
                ? new GUIContent("제한 시간(초) (진행 미션 미사용)", "보스 처치 제한 시간이다. 진행 미션에서는 쓰이지 않지만 검증을 위해 0보다 커야 한다.")
                : new GUIContent("생존 시간(초) (진행 미션 미사용)", "버텨야 하는 시간이다. 진행 미션에서는 쓰이지 않지만 검증을 위해 0보다 커야 한다.");
            DrawDoubleFieldLayout(stage.FindPropertyRelative("_timeLimit"), timeLabel);
            DrawIntFieldLayout(stage.FindPropertyRelative("_clearReward"), new GUIContent("클리어 보상"));
        }

        /// <summary>선택한 스테이지의 적 유입 간격·진형·후보 목록을 그린다.</summary>
        private void DrawStreamSection(SerializedProperty stage)
        {
            _foldStream = EditorGUILayout.Foldout(_foldStream, "적 유입 (Stream) - 진행 미션 미사용 · 검증용", true);
            if (!_foldStream) return;
            SerializedProperty stream = stage.FindPropertyRelative("_stream");
            if (stream == null)
            {
                EditorGUILayout.HelpBox("스트림 설정이 없습니다. 이 상태로는 저장 전 검증을 통과하지 못합니다.", MessageType.Warning);
                return;
            }
            DrawDoubleFieldLayout(stream.FindPropertyRelative("_baseInterval"), new GUIContent("기본 간격(초)"));
            DrawDoubleFieldLayout(stream.FindPropertyRelative("_minInterval"), new GUIContent("최소 간격(초)", "기본 간격 이하"));
            DrawDoubleFieldLayout(stream.FindPropertyRelative("_stageScale"), new GUIContent("스테이지 가속 계수", "0 이상. 스테이지가 오를수록 간격이 이 계수만큼 줄어든다."));
            DrawDoubleFieldLayout(stream.FindPropertyRelative("_intraRamp"), new GUIContent("스테이지 내 가속", "0 이상 1 미만. 스테이지 경과 시간에 따라 간격이 최대 이 비율만큼 줄어든다."));
            DrawDoubleFieldLayout(stream.FindPropertyRelative("_rampSeconds"), new GUIContent("가속 도달 시간(초)"));
            DrawIntFieldLayout(stream.FindPropertyRelative("_groupSize"), new GUIContent("묶음 크기", "한 번의 유입 간격마다 같은 틱에 묶어서 내는 적 수. 0은 1기로 본다"));
            DrawStringPopupLayout(stream.FindPropertyRelative("_formation"), "진형", _formationIds, _formationIds, null);
            if (_candidatesList != null)
                _candidatesList.DoLayoutList();
        }

        /// <summary>선택한 스테이지의 웨이브 목록을 그린다.</summary>
        private void DrawWavesSection()
        {
            _foldWaves = EditorGUILayout.Foldout(_foldWaves, "웨이브 - 진행 미션 미사용 · 검증용", true);
            if (_foldWaves && _wavesList != null)
                _wavesList.DoLayoutList();
        }

        /// <summary>선택한 스테이지의 엘리트 주기와 순회 타입 목록을 그린다.</summary>
        private void DrawEliteSection(SerializedProperty stage)
        {
            _foldElite = EditorGUILayout.Foldout(_foldElite, "엘리트", true);
            if (!_foldElite) return;
            EditorGUILayout.HelpBox("시간제 스테이지의 스트림 엘리트 설정이다. 연속 미션 엘리트는 진행 편성 탭의 엘리트 타입과 웨이브 유닛 토글로 설정한다.", MessageType.Info);
            DrawIntFieldLayout(stage.FindPropertyRelative("_eliteEvery"), new GUIContent("엘리트 주기", "기본 스폰 순번이 이 배수일 때 엘리트를 스폰한다. 0 = 엘리트 없음"));
            if (_eliteTypesList != null)
                _eliteTypesList.DoLayoutList();
        }

        /// <summary>선택한 스테이지의 Modifier 보상 목록을 그린다.</summary>
        private void DrawModifierRewardSection()
        {
            _foldModifierRewards = EditorGUILayout.Foldout(_foldModifierRewards, "Modifier 보상", true);
            if (_foldModifierRewards && _modifierRewardsList != null)
                _modifierRewardsList.DoLayoutList();
        }

        /// <summary>진행 탭 상세 영역에 선택 라운드의 제목, 스테이지 정의 요약과 세 웨이브를 그린다.</summary>
        private void DrawProgressionDetail()
        {
            SerializedProperty stage = SelectedProgressionStageProperty();
            if (stage == null)
            {
                EditorGUILayout.HelpBox("연속 미션 스테이지를 찾을 수 없습니다. JSON에서 _progressionStages 배열을 확인하세요.", MessageType.Warning);
                return;
            }
            int stageNumber = stage.FindPropertyRelative("_stage").intValue;
            SerializedProperty rounds = stage.FindPropertyRelative("_rounds");
            if (rounds == null || rounds.arraySize == 0)
            {
                EditorGUILayout.HelpBox("스테이지 " + stageNumber + "의 라운드를 찾을 수 없습니다.", MessageType.Warning);
                return;
            }
            _selectedProgressionRoundIndex = Mathf.Clamp(_selectedProgressionRoundIndex, 0, rounds.arraySize - 1);
            SerializedProperty round = rounds.GetArrayElementAtIndex(_selectedProgressionRoundIndex);

            GUILayout.Label("스테이지 " + stageNumber + " · 라운드 " + (_selectedProgressionRoundIndex + 1), EditorStyles.boldLabel);
            DrawProgressionDefinitionSummary(stageNumber);
            DrawProgressionEliteSection();
            EditorGUILayout.PropertyField(round.FindPropertyRelative("_reward"), new GUIContent("라운드 선택 보상"), true);

            SerializedProperty waves = round.FindPropertyRelative("_waves");
            if (waves == null || waves.arraySize != StageCatalog.PROGRESSION_WAVE_COUNT)
            {
                EditorGUILayout.HelpBox("라운드의 웨이브 수가 " + StageCatalog.PROGRESSION_WAVE_COUNT + "개가 아닙니다. JSON에서 배열 크기를 고치세요.", MessageType.Warning);
                return;
            }
            DrawProgressionWaves(waves);
        }

        /// <summary>진행 스테이지 번호에 대응하는 정의의 보상 요약과 스테이지 정의 열기 버튼을 그린다.</summary>
        private void DrawProgressionDefinitionSummary(int stageNumber)
        {
            int definitionIndex = FindStageDefinitionIndex(stageNumber);
            if (definitionIndex < 0)
            {
                EditorGUILayout.HelpBox("스테이지 " + stageNumber + "에 대응하는 스테이지 정의 항목이 없습니다.", MessageType.Warning);
                return;
            }
            SerializedProperty definition = StageProperties().GetArrayElementAtIndex(definitionIndex);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("클리어 보상 " + definition.FindPropertyRelative("_clearReward").intValue
                + " · Modifier 보상 " + definition.FindPropertyRelative("_modifierRewards").arraySize + "개", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("스테이지 정의 열기", EditorStyles.miniButton, GUILayout.Width(110)))
            {
                _selectedStageIndex = definitionIndex;
                SwitchTab(EditorTab.StageDefinition);
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>선택한 진행 스테이지의 엘리트 타입 목록을 그린다. 웨이브 엘리트 유닛이 이 정의로 생성된다.</summary>
        private void DrawProgressionEliteSection()
        {
            _foldElite = EditorGUILayout.Foldout(_foldElite, "엘리트 타입", true);
            if (!_foldElite) return;
            if (_eliteTypesList == null)
            {
                EditorGUILayout.HelpBox("엘리트 타입 목록 캐시가 준비되지 않았습니다. [다시 로드]로 목록을 갱신하세요.", MessageType.Warning);
                return;
            }
            _eliteTypesList.DoLayoutList();
        }

        /// <summary>선택 라운드의 웨이브들을 항상 펼친 형태로 그리고 보스 배치 안내를 표시한다.</summary>
        private void DrawProgressionWaves(SerializedProperty waves)
        {
            bool isBossRound = _selectedProgressionRoundIndex == StageCatalog.PROGRESSION_ROUND_COUNT - 1;
            bool isMiniBossRound = _selectedProgressionRoundIndex + 1 == StageCatalog.MINI_BOSS_ROUND;
            int bossWaveIndex = StageCatalog.PROGRESSION_WAVE_COUNT - 1;
            for (int waveIndex = 0; waveIndex < waves.arraySize; waveIndex++)
            {
                int bossCount = CountBossUnits(waves.GetArrayElementAtIndex(waveIndex), false);
                int miniBossCount = CountBossUnits(waves.GetArrayElementAtIndex(waveIndex), true);
                if (isBossRound && waveIndex == bossWaveIndex)
                {
                    EditorGUILayout.HelpBox("이 웨이브에 최종 보스 1기가 필요합니다.", MessageType.Info);
                    if (bossCount != 1)
                        EditorGUILayout.HelpBox("마지막 웨이브의 보스 수가 1기가 아닙니다.", MessageType.Warning);
                }
                else if (bossCount > 0)
                {
                    EditorGUILayout.HelpBox("보스는 마지막 라운드의 마지막 웨이브에만 둘 수 있습니다.", MessageType.Warning);
                }
                if (isMiniBossRound && waveIndex == bossWaveIndex)
                {
                    EditorGUILayout.HelpBox("이 웨이브에 미니 보스(레골라스) 1기가 필요합니다. 동반 적까지 모두 처치해야 완료됩니다.", MessageType.Info);
                    if (miniBossCount != 1) EditorGUILayout.HelpBox("미니 보스 수가 1기가 아닙니다.", MessageType.Warning);
                }
                else if (miniBossCount > 0)
                    EditorGUILayout.HelpBox("미니 보스는 3라운드의 마지막 웨이브에만 둘 수 있습니다.", MessageType.Warning);
                DrawProgressionWave(waves, waveIndex);
            }
        }

        /// <summary>진행 웨이브 하나를 진형 재정의 박스와 유닛 리스트로 그린다. 시각·반복 간격은 진행 미션에서 쓰지 않는다.</summary>
        private void DrawProgressionWave(SerializedProperty waves, int waveIndex)
        {
            SerializedProperty wave = waves.GetArrayElementAtIndex(waveIndex);
            Rect headerRect = EditorGUILayout.GetControlRect();
            GUI.Label(headerRect, "웨이브 " + (waveIndex + 1), EditorStyles.boldLabel);

            Rect formationRect = EditorGUILayout.GetControlRect(false, FORMATION_BOX_HEIGHT);
            DrawFormationOverrideBox(formationRect, wave.FindPropertyRelative("_formation"));

            if (waveIndex >= _progressionUnitLists.Count)
            {
                EditorGUILayout.HelpBox("웨이브 유닛 목록 캐시가 준비되지 않았습니다. [다시 로드]로 목록을 갱신하세요.", MessageType.Warning);
                return;
            }
            _progressionUnitLists[waveIndex].DoLayoutList();
            EditorGUILayout.Space(4);
        }

        /// <summary>보스 데이터의 미니 보스 구분과 일치하는 웨이브 유닛 수량 합을 반환한다.</summary>
        private int CountBossUnits(SerializedProperty wave, bool isMiniBoss)
        {
            SerializedProperty units = wave.FindPropertyRelative("_units");
            if (units == null) return 0;
            int total = 0;
            for (int i = 0; i < units.arraySize; i++)
            {
                SerializedProperty unit = units.GetArrayElementAtIndex(i);
                string id = unit.FindPropertyRelative("_enemyId").stringValue;
                if (_bosses != null && _bosses.TryGet(id, out BossDefinition boss) && boss.IsMiniBoss == isMiniBoss)
                    total += unit.FindPropertyRelative("_count").intValue;
            }
            return total;
        }

        /// <summary>스테이지 번호에 대응하는 정의 항목의 인덱스를 반환한다. 단일 항목을 먼저 보고 없으면 범위 포함 항목을 찾는다. 없으면 -1.</summary>
        private int FindStageDefinitionIndex(int stageNumber)
        {
            SerializedProperty stages = StageProperties();
            for (int i = 0; i < stages.arraySize; i++)
            {
                SerializedProperty stage = stages.GetArrayElementAtIndex(i);
                if (stage.FindPropertyRelative("_fromStage").intValue == stageNumber
                    && stage.FindPropertyRelative("_toStage").intValue == stageNumber)
                    return i;
            }
            for (int i = 0; i < stages.arraySize; i++)
            {
                SerializedProperty stage = stages.GetArrayElementAtIndex(i);
                int from = stage.FindPropertyRelative("_fromStage").intValue;
                int to = stage.FindPropertyRelative("_toStage").intValue;
                if (stageNumber >= from && (to == 0 || stageNumber <= to))
                    return i;
            }
            return -1;
        }

        /// <summary>창 하단에 현재 상태 메시지를 한 줄로 표시한다.</summary>
        private void DrawBottomStatus()
        {
            Color previous = GUI.contentColor;
            if (_statusIsError) GUI.contentColor = ERROR_TEXT_COLOR;
            GUILayout.Label(_statusMessage, EditorStyles.miniLabel);
            GUI.contentColor = previous;
        }

        /// <summary>루트 섹션과 스테이지 섹션 사이에 구분선을 그린다.</summary>
        private void DrawDivider()
        {
            Rect rect = EditorGUILayout.GetControlRect(GUILayout.Height(1));
            EditorGUI.DrawRect(rect, SECTION_DIVIDER);
            EditorGUILayout.Space(4);
        }

        /// <summary>보상표 한 행에 적 ID 팝업과 보상 코인 수를 그린다.</summary>
        private void DrawRewardRow(Rect rect, int index)
        {
            SerializedProperty element = _rewardsList.serializedProperty.GetArrayElementAtIndex(index);
            Rect content = ElementContent(rect);
            float y = rect.y + 1;
            float rewardWidth = Mathf.Min(84f, content.width * 0.35f);
            Rect rewardRect = new Rect(content.xMax - rewardWidth, y, rewardWidth, ROW_HEIGHT);
            Rect idRect = new Rect(content.x, y, rewardRect.xMin - FIELD_GAP - content.x, ROW_HEIGHT);
            DrawStringPopup(idRect, element.FindPropertyRelative("_enemyId"), GUIContent.none, _enemyIds, _enemyIds);
            DrawIntField(rewardRect, element.FindPropertyRelative("_reward"), GUIContent.none);
        }

        /// <summary>유입 후보 한 행에 적 ID·첫 등장 스테이지·가중치를 그린다.</summary>
        private void DrawCandidateRow(Rect rect, int index)
        {
            SerializedProperty element = _candidatesList.serializedProperty.GetArrayElementAtIndex(index);
            Rect content = ElementContent(rect);
            float y = rect.y + 1;
            float popupWidth = Mathf.Clamp(content.width - 230f, 80f, content.width * 0.55f);
            float firstWidth = Mathf.Min(90f, content.width * 0.25f);
            float weightWidth = content.width - popupWidth - firstWidth - FIELD_GAP * 2;
            Rect idRect = new Rect(content.x, y, popupWidth, ROW_HEIGHT);
            Rect firstRect = new Rect(idRect.xMax + FIELD_GAP, y, firstWidth, ROW_HEIGHT);
            Rect weightRect = new Rect(firstRect.xMax + FIELD_GAP, y, weightWidth, ROW_HEIGHT);
            DrawStringPopup(idRect, element.FindPropertyRelative("_enemyId"), GUIContent.none, _enemyIds, _enemyIds);
            DrawIntField(firstRect, element.FindPropertyRelative("_firstStage"), GUIContent.none);
            DrawDoubleField(weightRect, element.FindPropertyRelative("_weight"), GUIContent.none);
        }

        /// <summary>웨이브 요소에 시각·반복 간격과 진형 재정의 박스, 유닛 리스트를 그린다. 그립·삭제 버튼 영역은 비운다.</summary>
        private void DrawWaveElement(Rect rect, int index)
        {
            if (index >= _waveUnitLists.Count) return;
            SerializedProperty wave = _wavesList.serializedProperty.GetArrayElementAtIndex(index);
            SerializedProperty at = wave.FindPropertyRelative("_at");
            SerializedProperty units = wave.FindPropertyRelative("_units");
            bool expanded = _expandedWaves.Contains(index);

            Rect content = ElementContent(rect);
            float y = rect.y + 1;
            string summary = "  t=" + at.doubleValue.ToString("0.#") + "초 · 유닛 " + units.arraySize;
            expanded = EditorGUI.Foldout(new Rect(content.x, y, content.width, ROW_HEIGHT), expanded, "웨이브 " + (index + 1) + summary, true);
            if (expanded != _expandedWaves.Contains(index)) SetExpandedWave(index, expanded);
            y += ROW_HEIGHT + ROW_GAP;
            if (!expanded) return;

            float half = (content.width - FIELD_GAP) * 0.5f;
            DrawDoubleField(new Rect(content.x, y, half, ROW_HEIGHT), at, new GUIContent("발생 시각(초)"));
            DrawDoubleField(new Rect(content.x + half + FIELD_GAP, y, half, ROW_HEIGHT), wave.FindPropertyRelative("_repeatInterval"), new GUIContent("반복 간격(초)", "0 = 1회만 실행"));
            y += ROW_HEIGHT + ROW_GAP;

            DrawFormationOverrideBox(new Rect(content.x, y, content.width, FORMATION_BOX_HEIGHT), wave.FindPropertyRelative("_formation"));
            y += FORMATION_BOX_HEIGHT + ROW_GAP;

            ReorderableList unitList = _waveUnitLists[index];
            unitList.DoList(new Rect(content.x, y, content.width, unitList.GetHeight()));
        }

        /// <summary>Rect 영역에 진형 재정의 박스(진형·각도·간격·주기·슬롯 수)를 그린다. 기존 웨이브 요소와 진행 편성 탭이 함께 쓴다.</summary>
        private void DrawFormationOverrideBox(Rect rect, SerializedProperty formation)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            float innerX = rect.x + BOX_PADDING;
            float innerWidth = rect.width - BOX_PADDING * 2;
            float innerY = rect.y + BOX_PADDING + 2;
            GUI.Label(new Rect(innerX, innerY, innerWidth, ROW_HEIGHT), "진형 재정의", EditorStyles.boldLabel);
            innerY += ROW_HEIGHT;
            DrawStringPopup(new Rect(innerX, innerY, innerWidth, ROW_HEIGHT), formation.FindPropertyRelative("_id"), new GUIContent("진형"), _formationIds, _formationIds);
            innerY += ROW_HEIGHT;
            DrawStringPopup(new Rect(innerX, innerY, innerWidth, ROW_HEIGHT), formation.FindPropertyRelative("_angle"), new GUIContent("각도"), ANGLE_VALUES, ANGLE_LABELS);
            innerY += ROW_HEIGHT;
            float pairWidth = (innerWidth - FIELD_GAP) * 0.5f;
            DrawDoubleField(new Rect(innerX, innerY, pairWidth, ROW_HEIGHT), formation.FindPropertyRelative("_spacing"), new GUIContent("간격", "0 = 원본 유지"));
            DrawDoubleField(new Rect(innerX + pairWidth + FIELD_GAP, innerY, pairWidth, ROW_HEIGHT), formation.FindPropertyRelative("_interval"), new GUIContent("주기", "0 = 원본 유지"));
            innerY += ROW_HEIGHT;
            DrawIntField(new Rect(innerX, innerY, innerWidth, ROW_HEIGHT), formation.FindPropertyRelative("_slots"), new GUIContent("슬롯 수", "0 = 원본 유지. 반원 진형은 배수 제한이 있다."));
        }

        /// <summary>유닛 한 행에 적 ID 팝업과 수량을 그린다.</summary>
        private void DrawWaveUnitRow(Rect rect, int index, ReorderableList list)
        {
            SerializedProperty element = list.serializedProperty.GetArrayElementAtIndex(index);
            Rect content = ElementContent(rect);
            float y = rect.y + 1;
            float countWidth = Mathf.Min(84f, content.width * 0.35f);
            Rect countRect = new Rect(content.xMax - countWidth, y, countWidth, ROW_HEIGHT);
            Rect idRect = new Rect(content.x, y, countRect.xMin - FIELD_GAP - content.x, ROW_HEIGHT);
            DrawStringPopup(idRect, element.FindPropertyRelative("_enemyId"), GUIContent.none, _enemyIds, _enemyIds);
            DrawIntField(countRect, element.FindPropertyRelative("_count"), GUIContent.none);
        }

        /// <summary>진행 웨이브 유닛 한 행에 적 ID 팝업, 수량과 엘리트 토글을 그린다.</summary>
        private void DrawProgressionUnitRow(Rect rect, int index, ReorderableList list)
        {
            SerializedProperty element = list.serializedProperty.GetArrayElementAtIndex(index);
            Rect content = ElementContent(rect);
            float y = rect.y + 1;
            float toggleWidth = 56f;
            float countWidth = Mathf.Min(84f, content.width * 0.35f);
            Rect toggleRect = new Rect(content.xMax - toggleWidth, y, toggleWidth, ROW_HEIGHT);
            Rect countRect = new Rect(toggleRect.xMin - FIELD_GAP - countWidth, y, countWidth, ROW_HEIGHT);
            Rect idRect = new Rect(content.x, y, countRect.xMin - FIELD_GAP - content.x, ROW_HEIGHT);
            DrawStringPopup(idRect, element.FindPropertyRelative("_enemyId"), GUIContent.none, _enemyIds, _enemyIds);
            DrawIntField(countRect, element.FindPropertyRelative("_count"), GUIContent.none);
            DrawBoolField(toggleRect, element.FindPropertyRelative("_isElite"), new GUIContent("엘리트"));
        }

        /// <summary>Rect 영역에 체크박스를 왼쪽에 둔 bool 토글을 그리고 값이 바뀌면 프로퍼티에 반영한다.</summary>
        private void DrawBoolField(Rect rect, SerializedProperty property, GUIContent label)
        {
            // 좁은 칸에서는 라벨 폭 때문에 체크박스가 밀려나므로 체크박스를 왼쪽에 둔다.
            bool next = EditorGUI.ToggleLeft(rect, label, property.boolValue);
            if (next == property.boolValue) return;
            property.boolValue = next;
            ApplyChanges();
        }

        /// <summary>엘리트 타입 한 행에 적·드롭 테이블 팝업, 드롭 확률과 네 배율 입력칸을 그린다.</summary>
        private void DrawEliteRow(Rect rect, int index, ReorderableList list)
        {
            SerializedProperty element = list.serializedProperty.GetArrayElementAtIndex(index);
            Rect content = ElementContent(rect);
            float y = rect.y + 1;
            float chanceWidth = Mathf.Min(96f, content.width * 0.25f);
            float popupWidth = (content.width - chanceWidth - FIELD_GAP * 2) * 0.5f;
            DrawStringPopup(new Rect(content.x, y, popupWidth, ROW_HEIGHT), element.FindPropertyRelative("_enemyId"), GUIContent.none, _enemyIds, _enemyIds);
            DrawStringPopup(new Rect(content.x + popupWidth + FIELD_GAP, y, popupWidth, ROW_HEIGHT), element.FindPropertyRelative("_dropTable"), GUIContent.none, _dropPopupValues, _dropPopupLabels);
            DrawMultiplierCell(new Rect(content.xMax - chanceWidth, y, chanceWidth, ROW_HEIGHT), "드롭%", element.FindPropertyRelative("_dropChancePercent"));
            y += ROW_HEIGHT + ROW_GAP;

            float cellWidth = (content.width - FIELD_GAP * 3) * 0.25f;
            DrawMultiplierCell(new Rect(content.x, y, cellWidth, ROW_HEIGHT), "HP", element.FindPropertyRelative("_hpMultiplier"));
            DrawMultiplierCell(new Rect(content.x + cellWidth + FIELD_GAP, y, cellWidth, ROW_HEIGHT), "보상", element.FindPropertyRelative("_rewardMultiplier"));
            DrawMultiplierCell(new Rect(content.x + (cellWidth + FIELD_GAP) * 2, y, cellWidth, ROW_HEIGHT), "속도", element.FindPropertyRelative("_speedMultiplier"));
            DrawMultiplierCell(new Rect(content.x + (cellWidth + FIELD_GAP) * 3, y, cellWidth, ROW_HEIGHT), "크기", element.FindPropertyRelative("_radiusMultiplier"));
        }

        /// <summary>배율 입력칸 하나에 라벨과 필드를 그린다. 0은 미지정(1 적용)이다.</summary>
        private void DrawMultiplierCell(Rect rect, string label, SerializedProperty property)
        {
            Rect labelRect = new Rect(rect.x, rect.y + 1, CELL_LABEL_WIDTH - 4, ROW_HEIGHT);
            Rect fieldRect = new Rect(rect.x + CELL_LABEL_WIDTH, rect.y + 1, Mathf.Max(12f, rect.width - CELL_LABEL_WIDTH), ROW_HEIGHT);
            GUI.Label(labelRect, label, EditorStyles.miniLabel);
            DrawDoubleField(fieldRect, property, GUIContent.none);
        }

        /// <summary>Modifier 보상 한 행에 룬·등급 팝업과 수량을 그린다.</summary>
        private void DrawModifierRewardRow(Rect rect, int index)
        {
            SerializedProperty element = _modifierRewardsList.serializedProperty.GetArrayElementAtIndex(index);
            Rect content = ElementContent(rect);
            float y = rect.y + 1;
            float countWidth = Mathf.Min(84f, content.width * 0.3f);
            float gradeWidth = Mathf.Min(60f, content.width * 0.24f);
            Rect countRect = new Rect(content.xMax - countWidth, y, countWidth, ROW_HEIGHT);
            Rect gradeRect = new Rect(countRect.xMin - FIELD_GAP - gradeWidth, y, gradeWidth, ROW_HEIGHT);
            Rect runeRect = new Rect(content.x, y, gradeRect.xMin - FIELD_GAP - content.x, ROW_HEIGHT);
            DrawStringPopup(runeRect, element.FindPropertyRelative("_runeId"), GUIContent.none, _runeIds, _runeIds);
            DrawStringPopup(gradeRect, element.FindPropertyRelative("_grade"), GUIContent.none, _gradeValues, _gradeValues);
            DrawIntField(countRect, element.FindPropertyRelative("_count"), GUIContent.none);
        }

        /// <summary>ReorderableList 요소 rect에서 왼쪽 드래그 그립과 오른쪽 삭제 버튼 영역을 제외한 내용 영역을 반환한다.</summary>
        private static Rect ElementContent(Rect elementRect)
        {
            return new Rect(elementRect.x + ELEMENT_LEFT_INSET, elementRect.y, elementRect.width - ELEMENT_LEFT_INSET - ELEMENT_RIGHT_INSET, elementRect.height);
        }

        /// <summary>Rect 영역에 문자열 팝업을 그리고 값을 프로퍼티에 반영한다. 목록에 없는 값은 보존하고 배경을 경고색으로 표시한다.</summary>
        private void DrawStringPopup(Rect rect, SerializedProperty property, GUIContent label, string[] values, string[] labels)
        {
            string current = property.stringValue;
            int index = Array.IndexOf(values, current);
            bool isUnknown = index < 0;
            string[] display = isUnknown ? AppendUnknown(labels, current) : labels;
            if (isUnknown) index = display.Length - 1;

            Color previous = GUI.backgroundColor;
            if (isUnknown) GUI.backgroundColor = UNKNOWN_VALUE_BACKGROUND;
            // GUIContent 라벨 + string 옵션 조합 오버로드가 없어 라벨 유무에 따라 오버로드를 나눠 쓴다.
            int next = string.IsNullOrEmpty(label.text)
                ? EditorGUI.Popup(rect, index, display)
                : EditorGUI.Popup(rect, label.text, index, display);
            GUI.backgroundColor = previous;
            if (next == index || next >= values.Length) return;
            property.stringValue = values[next];
            ApplyChanges();
        }

        /// <summary>레이아웃 기반 문자열 팝업을 그리고 값을 프로퍼티에 반영한다. 목록에 없는 값은 보존하고 배경을 경고색으로 표시한다.</summary>
        private void DrawStringPopupLayout(SerializedProperty property, string label, string[] values, string[] labels, string tooltip)
        {
            string current = property.stringValue;
            int index = Array.IndexOf(values, current);
            bool isUnknown = index < 0;
            string[] display = isUnknown ? AppendUnknown(labels, current) : labels;
            if (isUnknown) index = display.Length - 1;

            Color previous = GUI.backgroundColor;
            if (isUnknown) GUI.backgroundColor = UNKNOWN_VALUE_BACKGROUND;
            int next = EditorGUILayout.Popup(new GUIContent(label, tooltip), index, display);
            GUI.backgroundColor = previous;
            if (next == index || next >= values.Length) return;
            property.stringValue = values[next];
            ApplyChanges();
        }

        /// <summary>옵션 목록 끝에 현재 값(목록에 없는 값)을 보존하는 항목을 붙인다.</summary>
        private static string[] AppendUnknown(string[] labels, string current)
        {
            string[] extended = new string[labels.Length + 1];
            Array.Copy(labels, extended, labels.Length);
            extended[labels.Length] = "(알 수 없는 값) " + current;
            return extended;
        }

        /// <summary>Rect 영역에 int 필드를 그리고 값이 바뀌면 프로퍼티에 반영한다.</summary>
        private void DrawIntField(Rect rect, SerializedProperty property, GUIContent label)
        {
            int next = EditorGUI.IntField(rect, label, property.intValue);
            if (next == property.intValue) return;
            property.intValue = next;
            ApplyChanges();
        }

        /// <summary>Rect 영역에 double 필드를 그리고 값이 바뀌면 프로퍼티에 반영한다.</summary>
        private void DrawDoubleField(Rect rect, SerializedProperty property, GUIContent label)
        {
            double next = EditorGUI.DoubleField(rect, label, property.doubleValue);
            if (next == property.doubleValue) return;
            property.doubleValue = next;
            ApplyChanges();
        }

        /// <summary>레이아웃 한 줄에 int 필드를 그리고 값이 바뀌면 프로퍼티에 반영한다. 데이터 클래스의 Header 속성이 함께 그려지지 않게 한다.</summary>
        private void DrawIntFieldLayout(SerializedProperty property, GUIContent label)
        {
            DrawIntField(EditorGUILayout.GetControlRect(), property, label);
        }

        /// <summary>레이아웃 한 줄에 double 필드를 그리고 값이 바뀌면 프로퍼티에 반영한다. 데이터 클래스의 Header 속성이 함께 그려지지 않게 한다.</summary>
        private void DrawDoubleFieldLayout(SerializedProperty property, GUIContent label)
        {
            DrawDoubleField(EditorGUILayout.GetControlRect(), property, label);
        }

        /// <summary>SerializedObject의 변경을 적용하고 변경이 있으면 dirty 상태와 상태 메시지를 갱신한다.</summary>
        private bool ApplyChanges()
        {
            if (_serialized == null) return false;
            bool changed = _serialized.ApplyModifiedProperties();
            if (changed)
            {
                _isDirty = true;
                UpdateTitle();
                SetStatus("변경됨 - 저장 전에 [검증]을 실행하세요.", false);
            }
            return changed;
        }

        /// <summary>하단 상태줄에 표시할 메시지를 기록한다.</summary>
        private void SetStatus(string message, bool isError)
        {
            _statusMessage = message;
            _statusIsError = isError;
        }

        /// <summary>dirty 상태를 창 제목에 반영한다.</summary>
        private void UpdateTitle()
        {
            titleContent = new GUIContent(_isDirty ? "Stage Table*" : "Stage Table");
        }

        /// <summary>웨이브 요소의 펼침 상태를 기록한다.</summary>
        private void SetExpandedWave(int index, bool expanded)
        {
            if (expanded) _expandedWaves.Add(index);
            else _expandedWaves.Remove(index);
        }

        /// <summary>Undo·Redo 후 리스트 캐시 재구성을 예약한다.</summary>
        private void OnUndoRedoPerformed()
        {
            _needsStageListRefresh = true;
        }

        /// <summary>Resources의 stages.json을 역직렬화해 편집 대상으로 삼고 선택·리스트 캐시를 초기화한다.</summary>
        private void LoadFromDisk()
        {
            if (_host != null) DestroyImmediate(_host);
            _host = null;
            _serialized = null;
            _isDirty = false;
            _expandedWaves.Clear();
            UpdateTitle();
            try
            {
                TextAsset asset = Resources.Load<TextAsset>(STAGES_RESOURCE);
                if (asset == null) throw new FormatException("누락된 스테이지 데이터: " + STAGES_RESOURCE);
                StageCatalog catalog = JsonUtility.FromJson<StageCatalog>(asset.text);
                if (catalog == null || catalog.Stages == null || catalog.Stages.Count == 0)
                    throw new FormatException("스테이지 정의가 없습니다: " + STAGES_RESOURCE);
                string path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path)) throw new FormatException("저장 경로를 찾지 못했습니다: " + STAGES_RESOURCE);

                _host = ScriptableObject.CreateInstance<StageCatalogHost>();
                _host.hideFlags = HideFlags.HideAndDontSave;
                _host.SetCatalog(catalog);
                _serialized = new SerializedObject(_host);
                _savePath = path;
                SetStatus("불러오기 완료 - " + path, false);
            }
            catch (Exception exception)
            {
                SetStatus("불러오기 실패 - " + exception.Message, true);
                return;
            }
            _selectedStageIndex = FindDefaultStageIndex();
            _selectedProgressionStageIndex = 0;
            _selectedProgressionRoundIndex = 0;
            RebuildStageLists();
        }

        /// <summary>팝업 후보로 쓸 적·진형·드롭·룬 ID와 Modifier 등급을 Resources JSON에서 읽어 캐시한다.</summary>
        private void LoadExternalOptions()
        {
            try
            {
                EnemyCatalog enemies = EnemyCatalog.FromJson(LoadResourceText(ENEMIES_RESOURCE));
                FormationCatalog formations = FormationCatalog.FromJson(LoadResourceText(FORMATIONS_RESOURCE));
                _bosses = BossCatalog.FromJson(LoadResourceText(BOSSES_RESOURCE), enemies, formations);
            }
            catch (Exception exception)
            {
                _bosses = null;
                Debug.LogError("[StageTableEditor] 보스 목록 로드 실패: " + exception.Message);
            }
            _enemyIds = LoadIdEntries(ENEMIES_RESOURCE, json => JsonUtility.FromJson<EnemyIdList>(json)?.Entries);
            _formationIds = LoadIdEntries(FORMATIONS_RESOURCE, json => JsonUtility.FromJson<FormationIdList>(json)?.Entries);
            _dropTableIds = LoadIdEntries(DROPS_RESOURCE, json => JsonUtility.FromJson<DropIdList>(json)?.Entries);
            _runeIds = LoadIdEntries(RUNES_RESOURCE, json => JsonUtility.FromJson<RuneIdList>(json)?.Entries);
            try
            {
                string[] grades = JsonUtility.FromJson<GradeOrderList>(LoadResourceText(GRADES_RESOURCE))?.Values;
                _gradeValues = grades != null && grades.Length > 0 ? grades : FALLBACK_GRADES;
            }
            catch (Exception)
            {
                _gradeValues = FALLBACK_GRADES;
            }

            _dropPopupValues = new string[_dropTableIds.Length + 1];
            _dropPopupLabels = new string[_dropTableIds.Length + 1];
            _dropPopupValues[0] = "";
            _dropPopupLabels[0] = "(없음)";
            for (int i = 0; i < _dropTableIds.Length; i++)
            {
                _dropPopupValues[i + 1] = _dropTableIds[i];
                _dropPopupLabels[i + 1] = _dropTableIds[i];
            }
        }

        /// <summary>리소스 JSON에서 숨김 표시가 없는 ID 항목만 추출해 문자열 배열로 반환하고 실패하면 빈 배열을 반환한다.</summary>
        private static string[] LoadIdEntries(string resource, Func<string, IdEntry[]> parse)
        {
            try
            {
                IdEntry[] entries = parse(LoadResourceText(resource));
                if (entries == null) return Array.Empty<string>();
                var ids = new List<string>(entries.Length);
                foreach (IdEntry entry in entries) if (!entry.IsHidden) ids.Add(entry.Id);
                return ids.ToArray();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Resources의 JSON 텍스트를 읽고 없으면 예외를 던진다. 런타임 로더와 같은 경로를 쓴다.</summary>
        private static string LoadResourceText(string resource)
        {
            TextAsset asset = Resources.Load<TextAsset>(resource);
            if (asset == null) throw new FormatException("누락된 데이터: " + resource);
            return asset.text;
        }

        /// <summary>JSON 텍스트를 런타임과 동일한 StageCatalog.FromJson으로 검증하고 실패 시 예외를 던진다.</summary>
        private static void ValidateStageJson(string json)
        {
            EnemyCatalog enemies = EnemyCatalog.FromJson(LoadResourceText(ENEMIES_RESOURCE));
            DropCatalog drops = DropCatalog.FromJson(LoadResourceText(DROPS_RESOURCE));
            FormationCatalog formations = FormationCatalog.FromJson(LoadResourceText(FORMATIONS_RESOURCE));
            RuneCatalog runes = RuneCatalog.FromTable(JsonUtility.FromJson<RuneTableData>(LoadResourceText(RUNES_RESOURCE)));
            ModifierGradeTable grades = ModifierGradeTable.FromJson(LoadResourceText(GRADES_RESOURCE), runes);
            BossCatalog bosses = BossCatalog.FromJson(LoadResourceText(BOSSES_RESOURCE), enemies, formations);
            StageCatalog.FromJson(json, enemies, formations, drops, grades, runes, bosses);
        }

        /// <summary>저장 없이 현재 편집 상태를 런타임 검증으로 확인하고 결과를 상태줄에 표시한다.</summary>
        private void RunValidation()
        {
            _serialized.ApplyModifiedProperties();
            string json = JsonUtility.ToJson(_host.Catalog, true);
            try
            {
                ValidateStageJson(json);
                SetStatus("검증 통과 - 저장할 수 있습니다.", false);
            }
            catch (Exception exception)
            {
                SetStatus("검증 실패 - " + exception.Message, true);
            }
        }

        /// <summary>편집 상태를 JSON으로 내보내 런타임 검증을 통과하면 stages.json에 쓰고 TextAsset을 재임포트한다.</summary>
        private void SaveToDisk()
        {
            _serialized.ApplyModifiedProperties();
            string json = JsonUtility.ToJson(_host.Catalog, true);
            try
            {
                ValidateStageJson(json);
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
            UpdateTitle();
            SetStatus("저장 완료 - " + _savePath, false);
        }

        /// <summary>카탈로그의 스테이지 배열 프로퍼티를 반환한다.</summary>
        private SerializedProperty StageProperties()
        {
            return _serialized.FindProperty("_catalog._stages");
        }

        /// <summary>사이드바에서 선택한 스테이지의 프로퍼티를 반환하고 선택이 범위를 벗어나면 보정한다.</summary>
        private SerializedProperty SelectedStageProperty()
        {
            SerializedProperty stages = StageProperties();
            if (stages.arraySize == 0) return null;
            _selectedStageIndex = Mathf.Clamp(_selectedStageIndex, 0, stages.arraySize - 1);
            return stages.GetArrayElementAtIndex(_selectedStageIndex);
        }

        /// <summary>1스테이지를 담는 항목의 목록 상 인덱스를 찾고 없으면 0을 반환한다.</summary>
        private int FindDefaultStageIndex()
        {
            SerializedProperty stages = StageProperties();
            for (int i = 0; i < stages.arraySize; i++)
                if (stages.GetArrayElementAtIndex(i).FindPropertyRelative("_fromStage").intValue == 1) return i;
            return 0;
        }

        /// <summary>스테이지 항목의 번호 범위 라벨을 반환한다.</summary>
        private static string StageRangeLabel(SerializedProperty stage)
        {
            int from = stage.FindPropertyRelative("_fromStage").intValue;
            int to = stage.FindPropertyRelative("_toStage").intValue;
            return to == from ? from + " 스테이지" : from + "~" + to + " 스테이지";
        }

        /// <summary>사이드바 선택을 바꾸고 스테이지 전용 리스트 캐시를 다시 만든다.</summary>
        private void SelectStage(int index)
        {
            if (_selectedStageIndex == index) return;
            _selectedStageIndex = index;
            RebuildStageLists();
        }

        /// <summary>선택한 진행 스테이지의 프로퍼티를 반환하고 선택이 범위를 벗어나면 보정한다.</summary>
        private SerializedProperty SelectedProgressionStageProperty()
        {
            SerializedProperty stages = _serialized.FindProperty("_catalog._progressionStages");
            if (stages == null || stages.arraySize == 0) return null;
            _selectedProgressionStageIndex = Mathf.Clamp(_selectedProgressionStageIndex, 0, stages.arraySize - 1);
            return stages.GetArrayElementAtIndex(_selectedProgressionStageIndex);
        }

        /// <summary>진행 편성의 선택 라운드를 바꾸고 리스트 캐시를 다시 만든다.</summary>
        private void SelectProgressionRound(int stageIndex, int roundIndex)
        {
            if (_selectedProgressionStageIndex == stageIndex && _selectedProgressionRoundIndex == roundIndex) return;
            _selectedProgressionStageIndex = stageIndex;
            _selectedProgressionRoundIndex = roundIndex;
            RebuildStageLists();
        }

        /// <summary>현재 탭에 맞는 스테이지 전용 리스트 캐시를 다시 만든다. 보상표는 탭과 관계없이 재구성한다.</summary>
        private void RebuildStageLists()
        {
            if (_serialized == null) return;
            _serialized.Update();
            _rewardsList = BuildRewardsList(_serialized.FindProperty("_catalog._rewards"));
            if (_activeTab == EditorTab.Progression)
            {
                RebuildProgressionLists();
                return;
            }
            _progressionUnitLists.Clear();
            SerializedProperty stage = SelectedStageProperty();
            if (stage == null)
            {
                _candidatesList = null;
                _wavesList = null;
                _eliteTypesList = null;
                _modifierRewardsList = null;
                _waveUnitLists.Clear();
                return;
            }
            _candidatesList = BuildCandidatesList(stage.FindPropertyRelative("_stream._candidates"));
            _eliteTypesList = BuildEliteTypesList(stage.FindPropertyRelative("_eliteTypes"));
            _modifierRewardsList = BuildModifierRewardsList(stage.FindPropertyRelative("_modifierRewards"));
            RebuildWaveLists(stage.FindPropertyRelative("_waves"));
        }

        /// <summary>웨이브 목록과 유닛 하위 리스트 캐시를 선택 스테이지 기준으로 다시 만든다.</summary>
        private void RebuildWaveLists(SerializedProperty waves)
        {
            _wavesList = BuildWavesList(waves);
            _waveUnitLists.Clear();
            for (int i = 0; i < waves.arraySize; i++)
                _waveUnitLists.Add(BuildWaveUnitList(waves.GetArrayElementAtIndex(i).FindPropertyRelative("_units")));
        }

        /// <summary>진행 탭에서 정의 탭 전용 리스트 캐시를 비우고 선택 스테이지의 엘리트 타입과 선택 라운드의 웨이브 유닛 리스트를 다시 만든다.</summary>
        private void RebuildProgressionLists()
        {
            _candidatesList = null;
            _wavesList = null;
            _modifierRewardsList = null;
            _waveUnitLists.Clear();
            _progressionUnitLists.Clear();

            SerializedProperty stage = SelectedProgressionStageProperty();
            if (stage == null)
            {
                _eliteTypesList = null;
                return;
            }
            _eliteTypesList = BuildEliteTypesList(stage.FindPropertyRelative("_eliteTypes"));
            SerializedProperty rounds = stage.FindPropertyRelative("_rounds");
            if (rounds == null || rounds.arraySize == 0) return;
            _selectedProgressionRoundIndex = Mathf.Clamp(_selectedProgressionRoundIndex, 0, rounds.arraySize - 1);
            SerializedProperty waves = rounds.GetArrayElementAtIndex(_selectedProgressionRoundIndex).FindPropertyRelative("_waves");
            if (waves == null || waves.arraySize != StageCatalog.PROGRESSION_WAVE_COUNT) return;
            for (int i = 0; i < waves.arraySize; i++)
                _progressionUnitLists.Add(BuildProgressionUnitList(waves.GetArrayElementAtIndex(i).FindPropertyRelative("_units")));
        }

        /// <summary>적 보상표 리스트를 만든다.</summary>
        private ReorderableList BuildRewardsList(SerializedProperty property)
        {
            ReorderableList list = NewList(property, "적 보상표 (적 / 보상 코인)");
            list.drawElementCallback = (rect, index, active, focused) => DrawRewardRow(rect, index);
            list.onAddCallback = target => AppendElement(target, element =>
            {
                element.FindPropertyRelative("_enemyId").stringValue = _enemyIds.Length > 0 ? _enemyIds[0] : "";
                element.FindPropertyRelative("_reward").intValue = 1;
            });
            return list;
        }

        /// <summary>유입 후보 리스트를 만든다.</summary>
        private ReorderableList BuildCandidatesList(SerializedProperty property)
        {
            ReorderableList list = NewList(property, "유입 후보 (적 / 첫 등장 / 가중치)");
            list.drawElementCallback = (rect, index, active, focused) => DrawCandidateRow(rect, index);
            list.onAddCallback = target => AppendElement(target, element =>
            {
                element.FindPropertyRelative("_enemyId").stringValue = _enemyIds.Length > 0 ? _enemyIds[0] : "";
                element.FindPropertyRelative("_firstStage").intValue = 1;
                element.FindPropertyRelative("_weight").doubleValue = 1;
            });
            return list;
        }

        /// <summary>웨이브 목록을 만든다. 요소 높이는 펼침 상태와 유닛 수에 따라 달라진다.</summary>
        private ReorderableList BuildWavesList(SerializedProperty property)
        {
            ReorderableList list = NewList(property, "웨이브");
            list.drawElementCallback = (rect, index, active, focused) => DrawWaveElement(rect, index);
            list.elementHeightCallback = index => WaveElementHeight(index);
            list.onAddCallback = target => AppendElement(target, InitializeNewWave);
            return list;
        }

        /// <summary>웨이브 유닛 리스트를 만든다.</summary>
        private ReorderableList BuildWaveUnitList(SerializedProperty property)
        {
            ReorderableList list = NewList(property, "유닛 (적 / 수량)");
            list.drawElementCallback = (rect, index, active, focused) => DrawWaveUnitRow(rect, index, list);
            list.onAddCallback = target => AppendElement(target, element =>
            {
                element.FindPropertyRelative("_enemyId").stringValue = _enemyIds.Length > 0 ? _enemyIds[0] : "";
                element.FindPropertyRelative("_count").intValue = 1;
            });
            return list;
        }

        /// <summary>진행 웨이브 유닛 리스트를 만든다. 유닛 행에 엘리트 토글을 포함한다.</summary>
        private ReorderableList BuildProgressionUnitList(SerializedProperty property)
        {
            ReorderableList list = NewList(property, "유닛 (적 / 수량 / 엘리트)");
            list.drawElementCallback = (rect, index, active, focused) => DrawProgressionUnitRow(rect, index, list);
            list.onAddCallback = target => AppendElement(target, element =>
            {
                element.FindPropertyRelative("_enemyId").stringValue = _enemyIds.Length > 0 ? _enemyIds[0] : "";
                element.FindPropertyRelative("_count").intValue = 1;
                element.FindPropertyRelative("_isElite").boolValue = false;
            });
            return list;
        }

        /// <summary>엘리트 타입 리스트를 만든다.</summary>
        private ReorderableList BuildEliteTypesList(SerializedProperty property)
        {
            ReorderableList list = NewList(property, "엘리트 타입 (적 / 드롭 테이블 + 배율)");
            list.elementHeight = ROW_HEIGHT * 2 + ROW_GAP + 4;
            list.drawElementCallback = (rect, index, active, focused) => DrawEliteRow(rect, index, list);
            list.onAddCallback = target => AppendElement(target, element =>
            {
                element.FindPropertyRelative("_enemyId").stringValue = _enemyIds.Length > 0 ? _enemyIds[0] : "";
                element.FindPropertyRelative("_hpMultiplier").doubleValue = 0;
                element.FindPropertyRelative("_rewardMultiplier").doubleValue = 0;
                element.FindPropertyRelative("_speedMultiplier").doubleValue = 0;
                element.FindPropertyRelative("_radiusMultiplier").doubleValue = 0;
                element.FindPropertyRelative("_dropTable").stringValue = "";
                element.FindPropertyRelative("_dropChancePercent").doubleValue = 0;
            });
            return list;
        }

        /// <summary>Modifier 보상 리스트를 만든다.</summary>
        private ReorderableList BuildModifierRewardsList(SerializedProperty property)
        {
            ReorderableList list = NewList(property, "Modifier 보상 (룬 / 등급 / 수량)");
            list.drawElementCallback = (rect, index, active, focused) => DrawModifierRewardRow(rect, index);
            list.onAddCallback = target => AppendElement(target, element =>
            {
                element.FindPropertyRelative("_runeId").stringValue = _runeIds.Length > 0 ? _runeIds[0] : "";
                element.FindPropertyRelative("_grade").stringValue = _gradeValues[0];
                element.FindPropertyRelative("_count").intValue = 1;
            });
            return list;
        }

        /// <summary>공통 설정(헤더 라벨, 구조 변경 시 캐시 재구성 예약)을 갖춘 리스트를 만든다.</summary>
        private ReorderableList NewList(SerializedProperty property, string header)
        {
            ReorderableList list = new ReorderableList(property.serializedObject, property, true, true, true, true);
            list.drawHeaderCallback = rect => GUI.Label(rect, header);
            list.onChangedCallback = target => _needsStageListRefresh = true;
            return list;
        }

        /// <summary>리스트 끝에 요소를 추가하고 기본값 초기화 후 변경을 적용한다.</summary>
        private void AppendElement(ReorderableList list, Action<SerializedProperty> initialize)
        {
            SerializedProperty property = list.serializedProperty;
            property.arraySize++;
            initialize?.Invoke(property.GetArrayElementAtIndex(property.arraySize - 1));
            ApplyChanges();
            _needsStageListRefresh = true;
        }

        /// <summary>웨이브 요소의 높이를 펼침 상태와 유닛 수에 따라 반환한다.</summary>
        private float WaveElementHeight(int index)
        {
            if (!_expandedWaves.Contains(index) || index >= _waveUnitLists.Count) return WAVE_COLLAPSED_HEIGHT;
            return WAVE_EXPANDED_BASE + _waveUnitLists[index].GetHeight();
        }

        /// <summary>검증을 통과하는 기본값으로 새 스테이지 항목을 목록 끝에 추가하고 선택한다.</summary>
        private void AddStage()
        {
            SerializedProperty stages = StageProperties();
            int nextNumber = stages.arraySize == 0
                ? 1
                : stages.GetArrayElementAtIndex(stages.arraySize - 1).FindPropertyRelative("_fromStage").intValue + 1;
            stages.arraySize++;
            InitializeNewStage(stages.GetArrayElementAtIndex(stages.arraySize - 1), nextNumber);
            ApplyChanges();
            _needsStageListRefresh = true;
            _selectedStageIndex = stages.arraySize - 1;
            RebuildStageLists();
        }

        /// <summary>새 스테이지 항목에 스트림 후보 1개를 포함한 최소 구성을 채운다.</summary>
        private void InitializeNewStage(SerializedProperty stage, int number)
        {
            stage.FindPropertyRelative("_fromStage").intValue = number;
            stage.FindPropertyRelative("_toStage").intValue = number;
            stage.FindPropertyRelative("_kind").stringValue = KIND_VALUES[0];
            stage.FindPropertyRelative("_timeLimit").doubleValue = 30;
            stage.FindPropertyRelative("_clearReward").intValue = 20;
            stage.FindPropertyRelative("_eliteEvery").intValue = 0;
            stage.FindPropertyRelative("_waves").arraySize = 0;
            stage.FindPropertyRelative("_eliteTypes").arraySize = 0;
            stage.FindPropertyRelative("_modifierRewards").arraySize = 0;

            SerializedProperty stream = stage.FindPropertyRelative("_stream");
            stream.FindPropertyRelative("_baseInterval").doubleValue = 1.6;
            stream.FindPropertyRelative("_minInterval").doubleValue = 0.35;
            stream.FindPropertyRelative("_stageScale").doubleValue = 0.08;
            stream.FindPropertyRelative("_intraRamp").doubleValue = 0.5;
            stream.FindPropertyRelative("_rampSeconds").doubleValue = 60;
            stream.FindPropertyRelative("_groupSize").intValue = 1;
            stream.FindPropertyRelative("_formation").stringValue = _formationIds.Length > 0 ? _formationIds[0] : "";
            SerializedProperty candidates = stream.FindPropertyRelative("_candidates");
            candidates.arraySize = 1;
            SerializedProperty candidate = candidates.GetArrayElementAtIndex(0);
            candidate.FindPropertyRelative("_enemyId").stringValue = _enemyIds.Length > 0 ? _enemyIds[0] : "";
            candidate.FindPropertyRelative("_firstStage").intValue = number;
            candidate.FindPropertyRelative("_weight").doubleValue = 1;
        }

        /// <summary>선택한 스테이지를 복제하고 번호를 1 늘려 단일 항목 중복 검증을 피하게 한다.</summary>
        private void DuplicateStage()
        {
            SerializedProperty stages = StageProperties();
            if (stages.arraySize == 0) return;
            _selectedStageIndex = Mathf.Clamp(_selectedStageIndex, 0, stages.arraySize - 1);
            stages.InsertArrayElementAtIndex(_selectedStageIndex);
            SerializedProperty copy = stages.GetArrayElementAtIndex(_selectedStageIndex);
            copy.FindPropertyRelative("_fromStage").intValue += 1;
            SerializedProperty toStage = copy.FindPropertyRelative("_toStage");
            if (toStage.intValue != 0) toStage.intValue += 1;
            ApplyChanges();
            _needsStageListRefresh = true;
            RebuildStageLists();
        }

        /// <summary>선택한 스테이지 항목을 확인 후 삭제한다.</summary>
        private void DeleteSelectedStage()
        {
            SerializedProperty stages = StageProperties();
            if (stages.arraySize == 0) return;
            string label = StageRangeLabel(stages.GetArrayElementAtIndex(_selectedStageIndex));
            if (!EditorUtility.DisplayDialog("스테이지 삭제", label + " 항목을 삭제할까요?", "삭제", "취소")) return;
            stages.DeleteArrayElementAtIndex(_selectedStageIndex);
            ApplyChanges();
            _needsStageListRefresh = true;
            _selectedStageIndex = Mathf.Clamp(_selectedStageIndex - 1, 0, Mathf.Max(0, stages.arraySize - 1));
            RebuildStageLists();
        }

        /// <summary>선택한 스테이지를 목록에서 위(-1)·아래(+1)로 이동한다.</summary>
        private void MoveSelectedStage(int delta)
        {
            SerializedProperty stages = StageProperties();
            int target = _selectedStageIndex + delta;
            if (stages.arraySize == 0 || target < 0 || target >= stages.arraySize) return;
            stages.MoveArrayElement(_selectedStageIndex, target);
            _selectedStageIndex = target;
            ApplyChanges();
            _needsStageListRefresh = true;
            RebuildStageLists();
        }

        /// <summary>새 웨이브에 기본 시각과 진형 재정의, 첫 유닛을 채운다.</summary>
        private void InitializeNewWave(SerializedProperty wave)
        {
            wave.FindPropertyRelative("_at").doubleValue = 5;
            wave.FindPropertyRelative("_repeatInterval").doubleValue = 0;
            SerializedProperty formation = wave.FindPropertyRelative("_formation");
            formation.FindPropertyRelative("_id").stringValue = _formationIds.Length > 0 ? _formationIds[0] : "";
            formation.FindPropertyRelative("_angle").stringValue = "";
            formation.FindPropertyRelative("_spacing").doubleValue = 0;
            formation.FindPropertyRelative("_interval").doubleValue = 0;
            formation.FindPropertyRelative("_slots").intValue = 0;
            SerializedProperty units = wave.FindPropertyRelative("_units");
            units.arraySize = 1;
            SerializedProperty unit = units.GetArrayElementAtIndex(0);
            unit.FindPropertyRelative("_enemyId").stringValue = _enemyIds.Length > 0 ? _enemyIds[0] : "";
            unit.FindPropertyRelative("_count").intValue = 1;
        }

        /// <summary>카탈로그를 담아 SerializedObject로 편집하기 위한 에디터 세션 전용 컨테이너다.</summary>
        private sealed class StageCatalogHost : ScriptableObject
        {
            [SerializeField] private StageCatalog _catalog;

            /// <summary>편집 중인 카탈로그다.</summary>
            public StageCatalog Catalog => _catalog;

            /// <summary>편집할 카탈로그 인스턴스를 넣는다.</summary>
            public void SetCatalog(StageCatalog value)
            {
                _catalog = value;
            }
        }

        /// <summary>외부 카탈로그 JSON에서 ID만 읽기 위한 항목이다.</summary>
        [Serializable]
        private sealed class IdEntry
        {
            [SerializeField] private string _id;
            [SerializeField] private bool _isHidden;
            public string Id => _id;
            public bool IsHidden => _isHidden;
        }

        /// <summary>enemies.json의 적 ID 목록을 읽기 위한 형식이다.</summary>
        [Serializable]
        private sealed class EnemyIdList
        {
            [SerializeField] private IdEntry[] _enemies;
            public IdEntry[] Entries => _enemies;
        }

        /// <summary>formations.json의 진형 ID 목록을 읽기 위한 형식이다.</summary>
        [Serializable]
        private sealed class FormationIdList
        {
            [SerializeField] private IdEntry[] _formations;
            public IdEntry[] Entries => _formations;
        }

        /// <summary>drops.json의 드롭 테이블 ID 목록을 읽기 위한 형식이다.</summary>
        [Serializable]
        private sealed class DropIdList
        {
            [SerializeField] private IdEntry[] _dropTables;
            public IdEntry[] Entries => _dropTables;
        }

        /// <summary>tables/runes.json의 룬 ID 목록을 읽기 위한 형식이다.</summary>
        [Serializable]
        private sealed class RuneIdList
        {
            [SerializeField] private IdEntry[] _runes;
            public IdEntry[] Entries => _runes;
        }

        /// <summary>tables/elite_modifier_drops.json의 등급 순서를 읽기 위한 형식이다.</summary>
        [Serializable]
        private sealed class GradeOrderList
        {
            [SerializeField] private string[] _gradeOrder;
            public string[] Values => _gradeOrder;
        }
    }
}
