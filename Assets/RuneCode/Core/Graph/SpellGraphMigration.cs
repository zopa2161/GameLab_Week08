using System;
using System.Collections.Generic;
using System.Linq;

namespace RuneCode
{
    /// <summary>
    /// 이전 저장 그래프를 현재 문법(그래프 버전 3: Trigger → Shape(← 속성 노드) → Behavior, Apply(Element))으로 변환한다.
    /// 1단계는 이전 룬(단독 형태·속성, 직접 마법, 방벽, 점멸, 노이즈, 사거리)을 버전 2 체인으로, 2단계는 버전 2 체인을 버전 3으로 바꾼다.
    /// 버전 2→3은 이름·표현만 바뀌고 의미가 같은 변환이며, 구조가 깨진 체인은 바꾸지 않아 기존과 같은 컴파일 오류를 낸다.
    /// 의미가 바뀐 연결(이전 onComplete 등)은 다른 포트로 옮기지 않고 그대로 둔다. 컴파일러가 지원 중단·의미 변경 경고를 낸다.
    /// 여러 번 호출해도 결과가 같다.
    /// </summary>
    public static class SpellGraphMigration
    {
        private const float SHIELD_POWER = 20f;
        private const float SHIELD_SECONDS = 3f;
        private const float BLOCK_SPACING = 150f;
        private const float BLOCK_DROP = 110f;

        private static readonly Dictionary<string, string> LEGACY_SHAPES = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "form.bolt", "launch" }, { "form.burst", "explosion" }, { "form.orbit", "orbit" }, { "form.zone", "remain" }
        };

        private static readonly Dictionary<string, string> LEGACY_ELEMENTS = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "elem.fire", "fire" }, { "elem.ice", "ice" }, { "elem.arc", "electric" }
        };

        private const string V2_TYPE_PREFIX = "type.";
        private const string V2_ELEMENT_PREFIX = "element.";
        private const string V2_SHAPE_PREFIX = "shape.";
        private const string V2_BUFF_TYPE = "type.buff";
        private const string V2_REMAIN_SHAPE = "shape.remain";
        private const string LEGACY_BEAM_BEHAVIOR = "behavior.beam";

        private static readonly string[] LEGACY_MODIFIER_PARAMETERS = { "sizeScale", "speedScale", "durationScale", "rangeScale" };
        private static readonly string[] LEGACY_SHAPE_SIZE_PARAMETERS =
        {
            SpellGrammar.SHAPE_RADIUS_PARAM, SpellGrammar.BOX_WIDTH_PARAM, SpellGrammar.BOX_HEIGHT_PARAM, SpellGrammar.BOX_LENGTH_PARAM,
            SpellGrammar.BOX_DIRECTION_PARAM, SpellGrammar.CONE_DISTANCE_PARAM, SpellGrammar.CONE_ANGLE_PARAM
        };

        private static readonly Dictionary<string, string> V2_SHAPES = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "type.sphere", "shape.sphere" }, { "type.box", "shape.box" }
        };

        private static readonly Dictionary<string, string> V2_BEHAVIORS = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "shape.launch", "behavior.launch" }, { "shape.explosion", "behavior.burst" },
            { "shape.orbit", "behavior.orbit" }, { "shape.remain", "behavior.persist" }
        };

        private static readonly Dictionary<string, string> V2_ELEMENTS = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "element.normal", "neutral" }, { "element.fire", "fire" }, { "element.electric", "lightning" },
            { "element.ice", "ice" }, { "element.heal", "healing" }, { "element.protection", "protection" }
        };

        private static readonly Dictionary<string, string> V2_TRIGGERS = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "attack", SpellGrammar.TRIGGER_ON_ATTACK }, { "move", SpellGrammar.TRIGGER_ON_MOVE },
            { "dashInput", SpellGrammar.TRIGGER_ON_DASH_START }, { "dashComplete", SpellGrammar.TRIGGER_ON_DASH_END },
            { "hit", SpellGrammar.TRIGGER_ON_HIT_TAKEN }
        };

        /// <summary>
        /// 이전 해금 룬 ID를 현재 문법의 해금 ID 목록으로 바꿔 반환한다. 이전 룬 → 버전 2 블록 → 버전 3 블록 순으로 변환한다.
        /// 대응 블록이 없는 ID(점멸, 노이즈, 버프 타입)는 그대로 두며 호출부가 카탈로그 기준으로 정리한다.
        /// </summary>
        public static IEnumerable<string> MapUnlockedRune(string runeId)
        {
            return MapLegacyUnlock(runeId).SelectMany(MapV2Unlock).Distinct();
        }

        /// <summary>이전 그래프 문법과 Modifier 파라미터를 바꾸며 등급 표가 있으면 누락 등급도 보완한다.</summary>
        public static bool Migrate(SpellGraph graph)
        {
            return Migrate(graph, null);
        }

        /// <summary>이전 그래프와 등급 없는 Modifier 노드를 현재 문법 및 지정 표의 최저 등급으로 바꾼다.</summary>
        public static bool Migrate(SpellGraph graph, ModifierGradeTable modifierGrades)
        {
            if (graph?.Nodes == null || graph.Edges == null) return false;
            bool isChanged = false;
            if (HasLegacyNode(graph))
            {
                ConvertLegacyRunes(graph);
                isChanged = true;
            }
            if (graph.Version < SpellGraph.CURRENT_VERSION)
            {
                ConvertToVersion3(graph);
                MarkLegacyEvents(graph);
                isChanged = true;
            }
            if (MigrateModifierGrades(graph, modifierGrades)) isChanged = true;
            if (RemoveBeamBehaviors(graph)) isChanged = true;
            if (RemoveShapeSizes(graph)) isChanged = true;
            if (ReplaceRemovedDashEndTrigger(graph)) isChanged = true;
            if (SplitShapeElements(graph)) isChanged = true;
            return isChanged;
        }

        /// <summary>core.cast 노드의 제거된 onDashEnd 시작 조건 값을 onAttack으로 바꾸고 변경 여부를 반환한다. 여러 번 호출해도 결과가 같다.</summary>
        public static bool ReplaceRemovedDashEndTrigger(SpellGraph graph)
        {
            bool isChanged = false;
            foreach (GraphNode node in graph.Nodes)
            {
                if (node == null || node.RuneId != SpellGrammar.CORE_RUNE) continue;
                if (node.GetText(SpellGrammar.TRIGGER_PARAM, null) != SpellGrammar.TRIGGER_ON_DASH_END) continue;
                node.SetText(SpellGrammar.TRIGGER_PARAM, SpellGrammar.TRIGGER_ON_ATTACK);
                isChanged = true;
            }
            return isChanged;
        }

        /// <summary>
        /// Shape 노드의 속성 드롭다운 값을 Shape element 포트에 붙는 속성 노드로 옮기고 변경 여부를 반환한다.
        /// 무속성은 노드를 만들지 않는다. 회복·보호의 효과량·지속시간은 속성 노드로 옮기고 Shape에서는 지운다.
        /// </summary>
        private static bool SplitShapeElements(SpellGraph graph)
        {
            bool isChanged = false;
            foreach (GraphNode shape in new List<GraphNode>(graph.Nodes))
            {
                if (shape == null || SpellGrammar.MagicTypeOf(shape.RuneId) == null || shape.RuneId == SpellGrammar.APPLY_RUNE) continue;
                string element = shape.GetText(SpellGrammar.ELEMENT_PARAM, null);
                bool hasPower = shape.Params.Any(param => param.Key == SpellGrammar.POWER_PARAM);
                bool hasDuration = shape.Params.Any(param => param.Key == SpellGrammar.BUFF_DURATION_PARAM);
                if (element == null && !hasPower && !hasDuration) continue;
                isChanged = true;
                float power = shape.GetNumber(SpellGrammar.POWER_PARAM);
                float duration = shape.GetNumber(SpellGrammar.BUFF_DURATION_PARAM);
                shape.RemoveParameter(SpellGrammar.ELEMENT_PARAM);
                shape.RemoveParameter(SpellGrammar.POWER_PARAM);
                shape.RemoveParameter(SpellGrammar.BUFF_DURATION_PARAM);
                if (element == null || element == SpellGrammar.ELEMENT_NEUTRAL) continue;
                string elementId = UniqueNodeId(graph, shape.Id + "-element");
                var elementNode = new GraphNode(elementId, SpellGrammar.ElementRune(element), shape.X, shape.Y + BLOCK_DROP);
                if (element == "healing" || element == "protection")
                {
                    if (hasPower) elementNode.SetNumber(SpellGrammar.POWER_PARAM, power);
                    if (hasDuration) elementNode.SetNumber(SpellGrammar.BUFF_DURATION_PARAM, duration);
                }
                graph.AddNode(elementNode);
                graph.AddEdge(new GraphEdge(UniqueEdgeId(graph, shape.Id + "-element"), elementId, SpellGrammar.ELEMENT_PORT,
                    shape.Id, SpellGrammar.ELEMENT_PORT));
            }
            return isChanged;
        }

        /// <summary>백서 v7에서 없어진 Beam Behavior 노드를 연결과 함께 제거하고 제거 여부를 반환한다. 다른 노드로 자동 변환하지 않는다.</summary>
        private static bool RemoveBeamBehaviors(SpellGraph graph)
        {
            bool isChanged = false;
            foreach (GraphNode node in new List<GraphNode>(graph.Nodes))
            {
                if (node != null && node.RuneId == LEGACY_BEAM_BEHAVIOR && graph.RemoveNode(node.Id)) isChanged = true;
            }
            return isChanged;
        }

        /// <summary>Shape·Inline Magic 노드에 저장된 이전 크기·방향 파라미터를 지우고 변경 여부를 반환한다. 크기는 형상 정의 데이터의 고정값을 쓴다(백서 v7).</summary>
        private static bool RemoveShapeSizes(SpellGraph graph)
        {
            bool isChanged = false;
            foreach (GraphNode node in graph.Nodes)
            {
                if (node == null || (SpellGrammar.MagicTypeOf(node.RuneId) == null && node.RuneId != SpellGrammar.INLINE_RUNE)) continue;
                foreach (string parameter in LEGACY_SHAPE_SIZE_PARAMETERS)
                    if (node.RemoveParameter(parameter)) isChanged = true;
            }
            return isChanged;
        }

        /// <summary>표의 최저 등급을 Modifier 노드에 넣고 이전 배율 파라미터를 제거한다.</summary>
        private static bool MigrateModifierGrades(SpellGraph graph, ModifierGradeTable modifierGrades)
        {
            bool isChanged = false;
            foreach (GraphNode node in graph.Nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.RuneId) || !node.RuneId.StartsWith("mod.", StringComparison.Ordinal)) continue;
                string grade = node.GetText(SpellGrammar.MODIFIER_GRADE_PARAM, null);
                if (modifierGrades != null && string.IsNullOrEmpty(grade))
                {
                    node.SetText(SpellGrammar.MODIFIER_GRADE_PARAM, modifierGrades.GetLowestAvailableGrade(node.RuneId));
                    isChanged = true;
                }
                foreach (string parameter in LEGACY_MODIFIER_PARAMETERS)
                    if (node.RemoveParameter(parameter)) isChanged = true;
            }
            return isChanged;
        }

        /// <summary>
        /// 이벤트 출력(onHit·onExpire·onFirstHitOrExpire·onComplete)에서 나가는 연결을 이전 이벤트 의미로 만든 연결로 표시한다.
        /// 버전 3 이전 그래프에만 호출하며, 사용자가 연결을 다시 만들면 표시가 사라진다.
        /// </summary>
        private static void MarkLegacyEvents(SpellGraph graph)
        {
            foreach (GraphEdge edge in new List<GraphEdge>(graph.Edges))
            {
                if (edge == null || edge.IsLegacyEvent || !SpellGrammar.IsEventPort(edge.FromPort)) continue;
                graph.ReplaceEdge(edge.Id, new GraphEdge(edge.Id, edge.FromNode, edge.FromPort, edge.ToNode, edge.ToPort, edge.Order, true));
            }
        }

        /// <summary>이전 룬 해금 ID를 버전 2 블록 해금 ID로 바꾼다.</summary>
        private static IEnumerable<string> MapLegacyUnlock(string runeId)
        {
            if (LEGACY_SHAPES.TryGetValue(runeId, out string shape)) return new[] { V2_SHAPE_PREFIX + shape };
            if (LEGACY_ELEMENTS.TryGetValue(runeId, out string element)) return new[] { V2_ELEMENT_PREFIX + element };
            if (runeId == "mod.range") return new[] { "mod.duration" };
            if (runeId == "act.shield") return new[] { V2_BUFF_TYPE, V2_ELEMENT_PREFIX + "protection", V2_REMAIN_SHAPE };
            return new[] { runeId };
        }

        /// <summary>
        /// 버전 2 블록 해금 ID를 버전 3 해금 ID로 바꾼다. 잔류(shape.remain) 해금은 버전 2에서 버프에도 필요했으므로
        /// Persist와 Apply를 함께 해금한다. 버전 3 ID는 그대로 반환한다.
        /// </summary>
        private static IEnumerable<string> MapV2Unlock(string runeId)
        {
            if (V2_SHAPES.TryGetValue(runeId, out string shape)) return new[] { shape };
            if (V2_ELEMENTS.TryGetValue(runeId, out string element)) return new[] { SpellGrammar.ElementRune(element) };
            if (runeId == V2_REMAIN_SHAPE) return new[] { V2_BEHAVIORS[runeId], SpellGrammar.APPLY_RUNE };
            if (V2_BEHAVIORS.TryGetValue(runeId, out string behavior)) return new[] { behavior };
            return new[] { runeId };
        }

        /// <summary>이전 룬 노드를 버전 2 체인 블록으로 바꾼다.</summary>
        private static void ConvertLegacyRunes(SpellGraph graph)
        {
            foreach (GraphNode node in new List<GraphNode>(graph.Nodes))
            {
                if (node == null) continue;
                if (LEGACY_SHAPES.TryGetValue(node.RuneId, out string shape))
                    ConvertToChain(graph, node, "sphere", AttachedLegacyElement(graph, node), shape, null);
                else if (node.RuneId == SpellGrammar.INLINE_RUNE)
                    ConvertToChain(graph, node, node.GetText("magicType", "sphere"), node.GetText("element", "normal"),
                        node.GetText("form", "launch"), node);
                else if (node.RuneId == "act.shield") ConvertShield(graph, node);
                else if (node.RuneId == "act.blink") Bypass(graph, node);
                else if (node.RuneId == "mod.range") ConvertRange(graph, node);
                else if (node.RuneId == "mod.noise") graph.RemoveNode(node.Id);
            }
            foreach (GraphNode node in new List<GraphNode>(graph.Nodes))
                if (node != null && LEGACY_ELEMENTS.ContainsKey(node.RuneId)) graph.RemoveNode(node.Id);
        }

        /// <summary>
        /// 버전 2 체인을 버전 3 블록으로 바꾸고 그래프 버전을 3으로 올린다.
        /// 버프 체인은 Apply로, 속성 블록은 Shape의 element 값으로 접고, Behavior·Trigger 이름을 바꾼다.
        /// 버전 1은 실행 순서를 대상 ID 순서로 정했으므로 그 순서를 연결 순번(Order)으로 옮긴 뒤 올린다.
        /// </summary>
        private static void ConvertToVersion3(SpellGraph graph)
        {
            foreach (GraphNode node in new List<GraphNode>(graph.Nodes))
            {
                if (node != null && node.RuneId == V2_BUFF_TYPE) ConvertBuffChain(graph, node);
            }
            foreach (GraphNode node in new List<GraphNode>(graph.Nodes))
            {
                if (node != null && V2_SHAPES.ContainsKey(node.RuneId)) FoldElement(graph, node);
            }
            foreach (GraphNode node in new List<GraphNode>(graph.Nodes))
            {
                if (node == null) continue;
                if (V2_BEHAVIORS.TryGetValue(node.RuneId, out string behavior))
                    graph.ReplaceNode(new GraphNode(node.Id, behavior, node.X, node.Y, node.Params));
                else if (V2_ELEMENTS.TryGetValue(node.RuneId, out string element))
                    graph.ReplaceNode(new GraphNode(node.Id, SpellGrammar.ElementRune(element), node.X, node.Y, node.Params));
                else if (node.RuneId == SpellGrammar.CORE_RUNE && V2_TRIGGERS.TryGetValue(node.GetText(SpellGrammar.TRIGGER_PARAM), out string trigger))
                    node.SetText(SpellGrammar.TRIGGER_PARAM, trigger);
            }
            if (graph.Version == 1) AssignOrderFromTargets(graph);
            graph.SetVersion(SpellGraph.CURRENT_VERSION);
        }

        /// <summary>
        /// 버전 1의 실행 순서(같은 출력의 연결을 컴파일 대상 노드 ID 순으로 실행)를 연결 순번으로 바꾼다.
        /// 컴파일 대상은 Shape로 들어가는 연결이면 Shape가 합쳐질 Behavior이므로 그 ID를 기준으로 정렬한다.
        /// </summary>
        private static void AssignOrderFromTargets(SpellGraph graph)
        {
            var groups = new Dictionary<string, List<GraphEdge>>(StringComparer.Ordinal);
            foreach (GraphEdge edge in graph.Edges)
            {
                if (edge == null || edge.FromPort == SpellGrammar.MODIFIER_PORT || edge.FromPort == SpellGrammar.CHAIN_OUT) continue;
                string key = edge.FromNode + "\n" + edge.FromPort;
                if (!groups.TryGetValue(key, out List<GraphEdge> group))
                {
                    group = new List<GraphEdge>();
                    groups.Add(key, group);
                }
                group.Add(edge);
            }
            foreach (List<GraphEdge> group in groups.Values)
            {
                // OrderBy는 안정 정렬이라 같은 대상끼리는 저장 순서를 유지하며, 버전 1 컴파일러와 같은 순서가 된다.
                List<GraphEdge> ordered = group.OrderBy(edge => CompiledTarget(graph, edge.ToNode), StringComparer.Ordinal).ToList();
                for (int order = 0; order < ordered.Count; order++)
                {
                    GraphEdge edge = ordered[order];
                    graph.ReplaceEdge(edge.Id, new GraphEdge(edge.Id, edge.FromNode, edge.FromPort, edge.ToNode, edge.ToPort, order, edge.IsLegacyEvent));
                }
            }
        }

        /// <summary>Shape 노드면 체인으로 이어진 Behavior 노드 ID를, 아니면 노드 ID를 그대로 반환한다.</summary>
        private static string CompiledTarget(SpellGraph graph, string nodeId)
        {
            GraphNode node = graph.FindNode(nodeId);
            if (node == null || SpellGrammar.MagicTypeOf(node.RuneId) == null) return nodeId;
            return TrySingleChainTarget(graph, nodeId, out GraphEdge chain) ? chain.ToNode : nodeId;
        }

        /// <summary>
        /// [버프 타입] → [회복·보호 속성] → [잔류] 체인을 잔류 노드 ID의 Apply 노드 하나로 바꾼다.
        /// 타입으로 들어오던 실행 연결은 Apply로 옮기고, 속성의 효과량 파라미터를 Apply로 옮긴다. 형식이 다르면 바꾸지 않는다.
        /// </summary>
        private static void ConvertBuffChain(SpellGraph graph, GraphNode type)
        {
            if (!TrySingleChainTarget(graph, type.Id, out GraphEdge typeOut)) return;
            GraphNode element = graph.FindNode(typeOut.ToNode);
            if (element == null || (element.RuneId != "element.heal" && element.RuneId != "element.protection")) return;
            if (CountChainSources(graph, element.Id) != 1 || !TrySingleChainTarget(graph, element.Id, out GraphEdge elementOut)) return;
            GraphNode remain = graph.FindNode(elementOut.ToNode);
            if (remain == null || remain.RuneId != V2_REMAIN_SHAPE || CountChainSources(graph, remain.Id) != 1) return;

            var parameters = new List<NodeParameter> { new NodeParameter(SpellGrammar.ELEMENT_PARAM, 0f, V2_ELEMENTS[element.RuneId]) };
            parameters.AddRange(EffectParameters(element));
            parameters.AddRange(remain.Params);
            graph.ReplaceNode(new GraphNode(remain.Id, SpellGrammar.APPLY_RUNE, remain.X, remain.Y, parameters));
            foreach (GraphEdge edge in new List<GraphEdge>(graph.Edges))
            {
                if (edge.ToNode == type.Id && edge.ToPort == SpellGrammar.EXEC_PORT)
                    graph.ReplaceEdge(edge.Id, new GraphEdge(edge.Id, edge.FromNode, edge.FromPort, remain.Id, SpellGrammar.EXEC_PORT, edge.Order));
            }
            graph.RemoveNode(type.Id);
            graph.RemoveNode(element.Id);
        }

        /// <summary>
        /// 타입 블록을 같은 ID의 Shape 블록으로 바꾸고, 바로 뒤의 속성 블록이 하나의 입력·출력만 가지면 element 값과 효과량으로 접어 넣는다.
        /// 접은 속성 블록의 다음 연결은 Shape에서 출발하도록 바꾼다.
        /// </summary>
        private static void FoldElement(SpellGraph graph, GraphNode type)
        {
            var parameters = new List<NodeParameter>(type.Params);
            if (TrySingleChainTarget(graph, type.Id, out GraphEdge typeOut))
            {
                GraphNode element = graph.FindNode(typeOut.ToNode);
                if (element != null && V2_ELEMENTS.TryGetValue(element.RuneId, out string elementId)
                    && CountChainSources(graph, element.Id) == 1 && TrySingleChainTarget(graph, element.Id, out GraphEdge elementOut))
                {
                    parameters.Add(new NodeParameter(SpellGrammar.ELEMENT_PARAM, 0f, elementId));
                    parameters.AddRange(EffectParameters(element));
                    graph.ReplaceEdge(elementOut.Id, new GraphEdge(elementOut.Id, type.Id, SpellGrammar.CHAIN_OUT,
                        elementOut.ToNode, elementOut.ToPort, elementOut.Order));
                    graph.RemoveEdge(typeOut.Id);
                    graph.RemoveNode(element.Id);
                }
            }
            graph.ReplaceNode(new GraphNode(type.Id, V2_SHAPES[type.RuneId], type.X, type.Y, parameters));
        }

        /// <summary>노드의 효과량(power)·보호막 지속시간(buffDuration) 파라미터만 저장 순서대로 반환한다.</summary>
        private static IEnumerable<NodeParameter> EffectParameters(GraphNode node)
        {
            return node.Params.Where(param => param.Key == SpellGrammar.POWER_PARAM || param.Key == SpellGrammar.BUFF_DURATION_PARAM);
        }

        /// <summary>노드의 체인 출력(next) 연결이 정확히 하나면 그 엣지를 반환한다.</summary>
        private static bool TrySingleChainTarget(SpellGraph graph, string nodeId, out GraphEdge target)
        {
            target = null;
            foreach (GraphEdge edge in graph.Edges)
            {
                if (edge.FromNode != nodeId || edge.FromPort != SpellGrammar.CHAIN_OUT) continue;
                if (target != null)
                {
                    target = null;
                    return false;
                }
                target = edge;
            }
            return target != null;
        }

        /// <summary>노드로 들어오는 체인 입력(in) 연결 수를 반환한다.</summary>
        private static int CountChainSources(SpellGraph graph, string nodeId)
        {
            int count = 0;
            foreach (GraphEdge edge in graph.Edges)
            {
                if (edge.ToNode == nodeId && edge.ToPort == SpellGrammar.CHAIN_IN && edge.FromPort == SpellGrammar.CHAIN_OUT) count++;
            }
            return count;
        }

        /// <summary>그래프에 변환 대상 이전 룬이 있는지 반환한다.</summary>
        private static bool HasLegacyNode(SpellGraph graph)
        {
            foreach (GraphNode node in graph.Nodes)
            {
                if (node == null) continue;
                if (LEGACY_SHAPES.ContainsKey(node.RuneId) || LEGACY_ELEMENTS.ContainsKey(node.RuneId) || node.RuneId == SpellGrammar.INLINE_RUNE
                    || node.RuneId == "act.shield" || node.RuneId == "act.blink" || node.RuneId == "mod.range" || node.RuneId == "mod.noise")
                    return true;
            }
            return false;
        }

        /// <summary>형태 노드에 mod로 연결된 이전 속성 룬의 문법 속성 값을 반환하고 없으면 normal을 반환한다.</summary>
        private static string AttachedLegacyElement(SpellGraph graph, GraphNode form)
        {
            foreach (GraphEdge edge in graph.Edges)
            {
                if (edge.ToNode != form.Id || edge.ToPort != "mod") continue;
                GraphNode source = graph.FindNode(edge.FromNode);
                if (source != null && LEGACY_ELEMENTS.TryGetValue(source.RuneId, out string element)) return element;
            }
            return "normal";
        }

        /// <summary>
        /// 노드를 같은 ID의 형태 블록으로 바꾸고 앞에 마법 타입과(일반이 아니면) 속성 부여 블록을 체인으로 붙인다.
        /// 들어오던 실행 엣지는 타입 블록으로 옮기고 이전 속성 룬 연결은 제거한다.
        /// </summary>
        private static void ConvertToChain(SpellGraph graph, GraphNode node, string magicType, string element, string shape, GraphNode buffSource)
        {
            string typeId = UniqueNodeId(graph, node.Id + "-type");
            graph.AddNode(new GraphNode(typeId, V2_TYPE_PREFIX + magicType, node.X - BLOCK_SPACING * 2, node.Y + BLOCK_DROP));
            string chainTail = typeId;
            if (element != "normal")
            {
                string elementId = UniqueNodeId(graph, node.Id + "-attr");
                GraphNode elementNode = new GraphNode(elementId, V2_ELEMENT_PREFIX + element, node.X - BLOCK_SPACING, node.Y + BLOCK_DROP);
                if (buffSource != null)
                {
                    foreach (NodeParameter param in buffSource.Params)
                        if (param.Key == "power" || param.Key == "buffDuration") elementNode.SetNumber(param.Key, param.Number);
                }
                graph.AddNode(elementNode);
                graph.AddEdge(new GraphEdge(UniqueEdgeId(graph, node.Id + "-chain"), typeId, SpellGrammar.CHAIN_OUT, elementId, SpellGrammar.CHAIN_IN));
                chainTail = elementId;
            }
            graph.ReplaceNode(new GraphNode(node.Id, V2_SHAPE_PREFIX + shape, node.X, node.Y));
            foreach (GraphEdge edge in new List<GraphEdge>(graph.Edges))
            {
                if (edge.ToNode != node.Id) continue;
                if (edge.ToPort == "exec")
                {
                    graph.RemoveEdge(edge.Id);
                    graph.AddEdge(new GraphEdge(edge.Id, edge.FromNode, edge.FromPort, typeId, "exec", edge.Order));
                }
                else if (edge.ToPort == "mod" && LEGACY_ELEMENTS.ContainsKey(graph.FindNode(edge.FromNode)?.RuneId ?? "")) graph.RemoveEdge(edge.Id);
            }
            graph.AddEdge(new GraphEdge(UniqueEdgeId(graph, node.Id + "-chain"), chainTail, SpellGrammar.CHAIN_OUT, node.Id, SpellGrammar.CHAIN_IN));
        }

        /// <summary>방벽 행동을 버프·보호·잔류 체인으로 바꾸고 기존 다음 실행 분기를 이전 완료 포트(onComplete)로 옮긴다.</summary>
        private static void ConvertShield(SpellGraph graph, GraphNode node)
        {
            GraphNode source = new GraphNode(node.Id, node.RuneId, node.X, node.Y);
            source.SetNumber("power", SHIELD_POWER);
            source.SetNumber("buffDuration", SHIELD_SECONDS);
            ConvertToChain(graph, node, "buff", "protection", "remain", source);
            foreach (GraphEdge edge in new List<GraphEdge>(graph.Edges))
            {
                if (edge.FromNode != node.Id || edge.FromPort != "next") continue;
                graph.RemoveEdge(edge.Id);
                graph.AddEdge(new GraphEdge(edge.Id, edge.FromNode, SpellGrammar.LEGACY_COMPLETE_PORT, edge.ToNode, edge.ToPort, edge.Order));
            }
        }

        /// <summary>문법에 없는 점멸 노드를 제거하고 앞 실행 출력을 뒤 실행 대상에 직접 연결한다.</summary>
        private static void Bypass(SpellGraph graph, GraphNode node)
        {
            List<GraphEdge> incoming = new List<GraphEdge>();
            List<GraphEdge> outgoing = new List<GraphEdge>();
            foreach (GraphEdge edge in graph.Edges)
            {
                if (edge.ToNode == node.Id && edge.ToPort == "exec") incoming.Add(edge);
                if (edge.FromNode == node.Id && edge.FromPort == "next") outgoing.Add(edge);
            }
            graph.RemoveNode(node.Id);
            foreach (GraphEdge input in incoming)
                foreach (GraphEdge output in outgoing)
                    graph.AddEdge(new GraphEdge(UniqueEdgeId(graph, input.Id + "-bypass"), input.FromNode, input.FromPort,
                        output.ToNode, output.ToPort, graph.NextEdgeOrder(input.FromNode, input.FromPort)));
        }

        /// <summary>사거리 수식을 같은 배율의 지속시간 효과로 바꾼다.</summary>
        private static void ConvertRange(SpellGraph graph, GraphNode node)
        {
            GraphNode duration = new GraphNode(node.Id, "mod.duration", node.X, node.Y);
            duration.SetNumber("durationScale", node.GetNumber("rangeScale", 1.5f));
            graph.ReplaceNode(duration);
        }

        /// <summary>그래프에 없는 노드 ID를 기준 ID에 번호를 붙여 반환한다.</summary>
        private static string UniqueNodeId(SpellGraph graph, string baseId)
        {
            string id = baseId;
            for (int index = 1; graph.FindNode(id) != null; index++) id = baseId + index;
            return id;
        }

        /// <summary>그래프에 없는 엣지 ID를 기준 ID에 번호를 붙여 반환한다.</summary>
        private static string UniqueEdgeId(SpellGraph graph, string baseId)
        {
            string id = baseId;
            for (int index = 1; ContainsEdge(graph, id); index++) id = baseId + index;
            return id;
        }

        /// <summary>엣지 ID가 그래프에 있는지 반환한다.</summary>
        private static bool ContainsEdge(SpellGraph graph, string id)
        {
            foreach (GraphEdge edge in graph.Edges) if (edge.Id == id) return true;
            return false;
        }
    }
}
