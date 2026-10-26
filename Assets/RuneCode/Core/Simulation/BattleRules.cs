using System;

namespace RuneCode
{
    /// <summary>미션 처치 목표·보스 처치 집계, 웨이브 종료 판정과 정산 스크랩 계산을 담당한다.</summary>
    internal sealed class BattleRules
    {
        private readonly SimulationWorld _world;
        private int _targetKills;
        private bool _isBossDefeated;

        /// <summary>처치한 목표 집계 대상 수다.</summary>
        internal int TargetKills => _targetKills;

        /// <summary>이번 미션에서 보스를 처치했는지 나타낸다. 상태 해시용이다.</summary>
        internal bool IsBossDefeated => _isBossDefeated;

        /// <summary>시간·스테이지 판정을 읽을 월드로 만든다.</summary>
        internal BattleRules(SimulationWorld world)
        {
            _world = world;
        }

        /// <summary>미션 시작 시 처치 목표 집계와 보스 처치 판정을 되돌린다.</summary>
        internal void ResetForBattle()
        {
            _targetKills = 0; _isBossDefeated = false;
        }

        /// <summary>새 웨이브의 보스 처치 판정만 초기화하고 미션 전체 처치 수는 유지한다.</summary>
        internal void ResetForWave() => _isBossDefeated = false;

        /// <summary>목표 처치 대상 집계와 보스 처치 판정을 기록한다.</summary>
        internal void RecordKillProgress(SimulationEnemy enemy)
        {
            if (enemy.IsKillTarget) _targetKills++;
            if (enemy.IsBoss) _isBossDefeated = true;
        }

        /// <summary>보스 웨이브는 보스 처치로, 일반 웨이브는 모든 배출·예약·적 소멸로 종료 가능한지 반환한다.</summary>
        internal bool IsProgressionWaveFinished(bool hasFinishedSpawning)
            => _world.IsBossStage ? _isBossDefeated : hasFinishedSpawning && _world.Enemies.Count == 0;

        /// <summary>회수한 스크랩 collectedFragments로 정산 스크랩을 반환한다. 실패는 보존 비율로 반올림하고, 그 외에는 그대로 반환한다.</summary>
        internal int GetSettlementFragments(int collectedFragments)
            => _world.IsFailed ? (int)Math.Round(collectedFragments * _world.Balance.Economy.DeathRetention, MidpointRounding.AwayFromZero) : collectedFragments;
    }
}
