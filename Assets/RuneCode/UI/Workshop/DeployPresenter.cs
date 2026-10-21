using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>출격 탭 Presenter다. 해금 범위 안의 스테이지 선택과 출격 준비 검증 후 미션 화면 전환을 처리한다.</summary>
    public sealed class DeployPresenter
    {
        private readonly RuneCodeSession _session;
        private readonly DeployPanel _view;
        private readonly HashSet<string> _previewEnemyIds = new HashSet<string>();

        /// <summary>세션과 출격 View를 받아 View 이벤트를 연결한다.</summary>
        public DeployPresenter(RuneCodeSession session, DeployPanel view)
        {
            _session = session;
            _view = view;
            _view.Bind();
            _view.StageStepClicked += ChangeStage;
            _view.LaunchClicked += Launch;
        }

    /// <summary>해금 최고 단계, 선택 스테이지, 제한시간과 출현 적을 다시 표시한다.</summary>
        public void Refresh()
        {
            _view.SetContent(
                GameData.L("ui.highestStage") + "  " + _session.HighestClearedStage,
                GameData.L("ui.stage") + " " + _session.SelectedStage,
                GameData.L("ui.duration") + "  " + _session.BattleDuration.ToString("0") + "s");
            RefreshEnemyPreview();
        }

        /// <summary>선택 스테이지의 일반 출현·웨이브·엘리트와 캐리어 생성 적을 모아 적 카드와 보스 강조를 갱신한다.</summary>
        private void RefreshEnemyPreview()
        {
            StageDefinition stage = _session.SelectedStageDefinition;
            _previewEnemyIds.Clear();
            foreach (StreamCandidate candidate in stage.Stream.Candidates)
                if (candidate.FirstStage <= _session.SelectedStage && candidate.Weight > 0) AddPreviewEnemy(candidate.EnemyId);
            foreach (WaveDefinition wave in stage.Waves)
                foreach (WaveUnit unit in wave.Units)
                    if (unit.Count > 0) AddPreviewEnemy(unit.EnemyId);
            if (stage.EliteEvery > 0)
                foreach (EliteSpawnDefinition elite in stage.EliteTypes) AddPreviewEnemy(elite.EnemyId);
            _view.SetEnemyPreview(_session.SelectedStage, stage.IsBoss, _previewEnemyIds);
        }

        /// <summary>적 ID를 중복 없이 추가하고 캐리어 생성 적을 포함한다. 분열 후 생기는 개체는 추가하지 않는다.</summary>
        private void AddPreviewEnemy(string enemyId)
        {
            if (!_previewEnemyIds.Add(enemyId)) return;
            EnemyDefinition enemy = GameData.Enemies.Get(enemyId);
            if (enemy.HasTrait(EnemyDefinition.TRAIT_CARRIER))
                foreach (string childId in GameData.Carrier.EnemyIds) AddPreviewEnemy(childId);
        }

        /// <summary>해금 범위 안에서 선택 스테이지를 이동하고 표시를 갱신한다.</summary>
        private void ChangeStage(int direction)
        {
            _session.SelectStage(_session.SelectedStage + direction);
            Refresh();
        }

        /// <summary>마법을 저장·검증해 준비가 되면 미션 화면 전환을 요청한다. 보관 중인 런이 있으면 미션 화면이 그 런을 이어 간다.</summary>
        public void Launch()
        {
            LocalTelemetry.RecordUsabilityEvent("simulation_request", "workshop", new Dictionary<string, object>
            {
                ["input_method"] = "deploy_button",
                ["stage"] = _session.SelectedStage,
            });
            if (_session.TryPrepareMission(out _)) _session.RequestScreen(AppScreen.Mission);
            else
                LocalTelemetry.RecordUsabilityEvent("simulation_cast_result", "workshop", new Dictionary<string, object>
                {
                    ["result"] = "rejected",
                    ["reason"] = "spell_not_ready",
                    ["stage"] = _session.SelectedStage,
                });
        }
    }
}
