using System;

namespace RuneCode
{
    /// <summary>Hexa의 고유 식별자와 고정형 보스 정의의 필수 조건을 관리한다.</summary>
    public static class HexaDefinition
    {
        public const string ID = "hexa";
        public const string PROJECTILE_SOURCE_ID = "enemy.sentry";
        public const int INITIAL_DELAY_TICKS = 3 * RuneSimulation.TICK_RATE;
        public const int SPRAY_INTERVAL_TICKS = RuneSimulation.TICK_RATE / 10;
        public const int SPRAY_DURATION_TICKS = 3 * RuneSimulation.TICK_RATE;
        public const int SPRAY_SHOT_COUNT = 30;
        public const double SPRAY_HALF_SPREAD_DEGREES = 15;
        public const int HOMING_SHOT_INTERVAL_TICKS = RuneSimulation.TICK_RATE;
        public const int HOMING_SHOT_COUNT = 5;
        public const double HOMING_TURN_DEGREES_PER_SECOND = 20;
        public const double HOMING_SECONDS = 2;

        /// <summary>적 정의가 Hexa의 ID와 고정형 접촉 공격 규칙을 만족하는지 검증한다.</summary>
        internal static void Validate(EnemyDefinition enemy)
        {
            if (enemy.Id != ID || enemy.Speed != 0 || enemy.Movement != EnemyDefinition.MOVEMENT_ANCHOR
                || enemy.Attack != EnemyDefinition.ATTACK_CONTACT || enemy.CanSplit)
                throw new FormatException("Hexa는 hexa ID의 고정형 접촉 공격 보스여야 합니다: " + enemy.Id);
        }
    }
}
