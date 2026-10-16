using System;
using System.Text;

namespace RuneCode
{
    /// <summary>
    /// 공전 모드의 이동 동작이다. 개체를 옮기는 행동(발사·잔류)은 생성 위치에서 궤도 자리까지 날아가 진입한 뒤
    /// 행동 기준점(직접 시전은 시전자의 현재 위치, 이벤트 생성은 이벤트 위치) 주위를 반시계로 돈다. 궤도·진입 중에는 벽을 무시한다.
    /// 궤도 자리는 조준 방향에서 시작하고 다중이면 개체 순번(entity.Angle)만큼 등각으로 나뉘며, 다른 모드가 이동을 맡는 동안에도 계속 돈다.
    /// 경로형(빔)은 시전 위치를 고정 중심으로 궤도 반경의 원을 반시계로 그린다.
    /// </summary>
    internal sealed class OrbitModifierRuntime : SpellModifierRuntime
    {
        private const double DEGREES_TO_RADIANS = Math.PI / 180;

        public override string RuneId => OrbitModifierRules.RUNE_ID;
        public override bool ReplacesBasePath => true;

        /// <summary>
        /// 시작 궤도 각도(조준 방향 + 개체 순번 각도), 회전 속도, 진입 속도와 궤도 중심을 정한 공전 상태를 만든다.
        /// 회전 속도는 이동형(발사)만 속도 배율을 받고, 진입 속도는 이동형이면 행동 속도, 아니면 모드 수치 _entrySpeed다.
        /// </summary>
        public override object CreateState(SimulationSpellEntity entity)
        {
            RuneStats data = GameData.Runes.Get(OrbitModifierRules.RUNE_ID).Stats;
            bool isMoving = entity.Action.Rules != null && entity.Action.Rules.MotionKind == SpellMotionKind.Moving;
            double angularSpeed = data.AngularSpeed * DEGREES_TO_RADIANS * (isMoving ? entity.Stats.SpeedMultiplier : 1);
            double entrySpeed = isMoving ? entity.Stats.Speed : data.EntrySpeed;
            double startAngle = Math.Atan2(entity.Direction.Y, entity.Direction.X) + entity.Angle;
            return new OrbitState(startAngle, angularSpeed, entrySpeed, !entity.FromEvent, entity.Anchor);
        }

        /// <summary>
        /// 궤도 자리를 한 틱만큼 돌리고, 진입을 마쳤으면 궤도 자리로, 아니면 궤도 자리 쪽으로 옮긴 뒤 true를 반환한다.
        /// 경로형 행동은 개체를 옮기지 않으므로 false를 반환한다.
        /// </summary>
        public override bool TryMove(RuneSimulation simulation, SimulationSpellEntity entity, object state)
        {
            SpellBehaviorRules rules = entity.Action.Rules;
            if (rules == null || rules.MotionKind == SpellMotionKind.Path) return false;
            var orbit = (OrbitState)state;
            orbit.Rotate();
            double radius = entity.Stats.OrbitRadius;
            SimVector radial = new SimVector(Math.Cos(orbit.Angle), Math.Sin(orbit.Angle));
            SimVector slot = orbit.GetCenter(entity) + radial * radius;
            SimVector tangent = new SimVector(-radial.Y, radial.X);
            entity.MarkPassingWalls();
            if (orbit.IsEntered)
            {
                entity.Move(slot, tangent);
                return true;
            }
            // 궤도 자리도 계속 돌므로 궤도 자리 이동 속도를 더한 속도로 따라가 반드시 따라잡게 한다.
            double step = (orbit.EntrySpeed + Math.Abs(orbit.AngularSpeed) * radius) * RuneSimulation.STEP_SECONDS;
            SimVector toSlot = slot - entity.Position;
            if (toSlot.Length <= step)
            {
                entity.Move(slot, tangent);
                orbit.Enter();
                return true;
            }
            SimVector heading = toSlot.Normalized();
            entity.Move(entity.Position + heading * step, heading);
            return true;
        }

        /// <summary>다른 이동 모드가 이동을 맡은 틱에는 궤도 자리만 돌리고, 개체가 궤도를 떠났으므로 진입 전 상태로 바꾼다.</summary>
        public override void UpdatePassive(RuneSimulation simulation, SimulationSpellEntity entity, object state)
        {
            var orbit = (OrbitState)state;
            orbit.Rotate();
            orbit.Leave();
        }

        /// <summary>궤도 진입을 마친 뒤에만 반응 모드(유도)에 이동을 넘긴다.</summary>
        public override bool CanYieldMove(SimulationSpellEntity entity, object state) => ((OrbitState)state).IsEntered;

        /// <summary>빔 경로의 시작 위치를 시전 위치 중심의 궤도 위에 두고 진행 방향을 반시계 접선으로 정한다. 반경이 없으면 유지한다.</summary>
        public override void BeginPath(RuneSimulation simulation, SimulationSpellEntity entity, object state)
        {
            double radius = entity.Stats.OrbitRadius;
            if (radius <= 0) return;
            double angle = ((OrbitState)state).Angle;
            SimVector radial = new SimVector(Math.Cos(angle), Math.Sin(angle));
            entity.Move(entity.Anchor + radial * radius, new SimVector(-radial.Y, radial.X));
        }

        /// <summary>시전 위치를 중심으로 현재 점에서 반시계 방향의 다음 궤도 점을 향하게 한다. step은 현 길이이며 반경이 없으면 맡지 않는다.</summary>
        public override bool SteerPathStep(RuneSimulation simulation, SimulationSpellEntity entity, object state,
            SimVector point, ref SimVector direction, double step, double pathSpeed, double halfWidth)
        {
            double radius = entity.Stats.OrbitRadius;
            if (radius <= 0) return false;
            SimVector radial = (point - entity.Anchor).Normalized();
            double angle = 2 * Math.Asin(Math.Min(1, step / (2 * radius)));
            SimVector next = entity.Anchor + radial.Rotated(angle) * radius;
            direction = (next - point).Normalized();
            return true;
        }

        /// <summary>궤도 각도와 진입 여부를 결정성 해시에 기록한다.</summary>
        public override void WriteState(SimulationSpellEntity entity, object state, StringBuilder buffer)
        {
            var orbit = (OrbitState)state;
            buffer.Append(FormattableString.Invariant($"|orbit:{orbit.Angle:R}:{orbit.IsEntered}"));
        }

        /// <summary>개체 하나의 궤도 각도, 회전·진입 속도, 궤도 중심(시전자 추종 또는 고정 위치)과 진입 여부다.</summary>
        private sealed class OrbitState
        {
            private readonly double _angularSpeed;
            private readonly double _entrySpeed;
            private readonly bool _isFollowingCaster;
            private readonly SimVector _fixedCenter;
            private double _angle;
            private bool _isEntered;

            /// <summary>현재 궤도 자리 각도(라디안)다.</summary>
            public double Angle => _angle;

            /// <summary>초당 회전 각도(라디안)다.</summary>
            public double AngularSpeed => _angularSpeed;

            /// <summary>궤도 자리로 날아갈 때의 기본 속도(px/s)다.</summary>
            public double EntrySpeed => _entrySpeed;

            /// <summary>궤도 진입을 마치고 궤도 위에 있는지 나타낸다.</summary>
            public bool IsEntered => _isEntered;

            /// <summary>시작 각도, 회전·진입 속도와 궤도 중심 방식을 정한다. 시전자를 따르지 않으면 fixedCenter를 중심으로 쓴다.</summary>
            public OrbitState(double angle, double angularSpeed, double entrySpeed, bool isFollowingCaster, SimVector fixedCenter)
            {
                _angle = angle;
                _angularSpeed = angularSpeed;
                _entrySpeed = entrySpeed;
                _isFollowingCaster = isFollowingCaster;
                _fixedCenter = fixedCenter;
            }

            /// <summary>궤도 중심을 반환한다. 시전자를 따르면 시전자의 현재 위치, 아니면 고정 위치다.</summary>
            public SimVector GetCenter(SimulationSpellEntity entity) => _isFollowingCaster ? entity.Caster.Position : _fixedCenter;

            /// <summary>궤도 자리를 한 틱만큼 반시계로 돌린다.</summary>
            public void Rotate() => _angle += _angularSpeed * RuneSimulation.STEP_SECONDS;

            /// <summary>궤도 진입을 마친 상태로 바꾼다.</summary>
            public void Enter() => _isEntered = true;

            /// <summary>개체가 궤도를 떠난 상태로 바꾼다. 다시 궤도 자리로 날아가 진입해야 한다.</summary>
            public void Leave() => _isEntered = false;
        }
    }
}
