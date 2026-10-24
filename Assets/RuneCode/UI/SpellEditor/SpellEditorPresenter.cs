using System;

using UnityEngine.InputSystem;

namespace RuneCode
{
    /// <summary>
    /// 비주얼 스크립팅 편집 Presenter다. 편집 세션(ISpellEditor)의 변경을 그래프·지표에 반영하고,
    /// 도구줄 버튼과 단축키를 편집 명령으로 바꾸며, 프리셋 대상을 Popup 층 팝업으로 연다.
    /// 팔레트는 하위 Presenter가 맡는다.
    /// </summary>
    public sealed class SpellEditorPresenter : IDisposable
    {
        private const string RAM_HIGHLIGHT_OPEN = "<b><color=#FFD54A>";
        private const string RAM_HIGHLIGHT_CLOSE = "</color></b>";
        private const string COST_EXCEED_OPEN = "<b><color=#FF5A5A>";
        private const string COST_EXCEED_CLOSE = "</color></b>";

        private readonly ISpellEditor _editor;
        private readonly ISpellEditorHost _host;
        private readonly UIManager _ui;
        private readonly SpellEditorPanel _view;
        private readonly SpellPalettePresenter _palette;
        private readonly SpellInspectorPresenter _inspector;
        private readonly RuneSynthesisPresenter _synthesis;

        public bool IsSynthesisOpen => _synthesis.IsOpen;

        /// <summary>편집 세션·작업실 호스트·UI 관리자·편집 View로 하위 Presenter를 만들고 View·세션 이벤트와 그래프를 연결한다.</summary>
        public SpellEditorPresenter(ISpellEditor editor, ISpellEditorHost host, UIManager ui, SpellEditorPanel view)
        {
            _editor = editor;
            _host = host;
            _ui = ui;
            _view = view;
            _palette = new SpellPalettePresenter(editor, ui, view);
            _inspector = new SpellInspectorPresenter(editor, view.Inspector);
            _synthesis = new RuneSynthesisPresenter(editor, host, ui, view, _palette);
            BindView();
            _view.GraphCanvas.Initialize(_editor, _view.Font, node => { }, ShowTooltip, _palette.OpenQuickPalette, _palette.OpenSlotPicker,
                _palette.OpenGradePicker);
            _editor.GraphChanged += OnGraphChanged;
            _editor.SpellSwitched += OnSpellSwitched;
            RefreshAll();
        }

        /// <summary>팔레트에서 호버한 룬과 등급을 인스펙터로 전달한다.</summary>
        public void ShowRuneInfo(RuneDefinition rune, string grade)
        {
            PreviewRuneDescription(rune, grade);
        }

        /// <summary>패널이 보일 때 팔레트 필터를 초기화하고 편집을 허용한 뒤 전체를 갱신한다.</summary>
        public void Enter()
        {
            _synthesis.RefreshAvailability();
            _editor.SetEditable(!_synthesis.IsOpen);
            _palette.ResetFilters();
            RefreshAll();
        }

        /// <summary>패널이 숨겨질 때 열린 팝업을 닫고 편집을 금지한다.</summary>
        public void Exit()
        {
            _ui.CloseAllPopups();
            _synthesis.Close();
            _editor.SetEditable(false);
        }

        /// <summary>팝업·문자 입력이 없으면 프레임마다 단축키를 처리한다.</summary>
        public void Tick()
        {
            if (_synthesis.IsOpen || _ui.IsPopupOpen || UiFactory.IsTyping())
            {
                _view.ShowHoverDescription(null);
                _view.GraphCanvas.CancelPickedModule();
                return;
            }
            if (_view.GraphCanvas.HasPickedModule) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null) HandleShortcuts(keyboard);
        }

        /// <summary>편집 세션 이벤트 구독을 해제한다.</summary>
        public void Dispose()
        {
            _synthesis.Dispose();
            _editor.GraphChanged -= OnGraphChanged;
            _editor.SpellSwitched -= OnSpellSwitched;
        }

        /// <summary>열린 합성 결과 또는 창을 닫고 Esc 처리 여부를 반환한다.</summary>
        public bool HandleSynthesisEscape() => _synthesis.HandleEscape();

        /// <summary>도구줄 버튼 이벤트를 편집 명령으로 연결한다.</summary>
        private void BindView()
        {
            _view.UndoClicked += _editor.Undo;
            _view.RedoClicked += _editor.Redo;
            _view.ArrangeClicked += _editor.AutoArrange;
            _view.CopyClicked += () => _editor.CopyNodes(_view.GraphCanvas.SelectedNodes);
            _view.PasteClicked += _editor.PasteNodes;
        }

        /// <summary>현재 입력으로 그래프 삭제·복사·붙여넣기·복제·되돌리기 단축키를 실행한다.</summary>
        private void HandleShortcuts(Keyboard keyboard)
        {
            bool isControlPressed = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            RuneGraphCanvas graph = _view.GraphCanvas;
            if (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) graph.DeleteSelection();
            if (isControlPressed && keyboard.cKey.wasPressedThisFrame) _editor.CopyNodes(graph.SelectedNodes);
            if (isControlPressed && keyboard.vKey.wasPressedThisFrame) _editor.PasteNodes();
            if (isControlPressed && keyboard.dKey.wasPressedThisFrame)
            {
                _editor.CopyNodes(graph.SelectedNodes);
                _editor.PasteNodes();
            }
            if (isControlPressed && keyboard.zKey.wasPressedThisFrame) _editor.Undo();
            if (isControlPressed && keyboard.yKey.wasPressedThisFrame) _editor.Redo();
        }

        /// <summary>팔레트·지표·그래프 뷰와 인스펙터를 현재 편집 상태로 다시 표시한다.</summary>
        private void RefreshAll()
        {
            _palette.Refresh();
            RefreshMetrics();
            _view.GraphCanvas.ResetView();
            _inspector.Refresh();
        }

        /// <summary>그래프 표시·팔레트(Modifier 남은 소지량)·지표·인스펙터를 다시 그린다. GraphChanged 구독자다.</summary>
        private void OnGraphChanged()
        {
            _view.GraphCanvas.RefreshGraph();
            _palette.Refresh();
            RefreshMetrics();
            _inspector.Refresh();
        }

        /// <summary>팝업을 닫고 새 마법 기준으로 전체를 다시 표시한다. SpellSwitched 구독자다.</summary>
        private void OnSpellSwitched()
        {
            _ui.CloseAllPopups();
            _synthesis.Close();
            RefreshAll();
        }

        /// <summary>장착 RAM·그래프 RAM, 시전 비용, 전력 회복량과 쿨다운을 표시하고, 시전 비용이 최대 전력을 넘으면 비용 구간을 붉게 강조한다.</summary>
        private void RefreshMetrics()
        {
            CompiledSpell spell = _editor.CompileResult.Spell;
            int ram = 0;
            foreach (GraphNode node in _editor.Graph.Nodes)
                if (_editor.Graph.IsNodeConnected(node.Id)) ram += GameData.Runes.NodeRam(node);
            // RAM 구간만 노란색 굵게 강조한다.
            string text = RAM_HIGHLIGHT_OPEN + GameData.L("ui.ram") + " " + _host.EquippedRam + " / " + _host.Capacity
                + RAM_HIGHLIGHT_CLOSE;
            if (spell == null)
            {
                _view.SetMetrics(text + "  ·  " + GameData.L("ui.warning"));
                return;
            }

            ResourceCostSet castCosts = spell.GetCastCosts(GameData.Balance.Sim.ExecutionTimeScale);
            string castCostText = GameData.L("ui.castCost") + " " +
                SpellCostText.FormatWithMaxEnergy(castCosts, _host.MaxEnergy);
            // 시전 비용이 최대 전력을 넘어 시전할 수 없으면 비용 구간만 붉게 강조한다.
            if (SpellCostText.ExceedsMaxEnergy(castCosts, _host.MaxEnergy))
                castCostText = COST_EXCEED_OPEN + castCostText + COST_EXCEED_CLOSE;
            text += "  ·  " + castCostText
                + "  ·  " + GameData.L("ui.energy") + " 회복 " + _host.EnergyRegen.ToString("0.#") + " /s"
                + "  ·  " + GameData.L("ui.cooldown") + " "
                + spell.GetCooldownSeconds(GameData.Balance.Sim.ExecutionTimeScale).ToString("0.00") + "s";
            _view.SetMetrics(text);
        }

        /// <summary>
        /// 포인터 아래 노드의 피해·노드 비용을 도구줄에 표시한다. Shape·Modifier 노드는 합쳐지거나 붙은 명령의 수치를 쓰고,
        /// 속성·모디 슬롯의 효과는 인스펙터에 미리 표시하고 호버 종료 시 기존 선택 정보를 복구한다.
        /// </summary>
        private void ShowTooltip(GraphNode node, string slotKey)
        {
            if (node == null)
            {
                PreviewRuneDescription(null, null);
                _view.SetTooltip(GameData.L("ui.executionOrder"));
                return;
            }
            if (slotKey != null && SpellGrammar.TryParseSlotKey(slotKey, out bool isElement, out _))
            {
                string value = node.GetText(slotKey, null);
                string runeId = null;
                string slotGrade = null;
                if (!string.IsNullOrEmpty(value))
                {
                    if (isElement && GameData.Runes.TryGetElement(value, out ElementDefinition element)) runeId = element.RuneId;
                    else if (!isElement && SpellGrammar.TryParseStockKey(value, out string modifierId, out slotGrade)) runeId = modifierId;
                }
                if (runeId != null && GameData.Runes.TryGet(runeId, out RuneDefinition slotRune))
                    PreviewRuneDescription(slotRune, slotGrade);
                else PreviewNodeDescription(node);
                return;
            }
            RuneDefinition rune = GameData.Runes.Get(node.RuneId);
            if (rune.Category == SpellGrammar.CATEGORY_MODIFIER)
            {
                string grade = SpellGrammar.GetModifierGrade(rune, node, GameData.ModifierGrades);
                PreviewRuneDescription(rune, grade);
                _view.SetTooltip(ModifierGradeText.FormatName(rune.Name, grade) + "  ·  " + GameData.L("ui.costMultiplier") + " ×"
                    + GameData.ModifierGrades.GetEnergyMultiplier(rune.Id, grade).ToString("0.##"));
                return;
            }
            PreviewNodeDescription(node);
            SpellAction action = _editor.CompileResult.Spell?.FindNodeAction(_editor.Graph, node.Id);
            string text = rune.Name;
            if (action?.Stats != null) text += "  ·  " + GameData.L("ui.damage") + " " + action.Stats.Damage.ToString("0.#");
            _view.SetTooltip(text + "  ·  " + GameData.L("ui.nodeCost") + " " + SpellCostText.Format(action?.OwnCosts ?? rune.Costs));
        }

        /// <summary>호버한 룬과 등급의 인스펙터 설명을 커서 옆에도 표시하며 null이면 호버 안내를 숨긴다.</summary>
        private void PreviewRuneDescription(RuneDefinition rune, string grade)
        {
            _inspector.PreviewRune(rune, grade);
            _view.ShowHoverDescription(rune == null ? null : _view.Inspector.Description);
        }

        /// <summary>호버한 노드의 현재 인스펙터 설명과 적용 수치를 커서 옆에 표시한다.</summary>
        private void PreviewNodeDescription(GraphNode node)
        {
            _inspector.PreviewNode(node);
            _view.ShowHoverDescription(node == null ? null : _view.Inspector.Description);
        }
    }
}
