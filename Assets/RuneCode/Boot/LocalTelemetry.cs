using System;
using System.Collections.Generic;
using System.Globalization;

using UnityEngine;

namespace RuneCode
{
    public static class LocalTelemetry
    {
        private const int MAX_ENTRIES = 500;
        private static readonly Queue<TelemetryEntry> _entries = new Queue<TelemetryEntry>();
        private static readonly string[] USABILITY_COMBAT_COLUMNS =
        {
            "combat_id", "stage", "spell_build_id", "result", "duration_ms", "damage_dealt", "damage_received", "shield_absorbed",
            "energy_spent", "cast_requests", "cast_successes", "dash_inputs", "dashes_started", "kill_count", "fragments", "seed", "reason", "skill_tree", "inventory", "module_rewards", "action_stats", "active_seconds", "enemy_stats"
        };
        private static UsabilityLogWriter _usabilityLogWriter;
        private static readonly Dictionary<string, int> _usabilityEventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly HashSet<string> _completedUsabilitySignatures = new HashSet<string>(StringComparer.Ordinal);
        private static readonly System.Diagnostics.Stopwatch _laboratoryClock = new System.Diagnostics.Stopwatch();
        private static readonly Dictionary<string, double> _laboratoryTabSeconds = new Dictionary<string, double>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> _laboratoryActions = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly HashSet<string> _laboratoryReturnOperations = new HashSet<string>(StringComparer.Ordinal);
        private static string _laboratoryTab;
        private static double _laboratoryTabStartedAt;
        private static string _editingEpisodeId;
        private static Func<Dictionary<string, object>> _editingSnapshot;
        private static string _graphRevisionId;
        private static string _spellBuildId;
        private static string _combatId;
        private static Func<IReadOnlyList<Dictionary<string, object>>> _enemySnapshot;
        public static int Count => _entries.Count;
        public static bool IsUsabilityTestActive => _usabilityLogWriter != null;
        public static string UsabilityTestSessionId => _usabilityLogWriter?.SessionId;

        /// <summary>Starts one durable usability-test log session under the persistent UsabilityTests folder.</summary>
        public static void BeginUsabilityTest(IDictionary<string, object> metadata, IEnumerable<string> combatColumns)
        {
            if (_usabilityLogWriter != null) throw new InvalidOperationException("A usability-test log session is already active.");
            _usabilityLogWriter = UsabilityLogWriter.StartSession(metadata, combatColumns ?? USABILITY_COMBAT_COLUMNS);
            _usabilityEventCounts.Clear();
            _completedUsabilitySignatures.Clear();
            _editingEpisodeId = null;
            _editingSnapshot = null;
            _graphRevisionId = Guid.NewGuid().ToString("N");
            _spellBuildId = null;
            _combatId = null;
            _enemySnapshot = null;
        }

        /// <summary>Writes a structured test event when a usability session is active.</summary>
        public static void RecordUsabilityEvent(string eventName, string context, IDictionary<string, object> data)
        {
            if (_usabilityLogWriter == null) return;
            var eventData = data == null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object>(data);
            if (!eventData.ContainsKey("editing_episode_id")) eventData["editing_episode_id"] = _editingEpisodeId;
            if (!eventData.ContainsKey("graph_revision_id")) eventData["graph_revision_id"] = _graphRevisionId;
            if (!eventData.ContainsKey("spell_build_id")) eventData["spell_build_id"] = _spellBuildId;
            if (!eventData.ContainsKey("combat_id")) eventData["combat_id"] = _combatId;
            if (_editingEpisodeId != null)
            {
                if (eventName == "workshop_tab_switch" && eventData.TryGetValue("to_tab", out object tab) && tab is string nextTab)
                {
                    AccumulateLaboratoryTab();
                    _laboratoryTab = nextTab;
                }
                if (eventName == "upgrade_purchase" || eventName == "upgrade_refund" || eventName == "node_return" || eventName == "skill_tree_element_selected")
                {
                    string action = eventName;
                    if (eventData.TryGetValue("result", out object result) && !string.Equals(result as string, "success", StringComparison.Ordinal)) action += "_rejected";
                    _laboratoryActions.TryGetValue(action, out int count);
                    _laboratoryActions[action] = count + 1;
                    if (eventName == "node_return" && eventData.TryGetValue("operation_id", out object operation)
                        && operation is string operationId && _laboratoryReturnOperations.Add(operationId))
                    {
                        _laboratoryActions.TryGetValue("node_return_operations", out int operations);
                        _laboratoryActions["node_return_operations"] = operations + 1;
                    }
                }
            }
            _usabilityLogWriter.AppendEvent(eventName, context, eventData);
            IncrementUsabilityEventCount(eventName, eventData);
            if (eventName == "test_start" || eventName == "test_end" || eventName == "skill_tree_definition" || eventName == "stage_enter"
                || eventName == "combat_start" || eventName == "combat_end" || eventName == "spell_complete"
                || eventName == "clear_reward_selected" || eventName == "round_break_end"
                || eventName == "editor_enter" || eventName == "editor_exit"
                || eventName == "upgrade_purchase" || eventName == "upgrade_refund" || eventName == "node_return"
                || eventName == "skill_tree_element_selected" || eventName == "rune_synthesis"
                || eventName == "pause_start" || eventName == "pause_end")
                _usabilityLogWriter.Flush();
        }

        /// <summary>Starts an editor interval and assigns its ID to following events.</summary>
        public static void BeginUsabilityEditingEpisode(Func<Dictionary<string, object>> snapshot = null, string initialTab = "editor")
        {
            if (_usabilityLogWriter == null) return;
            if (_editingEpisodeId != null) EndUsabilityEditingEpisode("reentered");
            _editingEpisodeId = Guid.NewGuid().ToString("N");
            _editingSnapshot = snapshot;
            _laboratoryClock.Restart();
            _laboratoryTabSeconds.Clear();
            _laboratoryActions.Clear();
            _laboratoryReturnOperations.Clear();
            _laboratoryTab = initialTab;
            _laboratoryTabStartedAt = 0;
            Dictionary<string, object> data = snapshot?.Invoke() ?? new Dictionary<string, object>();
            data["initial_tab"] = initialTab;
            RecordUsabilityEvent("editor_enter", "editor", data);
        }

        /// <summary>Closes the active editor interval and clears its event linkage.</summary>
        public static void EndUsabilityEditingEpisode(string reason)
        {
            if (_usabilityLogWriter == null || _editingEpisodeId == null) return;
            Dictionary<string, object> data = _editingSnapshot?.Invoke() ?? new Dictionary<string, object>();
            _laboratoryClock.Stop();
            AccumulateLaboratoryTab();
            data["reason"] = reason;
            data["laboratory_seconds"] = _laboratoryClock.Elapsed.TotalSeconds;
            data["tab_seconds"] = new Dictionary<string, double>(_laboratoryTabSeconds);
            data["action_counts"] = new Dictionary<string, int>(_laboratoryActions);
            data["final_tab"] = _laboratoryTab;
            RecordUsabilityEvent("editor_exit", "editor", data);
            _editingEpisodeId = null;
            _editingSnapshot = null;
        }

        /// <summary>현재 실험실 탭의 마지막 관측 이후 체류 초를 누적하고 시간 기준을 갱신한다.</summary>
        private static void AccumulateLaboratoryTab()
        {
            double now = _laboratoryClock.Elapsed.TotalSeconds;
            if (!string.IsNullOrEmpty(_laboratoryTab))
            {
                _laboratoryTabSeconds.TryGetValue(_laboratoryTab, out double seconds);
                _laboratoryTabSeconds[_laboratoryTab] = seconds + Math.Max(0, now - _laboratoryTabStartedAt);
            }
            _laboratoryTabStartedAt = now;
        }

        /// <summary>Creates a fresh graph revision ID after a successful editing operation.</summary>
        public static void AdvanceUsabilityGraphRevision()
        {
            if (_usabilityLogWriter != null) _graphRevisionId = Guid.NewGuid().ToString("N");
        }

        /// <summary>Writes a graph snapshot for a build and returns its ID, or null outside a test session.</summary>
        public static string SaveUsabilityBuild(string spellBuildId, SpellGraph graph, IDictionary<string, object> metadata)
        {
            if (_usabilityLogWriter == null || graph == null) return null;
            string buildId = string.IsNullOrWhiteSpace(spellBuildId) ? Guid.NewGuid().ToString("N") : spellBuildId;
            var snapshotMetadata = metadata == null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object>(metadata);
            snapshotMetadata["editing_episode_id"] = _editingEpisodeId;
            snapshotMetadata["graph_revision_id"] = _graphRevisionId;
            string snapshotPath = _usabilityLogWriter.WriteGraphSnapshot(buildId, graph, snapshotMetadata);
            _spellBuildId = buildId;
            RecordUsabilityEvent("build_snapshot", "editor", new Dictionary<string, object>
            {
                ["reason"] = snapshotMetadata.TryGetValue("reason", out object reason) ? reason : null,
                ["snapshot_path"] = snapshotPath,
            });
            return buildId;
        }

        /// <summary>전투 구간을 시작하고 빌드 ID와 이벤트를 연결한다. 종료 시 사용할 적 누계 공급자를 보관하고 전투 ID를 반환한다.</summary>
        public static string BeginUsabilityCombat(int stage, string spellBuildId, int seed, Func<IReadOnlyList<Dictionary<string, object>>> enemySnapshot = null)
        {
            if (_usabilityLogWriter == null) return null;
            _enemySnapshot = enemySnapshot;
            _combatId = Guid.NewGuid().ToString("N");
            _spellBuildId = spellBuildId;
            RecordUsabilityEvent("combat_start", "combat", new Dictionary<string, object>
            {
                ["stage"] = stage,
                ["seed"] = seed,
            });
            return _combatId;
        }

        /// <summary>Ends the current combat interval, records its CSV summary, and clears combat linkage.</summary>
        public static void EndUsabilityCombat(IDictionary<string, object> data)
        {
            if (_usabilityLogWriter == null) return;
            var combatData = data == null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object>(data);
            combatData["combat_id"] = _combatId;
            combatData["spell_build_id"] = _spellBuildId;
            if (_enemySnapshot != null) combatData["enemy_stats"] = _enemySnapshot();
            RecordUsabilityEvent("combat_end", "combat", combatData);
            _usabilityLogWriter.AppendCombatSummary(combatData);
            _combatId = null;
            _enemySnapshot = null;
        }

        /// <summary>Appends one structured row to the active test session's combat CSV.</summary>
        public static void RecordUsabilityCombat(IDictionary<string, object> row)
        {
            _usabilityLogWriter?.AppendCombatSummary(row);
        }

        /// <summary>Flushes buffered test records periodically while preserving the debug telemetry buffer.</summary>
        public static void FlushUsabilityTest()
        {
            _usabilityLogWriter?.Flush(false);
        }

        /// <summary>Snapshots the final graph, records the test end and summary counts, then closes the active log session.</summary>
        public static void EndUsabilityTest(string reason, SpellGraph finalGraph = null)
        {
            if (_usabilityLogWriter == null) return;
            UsabilityLogWriter writer = _usabilityLogWriter;
            EndUsabilityEditingEpisode(reason);
            if (_combatId != null)
                EndUsabilityCombat(new Dictionary<string, object> { ["result"] = "incomplete", ["reason"] = reason });
            if (finalGraph != null)
                SaveUsabilityBuild(null, finalGraph, new Dictionary<string, object>
                {
                    ["reason"] = "test_end",
                    ["signature"] = GraphCompiler.Signature(finalGraph),
                    ["comparison_key"] = GetUsabilityGraphSignature(finalGraph),
                    ["node_count"] = finalGraph.Nodes.Count,
                    ["edge_count"] = finalGraph.Edges.Count,
                });
            RecordUsabilityEvent("test_end", "session", new Dictionary<string, object> { ["reason"] = reason });
            _usabilityLogWriter = null;
            var summary = new Dictionary<string, object>
            {
                ["event_count"] = writer.EventCount,
                ["duration_ms"] = writer.ElapsedMilliseconds,
            };
            foreach (KeyValuePair<string, int> pair in _usabilityEventCounts)
                summary[pair.Key + "_count"] = pair.Value;
            summary["unique_completed_designs"] = _completedUsabilitySignatures.Count;
            var endState = new Dictionary<string, object>
            {
                ["reason"] = reason,
                ["spell_build_id"] = _spellBuildId,
            };
            writer.Close(summary, endState);
            _usabilityEventCounts.Clear();
            _completedUsabilitySignatures.Clear();
            _editingEpisodeId = null;
            _graphRevisionId = null;
            _spellBuildId = null;
            _combatId = null;
            _enemySnapshot = null;
        }

        /// <summary>Returns a source-graph signature that ignores names, coordinates, array order and arbitrary node IDs.</summary>
        public static string GetUsabilityGraphSignature(SpellGraph graph)
        {
            if (graph == null) return null;
            GraphNode core = null;
            foreach (GraphNode node in graph.Nodes)
                if (node.RuneId == SpellGrammar.CORE_RUNE) { core = node; break; }
            if (core == null) return null;

            var orderedNodes = new List<GraphNode>();
            VisitUsabilityGraph(graph, core, new HashSet<string>(StringComparer.Ordinal), orderedNodes);
            var nodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var canonicalNodes = new List<GraphNode>(orderedNodes.Count);
            for (int index = 0; index < orderedNodes.Count; index++)
            {
                GraphNode node = orderedNodes[index];
                string canonicalId = "node-" + index.ToString("D8", CultureInfo.InvariantCulture);
                nodeIds.Add(node.Id, canonicalId);
                canonicalNodes.Add(new GraphNode(canonicalId, node.RuneId, 0, 0, node.Params));
            }
            var canonicalEdges = new List<GraphEdge>();
            int edgeIndex = 0;
            foreach (GraphEdge edge in graph.Edges)
            {
                if (!nodeIds.TryGetValue(edge.FromNode, out string fromNode)
                    || !nodeIds.TryGetValue(edge.ToNode, out string toNode)) continue;
                canonicalEdges.Add(new GraphEdge("edge-" + edgeIndex++, fromNode, edge.FromPort, toNode, edge.ToPort,
                    edge.Order, edge.IsLegacyEvent));
            }
            canonicalEdges.Sort((left, right) =>
            {
                int comparison = StringComparer.Ordinal.Compare(left.FromNode, right.FromNode);
                if (comparison != 0) return comparison;
                comparison = StringComparer.Ordinal.Compare(left.FromPort, right.FromPort);
                if (comparison != 0) return comparison;
                comparison = left.Order.CompareTo(right.Order);
                if (comparison != 0) return comparison;
                comparison = StringComparer.Ordinal.Compare(left.ToPort, right.ToPort);
                if (comparison != 0) return comparison;
                return StringComparer.Ordinal.Compare(left.ToNode, right.ToNode);
            });
            var canonicalGraph = new SpellGraph("usability", string.Empty, graph.Version, canonicalNodes, canonicalEdges);
            var legacyEdges = new List<string>(canonicalEdges.Count);
            foreach (GraphEdge edge in canonicalEdges) legacyEdges.Add(edge.IsLegacyEvent ? "1" : "0");
            return graph.Version.ToString(CultureInfo.InvariantCulture) + ":" + GraphCompiler.Signature(canonicalGraph)
                + ":" + string.Join(string.Empty, legacyEdges);
        }

        /// <summary>Returns whether the normalized graph is the first completed design in this test session.</summary>
        public static bool MarkFirstUsabilityCompletion(SpellGraph graph, out string graphSignature)
        {
            graphSignature = GetUsabilityGraphSignature(graph);
            return _usabilityLogWriter != null && !string.IsNullOrEmpty(graphSignature)
                && _completedUsabilitySignatures.Add(graphSignature);
        }

        /// <summary>Adds the core-reachable graph nodes in connection order while excluding cycles and disconnected nodes.</summary>
        private static void VisitUsabilityGraph(SpellGraph graph, GraphNode node, HashSet<string> visited, List<GraphNode> orderedNodes)
        {
            if (!visited.Add(node.Id)) return;
            orderedNodes.Add(node);
            var outgoing = new List<GraphEdge>();
            foreach (GraphEdge edge in graph.Edges)
                if (edge.FromNode == node.Id) outgoing.Add(edge);
            outgoing.Sort((left, right) =>
            {
                int comparison = StringComparer.Ordinal.Compare(left.FromPort, right.FromPort);
                if (comparison != 0) return comparison;
                comparison = left.Order.CompareTo(right.Order);
                if (comparison != 0) return comparison;
                comparison = StringComparer.Ordinal.Compare(left.ToPort, right.ToPort);
                if (comparison != 0) return comparison;
                GraphNode leftTarget = graph.FindNode(left.ToNode);
                GraphNode rightTarget = graph.FindNode(right.ToNode);
                return StringComparer.Ordinal.Compare(leftTarget?.RuneId, rightTarget?.RuneId);
            });
            foreach (GraphEdge edge in outgoing)
            {
                GraphNode target = graph.FindNode(edge.ToNode);
                if (target != null) VisitUsabilityGraph(graph, target, visited, orderedNodes);
            }
        }

        /// <summary>Closes a partially started log without marking it as a completed test session.</summary>
        public static void AbortUsabilityTest()
        {
            UsabilityLogWriter writer = _usabilityLogWriter;
            _usabilityLogWriter = null;
            _usabilityEventCounts.Clear();
            _editingSnapshot = null;
            _editingEpisodeId = null;
            _graphRevisionId = null;
            _spellBuildId = null;
            _combatId = null;
            _enemySnapshot = null;
            writer?.Dispose();
        }

        /// <summary>Increments local event counters used in the final session summary.</summary>
        private static void IncrementUsabilityEventCount(string eventName, Dictionary<string, object> data)
        {
            _usabilityEventCounts.TryGetValue(eventName, out int count);
            _usabilityEventCounts[eventName] = count + 1;
            if (eventName == "editor_action_result" && data.TryGetValue("result", out object result))
            {
                string suffix = string.Equals(result as string, "success", StringComparison.Ordinal) ? "successful_actions"
                    : string.Equals(result as string, "rejected", StringComparison.Ordinal) ? "rejected_actions" : null;
                if (suffix != null)
                {
                    _usabilityEventCounts.TryGetValue(suffix, out int actionCount);
                    _usabilityEventCounts[suffix] = actionCount + 1;
                }
            }
        }

        /// <summary>tick·종류·상세를 최대 500건의 메모리 링 버퍼에 기록한다. 중복 상세 종류를 제외한 기록만 활성 세션 파일에도 남긴다.</summary>
        public static void Record(int tick, string kind, string detail)
        {
            if (_entries.Count == MAX_ENTRIES) _entries.Dequeue();
            _entries.Enqueue(new TelemetryEntry(tick, kind, detail));
            if (kind == "mission.node" || kind == "dock.node" || kind == "bench.cast"
                || kind == "mission.start" || kind == "mission.result" || kind == "upgradeTree.purchase") return;
            RecordUsabilityEvent("local_telemetry", "runtime", new Dictionary<string, object>
            {
                ["tick"] = tick,
                ["kind"] = kind,
                ["detail"] = detail,
            });
        }

        /// <summary>콘솔이나 파일로 복사할 수 있는 전체 로컬 기록 JSON을 반환한다.</summary>
        public static string DumpJson()
        {
            return JsonUtility.ToJson(new TelemetryDump(_entries.ToArray()), true);
        }

        /// <summary>로컬 기록을 Unity 콘솔에 JSON 형식으로 출력한다.</summary>
        public static void DumpToConsole()
        {
            Debug.Log(DumpJson());
        }
    }

    [Serializable]
    public sealed class TelemetryEntry
    {
        [Header("로컬 기록")]
        [SerializeField] private int _tick;
        [SerializeField] private string _kind;
        [SerializeField] private string _detail;

        /// <summary>기록 시점·종류·상세를 직렬화 가능한 데이터로 보관한다.</summary>
        public TelemetryEntry(int tick, string kind, string detail)
        {
            _tick = tick;
            _kind = kind;
            _detail = detail;
        }
    }

    [Serializable]
    public sealed class TelemetryDump
    {
        [Header("로컬 기록 목록")]
        [SerializeField] private TelemetryEntry[] _entries;

        /// <summary>전달된 기록 배열을 JSON 내보내기용으로 보관한다.</summary>
        public TelemetryDump(TelemetryEntry[] entries) { _entries = entries; }
    }
}
