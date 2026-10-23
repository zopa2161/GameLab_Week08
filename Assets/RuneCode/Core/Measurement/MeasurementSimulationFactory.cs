using System;

namespace RuneCode
{
    /// <summary>측정 프로필과 조건을 측정 시나리오 이름과 시험 도크 시뮬레이션 생성으로 바꾼다.</summary>
    public static class MeasurementSimulationFactory
    {
        /// <summary>측정 시뮬레이션이 쓰는 해금 속성 목록이다. SimulationCli 자동 실행과 같은 구성을 유지한다.</summary>
        private static readonly string[] UNLOCKED_ELEMENTS = { "raw", "fire", "ice", "arc" };

        /// <summary>측정 상황 코드(MeasurementCodes)를 시험 시나리오 이름으로 바꿔 반환한다. 정의되지 않은 상황이면 ArgumentException을 던진다.</summary>
        public static string GetScenario(string situation)
        {
            if (situation == MeasurementCodes.SITUATION_SINGLE) return BenchScenario.MEASURE_SINGLE;
            if (situation == MeasurementCodes.SITUATION_SWARM) return BenchScenario.MEASURE_SWARM;
            if (situation == MeasurementCodes.SITUATION_PENTAGON) return BenchScenario.MEASURE_PENTAGON;
            throw new ArgumentException("알 수 없는 측정 상황입니다.", nameof(situation));
        }

        /// <summary>
        /// 측정 프로필과 조건, 시드로 측정용 시뮬레이션을 만들어 반환한다. 측정 시나리오 적 배치, 적응 비활성화, 무적 설정과 해금 속성을 반영한다.
        /// 마법 장착(SetLoadout)과 틱 실행은 하지 않는다. 측정 실행기가 별도로 처리한다.
        /// StartEnergyRatio가 1이 아니면 ArgumentException을 던진다. 시뮬레이션 생성 경로가 플레이어를 항상 가득 찬 전력으로 시작시켜 현재는 가득 찬 시작만 지원한다.
        /// InputMode가 MeasurementCodes.IsInputMode를 통과하지 못해도 ArgumentException을 던진다.
        /// </summary>
        public static RuneSimulation Create(MeasurementPlayerProfile profile, MeasurementCondition condition, int seed)
        {
            if (profile.StartEnergyRatio != 1f) throw new ArgumentException("현재는 가득 찬 전력 시작만 지원합니다.", nameof(profile));
            if (!MeasurementCodes.IsInputMode(condition.InputMode)) throw new ArgumentException("알 수 없는 측정 입력 방식입니다.", nameof(condition));
            RuneSimulation simulation = new RuneSimulation(SimulationSetup.FromGameData(seed, isMission: false, maxHp: GameData.Balance.Player.MaxHp,
                maxEnergy: profile.MaxEnergy, stageNumber: 1, energyRegen: profile.EnergyRegen, damageMultiplier: profile.DamageMultiplier));
            simulation.ResetBench(GetScenario(condition.Situation), 0, condition.Distance);
            simulation.SetAdaptationEnabled(false);
            simulation.SetDebugInvulnerable(profile.IsInvulnerable);
            simulation.SetUnlockedElements(UNLOCKED_ELEMENTS);
            return simulation;
        }
    }
}
