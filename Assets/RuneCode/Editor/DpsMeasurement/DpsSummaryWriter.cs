using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RuneCode.Editor
{
    /// <summary>
    /// 측정 결과 묶음을 등급·첫 행동·체인 길이·모드 기여·상위·하위·경고 섹션으로 요약한 CSV를 쓴다.
    /// measured 상태인 결과만 대상으로 삼고 모든 섹션을 한 파일의 section 열로 구분한다.
    /// </summary>
    public static class DpsSummaryWriter
    {
        private const string CSV_HEADER = "section,conditionId,key,count,meanDps,meanBurstDps,meanSustainedDps,value";
        private const string CSV_SEPARATOR = ",";
        private const string CSV_QUOTE = "\"";
        private const string NUMBER_FORMAT = "0.###";
        private const string SECTION_GRADE = "grade";
        private const string SECTION_FIRST_BEHAVIOR = "firstBehavior";
        private const string SECTION_CHAIN_LENGTH = "chainLength";
        private const string SECTION_MODIFIER_CONTRIBUTION = "modifierContribution";
        private const string SECTION_TOP = "top";
        private const string SECTION_BOTTOM = "bottom";
        private const string SECTION_WARNING = "warning";
        private const string GRADE_SEPARATOR = ":";
        private const string PRESET_SEPARATOR = ">";
        private const char PRESET_SEPARATOR_CHAR = '>';
        private const string CONTRIBUTION_KEY_SEPARATOR = ":";
        private const int TOP_BOTTOM_COUNT = 10;

        /// <summary>
        /// 요약 CSV를 쓴다. 섹션 순서는 grade, firstBehavior, chainLength, modifierContribution, top, bottom, warning이고
        /// 행은 조건을 측정 입력 나열 순서대로, 조건 안에서는 키 정렬 순서로 배치한다. 폴더가 없으면 만든다.
        /// </summary>
        public static void Write(MeasurementResultSet set, string path)
        {
            List<MeasurementResult> measuredResults = CollectMeasuredResults(set);
            var rows = new List<SummaryRow>();
            AddGradeRows(rows, set.Suite, measuredResults);
            AddFirstBehaviorRows(rows, set.Suite, measuredResults);
            AddChainLengthRows(rows, set.Suite, measuredResults);
            AddModifierContributionRows(rows, set.Suite, measuredResults);
            AddTopBottomRows(rows, set.Suite, measuredResults);
            AddWarningRows(rows, set.Suite, measuredResults);
            WriteCsv(path, rows);
        }

        /// <summary>결과 목록에서 measured 상태인 결과만 나열 순서대로 모은다.</summary>
        private static List<MeasurementResult> CollectMeasuredResults(MeasurementResultSet set)
        {
            var measuredResults = new List<MeasurementResult>();
            foreach (MeasurementResult result in set.Results)
                if (result.Status == MeasurementCodes.STATUS_MEASURED) measuredResults.Add(result);
            return measuredResults;
        }

        /// <summary>조건 × 등급별 평균 행을 추가한다. key는 등급이고 value는 비워 둔다.</summary>
        private static void AddGradeRows(List<SummaryRow> rows, MeasurementSuite suite, List<MeasurementResult> measuredResults)
        {
            foreach (MeasurementCondition condition in suite.Conditions)
            {
                var accumulators = new Dictionary<string, MeanAccumulator>(StringComparer.Ordinal);
                foreach (MeasurementResult result in measuredResults)
                {
                    if (result.ConditionId != condition.ConditionId) continue;
                    GetOrCreate(accumulators, result.Grade).Add(result);
                }
                AddAccumulatorRows(rows, SECTION_GRADE, condition.ConditionId, accumulators);
            }
        }

        /// <summary>
        /// 조건 × 첫 행동의 행동 ID별 평균 행을 추가한다. 조합 ID에서 프리셋 ID를 읽어 첫 프리셋의 행동 ID를 key로 쓰고
        /// value는 비워 둔다. 조합 ID를 읽을 수 없거나 프리셋을 찾을 수 없는 결과는 건너뛴다.
        /// </summary>
        private static void AddFirstBehaviorRows(List<SummaryRow> rows, MeasurementSuite suite, List<MeasurementResult> measuredResults)
        {
            Dictionary<string, string> behaviorIdByPresetId = CreateBehaviorIdLookup(suite);
            foreach (MeasurementCondition condition in suite.Conditions)
            {
                var accumulators = new Dictionary<string, MeanAccumulator>(StringComparer.Ordinal);
                foreach (MeasurementResult result in measuredResults)
                {
                    if (result.ConditionId != condition.ConditionId) continue;
                    if (!TryParseBuildId(result.BuildId, out string grade, out string[] presetIds)) continue;
                    if (!behaviorIdByPresetId.TryGetValue(presetIds[0], out string behaviorId)) continue;
                    GetOrCreate(accumulators, behaviorId).Add(result);
                }
                AddAccumulatorRows(rows, SECTION_FIRST_BEHAVIOR, condition.ConditionId, accumulators);
            }
        }

        /// <summary>조건 × 체인 길이별 평균 행을 추가한다. key는 결과의 체인 길이 값이고 value는 비워 둔다.</summary>
        private static void AddChainLengthRows(List<SummaryRow> rows, MeasurementSuite suite, List<MeasurementResult> measuredResults)
        {
            foreach (MeasurementCondition condition in suite.Conditions)
            {
                var accumulators = new Dictionary<int, MeanAccumulator>();
                foreach (MeasurementResult result in measuredResults)
                {
                    if (result.ConditionId != condition.ConditionId) continue;
                    if (!accumulators.TryGetValue(result.ChainLength, out MeanAccumulator accumulator))
                    {
                        accumulator = new MeanAccumulator();
                        accumulators.Add(result.ChainLength, accumulator);
                    }
                    accumulator.Add(result);
                }
                var lengths = new List<int>(accumulators.Keys);
                lengths.Sort();
                foreach (int length in lengths)
                    rows.Add(accumulators[length].CreateRow(SECTION_CHAIN_LENGTH, condition.ConditionId, length.ToString(CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>
        /// 모드 하나 추가 짝(P→Q)의 기여율 행을 추가한다. 같은 조건·등급에서 체인의 한 위치만 P와 Q가 다르고 나머지 위치가 모두 같은
        /// 두 결과 A(P 쪽), B(Q 쪽)를 짝으로 삼되, A.Dps가 0보다 크고 두 결과 모두 outOfRange 경고가 없을 때만 누적한다.
        /// key는 "행동ID:모드ID:등급", value는 기여율 (B.Dps − A.Dps) / A.Dps의 평균, count는 짝 수, meanDps는 B 쪽 평균 DPS다.
        /// </summary>
        private static void AddModifierContributionRows(List<SummaryRow> rows, MeasurementSuite suite, List<MeasurementResult> measuredResults)
        {
            List<PresetUpgrade> upgrades = FindPresetUpgrades(suite);
            if (upgrades.Count == 0) return;
            foreach (MeasurementCondition condition in suite.Conditions)
            {
                var resultByBuildId = new Dictionary<string, MeasurementResult>(StringComparer.Ordinal);
                foreach (MeasurementResult result in measuredResults)
                    if (result.ConditionId == condition.ConditionId) resultByBuildId[result.BuildId] = result;
                var contributions = new Dictionary<string, ContributionAccumulator>(StringComparer.Ordinal);
                foreach (MeasurementResult resultA in resultByBuildId.Values)
                {
                    if (resultA.Dps <= 0) continue;
                    if (HasOutOfRangeWarning(resultA)) continue;
                    if (!TryParseBuildId(resultA.BuildId, out string grade, out string[] presetIds)) continue;
                    foreach (PresetUpgrade upgrade in upgrades)
                    {
                        for (int position = 0; position < presetIds.Length; position++)
                        {
                            if (presetIds[position] != upgrade.BasePresetId) continue;
                            string buildIdB = ReplacePreset(grade, presetIds, position, upgrade.UpgradedPresetId);
                            if (!resultByBuildId.TryGetValue(buildIdB, out MeasurementResult resultB)) continue;
                            if (HasOutOfRangeWarning(resultB)) continue;
                            string key = upgrade.BehaviorId + CONTRIBUTION_KEY_SEPARATOR + upgrade.AddedModifierId
                                + CONTRIBUTION_KEY_SEPARATOR + grade;
                            if (!contributions.TryGetValue(key, out ContributionAccumulator accumulator))
                            {
                                accumulator = new ContributionAccumulator();
                                contributions.Add(key, accumulator);
                            }
                            accumulator.Add((resultB.Dps - resultA.Dps) / resultA.Dps, resultB.Dps);
                        }
                    }
                }
                AddContributionRows(rows, condition.ConditionId, contributions);
            }
        }

        /// <summary>
        /// suite.Presets에서 "행동 ID·속성이 같고 P의 모드 집합을 모두 포함하며 모드 수가 정확히 1 더 많은" (P, Q, 추가 모드) 짝을 찾는다.
        /// 프리셋 나열 순서 기준으로 기준 프리셋 → 추가 프리셋 순서대로 반환한다.
        /// </summary>
        private static List<PresetUpgrade> FindPresetUpgrades(MeasurementSuite suite)
        {
            var upgrades = new List<PresetUpgrade>();
            IReadOnlyList<BehaviorPreset> presets = suite.Presets;
            for (int baseIndex = 0; baseIndex < presets.Count; baseIndex++)
            {
                for (int upgradedIndex = 0; upgradedIndex < presets.Count; upgradedIndex++)
                {
                    if (baseIndex == upgradedIndex) continue;
                    if (TryCreatePresetUpgrade(presets[baseIndex], presets[upgradedIndex], out PresetUpgrade upgrade))
                        upgrades.Add(upgrade);
                }
            }
            return upgrades;
        }

        /// <summary>두 프리셋이 모드 하나 추가 관계인지 검사해 짝을 만든다. 관계가 아니면 false를 반환한다.</summary>
        private static bool TryCreatePresetUpgrade(BehaviorPreset basePreset, BehaviorPreset upgradedPreset, out PresetUpgrade upgrade)
        {
            upgrade = null;
            if (basePreset.BehaviorId != upgradedPreset.BehaviorId) return false;
            if (basePreset.ElementId != upgradedPreset.ElementId) return false;
            if (upgradedPreset.ModifierIds.Count != basePreset.ModifierIds.Count + 1) return false;
            var baseModifiers = new HashSet<string>(basePreset.ModifierIds, StringComparer.Ordinal);
            var upgradedModifiers = new HashSet<string>(upgradedPreset.ModifierIds, StringComparer.Ordinal);
            foreach (string modifierId in basePreset.ModifierIds)
                if (!upgradedModifiers.Contains(modifierId)) return false;
            string addedModifierId = null;
            foreach (string modifierId in upgradedPreset.ModifierIds)
            {
                if (baseModifiers.Contains(modifierId)) continue;
                // 새 모드가 둘로 나오면 정확히 하나 더 많은 관계가 아니다.
                if (addedModifierId != null) return false;
                addedModifierId = modifierId;
            }
            if (addedModifierId == null) return false;
            upgrade = new PresetUpgrade(basePreset, upgradedPreset, addedModifierId);
            return true;
        }

        /// <summary>기여 누적기를 키 정렬 순서로 행으로 바꿔 추가한다.</summary>
        private static void AddContributionRows(List<SummaryRow> rows, string conditionId, Dictionary<string, ContributionAccumulator> contributions)
        {
            var keys = new List<string>(contributions.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
                rows.Add(contributions[key].CreateRow(conditionId, key));
        }

        /// <summary>
        /// 조건별 DPS 상위·하위 행을 추가한다. 상위는 measured 결과 전체에서, 하위는 Dps가 0보다 크고 outOfRange 경고가 없는
        /// 결과 중에서 골라 각각 최대 10개를 순위와 함께 쓴다. key는 buildId, value는 1부터 시작하는 순위다.
        /// </summary>
        private static void AddTopBottomRows(List<SummaryRow> rows, MeasurementSuite suite, List<MeasurementResult> measuredResults)
        {
            foreach (MeasurementCondition condition in suite.Conditions)
            {
                var conditionResults = new List<MeasurementResult>();
                foreach (MeasurementResult result in measuredResults)
                    if (result.ConditionId == condition.ConditionId) conditionResults.Add(result);
                conditionResults.Sort(CompareByDpsDescending);
                AddRankRows(rows, SECTION_TOP, condition.ConditionId, conditionResults);
                conditionResults.RemoveAll(result => result.Dps <= 0 || HasOutOfRangeWarning(result));
                conditionResults.Sort(CompareByDpsAscending);
                AddRankRows(rows, SECTION_BOTTOM, condition.ConditionId, conditionResults);
            }
        }

        /// <summary>정렬된 결과 목록 앞에서부터 최대 10개의 순위 행을 추가한다.</summary>
        private static void AddRankRows(List<SummaryRow> rows, string section, string conditionId, List<MeasurementResult> sortedResults)
        {
            for (int index = 0; index < sortedResults.Count && index < TOP_BOTTOM_COUNT; index++)
                rows.Add(new SummaryRow(section, conditionId, sortedResults[index].BuildId, null, null, null, null, index + 1));
        }

        /// <summary>DPS 내림차순, 같으면 조합 ID 오름차순으로 두 결과의 순서를 정한다.</summary>
        private static int CompareByDpsDescending(MeasurementResult left, MeasurementResult right)
        {
            int dpsOrder = right.Dps.CompareTo(left.Dps);
            if (dpsOrder != 0) return dpsOrder;
            return string.CompareOrdinal(left.BuildId, right.BuildId);
        }

        /// <summary>DPS 오름차순, 같으면 조합 ID 오름차순으로 두 결과의 순서를 정한다.</summary>
        private static int CompareByDpsAscending(MeasurementResult left, MeasurementResult right)
        {
            int dpsOrder = left.Dps.CompareTo(right.Dps);
            if (dpsOrder != 0) return dpsOrder;
            return string.CompareOrdinal(left.BuildId, right.BuildId);
        }

        /// <summary>조건 × 경고 코드별 결과 수 행을 추가한다. 한 결과에 경고가 여러 개면 각 코드마다 센다. count만 채우고 나머지는 비워 둔다.</summary>
        private static void AddWarningRows(List<SummaryRow> rows, MeasurementSuite suite, List<MeasurementResult> measuredResults)
        {
            foreach (MeasurementCondition condition in suite.Conditions)
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (MeasurementResult result in measuredResults)
                {
                    if (result.ConditionId != condition.ConditionId) continue;
                    foreach (string warning in result.Warnings)
                        counts[warning] = counts.TryGetValue(warning, out int count) ? count + 1 : 1;
                }
                var codes = new List<string>(counts.Keys);
                codes.Sort(StringComparer.Ordinal);
                foreach (string code in codes)
                    rows.Add(new SummaryRow(SECTION_WARNING, condition.ConditionId, code, counts[code], null, null, null, null));
            }
        }

        /// <summary>결과 경고에 outOfRange가 있는지 반환한다.</summary>
        private static bool HasOutOfRangeWarning(MeasurementResult result)
        {
            return result.Warnings.Contains(MeasurementCodes.WARNING_OUT_OF_RANGE);
        }

        /// <summary>프리셋 ID → 행동 ID 조회표를 만든다.</summary>
        private static Dictionary<string, string> CreateBehaviorIdLookup(MeasurementSuite suite)
        {
            var behaviorIdByPresetId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (BehaviorPreset preset in suite.Presets)
                behaviorIdByPresetId[preset.PresetId] = preset.BehaviorId;
            return behaviorIdByPresetId;
        }

        /// <summary>"등급:프리셋>프리셋" 형식의 조합 ID를 등급과 프리셋 ID 목록으로 나눈다. 형식이 다르면 false를 반환한다.</summary>
        private static bool TryParseBuildId(string buildId, out string grade, out string[] presetIds)
        {
            grade = null;
            presetIds = null;
            int separatorIndex = buildId.IndexOf(GRADE_SEPARATOR, StringComparison.Ordinal);
            if (separatorIndex <= 0) return false;
            grade = buildId.Substring(0, separatorIndex);
            presetIds = buildId.Substring(separatorIndex + 1).Split(PRESET_SEPARATOR_CHAR);
            foreach (string presetId in presetIds)
                if (presetId.Length == 0) return false;
            return true;
        }

        /// <summary>조합 ID의 한 위치를 다른 프리셋 ID로 바꾼 조합 ID를 반환한다.</summary>
        private static string ReplacePreset(string grade, string[] presetIds, int position, string presetId)
        {
            var replaced = new string[presetIds.Length];
            Array.Copy(presetIds, replaced, presetIds.Length);
            replaced[position] = presetId;
            return grade + GRADE_SEPARATOR + string.Join(PRESET_SEPARATOR, replaced);
        }

        /// <summary>조회표에서 누적기를 꺼내고 없으면 만들어 넣는다.</summary>
        private static MeanAccumulator GetOrCreate(Dictionary<string, MeanAccumulator> accumulators, string key)
        {
            if (accumulators.TryGetValue(key, out MeanAccumulator accumulator)) return accumulator;
            accumulator = new MeanAccumulator();
            accumulators.Add(key, accumulator);
            return accumulator;
        }

        /// <summary>문자열 키 누적기를 키 정렬 순서로 행으로 바꿔 추가한다.</summary>
        private static void AddAccumulatorRows(List<SummaryRow> rows, string section, string conditionId, Dictionary<string, MeanAccumulator> accumulators)
        {
            var keys = new List<string>(accumulators.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
                rows.Add(accumulators[key].CreateRow(section, conditionId, key));
        }

        /// <summary>숫자를 InvariantCulture "0.###" 형식 문자열로 만든다.</summary>
        private static string FormatNumber(double value)
        {
            return value.ToString(NUMBER_FORMAT, CultureInfo.InvariantCulture);
        }

        /// <summary>값이 있으면 InvariantCulture "0.###" 문자열, 없으면 null을 반환한다.</summary>
        private static string FormatNumberOrNull(double? value)
        {
            return value.HasValue ? FormatNumber(value.Value) : null;
        }

        /// <summary>셀 값에 쉼표·따옴표·줄바꿈이 있으면 따옴표로 감싸고 안쪽 따옴표는 두 번 쓴다. 값이 없으면 null을 반환한다.</summary>
        private static string EscapeCell(string value)
        {
            if (value == null) return null;
            if (value.IndexOf(CSV_SEPARATOR, StringComparison.Ordinal) < 0 && value.IndexOf(CSV_QUOTE, StringComparison.Ordinal) < 0
                && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0)
                return value;
            return CSV_QUOTE + value.Replace(CSV_QUOTE, CSV_QUOTE + CSV_QUOTE) + CSV_QUOTE;
        }

        /// <summary>요약 행 목록을 헤더와 함께 UTF-8 BOM CSV 파일로 쓴다.</summary>
        private static void WriteCsv(string path, List<SummaryRow> rows)
        {
            var csv = new StringBuilder();
            csv.AppendLine(CSV_HEADER);
            foreach (SummaryRow row in rows)
            {
                csv.AppendLine(string.Join(CSV_SEPARATOR,
                    EscapeCell(row.Section),
                    EscapeCell(row.ConditionId),
                    EscapeCell(row.Key),
                    EscapeCell(row.Count.HasValue ? row.Count.Value.ToString(CultureInfo.InvariantCulture) : null),
                    EscapeCell(FormatNumberOrNull(row.MeanDps)),
                    EscapeCell(FormatNumberOrNull(row.MeanBurstDps)),
                    EscapeCell(FormatNumberOrNull(row.MeanSustainedDps)),
                    EscapeCell(FormatNumberOrNull(row.Value))));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        /// <summary>조건 내 그룹 하나의 평균 누적이다. DPS 세 지표를 함께 더해 개수로 나눈 행을 만든다.</summary>
        private sealed class MeanAccumulator
        {
            private double _dpsSum;
            private double _burstDpsSum;
            private double _sustainedDpsSum;
            private int _count;

            /// <summary>결과 하나의 DPS, 버스트 DPS, 지속 DPS를 더한다.</summary>
            public void Add(MeasurementResult result)
            {
                _dpsSum += result.Dps;
                _burstDpsSum += result.BurstDps;
                _sustainedDpsSum += result.SustainedDps;
                _count++;
            }

            /// <summary>count와 세 평균을 채우고 value는 비워 둔 행을 만든다.</summary>
            public SummaryRow CreateRow(string section, string conditionId, string key)
            {
                return new SummaryRow(section, conditionId, key, _count, _dpsSum / _count, _burstDpsSum / _count, _sustainedDpsSum / _count, null);
            }
        }

        /// <summary>모드 기여 짝의 누적이다. 기여율과 B 쪽 DPS를 짝 수만큼 더해 평균 행을 만든다.</summary>
        private sealed class ContributionAccumulator
        {
            private double _ratioSum;
            private double _upgradedDpsSum;
            private int _pairCount;

            /// <summary>기여율 하나와 짝의 B 쪽 DPS를 더한다.</summary>
            public void Add(double ratio, double upgradedDps)
            {
                _ratioSum += ratio;
                _upgradedDpsSum += upgradedDps;
                _pairCount++;
            }

            /// <summary>count = 짝 수, meanDps = B 쪽 평균, value = 기여율 평균인 행을 만들고 나머지는 비워 둔다.</summary>
            public SummaryRow CreateRow(string conditionId, string key)
            {
                return new SummaryRow(SECTION_MODIFIER_CONTRIBUTION, conditionId, key, _pairCount,
                    _upgradedDpsSum / _pairCount, null, null, _ratioSum / _pairCount);
            }
        }

        /// <summary>프리셋 Q가 프리셋 P에 모드 하나를 추가한 관계라는 짝이다.</summary>
        private sealed class PresetUpgrade
        {
            private readonly BehaviorPreset _basePreset;
            private readonly BehaviorPreset _upgradedPreset;
            private readonly string _addedModifierId;

            public string BasePresetId => _basePreset.PresetId;
            public string UpgradedPresetId => _upgradedPreset.PresetId;
            public string BehaviorId => _basePreset.BehaviorId;
            public string AddedModifierId => _addedModifierId;

            /// <summary>기준 프리셋, 모드가 추가된 프리셋과 추가된 모드 ID로 짝을 만든다.</summary>
            public PresetUpgrade(BehaviorPreset basePreset, BehaviorPreset upgradedPreset, string addedModifierId)
            {
                _basePreset = basePreset;
                _upgradedPreset = upgradedPreset;
                _addedModifierId = addedModifierId;
            }
        }

        /// <summary>요약 CSV 한 행이다. 값이 없는 열은 null로 두고 쓸 때 빈 칸으로 만든다.</summary>
        private sealed class SummaryRow
        {
            private readonly string _section;
            private readonly string _conditionId;
            private readonly string _key;
            private readonly int? _count;
            private readonly double? _meanDps;
            private readonly double? _meanBurstDps;
            private readonly double? _meanSustainedDps;
            private readonly double? _value;

            public string Section => _section;
            public string ConditionId => _conditionId;
            public string Key => _key;
            public int? Count => _count;
            public double? MeanDps => _meanDps;
            public double? MeanBurstDps => _meanBurstDps;
            public double? MeanSustainedDps => _meanSustainedDps;
            public double? Value => _value;

            /// <summary>섹션, 조건, 키와 지표 값으로 행을 만든다. 값이 없는 열은 null로 전달한다.</summary>
            public SummaryRow(string section, string conditionId, string key, int? count, double? meanDps, double? meanBurstDps, double? meanSustainedDps, double? value)
            {
                _section = section;
                _conditionId = conditionId;
                _key = key;
                _count = count;
                _meanDps = meanDps;
                _meanBurstDps = meanBurstDps;
                _meanSustainedDps = meanSustainedDps;
                _value = value;
            }
        }
    }
}
