// 테이블 편집기 스키마 메타데이터다. 현재 포멧 기준으로 필드 레이블·어휘·그리드 열을 정의한다.
// 스키마에 없는 키는 런타임에 값 타입으로 추론해 "기타"로 렌더링한다.

const VOCAB = {
  runeCategory: ["core", "form", "element", "modifier", "flow", "action", "magic", "shape", "behavior", "method", "internal"],
  unlockType: ["start", "tree", "bench"],
  portKind: ["exec", "mod", "chain", "element"],
  portDirection: ["in", "out"],
  paramKind: ["number", "enum", "text"],
  elementCategory: ["elemental", "functional"],
  enemyMovement: ["chase", "turret", "anchor", "dashChase", "keepDistance", "sniper", "interceptor"],
  enemyAttack: ["none", "contact", "burst", "dashContact", "sniper", "interceptor"],
  enemyTrait: ["aegisShield", "relayAura", "deathExplosion", "circleShield", "carrier"],
  formationShape: ["ringRandomSlot", "ringAllSlots", "ringDouble", "pointSequence", "ringHalf", "ringSectorBlock", "arenaRandom", "arenaCluster"],
  angle: ["random", "ahead", "fixed"],
  stageType: ["normal", "boss"],
  dropType: ["behavior", "modifier", "element"],
  ramMode: ["shared", "perSpell"],
  effectType: ["effectMultiplier", "energyCostMultiplier", "multipleCount", "pierceCount", "radiusMultiplier", "durationMultiplier", "speedMultiplier", "homingTier", "waveCount", "beamEmpowerMultiplier"],
  grade: ["C", "B", "A", "S"],
};

// 필드 레이블 사전이다. 같은 키가 테이블마다 달리 읽히는 경우 개별 스키마에서 덮어쓴다.
const LABELS = {
  _id: "ID", _name: "이름", _nameKey: "이름 키(현지화)", _titleKey: "제목 키", _descriptionKey: "설명 키",
  _enemyId: "적 ID", _formation: "진형", _dropTable: "드롭 테이블", _reward: "보상", _count: "수량",
  _grade: "등급", _runeId: "룬 ID", _modifierId: "Modifier ID", _stage: "스테이지", _type: "형식",
  _weight: "가중치", _min: "최소", _max: "최대", _order: "순서", _point: "스폰 포인트", _key: "키", _value: "값",
  // 적
  _hp: "체력", _speed: "이동 속도", _radius: "반경", _damage: "피해", _attackInterval: "공격 간격",
  _warningSeconds: "경고 시간", _attackRange: "공격 사거리", _dashSpeed: "돌진 속도", _dashSeconds: "돌진 시간",
  _projectileSpeed: "탄환 속도", _projectileRadius: "탄환 반경", _projectileLifetime: "탄환 수명",
  _spreadDegrees: "산각(도)", _burstInterval: "연사 간격", _movement: "이동 방식", _attack: "공격 방식",
  _traits: "특수 능력", _sides: "변 수", _splitInto: "분열 대상", _splitCount: "분열 수", _splitDelay: "분열 지연",
  _explosionRadius: "폭발 반경", _explosionDamage: "폭발 피해", _shieldRadius: "보호막 반경",
  _aimSeconds: "조준 시간", _lockSeconds: "잠금 시간", _moveSeconds: "이동 시간", _stopSeconds: "정지 시간",
  _preferredDistance: "선호 거리",
  // 룬
  _category: "카테고리", _ram: "RAM", _energy: "에너지", _energyMult: "에너지 배율",
  _unlockType: "해금 방식", _unlockCost: "해금 비용", _stats: "전투 수치", _ports: "포트", _params: "파라미터",
  _tags: "태그", _element: "속성", _modifierTargets: "효과 대상", _options: "선택지", _option: "선택지 값",
  _damage: "피해", _lifetime: "수명", _offset: "생성 거리", _orbitCount: "궤도 수", _orbitRadius: "궤도 반경",
  _angularSpeed: "각속도", _hitInterval: "재적중 간격", _tickInterval: "틱 간격",
  _damageMultiplier: "피해 배율", _radiusMultiplier: "반경 배율", _pierce: "관통", _pierceLoss: "관통 감소",
  _homingTurn: "유도 회전", _homingRange: "유도 거리", _orbitTargetCount: "공전 대상 수", _arcRange: "연쇄 거리", _arcTargets: "연쇄 대상",
  _waveCount: "회차 수", _waveInterval: "회차 간격",
  _arcMultiplier: "연쇄 배율", _learningMultiplier: "학습 배율", _spreadAngle: "발사 각도",
  _shieldAmount: "보호막량", _shieldSeconds: "보호막 지속", _coneAngle: "부채꼴 각도",
  _expandSeconds: "확장 시간", _warnSeconds: "예고 시간",
  _speedMultiplier: "속도 배율", _durationMultiplier: "지속 배율", _beamWidth: "빔 폭", _beamLength: "빔 길이",
  _executionSeconds: "실행 시간", _executionSecondsPerArea: "넓이당 실행 시간", _shapeCostRates: "넓이당 비용",
  _resource: "자원", _rate: "단가", _homingTiers: "유도 단계", _tier: "단계",
  _portId: "포트 ID", _kind: "종류", _direction: "방향", _status: "상태", _paramId: "파라미터 ID",
  _defaultNumber: "숫자 기본값", _defaultText: "문자 기본값", _internalValue: "내부 값", _runtimeTag: "런타임 태그",
  // 밸런스
  _maxHp: "최대 체력", _moveSpeed: "이동 속도", _maxEnergy: "최대 에너지", _energyRegen: "에너지 회복",
  _dashDistance: "대시 거리", _dashDuration: "대시 시간", _dashCooldown: "대시 쿨다운", _hitInvulnerability: "피격 무적",
  _maxLiveSpellEntities: "생존 마법 개체 상한", _maxFrameSteps: "프레임 단계 상한", _maxGraphNodes: "그래프 노드 상한",
  _maxGraphEdges: "그래프 엣지 상한", _maxEnemies: "적 상한", _maxCompiledActions: "컴파일 액션 상한",
  _maxScheduledExecutions: "예약 실행 상한", _maxActionsPerTick: "틱당 실행 상한",
  _mode: "RAM 모드", _cooldownBase: "기본 쿨다운", _cooldownPerRam: "RAM당 쿨다운",
  _elementLearning: "속성 학습", _formLearning: "형태 학습", _elementCap: "속성 상한", _formCap: "형태 상한",
  _dotWeight: "도트 가중치", _noiseMultiplier: "노이즈 배율", _relayMultiplier: "중계 배율",
  _decayDelay: "감쇠 지연", _decayPerSecond: "감쇠/초", _resistanceThreshold: "저항 문턱",
  _burnDps: "자기 적용 화상 DPS", _burnDamageRatio: "적 화상 틱 피해 비율(공격 대비)",
  _burnInterval: "화상 간격", _burnSeconds: "화상 시간", _chillSlow: "냉기 감속",
  _chillSeconds: "냉기 시간", _chillMaxStacks: "냉기 최대 스택", _chillImmuneMaxStacks: "냉기 면역 스택",
  _freezeSeconds: "빙결 시간", _freezeImmunity: "빙결 면역", _empSeconds: "EMP 시간", _aegisAngle: "이지스 각도",
  _aegisReduction: "이지스 감소", _relayRadius: "중계 반경", _relayReduction: "중계 감소",
  _separationAllowance: "분리 여유", _separationStrength: "분리 강도", _separationMaxStep: "분리 최대 단계",
  _slotCost: "슬롯 비용", _deathRetention: "사망 보존율", _baseCapacity: "기본 용량", _maxLibrary: "보관함 상한",
  _modifierStartStock: "Modifier 시작 재고", _orbAbsorbRadius: "구 흡수 반경", _pickupAttractSpeed: "획득 흡인 속도",
  _terminalHeal: "터미널 회복",
  _tickRate: "틱 레이트", _multiOffset: "다중 오프셋", _telemetryHighlightSeconds: "텔레메트리 강조",
  _benchPlayerX: "벤치 플레이어 X", _benchPlayerY: "벤치 플레이어 Y", _benchDummyX: "벤치 더미 X",
  _benchDummyY: "벤치 더미 Y", _benchHp: "벤치 체력", _benchLineStartX: "벤치 열 시작 X", _benchLineGap: "벤치 열 간격",
  _benchLineCount: "벤치 열 수", _benchSwarmX: "벤치 군집 X", _benchSwarmY: "벤치 군집 Y",
  _benchSwarmColumns: "벤치 군집 열", _benchSwarmRows: "벤치 군집 행", _benchSwarmGapX: "벤치 군집 간격 X",
  _benchSwarmGapY: "벤치 군집 간격 Y", _spellVisualSeconds: "마법 이펙트 시간", _hitFlashSeconds: "피격 플래시",
  _damageNumberSeconds: "피해 숫자 시간", _wandOffset: "지팡이 오프셋", _areaPlacementRange: "폭발·잔류 공통 사거리", _beamCooldownSeconds: "빔 종료 후 쿨타임(초)", _executionTimeScale: "실행 시간 배율",
  // 스테이지
  _roomColumns: "방 열 수", _roomRows: "방 행 수", _roomsX: "가로 방 수", _roomsY: "세로 방 수",
  _hpStageScale: "체력 스케일", _damageStageScale: "피해 스케일", _clearRewardStageScale: "클리어 보상 스케일",
  _ringSlots: "링 슬롯", _ringMargin: "링 여백",
  _jitterScale: "지터 스케일", _reinforcementGap: "증원 간격", _stationaryAdvanceSpeed: "정지 전진 속도",
  _warningSeconds: "스폰 예고 시간", _minPlayerDistance: "스폰 최소 거리", _maxPlayerDistance: "스폰 최대 거리(0=무제한)",
  _spawnGraceSeconds: "스폰 접촉 유예", _groupSize: "묶음 크기",
  _firstStage: "시작 스테이지", _baseInterval: "기본 간격", _minInterval: "최소 간격", _stageScale: "스테이지 스케일",
  _intraRamp: "내부 램프", _rampSeconds: "램프 시간", _shape: "모양", _angle: "각도", _spacing: "간격",
  _interval: "간격(초)", _slots: "슬롯", _radiusScale: "스폰 반경 배율", _at: "시점", _repeatInterval: "반복 간격", _units: "유닛",
  _hpMultiplier: "체력 배율", _rewardMultiplier: "보상 배율", _speedMultiplier: "속도 배율",
  _radiusMultiplier: "반경 배율", _fromStage: "시작 스테이지", _toStage: "끝 스테이지(0=무한)", _kind: "종류",
  _timeLimit: "생존·제한 시간", _clearReward: "클리어 보상", _stream: "스트림",
  _waves: "웨이브", _eliteEvery: "엘리트 주기", _eliteTypes: "엘리트 종류", _modifierRewards: "Modifier 보상",
  _candidates: "후보", _rewards: "보상표", _map: "맵", _growth: "성장", _spawn: "스폰",
  // 보스·드롭·등급표
  _pattern: "패턴", _phaseTwoThreshold: "2페이즈 문턱", _phaseThreeThreshold: "3페이즈 문턱",
  _patchSeconds: "패치 시간", _radialInterval: "원거리 공격 간격", _radialCount: "원거리 발 수",
  _aimInterval: "조준 간격", _hazardInterval: "위험지대 간격", _hazardWarning: "위험지대 경고",
  _hazardLifetime: "위험지대 수명", _hazardRadius: "위험지대 반경", _hazardDamage: "위험지대 피해",
  _phaseThreeMultiplier: "3페이즈 배율", _reinforcement: "증원", _rolls: "추첨 횟수", _guaranteed: "보장 항목",
  _entries: "항목", _schemaVersion: "스키마 버전", _tableId: "표 ID", _gradeOrder: "등급 순서",
  _modifierGradeEffects: "Modifier 등급 효과", _effectType: "효과 종류", _gradeValues: "등급별 값",
  _stageDropTables: "스테이지 드롭표", _stageType: "스테이지 종류",
  _eliteModifierGradeChancesPercent: "등급 확률(%)", _stageClearModifierRewardChoice: "클리어 보상 선택",
  _choiceCount: "제시 수", _selectCount: "선택 수", _drawMethod: "추첨 방식", _candidateRule: "후보 규칙",
  _dataNotes: "데이터 메모", _tiles: "타일(ASCII)", _wavesCount: "웨이브 수",
  // 마법
  _version: "버전", _nodes: "노드", _edges: "엣지", _x: "X", _y: "Y",
  _fromNode: "시작 노드", _fromPort: "시작 포트", _toNode: "대상 노드", _toPort: "대상 포트", _isLegacyEvent: "이전 이벤트",
  _number: "숫자", _text: "문자",
};

// 테이블별 그리드 열과 상세 폼 필드다. type: text/number/bool/select/ref/multi/textarea/list/object
const SCHEMAS = {
  runes: {
    columns: ["_id", "_name", "_category", "_ram", "_energy", "_unlockType"],
    fields: [
      { k: "_id", label: "ID", type: "text" }, { k: "_name", label: "이름", type: "text" },
      { k: "_category", label: "카테고리", type: "select", vocab: "runeCategory" },
      { k: "_ram", label: "RAM", type: "number" }, { k: "_energy", label: "에너지", type: "number" },
      { k: "_energyMult", label: "에너지 배율", type: "number" },
      { k: "_unlockType", label: "해금 방식", type: "select", vocab: "unlockType" },
      { k: "_unlockCost", label: "해금 비용", type: "number" },
      { k: "_executionSeconds", label: "실행 시간(초)", type: "number" },
      { k: "_executionSecondsPerArea", label: "넓이당 실행 시간", type: "number" },
      { k: "_shapeCostRates", label: "넓이당 비용(Shape)", type: "list", item: [
        { k: "_resource", label: "자원", type: "text" }, { k: "_rate", label: "단가", type: "number" } ] },
      { k: "_stats", label: "전투 수치", type: "object", fields: "runeStats" },
      { k: "_ports", label: "포트", type: "list", item: [
        { k: "_order", label: "순서", type: "number" }, { k: "_portId", label: "포트 ID", type: "text" },
        { k: "_kind", label: "종류", type: "select", vocab: "portKind" },
        { k: "_direction", label: "방향", type: "select", vocab: "portDirection" },
        { k: "_max", label: "최대 연결", type: "number" }, { k: "_status", label: "상태", type: "select", vocab: "portStatus", allowEmpty: true },
      ] },
      { k: "_params", label: "파라미터", type: "list", item: [
        { k: "_order", label: "순서", type: "number" }, { k: "_paramId", label: "파라미터 ID", type: "text" },
        { k: "_kind", label: "종류", type: "select", vocab: "paramKind" },
        { k: "_min", label: "최소", type: "number" }, { k: "_max", label: "최대", type: "number" },
        { k: "_step", label: "단위", type: "number" }, { k: "_defaultNumber", label: "숫자 기본값", type: "number" },
        { k: "_defaultText", label: "문자 기본값", type: "text" },
        { k: "_options", label: "선택지", type: "list", item: [
          { k: "_order", label: "순서", type: "number" }, { k: "_option", label: "선택지 값", type: "text" } ] },
      ] },
      { k: "_tags", label: "태그", type: "list", item: [
        { k: "_order", label: "순서", type: "number" }, { k: "_tag", label: "태그", type: "text" } ] },
      { k: "_element", label: "속성", type: "object", fields: [
        { k: "_id", label: "속성 ID", type: "text" },
        { k: "_category", label: "분류", type: "select", vocab: "elementCategory" },
        { k: "_internalValue", label: "내부 값", type: "text" }, { k: "_runtimeTag", label: "런타임 태그", type: "text" } ] },
      { k: "_modifierTargets", label: "효과 대상", type: "list", item: [ { k: "", label: "대상 룬", type: "ref", ref: "rune" } ], scalarItem: true },
      { k: "_homingTiers", label: "유도 단계(mod.homing 전용)", type: "list", item: [
        { k: "_tier", label: "단계 이름", type: "text" }, { k: "_homingTurn", label: "유도 회전", type: "number" },
        { k: "_homingRange", label: "유도 거리", type: "number" },
        { k: "_orbitTargetCount", label: "공전 대상 수", type: "number" } ] },
    ],
  },
  runeStats: [
    { k: "_damage", label: "피해", type: "number" }, { k: "_speed", label: "속도", type: "number" },
    { k: "_radius", label: "반경", type: "number" }, { k: "_lifetime", label: "수명", type: "number" },
    { k: "_offset", label: "생성 거리", type: "number" }, { k: "_count", label: "개수", type: "number" },
    { k: "_orbitCount", label: "궤도 수", type: "number" }, { k: "_orbitRadius", label: "궤도 반경", type: "number" },
    { k: "_angularSpeed", label: "각속도", type: "number" }, { k: "_hitInterval", label: "재적중 간격", type: "number" },
    { k: "_tickInterval", label: "틱 간격", type: "number" },
    { k: "_damageMultiplier", label: "피해 배율", type: "number" }, { k: "_radiusMultiplier", label: "반경 배율", type: "number" },
    { k: "_pierce", label: "관통", type: "number" }, { k: "_pierceLoss", label: "관통 감소", type: "number" },
    { k: "_homingTurn", label: "유도 회전", type: "number" }, { k: "_homingRange", label: "유도 거리", type: "number" },
    { k: "_arcRange", label: "연쇄 거리", type: "number" }, { k: "_arcTargets", label: "연쇄 대상", type: "number" },
    { k: "_arcMultiplier", label: "연쇄 배율", type: "number" }, { k: "_learningMultiplier", label: "학습 배율", type: "number" },
    { k: "_spreadAngle", label: "발사 각도", type: "number" }, { k: "_shieldAmount", label: "보호막량", type: "number" },
    { k: "_shieldSeconds", label: "보호막 지속", type: "number" }, { k: "_coneAngle", label: "부채꼴 각도", type: "number" },
    { k: "_expandSeconds", label: "확장 시간", type: "number" }, { k: "_warnSeconds", label: "예고 시간", type: "number" },
    { k: "_speedMultiplier", label: "속도 배율", type: "number" }, { k: "_durationMultiplier", label: "지속 배율", type: "number" },
    { k: "_beamWidth", label: "빔 폭(Beam Shape)", type: "number" }, { k: "_beamLength", label: "빔 길이(Beam Shape)", type: "number" },
    { k: "_waveCount", label: "회차 수", type: "number" }, { k: "_waveInterval", label: "회차 간격", type: "number" },
  ],
  enemies: {
    columns: ["_id", "_movement", "_attack", "_hp", "_damage", "_reward"],
    groups: [
      { title: "기본", fields: [
        { k: "_id", label: "ID", type: "text" }, { k: "_hp", label: "체력", type: "number" },
        { k: "_speed", label: "이동 속도", type: "number" }, { k: "_radius", label: "반경", type: "number" },
        { k: "_damage", label: "피해", type: "number" }, { k: "_reward", label: "보상", type: "number" },
        { k: "_sides", label: "변 수", type: "number" } ] },
      { title: "행동", fields: [
        { k: "_movement", label: "이동 방식", type: "select", vocab: "enemyMovement" },
        { k: "_attack", label: "공격 방식", type: "select", vocab: "enemyAttack" },
        { k: "_attackInterval", label: "공격 간격", type: "number" }, { k: "_warningSeconds", label: "경고 시간", type: "number" },
        { k: "_attackRange", label: "공격 사거리", type: "number" }, { k: "_burstInterval", label: "연사 간격", type: "number" },
        { k: "_spreadDegrees", label: "산각(도)", type: "number" },
        { k: "_projectileSpeed", label: "탄환 속도", type: "number" }, { k: "_projectileRadius", label: "탄환 반경", type: "number" },
        { k: "_projectileLifetime", label: "탄환 수명", type: "number" },
        { k: "_dashSpeed", label: "돌진 속도", type: "number" }, { k: "_dashSeconds", label: "돌진 시간", type: "number" },
        { k: "_aimSeconds", label: "조준 시간", type: "number" }, { k: "_lockSeconds", label: "잠금 시간", type: "number" },
        { k: "_moveSeconds", label: "이동 시간", type: "number" }, { k: "_stopSeconds", label: "정지 시간", type: "number" },
        { k: "_preferredDistance", label: "선호 거리", type: "number" } ] },
      { title: "특수 능력", fields: [
        { k: "_traits", label: "특수 능력", type: "multi", vocab: "enemyTrait" },
        { k: "_explosionRadius", label: "폭발 반경", type: "number" }, { k: "_explosionDamage", label: "폭발 피해", type: "number" },
        { k: "_shieldRadius", label: "보호막 반경", type: "number" } ] },
      { title: "분열·드롭", fields: [
        { k: "_splitInto", label: "분열 대상", type: "ref", ref: "enemy" }, { k: "_splitCount", label: "분열 수", type: "number" },
        { k: "_splitDelay", label: "분열 지연", type: "number" },
        { k: "_dropTable", label: "드롭 테이블", type: "ref", ref: "drop", allowEmpty: true } ] },
    ],
  },
  formations: {
    columns: ["_id", "_shape", "_angle", "_spacing", "_interval", "_slots", "_radiusScale"],
    groups: [ { title: "진형", fields: [
      { k: "_id", label: "ID", type: "text" },
      { k: "_shape", label: "모양", type: "select", vocab: "formationShape" },
      { k: "_angle", label: "각도", type: "select", vocab: "angle" },
      { k: "_spacing", label: "간격", type: "number" }, { k: "_interval", label: "간격(초)", type: "number" },
      { k: "_slots", label: "슬롯", type: "number" },
      { k: "_radiusScale", label: "스폰 반경 배율(0=1)", type: "number" } ] } ],
  },
  bosses: {
    columns: ["_enemyId", "_pattern", "_phaseTwoThreshold", "_phaseThreeThreshold", "_dropTable"],
    groups: [
      { title: "보스", fields: [
        { k: "_enemyId", label: "적 ID", type: "ref", ref: "enemy" },
        { k: "_pattern", label: "패턴", type: "text" },
        { k: "_phaseTwoThreshold", label: "2페이즈 문턱", type: "number" },
        { k: "_phaseThreeThreshold", label: "3페이즈 문턱", type: "number" },
        { k: "_patchSeconds", label: "패치 시간", type: "number" },
        { k: "_radialInterval", label: "원거리 공격 간격", type: "number" }, { k: "_radialCount", label: "원거리 발 수", type: "number" },
        { k: "_aimInterval", label: "조준 간격", type: "number" },
        { k: "_hazardInterval", label: "위험지대 간격", type: "number" }, { k: "_hazardWarning", label: "위험지대 경고", type: "number" },
        { k: "_hazardLifetime", label: "위험지대 수명", type: "number" }, { k: "_hazardRadius", label: "위험지대 반경", type: "number" },
        { k: "_hazardDamage", label: "위험지대 피해", type: "number" },
        { k: "_phaseThreeMultiplier", label: "3페이즈 배율", type: "number" },
        { k: "_preferredDistance", label: "선호 거리", type: "number" },
        { k: "_dropTable", label: "드롭 테이블", type: "ref", ref: "drop", allowEmpty: true } ] },
      { title: "증원", fields: [
        { k: "_reinforcement", label: "증원", type: "object", fields: [
          { k: "_enemyId", label: "적 ID", type: "ref", ref: "enemy" }, { k: "_count", label: "수량", type: "number" },
          { k: "_interval", label: "간격(초)", type: "number" },
          { k: "_formation", label: "진형", type: "ref", ref: "formation" } ] } ] },
    ],
  },
  drops: {
    columns: ["_id"],
    groups: [ { title: "드롭 테이블", fields: [
      { k: "_id", label: "ID", type: "text" },
      { k: "_categoryChances", label: "분류 확률(합 100)", type: "list", item: [
        { k: "_category", label: "분류", type: "select", vocab: "dropType" }, { k: "_percent", label: "확률(%)", type: "number" } ] },
      { k: "_gradeChances", label: "분류별 등급 확률(분류마다 합 100, 비운 분류는 스테이지 등급표)", type: "list", item: [
        { k: "_category", label: "분류", type: "select", vocab: "dropType" },
        { k: "_grade", label: "등급", type: "ref", ref: "grade" }, { k: "_percent", label: "확률(%)", type: "number" } ] },
      { k: "_entries", label: "후보 룬(비운 분류는 활성 룬 전체)", type: "list", item: "dropEntry" } ] } ],
  },
  dropEntry: [
    { k: "_type", label: "분류", type: "select", vocab: "dropType" },
    { k: "_id", label: "룬 ID", type: "ref", ref: "rune" },
    { k: "_weight", label: "가중치", type: "number" },
  ],
  stages: {
    head: [
      { k: "_map", label: "맵", type: "object", fields: [
        { k: "_roomColumns", label: "방 열 수", type: "number" }, { k: "_roomRows", label: "방 행 수", type: "number" },
        { k: "_roomsX", label: "가로 방 수", type: "number" }, { k: "_roomsY", label: "세로 방 수", type: "number" } ] },
      { k: "_growth", label: "성장", type: "object", fields: [
        { k: "_hpStageScale", label: "체력 스케일", type: "number" }, { k: "_damageStageScale", label: "피해 스케일", type: "number" } ] },
      { k: "_spawn", label: "스폰", type: "object", fields: [
        { k: "_ringSlots", label: "링 슬롯", type: "number" }, { k: "_ringMargin", label: "링 여백", type: "number" },
        { k: "_jitterScale", label: "지터 스케일", type: "number" }, { k: "_reinforcementGap", label: "증원 간격", type: "number" },
        { k: "_warningSeconds", label: "스폰 예고 시간", type: "number" }, { k: "_spawnGraceSeconds", label: "스폰 접촉 유예", type: "number" },
        { k: "_minPlayerDistance", label: "스폰 최소 거리", type: "number" }, { k: "_maxPlayerDistance", label: "스폰 최대 거리(0=무제한)", type: "number" } ] },
      { k: "_stationaryAdvanceSpeed", label: "정지 전진 속도", type: "number" },
      { k: "_rewards", label: "보상표", type: "list", item: [
        { k: "_enemyId", label: "적 ID", type: "ref", ref: "enemy" }, { k: "_reward", label: "보상", type: "number" } ] },
    ],
  },
  sector1: {
    columns: ["_id", "_nameKey"],
    groups: [ { title: "방", fields: [
      { k: "_id", label: "ID", type: "text" },
      { k: "_nameKey", label: "이름 키(현지화)", type: "ref", ref: "strings", allowEmpty: true },
      { k: "_tiles", label: "타일(ASCII)", type: "tiles" },
      { k: "_waves", label: "웨이브", type: "list", item: [
        { k: "_spawns", label: "스폰", type: "list", item: [
          { k: "_enemyId", label: "적 ID", type: "ref", ref: "enemy" }, { k: "_point", label: "스폰 포인트", type: "number" },
          { k: "_count", label: "수량", type: "number" } ] } ] } ] } ],
  },
  spell: {
    columns: ["_id", "_name", "_version"],
    groups: [
      { title: "마법", fields: [
        { k: "_id", label: "ID", type: "text" }, { k: "_name", label: "이름", type: "text" },
        { k: "_version", label: "버전", type: "number" } ] },
      { title: "노드", fields: [
        { k: "_nodes", label: "노드", type: "list", item: [
          { k: "_id", label: "노드 ID", type: "text" }, { k: "_runeId", label: "룬 ID", type: "ref", ref: "rune" },
          { k: "_x", label: "X", type: "number" }, { k: "_y", label: "Y", type: "number" },
          { k: "_params", label: "파라미터", type: "list", item: [
            { k: "_key", label: "키", type: "text" }, { k: "_number", label: "숫자", type: "number" },
            { k: "_text", label: "문자", type: "text" } ] } ] } ] },
      { title: "엣지", fields: [
        { k: "_edges", label: "엣지", type: "list", item: [
          { k: "_id", label: "ID", type: "text" },
          { k: "_fromNode", label: "시작 노드", type: "ref", ref: "spellNode" }, { k: "_fromPort", label: "시작 포트", type: "text" },
          { k: "_toNode", label: "대상 노드", type: "ref", ref: "spellNode" }, { k: "_toPort", label: "대상 포트", type: "text" },
          { k: "_order", label: "순서", type: "number" }, { k: "_isLegacyEvent", label: "이전 이벤트", type: "bool" } ] } ] },
    ],
  },
  balance: {
    groups: [
      { title: "플레이어", keys: ["_maxHp", "_moveSpeed", "_maxEnergy", "_energyRegen", "_dashDistance", "_dashDuration", "_dashCooldown", "_hitInvulnerability"] },
      { title: "제한", keys: ["_maxLiveSpellEntities", "_maxFrameSteps", "_maxGraphNodes", "_maxGraphEdges", "_maxEnemies", "_maxCompiledActions", "_maxScheduledExecutions", "_maxActionsPerTick"] },
      { title: "RAM", keys: ["_mode", "_cooldownBase", "_cooldownPerRam"] },
      { title: "적응", keys: ["_elementLearning", "_formLearning", "_elementCap", "_formCap", "_dotWeight", "_noiseMultiplier", "_relayMultiplier", "_decayDelay", "_decayPerSecond", "_resistanceThreshold"] },
      { title: "전투와 방어", keys: ["_burnDps", "_burnInterval", "_burnSeconds", "_chillSlow", "_chillSeconds", "_chillMaxStacks", "_chillImmuneMaxStacks", "_freezeSeconds", "_freezeImmunity", "_empSeconds", "_aegisAngle", "_aegisReduction", "_relayRadius", "_relayReduction", "_separationAllowance", "_separationStrength", "_separationMaxStep"] },
      { title: "성장", keys: ["_slotCost", "_deathRetention", "_baseCapacity", "_maxLibrary", "_modifierStartStock", "_orbAbsorbRadius", "_pickupAttractSpeed", "_terminalHeal"] },
      { title: "시뮬레이션", keys: ["_tickRate", "_multiOffset", "_telemetryHighlightSeconds", "_benchPlayerX", "_benchPlayerY", "_benchDummyX", "_benchDummyY", "_benchHp", "_benchLineStartX", "_benchLineGap", "_benchLineCount", "_benchSwarmX", "_benchSwarmY", "_benchSwarmColumns", "_benchSwarmRows", "_benchSwarmGapX", "_benchSwarmGapY", "_spellVisualSeconds", "_hitFlashSeconds", "_damageNumberSeconds", "_wandOffset", "_areaPlacementRange", "_beamCooldownSeconds", "_executionTimeScale"] },
    ],
  },
};

// 휴지통 없는 select 보조 어휘다. _status처럼 빈 값이 의미 있는 선택지는 여기 둔다.
VOCAB.portStatus = ["active", "deprecated"];

// Modifier 등급 값의 형식이다. 게임(ModifierGradeTable)이 효과 종류마다 받는 값 형식과 같다. 없는 등급은 모두 null이다.
// decimal: 배율 소수, integer: 0 이상 정수, integerOrAll: 0 이상 정수 또는 "ALL"(무제한), homingTier: mod.homing의 유도 단계 이름.
const GRADE_VALUE_KINDS = {
  effectMultiplier: "decimal", energyCostMultiplier: "decimal", radiusMultiplier: "decimal", durationMultiplier: "decimal", speedMultiplier: "decimal", beamEmpowerMultiplier: "decimal",
  multipleCount: "integer", pierceCount: "integerOrAll", homingTier: "homingTier",
};
