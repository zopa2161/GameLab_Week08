using System;
using System.Collections.Generic;

namespace RuneCode
{
    public sealed class NodeParameter
    {
        private readonly string _key;
        private readonly float _number;
        private readonly string _text;
        public string Key => _key;
        public float Number => _number;
        public string Text => _text;

        /// <summary>키에 숫자 또는 문자열 파라미터 값을 연결한다.</summary>
        public NodeParameter(string key, float number, string text = null)
        {
            _key = key;
            _number = number;
            _text = text;
        }
    }

    public sealed class GraphNode
    {
        private readonly string _id;
        private readonly string _runeId;
        private readonly List<NodeParameter> _params = new List<NodeParameter>();
        private float _x;
        private float _y;
        public string Id => _id;
        public string RuneId => _runeId;
        public float X => _x;
        public float Y => _y;
        public IReadOnlyList<NodeParameter> Params => _params;

        /// <summary>ID와 룬 종류 및 캔버스 좌표로 편집 가능한 노드를 생성한다.</summary>
        public GraphNode(string id, string runeId, float x = 0f, float y = 0f)
        {
            _id = id;
            _runeId = runeId;
            _x = x;
            _y = y;
        }

        /// <summary>
        /// 저장 데이터 복원용으로 ID·룬 종류·좌표와 파라미터 목록을 그대로 받아 노드를 만든다.
        /// 키 중복을 정리하지 않으므로 외부 데이터는 SpellGraphValidator로 검증한다.
        /// </summary>
        public GraphNode(string id, string runeId, float x, float y, IEnumerable<NodeParameter> parameters)
            : this(id, runeId, x, y)
        {
            if (parameters != null)
            {
                _params.AddRange(parameters);
            }
        }

        /// <summary>캔버스 좌표만 변경하며 마법 실행 의미는 유지한다.</summary>
        public void Move(float x, float y)
        {
            _x = x;
            _y = y;
        }

        /// <summary>키의 숫자 파라미터를 읽고 값이 없으면 지정 기본값을 반환한다.</summary>
        public float GetNumber(string key, float fallback = 0f)
        {
            foreach (NodeParameter param in _params)
                if (param.Key == key) return param.Number;
            return fallback;
        }

        /// <summary>키의 문자열 파라미터를 읽고 값이 없으면 지정 기본값을 반환한다.</summary>
        public string GetText(string key, string fallback = "")
        {
            foreach (NodeParameter param in _params)
                if (param.Key == key) return param.Text ?? fallback;
            return fallback;
        }

        /// <summary>키의 숫자 파라미터를 교체하거나 추가한다.</summary>
        public void SetNumber(string key, float value) => SetParameter(new NodeParameter(key, value));

        /// <summary>키의 문자열 파라미터를 교체하거나 추가한다.</summary>
        public void SetText(string key, string value) => SetParameter(new NodeParameter(key, 0f, value));

        /// <summary>지정한 키의 파라미터를 모두 제거하고 하나라도 제거했는지 반환한다.</summary>
        public bool RemoveParameter(string key)
        {
            return _params.RemoveAll(parameter => parameter.Key == key) > 0;
        }

        /// <summary>파라미터가 독립된 노드 복사본을 만들고 새 노드 ID를 지정한다.</summary>
        public GraphNode Clone(string newId)
        {
            GraphNode clone = new GraphNode(newId, _runeId, _x, _y);
            foreach (NodeParameter param in _params)
                clone.SetParameter(new NodeParameter(param.Key, param.Number, param.Text));
            return clone;
        }

        /// <summary>파라미터 키의 기존 값을 교체하고 새 키는 목록 끝에 추가한다.</summary>
        private void SetParameter(NodeParameter value)
        {
            for (int i = 0; i < _params.Count; i++)
            {
                if (_params[i].Key != value.Key) continue;
                _params[i] = value;
                return;
            }
            _params.Add(value);
        }
    }

    public sealed class GraphEdge
    {
        private readonly string _id;
        private readonly string _fromNode;
        private readonly string _fromPort;
        private readonly string _toNode;
        private readonly string _toPort;
        private readonly int _order;
        private readonly bool _isLegacyEvent;
        public string Id => _id;
        public string FromNode => _fromNode;
        public string FromPort => _fromPort;
        public string ToNode => _toNode;
        public string ToPort => _toPort;
        public int Order => _order;

        /// <summary>이벤트 의미가 바뀌기 전(그래프 버전 2 이하)에 만든 이벤트 출력 연결인지 반환한다. 컴파일러가 의미 변경 경고를 붙인다.</summary>
        public bool IsLegacyEvent => _isLegacyEvent;

        /// <summary>출력과 입력 노드의 포트 ID로 그래프 연결을 생성한다. isLegacyEvent는 이전 이벤트 의미로 만든 연결 표시다.</summary>
        public GraphEdge(string id, string fromNode, string fromPort, string toNode, string toPort, int order = 0, bool isLegacyEvent = false)
        {
            _id = id;
            _fromNode = fromNode;
            _fromPort = fromPort;
            _toNode = toNode;
            _toPort = toPort;
            _order = order;
            _isLegacyEvent = isLegacyEvent;
        }
    }

    public sealed class SpellGraph
    {
        // 첫 마법의 ID는 기존 시작 마법 템플릿과 같아야 이전 저장의 활성 마법·로드아웃 참조가 유지된다.
        private const string STARTER_SPELL_ID = "energybolt";
        private const string STARTER_BEHAVIOR_RUNE = "behavior.launch";

        private string _id;
        private string _name;
        public const int CURRENT_VERSION = 4;

        private int _version = CURRENT_VERSION;
        private readonly List<GraphNode> _nodes = new List<GraphNode>();
        private readonly List<GraphEdge> _edges = new List<GraphEdge>();
        public string Id => _id;
        public string Name => _name;
        public int Version => _version;
        public IReadOnlyList<GraphNode> Nodes => _nodes;
        public IReadOnlyList<GraphEdge> Edges => _edges;

        /// <summary>마법 ID와 이름으로 편집용 그래프를 초기화한다.</summary>
        public SpellGraph(string id, string name)
        {
            _id = id;
            _name = name;
        }

        /// <summary>
        /// 저장 데이터 복원용으로 ID·이름·스키마 버전과 노드·엣지 목록을 그대로 받아 그래프를 만든다.
        /// 중복·참조를 정리하지 않으므로 외부 데이터는 SpellGraphValidator로 검증한다.
        /// </summary>
        public SpellGraph(string id, string name, int version, IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges)
            : this(id, name)
        {
            _version = version;
            if (nodes != null)
            {
                _nodes.AddRange(nodes);
            }
            if (edges != null)
            {
                _edges.AddRange(edges);
            }
        }

        /// <summary>삭제할 수 없는 시전 Core가 포함된 새 마법을 반환한다.</summary>
        public static SpellGraph Create(string id, string name)
        {
            SpellGraph graph = new SpellGraph(id, name);
            graph.AddNode(new GraphNode("core", SpellGrammar.CORE_RUNE, 80f, 140f));
            return graph;
        }

        /// <summary>
        /// 새 진행 데이터의 첫 마법을 이름 name으로 반환한다. 공격 시전 Core에 발사 행동(C 등급) 하나가 연결된 형태이며,
        /// 마법 ID와 노드·간선 ID는 기존 저장의 로드아웃 참조와 맞도록 고정한다.
        /// </summary>
        public static SpellGraph CreateStarter(string name)
        {
            SpellGraph graph = new SpellGraph(STARTER_SPELL_ID, name);
            GraphNode core = new GraphNode("core", SpellGrammar.CORE_RUNE, 20f, 150f);
            core.SetText(SpellGrammar.TRIGGER_PARAM, SpellGrammar.TRIGGER_ON_ATTACK);
            GraphNode behavior = new GraphNode("form", STARTER_BEHAVIOR_RUNE, 200f, 150f);
            behavior.SetText(SpellGrammar.MODIFIER_GRADE_PARAM, SpellGrammar.DEFAULT_BEHAVIOR_GRADE);
            graph.AddNode(core);
            graph.AddNode(behavior);
            graph.AddEdge(new GraphEdge("exec", core.Id, "exec", behavior.Id, "exec"));
            return graph;
        }

        /// <summary>다른 보관함 항목으로 식별할 새 마법 ID를 지정한다.</summary>
        public void SetIdentity(string id) => _id = id;

        /// <summary>마이그레이션이 그래프 스키마 버전을 갱신할 때 사용한다.</summary>
        internal void SetVersion(int version) => _version = version;

        /// <summary>마법의 표시 이름을 변경한다.</summary>
        public void Rename(string name) => _name = name;

        /// <summary>ID로 편집 노드를 찾으며 존재하지 않으면 null을 반환한다.</summary>
        public GraphNode FindNode(string id)
        {
            foreach (GraphNode node in _nodes)
                if (node.Id == id) return node;
            return null;
        }

        /// <summary>시작 노드들과 활성 실행 출력으로 이어지는 모든 후속 노드 ID를 중복 없이 반환한다.</summary>
        public HashSet<string> GetBlockGroup(IEnumerable<string> roots, RuneCatalog runes)
        {
            var result = new HashSet<string>();
            var pending = new Queue<string>();
            foreach (string root in roots)
                if (FindNode(root) != null && result.Add(root)) pending.Enqueue(root);
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                RuneDefinition rune = runes.Get(FindNode(id).RuneId);
                foreach (GraphEdge edge in _edges)
                {
                    if (edge.FromNode != id) continue;
                    PortDefinition port = rune.FindPort(edge.FromPort, SpellGrammar.DIRECTION_OUT);
                    if (port == null || port.IsDeprecated || port.Kind != SpellGrammar.EXEC_KIND) continue;
                    if (FindNode(edge.ToNode) != null && result.Add(edge.ToNode)) pending.Enqueue(edge.ToNode);
                }
            }
            return result;
        }

        /// <summary>모든 시작 노드에서 활성 실행 포트로 도달하는 노드 ID를 반환한다. 분리된 묶음과 순환 중복은 제외한다.</summary>
        public HashSet<string> GetExecutableNodeIds(RuneCatalog runes)
        {
            var reachable = new HashSet<string>();
            foreach (GraphNode node in _nodes)
            {
                if (node == null || !runes.TryGet(node.RuneId, out RuneDefinition rune)
                    || rune.IsDisabled || rune.Category != SpellGrammar.CATEGORY_CORE) continue;
                reachable.UnionWith(GetExecutableNodeIds(runes, node.Id));
            }
            return reachable;
        }

        /// <summary>지정한 시작 노드 하나에서 활성 실행 포트로 도달하는 노드 ID를 반환한다. core 시작 노드가 아니면 빈 집합이다.</summary>
        public HashSet<string> GetExecutableNodeIds(RuneCatalog runes, string startNodeId)
        {
            var reachable = new HashSet<string>();
            var pending = new Queue<GraphNode>();
            GraphNode start = FindNode(startNodeId);
            if (start == null || !runes.TryGet(start.RuneId, out RuneDefinition startRune)
                || startRune.IsDisabled || startRune.Category != SpellGrammar.CATEGORY_CORE) return reachable;
            reachable.Add(start.Id);
            pending.Enqueue(start);
            while (pending.Count > 0)
            {
                GraphNode source = pending.Dequeue();
                RuneDefinition sourceRune = runes.Get(source.RuneId);
                foreach (GraphEdge edge in _edges)
                {
                    if (edge == null || edge.FromNode != source.Id) continue;
                    PortDefinition output = sourceRune.FindPort(edge.FromPort, SpellGrammar.DIRECTION_OUT);
                    if (output == null || output.IsDeprecated || output.Kind != SpellGrammar.EXEC_KIND) continue;
                    GraphNode target = FindNode(edge.ToNode);
                    if (target == null || !runes.TryGet(target.RuneId, out RuneDefinition targetRune) || targetRune.IsDisabled) continue;
                    PortDefinition input = targetRune.FindPort(edge.ToPort, SpellGrammar.DIRECTION_IN);
                    if (input == null || input.IsDeprecated || input.Kind != SpellGrammar.EXEC_KIND) continue;
                    if (reachable.Add(target.Id)) pending.Enqueue(target);
                }
            }
            return reachable;
        }

        /// <summary>지정한 노드에 연결된 그래프 엣지가 하나라도 있는지 반환한다.</summary>
        public bool IsNodeConnected(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (GraphEdge edge in _edges)
                if (edge != null && (edge.FromNode == id || edge.ToNode == id)) return true;
            return false;
        }

        /// <summary>중복 ID를 차단하면서 노드를 그래프에 배치한다.</summary>
        public bool AddNode(GraphNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.Id) || FindNode(node.Id) != null) return false;
            _nodes.Add(node);
            return true;
        }

        /// <summary>core.cast 시작 노드를 제외한 노드와 연결된 엣지를 제거하고 제거 성공 여부를 반환한다. 다른 시작 노드는 삭제할 수 있다.</summary>
        public bool RemoveNode(string id)
        {
            GraphNode node = FindNode(id);
            if (node == null || node.RuneId == SpellGrammar.CORE_RUNE) return false;
            _nodes.Remove(node);
            _edges.RemoveAll(edge => edge.FromNode == id || edge.ToNode == id);
            return true;
        }

        /// <summary>중복 ID 및 같은 포트 연결을 차단하며 엣지를 추가한다.</summary>
        public bool AddEdge(GraphEdge edge)
        {
            if (edge == null || string.IsNullOrEmpty(edge.Id)) return false;
            foreach (GraphEdge current in _edges)
                if (current.Id == edge.Id || (current.FromNode == edge.FromNode && current.FromPort == edge.FromPort
                    && current.ToNode == edge.ToNode && current.ToPort == edge.ToPort)) return false;
            _edges.Add(edge);
            return true;
        }

        /// <summary>출력 포트의 다음 실행 순번을 반환한다.</summary>
        public int NextEdgeOrder(string fromNode, string fromPort)
        {
            int order = 0;
            foreach (GraphEdge edge in _edges)
                if (edge.FromNode == fromNode && edge.FromPort == fromPort) order = Math.Max(order, edge.Order + 1);
            return order;
        }

        /// <summary>ID에 해당하는 엣지를 제거하고 제거 여부를 반환한다.</summary>
        public bool RemoveEdge(string id) => _edges.RemoveAll(edge => edge.Id == id) > 0;

        /// <summary>같은 ID의 엣지를 목록 위치를 유지한 채 교체하고 성공 여부를 반환한다. 마이그레이션에서 실행 순서를 보존하려고 사용한다.</summary>
        internal bool ReplaceEdge(string id, GraphEdge replacement)
        {
            for (int i = 0; i < _edges.Count; i++)
            {
                if (_edges[i].Id != id) continue;
                _edges[i] = replacement;
                return true;
            }
            return false;
        }

        /// <summary>같은 ID의 노드를 연결 엣지는 유지한 채 전달된 노드로 교체하고 성공 여부를 반환한다.</summary>
        public bool ReplaceNode(GraphNode replacement)
        {
            if (replacement == null) return false;
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].Id != replacement.Id) continue;
                _nodes[i] = replacement;
                return true;
            }
            return false;
        }

        /// <summary>ID·이름·스키마 버전을 유지하고 노드와 엣지만 전달된 목록으로 바꾼 새 그래프를 반환한다. 컴파일 정규화에서만 사용한다.</summary>
        internal SpellGraph CopyWith(IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges)
        {
            return new SpellGraph(_id, _name, _version, nodes, edges);
        }

        /// <summary>
        /// 노드와 파라미터 목록이 독립된 깊은 복사본을 만들고 선택적으로 새 ID를 지정한다.
        /// 파라미터와 엣지는 변경 불가 값이라 공유한다.
        /// </summary>
        public SpellGraph Clone(string id = null)
        {
            var nodes = new List<GraphNode>(_nodes.Count);
            foreach (GraphNode node in _nodes)
            {
                nodes.Add(new GraphNode(node.Id, node.RuneId, node.X, node.Y, node.Params));
            }
            return new SpellGraph(id ?? _id, _name, _version, nodes, _edges);
        }
    }
}
