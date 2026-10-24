using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

namespace RuneCode.Editor
{
    /// <summary>
    /// 측정 결과 묶음을 사람이 읽고 표로 비교할 수 있는 JSON·CSV 파일로 쓴다.
    /// CSV는 엑셀에서 한글이 깨지지 않도록 UTF-8 BOM을 두고 표준 따옴표 규칙을 따르며,
    /// 이전 측정 DPS를 받으면 행 끝에 이전 값과 변화율 열을 더한다.
    /// </summary>
    public static class DpsResultWriter
    {
        private const string CSV_HEADER = "buildId,conditionId,situation,distance,grade,chainLength,presets,status,warnings,compileErrors,"
            + "ramUsed,castCost,cooldownSeconds,castRange,"
            + "totalDamage,dps,burstDps,sustainedDps,statusDamage,hitCount,"
            + "energySpent,damagePerEnergy,castAttempts,castSuccesses,castFailures,firstFailureSeconds,bottleneck,"
            + "damagedEnemyCount,enemyDamageMin,enemyDamageMax,"
            + "behavior0Preset,behavior0Damage,behavior0Ratio,behavior1Preset,behavior1Damage,behavior1Ratio,behavior2Preset,behavior2Damage,behavior2Ratio,"
            + "peakSpellEntities,droppedExecutions,runCount,dpsMin,dpsMax";
        private const string PREVIOUS_DPS_HEADER = ",previousDps,dpsChangeRatio";
        private const string CSV_LINE_BREAK = "\r\n";
        private const string NUMBER_FORMAT = "0.###";
        private const string CSV_QUOTE = "\"";
        private const string CSV_DOUBLE_QUOTE = "\"\"";
        private const string LIST_SEPARATOR = "|";
        private const char CSV_SEPARATOR = ',';
        private const char GRADE_SEPARATOR = ':';
        private const int BEHAVIOR_COLUMN_COUNT = 3;

        private static readonly char[] CSV_SPECIAL_CHARACTERS = { CSV_SEPARATOR, '\"', '\r', '\n' };

        /// <summary>결과 묶음을 pretty JSON 파일로 쓴다. 파일 경로의 폴더가 없으면 만든다.</summary>
        public static void WriteJson(MeasurementResultSet set, string path)
        {
            WriteFile(path, JsonUtility.ToJson(set, true), new UTF8Encoding(false));
        }

        /// <summary>
        /// 결과 하나를 한 행으로 하는 CSV 파일을 UTF-8 BOM과 함께 쓴다. 상황·거리는 set.Suite의 조건에서,
        /// 프리셋은 buildId의 등급 구분자 뒤에서 찾는다. previousDps가 null이 아니면 행 끝에 previousDps와
        /// dpsChangeRatio를 더하며 키는 DpsResultKey(조합|조건)다. 이전 값이 없으면 두 칸을 비우고,
        /// 이전 값이 0이면 변화율만 비운다. 아니면 (dps - 이전 값) / 이전 값을 쓴다.
        /// </summary>
        public static void WriteCsv(MeasurementResultSet set, string path, IReadOnlyDictionary<string, double> previousDps)
        {
            var conditionsById = new Dictionary<string, MeasurementCondition>(StringComparer.Ordinal);
            foreach (MeasurementCondition condition in set.Suite.Conditions)
                conditionsById[condition.ConditionId] = condition;

            var csv = new StringBuilder();
            csv.Append(CSV_HEADER);
            if (previousDps != null) csv.Append(PREVIOUS_DPS_HEADER);
            csv.Append(CSV_LINE_BREAK);
            foreach (MeasurementResult result in set.Results)
                AppendRow(csv, conditionsById, result, previousDps);
            WriteFile(path, csv.ToString(), new UTF8Encoding(true));
        }

        /// <summary>결과 하나를 CSV 행으로 덧붙인다. 첫 칸을 제외한 모든 칸 앞에 쉼표를 붙인다.</summary>
        private static void AppendRow(StringBuilder csv, Dictionary<string, MeasurementCondition> conditionsById,
            MeasurementResult result, IReadOnlyDictionary<string, double> previousDps)
        {
            conditionsById.TryGetValue(result.ConditionId, out MeasurementCondition condition);
            csv.Append(EscapeCell(result.BuildId));
            AppendCell(csv, result.ConditionId);
            AppendCell(csv, condition != null ? condition.Situation : string.Empty);
            AppendCell(csv, condition != null ? FormatNumber(condition.Distance) : string.Empty);
            AppendCell(csv, result.Grade);
            AppendCell(csv, FormatInt(result.ChainLength));
            AppendCell(csv, GetPresets(result.BuildId));
            AppendCell(csv, result.Status);
            AppendCell(csv, string.Join(LIST_SEPARATOR, result.Warnings));
            AppendCell(csv, string.Join(LIST_SEPARATOR, result.CompileErrors));

            AppendCell(csv, FormatInt(result.RamUsed));
            AppendCell(csv, FormatNumber(result.CastCost));
            AppendCell(csv, FormatNumber(result.CooldownSeconds));
            AppendCell(csv, FormatNumber(result.CastRange));
            AppendCell(csv, FormatNumber(result.TotalDamage));
            AppendCell(csv, FormatNumber(result.Dps));
            AppendCell(csv, FormatNumber(result.BurstDps));
            AppendCell(csv, FormatNumber(result.SustainedDps));
            AppendCell(csv, FormatNumber(result.StatusDamage));
            AppendCell(csv, FormatInt(result.HitCount));
            AppendCell(csv, FormatNumber(result.EnergySpent));
            AppendCell(csv, FormatNumber(result.DamagePerEnergy));
            AppendCell(csv, FormatInt(result.CastAttempts));
            AppendCell(csv, FormatInt(result.CastSuccesses));
            AppendCell(csv, FormatInt(result.CastFailures));
            AppendCell(csv, FormatNumber(result.FirstFailureSeconds));
            AppendCell(csv, result.Bottleneck);
            AppendCell(csv, FormatInt(result.DamagedEnemyCount));
            AppendCell(csv, FormatNumber(result.EnemyDamageMin));
            AppendCell(csv, FormatNumber(result.EnemyDamageMax));

            for (int behaviorIndex = 0; behaviorIndex < BEHAVIOR_COLUMN_COUNT; behaviorIndex++)
                AppendBehaviorCells(csv, result, behaviorIndex);

            AppendCell(csv, FormatInt(result.PeakSpellEntities));
            AppendCell(csv, FormatInt(result.DroppedExecutions));
            AppendCell(csv, FormatInt(result.RunCount));
            AppendCell(csv, FormatNumber(result.DpsMin));
            AppendCell(csv, FormatNumber(result.DpsMax));

            if (previousDps != null) AppendPreviousDpsCells(csv, result, previousDps);
            csv.Append(CSV_LINE_BREAK);
        }

        /// <summary>체인 위치 하나의 프리셋·피해·비중 칸을 덧붙인다. 체인 길이보다 큰 위치나 결과에 없는 위치는 빈 칸으로 둔다.</summary>
        private static void AppendBehaviorCells(StringBuilder csv, MeasurementResult result, int behaviorIndex)
        {
            if (behaviorIndex >= result.ChainLength)
            {
                AppendCell(csv, string.Empty);
                AppendCell(csv, string.Empty);
                AppendCell(csv, string.Empty);
                return;
            }
            BehaviorDamageEntry entry = FindBehavior(result.BehaviorDamages, behaviorIndex);
            AppendCell(csv, entry != null ? entry.PresetId : string.Empty);
            AppendCell(csv, entry != null ? FormatNumber(entry.Damage) : string.Empty);
            AppendCell(csv, entry != null ? FormatNumber(entry.Ratio) : string.Empty);
        }

        /// <summary>이전 측정 DPS와 변화율 칸을 덧붙인다. 이전 값이 없거나 0이면 변화율은 빈 칸으로 둔다.</summary>
        private static void AppendPreviousDpsCells(StringBuilder csv, MeasurementResult result, IReadOnlyDictionary<string, double> previousDps)
        {
            bool hasPrevious = previousDps.TryGetValue(DpsResultKey.Create(result.BuildId, result.ConditionId), out double previousDpsValue);
            AppendCell(csv, hasPrevious ? FormatNumber(previousDpsValue) : string.Empty);
            AppendCell(csv, hasPrevious && previousDpsValue != 0
                ? FormatNumber((result.Dps - previousDpsValue) / previousDpsValue)
                : string.Empty);
        }

        /// <summary>체인 위치가 같은 행동 피해 항목을 찾아 반환한다. 없으면 null을 반환한다.</summary>
        private static BehaviorDamageEntry FindBehavior(IReadOnlyList<BehaviorDamageEntry> behaviorDamages, int chainIndex)
        {
            foreach (BehaviorDamageEntry entry in behaviorDamages)
                if (entry.ChainIndex == chainIndex) return entry;
            return null;
        }

        /// <summary>조합 ID("등급:A>B>C")에서 ':' 뒤의 프리셋 ID 목록('>' 연결)을 반환한다. 구분자가 없으면 빈 문자열을 반환한다.</summary>
        private static string GetPresets(string buildId)
        {
            int separatorIndex = buildId.IndexOf(GRADE_SEPARATOR);
            return separatorIndex >= 0 ? buildId.Substring(separatorIndex + 1) : string.Empty;
        }

        /// <summary>칸 하나를 쉼표와 함께 덧붙인다.</summary>
        private static void AppendCell(StringBuilder csv, string value)
        {
            csv.Append(CSV_SEPARATOR).Append(EscapeCell(value));
        }

        /// <summary>값에 쉼표·따옴표·줄바꿈이 있으면 따옴표로 감싸고 안쪽 따옴표는 두 번 쓴다.</summary>
        private static string EscapeCell(string value)
        {
            if (value.IndexOfAny(CSV_SPECIAL_CHARACTERS) < 0) return value;
            return CSV_QUOTE + value.Replace(CSV_QUOTE, CSV_DOUBLE_QUOTE) + CSV_QUOTE;
        }

        /// <summary>소수를 불변 문화권 고정 소수점("0.###") 문자열로 바꾼다.</summary>
        private static string FormatNumber(double value)
        {
            return value.ToString(NUMBER_FORMAT, CultureInfo.InvariantCulture);
        }

        /// <summary>정수를 불변 문화권 문자열로 바꾼다.</summary>
        private static string FormatInt(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>파일 경로의 폴더가 없으면 만들고 지정한 인코딩으로 내용을 쓴다.</summary>
        private static void WriteFile(string path, string content, Encoding encoding)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, content, encoding);
        }
    }
}
