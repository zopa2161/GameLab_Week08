using System;

namespace RuneCode
{
    /// <summary>미션 처치 목표·보스 처치 집계, 전투 종료 판정과 클리어 보상·정산 스크랩 계산을 담당한다.</summary>
    internal sealed class BattleRules
    {
        private readonly SimulationWorld _world;
        private readonly RuneSimulation _simulation;
        private int _targetKills;
        private bool _isBossDefeated;

        /// <summary>처치한 목표 집계 대상 수다.</summary>
        internal int TargetKills => _targetKills;

        /// <summary>이번 미션에서 보스를 처치했는지 나타낸다. 상태 해시용이다.</summary>
        internal bool IsBossDefeated => _isBossDefeated;

        /// <summary>완료 스테이지의 클리어 보상 스크랩이다. 클리어 전이면 0이다.</summary>
        internal int ClearReward => _simulation.Completed && _world.StageDefinition != null ? _world.Stages.Growth.GetClearReward(_world.StageDefinition.ClearReward, _world.StageNumber) : 0;

        /// <summary>월드와 시간·스테이지 판정을 읽을 시뮬레이션 참조로 만든다.</summary>
        internal BattleRules(SimulationWorld world, RuneSimulation simulation)
        {
            _world = world;
            _simulation = simulation;
        }

        /// <summary>미션 시작 시 처치 목표 집계와 보스 처치 판정을 되돌린다.</summary>
        internal void ResetForBattle()
        {
            _targetKills = 0; _isBossDefeated = false;
        }

        /// <summary>목표 처치 대상 집계와 보스 처치 판정을 기록한다.</summary>
        internal void RecordKillProgress(SimulationEnemy enemy)
        {
            if (enemy.IsKillTarget) _targetKills++;
            if (enemy.Boss != null) _isBossDefeated = true;
        }

        /// <summary>
        /// 일반 스테이지는 TimeLimit 도달, 보스 스테이지는 보스 처치로 Cleared, 보스 스테이지 제한시간 초과로 TimedOut을 설정한다.
        /// 클리어가 같은 틱의 시간 초과보다 우선한다. 종료했으면 true를 반환한다.
        /// </summary>
        internal bool TryEndBattle()
        {
            bool hasReachedTimeLimit = _simulation.Time + 0.000001 >= _simulation.TimeLimit;
            if (_simulation.IsBossStage ? _isBossDefeated : hasReachedTimeLimit) _world.Stage = MissionStage.Cleared;
            else if (_simulation.IsBossStage && hasReachedTimeLimit) _world.Stage = MissionStage.TimedOut;
            else return false;
            return true;
        }

        /// <summary>회수한 스크랩 collectedFragments로 정산 스크랩을 반환한다. 실패는 보존 비율로 반올림하고, 그 외에는 클리어 보상을 더한다.</summary>
        internal int GetSettlementFragments(int collectedFragments)
            => _simulation.IsFailed ? (int)Math.Round(collectedFragments * _world.Balance.Economy.DeathRetention, MidpointRounding.AwayFromZero) : collectedFragments + ClearReward;
    }
}
