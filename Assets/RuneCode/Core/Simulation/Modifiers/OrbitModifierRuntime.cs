using System;
using System.Text;

namespace RuneCode
{
    /// <summary>
    /// 공전 모드의 이동 동작이다. 개체를 옮기는 행동(발사·잔류)은 생성 위치에서 궤도 자리까지 날아가 진입한 뒤
    /// 행동 기준점(직접 시전은 시전자의 현재 위치, 이벤트 생성은 이벤트 위치) 주위를 반시계로 돈다. 궤도·진입 중에는 벽을 무시한다.
    /// 궤도 자리는 조준 방향에서 시작하고 다중이면 개체 순번(entity.Angle)만큼 등각으로 나뉘며, 다른 모드가 이동을 맡는 동안에도 계속 돈다.
    /// 경로형(빔)은 직접 시전이면 시전자를 따라가고 이벤트 생성이면 이벤트 위치를 중심으로 원을 그린다.
    /// </summary>
    internal sealed class OrbitModifierRuntime : SpellModifierRuntime
    {
        private const double DEGREES_TO_RADIANS = Math.PI / 180;
        private const double FULL_TURN_DEGREES = 360;

        public override string RuneId => OrbitModifierRules.RUNE_ID;
        public override bool ReplacesBasePath => true;

        /// <summary>
        /// 시작 궤도 각도(조준 방향 + 개체 순번 각도), 회전 속도, 진입 속도와 궤도 중심을 정한 공전 상태를 만든다.
        /// 진입 중에는 설정 각속도를 사용하고 최초 궤도 진입 시 남은 기본 수명으로 한 바퀴의 각속도를 정한다.
        /// 진입 속도는 이동형이면 행동 속도, 아니면 모드 수치 _entrySpeed다.
        /// </summary>
        public override object CreateState(SimulationSpellEntity entity)
        {
            RuneStats data = GameData.Runes.Get(OrbitModifierRules.RUNE_ID).Stats;
            SpellBehaviorRules rules = entity.Action.Rules;
            bool isMoving = rules != null && rules.MotionKind == SpellMotionKind.Moving;
            double baseAngularSpeed = isMoving || data.StationaryAngularSpeed <= 0f ? data.AngularSpeed : data.StationaryAngularSpeed;
            double angularSpeed = baseAngularSpeed * DEGREES_TO_RADIANS * (isMoving ? entity.Stats.SpeedMultiplier : 1);
            double entrySpeed = isMoving ? entity.Stats.Speed : data.EntrySpeed;
            double startAngle = Math.Atan2(entity.Direction.Y, entity.Direction.X) + entity.Angle;
            return new OrbitState(startAngle, angularSpeed, entrySpeed);
        }

        /// <summary>
        /// 실제 최초 진입 완료 시점부터 남은 기본 행동·공전 수명으로 한 바퀴의 각속도(라디안/초)를 반환한다.
        /// 이동형의 속도 배율은 각속도에 적용하고 지속시간 배율은 기존 수명에만 적용되도록 기본 수명을 기준으로 쓴다.
        /// </summary>
        private static double GetEnteredAngularSpeed(SimulationSpellEntity entity)
        {
            SpellBehaviorRules rules = entity.Action.Rules;
            RuneStats orbitStats = GameData.Runes.Get(OrbitModifierRules.RUNE_ID).Stats;
            RuneStats form = GameData.Runes.Get(rules.StatsRuneId).Stats;
            bool isMoving = rules.MotionKind == SpellMotionKind.Moving;
            double lifetime = isMoving ? form.Lifetime * orbitStats.MovingLifetimeBonus : form.Lifetime + form.WarnSeconds;
            double orbitSeconds = Math.Max(RuneSimulation.STEP_SECONDS, lifetime - entity.Age - RuneSimulation.STEP_SECONDS);
            double speedMultiplier = isMoving ? entity.Stats.SpeedMultiplier : 1;
            return FULL_TURN_DEGREES * DEGREES_TO_RADIANS / orbitSeconds * speedMultiplier;
        }

        /// <summary>
        /// 궤도 자리를 한 틱만큼 돌리고, 진입을 마쳤으면 궤도 자리로, 아니면 궤도 자리 쪽으로 옮긴 뒤 true를 반환한다.
        /// 경로형 행동은 개체를 옮기지 않으므로 false를 반환한다.
        /// </summary>
        public override bool TryMove(SpellRuntimeContext context, SimulationSpellEntity entity, object state)
        {
            SpellBehaviorRules rules = entity.Action.Rules;
            if (rules == null || rules.MotionKind == SpellMotionKind.Path) return false;
            var orbit = (OrbitState)state;
            orbit.Rotate();
            double radius = entity.Stats.OrbitRadius;
            SimVector radial = new SimVector(Math.Cos(orbit.Angle), Math.Sin(orbit.Angle));
            SimVector slot = GetPoint(entity, radial);
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
                orbit.Enter(entity);
                return true;
            }
            SimVector heading = toSlot.Normalized();
            entity.Move(entity.Position + heading * step, heading);
            return true;
        }

        /// <summary>다른 이동 모드가 이동을 맡은 틱에는 궤도 자리만 돌리고, 개체가 궤도를 떠났으므로 진입 전 상태로 바꾼다.</summary>
        public override void UpdatePassive(SpellRuntimeContext context, SimulationSpellEntity entity, object state)
        {
            var orbit = (OrbitState)state;
            orbit.Rotate();
            orbit.Leave();
        }

        /// <summary>궤도 진입을 마친 뒤에만 반응 모드(유도)에 이동을 넘긴다.</summary>
        public override bool CanYieldMove(SimulationSpellEntity entity, object state) => ((OrbitState)state).IsEntered;

        /// <summary>직접 시전은 시전자의 현재 위치, 이벤트 생성은 생성 기준 위치를 공전 중심으로 반환한다.</summary>
        internal static SimVector GetCenter(SimulationSpellEntity entity)
            => entity.FromEvent ? entity.Anchor : entity.Caster.Position;

        /// <summary>단위 반경 방향과 공통 반경으로 현재 중심에 대한 공전 궤도 위치를 반환한다.</summary>
        internal static SimVector GetPoint(SimulationSpellEntity entity, SimVector radial)
            => GetCenter(entity) + radial * entity.Stats.OrbitRadius;

        /// <summary>장착된 공전 상태의 각속도와 반경으로 이탈 시 접선 속도(px/초)를 반환한다. 공전이 없으면 0이다.</summary>
        internal static double GetExitSpeed(SimulationSpellEntity entity)
        {
            for (int index = 0; index < entity.ModifierRuntimes.Count; index++)
            {
                if (entity.ModifierRuntimes[index].RuneId != OrbitModifierRules.RUNE_ID) continue;
                var orbit = (OrbitState)entity.GetModifierState(index);
                return Math.Abs(orbit.AngularSpeed) * entity.Stats.OrbitRadius;
            }
            return 0;
        }

        /// <summary>빔 경로의 시작 위치를 현재 공전 중심의 궤도 위에 두고 진행 방향을 반시계 접선으로 정한다. 반경이 없으면 유지한다.</summary>
        public override void BeginPath(SpellRuntimeContext context, SimulationSpellEntity entity, object state)
        {
            double radius = entity.Stats.OrbitRadius;
            if (radius <= 0) return;
            double angle = ((OrbitState)state).Angle;
            SimVector radial = new SimVector(Math.Cos(angle), Math.Sin(angle));
            entity.Move(GetPoint(entity, radial), new SimVector(-radial.Y, radial.X));
        }

        /// <summary>궤도 각도·각속도와 현재·최초 진입 여부를 결정성 해시에 기록한다.</summary>
        public override void WriteState(SimulationSpellEntity entity, object state, StringBuilder buffer)
        {
            var orbit = (OrbitState)state;
            buffer.Append(FormattableString.Invariant($"|orbit:{orbit.Angle:R}:{orbit.IsEntered}:{orbit.AngularSpeed:R}:{orbit.HasEnteredOnce}"));
        }

        /// <summary>개체 하나의 궤도 각도, 회전·진입 속도와 진입 여부다.</summary>
        private sealed class OrbitState
        {
            private readonly double _entrySpeed;
            private double _angularSpeed;
            private double _angle;
            private bool _isEntered;
            private bool _hasEnteredOnce;

            /// <summary>현재 궤도 자리 각도(라디안)다.</summary>
            public double Angle => _angle;

            /// <summary>초당 회전 각도(라디안)다.</summary>
            public double AngularSpeed => _angularSpeed;

            /// <summary>궤도 자리로 날아갈 때의 기본 속도(px/s)다.</summary>
            public double EntrySpeed => _entrySpeed;

            /// <summary>궤도 진입을 마치고 궤도 위에 있는지 나타낸다.</summary>
            public bool IsEntered => _isEntered;

            /// <summary>최초 진입 시 한 바퀴 각속도를 이미 계산했는지 반환한다.</summary>
            public bool HasEnteredOnce => _hasEnteredOnce;

            /// <summary>시작 각도와 초당 회전 속도, 궤도 진입 속도로 런타임 상태를 초기화한다.</summary>
            public OrbitState(double angle, double angularSpeed, double entrySpeed)
            {
                _angle = angle;
                _angularSpeed = angularSpeed;
                _entrySpeed = entrySpeed;
            }

            /// <summary>궤도 자리를 한 틱만큼 반시계로 돌린다.</summary>
            public void Rotate() => _angle += _angularSpeed * RuneSimulation.STEP_SECONDS;

            /// <summary>최초 진입 시 개체의 실제 진입 나이로 각속도를 한 번 정하고 현재 진입 완료 상태를 켠다.</summary>
            public void Enter(SimulationSpellEntity entity)
            {
                if (!_hasEnteredOnce)
                {
                    _angularSpeed = GetEnteredAngularSpeed(entity);
                    _hasEnteredOnce = true;
                }
                _isEntered = true;
            }

            /// <summary>개체가 궤도를 떠난 상태로 바꾼다. 다시 궤도 자리로 날아가 진입해야 한다.</summary>
            public void Leave() => _isEntered = false;
        }
    }
}
