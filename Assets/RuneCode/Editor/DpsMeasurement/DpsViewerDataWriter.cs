using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

namespace RuneCode.Editor
{
    /// <summary>
    /// 측정 결과 폴더 하나를 뷰어(Viewer~/dps_viewer.html)가 script 태그로 읽는 viewer_data.js로 묶고
    /// 측정 목록 index.js를 다시 쓴다. viewer_data.js는 CSV 원문과 측정 입력 JSON을 문자열로 담고
    /// 첫 줄 다음에 RebuildIndex가 읽는 메타 주석 한 줄을 둔다. 모든 파일을 UTF-8(BOM 없음)로 쓴다.
    /// </summary>
    public static class DpsViewerDataWriter
    {
        public const string VIEWER_DATA_FILE_NAME = "viewer_data.js";
        public const string INDEX_FILE_NAME = "index.js";

        private const string LOG_PREFIX = "[DpsMeasurement]";
        private const string RESULTS_JSON_FILE_NAME = "results.json";
        private const string RESULTS_CSV_FILE_NAME = "results.csv";
        private const string SUMMARY_CSV_FILE_NAME = "summary.csv";
        private const string EXCLUDED_CSV_FILE_NAME = "excluded.csv";
        private const string COMPARISON_CSV_FILE_NAME = "comparison.csv";
        private const string RUN_INDEX_PREFIX = "window.DPS_RUN_INDEX = ";
        private const string RUNS_HEADER = "window.DPS_RUNS = window.DPS_RUNS || {};";
        private const string META_PREFIX = "// DPS_RUN_META ";
        private const string ID_FIELD_NAME = "id";
        private const string JSON_NULL = "null";
        private const char BOM_CHARACTER = '\uFEFF';
        private const string LINE_BREAK = "\r\n";
        private const int META_LINE_SEARCH_LIMIT = 5;

        /// <summary>
        /// 측정 결과 묶음을 outputFolder/viewer_data.js로 쓴다. id는 outputFolder의 폴더 이름이고
        /// buildCount는 결과에 나오는 조합(buildId) 수, resultCount는 결과 행 수다.
        /// files의 results·summary·excluded·comparison은 outputFolder의 CSV 원문(파일이 없으면 null)이고
        /// suite는 측정 입력 JSON 원문이다. 첫 줄 다음에 index.js 항목으로 그대로 쓰는 메타 주석을 둔다.
        /// </summary>
        public static void Write(MeasurementResultSet set, string outputFolder)
        {
            string id = Path.GetFileName(outputFolder);
            int buildCount = CountDistinctBuilds(set);
            int resultCount = set.Results.Count;

            var runJson = new StringBuilder();
            runJson.Append("{\"id\":").Append(QuoteJsonString(id));
            runJson.Append(",\"createdAt\":").Append(QuoteJsonString(set.CreatedAt));
            runJson.Append(",\"dataVersion\":").Append(QuoteJsonString(set.DataVersion));
            runJson.Append(",\"buildCount\":").Append(buildCount.ToString(CultureInfo.InvariantCulture));
            runJson.Append(",\"resultCount\":").Append(resultCount.ToString(CultureInfo.InvariantCulture));
            runJson.Append(",\"files\":{");
            runJson.Append("\"results\":").Append(ReadCsvJsonValue(outputFolder, RESULTS_CSV_FILE_NAME));
            runJson.Append(",\"summary\":").Append(ReadCsvJsonValue(outputFolder, SUMMARY_CSV_FILE_NAME));
            runJson.Append(",\"excluded\":").Append(ReadCsvJsonValue(outputFolder, EXCLUDED_CSV_FILE_NAME));
            runJson.Append(",\"comparison\":").Append(ReadCsvJsonValue(outputFolder, COMPARISON_CSV_FILE_NAME));
            runJson.Append(",\"suite\":").Append(QuoteJsonString(JsonUtility.ToJson(set.Suite)));
            runJson.Append("}}");

            var content = new StringBuilder();
            content.Append(RUNS_HEADER).Append(LINE_BREAK);
            content.Append(META_PREFIX)
                .Append(BuildIndexEntryJson(id, set.CreatedAt, set.DataVersion, buildCount, resultCount))
                .Append(LINE_BREAK);
            content.Append("window.DPS_RUNS[").Append(QuoteJsonString(id)).Append("] = ")
                .Append(runJson).Append(';').Append(LINE_BREAK);
            WriteFile(Path.Combine(outputFolder, VIEWER_DATA_FILE_NAME), content.ToString());
        }

        /// <summary>기존 측정 폴더의 results.json을 읽어 그 폴더에 viewer_data.js를 다시 쓴다. CSV 원문은 폴더에서 함께 읽는다.</summary>
        public static void WriteFromFolder(string outputFolder)
        {
            MeasurementResultSet set = JsonUtility.FromJson<MeasurementResultSet>(
                File.ReadAllText(Path.Combine(outputFolder, RESULTS_JSON_FILE_NAME)));
            Write(set, outputFolder);
        }

        /// <summary>
        /// resultsRoot의 하위 폴더 중 viewer_data.js가 있는 폴더만 찾아 메타 주석의 JSON으로 resultsRoot/index.js를 새로 쓴다.
        /// 항목은 id 내림차순(최신이 첫 번째)으로 놓고 메타 줄이 없거나 해석할 수 없는 폴더는 경고를 남기고 건너뛴다.
        /// 각 viewer_data.js는 메타 줄까지만 읽어 큰 results.json을 다시 읽지 않는다.
        /// </summary>
        public static void RebuildIndex(string resultsRoot)
        {
            var entries = new List<KeyValuePair<string, string>>();
            foreach (string folder in Directory.GetDirectories(resultsRoot))
            {
                string viewerDataPath = Path.Combine(folder, VIEWER_DATA_FILE_NAME);
                if (!File.Exists(viewerDataPath)) continue;
                if (!TryReadMeta(viewerDataPath, out string metaJson, out string id))
                {
                    Debug.LogWarning(LOG_PREFIX + " 뷰어 데이터의 메타 줄이 없거나 깨져 건너뜁니다: " + viewerDataPath);
                    continue;
                }
                entries.Add(new KeyValuePair<string, string>(id, metaJson));
            }
            entries.Sort((first, second) => string.CompareOrdinal(second.Key, first.Key));

            var content = new StringBuilder();
            content.Append(RUN_INDEX_PREFIX).Append('[');
            for (int index = 0; index < entries.Count; index++)
            {
                content.Append(LINE_BREAK).Append(entries[index].Value);
                if (index < entries.Count - 1) content.Append(',');
            }
            if (entries.Count > 0) content.Append(LINE_BREAK);
            content.Append("];").Append(LINE_BREAK);
            WriteFile(Path.Combine(resultsRoot, INDEX_FILE_NAME), content.ToString());
        }

        /// <summary>index.js 항목 형식의 메타 JSON을 만든다. path는 id 아래 viewer_data.js 상대 경로다.</summary>
        private static string BuildIndexEntryJson(string id, string createdAt, string dataVersion, int buildCount, int resultCount)
        {
            var json = new StringBuilder();
            json.Append("{\"id\":").Append(QuoteJsonString(id));
            json.Append(",\"path\":").Append(QuoteJsonString(id + "/" + VIEWER_DATA_FILE_NAME));
            json.Append(",\"createdAt\":").Append(QuoteJsonString(createdAt));
            json.Append(",\"dataVersion\":").Append(QuoteJsonString(dataVersion));
            json.Append(",\"buildCount\":").Append(buildCount.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"resultCount\":").Append(resultCount.ToString(CultureInfo.InvariantCulture));
            json.Append('}');
            return json.ToString();
        }

        /// <summary>
        /// 문자열을 JSON 문자열 규칙으로 이스케이프해 앞뒤 따옴표와 함께 반환한다. 대상은 백슬래시와 따옴표,
        /// 제어 문자(\n \r \t, 그 밖의 U+0000~U+001F는 \u00XX), U+2028·U+2029다.
        /// </summary>
        private static string QuoteJsonString(string value)
        {
            var quoted = new StringBuilder(value.Length + 2).Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\': quoted.Append("\\\\"); break;
                    case '"': quoted.Append("\\\""); break;
                    case '\n': quoted.Append("\\n"); break;
                    case '\r': quoted.Append("\\r"); break;
                    case '\t': quoted.Append("\\t"); break;
                    default:
                        if (character < ' ' || character == '\u2028' || character == '\u2029')
                            quoted.Append("\\u").Append(((int)character).ToString("x4"));
                        else quoted.Append(character);
                        break;
                }
            }
            return quoted.Append('"').ToString();
        }

        /// <summary>outputFolder의 CSV 파일을 읽어 맨 앞 BOM을 뺀 원문을 JSON 문자열로 반환한다. 파일이 없으면 null 리터럴을 반환한다.</summary>
        private static string ReadCsvJsonValue(string outputFolder, string fileName)
        {
            string path = Path.Combine(outputFolder, fileName);
            if (!File.Exists(path)) return JSON_NULL;
            string text = File.ReadAllText(path);
            if (text.Length > 0 && text[0] == BOM_CHARACTER) text = text.Substring(1);
            return QuoteJsonString(text);
        }

        /// <summary>결과 묶음에 나오는 서로 다른 조합(buildId) 수를 센다.</summary>
        private static int CountDistinctBuilds(MeasurementResultSet set)
        {
            var buildIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (MeasurementResult result in set.Results) buildIds.Add(result.BuildId);
            return buildIds.Count;
        }

        /// <summary>viewer_data.js의 앞 줄에서 메타 주석을 찾아 JSON 원문과 id 값을 반환한다. 주석이 없거나 JSON 객체 형태가 아니거나 id가 없으면 false를 반환한다.</summary>
        private static bool TryReadMeta(string viewerDataPath, out string metaJson, out string id)
        {
            metaJson = null;
            id = null;
            using (StreamReader reader = new StreamReader(viewerDataPath))
            {
                for (int lineIndex = 0; lineIndex < META_LINE_SEARCH_LIMIT; lineIndex++)
                {
                    string line = reader.ReadLine();
                    if (line == null) return false;
                    if (!line.StartsWith(META_PREFIX, StringComparison.Ordinal)) continue;
                    metaJson = line.Substring(META_PREFIX.Length).Trim();
                    break;
                }
            }
            if (metaJson == null || metaJson.Length < 2 || metaJson[0] != '{' || metaJson[metaJson.Length - 1] != '}') return false;
            id = FindJsonStringFieldValue(metaJson, ID_FIELD_NAME);
            return !string.IsNullOrEmpty(id);
        }

        /// <summary>JSON 객체 원문에서 문자열 필드의 값 원문을 찾아 반환한다. 값의 이스케이프는 풀지 않고 필드가 없거나 형식이 깨지면 null을 반환한다.</summary>
        private static string FindJsonStringFieldValue(string json, string fieldName)
        {
            int keyIndex = json.IndexOf("\"" + fieldName + "\":", StringComparison.Ordinal);
            if (keyIndex < 0) return null;
            int cursor = keyIndex + fieldName.Length + 3;
            while (cursor < json.Length && char.IsWhiteSpace(json[cursor])) cursor++;
            if (cursor >= json.Length || json[cursor] != '"') return null;
            cursor++;
            int valueStart = cursor;
            while (cursor < json.Length)
            {
                char character = json[cursor];
                if (character == '\\')
                {
                    cursor += 2;
                    continue;
                }
                if (character == '"') return json.Substring(valueStart, cursor - valueStart);
                cursor++;
            }
            return null;
        }

        /// <summary>파일 경로의 폴더가 없으면 만들고 UTF-8(BOM 없음)로 내용을 쓴다.</summary>
        private static void WriteFile(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
