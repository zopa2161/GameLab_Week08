# RuneCode 시스템 문서

- 주의
  - 이 폴더 안의 문서들은 에이전트가 아닌 사람이 읽기 위한 문서입니다. 에이전트는 이 문서를 참고하여 작업하지 말고 코드를 직접 참고하세요.

작성일: 2026-10-21
기준 코드: `develop` 브랜치 (`4fc3d9c UI 하드코딩 제거 작업` 시점)

이 문서 묶음은 RuneCode 프로젝트를 처음 보는 팀원이 **어디에 무엇이 있고, 데이터가 어떻게 흐르며, 어디를 고치면 무엇이 바뀌는지** 파악하도록 쓴 시스템 설명서다. 모든 내용은 `Assets/RuneCode` 아래 코드와 `Resources` 데이터를 직접 읽고 작성했다. 코드에서 확인하지 못한 내용은 **확인 필요**로 표시했다.

## 게임 한 줄 요약

플레이어는 작업실에서 **룬 노드를 연결해 마법 하나를 설계**하고(비주얼 스크립팅), 시험 도크에서 더미에게 시험한 뒤, **스테이지에 출격해 직접 이동·조준하며 전투**한다. 전투에서 얻은 스크랩(재화)과 룬(행동·속성·모드)으로 강화 트리를 올리고 마법을 확장한다.

## 문서 구성


| 순서  | 문서                                                        | 읽으면 알 수 있는 것                                              |
| --- | --------------------------------------------------------- | --------------------------------------------------------- |
| 1   | [01\_Architecture.md](01_Architecture.md)                 | 어셈블리·폴더 구조, 계층과 의존 방향, 부팅 순서, 화면 전환 구조                    |
| 2   | [02\_Data.md](02_Data.md)                                 | 정적 데이터(JSON·SO) 목록과 스키마, 로딩·검증, 세이브 데이터, 데이터 소유권          |
| 3   | [03\_SpellGraph\_Compile.md](03_SpellGraph_Compile.md)    | 마법 그래프 데이터 모델, 편집 세션, 컴파일 파이프라인, 행동·모드 규칙 구조              |
| 4   | [04\_Simulation.md](04_Simulation.md)                     | 60Hz 결정적 시뮬레이션, 틱 순서, 하위 시스템, 마법 실행·전투·적·스폰·보상            |
| 5   | [05\_UI.md](05_UI.md)                                     | UIManager와 MVP 구조, 화면별 View·Presenter, 시뮬레이션 표시 방식, 입력 흐름 |
| 6   | [06\_Flows\_and\_Extension.md](06_Flows_and_Extension.md) | 처음부터 끝까지의 실행 시나리오, 기능 추가 절차, 검증·디버그 도구                    |


처음 읽는다면 01 → 02 → 06(시나리오 부분) 순서로 큰 그림을 잡고, 맡은 영역에 따라 03·04·05를 읽는 것을 권한다.


| 맡은 일           | 우선 읽을 문서                 |
| -------------- | ------------------------ |
| 밸런스·스테이지·적 데이터 | 02, 04의 5\~8절            |
| 새 룬(행동·모드) 추가  | 03, 04의 3\~4절, 06의 확장 절차 |
| UI·화면 작업       | 05, 01의 5절               |
| 세이브·진행·강화 트리   | 02의 4\~5절, 06            |


## 핵심 용어


| 용어                          | 뜻                                                               | 코드 위치                                                     |
| --------------------------- | --------------------------------------------------------------- | --------------------------------------------------------- |
| 룬(Rune)                     | 그래프에 쓰이는 모든 블록의 정의. `runes.json` 한 행이 룬 하나                      | `RuneDefinition`, `RuneCatalog`                           |
| 시작 조건(Core)                 | 그래프의 시작 노드 `core.cast`. 발동 트리거(`onAttack` 등)를 가진다               | `SpellGrammar.CORE_RUNE`                                  |
| 행동(Behavior)                | 실제로 생성되는 마법 개체의 종류. 발사·폭발·잔류·빔·참격 5종                            | `behavior.*` 룬, `SpellBehaviorRules`                      |
| 속성(Element)                 | 행동 노드 슬롯에 장착하는 원소(화염·냉기·전격). 상태 이상을 결정                          | `element.*` 룬, `ElementDefinition`                        |
| 모드(Modifier)                | 행동 노드 슬롯에 장착하는 효과(다중·유도·관통 등). 등급 C/B/A/S                       | `mod.*` 룬, `SpellModifierRules`                           |
| 등급(Grade)                   | 행동·모드의 C/B/A/S. 행동 등급은 슬롯 수, 모드 등급은 효과량을 정한다                    | `runes.json _behaviorGrades`, `elite_modifier_drops.json` |
| 소지량(Stock)                  | 플레이어가 가진 행동·속성·모드 개수. 키는 `룬ID#등급`                               | `PlayerSave`, `SpellGrammar.ModifierStockKey`             |
| SpellGraph                  | 편집 중인 마법의 노드·엣지 데이터(순수 C#)                                      | `Core/Graph/SpellGraph.cs`                                |
| CompiledSpell / SpellAction | 그래프를 컴파일한 실행 트리. 시뮬레이션은 이것만 읽는다                                 | `Core/Graph/CompiledSpell.cs`                             |
| RuneSimulation              | 전투 한 판(미션 또는 시험 도크)의 결정적 시뮬레이션                                  | `Core/Simulation/RuneSimulation.cs`                       |
| 미션(Mission)                 | 스테이지 출격 전투. `MissionRun`이 구동                                    | `Features/Mission/MissionRun.cs`                          |
| 시험 도크(Dock, Bench)          | 작업실에서 더미에게 마법을 시험하는 독립 시뮬레이션                                    | `Features/Workshop/DockRun.cs`                            |
| 스크랩(Fragments, RAM)         | 적 처치로 얻는 재화. 코드에서 `Currency`, `Fragments`, `Scrap`이 같은 재화를 가리킨다 | `PlayerSave._currency`                                    |
| RAM 용량(Capacity)            | 그래프 크기 제한. 연결된 노드의 RAM 합이 용량을 넘으면 출격 불가                         | `RuneCodeSession.Capacity`                                |
| 전력(Energy, mana)            | 전투 중 마법 시전 비용 자원. 코드 내부 자원 이름은 `"mana"`                         | `SimulationPlayer.Energy`                                 |


## 문서와 코드가 다를 때

- 이 문서는 작성 시점 코드 스냅샷 기준이다. 코드가 바뀌면 코드가 우선이다.
- 저장소 루트 `README.md`의 일부 규칙(예: "편성된 적을 모두 처치하면 클리어")은 현재 코드와 다르다. 현재 코드는 일반 스테이지를 **제한시간 생존**으로 클리어한다(04 문서 9절).
- 리팩토링 진행 기록은 `Assets/Docs~/Refactoring/`에 있다. 이 문서 묶음은 리팩토링 이후 현재 구조를 설명한다.