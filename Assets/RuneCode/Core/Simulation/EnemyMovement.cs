using System;

namespace RuneCode
{
    internal enum EnemyMovementType { Chase, TurnChase, Turret, Anchor, DashChase, KeepDistance, Sniper, Interceptor }

    internal enum EnemyMoveMode { Stay, Face, Move }

    internal readonly struct EnemyMoveContext
    {
        private readonly SimVector _offset;
        private readonly SimVector _direction;
        private readonly double _time;
        private readonly bool _isMission;
        private readonly double _advanceSpeed;
        private readonly double _preferredDistance;
        private readonly double _speedMultiplier;
        public SimVector Offset => _offset;
        public SimVector Direction => _direction;
        public double Time => _time;
        public bool IsMission => _isMission;
        public double AdvanceSpeed => _advanceSpeed;
        public double PreferredDistance => _preferredDistance;
        public double SpeedMultiplier => _speedMultiplier;

        /// <summary>
        /// 이동 전 플레이어까지의 거리 벡터와 방향, 현재 시각, 미션 여부, 미션 전진 속도, 보스 유지 거리 및 보스 속도 배율을 보관한다.
        /// </summary>
        public EnemyMoveContext(SimVector offset, SimVector direction, double time, bool isMission, double advanceSpeed, double preferredDistance, double speedMultiplier)
        { _offset = offset; _direction = direction; _time = time; _isMission = isMission; _advanceSpeed = advanceSpeed; _preferredDistance = preferredDistance; _speedMultiplier = speedMultiplier; }
    }

    internal readonly struct EnemyMoveDecision
    {
        private readonly EnemyMoveMode _mode;
        private readonly SimVector _direction;
        private readonly double _speed;
        public EnemyMoveMode Mode => _mode;
        public SimVector Direction => _direction;
        public double Speed => _speed;
        public static EnemyMoveDecision Stay => new EnemyMoveDecision(EnemyMoveMode.Stay, SimVector.Zero, 0);

        /// <summary>이동 방식, 방향 및 감속 전 속도로 이동 판단 결과를 생성한다.</summary>
        private EnemyMoveDecision(EnemyMoveMode mode, SimVector direction, double speed) { _mode = mode; _direction = direction; _speed = speed; }

        /// <summary>제자리에서 지정 방향을 바라보는 판단 결과를 반환한다.</summary>
        public static EnemyMoveDecision Face(SimVector direction) => new EnemyMoveDecision(EnemyMoveMode.Face, direction, 0);

        /// <summary>지정 방향으로 감속 전 속도만큼 이동하는 판단 결과를 반환한다.</summary>
        public static EnemyMoveDecision Move(SimVector direction, double speed) => new EnemyMoveDecision(EnemyMoveMode.Move, direction, speed);
    }

    internal abstract class EnemyMovement
    {
        /// <summary>적의 현재 상태와 이동 전 판단 값으로 이번 틱의 이동 방식, 방향 및 속도를 반환한다. 적 상태는 변경하지 않는다.</summary>
        public abstract EnemyMoveDecision Decide(SimulationEnemy enemy, in EnemyMoveContext context);
    }

    internal sealed class ChaseMovement : EnemyMovement
    {
        /// <summary>플레이어 방향으로 적의 기본 이동 속도만큼 이동하는 판단을 반환한다.</summary>
        public override EnemyMoveDecision Decide(SimulationEnemy enemy, in EnemyMoveContext context)
            => EnemyMoveDecision.Move(context.Direction, enemy.Definition.Speed);
    }

    internal sealed class TurnChaseMovement : EnemyMovement
    {
        /// <summary>플레이어 방향으로 적의 기본 이동 속도만큼 이동하는 판단을 반환한다. 방향 전환 지연은 Move에서 정의의 방향 전환 시간으로 처리한다.</summary>
        public override EnemyMoveDecision Decide(SimulationEnemy enemy, in EnemyMoveContext context)
            => EnemyMoveDecision.Move(context.Direction, enemy.Definition.Speed);
    }

    internal sealed class StationaryAdvanceMovement : EnemyMovement
    {
        private readonly bool _isFacingInBench;

        /// <summary>시험 도크에서 방향만 바꿀지(true) 아무것도 하지 않을지(false)를 지정해 고정형 이동을 생성한다.</summary>
        public StationaryAdvanceMovement(bool isFacingInBench) { _isFacingInBench = isFacingInBench; }

        /// <summary>미션에서는 공용 전진 속도로 플레이어에게 다가가고, 시험 도크에서는 설정에 따라 방향만 바꾸거나 정지하는 판단을 반환한다.</summary>
        public override EnemyMoveDecision Decide(SimulationEnemy enemy, in EnemyMoveContext context)
        {
            if (context.IsMission) return EnemyMoveDecision.Move(context.Direction, context.AdvanceSpeed);
            return _isFacingInBench ? EnemyMoveDecision.Face(context.Direction) : EnemyMoveDecision.Stay;
        }
    }

    internal sealed class DashChaseMovement : EnemyMovement
    {
        /// <summary>
        /// 돌진 중이면 고정 방향 돌진, 예고 중이거나 이번 틱에 예고를 시작할 조건이면 정지, 그 외에는 추적하는 판단을 반환한다.
        /// 예고 시작 조건은 시뮬레이션의 공격 갱신과 같은 이동 전 거리로 판정한다.
        /// </summary>
        public override EnemyMoveDecision Decide(SimulationEnemy enemy, in EnemyMoveContext context)
        {
            EnemyDefinition definition = enemy.Definition;
            if (context.Time < enemy.DashUntil) return EnemyMoveDecision.Move(enemy.AttackDirection, definition.DashSpeed);
            if (enemy.WarningUntil > 0) return EnemyMoveDecision.Stay;
            if (context.Offset.Length < definition.AttackRange && context.Time >= enemy.AttackAt) return EnemyMoveDecision.Stay;
            return EnemyMoveDecision.Move(context.Direction, definition.Speed);
        }
    }

    internal sealed class KeepDistanceMovement : EnemyMovement
    {
        /// <summary>유지 거리보다 멀면 속도 배율을 적용해 추적하고, 가까우면 방향만 바꾸는 판단을 반환한다.</summary>
        public override EnemyMoveDecision Decide(SimulationEnemy enemy, in EnemyMoveContext context)
        {
            if (context.Offset.Length > context.PreferredDistance) return EnemyMoveDecision.Move(context.Direction, enemy.Definition.Speed * context.SpeedMultiplier);
            return EnemyMoveDecision.Face(context.Direction);
        }
    }

    internal static class EnemyMovements
    {
        // EnemyMovementType 값 순서와 같은 순서로 둔다.
        private static readonly EnemyMovement[] _movements =
        {
            new ChaseMovement(),
            new TurnChaseMovement(),
            new StationaryAdvanceMovement(true),
            new StationaryAdvanceMovement(false),
            new DashChaseMovement(),
            new KeepDistanceMovement(),
            new SniperMovement(),
            new InterceptorMovement()
        };

        /// <summary>적 설정의 이동 방식 이름에 대응하는 이동 종류를 반환하며 모르는 이름에는 데이터 오류를 발생시킨다.</summary>
        public static EnemyMovementType Resolve(string movement)
        {
            switch (movement)
            {
                case EnemyDefinition.MOVEMENT_SNIPER: return EnemyMovementType.Sniper;
                case EnemyDefinition.MOVEMENT_INTERCEPTOR: return EnemyMovementType.Interceptor;
                case EnemyDefinition.MOVEMENT_TURRET: return EnemyMovementType.Turret;
                case EnemyDefinition.MOVEMENT_ANCHOR: return EnemyMovementType.Anchor;
                case EnemyDefinition.MOVEMENT_DASH_CHASE: return EnemyMovementType.DashChase;
                case EnemyDefinition.MOVEMENT_KEEP_DISTANCE: return EnemyMovementType.KeepDistance;
                case EnemyDefinition.MOVEMENT_CHASE: return EnemyMovementType.Chase;
                case EnemyDefinition.MOVEMENT_TURN_CHASE: return EnemyMovementType.TurnChase;
                default: throw new FormatException("알 수 없는 적 이동 방식입니다: " + movement);
            }
        }

        /// <summary>적의 이동 종류에 해당하는 공유 이동 판단으로 이번 틱의 이동 결과를 계산해 반환한다.</summary>
        public static EnemyMoveDecision Decide(SimulationEnemy enemy, in EnemyMoveContext context)
            => _movements[(int)enemy.MovementType].Decide(enemy, context);
    }

    internal sealed class SniperMovement : EnemyMovement
    {
        /// <summary>조준·발사 대기 중에는 정지하고 그 외에는 미션 접근 속도로 이동한다. 도크에서는 방향만 바꾼다.</summary>
        public override EnemyMoveDecision Decide(SimulationEnemy enemy, in EnemyMoveContext context)
        {
            if (enemy.IsAiming || context.Time + 0.000001 >= enemy.AttackAt) return EnemyMoveDecision.Stay;
            return context.IsMission ? EnemyMoveDecision.Move(context.Direction, context.AdvanceSpeed) : EnemyMoveDecision.Face(context.Direction);
        }
    }

    internal sealed class InterceptorMovement : EnemyMovement
    {
        /// <summary>정지 주기에는 쉬고 이동 주기에는 유지 거리 보정과 접선 방향을 합쳐 플레이어 주변을 돈다.</summary>
        public override EnemyMoveDecision Decide(SimulationEnemy enemy, in EnemyMoveContext context)
        {
            if (enemy.IsStopping) return EnemyMoveDecision.Face(context.Direction);
            SimVector tangent = new SimVector(-context.Direction.Y, context.Direction.X) * enemy.OrbitSign;
            double correction = Math.Max(-1, Math.Min(1, (context.Offset.Length - enemy.Definition.PreferredDistance) / enemy.Definition.PreferredDistance));
            return EnemyMoveDecision.Move((tangent + context.Direction * correction).Normalized(), enemy.Definition.Speed);
        }
    }
}
