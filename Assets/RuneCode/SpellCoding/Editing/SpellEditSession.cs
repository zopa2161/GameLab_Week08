using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 마법 그래프 편집 상태(편집 사본, 되돌리기 기록, 복사 버퍼, 컴파일 결과)를 소유하고
    /// ISpellEditor 편집 명령을 저장소와 편집 규칙으로 수행한다.
    /// </summary>
    public sealed class SpellEditSession : ISpellEditor
    {
        private const int HISTORY_LIMIT = 50;
        private const float COMPILE_DEBOUNCE = 0.2f;

        private readonly ISpellStorage _storage;
        private readonly ISpellEditPolicy _policy;
        private readonly List<SpellGraph> _undo = new List<SpellGraph>();
        private readonly List<SpellGraph> _redo = new List<SpellGraph>();
        private readonly List<GraphNode> _copiedNodes = new List<GraphNode>();
        private readonly List<GraphEdge> _copiedEdges = new List<GraphEdge>();

        private SpellGraph _editingGraph;
        private SpellGraph _previousGraph;
        private CompileResult _compileResult;
        private int _nextId;
        private bool _isEditable;
        private bool _graphDirty;
        private float _compileAt;

        public string SpellId => _editingGraph.Id;
        public string SpellName => _editingGraph.Name;
        public CompileResult CompileResult => _compileResult;
        public SpellGraph Graph => _editingGraph;
        public IReadOnlyList<SpellGraph> Library => _storage.Library;
        public string ActiveSpellId => _storage.ActiveSpellId;
        public bool IsEditable => _isEditable;

        /// <summary>컴파일 결과가 갱신될 때마다 호출된다.</summary>
        public event Action Compiled;

        /// <summary>노드·엣지·파라미터가 바뀌어 그래프 표시만 갱신하면 될 때 호출된다.</summary>
        public event Action GraphChanged;

        /// <summary>편집 대상 마법이 바뀌거나 이름 변경·가져오기처럼 패널 전체 갱신이 필요할 때 호출된다.</summary>
        public event Action SpellSwitched;

        /// <summary>저장소와 편집 규칙을 받아 활성 마법의 편집 사본을 연다.</summary>
        public SpellEditSession(ISpellStorage storage, ISpellEditPolicy policy)
        {
            _storage = storage;
            _policy = policy;
            _nextId = storage.Library.Count + 1;
            _editingGraph = storage.Find(storage.ActiveSpellId).Clone();
            _previousGraph = _editingGraph.Clone();
            Recompile();
        }

        /// <summary>작업실 에디터 탭 활성 여부에 따라 편집 허용 상태를 바꾼다.</summary>
        public void SetEditable(bool isEditable)
        {
            _isEditable = isEditable;
        }

        /// <summary>디바운스 예약된 컴파일과 저장을 전달된 현재 시간 기준으로 처리한다.</summary>
        public void Tick(float unscaledTime)
        {
            if (!_graphDirty || unscaledTime < _compileAt) return;
            _graphDirty = false;
            Recompile();
            Save();
            RaiseGraphChanged();
        }

        /// <summary>룬이 해금되어 팔레트에서 배치 가능한지 반환한다.</summary>
        public bool IsRuneUnlocked(string runeId)
        {
            return _policy.IsRuneUnlocked(runeId);
        }

        /// <summary>편집 중인 마법을 포함한 보관함 전체에서 지정 등급 Modifier 배치 수와 소지량을 구한다.</summary>
        public bool TryGetModifierUsage(string runeId, string grade, out int used, out int owned)
        {
            return _policy.TryGetModifierUsage(runeId, grade, _editingGraph, out used, out owned);
        }

        /// <summary>현재 진행 상태로 편집 마법을 다시 컴파일하고 결과를 알린다.</summary>
        public void Recompile()
        {
            var context = _policy.GetCompileContext();
            _compileResult = GraphCompiler.Compile(_editingGraph, GameData.Runes, GameData.Balance.Grammar,
                context.UnlockedRunes, context.Capacity, context.MaxEnergy, context.Library, context.ModifierStock,
                context.MaxResourceCosts);
            RaiseCompiled();
        }

        /// <summary>보관함의 마법을 편집 사본으로 열고 활성 마법을 바꾼다.</summary>
        public void SelectSpell(string id)
        {
            if (!_isEditable) return;
            Save();
            var graph = _storage.Find(id);
            if (graph == null) return;
            _storage.SetActive(id);
            _editingGraph = graph.Clone();
            _undo.Clear();
            _redo.Clear();
            _previousGraph = _editingGraph.Clone();
            Recompile();
            RaiseSpellSwitched();
        }

        /// <summary>보관함 한도 안에서 새 마법을 만들어 편집 대상으로 연다.</summary>
        public void NewSpell()
        {
            if (!_isEditable) return;
            if (_storage.Library.Count >= _policy.MaxLibrary) { _policy.ReportStatus("editor.libraryFull"); return; }
            SpellGraph graph = SpellGraph.Create(NewId("spell"), GameData.L("editor.newSpell") + " " + (_storage.Library.Count + 1));
            _storage.Store(graph);
            _storage.SetActive(graph.Id);
            _editingGraph = graph.Clone();
            _undo.Clear();
            _redo.Clear();
            _previousGraph = _editingGraph.Clone();
            Recompile();
            _storage.Persist(out _);
            RaiseSpellSwitched();
        }

        /// <summary>현재 그래프를 고유 ID와 표시 이름을 가진 독립 마법으로 복제한다. 복제로 Modifier 소지량을 넘으면 거부한다.</summary>
        public void DuplicateSpell()
        {
            if (!_isEditable || _storage.Library.Count >= _policy.MaxLibrary) return;
            SpellGraph duplicate = _editingGraph.Clone();
            duplicate.SetIdentity(NewId("spell"));
            if (!_policy.IsWithinModifierStock(null, duplicate)) { _policy.ReportStatus("editor.modifierStockBlocked"); return; }
            duplicate.Rename(_editingGraph.Name + GameData.L("editor.copySuffix"));
            _storage.Store(duplicate);
            SelectSpell(duplicate.Id);
        }

        /// <summary>활성 마법 또는 마지막 라이브러리 항목 삭제를 거부한다.</summary>
        public void DeleteSpell()
        {
            if (!_isEditable || _storage.Library.Count <= 1 || _editingGraph.Id == _storage.ActiveSpellId) return;
            _storage.Remove(_editingGraph.Id);
            _storage.Persist(out _);
            SelectSpell(_storage.ActiveSpellId);
        }

        /// <summary>공백이 아닌 이름을 제한된 길이로 현재 마법에 반영한다.</summary>
        public void Rename(string name)
        {
            if (!_isEditable || string.IsNullOrWhiteSpace(name)) return;
            _editingGraph.Rename(name.Trim().Substring(0, Math.Min(40, name.Trim().Length)));
            MarkChanged(); RaiseSpellSwitched();
        }

        /// <summary>현재 그래프 버전에 맞는 RC1 또는 RC2 공유 문자열을 반환한다.</summary>
        public string Export()
        {
            return ShareCodec.Encode(_editingGraph);
        }

        /// <summary>공유 코드를 현재 마법에 반영하고 실패 이유를 상태 문구로 표시한 뒤 반환한다.</summary>
        public string Import(string code)
        {
            var error = ImportGraph(code);
            if (!string.IsNullOrEmpty(error)) _policy.ReportStatusText(error);
            return error;
        }

        /// <summary>편집 사본을 보관함에 반영하고 저장 파일에 기록한다.</summary>
        public void Save()
        {
            if (_editingGraph == null) return;
            _storage.Store(_editingGraph);
            if (!_storage.Persist(out var warning)) _policy.ReportStatusText(warning);
        }

        /// <summary>세이브 초기화 후 활성 마법으로 편집 사본·되돌리기·복사 버퍼 상태를 다시 연다. 세션만 호출한다.</summary>
        public void Reload()
        {
            _editingGraph = _storage.Find(_storage.ActiveSpellId).Clone();
            _previousGraph = _editingGraph.Clone();
            _undo.Clear();
            _redo.Clear();
            _copiedNodes.Clear();
            _copiedEdges.Clear();
            _graphDirty = false;
            Recompile();
            RaiseSpellSwitched();
        }

        /// <summary>
        /// 행동 노드를 지정 등급(비면 기본 등급)과 기본 형태로 지정 좌표에 배치한다. 행동 외 룬, 등급표에 없는 등급,
        /// 행동 소지량 초과는 배치하지 않는다. RAM은 연결될 때 검사한다.
        /// </summary>
        public void AddRune(string runeId, float x, float y, string grade = null)
        {
            if (!_isEditable || !_policy.IsRuneUnlocked(runeId)) return;
            if (!GameData.Runes.TryGet(runeId, out RuneDefinition rune) || rune.IsDisabled
                || rune.Category != SpellGrammar.CATEGORY_BEHAVIOR) return;
            grade = string.IsNullOrEmpty(grade) ? SpellGrammar.DEFAULT_BEHAVIOR_GRADE : grade;
            if (!GameData.Runes.BehaviorGradeOrder.Contains(grade)) return;
            if (_editingGraph.Nodes.Count >= GameData.Balance.Limits.MaxGraphNodes)
            { _policy.ReportStatus("editor.graphLimit"); return; }
            var candidate = _editingGraph.Clone();
            var node = new GraphNode(NewId("node"), runeId, x, y);
            node.SetText(SpellGrammar.MODIFIER_GRADE_PARAM, grade);
            node.SetText(SpellGrammar.SHAPE_PARAM, SpellGrammar.DEFAULT_SHAPE);
            candidate.AddNode(node);
            if (!_policy.IsWithinModifierStock(_editingGraph, candidate)) { _policy.ReportStatus("editor.stockBlocked"); return; }
            _editingGraph = candidate;
            _policy.OnRunePlaced(runeId);
            MarkChanged(); RaiseGraphChanged();
        }

        /// <summary>
        /// 행동 노드의 형태를 SHAPE_OPTIONS 순서의 다음 값으로 바꾼다. 해금되지 않았거나 그 행동에 허용되지 않는 형태는 건너뛴다.
        /// 바꿀 값이 없으면 false를 반환한다.
        /// </summary>
        public bool CycleShape(string nodeId)
        {
            if (!_isEditable || !TryGetBehaviorNode(_editingGraph, nodeId, out GraphNode node, out _)) return false;
            string current = node.GetText(SpellGrammar.SHAPE_PARAM, SpellGrammar.DEFAULT_SHAPE);
            int start = Array.IndexOf(SpellGrammar.SHAPE_OPTIONS, current);
            for (int step = 1; step < SpellGrammar.SHAPE_OPTIONS.Length; step++)
            {
                string next = SpellGrammar.SHAPE_OPTIONS[(Math.Max(start, 0) + step) % SpellGrammar.SHAPE_OPTIONS.Length];
                if (!_policy.IsRuneUnlocked(SpellGrammar.ShapeRune(next)) || !SpellGrammar.IsShapeAllowed(node.RuneId, next)) continue;
                SpellGraph candidate = _editingGraph.Clone();
                candidate.FindNode(nodeId).SetText(SpellGrammar.SHAPE_PARAM, next);
                if (!CommitSlotCandidate(candidate, nodeId)) return false;
                return true;
            }
            return false;
        }

        /// <summary>행동 노드 등급의 한도 안에서 지정 속성·모디를 장착할 수 있는 첫 빈 슬롯 키를 찾는다.</summary>
        public bool TryFindEquipSlot(string nodeId, string runeId, string grade, out string slotKey)
        {
            slotKey = null;
            if (!TryGetBehaviorNode(_editingGraph, nodeId, out GraphNode node, out _)
                || !GameData.Runes.TryGet(runeId, out RuneDefinition rune)) return false;
            bool isElement = rune.Category == SpellGrammar.CATEGORY_ELEMENT;
            int max = isElement ? SpellGrammar.MAX_ELEMENT_SLOTS : SpellGrammar.MAX_MODIFIER_SLOTS;
            for (int index = 0; index < max; index++)
            {
                string key = isElement ? SpellGrammar.ElementSlotKey(index) : SpellGrammar.ModifierSlotKey(index);
                if (IsSlotLocked(nodeId, key) || !string.IsNullOrEmpty(node.GetText(key, null))) continue;
                if (!CanEquip(node, key, rune, grade)) return false;
                slotKey = key;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 행동 노드의 슬롯에 속성(속성 ID로 저장)·모디(룬ID#등급으로 저장)를 장착한다. 이미 장착된 슬롯이면 교체한다.
        /// 슬롯 종류·등급 한도·속성 종류·모디 호환과 중복·소지량·RAM을 검사하고 실패하면 상태 문구를 표시한다.
        /// </summary>
        public bool Equip(string nodeId, string slotKey, string runeId, string grade)
        {
            if (!_isEditable || !TryGetBehaviorNode(_editingGraph, nodeId, out GraphNode node, out _)
                || !GameData.Runes.TryGet(runeId, out RuneDefinition rune) || IsSlotLocked(nodeId, slotKey)
                || !CanEquip(node, slotKey, rune, grade))
            { _policy.ReportStatus("editor.slotBlocked"); return false; }
            string value = rune.Category == SpellGrammar.CATEGORY_ELEMENT
                ? (GameData.Runes.TryGetRuneElement(runeId, out ElementDefinition element) ? element.Id : null)
                : SpellGrammar.ModifierStockKey(runeId, grade);
            if (string.IsNullOrEmpty(value) || node.GetText(slotKey, null) == value) return false;
            SpellGraph candidate = _editingGraph.Clone();
            candidate.FindNode(nodeId).SetText(slotKey, value);
            if (!_policy.IsWithinModifierStock(_editingGraph, candidate)) { _policy.ReportStatus("editor.stockBlocked"); return false; }
            return CommitSlotCandidate(candidate, nodeId);
        }

        /// <summary>행동 노드의 지정 슬롯 값을 비운다. 비어 있거나 슬롯 키가 아니면 false를 반환한다.</summary>
        public bool ClearSlot(string nodeId, string slotKey)
        {
            if (!_isEditable || !TryGetBehaviorNode(_editingGraph, nodeId, out GraphNode node, out _)
                || !SpellGrammar.TryParseSlotKey(slotKey, out _, out _) || string.IsNullOrEmpty(node.GetText(slotKey, null))) return false;
            node.SetText(slotKey, "");
            MarkChanged(); RaiseGraphChanged();
            return true;
        }

        /// <summary>슬롯 순번이 행동 노드 등급의 속성·모디 슬롯 수 이상이면 잠긴 슬롯으로 보고 true를 반환한다. 슬롯 키가 아니어도 true다.</summary>
        public bool IsSlotLocked(string nodeId, string slotKey)
        {
            if (!TryGetBehaviorNode(_editingGraph, nodeId, out GraphNode node, out _)
                || !SpellGrammar.TryParseSlotKey(slotKey, out bool isElement, out int index)) return true;
            string grade = node.GetText(SpellGrammar.MODIFIER_GRADE_PARAM, SpellGrammar.DEFAULT_BEHAVIOR_GRADE);
            if (!GameData.Runes.TryGetBehaviorSlots(grade, out int elementSlots, out int modifierSlots)) return true;
            return index >= (isElement ? elementSlots : modifierSlots);
        }

        /// <summary>문자열 키에 해당하는 상태 문구를 편집 규칙에 전달한다.</summary>
        public void ReportStatus(string key) => _policy.ReportStatus(key);

        /// <summary>
        /// 슬롯 종류와 룬이 맞는지 검사한다. 속성 슬롯은 비활성 아닌 전투 속성만, 모디 슬롯은 등급이 있고 행동과 호환되며
        /// 같은 노드의 다른 모디 슬롯에 같은 모디가 없을 때만 true다. 같은 속성 2개는 허용한다.
        /// </summary>
        private static bool CanEquip(GraphNode node, string slotKey, RuneDefinition rune, string grade)
        {
            if (rune.IsDisabled || !SpellGrammar.TryParseSlotKey(slotKey, out bool isElement, out _)) return false;
            if (isElement)
            {
                return rune.Category == SpellGrammar.CATEGORY_ELEMENT && GameData.Runes.TryGetRuneElement(rune.Id, out ElementDefinition element)
                    && element.Category == SpellGrammar.ELEMENT_CATEGORY_ELEMENTAL;
            }
            if (rune.Category != SpellGrammar.CATEGORY_MODIFIER || !GameData.ModifierGrades.IsGradeAvailable(rune.Id, grade)
                || !GameData.Runes.IsModifierCompatible(rune.Id, node.RuneId)) return false;
            for (int index = 0; index < SpellGrammar.MAX_MODIFIER_SLOTS; index++)
            {
                string key = SpellGrammar.ModifierSlotKey(index);
                if (key == slotKey) continue;
                if (SpellGrammar.TryParseStockKey(node.GetText(key, null), out string equipped, out _) && equipped == rune.Id) return false;
            }
            return true;
        }

        /// <summary>그래프에서 비활성이 아닌 행동 노드를 찾는다. 없으면 false를 반환한다.</summary>
        private static bool TryGetBehaviorNode(SpellGraph graph, string nodeId, out GraphNode node, out RuneDefinition rune)
        {
            rune = null;
            node = graph.FindNode(nodeId);
            return node != null && GameData.Runes.TryGet(node.RuneId, out rune) && !rune.IsDisabled
                && rune.Category == SpellGrammar.CATEGORY_BEHAVIOR;
        }

        /// <summary>슬롯·형태를 바꾼 후보가 연결된 노드라면 장착 RAM을 검사하고 통과하면 편집 그래프로 확정한다.</summary>
        private bool CommitSlotCandidate(SpellGraph candidate, string nodeId)
        {
            if (candidate.IsNodeConnected(nodeId) && !_policy.IsWithinRam(candidate)) { _policy.ReportStatus("editor.ramBlocked"); return false; }
            _editingGraph = candidate;
            MarkChanged(); RaiseGraphChanged();
            return true;
        }

        /// <summary>필요하면 단일 포트의 기존 연결을 교체한 후보의 RAM·문법을 검사하고 성공할 때만 저장한다.</summary>
        public bool Connect(string fromNode, string fromPort, string toNode, string toPort, bool replaceExisting = false)
        {
            if (!_isEditable) return false;
            var candidate = CreateConnectionCandidate(fromNode, fromPort, toNode, toPort, replaceExisting);
            if (candidate.Edges.Count >= GameData.Balance.Limits.MaxGraphEdges)
            { _policy.ReportStatus("editor.graphLimit"); return false; }
            var edge = new GraphEdge(NewId("edge"), fromNode, fromPort, toNode, toPort,
                candidate.NextEdgeOrder(fromNode, fromPort));
            if (!GraphCompiler.CanConnect(candidate, edge, GameData.Runes, out _))
            { _policy.ReportStatus("editor.invalidConnection"); return false; }
            candidate.AddEdge(edge);
            if (!_policy.IsWithinRam(candidate)) { _policy.ReportStatus("editor.ramBlocked"); return false; }
            var context = _policy.GetCompileContext();
            CompileResult result = GraphCompiler.Compile(candidate, GameData.Runes, GameData.Balance.Grammar,
                context.UnlockedRunes, context.Capacity, context.MaxEnergy, context.Library, context.ModifierStock,
                context.MaxResourceCosts);
            if (result.Errors.Any(issue => issue.Code == "E2" || issue.Code == "E3" || issue.Code == "E4" || issue.Code == "E5" || issue.Code == "E6"))
            { _policy.ReportStatus("editor.invalidConnection"); return false; }
            _editingGraph = candidate;
            MarkChanged(); RaiseGraphChanged();
            return true;
        }

        /// <summary>기존 연결 교체 옵션을 반영한 후보의 포트·슬롯·수식 제한을 검사해 연결 가능 여부를 반환한다.</summary>
        public bool CanConnectPorts(string fromNode, string fromPort, string toNode, string toPort, bool replaceExisting = false)
        {
            if (!_isEditable) return false;
            var candidate = CreateConnectionCandidate(fromNode, fromPort, toNode, toPort, replaceExisting);
            if (candidate.Edges.Count >= GameData.Balance.Limits.MaxGraphEdges) return false;
            GraphEdge edge = new GraphEdge("preview", fromNode, fromPort, toNode, toPort,
                candidate.NextEdgeOrder(fromNode, fromPort));
            return GraphCompiler.CanConnect(candidate, edge, GameData.Runes, out _);
        }

        /// <summary>연결 검사용 사본을 만들고 교체 요청 시 단일 연결 포트에 걸린 다른 엣지만 제거한다. 실제 그래프는 유지한다.</summary>
        private SpellGraph CreateConnectionCandidate(string fromNode, string fromPort, string toNode, string toPort, bool replaceExisting)
        {
            SpellGraph candidate = _editingGraph.Clone();
            if (!replaceExisting) return candidate;
            GraphNode from = candidate.FindNode(fromNode);
            GraphNode to = candidate.FindNode(toNode);
            if (from == null || to == null) return candidate;
            PortDefinition output = GameData.Runes.Get(from.RuneId).FindPort(fromPort, SpellGrammar.DIRECTION_OUT);
            PortDefinition input = GameData.Runes.Get(to.RuneId).FindPort(toPort, SpellGrammar.DIRECTION_IN);
            if (output == null || input == null) return candidate;
            bool replaceOutput = output.Kind == SpellGrammar.CHAIN_KIND || output.Kind == SpellGrammar.ELEMENT_KIND
                || (output.Kind == "mod" && output.Max == 1);
            bool replaceInput = input.Kind == SpellGrammar.CHAIN_KIND || input.Kind == SpellGrammar.ELEMENT_KIND
                || input.Kind == "exec" || (input.Kind == "mod" && input.Max == 1);
            for (int i = candidate.Edges.Count - 1; i >= 0; i--)
            {
                GraphEdge current = candidate.Edges[i];
                bool sameOutput = current.FromNode == fromNode && current.FromPort == fromPort;
                bool sameInput = current.ToNode == toNode && current.ToPort == toPort;
                if (sameOutput && sameInput) continue;
                if ((replaceOutput && sameOutput) || (replaceInput && sameInput)) candidate.RemoveEdge(current.Id);
            }
            return candidate;
        }

        /// <summary>Core 이외의 지정 노드와 관련 엣지를 제거한다.</summary>
        public void RemoveNode(string nodeId)
        {
            if (!_isEditable) return;
            _editingGraph.RemoveNode(nodeId);
            MarkChanged(); RaiseGraphChanged();
        }

        /// <summary>지정한 엣지를 삭제하고 검증을 갱신한다.</summary>
        public void RemoveEdge(string edgeId)
        {
            if (!_isEditable) return;
            _editingGraph.RemoveEdge(edgeId);
            MarkChanged(); RaiseGraphChanged();
        }

        /// <summary>숫자 파라미터를 노드에 저장한다.</summary>
        public void SetNodeNumber(string nodeId, string key, float value)
        {
            if (!_isEditable) return;
            var node = _editingGraph.Nodes.FirstOrDefault(item => item.Id == nodeId);
            if (node == null) return;
            if (key == SpellGrammar.MODIFIER_GRADE_PARAM && GameData.Runes.TryGet(node.RuneId, out RuneDefinition gradeRune)
                && gradeRune.Category == SpellGrammar.CATEGORY_MODIFIER)
            {
                _policy.ReportStatus("editor.modifierStockBlocked");
                return;
            }
            node.SetNumber(key, value);
            MarkChanged();
        }

        /// <summary>
        /// 문자열 파라미터를 선택 노드에 저장하고 Modifier 등급 소지량을 검증한다.
        /// 행동 노드의 등급·형태·슬롯 값은 소지량 검사를 거치는 전용 명령(CycleShape, Equip, ClearSlot)으로만 바꾼다.
        /// </summary>
        public void SetNodeText(string nodeId, string key, string value)
        {
            if (!_isEditable) return;
            var node = _editingGraph.Nodes.FirstOrDefault(item => item.Id == nodeId);
            if (node == null) return;
            if (TryGetBehaviorNode(_editingGraph, nodeId, out _, out _) && (key == SpellGrammar.MODIFIER_GRADE_PARAM
                || key == SpellGrammar.SHAPE_PARAM || SpellGrammar.TryParseSlotKey(key, out _, out _))) return;
            if (key == SpellGrammar.MODIFIER_GRADE_PARAM && GameData.Runes.TryGet(node.RuneId, out RuneDefinition rune)
                && rune.Category == SpellGrammar.CATEGORY_MODIFIER)
            {
                if (!GameData.ModifierGrades.IsGradeAvailable(rune.Id, value))
                { _policy.ReportStatus("editor.modifierStockBlocked"); return; }
                if (node.GetText(key, null) == value) return;
                SpellGraph candidate = _editingGraph.Clone();
                candidate.FindNode(nodeId).SetText(key, value);
                if (!_policy.IsWithinModifierStock(_editingGraph, candidate))
                { _policy.ReportStatus("editor.modifierStockBlocked"); return; }
                _editingGraph = candidate;
                MarkChanged();
                return;
            }
            node.SetText(key, value);
            if (key == SpellGrammar.ELEMENT_PARAM) _policy.OnElementSelected(value);
            MarkChanged();
        }

        /// <summary>노드 드래그 시작 시 마지막 확정 그래프를 되돌리기용 사본으로 보관한다.</summary>
        public void BeginEdit()
        {
            _previousGraph = _editingGraph.Clone();
        }

        /// <summary>이전 편집 상태를 최대 50단계 기록하고 자동 컴파일을 예약한다.</summary>
        public void MarkChanged()
        {
            if (_previousGraph != null && ShareCodec.Serialize(_previousGraph) != ShareCodec.Serialize(_editingGraph))
            {
                _undo.Add(_previousGraph.Clone());
                if (_undo.Count > HISTORY_LIMIT) _undo.RemoveAt(0);
                _redo.Clear(); _previousGraph = _editingGraph.Clone();
            }
            _graphDirty = true;
            _compileAt = Time.unscaledTime + COMPILE_DEBOUNCE;
        }

        /// <summary>이전 그래프 스냅샷으로 되돌리고 현재 상태를 다시 실행 목록에 기록한다.</summary>
        public void Undo()
        {
            if (_undo.Count == 0 || !_isEditable) return;
            _redo.Add(_editingGraph.Clone()); _editingGraph = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1);
            _previousGraph = _editingGraph.Clone(); Recompile(); Save(); RaiseGraphChanged();
        }

        /// <summary>되돌린 그래프 스냅샷을 복구한다.</summary>
        public void Redo()
        {
            if (_redo.Count == 0 || !_isEditable) return;
            _undo.Add(_editingGraph.Clone()); _editingGraph = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1);
            _previousGraph = _editingGraph.Clone(); Recompile(); Save(); RaiseGraphChanged();
        }

        /// <summary>선택된 노드와 내부 연결을 별도 사본에 기록한다.</summary>
        public void CopyNodes(IEnumerable<string> nodeIds)
        {
            var selected = new HashSet<string>(nodeIds);
            var snapshot = _editingGraph.Clone();
            _copiedNodes.Clear(); _copiedEdges.Clear();
            _copiedNodes.AddRange(snapshot.Nodes.Where(node => selected.Contains(node.Id) && node.RuneId != "core.cast"));
            _copiedEdges.AddRange(snapshot.Edges.Where(edge => _copiedNodes.Any(node => node.Id == edge.FromNode) && _copiedNodes.Any(node => node.Id == edge.ToNode)));
        }

        /// <summary>복사 노드에 새 ID와 좌표 오프셋을 부여하고 내부 연결이 있으면 장착 RAM, Modifier 소지량을 검증해 붙여넣는다.</summary>
        public void PasteNodes()
        {
            if (_copiedNodes.Count == 0 || !_isEditable) return;
            if (_editingGraph.Nodes.Count + _copiedNodes.Count > GameData.Balance.Limits.MaxGraphNodes ||
                _editingGraph.Edges.Count + _copiedEdges.Count > GameData.Balance.Limits.MaxGraphEdges)
            { _policy.ReportStatus("editor.graphLimit"); return; }
            var candidate = _editingGraph.Clone();
            var map = new Dictionary<string, string>();
            foreach (var node in _copiedNodes)
            {
                var clone = node.Clone(NewId("node")); clone.Move(node.X + 30, node.Y + 30); map[node.Id] = clone.Id; candidate.AddNode(clone);
            }
            foreach (var edge in _copiedEdges) candidate.AddEdge(new GraphEdge(NewId("edge"), map[edge.FromNode], edge.FromPort, map[edge.ToNode], edge.ToPort));
            if (_copiedEdges.Count > 0 && !_policy.IsWithinRam(candidate)) { _policy.ReportStatus("editor.ramBlocked"); return; }
            if (!_policy.IsWithinModifierStock(_editingGraph, candidate)) { _policy.ReportStatus("editor.modifierStockBlocked"); return; }
            _editingGraph = candidate; MarkChanged(); RaiseGraphChanged();
        }

        /// <summary>모든 노드를 한 줄에 네 개씩 실행 행으로 정렬한다. 속성·모디는 행동 노드 슬롯에 있어 따로 정렬하지 않는다.</summary>
        public void AutoArrange()
        {
            if (!_isEditable) return;
            IReadOnlyList<GraphNode> nodes = _editingGraph.Nodes;
            for (var i = 0; i < nodes.Count; i++) nodes[i].Move(35 + (i % 4) * 170, 55 + (i / 4) * 200);
            MarkChanged(); RaiseGraphChanged();
        }

        /// <summary>RC1·RC2 구조와 라이브러리 참조를 검증해 현재 ID로 가져오고 실패 이유를 반환한다.</summary>
        private string ImportGraph(string code)
        {
            if (!_isEditable) return GameData.L("editor.combatLocked");
            if (!ShareCodec.TryDecode(code, out var graph, out var error, _storage.Library)) return error;
            ApplyGraph(graph);
            Recompile(); Save();
            return null;
        }

        /// <summary>전달된 그래프를 현재 ID의 편집 상태에 반영하고 검증을 예약한다.</summary>
        private void ApplyGraph(SpellGraph graph)
        {
            if (!_isEditable) return;
            if (graph.Nodes.Count > GameData.Balance.Limits.MaxGraphNodes || graph.Edges.Count > GameData.Balance.Limits.MaxGraphEdges)
            { _policy.ReportStatus("editor.graphLimit"); return; }
            _editingGraph = graph.Clone(_editingGraph.Id);
            MarkChanged(); RaiseGraphChanged();
        }

        /// <summary>순번과 GUID를 결합한 보관함·노드·엣지의 영구 고유 ID를 반환한다.</summary>
        private string NewId(string prefix)
        {
            return prefix + "-" + _nextId++ + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        /// <summary>구독자가 있을 때만 컴파일 완료 이벤트를 알린다.</summary>
        private void RaiseCompiled() { Compiled?.Invoke(); }

        /// <summary>구독자가 있을 때만 그래프 갱신 이벤트를 알린다.</summary>
        private void RaiseGraphChanged() { GraphChanged?.Invoke(); }

        /// <summary>구독자가 있을 때만 마법 전환 이벤트를 알린다.</summary>
        private void RaiseSpellSwitched() { SpellSwitched?.Invoke(); }
    }
}
