using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

namespace RuneCode.Editor
{
    /// <summary>
    /// 이전 측정 결과와 현재 결과를 조합·조건 키로 비교한다. 이전 DPS 조회표 로드와 비교 CSV 기록을 담당한다.
    /// </summary>
    public static class DpsResultComparer
    {
        private const string CSV_HEADER = "buildId,conditionId,previousDps,dps,dpsChangeRatio,status";
        private const string CSV_SEPARATOR = ",";
        private const string CSV_QUOTE = "\"";
        private const string NUMBER_FORMAT = "0.###";
        private const string STATUS_CHANGED = "changed";
        private const string STATUS_NEW = "new";
        private const string STATUS_REMOVED = "removed";
        private const string KEY_SEPARATOR = "|";

        /// <summary>이전 results.json을 읽어 measured 상태인 결과의 DpsResultKey → DPS 조회표를 반환한다. 빈 파일이면 빈 조회표를 반환한다.</summary>
        public static IReadOnlyDictionary<string, double> LoadPreviousDps(string resultsJsonPath)
        {
            MeasurementResultSet previous = JsonUtility.FromJson<MeasurementResultSet>(File.ReadAllText(resultsJsonPath));
            var previousDps = new Dictionary<string, double>(StringComparer.Ordinal);
            if (previous == null) return previousDps;
            foreach (MeasurementResult result in previous.Results)
            {
                if (result.Status != MeasurementCodes.STATUS_MEASURED) continue;
                previousDps[DpsResultKey.Create(result.BuildId, result.ConditionId)] = result.Dps;
            }
            return previousDps;
        }

        /// <summary>
        /// 현재 결과를 이전 DPS 조회표와 비교해 비교 CSV를 쓴다. 현재에만 있는 조합은 new, 이전에만 있는 조합은 removed로 기록하고
        /// 행은 변화율 절댓값 내림차순으로 정렬하되 new·removed는 맨 뒤에 둔다. changed 행의 이전 DPS가 0 이하면 변화율을 계산할 수 없어
        /// 빈 칸으로 쓰고 정렬 시 무한대 변화로 취급해 changed 행 중 맨 앞에 둔다. 폴더가 없으면 만든다.
        /// </summary>
        public static void WriteComparisonCsv(MeasurementResultSet current, IReadOnlyDictionary<string, double> previousDps, string path)
        {
            var rows = new List<ComparisonRow>();
            var remainingPreviousKeys = new HashSet<string>(previousDps.Keys, StringComparer.Ordinal);
            foreach (MeasurementResult result in current.Results)
            {
                if (result.Status != MeasurementCodes.STATUS_MEASURED) continue;
                string key = DpsResultKey.Create(result.BuildId, result.ConditionId);
                remainingPreviousKeys.Remove(key);
                if (!previousDps.TryGetValue(key, out double previousValue))
                {
                    rows.Add(new ComparisonRow(result.BuildId, result.ConditionId, null, result.Dps, null, STATUS_NEW, 0));
                    continue;
                }
                // 이전 DPS가 0 이하면 나눌 수 없어 변화율 열은 비우고 정렬만 무한대로 밀어 앞세운다.
                bool hasRatio = previousValue > 0;
                double changeRatio = hasRatio ? (result.Dps - previousValue) / previousValue : 0;
                double sortChangeRatio = hasRatio ? Math.Abs(changeRatio) : double.PositiveInfinity;
                rows.Add(new ComparisonRow(result.BuildId, result.ConditionId, previousValue, result.Dps,
                    hasRatio ? changeRatio : (double?)null, STATUS_CHANGED, sortChangeRatio));
            }
            foreach (string key in remainingPreviousKeys)
            {
                SplitKey(key, out string buildId, out string conditionId);
                rows.Add(new ComparisonRow(buildId, conditionId, previousDps[key], null, null, STATUS_REMOVED, 0));
            }
            rows.Sort(CompareRows);
            WriteCsv(path, rows);
        }

        /// <summary>status 순위(new, removed는 changed보다 뒤)를 반환한다.</summary>
        private static int StatusRank(string status)
        {
            if (status == STATUS_CHANGED) return 0;
            if (status == STATUS_NEW) return 1;
            return 2;
        }

        /// <summary>변화율 절댓값 내림차순, 같으면 buildId·conditionId 오름차순으로 두 행의 순서를 정한다. changed 행을 new·removed보다 앞에 둔다.</summary>
        private static int CompareRows(ComparisonRow left, ComparisonRow right)
        {
            int statusOrder = StatusRank(left.Status).CompareTo(StatusRank(right.Status));
            if (statusOrder != 0) return statusOrder;
            int ratioOrder = right.SortChangeRatio.CompareTo(left.SortChangeRatio);
            if (ratioOrder != 0) return ratioOrder;
            int buildOrder = string.CompareOrdinal(left.BuildId, right.BuildId);
            if (buildOrder != 0) return buildOrder;
            return string.CompareOrdinal(left.ConditionId, right.ConditionId);
        }

        /// <summary>DpsResultKey를 buildId와 conditionId로 되돌린다. 구분자는 첫 번째 '|' 하나만 본다.</summary>
        private static void SplitKey(string key, out string buildId, out string conditionId)
        {
            int separatorIndex = key.IndexOf(KEY_SEPARATOR, StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                buildId = key;
                conditionId = string.Empty;
                return;
            }
            buildId = key.Substring(0, separatorIndex);
            conditionId = key.Substring(separatorIndex + 1);
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

        /// <summary>비교 행 목록을 헤더와 함께 UTF-8 BOM CSV 파일로 쓴다.</summary>
        private static void WriteCsv(string path, List<ComparisonRow> rows)
        {
            var csv = new StringBuilder();
            csv.AppendLine(CSV_HEADER);
            foreach (ComparisonRow row in rows)
            {
                csv.AppendLine(string.Join(CSV_SEPARATOR,
                    EscapeCell(row.BuildId),
                    EscapeCell(row.ConditionId),
                    EscapeCell(FormatNumberOrNull(row.PreviousDps)),
                    EscapeCell(FormatNumberOrNull(row.Dps)),
                    EscapeCell(FormatNumberOrNull(row.DpsChangeRatio)),
                    EscapeCell(row.Status)));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        /// <summary>비교 CSV 한 행이다. 값이 없는 열은 null로 두고 쓸 때 빈 칸으로 만든다.</summary>
        private sealed class ComparisonRow
        {
            private readonly string _buildId;
            private readonly string _conditionId;
            private readonly double? _previousDps;
            private readonly double? _dps;
            private readonly double? _dpsChangeRatio;
            private readonly string _status;
            private readonly double _sortChangeRatio;

            public string BuildId => _buildId;
            public string ConditionId => _conditionId;
            public double? PreviousDps => _previousDps;
            public double? Dps => _dps;
            public double? DpsChangeRatio => _dpsChangeRatio;
            public string Status => _status;
            public double SortChangeRatio => _sortChangeRatio;

            /// <summary>식별자, 이전·현재 DPS, 변화율, 상태와 정렬용 변화율 절댓값으로 행을 만든다.</summary>
            public ComparisonRow(string buildId, string conditionId, double? previousDps, double? dps, double? dpsChangeRatio, string status, double sortChangeRatio)
            {
                _buildId = buildId;
                _conditionId = conditionId;
                _previousDps = previousDps;
                _dps = dps;
                _dpsChangeRatio = dpsChangeRatio;
                _status = status;
                _sortChangeRatio = sortChangeRatio;
            }
        }
    }
}
