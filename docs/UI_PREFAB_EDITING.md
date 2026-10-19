# UI 프리팹 편집 안내

화면 UI의 기본 색상과 이미지는 프리팹의 Image, TMP 컴포넌트에서 편집한다. 런타임 코드가 룬 분류별 색상을 덮어쓰지 않으며, 버튼 상호작용 색상은 Unity Button의 Color Tint가 처리한다. 기존 이미지 리소스는 보존했다.

## 적용된 스프라이트

색상이 포함된 새 Corner 이미지 경로는 `Assets/RuneCode/Art/UI/`이다. 모두 128×128이며 Image Type은 Sliced, Fill Center는 활성화했다. 기존 흰색 이미지는 `Assets/RuneCode/Art/`에 보존했다.

| 대상 | Source Image | 9-slice Border |
| --- | --- | --- |
| 룬 팔레트·빠른 검색 행 | Rune_FourCorners_분류_128 / Unavailable / Default | 네 방향 16px |
| 강화 트리 노드 | Locked / Available의 Vivid 이미지 + LevelFill 채움 | 네 방향 16px |
| TabButton의 Normal / Selected | Button_TopCorners_Navy_128 | 네 방향 16px |
| 일반 버튼·목록 조작 버튼 | 기존 프리팹 설정 유지 | 기존 설정 유지 |
| UiPanel 및 화면의 패널·입력창 배경 | Panel_White_Square_128 | 네 방향 8px |

새 Corner 이미지는 배경색과 회색 테두리를 이미지 자체에 포함한다. 해당 Image Color의 RGB는 흰색으로 두며 투명 클릭 영역의 Alpha는 0을 유지한다. 탭의 Normal / Selected는 원본과 같은 `#1A2130` 남색 이미지를 공유하고, 현재 탭은 기존 SelectionMarker로 구분한다. 일반 버튼·패널·HUD·화면 배치는 이번 Corner 교체에서 변경하지 않았다. 파일별 색상과 생성 프롬프트는 [UI_BAKED_CORNER_SPRITES.md](UI_BAKED_CORNER_SPRITES.md)를 참고한다.

## 편집 대상

별도 요소 경로: `Assets/RuneCode/Resources/RuneCode/UI/Elements/`

| 프리팹 | 편집 대상 |
| --- | --- |
| TabButton | 작업실 탭과 룬 팔레트 분류 탭. Normal / Selected 표시 및 SelectionMarker |
| RunePaletteRow | 룬 팔레트의 공통 행. Available / Unavailable 배경과 Label |
| RunePaletteShapeRow / RunePaletteBehaviorRow / RunePaletteModifierRow / RunePaletteFlowRow / RunePaletteMethodRow | 형태 / 행동 / Modifier / 흐름 / 메서드별 팔레트 행 Variant |
| QuickPaletteRow | 빠른 룬 검색 결과 행 |
| QuickPaletteShapeRow / QuickPaletteBehaviorRow / QuickPaletteModifierRow / QuickPaletteFlowRow / QuickPaletteMethodRow | 빠른 검색의 분류별 행 Variant |
| SpellListRow | 저장한 마법 목록 행 |
| ValidationRow / ValidationErrorRow / ValidationWarningRow / ValidationSuccessRow | 검증 안내, 오류, 경고, 성공 행 |
| InspectorHeading / InspectorTitle / InspectorNote / InspectorHint | 런타임 생성 인스펙터의 머리글, 노드 이름, 설명, 미선택 안내 |
| InspectorButton / InspectorDeleteButton | 인스펙터 조작 버튼과 삭제 버튼 |
| UiText / UiInput / UiScrollList | 공통 생성 텍스트, 입력창, 스크롤 목록 원본 |
| MissionTimer | 일반 TimerLabel / Warning 표시. 타이머 문구는 두 텍스트에 동일하게 갱신됨 |
| CooldownRadial | 원형 Fill, Track, Background, Caption, Remaining 표시. 공격·대쉬의 색상과 모양은 각 인스턴스의 프리팹 이미지에서 편집 |
| UpgradeTreeConnection | 양 끝 노드 중 미학습 노드가 있는 Locked / 양쪽을 모두 배운 Satisfied 연결선 |
| GraphNodeLabel | 그래프 노드의 라벨 원본 |
| GraphVisualStyle | 그래프 배경, 격자, 노드, 연결선, 포트, 선택 표시의 색상 템플릿 |
| PopupShade / UiHitArea | 화면 생성 도구에서 쓰는 팝업 차단 배경 / 투명 입력 영역 |

강화 노드 원본은 `Assets/RuneCode/Prefabs/UI/UpgradeTreeNode.prefab`이다. Locked / Available / Partial / Max 자식의 Image를 각각 편집한다. Available / Partial / Max의 기본 이미지는 동일한 파란색이며, 레벨에 따른 노란색은 공통 LevelFill 자식에서 그린다. 루트 Image는 Source Image 없이 Alpha 0인 투명 클릭 영역으로 유지한다. 표시용 이미지를 루트에 넣지 말고 상태 자식과 LevelFill에서 편집한다. 효과 아이콘은 기존 Icon / SpriteIcon과 강화 트리 SO의 아이콘 연결을 사용한다.

SO의 Icon이 비어 있으면 `UpgradeTreeNodeIconGraphic.cs`가 벡터 아이콘을 그린다. 강화는 EffectType, 해금은 RuneId로 구분한다. 메인보드는 회로 기판, 파워는 배터리, RAM은 메모리 모듈, GPU는 팬이 달린 보드, CPU는 칩, HP는 하트, 이동속도는 부츠, 스크랩 획득량은 조각과 더하기, 획득범위는 자석, 처치 전력은 쓰러진 적과 번개다. 화염·얼음·전격은 각각 불꽃·눈송이·번개, 폭발·공전·반복·빔·부채꼴은 각각 폭발 윤곽·궤도·순환 화살표·평행 광선·부채꼴이다. 같은 효과가 반복되는 강화 노드는 같은 상징을 사용한다. 배치하지 않은 기존 해금 룬도 개별 아이콘을 지원한다.

직접 만든 이미지로 교체하려면 `UpgradeTree.asset`의 해당 노드 Icon에 Sprite를 지정한다. 지정한 Sprite가 벡터보다 우선한다. 공통 크기와 색은 노드 프리팹의 Icon / SpriteIcon에서 변경한다. 새로운 룬의 기본 벡터 모양은 `DrawRune`에 해당 RuneId 분기를 추가하며, Presenter → Panel → NodeView가 RuneId를 전달한다.

강화 노드는 회색 테두리와 채도를 높인 배경색을 포함한 `Tree_FourCorners_Locked_Vivid_128.png`, `Tree_FourCorners_Available_Vivid_128.png`를 사용한다. 상태 자식의 Image Color는 흰색이다. UiStateVisual의 States 순서는 Locked(0), Available(1), Partial(2), Max(3)이며 Selectable은 루트 Button을 참조한다. 비용 부족 전용 상태는 제거했다. 비용이 부족해도 개방 여부와 학습 레벨에 맞는 모습을 유지하며 구매는 차단하고 툴팁에 필요한 비용을 표시한다. 이전 보라색 Partial·초록색 Max 이미지 파일은 보존하지만 현재 프리팹에서는 사용하지 않는다.

LevelFill의 `UpgradeTreeNodeFillGraphic` 컴포넌트에서 Color를 바꾸면 일부 강화와 MAX의 채움 색이 함께 바뀐다. 현재 노란색은 `#F4CC47`이다. Inset과 Corner Cut은 테두리를 가리지 않도록 채움 내부 영역의 여백·모서리를 조정한다. 실행 중 코드는 색을 덮어쓰지 않고 `현재 레벨 / 최대 레벨`에 따라 아래에서 위로 채움 높이만 갱신한다. 1/3·2/3은 파란 바탕 위에 노란색이 각각 1/3·2/3만큼 표시되고 MAX는 전체 내부가 노란색이 된다.

LevelBadge는 노드 중앙 아래의 가독성 배경이며, 자식 Label에서 글꼴·크기·색을 편집한다. 열린 강화 노드는 `0/3`, `1/3`, `2/3`, 완료 노드는 `MAX`로 표시한다. 해금 노드는 열려 있을 때 `0/1`, 해금 후 `MAX`이며 잠긴 노드는 레벨 배지를 숨긴다. Icon / SpriteIcon은 배지와 겹치지 않도록 중심보다 위에 배치했다. 레벨 문구·채움은 UpgradeTreeNodeView, 최대 레벨 전달은 UpgradeTreePresenter·UpgradeTreePanel이 담당한다.

CostBadge는 오른쪽 위 모서리의 다음 구매 비용 표시다. Available / Partial에서 스크랩 아이콘과 숫자만 표시하며, 비용이 부족해도 표시를 유지한다. Locked / Max에서는 숨긴다. 숫자는 세션의 실제 다음 구매 비용을 사용하므로 강화 레벨에 따라 갱신되고 디버그 모드에서는 0으로 표시된다. 기존 툴팁의 비용 표시도 유지된다. `UpgradeTreeNode.prefab/CostBadge`의 RectTransform·Image에서 위치·배경을, 자식 ScrapIcon의 Graphic Color와 Label의 TMP 설정에서 아이콘 색·글자 모양을 편집한다. 배지의 Image·텍스트·아이콘은 Raycast Target을 꺼 노드 입력을 가로막지 않는다.

연결선은 양 끝 노드가 모두 1레벨 이상일 때만 Satisfied로 바뀐다. 선행 노드를 배우기만 했거나 디버그 모드로 선행 조건을 무시할 수 있어도, 다음 노드를 배우기 전에는 기본 Locked 표시를 유지한다. MAX까지 강화할 필요는 없다.

화면 전체 배치는 기존 `Assets/RuneCode/Resources/RuneCode/UI/WorkshopScreen.prefab`, `MissionScreen.prefab` 및 각 Popup 프리팹에서 편집한다. 공통 일반 버튼과 패널은 기존 `Assets/RuneCode/Prefabs/UI/UiButton.prefab`, `UiPanel.prefab`을 사용한다.

전투 HUD의 공격·대쉬 원형 쿨다운 게이지는 `Assets/RuneCode/Prefabs/UI/CooldownRadial.prefab`을 중첩 사용한다. `MissionScreen.prefab`의 `AttackCooldownRadial` / `DashCooldownRadial` 인스턴스에서 위치와 캡션을 바꾸고, `Fill` 이미지의 색상은 인스턴스별로 조정한다. 공통 링 두께·배경·글꼴은 원본 프리팹에서 편집한다.

## 강화 트리 데이터와 시작 레벨

`Assets/RuneCode/Resources/RuneCode/UpgradeTree.asset`에서 배치·선행 노드·비용·강화량·아이콘을 편집한다. 현재 배치 노드는 16개이며 중앙 줄기는 메인보드 → RAM → GPU → CPU → 스크랩 획득범위 → 이동속도다. 나머지 가지는 각 부모에서 대각선으로 뻗는다.

```text
메인보드 (시작 1/1)
├─ 화염 해금: 50
└─ RAM
   ├─ 공전 해금: 100
   ├─ 스크랩 획득량
   └─ GPU
      ├─ 파워
      ├─ 얼음 해금: 150
      └─ CPU
         ├─ 반복 해금: 200
         ├─ HP
         └─ 스크랩 획득범위
            ├─ 폭발 해금: 250
            └─ 이동속도
               ├─ 메인보드
               └─ GPU
```

루트를 depth 1로 계산해 강화의 1·2·3레벨 비용을 `(depth - 1) × 10 / 15 / 30`으로 설정했다. 비용은 각 노드의 Levels → Cost에 저장되어 있으므로 노드를 추가하거나 선행 관계를 바꾸면 직접 수정한다. 해금은 1회, 루트 외 강화는 최대 3레벨이며, 직전 선행 노드 1레벨부터 구매 가능하다. CPU는 기존처럼 구매·레벨만 반영하고 시전 속도 효과는 아직 적용하지 않는다.

| depth | 강화 노드 | 레벨별 스크랩 비용 |
| --- | --- | --- |
| 1 | 루트 메인보드 | 시작부터 1/1, 추가 구매 없음 |
| 2 | RAM | 10 / 15 / 30 |
| 3 | 스크랩 획득량, GPU | 20 / 30 / 60 |
| 4 | 파워, CPU | 30 / 45 / 90 |
| 5 | HP, 스크랩 획득범위 | 40 / 60 / 120 |
| 6 | 이동속도 | 50 / 75 / 150 |
| 7 | 마지막 메인보드, GPU | 60 / 90 / 180 |

Initial Level은 구매 기록이 없어도 적용되는 강화 시작 레벨이다. 루트만 1이며 Levels는 비용 0·회복량 0.5인 항목 하나를 가진다. 신규·기존 진행과 디버그 초기화에서 모두 루트의 전력 회복량 +0.5/s와 MAX 표시가 적용된다. 다른 노드는 Initial Level 0이고, 룬 해금 노드는 항상 0을 사용한다. 실제 레벨은 시작 레벨과 저장된 구매 레벨 중 높은 값을 최대 레벨 이내로 제한한 값이다.

기존 노드 ID·강화량·아이콘은 유지했다. 이전 트리에서 빠진 부채꼴·잔류·빔 해금과 중간 메인보드는 배치하지 않을 노드 목록에 있으며, 화면에 표시하거나 강화 효과를 합산하지 않는다. 기존 구매·룬 해금 저장 기록은 초기화하지 않는다.

## 작업실 탐색과 설정 팝업

작업실 탭은 **룬 설계 / 강화 트리** 두 개만 사용한다. 시간제 전투 진입 버튼과 설정 버튼은 `UiButton` 일반 버튼이며, `TabButton`의 선택 상태를 사용하지 않는다.

| 편집 대상 | 프리팹과 위치 |
| --- | --- |
| 룬 설계·강화 트리 탭 | `WorkshopScreen.prefab` → `Page/HeaderArea/BtnArea/editorTabButton`, `treeTabButton` |
| 설정 열기 버튼 | `WorkshopScreen.prefab` → `Page/HeaderArea/BtnArea/settingsButton` |
| 시간제 전투 진입 버튼 | `WorkshopScreen.prefab` → `Page/HeaderArea/stageButton` |
| 스테이지 선택의 뒤로가기 버튼 | `WorkshopScreen.prefab` → `Page/HeaderArea/BtnArea/BackButton` |
| 설정 창 배치·배경·닫기 | `SettingsPopup.prefab` → `Card`, `Card/Content`, `Card/CloseButton` |
| 소리 설정 열기 버튼 | `SettingsPopup.prefab` → `Card/Content/SoundSettingsButton` |
| 마스터 음량·저장·닫기 | `SoundSettingsPopup.prefab` → `Card/MasterVolumeSlider`, `Card/SaveButton`, `Card/CloseButton` |
| 설정 창의 검은 딤 배경 | `SettingsPopup.prefab` → `PopupShade`의 Image Color `(0, 0, 0, 0.7)` |

`SettingsPopup.prefab`은 `Assets/RuneCode/Resources/RuneCode/UI/`에 있다. 기존 화면 흔들림, 히트스톱, 저장 초기화와 디버그 설정 참조를 이 팝업으로 옮겼다. 배경은 입력을 차단하며 닫기 버튼 또는 Esc로 닫는다. 딤 색상은 이 팝업 인스턴스의 프리팹 Override이므로 공용 `PopupShade.prefab`을 바꿀 필요가 없다.

`SoundSettingsPopup.prefab`도 같은 Resources 폴더에 있으며 기존 설정 창 위에 열린다. 열 때마다 현재 런타임 마스터 음량으로 슬라이더를 초기화하고, 슬라이더 조작 중에는 음량을 적용하지 않는다. `저장`을 누를 때만 런타임 음량과 기기 공통 설정을 함께 확정하며 팝업은 유지한다. X 또는 Esc로 닫으면 저장하지 않은 슬라이더 값은 폐기되고 다음 진입 때 다시 초기화된다.

스테이지 선택 중에는 두 탭과 전투 진입 버튼을 숨기고 뒤로가기를 표시한다. 뒤로가기 또는 Esc는 진입 직전에 보던 룬 설계·강화 트리로 돌아간다. 설정 팝업을 열거나 닫아도 현재 화면은 유지한다. 이동할 버튼을 직접 배치한 뒤에는 `WorkshopScreen` 컴포넌트의 Tab Buttons(룬 설계, 강화 트리 순서), Deploy Button, Settings Button, Back Button 참조를 유지한다. 팝업의 Panel과 Close Button 참조도 유지한다.

## 상태 편집 방법

1. 원본 프리팹을 열고 해당 상태 자식을 선택한다.
2. Image의 Source Image에 원하는 Sprite를 연결한다. 9-slice 이미지는 Image Type을 Sliced로 설정한다.
3. 새 Corner 이미지는 Image Color를 흰색으로 두고 Source Image로 배경색을 선택한다. Image Color를 추가 변경하면 테두리에도 같은 색이 곱해진다. 글자 색상은 TMP의 Vertex Color에서 정한다.
4. 다른 상태의 자식도 각각 편집한다. 프리팹의 기본 활성 상태를 바꿔 미리 볼 수 있다. 실행 중에는 UiStateVisual이 상태에 맞는 자식 하나만 켠다.

TabButton의 클릭 연결, 팔레트 문구와 사용 가능 여부, 강화 트리 상태 판단은 기존 화면 코드가 담당한다. Button의 Transition은 **Color Tint**이며 상태 색상은 Sprite 색상과 Image Color에 곱해진다. 기존 상호작용 설정을 유지했다.

| Button 설정 | RGB 배율 | Alpha |
| --- | --- | --- |
| Normal Color | 1.00 | 1.00 |
| Highlighted Color (Hover) | 1.22 | 1.00 |
| Pressed Color | 0.72 | 1.00 |
| Selected Color (키보드·게임패드 포커스) | 1.12 | 1.00 |
| Disabled Color | 0.45 | 0.70 |

Color Multiplier는 1, Fade Duration은 0.08초이다. 상태별 배경 자식이 있는 버튼은 UiStateVisual의 **Selectable**에 해당 Button을 연결한다. Show 호출 시 활성 자식의 Graphic을 Target Graphic으로 연결하므로, 그 자식에 기본 색상과 이미지를 설정한다. 루트의 투명 Image는 클릭 영역이므로 유지한다. TabButton의 Selected 자식은 현재 열린 탭을 표시하며, Button Selected Color의 포커스 표시와 별개이다.

## 룬 분류마다 다르게 꾸미기

`RunePaletteShapeRow.prefab` 등 원하는 분류 Variant를 열고 `Available`, `Unavailable`의 Source Image와 `Label`의 Vertex Color를 편집한다. 공통 `RunePaletteRow`를 편집하면 개별 Variant에서 Override하지 않은 설정이 함께 바뀐다. 특정 분류만 다르게 하려면 해당 Variant에서 값을 변경한다. 빠른 검색은 `QuickPalette…Row` Variant를 별도로 편집한다. 각 Variant에는 색상이 포함된 FourCorners 이미지를 적용했다. 파일·색상표는 [UI_BAKED_CORNER_SPRITES.md](UI_BAKED_CORNER_SPRITES.md)를 참고한다.

프리팹 연결을 바꾸려면 `WorkshopScreen.prefab`의 `SpellEditorPanel` 컴포넌트에서 **Category Row Prefabs** 배열을 편집한다. 빠른 검색은 `QuickPalettePopup.prefab`의 같은 배열에서 변경한다.

| Category | 분류 | 기본 팔레트 Prefab |
| --- | --- | --- |
| shape | 형태 | RunePaletteShapeRow |
| behavior | 행동 | RunePaletteBehaviorRow |
| modifier | Modifier | RunePaletteModifierRow |
| flow | 흐름 | RunePaletteFlowRow |
| method | 메서드 | RunePaletteMethodRow |

배열의 Category에는 위 ID를, Prefab에는 사용할 UiRow 프리팹을 연결한다. 연결되지 않은 분류나 비어 있는 Prefab은 기존 Rune Row Prefab(빠른 검색은 Row Prefab)을 사용한다. 중복 Category가 있으면 위쪽의 유효한 연결을 사용한다. '전체' 탭에서도 각 룬의 실제 분류에 맞는 Variant를 사용한다.

속성은 현재 팔레트 행이 아닌 노드 파라미터의 드롭다운으로 선택하므로 이 배열에 기본 연결을 추가하지 않았다. 색상 변경은 프리팹에서만 하며 런타임 코드는 분류에 맞는 행 원본만 선택한다.

## 팔레트 2열 배치와 문구

룬 팔레트와 빠른 검색 목록은 Content의 Grid Layout Group을 사용한다. Constraint는 Fixed Column Count / 2, Spacing은 (6, 6), 셀 높이는 52이다. 현재 셀 너비는 작업실 80, 빠른 검색 244이다. 팔레트 폭을 직접 바꾸면 해당 Grid의 Cell Size X도 `(콘텐츠 너비 - 가로 간격) / 2`로 조정한다. Content Size Fitter는 Horizontal Unconstrained / Vertical Preferred Size를 유지한다.

일반 룬은 이름만 한 줄로 표시하고 잠긴 경우에만 둘째 줄에 `미해금`을 추가한다. 해금된 룬에는 빈 줄이나 개행을 붙이지 않으며 RAM과 강화 트리 안내는 버튼에 표시하지 않는다. Modifier는 첫 줄에 이름·등급, 둘째 줄에 `보유 남은 수/전체 보유 수`를 표시한다. Label의 Vertical Alignment는 Middle이며 위·아래 Margin은 0으로, 한 줄과 두 줄 모두 버튼의 세로 가운데에 정렬한다. 기존 보유량 필터와 클릭·드래그 동작은 유지한다. 빠른 검색은 기존처럼 해금된 룬만 표시한다.

행 원본의 Label은 자동 줄바꿈을 꺼 두 줄 문구가 세 줄로 늘어나지 않게 했다. 긴 이름은 Auto Size로 글자를 줄이며 그래도 넘치면 Ellipsis로 표시한다. 문구는 SpellPalettePresenter, 두 줄 높이는 같은 클래스의 ROW_HEIGHT에서 관리한다.

## 그래프 노드의 색상과 텍스트

그래프 노드의 분류 색상은 `GraphVisualStyle.prefab`의 Category Row Prefabs에 연결된 팔레트 행 프리팹에서 읽는다. 해당 행 Button의 Target Graphic에 지정된 Image Color와 읽기 가능한 Sprite의 중앙 픽셀 색을 곱해 상단 띠·분류 아이콘·선택 테두리 색상으로 사용한다. 새 Rune_FourCorners 이미지의 Texture Importer는 이 조회를 위해 Read/Write Enabled를 켰다. 교체할 룬 이미지도 중앙 픽셀을 배경색으로 두고 Read/Write Enabled를 켜야 한다. 형태·행동·Modifier·흐름·메서드는 팔레트와 같은 Variant를 참조하므로 새 이미지로 교체해도 분류 색상이 유지된다. 시전 코어처럼 팔레트에 없는 분류는 기존 NodeAccent 템플릿 색상을 사용한다.

분류 띠는 노드 상단에 높이 8px로 표시하며 GraphMeshPainter의 NODE_ACCENT_HEIGHT에서 조정한다. 제목과 입력 포트 이름의 왼쪽 여백은 GraphNodeLabels의 LABEL_PADDING_X(24px), 출력 포트 이름의 오른쪽 여백은 LABEL_PADDING_RIGHT(14px)이다. 출력 이름은 연결점과 겹치지 않도록 오른쪽 여백을 유지한다.

제목·설명과 포트 행은 별도 TMP 라벨로 표시한다. 포트 행의 중심 Y는 GraphNodeLayout의 실제 포트 좌표를 사용하며, 확대·축소와 노드 이동에도 점과 이름이 함께 움직인다. 첫 포트는 노드 상단에서 52px, 이후는 23px 간격이며 제목·설명을 위한 공간을 포함해 노드 높이를 계산한다. 지원 중단 출력은 기존 연결이 있을 때만 표시하고 취소선을 유지한다. 글자 색상과 글꼴 자산은 기존 GraphNodeLabel 프리팹에서 편집한다.

## 스테이지 출현 적 미리보기

`Assets/RuneCode/Resources/RuneCode/UI/WorkshopScreen.prefab`의 `DeployTab/DeployCard`에서 편집한다. `EnemyPreview`는 스테이지 선택 제목과 스테이지 변경 버튼 사이에 있으며, 선택한 1~10스테이지의 출현 적만 표시한다. 일반 출현 후보·웨이브·엘리트 편성과 캐리어 생성 설정을 읽으므로 스테이지별 적 목록을 UI 코드에 별도로 작성하지 않는다. 분열체는 최초 개체만 표시하고, 분열 후 생기는 중형·소형 개체는 추가하지 않는다.

| 편집 대상 | 위치 및 방법 |
| --- | --- |
| 카드 공통 배치 | `Assets/RuneCode/Prefabs/UI/StageEnemyPreviewRow.prefab`의 Background, Icon, Name, Description. 이미지 → 이름 → 설명 순서의 세로 배치다. |
| 각 적의 이미지·이름·한 줄 설명 | WorkshopScreen의 `EnemyPreview/EnemyGrid/적 ID` 인스턴스. Icon/Body와 Icon/Core의 Sprite·Color, Name·Description의 TMP 텍스트를 편집한다. |
| 목록 열 수·간격 | EnemyGrid의 Grid Layout Group. 현재 6열, 행 높이 102, 간격 (6, 6)이다. 열 수를 바꾸면 Cell Size와 미리보기 영역 높이도 조정한다. |
| 보스 배너 | `Assets/RuneCode/Prefabs/UI/BossStageBanner.prefab`. WorkshopScreen의 BossStageBanner 인스턴스에서 위치를 편집한다. |
| 보스 카드 강조 | EnemyGrid의 `boss.governor` 인스턴스에서 Background와 Name의 색상을 편집한다. |

5·10스테이지는 스테이지 데이터의 IsBoss에 따라 보스 배너가 표시된다. 색상·이미지·설명은 프리팹 설정을 사용하고, 실행 중 코드는 카드와 배너의 활성 상태만 바꾼다. 설명은 한 줄이며 긴 문구는 자동 크기 조절 후 말줄임 처리된다.

새 적을 표시하려면 EnemyGrid에 StageEnemyPreviewRow를 중첩하고 이미지·이름·설명을 설정한 뒤, DeployTab의 DeployPanel 컴포넌트에서 Enemy Previews 배열에 해당 Enemy Id와 카드 Root를 연결한다. 캐리어가 생성하는 적도 개별 카드 연결이 필요하다. 기존 카드의 오브젝트 이름만 바꿀 때는 배열의 Root 참조를 유지한다.

디버그 모드로 새로 시작하거나 저장 초기화를 하면, 효과 표에 실제 값이 정의된 모든 Modifier 종류·등급을 각각 9개씩 가진다. 효과 값이 null인 등급은 지급하지 않는다. 디버그에서는 스테이지 변경 버튼으로 클리어 여부와 관계없이 이동할 수 있으며, 일반 모드의 보유량·스테이지 해금 기록과 저장 파일에는 영향을 주지 않는다.

## 강화 트리 배치 편집

`Assets/RuneCode/Prefabs/UI/UpgradeTreeLayout.prefab`을 Prefab Mode로 연다. 현재 트리의 노드 32개와 연결선 31개가 배치되어 있고, WorkshopScreen은 이 프리팹을 중첩해서 사용한다. 각 노드는 기존 `UpgradeTreeNode.prefab`의 인스턴스이므로 공통 디자인 변경도 유지된다.

노드 이름은 `표시 이름 [노드 ID]` 형식이다. 옮길 노드를 선택하고 Rect Tool 또는 RectTransform의 Pos X / Pos Y로 위치를 수정한 뒤 저장한다. 실행할 때 노드를 재생성하거나 좌표를 덮어쓰지 않는다. 노드 위치의 원본은 이 프리팹이며 SO에는 좌표를 저장하지 않는다.

연결선의 `UpgradeTreeConnectionView`는 Source / Target에 연결된 노드의 실제 중심을 따라 위치·길이·각도를 갱신한다. Prefab Mode에서도 노드를 옮기면 선이 따라온다. 선 자체를 이동하는 대신 끝점 노드를 옮긴다. 두께는 Line Width, 상태별 색상은 기존 `Elements/UpgradeTreeConnection.prefab`의 Locked / Satisfied에서 편집한다.

비용·레벨·효과량·선행 구매 조건은 계속 `Assets/RuneCode/Resources/RuneCode/UpgradeTree.asset`에서 편집한다. 위치를 바꾸는 동안 `UpgradeTreeNodeView`의 Node Id는 유지한다. 새 노드를 추가하려면 SO에 배치 노드를 추가하고, 배치 프리팹에서 기존 노드를 복제해 Node Id를 맞춘다. 연결선도 복제해 Source / Target을 SO의 선행 관계와 동일하게 연결한다. 선행 관계를 변경할 때는 SO와 연결선의 끝점 참조를 함께 수정한다.

강화 합계 표시는 `Assets/RuneCode/Prefabs/UI/UpgradeTreeStats.prefab`에서 디자인을 편집한다. Title은 제목, Values는 강화 수치 텍스트다. WorkshopScreen의 `UpgradeTreeViewport/UpgradeTreeStats` 인스턴스에서 화면 위치를 바꿀 수 있다. 콘텐츠 밖에 있어 우클릭 이동·휠 확대의 영향을 받지 않는다. 기본 능력치를 제외한 트리의 누적 증가량을 표시하며 루트의 시작 레벨 효과도 포함한다. 구매·디버그 초기화 후 다른 노드 표시와 함께 갱신된다.

## 코드가 계속 담당하는 항목

- 강화 노드의 비용·선행 조건·효과는 Upgrade Tree SO가 관리한다. 위치는 UpgradeTreeLayout 프리팹, 연결선 길이·각도는 연결된 노드의 실제 위치가 결정한다. 강화 합계의 표시 수치는 세션에서 계산한다.
- 반복 목록과 인스펙터는 프리팹을 사용하되 행 높이, 표시 문자열, 입력값과 클릭 동작은 코드가 갱신한다.
- 그래프의 노드·포트·연결선 모양은 메시로 그린다. GraphVisualStyle의 자식 Image는 색상 템플릿이며 직접 화면에 표시하지 않는다. 이 템플릿에 Sprite를 넣어도 메시 모양은 바뀌지 않는다. 자식 순서와 GraphVisualStyle의 Templates 참조 배열은 유지한다.
- 적·마법·투사체·월드 이펙트 및 전투 피해 숫자의 색상 처리는 전투 그래픽이므로 유지한다.

새 요소는 Resources 경로로도 로드하므로 Elements 폴더와 프리팹 파일 이름을 변경하려면 해당 로드 경로도 수정해야 한다. 기존 사용자 편집 프리팹을 다시 생성하지 않고 원본을 직접 편집하는 방식을 권장한다.
