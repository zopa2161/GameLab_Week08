using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RuneCode
{
    public static class GraphCompiler
    {
        private const string FORM_RUNE_PREFIX = "form.";

        /// <summary>
        /// 그래프와 해금·에너지 조건을 검증하고 최악 비용의 실행 트리를 컴파일한다.
        /// modifierStock이 있으면 현재 프리셋의 모디파이어·속성·행동 배치 수가 소지량을 넘을 때 E21 오류로 낸다. null이면 검사하지 않는다.
        /// isSingleBehavior가 true면 시작 노드마다 루트 행동을 하나로 제한(E26)하고 통과한 루트 행동의 OnHit 맨 앞에 자기 복제를 넣는다.
        /// elementCostReduction은 전투 속성(fire·ice·arc)이 붙은 노드 비용에서 뺄 감소량이다.
        /// elementSlotBonus는 속성 슬롯 확장 강화로 모든 행동 등급에 더할 속성 슬롯 수다.
        /// </summary>
        public static CompileResult Compile(SpellGraph graph, RuneCatalog runes, GrammarLimits limits,
            IEnumerable<string> unlocked, float maxEnergy = -1f, IEnumerable<SpellGraph> library = null,
            IReadOnlyDictionary<string, int> modifierStock = null, IReadOnlyDictionary<string, float> maxResourceCosts = null,
            bool isSingleBehavior = false, float elementCostReduction = 0f, int elementSlotBonus = 0)
        {
            CompileContext context = new CompileContext(unlocked ?? runes.StartRunes, library, graph, maxEnergy,
                maxResourceCosts, isSingleBehavior, elementCostReduction, elementSlotBonus);
            List<CompileIssue> dependencyErrors = new List<CompileIssue>();
            ValidateSpellDependencies(graph, context.Library, new HashSet<string>(StringComparer.Ordinal),
                new List<string>(), dependencyErrors);
            if (dependencyErrors.Count > 0) return new CompileResult(dependencyErrors, new List<CompileIssue>(), null);
            CompileResult result = CompileGraph(graph, runes, limits, context);
            List<CompileIssue> stockErrors = modifierStock == null ? null : FindModifierStockIssues(graph, runes, modifierStock);
            if (stockErrors == null || stockErrors.Count == 0) return result;
            return new CompileResult(result.Errors.Concat(stockErrors).ToList(), result.Warnings, result.Spell, result.Entries);
        }

        /// <summary>
        /// 그래프에서 소지량 키별 사용 수를 세어 반환한다. behavior 노드는 (행동, 등급) 키 하나에, 비어 있지 않은 속성 슬롯은
        /// 속성 룬 키(등급 빈 문자열), 모디 슬롯은 슬롯 값 그대로의 키를 센다. 존재하지 않는 룬이나 등급은 세지 않는다.
        /// </summary>
        public static Dictionary<string, int> CountStockUsage(SpellGraph graph, RuneCatalog runes)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (graph?.Nodes == null) return counts;
            foreach (GraphNode node in graph.Nodes)
            {
                if (node == null || !runes.TryGet(node.RuneId, out RuneDefinition rune)
                    || rune.Category != SpellGrammar.CATEGORY_BEHAVIOR) continue;
                string grade = node.GetText(SpellGrammar.MODIFIER_GRADE_PARAM, SpellGrammar.DEFAULT_BEHAVIOR_GRADE);
                if (runes.TryGetBehaviorSlots(grade, out _, out _)) AddStockCount(counts, SpellGrammar.ModifierStockKey(node.RuneId, grade));
                for (int index = 0; index < SpellGrammar.MAX_ELEMENT_SLOTS; index++)
                {
                    string value = node.GetText(SpellGrammar.ElementSlotKey(index), null);
                    if (!string.IsNullOrEmpty(value) && runes.TryGetElement(value, out ElementDefinition element))
                        AddStockCount(counts, SpellGrammar.ModifierStockKey(element.RuneId, ""));
                }
                for (int index = 0; index < SpellGrammar.MAX_MODIFIER_SLOTS; index++)
                {
                    string value = node.GetText(SpellGrammar.ModifierSlotKey(index), null);
                    if (string.IsNullOrEmpty(value) || !SpellGrammar.TryParseStockKey(value, out string modifierId, out string modifierGrade)
                        || !runes.TryGet(modifierId, out RuneDefinition modifierRune)
                        || modifierRune.Category != SpellGrammar.CATEGORY_MODIFIER
                        || !runes.ModifierGrades.IsGradeAvailable(modifierId, modifierGrade)) continue;
                    AddStockCount(counts, value);
                }
            }
            return counts;
        }

        /// <summary>소지량 사용 수 맵에 키 하나를 더한다.</summary>
        private static void AddStockCount(Dictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        /// <summary>소지량 사용 수 중 Modifier 카테고리 키만 골라 반환한다.</summary>
        public static Dictionary<string, int> CountModifiers(SpellGraph graph, RuneCatalog runes)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> pair in CountStockUsage(graph, runes))
            {
                if (!SpellGrammar.TryParseStockKey(pair.Key, out string runeId, out _)
                    || !runes.TryGet(runeId, out RuneDefinition rune)
                    || rune.Category != SpellGrammar.CATEGORY_MODIFIER) continue;
                counts[pair.Key] = pair.Value;
            }
            return counts;
        }

        /// <summary>
        /// 그래프 자체의 소지량 키(모디·속성·행동)별 사용 수가 소지량을 넘는 키마다 이 그래프의 첫 사용 노드에 E21 오류를 만든다.
        /// 프리셋별 소지량을 검사하므로 보관함의 다른 마법 배치는 합산하지 않는다. 소지량에 없는 키의 소지량은 0이다.
        /// </summary>
        private static List<CompileIssue> FindModifierStockIssues(SpellGraph graph, RuneCatalog runes,
            IReadOnlyDictionary<string, int> modifierStock)
        {
            Dictionary<string, int> used = CountStockUsage(graph, runes);
            var issues = new List<CompileIssue>();
            foreach (KeyValuePair<string, int> pair in used)
            {
                modifierStock.TryGetValue(pair.Key, out int stock);
                if (pair.Value <= stock) continue;
                GraphNode node = FindStockUsageNode(graph, runes, pair.Key);
                if (node == null) continue;
                string runeId = pair.Key;
                string grade = "";
                int separator = pair.Key.LastIndexOf('#');
                if (separator >= 0)
                {
                    runeId = pair.Key.Substring(0, separator);
                    grade = pair.Key.Substring(separator + 1);
                }
                string detail = runes.TryGet(runeId, out RuneDefinition rune) ? rune.Name : runeId;
                if (!string.IsNullOrEmpty(grade)) detail += " [" + grade + "]";
                issues.Add(new CompileIssue("E21", node.Id, detail + " " + pair.Value + "/" + stock));
            }
            return issues;
        }

        /// <summary>지정 소지량 키를 사용하는(행동 자신·속성·모디 슬롯) 그래프의 첫 behavior 노드를 반환한다.</summary>
        private static GraphNode FindStockUsageNode(SpellGraph graph, RuneCatalog runes, string key)
        {
            foreach (GraphNode node in graph.Nodes)
            {
                if (node == null || !runes.TryGet(node.RuneId, out RuneDefinition rune)
                    || rune.Category != SpellGrammar.CATEGORY_BEHAVIOR) continue;
                if (UsesStockKey(node, runes, key)) return node;
            }
            return null;
        }

        /// <summary>behavior 노드가 지정 소지량 키(행동 자신·속성 슬롯·모디 슬롯)를 쓰는지 반환한다.</summary>
        private static bool UsesStockKey(GraphNode node, RuneCatalog runes, string key)
        {
            string grade = node.GetText(SpellGrammar.MODIFIER_GRADE_PARAM, SpellGrammar.DEFAULT_BEHAVIOR_GRADE);
            if (runes.TryGetBehaviorSlots(grade, out _, out _)
                && SpellGrammar.ModifierStockKey(node.RuneId, grade) == key) return true;
            for (int index = 0; index < SpellGrammar.MAX_ELEMENT_SLOTS; index++)
            {
                string value = node.GetText(SpellGrammar.ElementSlotKey(index), null);
                if (!string.IsNullOrEmpty(value) && runes.TryGetElement(value, out ElementDefinition element)
                    && SpellGrammar.ModifierStockKey(element.RuneId, "") == key) return true;
            }
            for (int index = 0; index < SpellGrammar.MAX_MODIFIER_SLOTS; index++)
            {
                if (node.GetText(SpellGrammar.ModifierSlotKey(index), null) == key) return true;
            }
            return false;
        }

        /// <summary>유효한 종속성을 가진 그래프를 순환 검사 스택에 넣고 한 번 컴파일한다.</summary>
        private static CompileResult CompileGraph(SpellGraph graph, RuneCatalog runes, GrammarLimits limits, CompileContext context)
        {
            if (graph == null)
            {
                return new CompileResult(new List<CompileIssue> { new CompileIssue("E1") }, new List<CompileIssue>(), null);
            }
            if (!string.IsNullOrEmpty(graph.Id) && context.Compiled.TryGetValue(graph.Id, out CompiledSpell cached))
                return new CompileResult(new List<CompileIssue>(), new List<CompileIssue>(), cached);
            if (!string.IsNullOrEmpty(graph.Id) && context.ActiveSpellIds.Contains(graph.Id))
                return new CompileResult(new List<CompileIssue> { new CompileIssue("E12", null, graph.Id) }, new List<CompileIssue>(), null);
            if (!string.IsNullOrEmpty(graph.Id)) context.ActiveSpellIds.Add(graph.Id);
            List<CompileIssue> chainErrors = new List<CompileIssue>();
            List<CompileIssue> chainWarnings = new List<CompileIssue>();
            SpellGraph normalized = SpellChainNormalizer.Normalize(graph, runes, chainErrors, chainWarnings,
                out Dictionary<string, List<GraphNode>> slotModifiers);
            CompileResult result = chainErrors.Any(issue => issue.Code != "E10")
                ? new CompileResult(chainErrors, chainWarnings, null)
                : CompileGraphBody(normalized, runes, limits, context, chainErrors, chainWarnings, slotModifiers);
            if (!string.IsNullOrEmpty(graph.Id)) context.ActiveSpellIds.Remove(graph.Id);
            if (result.Ok && !string.IsNullOrEmpty(graph.Id)) context.Compiled[graph.Id] = result.Spell;
            return result;
        }

        /// <summary>
        /// 슬롯 정규화를 마친 그래프의 해금·에너지 조건을 검사하고 시작 노드별 실행 트리 및 비용 상한을 계산한다.
        /// 정규화 단계의 오류·경고 목록에 이어서 기록하며 slotModifiers는 behavior 노드별 합성 Modifier 노드 목록이다.
        /// 성공하면 시작 노드별 Entry 목록(0번이 core.cast)을 담은 CompileResult를 반환한다.
        /// </summary>
        private static CompileResult CompileGraphBody(SpellGraph graph, RuneCatalog runes, GrammarLimits limits, CompileContext context,
            List<CompileIssue> errors, List<CompileIssue> warnings,
            Dictionary<string, List<GraphNode>> slotModifiers)
        {
            if (graph.Version != SpellGraph.CURRENT_VERSION || graph.Nodes == null || graph.Edges == null
                || graph.Nodes.Count > limits.MaxGraphNodes || graph.Edges.Count > limits.MaxGraphEdges)
            {
                errors.Add(new CompileIssue("E9"));
                return new CompileResult(errors, warnings, null);
            }
            Dictionary<string, GraphNode> nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
            Dictionary<string, RuneDefinition> definitions = new Dictionary<string, RuneDefinition>(StringComparer.Ordinal);
            List<GraphNode> cores = new List<GraphNode>();
            foreach (GraphNode node in graph.Nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.Id) || !nodes.TryAdd(node.Id, node)
                    || !runes.TryGet(node.RuneId, out RuneDefinition rune))
                {
                    errors.Add(new CompileIssue("E9", node?.Id));
                    continue;
                }
                definitions[node.Id] = rune;
                if (rune.Category == "core") cores.Add(node);
                bool isAllowedRune = !rune.IsDisabled && (rune.Category == SpellGrammar.CATEGORY_CORE
                    || rune.Category == SpellGrammar.CATEGORY_BEHAVIOR || rune.Category == SpellGrammar.CATEGORY_INTERNAL);
                if (!isAllowedRune) errors.Add(new CompileIssue("E25", node.Id));
                if (!context.UnlockedIds.Contains(rune.Id)) errors.Add(new CompileIssue("E10", node.Id));
                ValidateParameters(node, rune, runes.ModifierGrades, errors);
                ValidateMagicNode(node, rune, errors);
            }
            ValidateStartNodes(cores, errors);
            ValidateEdges(graph, definitions, errors);
            if (!errors.Any(issue => issue.Code == "E3") && HasCycle(graph, nodes)) errors.Add(new CompileIssue("E4"));
            bool hasStructuralError = errors.Any(issue => issue.Code != "E10");
            if (hasStructuralError) return new CompileResult(errors, warnings, null);

            List<GraphNode> startNodes = OrderStartNodes(runes, cores);
            HashSet<string> reachable = new HashSet<string>();
            foreach (GraphNode start in startNodes)
                FindReachable(graph, start.Id, false, reachable, definitions, warnings);
            foreach (GraphNode node in graph.Nodes)
            {
                RuneDefinition rune = definitions[node.Id];
                if (!reachable.Contains(node.Id))
                {
                    if (rune.Id == "flow.if" && node.GetText("type", "targetHpBelow") != "selfHpBelow")
                        warnings.Add(new CompileIssue("W3", node.Id));
                }
            }
            List<CompiledSpell> entries = new List<CompiledSpell>();
            long actionCeiling = limits.MaxCompiledActions;
            foreach (GraphNode start in startNodes)
            {
                List<SpellAction> root = BuildBranch(graph, start.Id, "exec", nodes, definitions, warnings, errors, runes, limits, context, slotModifiers);
                if (errors.Any(issue => issue.Code == "E11" || issue.Code == "E12" || issue.Code == "E14" || issue.Code == "E15"))
                    return new CompileResult(errors, warnings, null);
                if (context.IsSingleBehavior && root.Count > 1)
                {
                    errors.Add(new CompileIssue("E26", start.Id));
                    continue;
                }
                if (context.IsSingleBehavior && root.Count == 1)
                    DuplicateRootAction(graph, start.Id, root[0], nodes, definitions, runes, limits, context, slotModifiers);
                entries.Add(BuildEntry(graph, limits, context, start, root, actionCeiling, warnings));
            }
            if (errors.Any(issue => issue.Code == "E26")) return new CompileResult(errors, warnings, null);
            return new CompileResult(errors, warnings, entries[0], entries);
        }

        /// <summary>
        /// 행동 하나 고정 강화가 켜진 시작 노드의 루트 행동을 같은 노드로 다시 빌드해 복제를 만들고 원본 행동의 OnHit 목록 맨 앞에 넣는다.
        /// 복제는 원본의 원래 OnHit 자식을 그대로 가지고 나머지 분기는 원본과 공유하며 자기 안에는 복제를 다시 두지 않는다(자기 재실행 1회).
        /// </summary>
        private static void DuplicateRootAction(SpellGraph graph, string startNodeId, SpellAction original,
            Dictionary<string, GraphNode> nodes, Dictionary<string, RuneDefinition> definitions, RuneCatalog runes,
            GrammarLimits limits, CompileContext context, Dictionary<string, List<GraphNode>> slotModifiers)
        {
            // 원본 빌드에서 이미 경고·오류를 기록했으므로 재빌드의 기록은 버린다.
            List<SpellAction> rebuilt = BuildBranch(graph, startNodeId, "exec", nodes, definitions, new List<CompileIssue>(),
                new List<CompileIssue>(), runes, limits, context, slotModifiers);
            SpellAction clone = rebuilt[0];
            // 같은 노드로 다시 빌드하므로 스탯·비용은 원본과 같다. 복제의 모든 분기는 원본의 목록을 그대로 공유한다.
            clone.SetBranch(SpellGrammar.ON_HIT_PORT, original.OnHit);
            clone.SetBranch(SpellGrammar.ON_EXPIRE_PORT, original.OnExpire);
            clone.SetBranch(SpellGrammar.ON_FIRST_HIT_OR_EXPIRE_PORT, original.OnFirstHitOrExpire);
            clone.SetBranch("then", original.Then);
            clone.SetBranch("else", original.Else);
            clone.SetBranch("body", original.Body);
            clone.SetBranch(SpellGrammar.ON_COMPLETE_PORT, original.OnComplete);
            clone.SetBranch("next", original.Next);
            List<SpellAction> onHitWithClone = new List<SpellAction>(original.OnHit.Count + 1) { clone };
            onHitWithClone.AddRange(original.OnHit);
            original.SetBranch(SpellGrammar.ON_HIT_PORT, onHitWithClone);
        }

        /// <summary>
        /// 시작 노드 배치 규칙을 검사한다. core.cast는 정확히 1개여야 하고(어긋나면 E1), 나머지 시작 노드 룬은
        /// 종류별 최대 1개다(넘는 노드에 E27). 시작 노드 총수가 MAX_START_NODES를 넘으면 E9를 낸다.
        /// </summary>
        private static void ValidateStartNodes(List<GraphNode> cores, List<CompileIssue> errors)
        {
            if (cores.Count > SpellGrammar.MAX_START_NODES) errors.Add(new CompileIssue("E9"));
            bool hasCast = false;
            var seenStartIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (GraphNode node in cores)
            {
                if (node.RuneId == SpellGrammar.CORE_RUNE)
                {
                    if (hasCast) errors.Add(new CompileIssue("E1"));
                    hasCast = true;
                    continue;
                }
                if (!seenStartIds.Add(node.RuneId)) errors.Add(new CompileIssue("E27", node.Id));
            }
            if (!hasCast) errors.Add(new CompileIssue("E1"));
        }

        /// <summary>
        /// 시작 노드를 Entry 순서로 정렬해 반환한다. core.cast가 항상 첫 번째이고 나머지는 룬 카탈로그(runes.json) 순서이며,
        /// 같은 순서면 노드 ID 순으로 정렬해 결과를 결정적으로 유지한다.
        /// </summary>
        private static List<GraphNode> OrderStartNodes(RuneCatalog runes, List<GraphNode> cores)
        {
            return cores.OrderBy(node => node.RuneId == SpellGrammar.CORE_RUNE ? -1 : CatalogOrder(runes, node.RuneId))
                .ThenBy(node => node.Id, StringComparer.Ordinal).ToList();
        }

        /// <summary>룬 ID의 카탈로그 등장 순서를 반환한다. 카탈로그에 없는 룬은 끝으로 밀어낸다.</summary>
        private static int CatalogOrder(RuneCatalog runes, string runeId)
        {
            for (int index = 0; index < runes.All.Count; index++)
                if (runes.All[index].Id == runeId) return index;
            return int.MaxValue;
        }

        /// <summary>시작 노드의 실행 트리거를 반환한다. core.cast는 trigger 파라미터 값을, 나머지 시작 노드는 룬별 고정 트리거를 쓴다.</summary>
        private static string StartNodeTrigger(GraphNode start)
        {
            switch (start.RuneId)
            {
                case SpellGrammar.DASH_END_CORE_RUNE: return SpellGrammar.TRIGGER_ON_DASH_END;
                case SpellGrammar.STATUS_STACK_CORE_RUNE: return SpellGrammar.TRIGGER_ON_STATUS_STACK;
                case SpellGrammar.RIGHT_CLICK_CORE_RUNE: return SpellGrammar.TRIGGER_ON_RIGHT_CLICK;
                default: return start.GetText(SpellGrammar.TRIGGER_PARAM, SpellGrammar.TRIGGER_ON_ATTACK);
            }
        }

        /// <summary>
        /// 시작 노드 하나의 실행 트리를 최악 비용·실행 비용·개체 수·태그와 함께 CompiledSpell로 만든다.
        /// 쿨다운은 기본 쿨다운을 넣고 실행 시간은 CompiledSpell이 시전 시점에 더한다.
        /// 빔 유지 비용 무한 표시는 트리거가 onAttack인 Entry에만 적용하며, 최대 예상 비용(W8·W7)·실행 수(W11)·
        /// 동시 개체 수(W10) 경고를 같은 코드·상세값이 중복되지 않게 검사한다.
        /// </summary>
        private static CompiledSpell BuildEntry(SpellGraph graph, GrammarLimits limits, CompileContext context,
            GraphNode start, List<SpellAction> root, long actionCeiling, List<CompileIssue> warnings)
        {
            string trigger = StartNodeTrigger(start);
            ResourceCostSet resourceCosts = Cost(root, allowBeamHold: trigger == SpellGrammar.TRIGGER_ON_ATTACK);
            ResourceCostSet castCosts = Cost(root, includeEvents: false);
            int entities = EntityCount(root);
            long expandedActions = AddBounded(1L, ActionCount(root, actionCeiling + 1L), actionCeiling + 1L);
            // 최대 예상 비용·실행 수·동시 개체 수는 최악 분기 추정이라 시전을 막지 않고 경고만 낸다(백서 7.2).
            // 실제 실행은 런타임의 틱당 실행 상한·예약 실행 상한·동시 개체 상한이 안전을 맡는다.
            if (expandedActions > actionCeiling) AddWarningOnce(warnings, new CompileIssue("W11"));
            foreach (ResourceAmount cost in resourceCosts.Amounts)
            {
                if (double.IsInfinity(cost.Amount) || double.IsNaN(cost.Amount))
                {
                    AddWarningOnce(warnings, new CompileIssue("W8", null, cost.Resource));
                }
                else if (context.TryGetMaxResource(cost.Resource, limits, out float maximum) && cost.Amount > maximum)
                {
                    AddWarningOnce(warnings, new CompileIssue("W7", null, cost.Resource + "="
                        + cost.Amount.ToString("0.#", CultureInfo.InvariantCulture) + "/" + maximum.ToString("0.#", CultureInfo.InvariantCulture)));
                }
            }
            if (entities > limits.MaxLiveSpellEntities) AddWarningOnce(warnings, new CompileIssue("W10"));
            SortedSet<string> tags = new SortedSet<string>(StringComparer.Ordinal);
            GatherTags(root, tags);
            return new CompiledSpell(graph.Name, Signature(graph, root), start.Id, resourceCosts,
                limits.CooldownBase, entities, tags.ToList(), root, graph.Id, trigger, castCosts);
        }

        /// <summary>같은 코드·상세값의 경고가 이미 있으면 무시하고 없으면 추가한다. 시작 노드별 검사에서 중복 경고를 막는다.</summary>
        private static void AddWarningOnce(List<CompileIssue> warnings, CompileIssue issue)
        {
            foreach (CompileIssue existing in warnings)
                if (existing.Code == issue.Code && existing.Detail == issue.Detail) return;
            warnings.Add(issue);
        }

        /// <summary>연결 후보가 포트 존재·exec 종류·비활성 룬·중복·입력 수·순환 규칙을 지키는지 반환한다.</summary>
        public static bool CanConnect(SpellGraph graph, GraphEdge edge, RuneCatalog runes, out CompileIssue issue)
        {
            issue = null;
            GraphNode from = graph.FindNode(edge.FromNode);
            GraphNode to = graph.FindNode(edge.ToNode);
            if (from == null || to == null || !runes.TryGet(from.RuneId, out RuneDefinition fromRune)
                || !runes.TryGet(to.RuneId, out RuneDefinition toRune))
            {
                issue = new CompileIssue("E3", edge.ToNode);
                return false;
            }
            PortDefinition output = fromRune.FindPort(edge.FromPort, "out");
            PortDefinition input = toRune.FindPort(edge.ToPort, "in");
            if (output == null || input == null || output.Kind != SpellGrammar.EXEC_KIND || input.Kind != SpellGrammar.EXEC_KIND)
                issue = new CompileIssue("E3", edge.ToNode);
            else if (fromRune.IsDisabled)
                issue = new CompileIssue("E25", edge.FromNode);
            else if (toRune.IsDisabled)
                issue = new CompileIssue("E25", edge.ToNode);
            else if (graph.Edges.Any(current => current.FromNode == edge.FromNode && current.FromPort == edge.FromPort
                && current.Order == edge.Order))
                issue = new CompileIssue("E9", edge.ToNode);
            else if (graph.Edges.Any(current => current.FromNode == edge.FromNode && current.FromPort == edge.FromPort
                && current.ToNode == edge.ToNode && current.ToPort == edge.ToPort))
                issue = new CompileIssue("E9", edge.ToNode);
            else if (graph.Edges.Any(current => current.ToNode == edge.ToNode && current.ToPort == edge.ToPort))
                issue = new CompileIssue("E2", edge.ToNode);
            else if (HasPath(graph, edge.ToNode, edge.FromNode, new HashSet<string>()))
                issue = new CompileIssue("E4", edge.ToNode);
            return issue == null;
        }

        /// <summary>접속 후보 검증을 수행하고 연결 불가 사유를 반환한다.</summary>
        public static bool ValidateConnection(SpellGraph graph, GraphEdge edge, RuneCatalog runes, out CompileIssue issue)
            => CanConnect(graph, edge, runes, out issue);

        /// <summary>효과 입력에는 Behavior 블록·프리셋 호출(및 내부 Inline Magic)에 효과 룬만 연결할 수 있는지 반환한다.</summary>
        internal static bool CanAttachModifier(RuneDefinition source, RuneDefinition target)
        {
            if (target.Category == SpellGrammar.CATEGORY_BEHAVIOR || target.Id == SpellGrammar.CALL_RUNE || target.Id == SpellGrammar.INLINE_RUNE)
                return source.Category == "modifier";
            return target.Category == "form" && (source.Category == "modifier" || source.Category == "element");
        }

        /// <summary>
        /// Inline Magic의 종류·속성 조합을 검사한다. 해금은 문법 블록 단계에서 검사한다.
        /// 시뮬레이션이 지원하지 않는 buff 종류와 회복·보호 속성은 E14로 거부한다.
        /// </summary>
        private static void ValidateMagicNode(GraphNode node, RuneDefinition rune, List<CompileIssue> errors)
        {
            if (rune.Id != "magic.inline") return;
            string magicType = node.GetText("magicType", "sphere");
            string element = node.GetText("element", "normal");
            if (magicType == SpellGrammar.MAGIC_TYPE_BUFF || element == "heal" || element == "protection")
                errors.Add(new CompileIssue("E14", node.Id));
        }

        /// <summary>Inline Magic의 form 값에 해당하는 행동 수치 룬 ID(form.bolt 등)를 반환한다. 알 수 없는 form이면 빈 문자열이다.</summary>
        private static string EffectiveFormId(GraphNode node, RuneDefinition rune)
        {
            if (rune.Id != "magic.inline") return rune.Category == "form" ? rune.Id : "";
            string kind = SpellGrammar.KindOf(node.GetText("form", "launch"));
            return string.IsNullOrEmpty(kind) ? "" : FORM_RUNE_PREFIX + kind;
        }

        /// <summary>라이브러리 참조를 따라가며 없는 Spell ID와 직접·간접 재귀를 검사한다.</summary>
        private static void ValidateSpellDependencies(SpellGraph graph,
            Dictionary<string, SpellGraph> library, HashSet<string> visited, List<string> active, List<CompileIssue> errors)
        {
            if (graph == null) return;
            if (!string.IsNullOrEmpty(graph.Id))
            {
                if (visited.Contains(graph.Id)) return;
                active.Add(graph.Id);
            }
            if (graph.Nodes != null)
            {
                foreach (GraphNode node in graph.Nodes)
                {
                    if (node == null || node.RuneId != "spell.call") continue;
                    string spellId = node.GetText("spellId").Trim();
                    if (!IsSpellId(spellId) || !library.TryGetValue(spellId, out SpellGraph target))
                    {
                        errors.Add(new CompileIssue("E11", node.Id, spellId));
                        continue;
                    }
                    if (active.Contains(spellId))
                    {
                        errors.Add(new CompileIssue("E12", node.Id, spellId));
                        continue;
                    }
                    ValidateSpellDependencies(target, library, visited, active, errors);
                }
            }
            if (!string.IsNullOrEmpty(graph.Id))
            {
                active.RemoveAt(active.Count - 1);
                visited.Add(graph.Id);
            }
        }

        /// <summary>Spell ID가 JSON 및 라이브러리 참조에서 허용되는 문자만 포함하는지 반환한다.</summary>
        private static bool IsSpellId(string spellId)
        {
            if (string.IsNullOrWhiteSpace(spellId) || spellId.Length > 80) return false;
            foreach (char character in spellId)
                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')) && character != '.' && character != '_' && character != '-') return false;
            return true;
        }

        /// <summary>각 노드의 숫자·열거 파라미터, Modifier 등급 범위와 중복 키를 검증한다.</summary>
        private static void ValidateParameters(GraphNode node, RuneDefinition rune, ModifierGradeTable modifierGrades, List<CompileIssue> errors)
        {
            if (node.Params == null)
            {
                errors.Add(new CompileIssue("E9", node.Id));
                return;
            }
            HashSet<string> seen = new HashSet<string>();
            foreach (NodeParameter value in node.Params)
            {
                if (value == null || string.IsNullOrEmpty(value.Key))
                {
                    errors.Add(new CompileIssue("E9", node.Id));
                    continue;
                }
                ParameterDefinition definition = rune.Params.FirstOrDefault(param => param.Id == value.Key);
                bool isModifierGrade = value.Key == SpellGrammar.MODIFIER_GRADE_PARAM && rune.Category == SpellGrammar.CATEGORY_MODIFIER;
                bool isInvalid = !seen.Add(value.Key) || (!isModifierGrade && definition == null);
                if (!isInvalid && isModifierGrade)
                {
                    isInvalid = value.Text == null || modifierGrades == null || !modifierGrades.IsGradeAvailable(rune.Id, value.Text);
                }
                else if (!isInvalid && definition.Kind == "number")
                {
                    isInvalid = float.IsNaN(value.Number) || float.IsInfinity(value.Number)
                        || value.Number < definition.Min || value.Number > definition.Max;
                    if (value.Key == "times" && value.Number != (int)value.Number) isInvalid = true;
                }
                if (!isInvalid && !isModifierGrade && definition.Kind == "enum") isInvalid = !definition.Options.Contains(value.Text);
                if (!isInvalid && !isModifierGrade && definition.Kind == "text") isInvalid = value.Text != null && value.Text.Length > 80;
                if (isInvalid) errors.Add(new CompileIssue("E9", node.Id, value.Key));
            }
        }

        /// <summary>연결의 포트 종류·입력 수·실행 순번 오류를 추가한다.</summary>
        private static void ValidateEdges(SpellGraph graph, Dictionary<string, RuneDefinition> definitions,
            List<CompileIssue> errors)
        {
            HashSet<string> ids = new HashSet<string>();
            Dictionary<string, int> incoming = new Dictionary<string, int>();
            Dictionary<string, Dictionary<string, HashSet<int>>> execOrders = new Dictionary<string, Dictionary<string, HashSet<int>>>(StringComparer.Ordinal);
            foreach (GraphEdge edge in graph.Edges)
            {
                if (edge == null || string.IsNullOrEmpty(edge.Id) || string.IsNullOrEmpty(edge.FromNode) || string.IsNullOrEmpty(edge.ToNode)
                    || edge.Order < 0 || !ids.Add(edge.Id) || !definitions.TryGetValue(edge.FromNode, out RuneDefinition from)
                    || !definitions.TryGetValue(edge.ToNode, out RuneDefinition to))
                {
                    errors.Add(new CompileIssue("E3", edge?.ToNode));
                    continue;
                }
                PortDefinition output = from.FindPort(edge.FromPort, "out");
                PortDefinition input = to.FindPort(edge.ToPort, "in");
                if (output == null || input == null || output.Kind != input.Kind)
                {
                    errors.Add(new CompileIssue("E3", edge.ToNode));
                    continue;
                }
                if (output.Kind == "exec")
                {
                    if (!execOrders.TryGetValue(edge.FromNode, out Dictionary<string, HashSet<int>> ports))
                    {
                        ports = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
                        execOrders.Add(edge.FromNode, ports);
                    }
                    if (!ports.TryGetValue(edge.FromPort, out HashSet<int> orders))
                    {
                        orders = new HashSet<int>();
                        ports.Add(edge.FromPort, orders);
                    }
                    if (!orders.Add(edge.Order)) errors.Add(new CompileIssue("E9", edge.ToNode));
                }
                string portKey = edge.ToNode + ":" + edge.ToPort;
                int count = incoming.TryGetValue(portKey, out int previous) ? previous + 1 : 1;
                incoming[portKey] = count;
                if (input.Kind == "exec" && count > 1) errors.Add(new CompileIssue("E2", edge.ToNode));
            }
        }

        /// <summary>각 노드의 연결을 깊이 우선으로 검사하여 순환 여부를 반환한다.</summary>
        private static bool HasCycle(SpellGraph graph, Dictionary<string, GraphNode> nodes)
        {
            Dictionary<string, int> colors = new Dictionary<string, int>();
            foreach (string id in nodes.Keys)
                if (VisitCycle(graph, id, colors)) return true;
            return false;
        }

        /// <summary>방문 중인 노드로 다시 진입하는 경로를 순환으로 검출한다.</summary>
        private static bool VisitCycle(SpellGraph graph, string id, Dictionary<string, int> colors)
        {
            if (colors.TryGetValue(id, out int color)) return color == 1;
            colors[id] = 1;
            foreach (GraphEdge edge in graph.Edges)
                if (edge.FromNode == id && VisitCycle(graph, edge.ToNode, colors)) return true;
            colors[id] = 2;
            return false;
        }

        /// <summary>연결 시작 노드에서 대상 노드에 도달할 수 있는지 반환한다.</summary>
        private static bool HasPath(SpellGraph graph, string from, string target, HashSet<string> visited)
        {
            if (from == target) return true;
            if (!visited.Add(from)) return false;
            foreach (GraphEdge edge in graph.Edges)
                if (edge.FromNode == from && HasPath(graph, edge.ToNode, target, visited)) return true;
            return false;
        }

        /// <summary>실행 경로와 참조 수식을 수집하고 대상이 없는 조건에 경고를 추가한다.</summary>
        private static void FindReachable(SpellGraph graph, string id, bool hasTarget, HashSet<string> reachable,
            Dictionary<string, RuneDefinition> definitions, List<CompileIssue> warnings)
        {
            if (!reachable.Add(id)) return;
            if (definitions[id].Id == "flow.if" && graph.FindNode(id).GetText("type", "targetHpBelow") != "selfHpBelow" && !hasTarget)
                warnings.Add(new CompileIssue("W3", id));
            foreach (GraphEdge edge in graph.Edges)
            {
                if (edge.ToNode == id && edge.ToPort == "mod") reachable.Add(edge.FromNode);
                if (edge.FromNode != id || edge.FromPort == "mod") continue;
                // onFirstHitOrExpire는 소멸 시에도 실행되어 대상이 없을 수 있으므로 onExpire처럼 대상 없음으로 본다.
                bool nextHasTarget = edge.FromPort == SpellGrammar.ON_HIT_PORT
                    || (hasTarget && edge.FromPort != SpellGrammar.ON_EXPIRE_PORT && edge.FromPort != SpellGrammar.ON_FIRST_HIT_OR_EXPIRE_PORT);
                FindReachable(graph, edge.ToNode, nextHasTarget, reachable, definitions, warnings);
            }
        }

        /// <summary>출력 포트에 연결된 노드를 안정된 순서의 실행 명령으로 변환한다.</summary>
        private static List<SpellAction> BuildBranch(SpellGraph graph, string nodeId, string port,
            Dictionary<string, GraphNode> nodes, Dictionary<string, RuneDefinition> definitions, List<CompileIssue> warnings,
            List<CompileIssue> errors, RuneCatalog runes, GrammarLimits limits, CompileContext context,
            Dictionary<string, List<GraphNode>> slotModifiers)
        {
            List<SpellAction> actions = new List<SpellAction>();
            List<GraphEdge> branchEdges = graph.Edges.Select((edge, index) => new { edge, index })
                .Where(item => item.edge.FromNode == nodeId && item.edge.FromPort == port)
                .OrderBy(item => item.edge.Order).ThenBy(item => item.index).Select(item => item.edge).ToList();
            foreach (GraphEdge edge in branchEdges)
            {
                GraphNode node = nodes[edge.ToNode];
                RuneDefinition rune = definitions[node.Id];
                RuneDefinition element = null;
                RuneDefinition effectForm = rune.Category == "form" ? rune : null;
                CompiledSpell calledSpell = null;
                List<RuneDefinition> mods = new List<RuneDefinition>();
                List<GraphNode> modifierNodes = new List<GraphNode>();
                List<string> attachedNodeIds = new List<string>();
                if (slotModifiers != null && slotModifiers.TryGetValue(node.Id, out List<GraphNode> slotNodes))
                {
                    foreach (GraphNode modifierNode in slotNodes)
                    {
                        if (modifierNode == null || !runes.TryGet(modifierNode.RuneId, out RuneDefinition attached)) continue;
                        mods.Add(attached);
                        modifierNodes.Add(modifierNode);
                    }
                }
                if (rune.Id == "magic.inline")
                {
                    string formId = EffectiveFormId(node, rune);
                    if (!runes.TryGet(formId, out effectForm)) errors.Add(new CompileIssue("E14", node.Id));
                    string elementId = InlineElementRuneId(node.GetText("element", "normal"));
                    if (elementId != null && !runes.TryGet(elementId, out element)) errors.Add(new CompileIssue("E14", node.Id));
                }
                if (rune.Id == "spell.call")
                {
                    string spellId = node.GetText("spellId").Trim();
                    if (context.Compiled.TryGetValue(spellId, out CompiledSpell cached)) calledSpell = cached;
                    else if (context.Library.TryGetValue(spellId, out SpellGraph target))
                    {
                        CompileResult targetResult = CompileGraph(target, runes, limits, context);
                        if (targetResult.Ok) calledSpell = targetResult.Spell;
                        else errors.Add(new CompileIssue("E15", node.Id, target.Name, targetResult.Errors[0]));
                    }
                }
                RuneDefinition shape = null;
                RuneDefinition behavior = null;
                if (rune.Id == SpellGrammar.INLINE_RUNE && node.GetText("magicType", SpellGrammar.MAGIC_TYPE_SPHERE) != SpellGrammar.MAGIC_TYPE_BUFF)
                    runes.TryGet(SpellGrammar.ShapeRune(node.GetText("magicType", SpellGrammar.MAGIC_TYPE_SPHERE)), out shape);
                if (rune.Id == SpellGrammar.INLINE_RUNE)
                {
                    string behaviorId = node.GetText("magicType", SpellGrammar.MAGIC_TYPE_SPHERE) == SpellGrammar.MAGIC_TYPE_BUFF
                        ? SpellGrammar.APPLY_RUNE : SpellGrammar.BehaviorRune(node.GetText("form", "launch"));
                    runes.TryGet(behaviorId, out behavior);
                }
                else if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR) behavior = rune;
                float coneAngle = shape?.Stats.ConeAngle ?? 0f;
                SpellAction action = new SpellAction(node, rune, effectForm, element, mods, modifierNodes, calledSpell,
                    shape, coneAngle, runes.ModifierGrades, behavior,
                    effectForm?.Id == "form.beam" ? runes.Get("form.bolt").Stats.Radius : -1f, context.ElementCostReduction);
                action.SetAttachedNodes(attachedNodeIds);
                foreach (PortDefinition output in rune.Ports)
                {
                    if (output.Direction == "out" && output.Kind == "exec")
                        action.SetBranch(output.Id, BuildBranch(graph, node.Id, output.Id, nodes, definitions, warnings, errors, runes, limits, context, slotModifiers));
                }
                actions.Add(action);
            }
            return actions;
        }

        /// <summary>Inline Magic의 표준 원소를 기존 원소 룬 ID로 연결한다.</summary>
        private static string InlineElementRuneId(string element)
        {
            switch (element)
            {
                case "fire": return "elem.fire";
                case "electric": return "elem.arc";
                case "ice": return "elem.ice";
                default: return null;
            }
        }

        /// <summary>
        /// 개체 하나가 낼 수 있는 최대 OnHit 횟수를 행동 규칙에서 구하고 유한하면 true를 반환한다.
        /// 행동 규칙이 없는 종류(공전)는 대상마다 OnHit을 내므로 상한이 없어 false다.
        /// </summary>
        private static bool TryGetMaxHitEvents(SpellAction action, out int count)
        {
            count = 0;
            if (action.Rules == null || !action.Rules.TryGetMaxHitEvents(action.Stats, out count)) return false;
            foreach (SpellModifierRules modifierRules in action.ModifierRules)
                if (!modifierRules.TryModifyMaxHitEvents(action.Stats, ref count)) return false;
            return true;
        }

        /// <summary>행동 규칙상 명중 경로와 미명중 소멸 경로가 서로 배타적이라 큰 쪽만 세는지 반환한다.</summary>
        private static bool HasExclusiveHitAndExpire(SpellAction action) => action.Rules != null && action.Rules.HasExclusiveHitAndExpire;

        /// <summary>
        /// 실행 수·생성 수 안전 검사에 쓰는 개체당 OnHit 배수를 반환한다. 유한하면 최대 횟수이고, 상한이 없으면 이벤트 분기를 한 번으로 센다.
        /// </summary>
        private static int HitEventMultiplier(SpellAction action)
        {
            if (action.Form == BeamBehaviorRules.BEAM_KIND) return int.MaxValue;
            return TryGetMaxHitEvents(action, out int count) ? count : 1;
        }

        /// <summary>상한 없는 이벤트 후속 비용이 있으면 산정 불가로 표시하고, 없으면 빈 비용을 반환한다.</summary>
        private static ResourceCostSet UnboundedCost(ResourceCostSet chainCost)
            => HasCosts(chainCost) ? chainCost.MarkUnbounded() : ResourceCostSet.Empty;

        /// <summary>다중의 폭발 회차를 포함한 spawn 동작의 총 생성 수를 반환한다. 호출·적용 동작은 쓰지 않는다.</summary>
        private static long SpawnInstanceCount(SpellAction action)
            => (long)action.Count * action.WaveCount;

        /// <summary>
        /// onHit 전력 선결제와 나머지 자원의 최악 이벤트 비용을 합산한다. 선결제된 후속 전력에는 적중·생성 횟수를 곱하지 않는다.
        /// includeEvents가 false이면 시전 비용과 onHit 선결제만 포함하고 소멸·첫 이벤트의 별도 비용은 제외한다.
        /// allowBeamHold이면 현재 목록에 직접 연결된 빔의 총 유지 비용에 상한이 없음을 반영한다.
        /// </summary>
        private static ResourceCostSet Cost(IReadOnlyList<SpellAction> actions,
            SpellModifierValues inheritedModifiers = null, double costMultiplier = 1d, bool includeEvents = true, bool allowBeamHold = false,
            bool isManaPrepaid = false, SpellEventScope callEvents = null)
        {
            ResourceCostSet sum = ResourceCostSet.Empty;
            inheritedModifiers = inheritedModifiers ?? SpellModifierValues.None;
            foreach (SpellAction action in actions)
            {
                ResourceCostSet ownCosts = action.GetExecutionCosts(inheritedModifiers, costMultiplier, isManaPrepaid, callEvents);
                if (includeEvents && allowBeamHold && action.Form == BeamBehaviorRules.BEAM_KIND)
                    ownCosts = UnboundedCost(ownCosts);
                if (!includeEvents && (action.Kind == "spawn" || action.Kind == "buff"))
                {
                    sum = sum.Add(ownCosts);
                    continue;
                }
                switch (action.Kind)
                {
                    case "spawn":
                        ResourceCostSet hitChainCost = Cost(action.OnHit, inheritedModifiers, costMultiplier,
                            isManaPrepaid: true, callEvents: callEvents);
                        // 대상마다 내는 OnHit은 개체당 상한이 없어 후속 비용이 있으면 최대 비용을 산정할 수 없다(W8).
                        ResourceCostSet hitCost = TryGetMaxHitEvents(action, out int maxHits)
                            ? hitChainCost.Multiply(maxHits) : UnboundedCost(hitChainCost);
                        ResourceCostSet expireCost = Cost(action.OnExpire, inheritedModifiers, costMultiplier, callEvents: callEvents);
                        ResourceCostSet eventCost = HasExclusiveHitAndExpire(action)
                            ? ResourceCostSet.Max(hitCost, expireCost) : hitCost.Add(expireCost);
                        sum = sum.Add(ownCosts).Add(eventCost.Add(Cost(action.OnFirstHitOrExpire,
                            inheritedModifiers, costMultiplier, callEvents: callEvents)).Multiply(SpawnInstanceCount(action)));
                        break;
                    case "buff": sum = sum.Add(ownCosts).Add(Cost(action.OnFirstHitOrExpire,
                        inheritedModifiers, costMultiplier, callEvents: callEvents).Multiply(action.Count)); break;
                    case "call":
                        long eventCount = (long)action.Count * action.CalledSpell.WorstCaseEntities;
                        SpellModifierValues callModifiers = inheritedModifiers.Combine(action.ModifierValues);
                        double callCostMultiplier = costMultiplier * action.EnergyMultiplier;
                        var events = new SpellEventScope(action.OnHit, action.OnExpire, action.OnFirstHitOrExpire,
                            callEvents, inheritedModifiers, costMultiplier);
                        ResourceCostSet calledCosts = Cost(action.CalledSpell.Root, callModifiers, callCostMultiplier, includeEvents,
                            isManaPrepaid: isManaPrepaid, callEvents: events)
                            .Multiply(action.Count);
                        if (!includeEvents)
                        {
                            sum = sum.Add(ownCosts).Add(calledCosts);
                            break;
                        }
                        ResourceCostSet eventCosts = UnboundedCost(Cost(action.OnHit, inheritedModifiers, costMultiplier,
                                isManaPrepaid: true, callEvents: callEvents))
                            .Add(Cost(action.OnExpire, inheritedModifiers, costMultiplier, callEvents: callEvents))
                            .Add(Cost(action.OnFirstHitOrExpire, inheritedModifiers, costMultiplier, callEvents: callEvents));
                        sum = sum.Add(ownCosts).Add(calledCosts).Add(eventCosts.Multiply(eventCount));
                        break;
                    case "delay": sum = sum.Add(Cost(action.Then, inheritedModifiers, costMultiplier, includeEvents,
                        isManaPrepaid: isManaPrepaid, callEvents: callEvents)); break;
                    case "repeat": sum = sum.Add(Cost(action.Body, inheritedModifiers, costMultiplier, includeEvents,
                            isManaPrepaid: isManaPrepaid, callEvents: callEvents).Multiply(action.Times))
                        .Add(Cost(action.OnComplete, inheritedModifiers, costMultiplier, includeEvents,
                            isManaPrepaid: isManaPrepaid, callEvents: callEvents)); break;
                    case "if": sum = sum.Add(ResourceCostSet.Max(Cost(action.Then, inheritedModifiers, costMultiplier, includeEvents,
                            isManaPrepaid: isManaPrepaid, callEvents: callEvents),
                        Cost(action.Else, inheritedModifiers, costMultiplier, includeEvents,
                            isManaPrepaid: isManaPrepaid, callEvents: callEvents))); break;
                    default: sum = sum.Add(ownCosts).Add(Cost(action.Next, inheritedModifiers, costMultiplier, includeEvents,
                        isManaPrepaid: isManaPrepaid, callEvents: callEvents)); break;
                }
            }
            return sum;
        }

        /// <summary>자원별 비용 묶음에 양수 비용이 하나라도 있는지 반환한다.</summary>
        private static bool HasCosts(ResourceCostSet costs)
        {
            foreach (ResourceAmount amount in costs.Amounts)
                if (amount.Amount > 0 || double.IsInfinity(amount.Amount)) return true;
            return false;
        }

        /// <summary>최악 분기의 생성 수를 이벤트 배수와 반복 횟수에 따라 합산한다. 상한 없는 OnHit 분기는 한 번으로 센다.</summary>
        private static int EntityCount(IReadOnlyList<SpellAction> actions)
        {
            long sum = 0L;
            const long limit = int.MaxValue;
            foreach (SpellAction action in actions)
            {
                long count;
                switch (action.Kind)
                {
                    case "spawn":
                        long hitWork = MultiplyBounded(HitEventMultiplier(action), EntityCount(action.OnHit), limit);
                        long expireWork = EntityCount(action.OnExpire);
                        long pathWork = HasExclusiveHitAndExpire(action) ? Math.Max(hitWork, expireWork) : AddBounded(hitWork, expireWork, limit);
                        long eventWork = AddBounded(pathWork, EntityCount(action.OnFirstHitOrExpire), limit);
                        long perEntityWork = AddBounded(1L, eventWork, limit);
                        count = MultiplyBounded(SpawnInstanceCount(action), perEntityWork, limit);
                        break;
                    case "buff": count = MultiplyBounded(action.Count, EntityCount(action.OnFirstHitOrExpire), limit); break;
                    case "call":
                        long callEntities = MultiplyBounded(action.Count, action.CalledSpell.WorstCaseEntities, limit);
                        long callEvents = AddBounded(EntityCount(action.OnHit),
                            AddBounded(EntityCount(action.OnExpire), EntityCount(action.OnFirstHitOrExpire), limit), limit);
                        count = AddBounded(callEntities, MultiplyBounded(callEntities, callEvents, limit), limit);
                        break;
                    case "delay": count = EntityCount(action.Then); break;
                    case "repeat":
                        count = AddBounded(MultiplyBounded(action.Times, EntityCount(action.Body), limit),
                            EntityCount(action.OnComplete), limit);
                        break;
                    case "if": count = Math.Max(EntityCount(action.Then), EntityCount(action.Else)); break;
                    default: count = EntityCount(action.Next); break;
                }
                sum = AddBounded(sum, count, limit);
                if (sum == limit) return int.MaxValue;
            }
            return (int)sum;
        }

        /// <summary>반복·최악 분기·형태 이벤트를 펼친 실행 명령 수를 계산하고 지정 상한에서 포화한다. 상한 없는 OnHit 분기는 한 번으로 센다.</summary>
        private static long ActionCount(IReadOnlyList<SpellAction> actions, long saturation)
        {
            long sum = 0L;
            foreach (SpellAction action in actions)
            {
                long count = 1L;
                switch (action.Kind)
                {
                    case "spawn":
                        long hitWork = MultiplyBounded(HitEventMultiplier(action), ActionCount(action.OnHit, saturation), saturation);
                        long expireWork = ActionCount(action.OnExpire, saturation);
                        long pathWork = HasExclusiveHitAndExpire(action) ? Math.Max(hitWork, expireWork) : AddBounded(hitWork, expireWork, saturation);
                        long eventWork = AddBounded(pathWork, ActionCount(action.OnFirstHitOrExpire, saturation), saturation);
                        count = AddBounded(count, MultiplyBounded(SpawnInstanceCount(action), eventWork, saturation), saturation);
                        break;
                    case "buff": count = AddBounded(count, MultiplyBounded(action.Count,
                        ActionCount(action.OnFirstHitOrExpire, saturation), saturation), saturation); break;
                    case "call":
                        long calleeActions = ActionCount(action.CalledSpell.Root, saturation);
                        long invocationWork = MultiplyBounded(action.Count, calleeActions, saturation);
                        long callHitWork = ActionCount(action.OnHit, saturation);
                        long callExpireWork = AddBounded(ActionCount(action.OnExpire, saturation),
                            ActionCount(action.OnFirstHitOrExpire, saturation), saturation);
                        long callEvents = MultiplyBounded((long)action.Count * action.CalledSpell.WorstCaseEntities,
                            AddBounded(callHitWork, callExpireWork, saturation), saturation);
                        count = AddBounded(count, AddBounded(invocationWork, callEvents, saturation), saturation);
                        break;
                    case "delay": count = AddBounded(count, ActionCount(action.Then, saturation), saturation); break;
                    case "repeat":
                        long repeatWork = AddBounded(MultiplyBounded(action.Times, ActionCount(action.Body, saturation), saturation),
                            ActionCount(action.OnComplete, saturation), saturation);
                        count = AddBounded(count, repeatWork, saturation);
                        break;
                    case "if": count = AddBounded(count, Math.Max(ActionCount(action.Then, saturation), ActionCount(action.Else, saturation)), saturation); break;
                    default: count = AddBounded(count, ActionCount(action.Next, saturation), saturation); break;
                }
                sum = AddBounded(sum, count, saturation);
                if (sum == saturation) return sum;
            }
            return sum;
        }

        /// <summary>음수가 아닌 두 실행 수를 더하며 덧셈 전 지정 상한 초과를 차단한다.</summary>
        private static long AddBounded(long left, long right, long saturation)
            => left >= saturation - right ? saturation : left + right;

        /// <summary>음수가 아닌 실행 수와 반복 수를 곱하며 곱셈 전 지정 상한 초과를 차단한다.</summary>
        private static long MultiplyBounded(long left, long right, long saturation)
        {
            if (left == 0L || right == 0L) return 0L;
            return left > saturation / right ? saturation : Math.Min(saturation, left * right);
        }

        /// <summary>실행되는 모든 형태와 속성의 적응 태그를 수집한다.</summary>
        private static void GatherTags(IReadOnlyList<SpellAction> actions, SortedSet<string> tags)
        {
            foreach (SpellAction action in actions)
            {
                if (action.Kind == "spawn")
                {
                    tags.Add(action.Form);
                    tags.Add(action.Element);
                }
                if (action.CalledSpell != null)
                    foreach (string tag in action.CalledSpell.Tags) tags.Add(tag);
                GatherTags(action.OnHit, tags);
                GatherTags(action.OnExpire, tags);
                GatherTags(action.OnFirstHitOrExpire, tags);
                GatherTags(action.Then, tags);
                GatherTags(action.Else, tags);
                GatherTags(action.Body, tags);
                GatherTags(action.OnComplete, tags);
                GatherTags(action.Next, tags);
            }
        }

        /// <summary>표시 이름·좌표·배열 순서를 제외한 그래프 구성의 안정 해시를 반환한다.</summary>
        public static string Signature(SpellGraph graph)
        {
            StringBuilder canonical = new StringBuilder();
            foreach (GraphNode node in graph.Nodes.OrderBy(node => node.Id, StringComparer.Ordinal))
            {
                canonical.Append(node.Id).Append(':').Append(node.RuneId).Append(';');
                foreach (NodeParameter param in node.Params.OrderBy(param => param.Key, StringComparer.Ordinal))
                    canonical.Append(param.Key).Append('=').Append(param.Number.ToString("R", CultureInfo.InvariantCulture))
                        .Append('/').Append(param.Text).Append(';');
            }
            foreach (GraphEdge edge in graph.Edges.OrderBy(edge => edge.FromNode + ":" + edge.FromPort + ":" + edge.ToNode + ":" + edge.ToPort, StringComparer.Ordinal))
            {
                canonical.Append(edge.FromNode).Append('.').Append(edge.FromPort).Append('>').Append(edge.ToNode).Append('.').Append(edge.ToPort);
                if (graph.Version >= 2) canonical.Append('#').Append(edge.Order);
                canonical.Append(';');
            }
            return HashSignature(canonical);
        }

        /// <summary>소스 그래프와 참조된 컴파일 Spell의 의미를 결합한 안정 시그니처를 반환한다.</summary>
        private static string Signature(SpellGraph graph, IReadOnlyList<SpellAction> root)
        {
            StringBuilder canonical = new StringBuilder(Signature(graph));
            AppendCallSignatures(root, canonical);
            return HashSignature(canonical);
        }

        /// <summary>실행 순서의 호출 참조 ID와 대상 컴파일 시그니처를 누적한다.</summary>
        private static void AppendCallSignatures(IReadOnlyList<SpellAction> actions, StringBuilder canonical)
        {
            foreach (SpellAction action in actions)
            {
                if (action.Kind == "call")
                    canonical.Append("call:").Append(action.CalledSpellId).Append('=').Append(action.CalledSpell.Signature).Append(';');
                AppendCallSignatures(action.OnHit, canonical);
                AppendCallSignatures(action.OnExpire, canonical);
                AppendCallSignatures(action.OnFirstHitOrExpire, canonical);
                AppendCallSignatures(action.Then, canonical);
                AppendCallSignatures(action.Else, canonical);
                AppendCallSignatures(action.Body, canonical);
                AppendCallSignatures(action.OnComplete, canonical);
                AppendCallSignatures(action.Next, canonical);
            }
        }

        /// <summary>정규 그래프 표현을 FNV-1a 시그니처로 변환한다.</summary>
        private static string HashSignature(StringBuilder canonical)
        {
            ulong hash = 14695981039346656037UL;
            foreach (byte value in Encoding.UTF8.GetBytes(canonical.ToString()))
            {
                hash ^= value;
                hash = unchecked(hash * 1099511628211UL);
            }
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        private sealed class CompileContext
        {
            private readonly HashSet<string> _unlockedIds;
            private readonly Dictionary<string, SpellGraph> _library;
            private readonly Dictionary<string, CompiledSpell> _compiled = new Dictionary<string, CompiledSpell>(StringComparer.Ordinal);
            private readonly HashSet<string> _activeSpellIds = new HashSet<string>(StringComparer.Ordinal);
            private readonly float _maxEnergy;
            private readonly IReadOnlyDictionary<string, float> _maxResourceCosts;
            private readonly bool _isSingleBehavior;
            private readonly float _elementCostReduction;
            private readonly int _elementSlotBonus;
            public HashSet<string> UnlockedIds => _unlockedIds;
            public Dictionary<string, SpellGraph> Library => _library;
            public Dictionary<string, CompiledSpell> Compiled => _compiled;
            public HashSet<string> ActiveSpellIds => _activeSpellIds;
            public float MaxEnergy => _maxEnergy;
            public bool IsSingleBehavior => _isSingleBehavior;
            public float ElementCostReduction => _elementCostReduction;
            public int ElementSlotBonus => _elementSlotBonus;

            /// <summary>라이브러리·해금·컴파일 캐시, 자원 한도와 행동 하나 고정·속성 비용 감소·속성 슬롯 확장 값을 컴파일 작업에 보관한다.</summary>
            public CompileContext(IEnumerable<string> unlocked, IEnumerable<SpellGraph> library, SpellGraph current,
                float maxEnergy, IReadOnlyDictionary<string, float> maxResourceCosts, bool isSingleBehavior,
                float elementCostReduction, int elementSlotBonus)
            {
                _unlockedIds = new HashSet<string>(unlocked ?? Array.Empty<string>(), StringComparer.Ordinal);
                _unlockedIds.Add(SpellGrammar.INLINE_RUNE);
                _library = new Dictionary<string, SpellGraph>(StringComparer.Ordinal);
                if (library != null)
                    foreach (SpellGraph spell in library)
                        if (spell != null && !string.IsNullOrEmpty(spell.Id)) _library[spell.Id] = spell;
                if (current != null && !string.IsNullOrEmpty(current.Id)) _library[current.Id] = current;
                _maxEnergy = maxEnergy;
                _maxResourceCosts = maxResourceCosts;
                _isSingleBehavior = isSingleBehavior;
                _elementCostReduction = elementCostReduction;
                _elementSlotBonus = elementSlotBonus;
            }

            /// <summary>자원별 최대 비용 한도를 반환하고 등록되지 않은 자원에는 false를 반환한다.</summary>
            public bool TryGetMaxResource(string resource, GrammarLimits limits, out float maximum)
            {
                if (_maxResourceCosts != null && _maxResourceCosts.TryGetValue(resource, out maximum)) return true;
                if (resource == "mana")
                {
                    maximum = _maxEnergy < 0f ? limits.MaxEnergy : _maxEnergy;
                    return true;
                }
                maximum = 0f;
                return false;
            }
        }
    }
}
