using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace RuneCode
{
    [Serializable]
    public sealed class PlayerSave : ISerializationCallbackReceiver
    {
        private static readonly string[] STARTER_RUNE_IDS = { "shape.sphere", "element.neutral" };
        // 기본 마법 메소드 템플릿 ID다. 매직 미사일은 시작 마법 에너지 볼트와 같아 통합했으므로 지금은 비어 있다.
        private static readonly string[] DEFAULT_METHOD_IDS = Array.Empty<string>();

        [Header("저장 버전")]
        [SerializeField] private int _version = 5;

        [Header("완드 및 진행")]
        [SerializeField] private int _currency;
        [SerializeField] private int _upgradePoints;
        [SerializeField] private bool _hasPendingElementPick;
        [SerializeField] private int _slotCount = 1;
        [SerializeField] private int _highestClearedStage;
        [SerializeField] private int _selectedStage = 1;
        [SerializeField] private bool _hasClearedProgression;
        [SerializeField] private string[] _pendingProgressionChoiceIds;
        [SerializeField] private string[] _pendingProgressionChoiceGrades;
        [SerializeField] private string _activeSpellId;
        [SerializeField] private bool _sectorCleared;
        [SerializeField] private List<string> _unlockedRunes = new List<string>();
        [SerializeField] private List<SpellGraphData> _library = new List<SpellGraphData>();
        [SerializeField] private List<string> _loadout = new List<string>();
        [SerializeField] private List<KillRecord> _killCounts = new List<KillRecord>();
        [SerializeField] private List<UpgradeNodeProgress> _upgradeNodeProgress = new List<UpgradeNodeProgress>();
        [SerializeField] private List<ModifierStock> _modifierStock = new List<ModifierStock>();
        [SerializeField] private List<ModifierStock> _elementStock = new List<ModifierStock>();
        [SerializeField] private List<ModifierStock> _behaviorStock = new List<ModifierStock>();

        [Header("연속 미션 진행")]
        [SerializeField] private int _progressionStage;
        [SerializeField] private int _progressionRound;
        [SerializeField] private double _progressionHp;
        [SerializeField] private double _progressionMaxHp;
        [SerializeField] private string[] _pendingRoundChoiceIds;
        [SerializeField] private string[] _pendingRoundChoiceGrades;

        [Header("런 선택 보상 순서")]
        [SerializeField] private bool _usesFixedRewardGrades;
        [SerializeField] private string[] _modifierRewardGrades;
        [SerializeField] private string[] _behaviorRewardGrades;
        [SerializeField] private int _modifierRewardIndex;
        [SerializeField] private int _behaviorRewardIndex;
        public bool UsesFixedRewardGrades => _usesFixedRewardGrades;

        [Header("피드백 설정")]
        [SerializeField] private bool _screenShake = true;
        [SerializeField] private bool _hitStop = true;

        private List<SpellGraph> _graphs = new List<SpellGraph>();

        public int Version => _version;
        public int Currency => _currency;
        public int UpgradePoints => _upgradePoints;
        public bool HasPendingElementPick => _hasPendingElementPick;
        public int SlotCount => _slotCount;
        public int HighestClearedStage => _highestClearedStage;
        public bool HasClearedProgression => _hasClearedProgression;
        public IReadOnlyList<string> PendingProgressionChoiceIds => _pendingProgressionChoiceIds ?? Array.Empty<string>();
        public IReadOnlyList<string> PendingProgressionChoiceGrades => _pendingProgressionChoiceGrades ?? Array.Empty<string>();
        public int ProgressionStage => _progressionStage;
        public int ProgressionRound => _progressionRound;
        public bool HasProgressionRound => _progressionStage > 0 && _progressionRound > 0;
        public double ProgressionHp => _progressionHp;
        public double ProgressionMaxHp => _progressionMaxHp;
        public IReadOnlyList<string> PendingRoundChoiceIds => _pendingRoundChoiceIds ?? Array.Empty<string>();
        public IReadOnlyList<string> PendingRoundChoiceGrades => _pendingRoundChoiceGrades ?? Array.Empty<string>();
        public int SelectedStage => _selectedStage;
        public string ActiveSpellId => _activeSpellId;
        public bool SectorCleared => _sectorCleared;
        public IReadOnlyList<string> UnlockedRunes => _unlockedRunes;
        public IReadOnlyList<SpellGraph> Library => _graphs;
        public IReadOnlyList<string> Loadout => _loadout;
        public IReadOnlyList<KillRecord> KillCounts => _killCounts;
        public bool ScreenShake => _screenShake;
        public bool HitStop => _hitStop;

        /// <summary>Modifier 룬의 지정 문자열 등급 소지량(보관함 전체 공유)을 반환하고 기록이 없으면 0을 반환한다.</summary>
        public int GetModifierStock(string runeId, string grade)
        {
            int count = 0;
            foreach (ModifierStock stock in _modifierStock)
                if (stock != null && stock.RuneId == runeId && stock.Grade == grade) count += stock.Count;
            return count;
        }

        /// <summary>Modifier 룬 ID와 등급별 소지량을 등급 키 사전으로 반환한다. 컴파일·배치 검사에 쓴다.</summary>
        public Dictionary<string, int> CreateModifierStockMap()
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ModifierStock stock in _modifierStock)
            {
                if (stock == null || string.IsNullOrEmpty(stock.RuneId) || string.IsNullOrEmpty(stock.Grade)) continue;
                string key = SpellGrammar.ModifierStockKey(stock.RuneId, stock.Grade);
                map.TryGetValue(key, out int count);
                map[key] = count + stock.Count;
            }
            return map;
        }

        /// <summary>Modifier 룬의 지정 문자열 등급 소지량을 늘린다. 등급·룬이 유효하지 않거나 수량이 0 이하이면 무시한다.</summary>
        public void AddModifierStock(string runeId, string grade, int amount)
        {
            if (amount <= 0 || !GameData.Runes.TryGet(runeId, out RuneDefinition rune)
                || rune.Category != SpellGrammar.CATEGORY_MODIFIER || !GameData.ModifierGrades.IsGradeAvailable(runeId, grade)) return;
            foreach (ModifierStock stock in _modifierStock)
            {
                if (stock == null || stock.RuneId != runeId || stock.Grade != grade) continue;
                stock.Add(amount);
                return;
            }
            _modifierStock.Add(new ModifierStock(runeId, grade, amount));
        }

        /// <summary>
        /// 소지량 목록(모디·속성·행동)의 null과 잘못된 기록을 정리하고, 행동 소지량이 비어 있으면 behavior.launch 시작 소지량을 넣는다.
        /// 새 세이브와 이전 세이브 로드에서 호출하며 여러 번 호출해도 결과가 같다. 속성 시작 소지량은 없다.
        /// </summary>
        private void EnsureStock()
        {
            if (_modifierStock == null) _modifierStock = new List<ModifierStock>();
            _modifierStock.RemoveAll(stock => stock == null || !GameData.Runes.TryGet(stock.RuneId, out RuneDefinition rune)
                || rune.Category != SpellGrammar.CATEGORY_MODIFIER || (stock.HasGrade && !GameData.ModifierGrades.IsGradeAvailable(stock.RuneId, stock.Grade)));
            foreach (ModifierStock stock in _modifierStock)
                if (!stock.HasGrade) stock.SetGrade(GameData.ModifierGrades.GetLowestAvailableGrade(stock.RuneId));
            MergeDuplicateStock(_modifierStock);
            foreach (RuneDefinition rune in GameData.Runes.All)
            {
                string grade = GameData.ModifierGrades.GetLowestAvailableGrade(rune.Id);
                if (rune.Category != SpellGrammar.CATEGORY_MODIFIER
                    || _modifierStock.Exists(stock => stock.RuneId == rune.Id && stock.Grade == grade)) continue;
                _modifierStock.Add(new ModifierStock(rune.Id, grade, GameData.Balance.Economy.ModifierStartStock));
            }

            if (_elementStock == null) _elementStock = new List<ModifierStock>();
            _elementStock.RemoveAll(stock => stock == null || !GameData.Runes.TryGet(stock.RuneId, out RuneDefinition rune)
                || rune.Category != SpellGrammar.CATEGORY_ELEMENT || rune.IsDisabled
                || !GameData.Runes.TryGetRuneElement(stock.RuneId, out ElementDefinition element)
                || element.Category != SpellGrammar.ELEMENT_CATEGORY_ELEMENTAL || stock.HasGrade);
            MergeDuplicateStock(_elementStock);

            if (_behaviorStock == null) _behaviorStock = new List<ModifierStock>();
            _behaviorStock.RemoveAll(stock => stock == null || !GameData.Runes.TryGet(stock.RuneId, out RuneDefinition rune)
                || rune.Category != SpellGrammar.CATEGORY_BEHAVIOR || rune.IsDisabled
                || !stock.HasGrade || !GameData.Runes.BehaviorGradeOrder.Contains(stock.Grade));
            MergeDuplicateStock(_behaviorStock);
            if (_behaviorStock.Count == 0)
                _behaviorStock.Add(new ModifierStock("behavior.launch", SpellGrammar.DEFAULT_BEHAVIOR_GRADE,
                    GameData.Balance.Economy.BehaviorStartStock));
        }

        /// <summary>같은 (룬 ID, 등급)의 중복 기록을 첫 기록으로 합친다.</summary>
        private static void MergeDuplicateStock(List<ModifierStock> stocks)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < stocks.Count; index++)
            {
                ModifierStock stock = stocks[index];
                string key = SpellGrammar.ModifierStockKey(stock.RuneId, stock.Grade);
                if (seen.Add(key)) continue;
                ModifierStock first = stocks.Find(candidate => candidate != stock && candidate.RuneId == stock.RuneId && candidate.Grade == stock.Grade);
                first.Add(stock.Count);
                stocks.RemoveAt(index--);
            }
        }

        /// <summary>모디·속성·행동의 지정 (룬 ID, 등급) 소지량을 반환하고 기록이 없으면 0을 반환한다.</summary>
        public int GetStock(string runeId, string grade)
        {
            return GetModifierStock(runeId, grade)
                + CountStockList(_elementStock, runeId, grade)
                + CountStockList(_behaviorStock, runeId, grade);
        }

        /// <summary>
        /// 룬 카테고리에 맞는 소지량을 늘린다. 모디는 Modifier 지급 검사를, 속성은 비활성이 아니고 전투 속성이며 등급이 빈
        /// 문자열일 때만, 행동은 비활성이 아니고 등급이 등급표에 있을 때만 받는다. 그 외는 무시한다.
        /// </summary>
        public void AddStock(string runeId, string grade, int amount)
        {
            if (amount <= 0 || !GameData.Runes.TryGet(runeId, out RuneDefinition rune)) return;
            if (rune.Category == SpellGrammar.CATEGORY_MODIFIER)
            {
                AddModifierStock(runeId, grade, amount);
                return;
            }
            if (rune.IsDisabled) return;
            if (rune.Category == SpellGrammar.CATEGORY_ELEMENT)
            {
                if (!string.IsNullOrEmpty(grade) || !GameData.Runes.TryGetRuneElement(runeId, out ElementDefinition element)
                    || element.Category != SpellGrammar.ELEMENT_CATEGORY_ELEMENTAL) return;
                AddToStockList(_elementStock, runeId, grade, amount);
                return;
            }
            if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR)
            {
                if (string.IsNullOrEmpty(grade) || !GameData.Runes.BehaviorGradeOrder.Contains(grade)) return;
                AddToStockList(_behaviorStock, runeId, grade, amount);
            }
        }

        /// <summary>모든 요청 수량을 먼저 검사하고 행동·모드·전투 속성 재고에서 함께 차감한다. 부족하거나 잘못된 요청이면 변경하지 않는다.</summary>
        public bool TryConsumeStock(IReadOnlyDictionary<string, int> materials, string grade)
        {
            if (materials == null || materials.Count == 0) return false;
            foreach (KeyValuePair<string, int> pair in materials)
                if (pair.Value <= 0 || !GameData.Runes.TryGet(pair.Key, out RuneDefinition rune)
                    || !RuneSynthesisRules.IsEligible(rune, grade) || GetStock(pair.Key, grade) < pair.Value) return false;
            foreach (KeyValuePair<string, int> pair in materials)
            {
                List<ModifierStock> stocks = GameData.Runes.Get(pair.Key).Category == SpellGrammar.CATEGORY_MODIFIER
                    ? _modifierStock : GameData.Runes.Get(pair.Key).Category == SpellGrammar.CATEGORY_ELEMENT ? _elementStock : _behaviorStock;
                int remaining = pair.Value;
                foreach (ModifierStock stock in stocks)
                {
                    if (stock == null || stock.RuneId != pair.Key || stock.Grade != grade) continue;
                    int amount = Math.Min(remaining, stock.Count);
                    stock.Remove(amount);
                    remaining -= amount;
                    if (remaining == 0) break;
                }
            }
            return true;
        }

        /// <summary>모디·속성·행동 소지량을 (룬 ID, 등급) 키 하나로 합친 맵을 반환한다. 컴파일·배치 검사에 쓴다.</summary>
        public Dictionary<string, int> CreateStockMap()
        {
            Dictionary<string, int> map = CreateModifierStockMap();
            AppendStockMap(map, _elementStock);
            AppendStockMap(map, _behaviorStock);
            return map;
        }

        /// <summary>지정 (룬 ID, 등급) 기록의 소지량 합을 반환한다.</summary>
        private static int CountStockList(List<ModifierStock> stocks, string runeId, string grade)
        {
            if (stocks == null) return 0;
            var count = 0;
            foreach (ModifierStock stock in stocks)
                if (stock != null && stock.RuneId == runeId && stock.Grade == grade) count += stock.Count;
            return count;
        }

        /// <summary>같은 (룬 ID, 등급) 기록이 있으면 더하고 없으면 새 기록을 추가한다.</summary>
        private void AddToStockList(List<ModifierStock> stocks, string runeId, string grade, int amount)
        {
            foreach (ModifierStock stock in stocks)
            {
                if (stock == null || stock.RuneId != runeId || stock.Grade != grade) continue;
                stock.Add(amount);
                return;
            }
            stocks.Add(new ModifierStock(runeId, grade, amount));
        }

        /// <summary>소지량 목록의 (룬 ID, 등급) 키별 수를 맵에 합친다.</summary>
        private static void AppendStockMap(Dictionary<string, int> map, List<ModifierStock> stocks)
        {
            if (stocks == null) return;
            foreach (ModifierStock stock in stocks)
            {
                if (stock == null || string.IsNullOrEmpty(stock.RuneId)) continue;
                string key = SpellGrammar.ModifierStockKey(stock.RuneId, stock.Grade ?? "");
                map.TryGetValue(key, out int count);
                map[key] = count + stock.Count;
            }
        }

        /// <summary>저장된 업그레이드 트리 노드의 레벨을 반환하고 구매 기록이 없으면 0을 반환한다.</summary>
        public int GetUpgradeNodeLevel(string nodeId)
        {
            if (_upgradeNodeProgress == null) return 0;
            foreach (UpgradeNodeProgress progress in _upgradeNodeProgress)
                if (progress != null && progress.NodeId == nodeId) return progress.Level;
            return 0;
        }

        /// <summary>JSON 저장 직전에 실행 중 보관함 그래프를 직렬화 형식(_library)으로 옮긴다. JSON 키는 기존과 같다.</summary>
        public void OnBeforeSerialize()
        {
            _library = new List<SpellGraphData>(_graphs.Count);
            foreach (SpellGraph graph in _graphs)
            {
                _library.Add(SpellGraphData.From(graph));
            }
        }

        /// <summary>JSON을 읽은 직후 직렬화 형식(_library)을 실행 중 보관함 그래프로 되돌린다. 검증은 Validate가 수행한다.</summary>
        public void OnAfterDeserialize()
        {
            if (_upgradeNodeProgress == null) _upgradeNodeProgress = new List<UpgradeNodeProgress>();
            _graphs = new List<SpellGraph>();
            if (_library == null)
            {
                return;
            }
            foreach (SpellGraphData data in _library)
            {
                _graphs.Add(data?.ToGraph());
            }
        }

        /// <summary>시작 룬, 계속 강화할 에너지 볼트와 기본 마법 메소드를 가진 새 진행 데이터를 반환한다.</summary>
        public static PlayerSave CreateNew()
        {
            var save = new PlayerSave();
            save.AddStarterRunes();
            save._graphs.Add(GameData.Spells[0].Clone());
            save._activeSpellId = save._graphs[0].Id;
            save._loadout.Add(save._graphs[0].Id);
            save._loadout.Add(null);
            save._loadout.Add(null);
            save.AddDefaultMethods();
            save.EnsureStock();
            save._usesFixedRewardGrades = true;
            save._modifierRewardGrades = ShuffleRewardGrades(GameData.Balance.Economy.ModifierRewardGrades);
            save._behaviorRewardGrades = ShuffleRewardGrades(GameData.Balance.Economy.BehaviorRewardGrades);
            return save;
        }

        /// <summary>설정의 등급 목록을 복사하고 Fisher–Yates 방식으로 섞어 반환한다. 원본 설정은 변경하지 않는다.</summary>
        private static string[] ShuffleRewardGrades(IReadOnlyList<string> source)
        {
            var grades = new string[source.Count];
            for (int index = 0; index < source.Count; index++) grades[index] = source[index];
            for (int index = grades.Length - 1; index > 0; index--)
            {
                int other = UnityEngine.Random.Range(0, index + 1);
                string grade = grades[index];
                grades[index] = grades[other];
                grades[other] = grade;
            }
            return grades;
        }

        /// <summary>분류의 다음 고정 보상 등급을 소비하지 않고 반환한다. 기존 진행이거나 해당 목록을 모두 소비했으면 false다.</summary>
        public bool TryGetNextRewardGrade(string category, out string grade)
        {
            grade = null;
            if (!_usesFixedRewardGrades) return false;
            if (category != DropCatalog.TYPE_MODIFIER && category != DropCatalog.TYPE_BEHAVIOR)
                throw new ArgumentException("고정 보상은 행동 또는 모드 분류여야 합니다.", nameof(category));
            string[] grades = category == DropCatalog.TYPE_MODIFIER ? _modifierRewardGrades : _behaviorRewardGrades;
            int index = category == DropCatalog.TYPE_MODIFIER ? _modifierRewardIndex : _behaviorRewardIndex;
            if (index >= grades.Length) return false;
            grade = grades[index];
            return true;
        }

        /// <summary>제시한 분류·등급이 다음 고정 보상과 일치하면 한 칸 소비한다. 기존 진행은 유지하고 불일치는 오류로 보고한다.</summary>
        public void ConsumeRewardGrade(string category, string grade)
        {
            if (!_usesFixedRewardGrades) return;
            if (!TryGetNextRewardGrade(category, out string expected) || expected != grade)
                throw new InvalidOperationException("런 보상 등급 순서가 일치하지 않습니다.");
            if (category == DropCatalog.TYPE_MODIFIER) _modifierRewardIndex++;
            else _behaviorRewardIndex++;
        }

        /// <summary>디버그로 진입할 라운드 앞의 모든 보상 횟수까지 목록을 소비한다. 이전 라운드로 돌아가도 소비 위치를 되돌리지 않는다.</summary>
        public void SkipRewardGradesBefore(int stage, int round)
        {
            if (!_usesFixedRewardGrades) return;
            int modifierCount = 0;
            int behaviorCount = 0;
            for (int stageNumber = 1; stageNumber <= stage; stageNumber++)
            {
                int roundLimit = stageNumber == stage ? round - 1 : StageCatalog.PROGRESSION_ROUND_COUNT;
                for (int roundNumber = 1; roundNumber <= roundLimit; roundNumber++)
                {
                    string category = GameData.Stages.GetRoundRewardCategory(stageNumber, roundNumber, GameData.Drops);
                    if (category == DropCatalog.TYPE_MODIFIER) modifierCount++;
                    else behaviorCount++;
                }
            }
            _modifierRewardIndex = Math.Max(_modifierRewardIndex, Math.Min(modifierCount, _modifierRewardGrades.Length));
            _behaviorRewardIndex = Math.Max(_behaviorRewardIndex, Math.Min(behaviorCount, _behaviorRewardGrades.Length));
            ClearPendingRoundChoice();
        }

        /// <summary>저장된 목록의 수량·등급별 구성이 설정과 같고 소비 위치가 범위 안인지 반환한다.</summary>
        private static bool IsValidRewardGrades(string[] grades, int index, IReadOnlyList<string> configured)
        {
            if (grades == null || grades.Length != configured.Count || index < 0 || index > grades.Length) return false;
            var remaining = new List<string>(configured);
            foreach (string grade in grades)
                if (!remaining.Remove(grade)) return false;
            return remaining.Count == 0;
        }

        /// <summary>기본 시작 룬을 해금 목록에 중복 없이 추가한다.</summary>
        private void AddStarterRunes()
        {
            if (_unlockedRunes == null) _unlockedRunes = new List<string>();
            foreach (string runeId in STARTER_RUNE_IDS)
                if (GameData.Runes.TryGet(runeId, out RuneDefinition rune)
                    && rune.Category != SpellGrammar.CATEGORY_INTERNAL && !_unlockedRunes.Contains(runeId))
                    _unlockedRunes.Add(runeId);
        }

        /// <summary>
        /// 보관함 그래프의 행동 노드에서 이전 형태 선택 파라미터(shape)를 지운다. 형태는 행동 정의의 고정 형태를 쓰므로
        /// 저장된 값은 실행에 쓰이지 않는다. 여러 번 호출해도 결과가 같다.
        /// </summary>
        private void RemoveLegacyShapeParameters()
        {
            foreach (SpellGraph graph in _graphs)
                foreach (GraphNode node in graph.Nodes)
                    if (GameData.Runes.TryGet(node.RuneId, out RuneDefinition rune) && rune.Category == SpellGrammar.CATEGORY_BEHAVIOR)
                        node.RemoveParameter(SpellGrammar.LEGACY_SHAPE_PARAM);
        }

        /// <summary>
        /// 보관함 그래프에서 인식할 수 없거나 행동과 호환되지 않는 항목을 지운다. 알 수 없는 룬의 노드는 연결선과 함께 지우고,
        /// 행동 노드의 속성·모디 슬롯이 알 수 없는 룬이나 호환되지 않는 모드를 가리키면 그 슬롯 값만 비운다. 그래프와 나머지 노드는 유지한다.
        /// 지운 항목이 있으면 개수를 경고 로그로 남기며 여러 번 호출해도 결과가 같다.
        /// </summary>
        private void RemoveUnknownGraphEntries()
        {
            int removedNodes = 0;
            int clearedSlots = 0;
            var unknownNodeIds = new List<string>();
            foreach (SpellGraph graph in _graphs)
            {
                unknownNodeIds.Clear();
                foreach (GraphNode node in graph.Nodes)
                {
                    if (!GameData.Runes.TryGet(node.RuneId, out RuneDefinition rune)) unknownNodeIds.Add(node.Id);
                    else if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR) clearedSlots += ClearUnknownSlots(node);
                }
                foreach (string nodeId in unknownNodeIds)
                    if (graph.RemoveNode(nodeId)) removedNodes++;
            }
            if (removedNodes > 0 || clearedSlots > 0)
                Debug.LogWarning("[Save] 인식할 수 없거나 행동과 호환되지 않는 항목을 정리했습니다. 노드 " + removedNodes + "개, 슬롯 " + clearedSlots + "개");
        }

        /// <summary>행동 노드의 속성·모디 슬롯 중 카탈로그에 없거나 행동과 호환되지 않는 값을 지우고 지운 슬롯 수를 반환한다.</summary>
        private static int ClearUnknownSlots(GraphNode node)
        {
            int cleared = 0;
            for (int index = 0; index < SpellGrammar.MAX_ELEMENT_SLOTS; index++)
            {
                string slotKey = SpellGrammar.ElementSlotKey(index);
                string value = node.GetText(slotKey, null);
                if (string.IsNullOrEmpty(value) || GameData.Runes.TryGetElement(value, out _)) continue;
                node.RemoveParameter(slotKey);
                cleared++;
            }
            for (int index = 0; index < SpellGrammar.MAX_MODIFIER_SLOTS; index++)
            {
                string slotKey = SpellGrammar.ModifierSlotKey(index);
                string value = node.GetText(slotKey, null);
                if (string.IsNullOrEmpty(value)
                    || (SpellGrammar.TryParseStockKey(value, out string modifierId, out _)
                        && GameData.Runes.TryGet(modifierId, out _)
                        && GameData.Runes.IsModifierCompatible(modifierId, node.RuneId))) continue;
                node.RemoveParameter(slotKey);
                cleared++;
            }
            return cleared;
        }

        /// <summary>
        /// 스키마 버전 4가 아닌 보관함 그래프를 제거하고(호환 없음), 인식할 수 없거나 행동과 호환되지 않는 룬의 노드·슬롯 값과 행동 노드의 이전 형태 파라미터를 지우며,
        /// 해금 룬 ID를 현재 ID로 바꾸며 카탈로그에 없는 ID만 정리한다. 보관함이 비면 시작 마법으로 채우고, 활성 마법과 장착 슬롯 참조가
        /// 제거된 마법을 가리키면 유효한 값으로 정리한다. 기본 마법 메소드가 없으면 보관함 한도 안에서 추가한다.
        /// 여러 번 호출해도 결과가 같다.
        /// </summary>
        public void MigrateSpellGrammar()
        {
            if (_graphs == null || _unlockedRunes == null) return;
            _graphs.RemoveAll(graph => graph == null || graph.Version != SpellGraph.CURRENT_VERSION);
            RemoveUnknownGraphEntries();
            RemoveLegacyShapeParameters();
            foreach (SpellGraph graph in _graphs) SpellGraphMigration.ReplaceRemovedDashEndTrigger(graph);
            if (_graphs.Count == 0) _graphs.Add(GameData.Spells[0].Clone());
            var unlocked = new List<string>();
            foreach (var runeId in _unlockedRunes)
            {
                foreach (var mapped in SpellGraphMigration.MapUnlockedRune(runeId))
                    if (GameData.Runes.TryGet(mapped, out var rune) && rune.Category != SpellGrammar.CATEGORY_INTERNAL && !unlocked.Contains(mapped)) unlocked.Add(mapped);
            }
            foreach (string runeId in STARTER_RUNE_IDS)
                if (GameData.Runes.TryGet(runeId, out RuneDefinition rune)
                    && rune.Category != SpellGrammar.CATEGORY_INTERNAL && !unlocked.Contains(runeId)) unlocked.Add(runeId);
            _unlockedRunes = unlocked;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var graph in _graphs) ids.Add(graph.Id);
            if (string.IsNullOrEmpty(_activeSpellId) || !ids.Contains(_activeSpellId)) _activeSpellId = _graphs[0].Id;
            if (_loadout == null) _loadout = new List<string>();
            while (_loadout.Count < 3) _loadout.Add(null);
            _loadout[0] = _activeSpellId;
            for (var slot = 1; slot < _loadout.Count; slot++)
                if (!string.IsNullOrEmpty(_loadout[slot]) && !ids.Contains(_loadout[slot])) _loadout[slot] = null;
            AddDefaultMethods();
            EnsureStock();
        }

        /// <summary>기본 마법 메소드 템플릿 중 보관함에 없는 것을 한도 안에서 추가한다.</summary>
        private void AddDefaultMethods()
        {
            foreach (var id in DEFAULT_METHOD_IDS)
            {
                if (FindGraph(id) != null || _graphs.Count >= GameData.Balance.Economy.MaxLibrary) continue;
                var template = GameData.FindSpell(id);
                if (template != null) _graphs.Add(template.Clone());
            }
        }

        /// <summary>저장된 값과 룬·마법 참조를 검사하고 유효하지 않은 저장이면 원인을 반환한다.</summary>
        public bool Validate(out string error)
        {
            error = null;
            var economy = GameData.Balance.Economy;
            if (_usesFixedRewardGrades
                && (!IsValidRewardGrades(_modifierRewardGrades, _modifierRewardIndex, economy.ModifierRewardGrades)
                    || !IsValidRewardGrades(_behaviorRewardGrades, _behaviorRewardIndex, economy.BehaviorRewardGrades)))
            {
                error = "save.invalidValues";
                return false;
            }
            if (_version != 5 || _currency < 0 || _currency > 100000000 || _upgradePoints < 0 ||
                _highestClearedStage < 0 || _highestClearedStage >= 1000000 ||
                _selectedStage < 1 || _selectedStage > _highestClearedStage + 1 ||
                _slotCount != 1)
            {
                error = "save.invalidValues";
                return false;
            }
            if (PendingProgressionChoiceIds.Count != PendingProgressionChoiceGrades.Count
                || PendingProgressionChoiceIds.Count > 0 && !_hasClearedProgression)
            {
                error = "save.invalidValues";
                return false;
            }
            for (int index = 0; index < PendingProgressionChoiceIds.Count; index++)
            {
                string runeId = PendingProgressionChoiceIds[index];
                if (!RuneRewardRoller.IsCandidateGradeAvailable(GameData.Runes, GameData.ModifierGrades,
                    runeId, PendingProgressionChoiceGrades[index]))
                {
                    error = "save.invalidValues";
                    return false;
                }
            }
            if ((_progressionStage != 0 || _progressionRound != 0)
                && (_progressionStage < 1 || _progressionStage > StageCatalog.PROGRESSION_STAGE_COUNT
                    || _progressionRound < 1 || _progressionRound > StageCatalog.PROGRESSION_ROUND_COUNT))
            {
                error = "save.invalidValues";
                return false;
            }
            if (HasProgressionRound && (_progressionMaxHp <= 0 || _progressionHp <= 0 || _progressionHp > _progressionMaxHp))
            {
                error = "save.invalidValues";
                return false;
            }
            if (_pendingRoundChoiceIds != null || _pendingRoundChoiceGrades != null)
            {
                if (_pendingRoundChoiceIds == null || _pendingRoundChoiceGrades == null
                    || _pendingRoundChoiceIds.Length != _pendingRoundChoiceGrades.Length || !HasProgressionRound)
                {
                    error = "save.invalidValues";
                    return false;
                }
                for (int index = 0; index < _pendingRoundChoiceIds.Length; index++)
                {
                    string runeId = _pendingRoundChoiceIds[index];
                    if (!RuneRewardRoller.IsCandidateGradeAvailable(GameData.Runes, GameData.ModifierGrades,
                        runeId, _pendingRoundChoiceGrades[index]))
                    {
                        error = "save.invalidValues";
                        return false;
                    }
                }
            }
            if (_graphs == null || _graphs.Count == 0 || _graphs.Count > economy.MaxLibrary ||
                _loadout == null || _loadout.Count != 3 || _unlockedRunes == null || _killCounts == null || _upgradeNodeProgress == null)
            {
                error = "save.invalidStructure";
                return false;
            }
            var ids = new HashSet<string>();
            foreach (var graph in _graphs)
            {
                if (graph == null || string.IsNullOrWhiteSpace(graph.Id) || !ids.Add(graph.Id))
                {
                    error = "save.invalidStructure";
                    return false;
                }
                try
                {
                    ShareCodec.Deserialize(ShareCodec.Serialize(graph));
                    foreach (var node in graph.Nodes)
                        if (!GameData.Runes.TryGet(node.RuneId, out _))
                        { error = "save.invalidStructure"; return false; }
                }
                catch (Exception exception) when (exception is FormatException || exception is ArgumentException)
                { error = "save.invalidStructure"; return false; }
            }
            foreach (var runeId in _unlockedRunes)
                if (!GameData.Runes.TryGet(runeId, out _))
                {
                    error = "save.invalidStructure";
                    return false;
                }
            var upgradeNodeIds = new HashSet<string>();
            foreach (UpgradeNodeProgress progress in _upgradeNodeProgress)
                if (progress == null || string.IsNullOrWhiteSpace(progress.NodeId) || progress.NodeId.Length > 80 ||
                    progress.Level < 1 || progress.Level > 1000 || !upgradeNodeIds.Add(progress.NodeId))
                { error = "save.invalidStructure"; return false; }
            var killIds = new HashSet<string>();
            foreach (var record in _killCounts)
                if (record == null || string.IsNullOrWhiteSpace(record.Id) || record.Id.Length > 80 || record.Count < 0 || !killIds.Add(record.Id))
                { error = "save.invalidStructure"; return false; }
            for (var slot = 0; slot < _loadout.Count; slot++)
                if (!string.IsNullOrEmpty(_loadout[slot]) && (!ids.Contains(_loadout[slot]) || slot >= _slotCount))
                {
                    error = "save.invalidStructure";
                    return false;
                }
            if (string.IsNullOrEmpty(_activeSpellId) || !ids.Contains(_activeSpellId) || _loadout[0] != _activeSpellId)
            { error = "save.invalidStructure"; return false; }
            return true;
        }

        /// <summary>버전 1의 마법 슬롯을 단일 활성 설계로 이전해 버전 2 저장 형태로 바꾼다.</summary>
        public void MigrateToIncremental()
        {
            if (_version != 1) return;
            var economy = GameData.Balance.Economy;
            if (_slotCount < 2 || _slotCount > 3 || _loadout == null || _loadout.Count != 3 ||
                _graphs == null || _graphs.Count == 0 || _currency < 0 || _currency > 100000000)
                throw new FormatException("이전 버전 저장 구조가 유효하지 않습니다.");
            _activeSpellId = !string.IsNullOrEmpty(_loadout[0]) && _graphs.Any(graph => graph != null && graph.Id == _loadout[0])
                ? _loadout[0] : _graphs[0]?.Id;
            if (_slotCount == 3) _currency = Math.Min(100000000, _currency + economy.SlotCost);
            _slotCount = 1;
            _loadout[0] = _activeSpellId; _loadout[1] = null; _loadout[2] = null;
            _highestClearedStage = _sectorCleared ? 1 : 0;
            _selectedStage = _highestClearedStage + 1;
            _version = 2;
        }

        /// <summary>버전 2 저장의 구형 강화와 트리 구매를 초기화하고 기본 시작 룬만 남겨 버전 3으로 이전한다.</summary>
        public void MigrateToHardwareTree()
        {
            if (_version != 2) return;
            _upgradeNodeProgress = new List<UpgradeNodeProgress>();
            _unlockedRunes = new List<string>();
            AddStarterRunes();
            _version = 3;
        }

        /// <summary>버전 3의 기존 트리 구매와 비시작 룬 해금을 초기화하고 스크랩·마법·스테이지 진행을 보존해 버전 4로 이전한다.</summary>
        public void MigrateToUpgradeTreeV4()
        {
            if (_version != 3) return;
            _upgradeNodeProgress = new List<UpgradeNodeProgress>();
            _unlockedRunes = new List<string>();
            AddStarterRunes();
            _version = 4;
        }

        /// <summary>버전 4 저장을 버전 5로 이전한다. 온보딩 단계 데이터는 더 이상 저장하지 않으므로 다른 값 변경은 없다.</summary>
        public void MigrateToOnboarding()
        {
            if (_version != 4) return;
            _version = 5;
        }

        /// <summary>전투 스테이지 선택 상태를 변경한다. canSelectLockedStage가 참이면 해금 범위 제한을 무시한다.</summary>
        public void SelectStage(int stage, bool canSelectLockedStage = false)
        { if (stage >= 1 && (canSelectLockedStage || stage <= _highestClearedStage + 1)) _selectedStage = stage; }

        /// <summary>제한시간을 완주한 스테이지 기록을 갱신하고 다음 스테이지를 선택한다.</summary>
        public void RecordStageClear(int stage)
        {
            if (stage < 1 || stage > _highestClearedStage + 1 || stage >= 1000000) return;
            _highestClearedStage = Math.Max(_highestClearedStage, stage);
            _selectedStage = _highestClearedStage + 1;
        }

        /// <summary>연속 미션의 최종 클리어만 별도로 기록하고 기존 선택 스테이지·해금 기록은 유지한다.</summary>
        public void RecordProgressionClear() => _hasClearedProgression = true;

        /// <summary>첫 연속 미션 완주에서 선택하지 않은 보상 후보 ID와 등급을 저장한다.</summary>
        public void SetPendingProgressionChoice(IReadOnlyList<string> ids, IReadOnlyList<string> grades)
        {
            _pendingProgressionChoiceIds = new string[ids.Count];
            _pendingProgressionChoiceGrades = new string[grades.Count];
            for (int index = 0; index < ids.Count; index++) _pendingProgressionChoiceIds[index] = ids[index];
            for (int index = 0; index < grades.Count; index++) _pendingProgressionChoiceGrades[index] = grades[index];
        }

        /// <summary>선택 보상을 지급한 뒤 대기 중인 후보와 등급을 비운다.</summary>
        public void ClearPendingProgressionChoice()
        {
            _pendingProgressionChoiceIds = null;
            _pendingProgressionChoiceGrades = null;
        }

        /// <summary>마지막으로 끝낸 연속 미션 라운드 위치와 그 시점 체력, 라운드 보상 후보를 저장한다. 후보가 없거나 개수가 맞지 않으면 후보 기록을 비운다.</summary>
        public void RecordProgressionRound(int stage, int round, double hp, double maxHp, IReadOnlyList<string> choiceIds, IReadOnlyList<string> choiceGrades)
        {
            _progressionStage = stage;
            _progressionRound = round;
            _progressionHp = hp;
            _progressionMaxHp = maxHp;
            if (choiceIds == null || choiceGrades == null || choiceIds.Count == 0 || choiceIds.Count != choiceGrades.Count)
            {
                _pendingRoundChoiceIds = null;
                _pendingRoundChoiceGrades = null;
                return;
            }
            _pendingRoundChoiceIds = new string[choiceIds.Count];
            _pendingRoundChoiceGrades = new string[choiceGrades.Count];
            for (int index = 0; index < choiceIds.Count; index++) _pendingRoundChoiceIds[index] = choiceIds[index];
            for (int index = 0; index < choiceGrades.Count; index++) _pendingRoundChoiceGrades[index] = choiceGrades[index];
        }

        /// <summary>라운드 보상 후보를 지급한 뒤 대기 후보를 비운다. 라운드 위치와 체력 기록은 유지한다.</summary>
        public void ClearPendingRoundChoice()
        {
            _pendingRoundChoiceIds = null;
            _pendingRoundChoiceGrades = null;
        }

        /// <summary>ID와 일치하는 보관함 마법을 반환하며 없으면 null을 반환한다.</summary>
        public SpellGraph FindGraph(string id)
        {
            return _graphs.FirstOrDefault(graph => graph.Id == id);
        }

        /// <summary>라이브러리에 존재하는 Spell ID를 활성 편집·첫 슬롯 참조로 설정한다.</summary>
        public bool SetActiveGraph(string id)
        {
            if (FindGraph(id) == null) return false;
            _activeSpellId = id;
            _loadout[0] = id;
            return true;
        }

        /// <summary>마법을 복사하여 보관함에 추가하거나 같은 ID의 저장본을 갱신한다.</summary>
        public bool StoreGraph(SpellGraph graph)
        {
            var index = _graphs.FindIndex(item => item.Id == graph.Id);
            if (index < 0)
            {
                if (_graphs.Count >= GameData.Balance.Economy.MaxLibrary) return false;
                _graphs.Add(graph.Clone());
            }
            else _graphs[index] = graph.Clone();
            return true;
        }

        /// <summary>ID의 마법을 삭제하고 해당 마법을 참조하는 장착 슬롯을 비운다.</summary>
        public void RemoveGraph(string id)
        {
            if (id == _activeSpellId) return;
            _graphs.RemoveAll(graph => graph.Id == id);
            for (var slot = 0; slot < _loadout.Count; slot++)
                if (_loadout[slot] == id) _loadout[slot] = null;
        }

        /// <summary>단일 활성 마법과 일치하는 ID만 첫 슬롯에 유지한다.</summary>
        public void SetLoadout(int slot, string id)
        {
            if (slot != 0 || id != _activeSpellId) return;
            _loadout[0] = _activeSpellId;
        }

        /// <summary>비용을 지불할 수 있으면 조각을 차감하고 성공 여부를 반환한다.</summary>
        public bool Spend(int amount)
        {
            if (amount < 0 || _currency < amount) return false;
            _currency -= amount;
            return true;
        }

        /// <summary>라운드 보상 확정으로 지급된 강화 포인트를 누적한다. 0 이하 수량은 무시한다.</summary>
        public void GrantUpgradePoints(int amount)
        {
            if (amount <= 0) return;
            _upgradePoints += amount;
        }

        /// <summary>강화 포인트가 충분하면 차감하고 성공 여부를 반환한다. 부족하면 상태를 바꾸지 않는다.</summary>
        public bool TrySpendUpgradePoints(int amount)
        {
            if (amount < 0 || _upgradePoints < amount) return false;
            _upgradePoints -= amount;
            return true;
        }

        /// <summary>강화 트리의 속성 선택 노드 구매로 속성 선택 보상 대기를 설정한다.</summary>
        public void SetElementPickPending() => _hasPendingElementPick = true;

        /// <summary>속성 선택 보상을 지급한 뒤 대기 상태를 해제한다.</summary>
        public void ClearElementPickPending() => _hasPendingElementPick = false;

        /// <summary>유효한 노드 ID의 구매 단계를 저장하고 0단계가 되면 저장 항목을 제거한다.</summary>
        public void SetUpgradeNodeLevel(string nodeId, int level)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || nodeId.Length > 80 || level < 0) return;
            if (_upgradeNodeProgress == null) _upgradeNodeProgress = new List<UpgradeNodeProgress>();
            UpgradeNodeProgress progress = _upgradeNodeProgress.Find(item => item != null && item.NodeId == nodeId);
            if (level == 0)
            {
                if (progress != null) _upgradeNodeProgress.Remove(progress);
                return;
            }
            if (progress == null) _upgradeNodeProgress.Add(new UpgradeNodeProgress(nodeId, level));
            else progress.SetLevel(level);
        }

        /// <summary>양수 스크랩 보상을 누적하고 시간제 전투 완주 기록을 남긴다.</summary>
        public void Settle(int fragments, bool cleared)
        {
            _currency = (int)Math.Min(100000000L, _currency + (long)Math.Max(0, fragments));
            if (cleared) _sectorCleared = true;
        }

        /// <summary>처치한 적 ID별 기록에 전달된 횟수를 더한다.</summary>
        public void RecordKills(string enemyId, int count)
        {
            var record = _killCounts.FirstOrDefault(item => item.Id == enemyId);
            if (record == null)
            {
                record = new KillRecord(enemyId);
                _killCounts.Add(record);
            }
            record.Add(count);
        }

        /// <summary>유효한 룬 ID를 중복 없이 해금 목록에 추가한다.</summary>
        public void Unlock(string runeId)
        {
            if (GameData.Runes.TryGet(runeId, out _) && !_unlockedRunes.Contains(runeId))
                _unlockedRunes.Add(runeId);
        }

        [Serializable]
        private sealed class ModifierStock
        {
            [SerializeField] private string _runeId;
            [SerializeField] private string _grade;
            [SerializeField] private int _count;

            public string RuneId => _runeId;
            public string Grade => _grade;
            public int Count => _count;
            public bool HasGrade => !string.IsNullOrEmpty(_grade);

            /// <summary>저장 직렬화에서 소지량 목록을 복원하기 위한 기본 생성자다.</summary>
            private ModifierStock()
            {
            }

            /// <summary>Modifier 룬 ID, 문자열 등급과 소지량으로 기록을 만든다.</summary>
            public ModifierStock(string runeId, string grade, int count)
            {
                _runeId = runeId;
                _grade = grade;
                _count = Math.Max(0, count);
            }

            /// <summary>등급이 없는 이전 기록을 지정된 문자열 등급으로 표시한다.</summary>
            public void SetGrade(string grade)
            {
                _grade = grade;
            }

            /// <summary>소지량을 늘린다.</summary>
            public void Add(int amount)
            {
                _count += Math.Max(0, amount);
            }

            /// <summary>검증된 양수 수량을 차감하고 기록을 유지해 로드 시 시작 재고가 재지급되지 않게 한다.</summary>
            public void Remove(int amount)
            {
                _count = Math.Max(0, _count - Math.Max(0, amount));
            }
        }

        [Serializable]
        private sealed class UpgradeNodeProgress
        {
            [SerializeField] private string _nodeId;
            [SerializeField] private int _level;

            public string NodeId => _nodeId;
            public int Level => _level;

            /// <summary>저장 직렬화에서 노드 진행 목록을 복원하기 위한 기본 생성자다.</summary>
            private UpgradeNodeProgress()
            {
            }

            /// <summary>새 업그레이드 노드 ID와 구매 레벨을 저장한다.</summary>
            public UpgradeNodeProgress(string nodeId, int level)
            {
                _nodeId = nodeId;
                _level = level;
            }

            /// <summary>저장된 노드 레벨을 새 구매 단계로 갱신한다.</summary>
            public void SetLevel(int level)
            {
                _level = level;
            }
        }

        /// <summary>화면 흔들림과 히트스톱의 사용자 설정을 갱신한다.</summary>
        public void SetFeedback(bool screenShake, bool hitStop)
        {
            _screenShake = screenShake;
            _hitStop = hitStop;
        }
    }

    [Serializable]
    public sealed class KillRecord
    {
        [Header("처치 기록")]
        [SerializeField] private string _id;
        [SerializeField] private int _count;
        public string Id => _id;
        public int Count => _count;

        /// <summary>적 ID를 사용해 빈 처치 기록을 만든다.</summary>
        public KillRecord(string id) { _id = id; }

        /// <summary>양수 처치 횟수를 누적한다.</summary>
        public void Add(int count) { _count += Math.Max(0, count); }
    }
}
