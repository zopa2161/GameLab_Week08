# Stage Table Editor 구현 계획

`stages.json`을 Unity 에디터 윈도우에서 편집하는 도구의 구현 계획이다. 구현은 이 계획의 승인 후 시작한다.

## 1. 목표·범위

- 대상 파일: `Assets/RuneCode/Resources/RuneCode/stages.json` (Resources TextAsset)
- 신규 파일 1개: `Assets/RuneCode/Editor/StageTableEditorWindow.cs`
- 메뉴: `Rune Code/Stage Table Editor` (기존 `Rune Code/Upgrade Tree Editor`와 같은 컨벤션)
- 런타임 코드(`StageData.cs` 등) 수정 없음. 필드가 전부 `[SerializeField] private`이므로 `SerializedProperty` 경로로 편집 가능.
- 범위 밖: `tools/TableEditor`(웹 편집기) 유지·수정, 다른 JSON(enemies 등) 편집기, 테스트 코드.

## 2. 기존 구조 확인 결과 (설계 근거)

- `StageCatalog`와 모든 중첩 클래스가 `[Serializable]` + `[SerializeField] private`로 선언되어 있어 `SerializedObject` 편집이 가능하다.
- `stages.json`의 모든 키가 C# 모델과 100% 일치하고, JSON 키 순서도 C# 필드 선언 순서와 동일하다. `JsonUtility` 라운드트립 시 데이터 손실과 diff 노이즈가 없다.
- 저장 전 검증을 `StageCatalog.FromJson(json, enemies, formations, drops)`으로 그대로 재사용한다. 적 ID 존재, 진형 참조·슬롯 수, 드롭 테이블 참조, 스테이지 번호 중복을 전부 검사하며 오류 메시지가 한국어로 상세하다.
- 외부 참조 ID 목록의 원본:
  - 적: `enemies.json`의 `_enemies[]._id` (15종)
  - 진형: `formations.json`의 `_formations[]._id` (5종)
  - 드롭 테이블: `drops.json`의 `_dropTables[]._id`
  - 룬: `tables/runes.json`의 `_runes[]._id` (39종)
  - Modifier 등급: `tables/elite_modifier_drops.json`의 `_gradeOrder` (C·B·A·S)

## 3. 편집 메커니즘

- 에디터 전용 private 중첩 클래스 `StageCatalogHost : ScriptableObject`가 `[SerializeField] private StageCatalog _catalog` 필드로 카탈로그를 보유한다. 에셋으로 저장하지 않는 세션 전용 컨테이너다.
- 로드: `Resources.Load<TextAsset>("RuneCode/stages")`의 `text`를 `JsonUtility.FromJson<StageCatalog>`으로 역직렬화해 host에 넣고 `new SerializedObject(host)`로 편집을 시작한다.
- 왜 SerializedProperty인가: private 필드 접근이 리플렉션 없이 해결되고, `ApplyModifiedProperties`가 Undo를 자동 등록하며, `ReorderableList`를 그대로 쓸 수 있다.
- 저장 경로는 하드코딩하지 않고 `AssetDatabase.GetAssetPath(textAsset)`으로 얻는다.
- 저장 절차:
  1. `JsonUtility.ToJson(host 카탈로그, true)`로 JSON 생성
  2. 사전 검증: 런타임과 동일하게 enemies·formations·drops를 Resources에서 읽어 `StageCatalog.FromJson` 실행. 실패하면 저장을 차단하고 오류 메시지를 표시한다.
  3. `File.WriteAllText` + `AssetDatabase.ImportAsset(path)`로 TextAsset 재임포트
  4. dirty 해제
- dirty 관리: `ApplyModifiedProperties`가 변경을 반환하면 `_isDirty = true`, 창 제목에 `*` 표시.

## 4. 창 UI 구조

### 4.1 전체 레이아웃 (상하좌우)

```
+--------------------------------------------------------------+
| 툴바: [다시 로드] [검증] [저장]   상태 라벨(검증 결과/오류)      |
+---------------+----------------------------------------------+
| 스테이지 목록   |  루트 공통 섹션 (Foldout 4개 + 보상표 리스트)   |
| (사이드바)     |  ------------------------------------------  |
| 1 스테이지     |  선택된 스테이지 섹션 (Foldout 5개)             |
| 2 스테이지     |                                              |
| ...           |                                              |
| [추가][복제]   |                                              |
| [삭제][▲][▼]  |                                              |
+---------------+----------------------------------------------+
```

- 좌우 분할은 `GUILayout.BeginHorizontal` + `EditorGUILayout.GetControlRect` 기반 고정폭 사이드바(약 260px), 각 영역에 독립 `ScrollView`.
- 목록 행 표시: `1 스테이지` + 종류 태그(`normal` 회색 / `boss` 노란색) + 웨이브 수.
- 목록 연산: 추가(검증을 통과하는 기본값으로 생성), 복제(from/to 번호를 +1해 단일 항목 중복 검증 회피), 삭제, 위/아래 이동. 모두 `serializedProperty`의 `InsertArrayElementAtIndex` / `MoveArrayElement`로 구현한다.
- 사이드바와 상세는 같은 `SerializedObject`의 다른 property를 그리므로 동기화가 자동이다.

### 4.2 필드 표시 규칙 (타입별 공통 규칙)

| 데이터 종류 | 컨트롤 | 비고 |
|---|---|---|
| `int` | `IntField` | |
| `double` | `DoubleField` | |
| 참조 ID(`_enemyId`, `_formation`, `_dropTable`, `_runeId`) | `Popup` (외부 JSON에서 로드한 ID 목록) | 현재 값이 목록에 없으면 목록 맨 앞에 `(알 수 없는 값)` 항목을 추가해 값을 보존하고 노란색으로 경고 |
| 고정 문자열(`_kind`, `_angle`, `_grade`) | `Popup` | 문자열이므로 인덱스-문자열 매핑 헬퍼 사용 |
| 배열 | `ReorderableList` | 추가·삭제·드래그 순서 변경 |
| "의미 있는 0" | 라벨·툴팁으로 명시 | 예: `toStage 0 = 상한 없음`, `repeatInterval 0 = 1회만`, 배율 `0 = 미지정(1 적용)`, `eliteEvery 0 = 엘리트 없음` |

### 4.3 루트 공통 섹션 (상세 상단)

| 섹션 (Foldout) | 필드 | 표시 |
|---|---|---|
| 맵 | `_map` 4개 | `IntField` 2열 배치 (방 가로·세로 칸수, 가로·세로 방 수) |
| 성장 | `_growth` 3개 | `DoubleField`, 툴팁 "0.15 = 스테이지당 +15%" |
| 스폰 | `_spawn` 4개 | 링 슬롯 수 `IntField`, 나머지 `DoubleField` |
| 기타 | `_stationaryAdvanceSpeed` | `DoubleField` "정지 전진 속도" |
| 적 보상표 | `_rewards` | `ReorderableList`, 한 행 = 적 ID 팝업 + 보상 `IntField` |

### 4.4 선택된 스테이지 섹션

| 섹션 (Foldout) | 필드 | 표시 |
|---|---|---|
| 스테이지 | `_fromStage`, `_toStage` | `IntField`. toStage 툴팁 "0 = 상한 없음(무한 모드 순환 대상)" |
| | `_kind` | Popup: `normal` / `boss` |
| | `_timeLimit` | `DoubleField`, kind에 따라 라벨 변경: normal = "생존 시간(초)", boss = "제한 시간(초)" |
| | `_clearReward` | `IntField` |
| 적 유입 (Stream) | `_stream` 스칼라 6개 | 기본 간격·최소 간격·스테이지 가속 계수·스테이지 내 가속은 `DoubleField`, 가속 도달 시간은 `IntField`. 툴팁에 검증 규칙 요약(최소 간격 ≤ 기본 간격, 가속은 1 미만 등) |
| | `_stream._formation` | Popup (formations.json ID) |
| | `_stream._candidates` | `ReorderableList`, 한 행 = 적 ID 팝업 + 첫 등장 스테이지 `IntField` + 가중치 `DoubleField` |
| 웨이브 | `_waves` | `ReorderableList` (요소 높이 가변, 요소 내부에 Foldout). 각 요소: 발생 시각(초)·반복 간격(초, "0 = 1회만") `DoubleField` → 진형 재정의 인라인 박스 → 유닛 리스트 |
| | 진형 재정의 `_waves[]. _formation` | 진형 ID 팝업(필수, formations.json), 각도 Popup(`(원본 유지)`="" / `random` / `ahead`), 간격·주기 `DoubleField`, 슬롯 `IntField` — 각각 "0 = 원본 유지" 툴팁 |
| | 유닛 `_waves[]._units` | 중첩 `ReorderableList`, 한 행 = 적 ID 팝업 + 수량 `IntField`("1 이상") |
| 엘리트 | `_eliteEvery` | `IntField`, 툴팁 "0 = 엘리트 없음. 0이면 타입 목록을 비워도 된다" |
| | `_eliteTypes` | `ReorderableList`, 한 행 = 적 ID 팝업(`boss.*` 선택 시 경고색, 검증에서 거부됨) + 체력·보상·속도·크기 배율 `DoubleField`("0 = 미지정(1 적용)") + 드롭 테이블 Popup(`(없음)`="" + drops.json ID) |
| Modifier 보상 | `_modifierRewards` | `ReorderableList`, 한 행 = 룬 ID 팝업(runes.json) + 등급 Popup(C/B/A/S) + 수량 `IntField`("1 이상") |

### 4.5 툴바와 상태 표시

- `다시 로드`: dirty일 때 확인 다이얼로그 후 디스크에서 재적재.
- `검증`: 저장 없이 현재 상태로 `StageCatalog.FromJson` 실행, 통과 시 녹색 "검증 통과", 실패 시 빨간색 오류 메시지(FormatException 메시지 그대로)를 툴바 아래 한 줄에 표시.
- `저장`: 검증 통과 시에만 파일에 쓴다. 실패하면 저장하지 않고 오류를 표시한다 (강제 저장은 제공하지 않는다).
- 상태 메시지는 창 하단 고정 한 줄에도 표시한다.

## 5. 클래스 구조 (단일 파일)

- 상수: 메뉴 경로, 리소스 경로, `RuneCode/stages` 등 JSON 이름, 종류·각도 옵션 배열.
- 필드 (규칙 3 순서 준수): host, `SerializedObject`, 좌·우 `Vector2` 스크롤, 선택 인덱스, 외부 ID 목록 5종(적·진형·드롭·룬·등급), 상태 메시지·오류, `_isDirty`.
- 메서드: `Open`(MenuItem), 로드·저장·검증, 툴바·사이드바·루트·스테이지 섹션 드로잉, 각 리스트 드로잉, Popup 헬퍼(옵션 목록 + 현재값 보존), 배열 연산 헬퍼.
- 리스트는 그릴 때마다 `SerializedProperty`에서 새로 만들지 않고 선택·구조 변경 시 캐시를 갱신해 할당을 줄인다.
- 규칙 1(비-생명주기 메서드 XML summary), 2·6(필드·네이밍), 5(using 순서)를 준수한다.

## 6. 구현 순서

1. 호스트 ScriptableObject + 로드·저장·검증 뼈대와 툴바, 상태 표시
2. 루트 공통 섹션(맵·성장·스폰·기타·보상표)
3. 사이드바(목록·추가·복제·삭제·이동) + 스테이지 헤더 섹션
4. 스트림 섹션 (스칼라 + 후보 리스트)
5. 웨이브 섹션 (가변 높이 요소 + 중첩 유닛 리스트)
6. 엘리트·Modifier 보상 섹션과 ID Popup 헬퍼, 외부 목록 로딩
7. 컴파일 및 Unity에서 수동 검증

## 7. 검증 계획

코드 작성 후 다음을 Unity 에디터에서 확인한다. 이 문서 작성 시점에는 Unity 실행이 불가하므로 컴파일·동작은 미검증이다.

1. 컴파일 오류 없음, `Rune Code/Stage Table Editor` 메뉴로 창이 열린다.
2. 편집 없이 저장 후 `git diff`로 파일 변화가 사실상 없는지 확인 (키 순서·들여쓰기·숫자 표기).
3. 의도적으로 잘못된 값(가중치 0, 없는 적 ID, 보스 엘리트) 입력 시 저장이 차단되고 오류 메시지가 표시되는지 확인.
4. 스테이지 추가·삭제·이동과 Undo(Ctrl+Z) 동작 확인.
5. 플레이 모드에서 스테이지 데이터가 정상 로드되는지 확인.
6. 스크립트 리컴파일 후 창 상태 복구 확인.

## 8. 제약·주의사항

- 도메인 리로드(스크립트 리컴파일) 시 에디터 세션의 미저장 변경은 유실된다. `OnEnable`에서 host가 없으면 디스크에서 재로드하며 dirty였으면 콘솔에 경고를 남긴다. 세션 백업(EditorPrefs 등)은 범위에서 제외했다.
- 첫 저장 시 파일이 `JsonUtility` 출력으로 다시 쓰이므로 2번 검증에서 포맷 차이를 반드시 확인한다.
- `tools/TableEditor`(웹 편집기)가 같은 파일을 편집하므로 양쪽을 동시에 띄워 두고 수정하면 마지막 저장이 이긴다. 동시 편집을 피한다.
