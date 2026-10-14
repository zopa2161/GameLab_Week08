using System;
using System.Collections.Generic;
using System.Text;

using UnityEngine;

namespace RuneCode
{
    public static class ShareCodec
    {
        private const int MAX_JSON_LENGTH = 131072;
        private const string PREFIX_V1 = "RC1.";
        private const string PREFIX_V2 = "RC2.";

        /// <summary>마법 그래프를 저장 또는 공유 코드에 사용할 JSON 문자열로 직렬화한다. 형식은 SpellGraphData를 따른다.</summary>
        public static string Serialize(SpellGraph graph, bool pretty = false) => JsonUtility.ToJson(SpellGraphData.From(graph), pretty);

        /// <summary>크기·필수 필드·ID·숫자·스키마 버전을 검증한 JSON 그래프를 반환한다.</summary>
        public static SpellGraph Deserialize(string json)
        {
            return Deserialize(json, GameData.IsLoaded ? GameData.ModifierGrades : null);
        }

        /// <summary>JSON 그래프를 검사하고 현재 스키마 버전이 아니면 실패한다.</summary>
        public static SpellGraph Deserialize(string json, ModifierGradeTable modifierGrades)
        {
            string trimmed = json?.TrimStart();
            if (string.IsNullOrEmpty(trimmed) || json.Length > MAX_JSON_LENGTH || trimmed[0] != '{')
                throw new FormatException(GameData.L("share.invalid"));
            SpellGraph graph = JsonUtility.FromJson<SpellGraphData>(json)?.ToGraph();
            if (!SpellGraphValidator.IsValid(graph) || graph.Version != SpellGraph.CURRENT_VERSION)
                throw new FormatException(GameData.L("share.invalid"));
            return graph;
        }

        /// <summary>그래프 JSON을 스키마 버전에 맞는 RC1 또는 RC2 접두사 공유 코드로 인코딩한다.</summary>
        public static string Encode(SpellGraph graph)
        {
            string json = Serialize(graph);
            if (json.Length > MAX_JSON_LENGTH) throw new FormatException(GameData.L("share.tooLarge"));
            string prefix = graph.Version == 1 ? PREFIX_V1 : PREFIX_V2;
            return prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>공유 코드의 형식·크기·그래프 연결을 검증하고 실패 시 사용자 오류를 반환한다.</summary>
        public static bool TryDecode(string code, out SpellGraph graph, out string error, IEnumerable<SpellGraph> library = null)
        {
            graph = null;
            error = null;
            try
            {
                code = code?.Trim();
                string prefix = code != null && code.StartsWith(PREFIX_V2, StringComparison.Ordinal) ? PREFIX_V2 : PREFIX_V1;
                if (code == null || !code.StartsWith(prefix, StringComparison.Ordinal) || code.Length > MAX_JSON_LENGTH * 2)
                    throw new FormatException(GameData.L("share.invalid"));
                string payload = code.Substring(prefix.Length);
                foreach (char value in payload)
                    if (!((value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z')
                        || (value >= '0' && value <= '9')) && value != '-' && value != '_')
                        throw new FormatException(GameData.L("share.invalid"));
                string base64 = payload.Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
                string json = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(base64));
                graph = Deserialize(json, GameData.IsLoaded ? GameData.ModifierGrades : null);
                if (GameData.IsLoaded)
                {
                    List<string> allRunes = new List<string>();
                    foreach (RuneDefinition rune in GameData.Runes.All) allRunes.Add(rune.Id);
                    CompileResult result = GraphCompiler.Compile(graph, GameData.Runes, GameData.Balance.Grammar,
                        allRunes, int.MaxValue, float.MaxValue, library);
                    if (!result.Ok) throw new FormatException(CompileIssueText.Format(result.Errors[0]));
                }
                return true;
            }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException || exception is OverflowException)
            {
                graph = null;
                error = exception is FormatException ? exception.Message : GameData.L("share.invalid");
                return false;
            }
        }
    }
}
