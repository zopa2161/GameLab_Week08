using System;
using System.Collections.Generic;
using System.Linq;

namespace RuneCode
{
    /// <summary>
    /// 인스펙터 Presenter다. 호버 룬·노드의 설명 텍스트를 정해 인스펙터 View에 쌓는다.
    /// 쌓인 텍스트는 SpellEditorPresenter가 커서 옆 호버 설명으로 표시한다.
    /// </summary>
    public sealed class SpellInspectorPresenter
    {

        private readonly ISpellEditor _editor;
        private readonly SpellInspectorView _view;
        private GraphNode _hoveredNode;
        private RuneDefinition _hoveredRune;
        private string _hoveredGrade;
        private float? _hoveredDamage;
        private float? _hoveredLifetime;
        private float? _hoveredTickInterval;

        /// <summary>편집 세션과 인스펙터 View로 Presenter를 만든다.</summary>
        public SpellInspectorPresenter(ISpellEditor editor, SpellInspectorView view)
        {
            _editor = editor;
            _view = view;
        }

        /// <summary>룬·등급과 선택적 피해량·지속시간·타격 간격으로 호버 설명을 갱신하고 룬이 null이면 호버 정보를 비운다.</summary>
        public void PreviewRune(RuneDefinition rune, string grade, float? damage = null, float? lifetime = null, float? tickInterval = null)
        {
            if (_hoveredNode == null && _hoveredRune == rune && _hoveredGrade == grade && _hoveredDamage == damage
                && _hoveredLifetime == lifetime && _hoveredTickInterval == tickInterval) return;
            _hoveredNode = null;
            _hoveredRune = rune;
            _hoveredGrade = grade;
            _hoveredDamage = damage;
            _hoveredLifetime = lifetime;
            _hoveredTickInterval = tickInterval;
            Refresh();
        }

        /// <summary>선택을 바꾸지 않고 호버 노드의 기존 인스펙터 정보를 표시하며 null이면 선택 정보로 복구한다.\n</summary>
        public void PreviewNode(GraphNode node)
        {
            if (_hoveredNode == node && _hoveredRune == null) return;
            _hoveredNode = node;
            _hoveredRune = null;
            _hoveredGrade = null;
            _hoveredDamage = null;
            _hoveredLifetime = null;
            _hoveredTickInterval = null;
            Refresh();
        }

        /// <summary>호버 룬의 이름·등급을 제목에 한 번 표시하고 효과 설명을 붙이며, 호버 노드는 그래프에서 다시 찾아 정보를 쌓는다.</summary>
        public void Refresh()
        {
            _view.Begin();
            if (_hoveredRune != null)
            {
                _view.AddTitle(ModifierGradeText.FormatName(_hoveredRune.Name, _hoveredGrade));
                _view.AddNote(RuneDescriptionBuilder.GetHoverText(_hoveredRune, _hoveredGrade), 60f, 10f);
                if (_hoveredRune.Category == SpellGrammar.CATEGORY_BEHAVIOR)
                {
                    float? damage = _hoveredDamage;
                    if (!damage.HasValue && SpellBehaviorRuleRegistry.TryGetByRuneId(_hoveredRune.Id, out SpellBehaviorRules rules)
                        && GameData.Runes.TryGet(rules.StatsRuneId, out RuneDefinition statsRune)) damage = statsRune.Stats.Damage;
                    _view.AddNote("\n" + GameData.L("ui.runeDamageAmount") + " " + (damage.HasValue ? damage.Value.ToString("0.#") : "—"), 24f, 4f);
                    if (_hoveredRune.Id == "behavior.persist")
                    {
                        float? lifetime = _hoveredLifetime;
                        if (!lifetime.HasValue && SpellBehaviorRuleRegistry.TryGetByRuneId(_hoveredRune.Id, out SpellBehaviorRules persistRules)
                            && GameData.Runes.TryGet(persistRules.StatsRuneId, out RuneDefinition persistStats)) lifetime = persistStats.Stats.Lifetime;
                        _view.AddNote(GameData.L("ui.runeLifetime") + " " + (lifetime.HasValue ? lifetime.Value.ToString("0.#") + "초" : "—"), 24f, 4f);
                    }
                    if (_hoveredRune.Id == "behavior.persist" || _hoveredRune.Id == "behavior.beam")
                    {
                        float? interval = _hoveredTickInterval;
                        if (!interval.HasValue && SpellBehaviorRuleRegistry.TryGetByRuneId(_hoveredRune.Id, out SpellBehaviorRules intervalRules)
                            && GameData.Runes.TryGet(intervalRules.StatsRuneId, out RuneDefinition intervalStats)) interval = intervalStats.Stats.TickInterval;
                        _view.AddNote(GameData.L("ui.runeInterval") + " " + (interval.HasValue ? interval.Value.ToString("0.##") + "초마다 1회" : "—"), 24f, 4f);
                    }
                }
                return;
            }
            if (_hoveredNode != null) _hoveredNode = _editor.Graph.FindNode(_hoveredNode.Id);
            if (_hoveredNode != null) ShowNode(_hoveredNode);
        }

        /// <summary>시작 노드는 공격 시 실행 안내만 표시하고, 다른 노드는 룬 이름·RAM과 슬롯·효과 설명을 쌓는다.</summary>
        private void ShowNode(GraphNode node)
        {
            RuneDefinition rune = GameData.Runes.Get(node.RuneId);
            if (rune.Id == SpellGrammar.CORE_RUNE)
            {
                _view.AddNote("공격 시 실행됩니다", 24f, 4f);
                return;
            }
            string title = rune.Name;
            if (rune.Category == SpellGrammar.CATEGORY_MODIFIER)
            {
                string grade = SpellGrammar.GetModifierGrade(rune, node, GameData.ModifierGrades);
                title = ModifierGradeText.FormatName(title, grade);
            }
            else if (GraphNodeLayout.HasSlots(rune))
            {
                title = ModifierGradeText.FormatName(title, node.GetText(SpellGrammar.MODIFIER_GRADE_PARAM, SpellGrammar.DEFAULT_BEHAVIOR_GRADE));
            }
            _view.AddTitle(title + " / " + GameData.Runes.NodeRam(node) + " RAM");
            if (GraphNodeLayout.HasSlots(rune)) ShowSlots(node);
            if (rune.Category == SpellGrammar.CATEGORY_MODIFIER)
            {
                ShowModifierGrade(node, rune);
                ShowModifierHint(rune, SpellGrammar.GetModifierGrade(rune, node, GameData.ModifierGrades));
            }
            else if (rune.Category == SpellGrammar.CATEGORY_SHAPE)
            {
                ShowShapeHint(rune);
            }
            else
            {
                ShowElementInfo(node);
                if (rune.Category == SpellGrammar.CATEGORY_BEHAVIOR) ShowBehaviorStats(node, rune);
                else ShowCompiledStats(node, rune);
                if (rune.Id == "behavior.persist") _view.AddNote(RuneDescriptionBuilder.GetHoverText(rune, null), 60f, 10f);
            }
        }

        /// <summary>행동 노드에 장착된 속성과 모디 정보만 표시하며 빈 슬롯과 잠긴 슬롯 안내는 생략한다.\n</summary>
        private void ShowSlots(GraphNode node)
        {
            var lines = new List<string>();
            for (int index = 0; index < SpellGrammar.MAX_ELEMENT_SLOTS; index++)
            {
                string slotKey = SpellGrammar.ElementSlotKey(index);
                if (!GraphNodeLayout.IsSlotLocked(node, slotKey) && !string.IsNullOrEmpty(node.GetText(slotKey, null)))
                    lines.Add(SlotLine(node, slotKey));
            }
            for (int index = 0; index < SpellGrammar.MAX_MODIFIER_SLOTS; index++)
            {
                string slotKey = SpellGrammar.ModifierSlotKey(index);
                if (!GraphNodeLayout.IsSlotLocked(node, slotKey) && !string.IsNullOrEmpty(node.GetText(slotKey, null)))
                    lines.Add(SlotLine(node, slotKey));
            }
            if (lines.Count > 0) _view.AddNote(string.Join("\n", lines), 34, 6);
        }

        /// <summary>슬롯 한 칸의 표시 문구를 반환한다.\n 잠긴 칸은 잠김, 빈 칸은 비어 있음, 장착 칸은 룬 이름(모디는 등급 포함)이다.\n</summary>
        private static string SlotLine(GraphNode node, string slotKey)
        {
            if (GraphNodeLayout.IsSlotLocked(node, slotKey)) return GameData.L("ui.slotLocked");
            string value = node.GetText(slotKey, null);
            if (string.IsNullOrEmpty(value)) return GameData.L("ui.empty");
            if (SpellGrammar.TryParseSlotKey(slotKey, out bool isElement, out _) && isElement)
            {
                return GameData.Runes.TryGetElement(value, out ElementDefinition element)
                    && GameData.Runes.TryGet(element.RuneId, out RuneDefinition elementRune) ? elementRune.Name : value;
            }
            return SpellGrammar.TryParseStockKey(value, out string modifierId, out string grade)
                && GameData.Runes.TryGet(modifierId, out RuneDefinition modifierRune)
                ? ModifierGradeText.FormatName(modifierRune.Name, grade) : value;
        }

        /// <summary>형태 노드별 역할을 한 줄 설명으로 표시한다.\n</summary>
        private void ShowShapeHint(RuneDefinition rune)
        {
            if (GameData.TryL("ui." + rune.Id + "Hint", out string hint)) _view.AddNote(hint, 24, 4);
        }

        /// <summary>선택 행동에 대응하는 내부 형태의 기본값과 Modifier 적용 후 컴파일값을 표시한다.\n</summary>
        private void ShowBehaviorStats(GraphNode node, RuneDefinition rune)
        {
            if (!SpellBehaviorRuleRegistry.TryGetByRuneId(rune.Id, out SpellBehaviorRules rules)
                || !GameData.Runes.TryGet(rules.StatsRuneId, out RuneDefinition form)) return;

            CompiledSpell spell = _editor.CompileResult.Spell;
            List<SpellAction> actions = spell == null ? new List<SpellAction>() : FindActionsForNode(spell.Root, node.Id);
            if (actions.Count == 0)
            {
                AddBehaviorStatNote(form, null, rune, node);
                return;
            }
            foreach (SpellAction action in actions)
            {
                AddBehaviorStatNote(form, action, rune, node);
                AddBehaviorGeometryNote(spell, action);
            }
        }

        /// <summary>내부 형태의 기본 전력·피해·발사 수·지속시간과 행동의 최종값을 한 줄씩 비교해 표시한다.\n</summary>
        private void AddBehaviorStatNote(RuneDefinition form, SpellAction action, RuneDefinition behavior, GraphNode node)
        {
            var lines = new List<string>
            {
                FormatTransition(GameData.L("ui.runeUsedEnergy"), form.Energy, action == null ? form.Energy : action.OwnEnergy)
            };
            if (behavior.Id == "behavior.beam")
            {
                SpellStats beam = action?.Stats;
                CompiledSpell compiled = _editor.CompileResult.Spell;
                bool isSlotCycle = action != null && compiled != null && compiled.IsSlotCycleBeam(action);
                double period = isSlotCycle
                    ? compiled.GetSlotCycleTicks(GameData.Balance.Sim.ExecutionTimeScale) / (double)CompiledSpell.SIMULATION_TICK_RATE
                    : form.Stats.MinimumLifetime;
                double initialCost = action == null
                    ? form.Energy : isSlotCycle
                        ? compiled.GetBeamCycleCosts(action, GameData.Balance.Sim.ExecutionTimeScale).GetAmount("mana")
                        : action.GetExecutionCosts(SpellModifierValues.None).GetAmount("mana");
                lines.Clear();
                lines.Add(GameData.L("ui.beamInitialCost") + " " + initialCost.ToString("0.##"));
                lines.Add(GameData.L("ui.beamDuration") + " " + period.ToString("0.##") + "s"
                    + (isSlotCycle ? " · " + GameData.L("ui.beamUnlimitedHold") : ""));
                if (isSlotCycle) lines.Add("연장 소모 마나 " + initialCost.ToString("0.##") + " / 구간");
                lines.Add(GameData.L("ui.runeDamageAmount") + " " + (beam?.Damage ?? form.Stats.Damage).ToString("0.##")
                    + " / " + form.Stats.TickInterval.ToString("0.##") + "s");
                lines.Add(GameData.L("ui.beamWidth") + " " + (beam?.BoxWidth ?? GameData.Runes.Get("form.bolt").Stats.Radius).ToString("0.##") + "px");
                lines.Add(GameData.L("ui.beamSustainCost") + " " + (initialCost / period).ToString("0.##") + "/s");
                lines.Add(GameData.L("ui.beamHint"));
                _view.AddNote(string.Join("\n", lines), 18 * lines.Count + 16, 6);
                return;
            }

            SpellStats stats = action?.Stats;
            lines.Add(FormatTransition(GameData.L("ui.runeDamageAmount"), form.Stats.Damage, stats == null ? form.Stats.Damage : stats.Damage));
            lines.Add(FormatTransition(GameData.L("ui.runeCount"), form.Stats.Count, action == null ? form.Stats.Count : action.Count));
            lines.Add(FormatTransition(GameData.L("ui.runeLifetime"), form.Stats.Lifetime, stats == null ? form.Stats.Lifetime : stats.Lifetime, "s"));
            _view.AddNote(string.Join("\n", lines), 34, 6);
        }

        /// <summary>숫자 두 개를 지정 라벨과 기본값→적용값 형태로 묶어 반환한다.\n</summary>
        private static string FormatTransition(string label, double baseValue, double effectiveValue, string suffix = "")
        {
            return label + " " + baseValue.ToString("0.##") + suffix + " → " + effectiveValue.ToString("0.##") + suffix;
        }

        /// <summary>현재 등급 색을 적용해 Modifier 효과의 짧은 설명과 장착 가능 행동을 표시한다.\n</summary>
        private void ShowModifierHint(RuneDefinition rune, string grade)
        {
            string modifierName = rune.Id.Substring("mod.".Length);
            if (GameData.TryL("ui.mod." + modifierName + "Hint", out string hint)) _view.AddNote(ModifierGradeText.Colorize(hint, grade), 52, 6);
            string targets = RuneDescriptionBuilder.GetModifierTargets(rune);
            if (targets != null) _view.AddNote("\n" + targets, 42, 4);
        }

        /// <summary>노드에 선택된 속성의 이름과 원소·기능 효과 구분을 표시한다.\n</summary>
        private void ShowElementInfo(GraphNode node)
        {
            if (!GameData.Runes.TryGetNodeElement(node, out ElementDefinition element)) return;
            string classification = element.IsFunctional ? GameData.L("ui.runeFunctionalElement") : GameData.L("ui.runeElementalElement");
            _view.AddNote(OptionLabel("element", element.Id) + " · " + classification, 24, 4);
        }

        /// <summary>선택 노드가 포함된 컴파일 행동의 자원 비용과 비시간 전투 수치를 표시한다.\n</summary>
        private void ShowCompiledStats(GraphNode node, RuneDefinition rune)
        {
            CompiledSpell spell = _editor.CompileResult.Spell;
            if (spell == null)
            {
                ShowUncompiledRuneStats(rune);
                return;
            }

            List<SpellAction> actions = FindCompiledActions(node, rune, spell);
            if (actions.Count == 0)
            {
                ShowUncompiledRuneStats(rune);
                return;
            }
            foreach (SpellAction action in actions)
            {
                var lines = new List<string>();
                if (action.Kind == "spawn" || action.Kind == "buff")
                    lines.Add(GameData.L("ui.runeEffectiveCost") + " " + FormatCosts(action.OwnCosts));
                if (action.EnergyMultiplier != 1f)
                    lines.Add(GameData.L("ui.runeAppliedMultiplier") + " ×" + action.EnergyMultiplier.ToString("0.##"));

                AddActionStats(lines, action, rune);
                if (lines.Count > 0) _view.AddNote(string.Join("\n", lines), 34, 6);
            }
        }

        /// <summary>선택 노드의 실행을 찾고, 컴파일 과정에서 행동 노드에 합쳐지는 Shape는 연결된 Behavior 실행으로 연결한다.\n</summary>
        private List<SpellAction> FindCompiledActions(GraphNode node, RuneDefinition rune, CompiledSpell spell)
        {
            List<SpellAction> actions = FindActionsForNode(spell.Root, node.Id);
            if (actions.Count > 0 || rune.Category != SpellGrammar.CATEGORY_SHAPE) return actions;

            foreach (GraphEdge edge in _editor.Graph.Edges)
            {
                if (edge.FromNode != node.Id) continue;
                GraphNode target = _editor.Graph.FindNode(edge.ToNode);
                if (target == null || GameData.Runes.Get(target.RuneId).Category != SpellGrammar.CATEGORY_BEHAVIOR) continue;
                return FindActionsForNode(spell.Root, target.Id);
            }
            return actions;
        }

        /// <summary>아직 실행에 연결되지 않은 룬의 테이블 기본 전투 수치와 형상 비용률을 표시한다.\n</summary>
        private void ShowUncompiledRuneStats(RuneDefinition rune)
        {
            RuneStats stats = rune.Stats;
            var lines = new List<string>();
            var impact = new List<string>();
            if (stats.Damage > 0f) impact.Add(GameData.L("ui.runeDamage") + " " + stats.Damage.ToString("0.##"));
            if (stats.Count > 0) impact.Add(GameData.L("ui.runeCount") + " " + stats.Count);
            if (impact.Count > 0) lines.Add(string.Join(" · ", impact));

            if (stats.BeamWidth > 0f || stats.BeamLength > 0f)
                lines.Add(GameData.L("ui.spellBeamSize") + " " + stats.BeamWidth.ToString("0.#") + " × " + stats.BeamLength.ToString("0.#") + "px");
            else if (stats.ConeAngle > 0f)
                lines.Add(GameData.L("ui.runeConeAngle") + " " + stats.ConeAngle.ToString("0.#") + "°");
            else if (stats.Radius > 0f)
                lines.Add(GameData.L("ui.spellRadius") + " " + stats.Radius.ToString("0.#") + "px");

            var movement = new List<string>();
            if (stats.Speed > 0f) movement.Add(GameData.L("ui.spellSpeed") + " " + stats.Speed.ToString("0.#") + "px/s");
            if (stats.Lifetime > 0f) movement.Add(GameData.L("ui.runeLifetime") + " " + stats.Lifetime.ToString("0.##") + "s");
            if (stats.Offset > 0f) movement.Add(GameData.L("ui.runeOffset") + " " + stats.Offset.ToString("0.#") + "px");
            if (stats.OrbitRadius > 0f) movement.Add(GameData.L("ui.runeOrbitRadius") + " " + stats.OrbitRadius.ToString("0.#") + "px");
            if (stats.AngularSpeed > 0f) movement.Add(GameData.L("ui.spellSpeed") + " " + stats.AngularSpeed.ToString("0.#") + "°/s");
            if (movement.Count > 0) lines.Add(string.Join(" · ", movement));

            var secondary = new List<string>();
            if (stats.Pierce > 0) secondary.Add(GameData.L("ui.runePierce") + " " + stats.Pierce);
            if (stats.HomingTurn > 0f || stats.HomingRange > 0f)
                secondary.Add(GameData.L("ui.runeHoming") + " " + stats.HomingTurn.ToString("0.#") + "° / " + stats.HomingRange.ToString("0.#") + "px");
            if (stats.HitInterval > 0f) secondary.Add(GameData.L("ui.runeInterval") + " " + stats.HitInterval.ToString("0.##") + "s");
            if (stats.TickInterval > 0f) secondary.Add(GameData.L("ui.runeInterval") + " " + stats.TickInterval.ToString("0.##") + "s");
            if (stats.ArcTargets > 0)
                secondary.Add(GameData.L("ui.runeArc") + " " + stats.ArcTargets + " · " + stats.ArcRange.ToString("0.#") + "px · ×" + stats.ArcMultiplier.ToString("0.##"));
            if (stats.SpreadAngle > 0f) secondary.Add(GameData.L("ui.gradeSpread") + " " + stats.SpreadAngle.ToString("0.#") + "°");
            if (stats.PierceLoss > 0f) secondary.Add(GameData.L("ui.gradePierceLoss") + " " + stats.PierceLoss.ToString("0.##"));
            if (stats.ExpandSeconds > 0f) secondary.Add(GameData.L("ui.runeExpand") + " " + stats.ExpandSeconds.ToString("0.##") + "s");
            if (stats.WarnSeconds > 0f) secondary.Add(GameData.L("ui.runeWarning") + " " + stats.WarnSeconds.ToString("0.##") + "s");
            if (stats.ShieldAmount > 0f)
                secondary.Add(GameData.L("ui.runeShield") + " " + stats.ShieldAmount.ToString("0.##") + " · " + stats.ShieldSeconds.ToString("0.##") + "s");
            if (secondary.Count > 0) lines.Add(string.Join(" · ", secondary));

            var rates = new List<string>();
            foreach (ShapeCostRateDefinition rate in rune.ShapeCostRates)
                rates.Add(ResourceName(rate.Resource) + " " + rate.Rate.ToString("0.######") + "/px²");
            if (rates.Count > 0) lines.Add(GameData.L("ui.runeAreaCost") + " " + string.Join(" · ", rates));
            if (lines.Count > 0) _view.AddNote(string.Join("\n", lines), 34, 6);
        }

        /// <summary>실행 행동 목록에서 노드 자체 또는 해당 노드를 부착 데이터로 사용하는 행동을 깊이 우선으로 찾는다.\n</summary>
        private static List<SpellAction> FindActionsForNode(IReadOnlyList<SpellAction> actions, string nodeId)
        {
            var matches = new List<SpellAction>();
            foreach (SpellAction action in actions)
            {
                bool isAttached = action.NodeId == nodeId || action.AttachedNodeIds.Contains(nodeId);
                if (isAttached) matches.Add(action);
                foreach (IReadOnlyList<SpellAction> branch in action.Branches)
                    matches.AddRange(FindActionsForNode(branch, nodeId));
            }
            return matches;
        }

        /// <summary>행동 종류에 맞는 비시간 전투 수치, 제어 흐름 조건 또는 호출 마법 요약을 목록에 추가한다.\n</summary>
        private void AddActionStats(List<string> lines, SpellAction action, RuneDefinition rune)
        {
            SpellStats stats = action.Stats;
            if (stats != null)
            {
                var impact = new List<string>();
                if (stats.Damage > 0f) impact.Add(GameData.L("ui.runeDamage") + " " + stats.Damage.ToString("0.##"));
                if (action.Kind == "spawn" && action.Count > 0)
                    impact.Add(GameData.L("ui.runeCount") + " " + action.Count
                        + (action.WaveCount > 1 ? " × " + action.WaveCount : string.Empty));
                if (impact.Count > 0) lines.Add(string.Join(" · ", impact));

                if (action.MagicType == SpellGrammar.MAGIC_TYPE_BOX)
                    lines.Add(GameData.L("ui.spellBeamSize") + " " + stats.BoxWidth.ToString("0.#") + " × " + stats.BoxLength.ToString("0.#") + "px");
                else if (action.MagicType == SpellGrammar.MAGIC_TYPE_CONE)
                    lines.Add(GameData.L("ui.spellRadius") + " " + stats.ShapeRadius.ToString("0.#") + "px"
                        + " · " + GameData.L("ui.runeConeAngle") + " " + action.ConeAngle.ToString("0.#") + "°");
                else if (stats.Radius > 0f)
                    lines.Add(GameData.L("ui.spellRadius") + " " + stats.Radius.ToString("0.#") + "px");

                var movement = new List<string>();
                bool isTravelling = action.Rules != null && action.Rules.MotionKind == SpellMotionKind.Moving;
                if (isTravelling)
                {
                    if (stats.Speed > 0f) movement.Add(GameData.L("ui.spellSpeed") + " " + stats.Speed.ToString("0.#") + "px/s");
                    if (stats.Lifetime > 0f)
                        movement.Add(GameData.L("ui.runeRange") + " " + action.GetReach(stats).ToString("0.#") + "px"
                            + " · " + GameData.L("ui.runeLifetime") + " " + stats.Lifetime.ToString("0.##") + "s");
                }

                if (stats.Offset > 0f) movement.Add(GameData.L("ui.runeOffset") + " " + stats.Offset.ToString("0.#") + "px");
                // 궤도 반경은 행동마다 있으므로 공전 모드가 장착된 경우에만 표시한다.\n
                bool isOrbiting = action.Mods.Contains(OrbitModifierRules.RUNE_ID);
                if (isOrbiting && stats.OrbitRadius > 0f) movement.Add(GameData.L("ui.runeOrbitRadius") + " " + stats.OrbitRadius.ToString("0.#") + "px");
                if (action.Form == BeamBehaviorRules.BEAM_KIND)
                {
                    CompiledSpell owner = _editor.CompileResult.Spell;
                    string duration = owner != null && owner.IsSlotCycleBeam(action)
                        ? GameData.L("ui.beamUnlimitedHold") : stats.MinimumLifetime.ToString("0.##") + "s";
                    movement.Add(GameData.L("ui.beamDuration") + " " + duration);
                }
                else if (!isTravelling && stats.Lifetime > 0f)
                    movement.Add(GameData.L("ui.runeLifetime") + " " + stats.Lifetime.ToString("0.##") + "s");
                if (movement.Count > 0) lines.Add(string.Join(" · ", movement));

                var secondary = new List<string>();
                if (stats.Pierce > 0) secondary.Add(GameData.L("ui.runePierce") + " " + stats.Pierce);
                if (stats.HomingTurn > 0f || stats.HomingRange > 0f)
                    secondary.Add(GameData.L("ui.runeHoming") + " " + stats.HomingTurn.ToString("0.#") + "° / " + stats.HomingRange.ToString("0.#") + "px");
                if (stats.OrbitTargetCount > 0)
                    secondary.Add(GameData.L("ui.gradeOrbitTargetCount") + " " + stats.OrbitTargetCount);
                if (stats.HitInterval > 0f) secondary.Add(GameData.L("ui.runeInterval") + " " + stats.HitInterval.ToString("0.##") + "s");
                if (stats.TickInterval > 0f) secondary.Add(GameData.L("ui.runeInterval") + " " + stats.TickInterval.ToString("0.##") + "s");
                if (stats.ArcTargets > 0)
                    secondary.Add(GameData.L("ui.runeArc") + " " + stats.ArcTargets + " · " + stats.ArcRange.ToString("0.#") + "px · ×" + stats.ArcMultiplier.ToString("0.##"));
                if (stats.SpreadAngle > 0f) secondary.Add(GameData.L("ui.gradeSpread") + " " + stats.SpreadAngle.ToString("0.#") + "°");
                if (secondary.Count > 0) lines.Add(string.Join(" · ", secondary));
            }

            var effect = new List<string>();
            if (rune.Stats.ExpandSeconds > 0f) effect.Add(GameData.L("ui.runeExpand") + " " + rune.Stats.ExpandSeconds.ToString("0.##") + "s");
            if (rune.Stats.WarnSeconds > 0f) effect.Add(GameData.L("ui.runeWarning") + " " + rune.Stats.WarnSeconds.ToString("0.##") + "s");
            if (action.Power > 0f) effect.Add(GameData.L("ui.runePower") + " " + action.Power.ToString("0.##"));
            if (action.BuffDuration > 0f) effect.Add(GameData.L("ui.runeBuffDuration") + " " + action.BuffDuration.ToString("0.##") + "s");
            if (action.ShieldAmount > 0f)
                effect.Add(GameData.L("ui.runeShield") + " " + action.ShieldAmount.ToString("0.##") + " · " + action.ShieldSeconds.ToString("0.##") + "s");
            if (effect.Count > 0) lines.Add(string.Join(" · ", effect));
        }

        /// <summary>자원별 비용을 현지화 이름과 수치로 반환하고 비용이 없으면 0을 반환한다.\n</summary>
        private static string FormatCosts(ResourceCostSet costs)
        {
            if (costs == null || costs.Amounts.Count == 0) return "0";
            return costs.Format(ResourceName);
        }

        /// <summary>자원 ID를 인스펙터 표시 이름으로 바꾸고 마나 비용은 전력으로 표기한다.\n</summary>
        private static string ResourceName(string resource)
        {
            if (resource == "mana") return GameData.L("ui.energy");
            return GameData.TryL("resource." + resource, out string value) ? value : resource;
        }

        /// <summary>현재 등급과 소지량, 등급색을 적용한 Modifier 효과의 짧은 설명과 장착 가능 행동을 표시한다.</summary>
        private void ShowModifierGrade(GraphNode node, RuneDefinition rune)
        {
            string grade = SpellGrammar.GetModifierGrade(rune, node, GameData.ModifierGrades);
            _editor.TryGetModifierUsage(rune.Id, grade, out int used, out int owned);
            string label = GameData.L("ui.modifierGrade") + " " + grade + " · " + GameData.L("ui.modifierStock")
                + " " + Math.Max(0, owned - used) + "/" + owned;
            _view.AddNote(ModifierGradeText.Colorize(label, grade), 24, 4);
            _view.AddNote(ModifierGradeText.Colorize(GameData.L("ui.gradeStats") + " " + string.Join(" · ", RuneDescriptionBuilder.GetGradeLines(rune, grade, true)), grade), 70, 6);
        }

        /// <summary>Appends compiled behavior range, spawn location, size, and applicable movement speed to the inspector.</summary>
        private void AddBehaviorGeometryNote(CompiledSpell spell, SpellAction action)
        {
            if (action?.Stats == null) return;
            SpellStats stats = action.Stats;
            if (action.Form == SlashBehaviorRules.KIND)
            {
                _view.AddNote(GameData.L("ui.slashLength") + " " + stats.Radius.ToString("0.#") + "px\n"
                    + GameData.L("ui.slashWidth") + " " + stats.BoxWidth.ToString("0.#") + "px\n"
                    + GameData.L("ui.slashAngle") + " " + action.ConeAngle.ToString("0.#") + "°\n"
                    + GameData.L("ui.slashDuration") + " " + stats.Lifetime.ToString("0.##") + "s", 90, 6);
                return;
            }
            if (action.Rules?.DisplayType == SpellDisplayType.ExpandingRing)
            {
                float angle = action.ConeAngle;
                _view.AddNote(GameData.L("ui.spellRange") + " " + action.GetReach(stats).ToString("0.#") + "px\n"
                    + GameData.L("ui.burstAngle") + " " + angle.ToString("0.#") + "°\n"
                    + GameData.L("ui.burstThickness") + " " + (stats.Radius * 2).ToString("0.#") + "px\n"
                    + GameData.L("ui.spellSpeed") + " " + stats.Speed.ToString("0.#") + "px/s", 90, 6);
                return;
            }
            if (action.Form == BeamBehaviorRules.BEAM_KIND)
            {
                _view.AddNote(GameData.L("ui.beamMapRange") + "\n" + GameData.L("ui.beamCurveLength") + " "
                    + stats.BoxLength.ToString("0.#") + "px", 48, 6);
                return;
            }
            bool isAnchored = action.MagicType == SpellGrammar.MAGIC_TYPE_CONE || action.MagicType == SpellGrammar.MAGIC_TYPE_BOX
                || action.SourceElement == "heal" || action.SourceElement == "protection";
            bool isArea = SpellBehaviorRuleRegistry.GetDisplayType(action.Form) != SpellDisplayType.Projectile || isAnchored;
            var lines = new List<string>();
            if (isArea && spell.IsEventAction(action.NodeId)) lines.Add(GameData.L("ui.spellSpawnAtHit"));
            else if (isArea && isAnchored) lines.Add(GameData.L("ui.spellSpawnAtCaster"));
            else
            {
                float reach = action.GetReach(stats);
                if (reach > 0f) lines.Add(GameData.L("ui.spellRange") + " " + reach.ToString("0.#") + "px");
            }

            if (action.MagicType == SpellGrammar.MAGIC_TYPE_BOX && (stats.BoxWidth > 0f || stats.BoxLength > 0f))
                lines.Add(GameData.L("ui.spellBeamSize") + " " + stats.BoxWidth.ToString("0.#") + " × " + stats.BoxLength.ToString("0.#") + "px");
            else
            {
                float radius = action.MagicType == SpellGrammar.MAGIC_TYPE_CONE ? stats.ShapeRadius : stats.Radius;
                if (radius > 0f) lines.Add(GameData.L("ui.spellRadius") + " " + radius.ToString("0.#") + "px");
            }

            if (action.Rules != null && action.Rules.MotionKind == SpellMotionKind.Moving)
                lines.Add(GameData.L("ui.spellSpeed") + " " + stats.Speed.ToString("0.#") + "px/s");
            else if (stats.HomingRange > 0f)
                lines.Add(GameData.L("ui.spellTrackSpeed") + " " + stats.Speed.ToString("0.#") + "px/s");

            if (lines.Count > 0) _view.AddNote(string.Join("\n", lines), 18 * lines.Count + 16, 6);
        }

        /// <summary>선택지 값을 한국어 라벨로 반환하고 번역이 없으면 원문 값을 반환한다.\n</summary>
        private static string OptionLabel(string parameter, string value)
        {
            string prefix = parameter == "trigger" ? "trigger." : parameter == "magicType" ? "magicType."
                : parameter == "element" ? "element." : parameter == "form" ? "magicForm."
                : parameter == "status" ? "status." : "condition.";
            string localized = GameData.L(prefix + value);
            return localized == prefix + value ? value : localized;
        }
    }
}
