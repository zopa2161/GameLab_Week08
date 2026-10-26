using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;
using UnityEngine.UI;

namespace RuneCode
{
    /// <summary>
    /// 런 종료 결과 팝업 View다. 마지막 빌드와 이번 런의 전투 기록을 표시하고 구석의 타이틀 복귀 버튼 클릭을 알린다.
    /// Esc로 닫히지 않으며 MissionPresenter가 연다.
    /// </summary>
    public sealed class RunResultPopup : UiPopup
    {
        [Header("내용")]
        [SerializeField] private TextMeshProUGUI _titleLabel;
        [SerializeField] private TextMeshProUGUI _stageLabel;
        [SerializeField] private TextMeshProUGUI _playTimeLabel;
        [SerializeField] private TextMeshProUGUI _killLabel;

        [Header("마지막 빌드")]
        [SerializeField] private RuneGraphPreview _buildPreview;
        [SerializeField] private TextMeshProUGUI _buildNameLabel;
        [SerializeField] private TextMeshProUGUI _buildMetaLabel;

        [Header("전투 기록")]
        [SerializeField] private TextMeshProUGUI _damageLabel;
        [SerializeField] private TextMeshProUGUI _damageTakenLabel;
        [SerializeField] private TextMeshProUGUI _shieldLabel;
        [SerializeField] private TextMeshProUGUI _energyLabel;
        [SerializeField] private TextMeshProUGUI _dpsLabel;
        [SerializeField] private TextMeshProUGUI _dashLabel;

        [Header("버튼")]
        [SerializeField] private Button _titleButton;
        [SerializeField] private Button _resetViewButton;

        private bool _isBound;

        /// <summary>타이틀로 돌아가기 버튼을 눌렀을 때 알린다.</summary>
        public event Action TitleClicked;

        /// <summary>종료한 런의 보관 빌드와 실제 전투 누계를 표시하고 복귀·보기 초기화 버튼을 한 번 연결한다.</summary>
        public void Configure(SpellGraph graph, IReadOnlyList<SpellGraph> library, CompiledSpell spell, RuneSimulation simulation)
        {
            if (!_isBound)
            {
                _titleButton.onClick.AddListener(() => TitleClicked?.Invoke());
                _resetViewButton.onClick.AddListener(_buildPreview.ResetView);
                _isBound = true;
            }
            int elapsedSeconds = (int)simulation.Time;
            _titleLabel.text = simulation.Completed ? "런 클리어" : simulation.IsTimedOut ? "시간 초과" : "런 종료";
            _stageLabel.text = "도달 스테이지 " + simulation.StageNumber + "  ·  라운드 " + simulation.RoundNumber;
            _playTimeLabel.text = "전투 시간  " + (elapsedSeconds / 60).ToString("00") + ":" + (elapsedSeconds % 60).ToString("00");
            _killLabel.text = simulation.KillCount.ToString("N0");
            _damageLabel.text = simulation.TotalDamage.ToString("N0");
            _damageTakenLabel.text = simulation.Player.DamageReceived.ToString("N0");
            _shieldLabel.text = simulation.Player.ShieldAbsorbed.ToString("N0");
            _energyLabel.text = simulation.EnergySpent.ToString("N0");
            _dpsLabel.text = (simulation.Time > 0 ? simulation.TotalDamage / simulation.Time : 0).ToString("N1");
            _dashLabel.text = simulation.Player.DashesStarted.ToString("N0");
            _buildNameLabel.text = graph.Name;
            _buildMetaLabel.text = "노드 " + graph.Nodes.Count + "  ·  연결 " + graph.Edges.Count;
            _buildPreview.SetContent(graph, library, spell, _titleLabel.font);
        }

        /// <summary>Esc 입력을 소비해 팝업이 닫히지 않게 한다. 팝업은 타이틀 복귀 버튼으로만 닫는다.</summary>
        public override bool HandleEscape() => true;
    }
}
