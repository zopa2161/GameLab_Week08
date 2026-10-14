using System;

using UnityEngine;

using UnityEngine.InputSystem;

namespace RuneCode
{
    /// <summary>
    /// 비주얼 스크립팅 편집 Presenter다. 편집 세션(ISpellEditor)의 변경을 그래프·제목·지표·인스펙터에 반영하고,
    /// 제목 줄·도구줄 버튼과 단축키를 편집 명령으로 바꾸며, 보관함·프리셋 대상·이름 변경·공유·튜토리얼을 Popup 층 팝업으로 연다.
    /// 팔레트와 인스펙터는 하위 Presenter가 맡는다.
    /// </summary>
    public sealed class SpellEditorPresenter : IDisposable
    {
        private const string RAM_HIGHLIGHT_OPEN = "<b><color=#FFD54A>";
        private const string RAM_HIGHLIGHT_CLOSE = "</color></b>";

        private readonly ISpellEditor _editor;
        private readonly ISpellEditorHost _host;
        private readonly UIManager _ui;
        private readonly SpellEditorPanel _view;
        private readonly SpellPalettePresenter _palette;
        private readonly SpellInspectorPresenter _inspector;

        private bool _isTutorialActive;
        private int _tutorialStartExecutions;

        /// <summary>편집 세션·작업실 호스트·UI 관리자·편집 View로 하위 Presenter를 만들고 View·세션 이벤트와 그래프를 연결한다.</summary>
        public SpellEditorPresenter(ISpellEditor editor, ISpellEditorHost host, UIManager ui, SpellEditorPanel view)
        {
            _editor = editor;
            _host = host;
            _ui = ui;
            _view = view;
            _palette = new SpellPalettePresenter(editor, ui, view);
            _inspector = new SpellInspectorPresenter(editor, view.Inspector, view.GraphCanvas, OpenSpellPicker);
            BindView();
            _view.GraphCanvas.Initialize(_editor, _view.Font, _inspector.Select, ShowTooltip, _palette.OpenQuickPalette, _palette.OpenSlotPicker);
            _editor.GraphChanged += OnGraphChanged;
            _editor.SpellSwitched += OnSpellSwitched;
            RefreshAll();
        }

        /// <summary>패널이 보일 때 편집을 허용하고 전체를 갱신한다.</summary>
        public void Enter()
        {
            _editor.SetEditable(true);
            RefreshAll();
        }

        /// <summary>패널이 숨겨질 때 열린 팝업을 닫고 편집을 금지한다.</summary>
        public void Exit()
        {
            _ui.CloseAllPopups();
            _editor.SetEditable(false);
        }

        /// <summary>프레임마다 활성 튜토리얼의 완료 조건을 확인하고, 팝업·문자 입력이 없으면 단축키를 처리한다.</summary>
        public void Tick()
        {
            UpdateTutorial();
            if (_ui.IsPopupOpen || UiFactory.IsTyping()) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null) HandleShortcuts(keyboard);
        }

        /// <summary>편집 세션 이벤트 구독을 해제한다.</summary>
        public void Dispose()
        {
            _editor.GraphChanged -= OnGraphChanged;
            _editor.SpellSwitched -= OnSpellSwitched;
        }

        /// <summary>제목 줄·도구줄·튜토리얼 버튼 이벤트를 편집 명령과 팝업 열기로 연결한다.</summary>
        private void BindView()
        {
            _view.LibraryClicked += OpenLibrary;
            _view.RenameClicked += OpenRename;
            _view.SaveClicked += () => _editor.Save();
            _view.ShareClicked += OpenShare;
            _view.UndoClicked += _editor.Undo;
            _view.RedoClicked += _editor.Redo;
            _view.ArrangeClicked += _editor.AutoArrange;
            _view.CopyClicked += () => _editor.CopyNodes(_view.GraphCanvas.SelectedNodes);
            _view.PasteClicked += _editor.PasteNodes;
            _view.TutorialClicked += StartTutorial;
        }

        /// <summary>현재 입력으로 그래프 삭제·복사·붙여넣기·복제·저장·시험·되돌리기 단축키를 실행한다.</summary>
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
            if (isControlPressed && keyboard.sKey.wasPressedThisFrame) _editor.Save();
            if (keyboard.digit1Key.wasPressedThisFrame) _host.FireTest();
            if (isControlPressed && keyboard.zKey.wasPressedThisFrame) _editor.Undo();
            if (isControlPressed && keyboard.yKey.wasPressedThisFrame) _editor.Redo();
        }

        /// <summary>제목·팔레트·지표·그래프 뷰와 인스펙터를 현재 편집 상태로 다시 표시한다.</summary>
        private void RefreshAll()
        {
            _view.SetTitle(_editor.Graph.Name);
            _palette.Refresh();
            RefreshMetrics();
            _view.GraphCanvas.ResetView();
            _inspector.Refresh();
        }

        /// <summary>그래프 표시·제목·팔레트(Modifier 남은 소지량)·지표·인스펙터를 다시 그린다. GraphChanged 구독자다.</summary>
        private void OnGraphChanged()
        {
            _view.GraphCanvas.RefreshGraph();
            _view.SetTitle(_editor.Graph.Name);
            _palette.Refresh();
            RefreshMetrics();
            _inspector.Refresh();
        }

        /// <summary>팝업과 선택을 해제하고 새 마법 기준으로 전체를 다시 표시한다. SpellSwitched 구독자다.</summary>
        private void OnSpellSwitched()
        {
            _ui.CloseAllPopups();
            _inspector.ClearSelection();
            RefreshAll();
        }

        /// <summary>장착 RAM·그래프 RAM, 시전 비용, 쿨다운과 예상 실행 시간을 표시한다.</summary>
        private void RefreshMetrics()
        {
            CompiledSpell spell = _editor.CompileResult.Spell;
            int ram = 0;
            foreach (GraphNode node in _editor.Graph.Nodes)
                if (_editor.Graph.IsNodeConnected(node.Id)) ram += GameData.Runes.NodeRam(node);
            // RAM 구간만 노란색 굵게 강조한다.
            string text = RAM_HIGHLIGHT_OPEN + GameData.L("ui.ram") + " " + _host.EquippedRam + "/" + _host.Capacity + "  ·  " + ram + " RAM"
                + RAM_HIGHLIGHT_CLOSE;
            text += spell == null
                ? "  ·  " + GameData.L("ui.warning")
                : "  ·  " + GameData.L("ui.castCost") + " " + SpellCostText.Format(spell.CastCosts) + "  ·  " + GameData.L("ui.cooldown") + " "
                    + spell.GetCooldownSeconds(GameData.Balance.Sim.ExecutionTimeScale).ToString("0.00") + "s  ·  " + GameData.L("ui.executionTime") + " "
                    + spell.GetEstimatedExecutionSeconds(GameData.Balance.Sim.ExecutionTimeScale).ToString("0.00") + "s";
            _view.SetMetrics(text);
        }

        /// <summary>
        /// 포인터 아래 노드의 피해·노드 비용을 도구줄에 표시한다. Shape·Modifier 노드는 합쳐지거나 붙은 명령의 수치를 쓰고,
        /// Modifier는 비용 배율을 함께 표시한다. 노드가 없으면 실행 순서 안내를 표시한다.
        /// </summary>
        private void ShowTooltip(GraphNode node)
        {
            if (node == null)
            {
                _view.SetTooltip(GameData.L("ui.executionOrder"));
                return;
            }
            RuneDefinition rune = GameData.Runes.Get(node.RuneId);
            if (rune.Category == SpellGrammar.CATEGORY_MODIFIER)
            {
                string grade = SpellGrammar.GetModifierGrade(rune, node, GameData.ModifierGrades);
                _view.SetTooltip(ModifierGradeText.FormatName(rune.Name, grade) + "  ·  " + GameData.L("ui.costMultiplier") + " ×" + rune.EnergyMult.ToString("0.##"));
                return;
            }
            SpellAction action = _editor.CompileResult.Spell?.FindNodeAction(_editor.Graph, node.Id);
            string text = rune.Name;
            if (action?.Stats != null) text += "  ·  " + GameData.L("ui.damage") + " " + action.Stats.Damage.ToString("0.#");
            _view.SetTooltip(text + "  ·  " + GameData.L("ui.nodeCost") + " " + SpellCostText.Format(action?.OwnCosts ?? rune.Costs));
        }

        /// <summary>튜토리얼 안내 팝업을 열고 현재 시험 실행 수를 완료 판정 기준으로 저장한다.</summary>
        private void StartTutorial()
        {
            _isTutorialActive = true;
            _tutorialStartExecutions = _host.TestExecutionCount;
            _ui.OpenPopup<TutorialPopup>();
        }

        /// <summary>Shape→발사 체인 연결, Shape에 붙인 화염 속성 노드, 시험 시전을 모두 마친 활성 튜토리얼을 완료 처리한다.</summary>
        private void UpdateTutorial()
        {
            bool hasCastShape = false;
            bool hasLaunch = false;
            bool hasFire = false;
            foreach (GraphNode node in _editor.Graph.Nodes)
            {
                bool isShape = SpellGrammar.MagicTypeOf(node.RuneId) != null;
                foreach (GraphEdge edge in _editor.Graph.Edges)
                {
                    if (node.RuneId == SpellGrammar.ElementRune("fire") && edge.FromNode == node.Id && edge.FromPort == SpellGrammar.ELEMENT_PORT) hasFire = true;
                    if (isShape && edge.ToNode == node.Id && edge.ToPort == SpellGrammar.EXEC_PORT) hasCastShape = true;
                    if (node.RuneId == "behavior.launch" && edge.ToNode == node.Id && edge.ToPort == SpellGrammar.CHAIN_IN) hasLaunch = true;
                }
            }
            bool hasChain = hasCastShape && hasLaunch;
            bool hasTested = _host.TestExecutionCount > (_isTutorialActive ? _tutorialStartExecutions : 0);
            if (!_isTutorialActive || !hasChain || !hasFire || !hasTested) return;
            _isTutorialActive = false;
            _host.CompleteTutorial();
        }

        /// <summary>보관함 팝업을 열어 마법을 고르거나 새로 만들거나 복제하게 한다.</summary>
        private void OpenLibrary()
        {
            SpellListPopup popup = _ui.OpenPopup<SpellListPopup>();
            popup.Configure(GameData.L("ui.library"),
                () => { popup.Close(); _editor.NewSpell(); },
                () => { popup.Close(); _editor.DuplicateSpell(); });
            foreach (SpellGraph graph in _editor.Library)
            {
                string spellId = graph.Id;
                popup.AddSpell(graph.Name, graph.Id, () => { popup.Close(); _editor.SelectSpell(spellId); });
            }
        }

        /// <summary>프리셋 호출 대상 선택 팝업을 열고 고른 마법 ID를 노드에 저장한다.</summary>
        private void OpenSpellPicker(string nodeId)
        {
            SpellListPopup popup = _ui.OpenPopup<SpellListPopup>();
            popup.Configure(GameData.L("ui.selectSpell"), null, null);
            foreach (SpellGraph graph in _editor.Library)
            {
                string spellId = graph.Id;
                popup.AddSpell(graph.Name, graph.Id, () => { popup.Close(); _editor.SetNodeText(nodeId, "spellId", spellId); });
            }
        }

        /// <summary>이름 변경 팝업을 현재 이름으로 열고 저장하면 마법 이름을 바꾼다.</summary>
        private void OpenRename()
        {
            _ui.OpenPopup<RenamePopup>().Configure(_editor.Graph.Name, name => _editor.Rename(name));
        }

        /// <summary>공유 팝업을 현재 마법의 RC1 코드로 열고 가져오기하면 입력 코드를 현재 마법에 반영한다.</summary>
        private void OpenShare()
        {
            _ui.OpenPopup<SharePopup>().Configure(_editor.Export(), code => _editor.Import(code));
        }
    }
}
