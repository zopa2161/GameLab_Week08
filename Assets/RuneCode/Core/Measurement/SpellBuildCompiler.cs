using System;

namespace RuneCode
{
    /// <summary>
    /// 측정 조합(SpellBuild)을 마법 그래프로 변환하고 컴파일하는 컴파일러다. 그래프는 core의 onAttack 트리거에서
    /// 시작해 프리셋 순서대로 행동 노드를 exec·onHit 엣지로 연결한다.
    /// </summary>
    public static class SpellBuildCompiler
    {
        private const string GRAPH_ID_PREFIX = "dps.";
        private const float NODE_X_START = 240f;
        private const float NODE_X_STEP = 160f;
        private const float NODE_Y = 140f;

        private static RuneCatalog _cachedRuneCatalog;
        private static string[] _cachedRuneIds;

        /// <summary>
        /// 조합과 측정 입력으로 마법 그래프를 만든다. core 노드의 트리거를 onAttack으로 설정하고, build.PresetIds 순서대로
        /// 행동 노드를 만들어 core.exec → 첫 행동, 앞 행동.onHit → 다음 행동.exec 엣지로 연결해 반환한다.
        /// 프리셋이 비어 있거나 suite에 없는 프리셋 ID가 있으면 ArgumentException을 던진다.
        /// </summary>
        public static SpellGraph CreateGraph(SpellBuild build, MeasurementSuite suite)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (suite == null) throw new ArgumentNullException(nameof(suite));
            if (build.PresetIds.Count == 0)
                throw new ArgumentException("조합에 프리셋이 없습니다.", nameof(build));

            SpellGraph graph = SpellGraph.Create(GRAPH_ID_PREFIX + build.BuildId, build.BuildId);
            graph.FindNode("core").SetText(SpellGrammar.TRIGGER_PARAM, SpellGrammar.TRIGGER_ON_ATTACK);

            string previousNodeId = "core";
            for (int index = 0; index < build.PresetIds.Count; index++)
            {
                string presetId = build.PresetIds[index];
                if (!suite.TryGetPreset(presetId, out BehaviorPreset preset))
                    throw new ArgumentException("측정 입력에 없는 프리셋입니다: " + presetId, nameof(suite));

                GraphNode node = new GraphNode(GetBehaviorNodeId(index), preset.BehaviorId, NODE_X_START + index * NODE_X_STEP, NODE_Y);
                node.SetText(SpellGrammar.MODIFIER_GRADE_PARAM, build.Grade);
                for (int modifierIndex = 0; modifierIndex < preset.ModifierIds.Count; modifierIndex++)
                {
                    node.SetText(SpellGrammar.ModifierSlotKey(modifierIndex),
                        SpellGrammar.ModifierStockKey(preset.ModifierIds[modifierIndex], build.Grade));
                }
                if (!string.IsNullOrEmpty(preset.ElementId))
                {
                    node.SetText(SpellGrammar.ElementSlotKey(0), preset.ElementId);
                }
                graph.AddNode(node);
                graph.AddEdge(new GraphEdge("e" + index, previousNodeId,
                    index == 0 ? SpellGrammar.EXEC_PORT : SpellGrammar.ON_HIT_PORT,
                    node.Id, SpellGrammar.EXEC_PORT));
                previousNodeId = node.Id;
            }
            return graph;
        }

        /// <summary>체인 인덱스로 CreateGraph가 만드는 행동 노드 ID("b" + 인덱스)를 반환한다. 측정 결과에서 노드별 피해를 조회할 때 같은 값을 쓴다.</summary>
        public static string GetBehaviorNodeId(int chainIndex)
        {
            return "b" + chainIndex;
        }

        /// <summary>
        /// 조합을 그래프로 변환해 룬 테이블과 문법 제한, 측정 프로필의 RAM·에너지로 컴파일한 결과를 반환한다.
        /// 해금은 전체 룬 ID로 제한하지 않는다. GameData.Load()를 호출자가 먼저 완료했다고 가정한다.
        /// </summary>
        public static CompileResult Compile(SpellBuild build, MeasurementSuite suite)
        {
            SpellGraph graph = CreateGraph(build, suite);
            return GraphCompiler.Compile(graph, GameData.Runes, GameData.Balance.Grammar,
                GetAllRuneIds(), suite.Profile.RamCapacity, suite.Profile.MaxEnergy);
        }

        /// <summary>
        /// 전체 룬 ID 목록을 반환한다. 목록은 캐시하되 GameData.Runes의 RuneCatalog 참조가 바뀌면 다시 만든다.
        /// </summary>
        private static string[] GetAllRuneIds()
        {
            RuneCatalog runes = GameData.Runes;
            if (_cachedRuneIds != null && _cachedRuneCatalog == runes) return _cachedRuneIds;
            var ids = new string[runes.All.Count];
            for (int index = 0; index < runes.All.Count; index++)
            {
                ids[index] = runes.All[index].Id;
            }
            _cachedRuneCatalog = runes;
            _cachedRuneIds = ids;
            return ids;
        }
    }
}
