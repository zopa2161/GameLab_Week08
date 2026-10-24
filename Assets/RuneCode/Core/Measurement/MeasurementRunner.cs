using System;
using System.Collections.Generic;

namespace RuneCode
{
    /// <summary>
    /// 조합 하나 × 조건 하나를 시뮬레이션으로 실행해 MeasurementResult를 만드는 측정 실행기다.
    /// GameData.Load()는 호출자 책임이며 Run을 부르기 전에 완료돼 있어야 한다.
    /// </summary>
    public static class MeasurementRunner
    {
        private const int BURST_WINDOW_SECONDS = 5;
        private const int SUSTAINED_WINDOW_SECONDS = 10;
        private const double BOTTLENECK_ENERGY_FAILURE_RATIO = 0.1;
        private const double REPEAT_VARIANCE_RATIO = 0.05;
        private const double UNLIMITED_RANGE_THRESHOLD = float.MaxValue;

        /// <summary>
        /// 조합을 SpellBuildCompiler.Compile로 컴파일하고 조건의 시드·반복 횟수로 시뮬레이션을 실행해
        /// 지표와 경고를 채운 MeasurementResult를 반환한다. 컴파일에 실패하면 식별 필드와 오류 코드만 채워 반환한다.
        /// 입력은 매 틱 가만히 서서 +x 방향으로 시전을 유지하고 Noise 행동이 있는 조합만 RepeatCount회 반복한다.
        /// </summary>
        public static MeasurementResult Run(MeasurementSuite suite, SpellBuild build, MeasurementCondition condition)
        {
            var result = new MeasurementResult();
            result.BuildId = build.BuildId;
            result.ConditionId = condition.ConditionId;
            result.Grade = build.Grade;
            result.ChainLength = build.ChainLength;

            CompileResult compiled = SpellBuildCompiler.Compile(build, suite);
            if (!compiled.Ok)
            {
                foreach (CompileIssue error in compiled.Errors) result.CompileErrors.Add(error.Code);
                result.Status = MeasurementCodes.STATUS_COMPILE_ERROR;
                return result;
            }

            CompiledSpell spell = compiled.Spell;
            result.RamUsed = spell.RamUsed;
            float castCost = spell.EnergyCost;
            result.CastCost = float.IsInfinity(castCost) || float.IsNaN(castCost) ? -1 : castCost;
            result.CooldownSeconds = spell.GetCooldownSeconds(GameData.Balance.Sim.ExecutionTimeScale);
            double castRange = SpellCastRange.GetAutomaticCastRange(spell);
            // 빔은 사거리 제한이 없는 것으로 계산되어 float 최대값이 나온다. 결과 파일에는 비용과 같이 -1(제한 없음)로 기록한다.
            result.CastRange = castRange >= UNLIMITED_RANGE_THRESHOLD ? -1 : castRange;

            int runCount = HasNoiseAction(spell.Root) ? Math.Max(1, condition.RepeatCount) : 1;
            result.RunCount = runCount;
            double durationSeconds = condition.DurationSeconds;
            int ticks = (int)Math.Round(durationSeconds * RuneSimulation.TICK_RATE);

            var totals = new RunTotals(build.ChainLength);
            bool isStartOutOfRange = false;
            for (int runIndex = 0; runIndex < runCount; runIndex++)
            {
                RuneSimulation simulation = MeasurementSimulationFactory.Create(suite.Profile, condition, condition.Seed + runIndex);
                simulation.SetLoadout(new[] { spell });
                if (runIndex == 0) isStartOutOfRange = IsStartOutOfRange(simulation, castRange);
                RunOnce(simulation, ticks, durationSeconds, totals);
            }
            FillAverages(result, build, totals, runCount);
            AddWarnings(result, isStartOutOfRange);
            result.Status = MeasurementCodes.STATUS_MEASURED;
            return result;
        }

        /// <summary>실행 트리 어딘가에 Noise 행동이 있으면 true를 반환한다. Noise가 있으면 시드를 바꿔 반복 실행한다.</summary>
        private static bool HasNoiseAction(IReadOnlyList<SpellAction> actions)
        {
            foreach (SpellAction action in actions)
            {
                if (action.Noise) return true;
                foreach (IReadOnlyList<SpellAction> branch in action.Branches)
                    if (HasNoiseAction(branch)) return true;
            }
            return false;
        }

        /// <summary>시작 시점 적 위치 기준으로 가장 가까운 적까지의 거리에서 적 반경을 뺀 값이 사거리보다 크면 true를 반환한다.</summary>
        private static bool IsStartOutOfRange(RuneSimulation simulation, double castRange)
        {
            var nearest = double.MaxValue;
            foreach (SimulationEnemy enemy in simulation.Enemies)
            {
                if (!enemy.IsAlive) continue;
                double distance = SimVector.Distance(enemy.Position, simulation.Player.Position) - enemy.Radius;
                nearest = Math.Min(nearest, distance);
            }
            return nearest != double.MaxValue && nearest > castRange;
        }

        /// <summary>시뮬레이션을 지정 틱 수만큼 실행하고 이번 실행의 지표를 누계에 더한다. 잔여 피해는 버린다.</summary>
        private static void RunOnce(RuneSimulation simulation, int ticks, double durationSeconds, RunTotals totals)
        {
            for (int tick = 0; tick < ticks; tick++)
                simulation.Step(new SimulationInput(SimVector.Zero, new SimVector(1, 0), true, isAutomatic: true));

            double dps = simulation.TotalDamage / durationSeconds;
            totals._totalDamage += simulation.TotalDamage;
            totals._statusDamage += simulation.StatusDamage;
            totals._energySpent += simulation.EnergySpent;
            totals._hitCount += simulation.EnemyDamageEventCount;
            totals._dpsSum += dps;
            totals._dpsMin = Math.Min(totals._dpsMin, dps);
            totals._dpsMax = Math.Max(totals._dpsMax, dps);
            IReadOnlyList<double> damageBySecond = simulation.DamageBySecond;
            totals._burstDpsSum += WindowDps(damageBySecond, 0, BURST_WINDOW_SECONDS, BURST_WINDOW_SECONDS);
            int sustainedStart = Math.Max(0, (int)Math.Floor(durationSeconds - SUSTAINED_WINDOW_SECONDS));
            totals._sustainedDpsSum += WindowDps(damageBySecond, sustainedStart, (int)Math.Ceiling(durationSeconds), SUSTAINED_WINDOW_SECONDS);
            totals._damagePerEnergySum += simulation.EnergySpent > 0 ? simulation.TotalDamage / simulation.EnergySpent : 0;
            totals._castAttempts += simulation.RootCastAttempts;
            totals._castSuccesses += simulation.RootCastSuccesses;
            totals._castFailures += simulation.RootCastFailures;
            int failureTick = simulation.FirstRootCastFailureTick;
            if (failureTick >= 0)
            {
                totals._failureSecondsSum += failureTick / (double)RuneSimulation.TICK_RATE;
                totals._failureCount++;
            }
            totals._peakSpellEntities += simulation.PeakSpellEntities;
            totals._droppedExecutions += simulation.DroppedExecutions;
            CollectEnemySpread(simulation, totals);
            for (int chainIndex = 0; chainIndex < totals._nodeDamageSums.Length; chainIndex++)
                totals._nodeDamageSums[chainIndex] += simulation.GetNodeDamage(SpellBuildCompiler.GetBehaviorNodeId(chainIndex));
        }

        /// <summary>startSecond 이상 endSecondExclusive 미만의 초 버킷 피해 합을 창 길이로 나눠 창 DPS를 반환한다. 버킷이 없는 초는 0으로 센다.</summary>
        private static double WindowDps(IReadOnlyList<double> damageBySecond, int startSecond, int endSecondExclusive, int windowSeconds)
        {
            double sum = 0;
            int last = Math.Min(endSecondExclusive, damageBySecond.Count);
            for (int second = startSecond; second < last; second++) sum += damageBySecond[second];
            return sum / windowSeconds;
        }

        /// <summary>시뮬레이션의 모든 적(피해 0인 적 포함)을 돌며 피해를 준 적 수와 적별 누적 피해 최소·최대를 누계에 더한다.</summary>
        private static void CollectEnemySpread(RuneSimulation simulation, RunTotals totals)
        {
            double runMin = double.MaxValue;
            double runMax = 0;
            int damagedCount = 0;
            foreach (SimulationEnemy enemy in simulation.Enemies)
            {
                simulation.DamageByEnemyId.TryGetValue(enemy.Id, out double damage);
                if (damage > 0) damagedCount++;
                runMin = Math.Min(runMin, damage);
                runMax = Math.Max(runMax, damage);
            }
            totals._damagedEnemyCount += damagedCount;
            totals._enemyDamageMinSum += runMin;
            totals._enemyDamageMaxSum += runMax;
        }

        /// <summary>반복 누계를 결과 지표로 옮긴다. double 지표는 평균, int 지표는 평균을 반올림하고 DPS 최소·최대는 그대로 둔다.</summary>
        private static void FillAverages(MeasurementResult result, SpellBuild build, RunTotals totals, int runCount)
        {
            result.TotalDamage = totals._totalDamage / runCount;
            result.Dps = totals._dpsSum / runCount;
            result.BurstDps = totals._burstDpsSum / runCount;
            result.SustainedDps = totals._sustainedDpsSum / runCount;
            result.StatusDamage = totals._statusDamage / runCount;
            result.HitCount = (long)Math.Round(totals._hitCount / (double)runCount, MidpointRounding.AwayFromZero);
            result.EnergySpent = totals._energySpent / runCount;
            result.DamagePerEnergy = totals._damagePerEnergySum / runCount;
            result.CastAttempts = RoundToInt(totals._castAttempts / (double)runCount);
            result.CastSuccesses = RoundToInt(totals._castSuccesses / (double)runCount);
            result.CastFailures = RoundToInt(totals._castFailures / (double)runCount);
            result.FirstFailureSeconds = totals._failureCount > 0 ? totals._failureSecondsSum / totals._failureCount : -1;
            result.Bottleneck = GetBottleneck(result.CastAttempts, result.CastFailures);
            result.DamagedEnemyCount = RoundToInt(totals._damagedEnemyCount / (double)runCount);
            result.EnemyDamageMin = totals._enemyDamageMinSum / runCount;
            result.EnemyDamageMax = totals._enemyDamageMaxSum / runCount;
            for (int chainIndex = 0; chainIndex < totals._nodeDamageSums.Length; chainIndex++)
            {
                double damage = totals._nodeDamageSums[chainIndex] / runCount;
                double ratio = result.TotalDamage > 0 ? damage / result.TotalDamage : 0;
                result.BehaviorDamages.Add(new BehaviorDamageEntry(chainIndex, build.PresetIds[chainIndex], damage, ratio));
            }
            result.PeakSpellEntities = RoundToInt(totals._peakSpellEntities / (double)runCount);
            result.DroppedExecutions = RoundToInt(totals._droppedExecutions / (double)runCount);
            result.DpsMin = totals._dpsMin;
            result.DpsMax = totals._dpsMax;
        }

        /// <summary>시전 시도가 없으면 none, 실패 비율이 임계값 이상이면 energy, 아니면 cooldown을 반환한다.</summary>
        private static string GetBottleneck(int castAttempts, int castFailures)
        {
            if (castAttempts == 0) return MeasurementCodes.BOTTLENECK_NONE;
            return castFailures / (double)castAttempts >= BOTTLENECK_ENERGY_FAILURE_RATIO
                ? MeasurementCodes.BOTTLENECK_ENERGY
                : MeasurementCodes.BOTTLENECK_COOLDOWN;
        }

        /// <summary>개체·실행 한도, 시작 사거리, 반복 편차 경고를 측정 코드로 결과에 추가한다.</summary>
        private static void AddWarnings(MeasurementResult result, bool isStartOutOfRange)
        {
            if (result.PeakSpellEntities >= GameData.Balance.Limits.MaxLiveSpellEntities)
                result.Warnings.Add(MeasurementCodes.WARNING_ENTITY_LIMIT);
            if (result.DroppedExecutions > 0)
                result.Warnings.Add(MeasurementCodes.WARNING_EXECUTION_LIMIT);
            if (isStartOutOfRange)
                result.Warnings.Add(MeasurementCodes.WARNING_OUT_OF_RANGE);
            if (result.RunCount > 1 && result.Dps > 0
                && (result.DpsMax - result.DpsMin) / result.Dps > REPEAT_VARIANCE_RATIO)
                result.Warnings.Add(MeasurementCodes.WARNING_REPEAT_VARIANCE);
        }

        /// <summary>평균값을 0.5부터 올림해 int로 반환한다.</summary>
        private static int RoundToInt(double value)
        {
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        /// <summary>반복 실행의 지표 누계다. 최소·최대는 실행 간 값, 나머지는 합계로 보관한다.</summary>
        private sealed class RunTotals
        {
            internal double _totalDamage;
            internal double _statusDamage;
            internal double _energySpent;
            internal long _hitCount;
            internal double _dpsSum;
            internal double _dpsMin = double.MaxValue;
            internal double _dpsMax;
            internal double _burstDpsSum;
            internal double _sustainedDpsSum;
            internal double _damagePerEnergySum;
            internal int _castAttempts;
            internal int _castSuccesses;
            internal int _castFailures;
            internal double _failureSecondsSum;
            internal int _failureCount;
            internal int _peakSpellEntities;
            internal int _droppedExecutions;
            internal int _damagedEnemyCount;
            internal double _enemyDamageMinSum;
            internal double _enemyDamageMaxSum;
            internal readonly double[] _nodeDamageSums;

            /// <summary>체인 길이만큼 노드별 피해 누계 배열을 만든다.</summary>
            internal RunTotals(int chainLength)
            {
                _nodeDamageSums = new double[chainLength];
            }
        }
    }
}
