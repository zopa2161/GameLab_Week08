using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace RuneCode
{
    /// <summary>
    /// 인스펙터 Presenter다. 선택 노드의 파라미터 행·보조 정보·삭제 버튼과 컴파일 검증 목록을 정해 인스펙터 View에 표시하고,
    /// 입력을 편집 세션에 저장한다. 효과량은 기능 속성일 때만 보이고, 속성 선택은 잠긴 속성을 건너뛴다.
    /// </summary>
    public sealed class SpellInspectorPresenter
    {

        private readonly ISpellEditor _editor;
        private readonly SpellInspectorView _view;
        private readonly RuneGraphCanvas _graph;
        private readonly Action<string> _openSpellPicker;
        private GraphNode _selected;
        private GraphNode _hoveredNode;
        private RuneDefinition _hoveredRune;
        private string _hoveredGrade;

        /// <summary>편집 세션, 인스펙터 View, 그래프(검증 항목 포커스용)와 프리셋 대상 선택 팝업 열기 동작으로 Presenter를 만든다.</summary>
        public SpellInspectorPresenter(ISpellEditor editor, SpellInspectorView view, RuneGraphCanvas graph, Action<string> openSpellPicker)
        {
            _editor = editor;
            _view = view;
            _graph = graph;
            _openSpellPicker = openSpellPicker;
        }

        /// <summary>룬의 동작 설명과 지정 등급의 현재 효과 값을 반환한다.</summary>
        private static string RuneEffectText(RuneDefinition rune, string grade)
        {
            string effect;
            switch (rune.Id)
            {
                case "element.fire": case "elem.fire":
                    CombatBalance combat = GameData.Balance.Combat;
                    effect = "적에게 화상을 부여해 " + combat.BurnSeconds.ToString("0.#") + "초 동안 "
                        + combat.BurnInterval.ToString("0.#") + "초마다 공격 피해의 " + (combat.BurnDamageRatio * 100f).ToString("0.#") + "% 피해를 줍니다.";
                    break;
                case "element.ice": case "elem.ice": effect = "적을 둔화시키고 냉기가 누적되면 빙결시킵니다."; break;
                case "element.lightning": case "elem.arc": effect = "전격 피해와 EMP 효과를 부여합니다."; break;
                case "element.healing": effect = "회복 효과를 적용합니다."; break;
                case "element.protection": effect = "장착한 행동에 따라 보호 효과를 적용합니다."; break;
                case "mod.amplify": effect = "피해·회복 등 효과의 위력을 높입니다."; break;
                case "mod.multi": effect = "생성 개수를 늘립니다. 발사는 전용 개수와 퍼짐을 사용합니다."; break;
                case "mod.pierce": effect = "적을 관통해 뒤쪽 대상도 타격합니다."; break;
                case "mod.homing": effect = "탐색 범위 안의 적을 향해 방향을 조정합니다."; break;
                case "mod.orbit": effect = "생성된 개체가 시전자 주위 궤도를 돕니다."; break;
                case "mod.sequence": effect = "약간의 간격을 두고 같은 행동을 반복 실행합니다."; break;
                case "mod.expand": effect = "효과 면적을 늘립니다. 일반 형상의 길이는 면적 배율의 제곱근만큼 커집니다."; break;
                case "mod.speed": effect = "이동 속도를 높입니다."; break;
                case "mod.duration": effect = "효과의 지속시간을 늘립니다."; break;
                case "behavior.launch": effect = "마법을 발사합니다."; break;
                case "behavior.legacy_burst": effect = "범위에 레거시 폭발 효과를 줍니다."; break;
                case "behavior.persist":
                    RuneStats persist = GameData.Runes.Get("form.zone").Stats;
                    effect = "예고 후 범위를 유지하며 기본 " + persist.TickInterval.ToString("0.##") + "초마다 피해 "
                        + persist.Damage.ToString("0.##") + "를 줍니다. 대상별로 실제 피해를 준 첫 타격에 OnHit이 발동하고, 이후 두 번 타격할 때마다 발동합니다(1·3·5번째). 종료 시점에는 타격하지 않습니다.";
                    break;
                case "behavior.beam": effect = GameData.L("ui.beamHint"); break;
                case "behavior.burst": effect = GameData.L("ui.burstHint"); break;
                case "behavior.apply": effect = "설정한 속성 효과를 대상에 적용합니다."; break;
                default: effect = string.Empty; break;
            }
            object value = GameData.ModifierGrades.GetGradeValue(rune.Id, grade);
            if (value != null)
            {
                if (GameData.ModifierGrades.GetEffectType(rune.Id) == "homingTier")
                {
                    HomingTierDefinition homing = rune.FindHomingTier(value as string);
                    if (homing != null) effect += " 회전 " + homing.HomingTurn.ToString("0.#") + "° · 탐색 범위 " + homing.HomingRange.ToString("0.#") + " · 공전 대상 " + homing.OrbitTargetCount + "개";
                }
                else
                {
                    string label;
                    switch (GameData.ModifierGrades.GetEffectType(rune.Id))
                    {
                        case "effectMultiplier": label = " 효과 배율 ×"; break;
                        case "multipleCount": label = " 생성 개수 "; break;
                        case "waveCount": label = " 실행 회차 "; break;
                        case "pierceCount": label = " 관통 횟수 "; break;
                        case "radiusMultiplier": label = " 면적 배율 ×"; break;
                        case "durationMultiplier": label = " 지속시간 배율 ×"; break;
                        case "speedMultiplier": label = " 속도 배율 ×"; break;
                        case "orbitPath": label = " 궤도 반경 배율 ×"; break;
                        case "knockbackDistance": label = " 기준 피해당 밀어내기 거리(px) "; break;
                        default: label = " 등급 효과: "; break;
                    }
                    if (label != null) effect += label + value;
                    if (rune.Id == "mod.multi" && GameData.ModifierGrades.TryGetCountValue(rune.Id, grade, true, out int launchCount))
                        effect += " · 발사 " + launchCount + "개";
                }
            }
            return ModifierGradeText.FormatName(rune.Name, grade) + " · " + effect;
        }

        /// <summary>호버한 룬과 등급의 설명을 표시하고 null이면 기존 선택 내용을 복구한다.</summary>
        public void PreviewRune(RuneDefinition rune, string grade)
        {
            if (_hoveredNode == null && _hoveredRune == rune && _hoveredGrade == grade) return;
            _hoveredNode = null;
            _hoveredRune = rune;
            _hoveredGrade = grade;
            Refresh();
        }

        /// <summary>선택을 바꾸지 않고 호버 노드의 기존 인스펙터 정보를 표시하며 null이면 선택 정보로 복구한다.</summary>
        public void PreviewNode(GraphNode node)
        {
            if (_hoveredNode == node && _hoveredRune == null) return;
            _hoveredNode = node;
            _hoveredRune = null;
            _hoveredGrade = null;
            Refresh();
        }

        /// <summary>선택 노드를 바꾸고 인스펙터를 다시 그린다.</summary>
        public void Select(GraphNode node)
        {
            _hoveredRune = null;
            _hoveredGrade = null;
            _hoveredNode = null;
            _selected = node;
            Refresh();
        }

        /// <summary>선택 노드를 해제하고 인스펙터를 다시 그린다.</summary>
        public void ClearSelection() => Select(null);

        /// <summary>선택 노드를 현재 그래프에서 다시 찾아 인스펙터와 검증 목록을 다시 그린다.</summary>
        public void Refresh()
        {
            if (_selected != null) _selected = _editor.Graph.FindNode(_selected.Id);
            _view.Begin();
            if (_hoveredRune != null)
            {
                _view.AddTitle(ModifierGradeText.FormatName(_hoveredRune.Name, _hoveredGrade));
                _view.AddNote(RuneEffectText(_hoveredRune, _hoveredGrade), 60f, 10f);
                return;
            }
            if (_hoveredNode != null) _hoveredNode = _editor.Graph.FindNode(_hoveredNode.Id);
            GraphNode displayedNode = _hoveredNode ?? _selected;
            if (displayedNode == null) _view.AddEmptyHint(GameData.L("ui.selectNode"));
            else ShowNode(displayedNode);
            ShowValidation();
        }

        /// <summary>Displays the selected rune, editable parameters, type-specific guidance and stats, and delete action.</summary>
        private void ShowNode(GraphNode node)
        {
            RuneDefinition rune = GameData.Runes.Get(node.RuneId);
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
            foreach (ParameterDefinition parameter in rune.Params)
            {
                if (IsParameterVisible(node, parameter)) ShowParameter(node, rune, parameter);
            }
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
                if (rune.Id == "behavior.persist") _view.AddNote(RuneEffectText(rune, null), 60f, 10f);
            }
            if (rune.Category != SpellGrammar.CATEGORY_CORE) _view.AddButton(GameData.L("ui.delete"), "InspectorDeleteButton", () => _editor.RemoveNode(node.Id));
        }

        /// <summary>행동 노드에서 사용 가능한 속성과 모든 모디 슬롯의 장착·빈·잠금 상태를 한 줄씩 표시한다.</summary>
        private void ShowSlots(GraphNode node)
        {
            var lines = new List<string>();
            for (int index = 0; index < SpellGrammar.MAX_ELEMENT_SLOTS; index++)
            {
                string slotKey = SpellGrammar.ElementSlotKey(index);
                if (!GraphNodeLayout.IsSlotLocked(node, slotKey)) lines.Add(SlotLine(node, slotKey));
            }
            for (int index = 0; index < SpellGrammar.MAX_MODIFIER_SLOTS; index++)
                lines.Add(SlotLine(node, SpellGrammar.ModifierSlotKey(index)));
            _view.AddNote(string.Join("\n", lines), 34, 6);
        }

        /// <summary>슬롯 한 칸의 표시 문구를 반환한다. 잠긴 칸은 잠김, 빈 칸은 비어 있음, 장착 칸은 룬 이름(모디는 등급 포함)이다.</summary>
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

        /// <summary>형태 노드별 역할을 한 줄 설명으로 표시한다.</summary>
        private void ShowShapeHint(RuneDefinition rune)
        {
            string key = "ui." + rune.Id + "Hint";
            string hint = GameData.L(key);
            if (hint != key) _view.AddNote(hint, 24, 4);
        }

        /// <summary>선택 행동에 대응하는 내부 형태의 기본값과 Modifier 적용 후 컴파일값을 표시한다.</summary>
        private void ShowBehaviorStats(GraphNode node, RuneDefinition rune)
        {
            string formId = GetBehaviorFormId(rune.Id);
            if (formId == null || !GameData.Runes.TryGet(formId, out RuneDefinition form)) return;

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

        /// <summary>행동 ID를 해당 행동이 사용하는 내부 효과 형태의 룬 ID로 바꾸고 지원하지 않으면 null을 반환한다.</summary>
        private static string GetBehaviorFormId(string behaviorId)
        {
            if (SpellBehaviorRuleRegistry.TryGetByRuneId(behaviorId, out SpellBehaviorRules rules)) return rules.StatsRuneId;
            switch (behaviorId)
            {
                case "behavior.launch": return "form.bolt";
                case "behavior.burst": return "form.burst";
                case "behavior.persist":
                case "behavior.apply": return "form.zone";
                default: return null;
            }
        }

        /// <summary>내부 형태의 기본 전력·피해·발사 수·지속시간과 행동의 최종값을 한 줄씩 비교해 표시한다.</summary>
        private void AddBehaviorStatNote(RuneDefinition form, SpellAction action, RuneDefinition behavior, GraphNode node)
        {
            var lines = new List<string>
            {
                FormatTransition(GameData.L("ui.runeUsedEnergy"), form.Energy, action == null ? form.Energy : action.OwnEnergy)
            };
            if (behavior.Id == "behavior.beam")
            {
                SpellStats beam = action?.Stats;
                double initialCost = action == null
                    ? form.Energy : action.GetExecutionCosts(SpellModifierValues.None).GetAmount("mana");
                lines.Clear();
                lines.Add(GameData.L("ui.beamInitialCost") + " " + initialCost.ToString("0.##"));
                lines.Add(GameData.L("ui.beamDuration") + " " + GameData.L("ui.beamUnlimitedHold"));
                lines.Add(GameData.L("ui.beamStopCooldown") + " " + GameData.Balance.Sim.BeamCooldownSeconds.ToString("0.##") + "s");
                lines.Add(GameData.L("ui.runeDamageAmount") + " " + (beam?.Damage ?? form.Stats.Damage).ToString("0.##")
                    + " / " + form.Stats.TickInterval.ToString("0.##") + "s");
                lines.Add(GameData.L("ui.beamWidth") + " " + (beam?.BoxWidth ?? GameData.Runes.Get("form.bolt").Stats.Radius).ToString("0.##") + "px");
                lines.Add(GameData.L("ui.beamSustainCost") + " " + (initialCost / form.Stats.MinimumLifetime).ToString("0.##") + "/s");
                lines.Add(GameData.L("ui.beamHint"));
                _view.AddNote(string.Join("\n", lines), 150, 6);
                return;
            }

            if (behavior.Id == SpellGrammar.APPLY_RUNE)
            {
                AddApplyStats(lines, action, behavior, node);
            }
            else
            {
                SpellStats stats = action?.Stats;
                lines.Add(FormatTransition(GameData.L("ui.runeDamageAmount"), form.Stats.Damage, stats == null ? form.Stats.Damage : stats.Damage));
                lines.Add(FormatTransition(GameData.L("ui.runeCount"), form.Stats.Count, action == null ? form.Stats.Count : action.Count));
                lines.Add(FormatTransition(GameData.L("ui.runeLifetime"), form.Stats.Lifetime, stats == null ? form.Stats.Lifetime : stats.Lifetime, "s"));
            }
            _view.AddNote(string.Join("\n", lines), 34, 6);
        }

        /// <summary>Apply 행동의 효과 강도와 보호막 지속시간을 기본값에서 Modifier 적용값까지 표시한다.</summary>
        private static void AddApplyStats(List<string> lines, SpellAction action, RuneDefinition behavior, GraphNode node)
        {
            string sourceElement = action?.SourceElement;
            if (string.IsNullOrEmpty(sourceElement) && GameData.Runes.TryGetNodeElement(node, out ElementDefinition element))
                sourceElement = element.InternalValue;
            if (sourceElement == "heal" || sourceElement == "protection")
            {
                float basePower = action == null ? GetDefaultNumber(behavior, node, SpellGrammar.POWER_PARAM) : action.Power;
                float damageMultiplier = action?.ModifierValues?.DamageMultiplier ?? 1f;
                lines.Add(FormatTransition(GameData.L("ui.runePower"), basePower, basePower * damageMultiplier));
            }
            if (sourceElement == "protection")
            {
                float baseDuration = action == null ? GetDefaultNumber(behavior, node, SpellGrammar.BUFF_DURATION_PARAM) : action.BuffDuration;
                float durationMultiplier = action?.ModifierValues?.DurationMultiplier ?? 1f;
                lines.Add(FormatTransition(GameData.L("ui.runeBuffDuration"), baseDuration, baseDuration * durationMultiplier, "s"));
            }
        }

        /// <summary>행동 노드에서 지정 파라미터 값을 읽고 없으면 룬 정의의 기본값을 반환한다.</summary>
        private static float GetDefaultNumber(RuneDefinition rune, GraphNode node, string parameterId)
        {
            foreach (ParameterDefinition parameter in rune.Params)
                if (parameter.Id == parameterId) return node.GetNumber(parameterId, parameter.DefaultNumber);
            return 0f;
        }

        /// <summary>숫자 두 개를 지정 라벨과 기본값→적용값 형태로 묶어 반환한다.</summary>
        private static string FormatTransition(string label, double baseValue, double effectiveValue, string suffix = "")
        {
            return label + " " + baseValue.ToString("0.##") + suffix + " → " + effectiveValue.ToString("0.##") + suffix;
        }

        /// <summary>현재 등급 색을 적용해 Modifier 효과의 짧은 설명을 표시한다.</summary>
        private void ShowModifierHint(RuneDefinition rune, string grade)
        {
            string modifierName = rune.Id.Substring("mod.".Length);
            string key = "ui.mod." + modifierName + "Hint";
            string hint = GameData.L(key);
            if (hint != key) _view.AddNote(ModifierGradeText.Colorize(hint, grade), 52, 6);
        }

        /// <summary>노드에 선택된 속성의 이름과 원소·기능 효과 구분을 표시한다.</summary>
        private void ShowElementInfo(GraphNode node)
        {
            if (!GameData.Runes.TryGetNodeElement(node, out ElementDefinition element)) return;
            string classification = element.IsFunctional ? GameData.L("ui.runeFunctionalElement") : GameData.L("ui.runeElementalElement");
            _view.AddNote(OptionLabel("element", element.Id) + " · " + classification, 24, 4);
        }

        /// <summary>선택 노드가 포함된 컴파일 행동의 자원 비용과 비시간 전투 수치를 표시한다.</summary>
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
                else if (action.Kind == "call")
                    lines.Add(GameData.L("ui.runeEffectiveCost") + " " + FormatCosts(action.OwnCosts));
                if (action.EnergyMultiplier != 1f)
                    lines.Add(GameData.L("ui.runeAppliedMultiplier") + " ×" + action.EnergyMultiplier.ToString("0.##"));

                AddActionStats(lines, action, rune);
                if (lines.Count > 0) _view.AddNote(string.Join("\n", lines), 34, 6);
            }
        }

        /// <summary>선택 노드의 실행을 찾고, 컴파일 과정에서 행동 노드에 합쳐지는 Shape는 연결된 Behavior 실행으로 연결한다.</summary>
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

        /// <summary>아직 실행에 연결되지 않은 룬의 테이블 기본 전투 수치와 형상 비용률을 표시한다.</summary>
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

        /// <summary>실행 행동 목록에서 노드 자체 또는 해당 노드를 부착 데이터로 사용하는 행동을 깊이 우선으로 찾는다.</summary>
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

        /// <summary>행동 종류에 맞는 비시간 전투 수치, 제어 흐름 조건 또는 호출 마법 요약을 목록에 추가한다.</summary>
        private static void AddActionStats(List<string> lines, SpellAction action, RuneDefinition rune)
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
                // 궤도 반경은 행동마다 있으므로 공전 모드가 장착된 경우에만 표시한다.
                bool isOrbiting = action.Mods.Contains(OrbitModifierRules.RUNE_ID);
                if (isOrbiting && stats.OrbitRadius > 0f) movement.Add(GameData.L("ui.runeOrbitRadius") + " " + stats.OrbitRadius.ToString("0.#") + "px");
                if (action.Form == BeamBehaviorRules.BEAM_KIND)
                    movement.Add(GameData.L("ui.beamDuration") + " " + GameData.L("ui.beamUnlimitedHold"));
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

            if (action.Kind == "delay")
                lines.Add(GameData.L("ui.runeDelay") + " " + action.Seconds.ToString("0.##") + "s");
            else if (action.Kind == "repeat")
                lines.Add(GameData.L("ui.runeRepeat") + " " + action.Times + " · " + action.Interval.ToString("0.##") + "s");
            else if (action.Kind == "if" && action.Condition != null)
                lines.Add(GameData.L("ui.runeCondition") + " " + FormatCondition(action.Condition));

            if (action.CalledSpell != null)
                lines.Add(GameData.L("ui.runeCalledSpell") + " " + action.CalledSpell.Name + " · "
                    + action.CalledSpell.GetEstimatedExecutionSeconds(GameData.Balance.Sim.ExecutionTimeScale).ToString("0.##") + "s");
        }

        /// <summary>조건 유형에 맞는 퍼센트·거리·상태 값만 붙여 사용자용 조건 요약을 반환한다.</summary>
        private static string FormatCondition(SpellCondition condition)
        {
            string value = OptionLabel("type", condition.Type);
            switch (condition.Type)
            {
                case "targetHpBelow":
                case "selfHpBelow":
                    return value + " " + condition.Pct.ToString("0.#") + "%";
                case "targetDistanceBelow":
                    return value + " " + condition.Px.ToString("0.#") + "px";
                case "targetHasStatus":
                    return value + " " + OptionLabel("status", condition.Status);
                default:
                    return value;
            }
        }

        /// <summary>자원별 비용을 현지화 이름과 수치로 반환하고 비용이 없으면 0을 반환한다.</summary>
        private static string FormatCosts(ResourceCostSet costs)
        {
            if (costs == null || costs.Amounts.Count == 0) return "0";
            return costs.Format(ResourceName);
        }

        /// <summary>자원 ID를 인스펙터 표시 이름으로 바꾸고 마나 비용은 전력으로 표기한다.</summary>
        private static string ResourceName(string resource)
        {
            if (resource == "mana") return GameData.L("ui.energy");
            string key = "resource." + resource;
            string value = GameData.L(key);
            return value == key ? resource : value;
        }

        /// <summary>Modifier 등급 선택 버튼과 현재 등급의 고정 수치 안내를 표시한다.</summary>
        private void ShowModifierGrade(GraphNode node, RuneDefinition rune)
        {
            string grade = SpellGrammar.GetModifierGrade(rune, node, GameData.ModifierGrades);
            _editor.TryGetModifierUsage(rune.Id, grade, out int used, out int owned);
            string label = GameData.L("ui.modifierGrade") + " " + grade + " · " + GameData.L("ui.modifierStock")
                + " " + Math.Max(0, owned - used) + "/" + owned;
            _view.AddButton(ModifierGradeText.Colorize(label, grade), "InspectorButton", () => SelectNextModifierGrade(node.Id, rune));
            _view.AddNote(ModifierGradeText.Colorize(GameData.L("ui.gradeStats") + " " + FormatModifierStats(rune, grade), grade), 70, 6);
        }

        /// <summary>소지량이 있는 다음 등급을 찾아 선택 노드에 저장한다.</summary>
        private void SelectNextModifierGrade(string nodeId, RuneDefinition rune)
        {
            GraphNode node = _editor.Graph.FindNode(nodeId);
            IReadOnlyList<string> grades = GameData.ModifierGrades.GetAvailableGrades(rune.Id);
            if (node == null || grades.Count < 2) return;
            string currentGrade = SpellGrammar.GetModifierGrade(rune, node, GameData.ModifierGrades);
            int currentIndex = -1;
            for (int index = 0; index < grades.Count; index++)
            {
                if (grades[index] == currentGrade) { currentIndex = index; break; }
            }
            for (int step = 1; step < grades.Count; step++)
            {
                string grade = grades[(currentIndex + step + grades.Count) % grades.Count];
                if (!_editor.TryGetModifierUsage(rune.Id, grade, out int used, out int owned) || owned <= used) continue;
                _editor.SetNodeText(nodeId, SpellGrammar.MODIFIER_GRADE_PARAM, grade);
                Refresh();
                return;
            }
        }

        /// <summary>등급 고정값과 Modifier의 보조 고정 수치를 안내 문자열로 반환한다.</summary>
        private static string FormatModifierStats(RuneDefinition rune, string grade)
        {
            var values = new List<string>();
            ModifierGradeTable grades = GameData.ModifierGrades;
            string effectType = grades.GetEffectType(rune.Id);
            object gradeValue = grades.GetGradeValue(rune.Id, grade);
            switch (effectType)
            {
                case "effectMultiplier": AddMultiplier(values, "ui.gradeDamage", gradeValue); break;
                case "multipleCount": values.Add(GameData.L("ui.gradeCount") + " " + gradeValue); break;
                case "waveCount": values.Add(GameData.L("ui.gradeWaveCount") + " " + gradeValue); break;
                case "pierceCount": values.Add(GameData.L("ui.gradePierce") + " " + gradeValue); break;
                case "radiusMultiplier": AddMultiplier(values, "ui.gradeRadius", gradeValue); break;
                case "durationMultiplier": AddMultiplier(values, "ui.gradeDuration", gradeValue); break;
                case "speedMultiplier": AddMultiplier(values, "ui.gradeSpeed", gradeValue); break;
                case "homingTier":
                    string tier = gradeValue as string;
                    HomingTierDefinition homing = rune.FindHomingTier(tier);
                    values.Add(tier);
                    if (homing != null)
                    {
                        values.Add(GameData.L("ui.gradeHomingTurn") + " " + homing.HomingTurn.ToString("0.#") + "°");
                        values.Add(GameData.L("ui.gradeHomingRange") + " " + homing.HomingRange.ToString("0.#"));
                        values.Add(GameData.L("ui.gradeOrbitTargetCount") + " " + homing.OrbitTargetCount);
                    }
                    break;
                case "knockbackDistance":
                    values.Add("피해 " + rune.Stats.KnockbackReferenceDamage.ToString("0.##") + "당 밀어내기 " + gradeValue + "px");
                    values.Add("밀어내기 시간 " + rune.Stats.KnockbackSeconds.ToString("0.##") + "s");
                    break;
                case "orbitPath":
                    values.Add(GameData.L("ui.runeOrbitRadius") + " ×" + gradeValue);
                    values.Add(GameData.L("ui.gradeAngularSpeed") + " " + rune.Stats.AngularSpeed.ToString("0.#") + "°/s");
                    values.Add(GameData.L("ui.gradeEntrySpeed") + " " + rune.Stats.EntrySpeed.ToString("0.#") + "px/s");
                    break;
            }
            RuneStats stats = rune.Stats;
            values.Add(GameData.L("ui.costMultiplier") + " ×" + grades.GetEnergyMultiplier(rune.Id, grade).ToString("0.##"));
            if (rune.Id == "mod.multi")
            {
                if (grades.TryGetCountValue(rune.Id, grade, true, out int launchCount)
                    && grades.TryGetLaunchGradeValues(rune.Id, out _))
                    values.Add(GameData.L("ui.gradeLaunchCount") + " " + launchCount);
                if (stats.DamageMultiplier != 1f) values.Add(GameData.L("ui.gradeDamage") + " ×" + stats.DamageMultiplier.ToString("0.##"));
                if (stats.SpreadAngle > 0f) values.Add(GameData.L("ui.gradeSpread") + " " + stats.SpreadAngle.ToString("0.#") + "°");
                if (grades.TryGetLaunchSpreadAngle(rune.Id, grade, out float launchSpread))
                    values.Add(GameData.L("ui.gradeLaunchSpread") + " " + launchSpread.ToString("0.#") + "°");
            }
            if (rune.Id == "mod.sequence")
            {
                if (stats.DamageMultiplier != 1f) values.Add(GameData.L("ui.gradeDamage") + " ×" + stats.DamageMultiplier.ToString("0.##"));
                if (stats.WaveInterval > 0f) values.Add(GameData.L("ui.gradeWaveInterval") + " " + stats.WaveInterval.ToString("0.##") + "s");
            }
            if (rune.Id == "mod.expand" && stats.DamageMultiplier != 1f)
                values.Add(GameData.L("ui.gradeDamage") + " ×" + stats.DamageMultiplier.ToString("0.##"));
            if (grades.TryGetPierceDamageMultiplier(rune.Id, grade, out float pierceMultiplier))
                values.Add("관통 적중당 피해 ×" + pierceMultiplier.ToString("0.##") + " · 최소 피해 1");
            return string.Join(" · ", values);
        }

        /// <summary>숫자 등급 값에 번역된 효과 라벨과 배율 기호를 붙인다.</summary>
        private static void AddMultiplier(List<string> values, string labelKey, object value)
        {
            values.Add(GameData.L(labelKey) + " ×" + Convert.ToSingle(value).ToString("0.##"));
        }

        /// <summary>파라미터 종류(숫자·프리셋 대상·문자·선택지)에 맞는 행을 표시하고 입력을 편집 세션 저장으로 연결한다.</summary>
        private void ShowParameter(GraphNode node, RuneDefinition rune, ParameterDefinition parameter)
        {
            string nodeId = node.Id;
            string label = GameData.L("param." + parameter.Id);
            if (parameter.Kind == "number")
            {
                bool isScale = rune.Category == SpellGrammar.CATEGORY_MODIFIER;
                _view.AddNumberRow(label, node.GetNumber(parameter.Id, parameter.DefaultNumber).ToString("0.###"), isScale,
                    text => { if (float.TryParse(text, out float amount)) _editor.SetNodeNumber(nodeId, parameter.Id, Mathf.Clamp(amount, parameter.Min, parameter.Max)); },
                    () => StepNumber(nodeId, parameter, -1), () => StepNumber(nodeId, parameter, 1));
            }
            else if (parameter.Kind == "text" && rune.Id == SpellGrammar.CALL_RUNE && parameter.Id == "spellId")
            {
                _view.AddSpellRow(label, CalledSpellLabel(node.GetText(parameter.Id, parameter.DefaultText)), () => _openSpellPicker(nodeId));
            }
            else if (parameter.Kind == "text")
            {
                _view.AddTextRow(label, node.GetText(parameter.Id, parameter.DefaultText), text => _editor.SetNodeText(nodeId, parameter.Id, text));
            }
            else
            {
                string current = node.GetText(parameter.Id, parameter.DefaultText);
                _view.AddEnumRow(label, OptionLabel(parameter.Id, current), () => _editor.SetNodeText(nodeId, parameter.Id, NextOption(parameter, current)));
            }
        }

        /// <summary>Appends compiled behavior range, spawn location, size, and applicable movement speed to the inspector.</summary>
        private void AddBehaviorGeometryNote(CompiledSpell spell, SpellAction action)
        {
            if (action?.Stats == null) return;
            SpellStats stats = action.Stats;
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

        /// <summary>컴파일 성공 여부, 오류·경고(누르면 해당 노드로 이동)와 조작 안내를 검증 목록에 표시한다.</summary>
        private void ShowValidation()
        {
            RectTransform list = _view.BeginValidation();
            CompileResult result = _editor.CompileResult;
            if (result.Ok) _view.AddIssue(list, GameData.L("ui.valid"), "ValidationSuccessRow", null);
            foreach (CompileIssue issue in result.Errors) AddIssue(list, issue, "ValidationErrorRow");
            foreach (CompileIssue issue in result.Warnings) AddIssue(list, issue, "ValidationWarningRow");
            _view.AddIssue(list, GameData.L("ui.controls"), null, null);
        }

        /// <summary>검증 항목 하나를 코드·문구로 표시하고 누르면 관련 노드로 그래프를 이동하게 한다.</summary>
        private void AddIssue(RectTransform list, CompileIssue issue, string prefabName)
        {
            string nodeId = issue.NodeId;
            _view.AddIssue(list, issue.Code + " · " + CompileIssueText.Format(issue), prefabName, () => { if (nodeId != null) _graph.FocusNode(nodeId); });
        }

        /// <summary>수식 배율을 데이터의 한 단계만큼 바꾸고 범위 안으로 제한해 저장한 뒤 인스펙터를 다시 그린다.</summary>
        private void StepNumber(string nodeId, ParameterDefinition parameter, int direction)
        {
            GraphNode node = _editor.Graph.FindNode(nodeId);
            if (node == null) return;
            float value = node.GetNumber(parameter.Id, parameter.DefaultNumber) + parameter.Step * direction;
            _editor.SetNodeNumber(nodeId, parameter.Id, Mathf.Clamp(value, parameter.Min, parameter.Max));
            Refresh();
        }

        /// <summary>프리셋 호출 대상 ID를 보관함 이름과 ID, 미선택 또는 없는 마법 안내 문구로 바꿔 반환한다.</summary>
        private string CalledSpellLabel(string spellId)
        {
            SpellGraph called = _editor.Library.FirstOrDefault(graph => graph.Id == spellId);
            if (called != null) return called.Name + "\n" + called.Id;
            return string.IsNullOrEmpty(spellId) ? GameData.L("ui.selectSpell") : spellId + " · " + GameData.L("ui.missingSpell");
        }

        /// <summary>
        /// 인스펙터에 파라미터를 표시할지 반환한다. 효과량·보호막 지속시간은 Functional(회복·보호) 속성 노드와 그 속성의 Apply에서만 표시한다.
        /// 행동 노드의 등급·슬롯 값은 노드 슬롯과 슬롯 목록으로 다루므로 파라미터 행으로 표시하지 않는다.
        /// </summary>
        private static bool IsParameterVisible(GraphNode node, ParameterDefinition parameter)
        {
            if (GraphNodeLayout.HasSlots(GameData.Runes.Get(node.RuneId))
                && (parameter.Id == SpellGrammar.MODIFIER_GRADE_PARAM
                    || SpellGrammar.TryParseSlotKey(parameter.Id, out _, out _))) return false;
            if (parameter.Id != SpellGrammar.POWER_PARAM && parameter.Id != SpellGrammar.BUFF_DURATION_PARAM) return true;
            if (GameData.Runes.TryGetRuneElement(node.RuneId, out ElementDefinition own)) return own.IsFunctional;
            return GameData.Runes.TryGetNodeElement(node, out ElementDefinition element) && element.IsFunctional;
        }

        /// <summary>
        /// 선택지 파라미터의 다음 값을 반환한다. 속성(element)은 해금하지 않은 속성을 건너뛰며, 선택 가능한 값이 없으면 현재 값을 유지한다.
        /// </summary>
        private string NextOption(ParameterDefinition definition, string current)
        {
            int index = 0;
            for (int i = 0; i < definition.Options.Count; i++)
            {
                if (definition.Options[i] == current) index = i;
            }
            for (int step = 1; step <= definition.Options.Count; step++)
            {
                string candidate = definition.Options[(index + step) % definition.Options.Count];
                bool isLockedElement = definition.Id == SpellGrammar.ELEMENT_PARAM && GameData.Runes.TryGetElement(candidate, out ElementDefinition element)
                    && !_editor.IsRuneUnlocked(element.RuneId);
                if (!isLockedElement) return candidate;
            }
            return current;
        }

        /// <summary>선택지 값을 한국어 라벨로 반환하고 번역이 없으면 원문 값을 반환한다.</summary>
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
