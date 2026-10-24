using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

namespace RuneCode.Editor
{
    /// <summary>
    /// 측정 입력(MeasurementSuite)의 등급·체인 길이·프리셋으로 측정할 조합(SpellBuild)을 모두 열거하고
    /// 금지 룬 프리셋 → 수동 제외 패턴 → 컴파일 오류 → 전력 한도 순서로 걸러낸다.
    /// 조합 컴파일이 GameData를 쓰므로 GameData.Load()는 호출자가 먼저 완료해야 한다.
    /// </summary>
    public static class DpsBuildGenerator
    {
        private const char WILDCARD = '*';
        private const string EXCLUDED_RUNE_PREFIX = "excludedRune:";
        private const string MANUAL_PREFIX = "manual:";
        private const string COMPILE_ERROR_PREFIX = "compileError:";
        private const string OVER_ENERGY_PREFIX = "overEnergy:";
        private const string ENERGY_COST_FORMAT = "F2";
        private const string COMPILE_ERROR_SEPARATOR = ";";
        private const string BUILDS_FILE_NAME = "builds.json";
        private const string EXCLUDED_FILE_NAME = "excluded.csv";
        private const string CSV_HEADER = "buildId,reason";
        private const char CSV_SEPARATOR = ',';
        private const char CSV_QUOTE = '\"';

        /// <summary>
        /// 측정 입력과 수동 제외 목록으로 측정할 조합 목록을 만들어 반환한다. GameData.Load()는 호출자가 먼저 완료했다고 가정한다.
        /// suite.Grades × suite.ChainLengths × 프리셋 중복 허용 순열(프리셋 목록 순서, 마지막 자리가 가장 빨리 바뀌는 사전식 순서)로
        /// 열거한 뒤 각 조합을 판정 순서대로 검사해 처음 걸린 사유 하나만 DpsExcludedBuild로 기록하고 통과한 조합만 측정 대상에 둔다.
        /// 프리셋이 없거나 체인 길이가 1 미만인 값은 만들어지는 조합이 없어 건너뛰고, 프리셋 ID가 중복이면 InvalidOperationException을 던진다.
        /// </summary>
        public static DpsGenerationResult Generate(MeasurementSuite suite, DpsExclusionList exclusions)
        {
            Dictionary<string, string> excludedPresets = BuildPresetExclusions(suite);
            var builds = new List<SpellBuild>();
            var excludedBuilds = new List<DpsExcludedBuild>();
            int totalCount = 0;
            // 프리셋이 없으면 어떤 체인 길이로도 조합을 만들 수 없어 바로 빈 결과를 반환한다.
            if (suite.Presets.Count == 0) return new DpsGenerationResult(builds, excludedBuilds, totalCount);
            foreach (string grade in suite.Grades)
            {
                foreach (int chainLength in suite.ChainLengths)
                {
                    if (chainLength < 1) continue;
                    var presetIds = new string[chainLength];
                    var indexes = new int[chainLength];
                    // 마지막 자리가 가장 빨리 바뀌는 계수기로 중복 허용 순열을 센다.
                    while (true)
                    {
                        for (int index = 0; index < chainLength; index++)
                            presetIds[index] = suite.Presets[indexes[index]].PresetId;
                        SpellBuild build = new SpellBuild(grade, presetIds);
                        totalCount++;
                        string reason = Classify(build, suite, exclusions, excludedPresets);
                        if (reason != null) excludedBuilds.Add(new DpsExcludedBuild(build.BuildId, reason));
                        else builds.Add(build);

                        int position = chainLength - 1;
                        while (position >= 0 && ++indexes[position] >= suite.Presets.Count) indexes[position--] = 0;
                        if (position < 0) break;
                    }
                }
            }
            return new DpsGenerationResult(builds, excludedBuilds, totalCount);
        }

        /// <summary>
        /// 측정 대상 조합을 folder의 builds.json으로, 제외 항목을 excluded.csv로 쓴다.
        /// builds.json은 DpsBuildList의 pretty JSON이고 excluded.csv는 buildId,reason 헤더 행을 둔다. 폴더가 없으면 만든다.
        /// </summary>
        public static void WriteFiles(DpsGenerationResult result, string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, BUILDS_FILE_NAME),
                JsonUtility.ToJson(new DpsBuildList(result.Builds), true));
            var csv = new StringBuilder();
            csv.AppendLine(CSV_HEADER);
            foreach (DpsExcludedBuild excluded in result.ExcludedBuilds)
                csv.AppendLine(excluded.BuildId + CSV_SEPARATOR + QuoteReason(excluded.Reason));
            File.WriteAllText(Path.Combine(folder, EXCLUDED_FILE_NAME), csv.ToString());
        }

        /// <summary>
        /// 금지 룬이 들어 있는 프리셋을 프리셋 ID → 사유 조회표로 만들어 반환한다. BehaviorId·ModifierIds는 룬 ID 그대로,
        /// ElementId는 RuneCatalog.TryGetElement로 찾은 정의의 룬 ID를 suite.ExcludedRuneIds와 비교하며 프리셋마다
        /// 처음 걸린 룬 하나만 사유로 남긴다. 프리셋 ID가 중복이면 InvalidOperationException을 던진다.
        /// </summary>
        private static Dictionary<string, string> BuildPresetExclusions(MeasurementSuite suite)
        {
            var excludedRuneIds = new HashSet<string>(suite.ExcludedRuneIds, StringComparer.Ordinal);
            var excludedPresets = new Dictionary<string, string>(StringComparer.Ordinal);
            var seenPresetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BehaviorPreset preset in suite.Presets)
            {
                if (!seenPresetIds.Add(preset.PresetId))
                    throw new InvalidOperationException("측정 입력의 프리셋 ID가 중복됩니다: " + preset.PresetId);
                string reason = FindExcludedRune(preset, excludedRuneIds);
                if (reason != null) excludedPresets.Add(preset.PresetId, reason);
            }
            return excludedPresets;
        }

        /// <summary>프리셋 구성에서 suite가 금지한 룬을 행동 → 모디 → 속성 순서로 찾아 사유를 반환한다. 없으면 null을 반환한다.</summary>
        private static string FindExcludedRune(BehaviorPreset preset, HashSet<string> excludedRuneIds)
        {
            if (excludedRuneIds.Contains(preset.BehaviorId)) return EXCLUDED_RUNE_PREFIX + preset.BehaviorId;
            foreach (string modifierId in preset.ModifierIds)
                if (excludedRuneIds.Contains(modifierId)) return EXCLUDED_RUNE_PREFIX + modifierId;
            if (!string.IsNullOrEmpty(preset.ElementId) && GameData.Runes.TryGetElement(preset.ElementId, out ElementDefinition element)
                && excludedRuneIds.Contains(element.RuneId))
                return EXCLUDED_RUNE_PREFIX + element.RuneId;
            return null;
        }

        /// <summary>조합 하나를 판정 순서대로 검사해 처음 걸린 제외 사유를 반환한다. 측정 대상이면 null을 반환한다.</summary>
        private static string Classify(SpellBuild build, MeasurementSuite suite, DpsExclusionList exclusions,
            Dictionary<string, string> excludedPresets)
        {
            foreach (string presetId in build.PresetIds)
                if (excludedPresets.TryGetValue(presetId, out string runeReason)) return runeReason;
            foreach (DpsExclusionEntry entry in exclusions.Entries)
                if (MatchesPattern(build.BuildId, entry.Pattern)) return MANUAL_PREFIX + entry.Reason;

            CompileResult compile = SpellBuildCompiler.Compile(build, suite);
            if (!compile.Ok)
            {
                var codes = new string[compile.Errors.Count];
                for (int index = 0; index < compile.Errors.Count; index++)
                    codes[index] = compile.Errors[index].Code;
                return COMPILE_ERROR_PREFIX + string.Join(COMPILE_ERROR_SEPARATOR, codes);
            }
            float energyCost = compile.Spell.EnergyCost;
            // 무한 비용(빔이 첫 행동인 체인)은 슬롯 주기로 유지되므로 한도 초과로 보지 않는다.
            if (float.IsFinite(energyCost) && energyCost > suite.Profile.MaxEnergy)
                return OVER_ENERGY_PREFIX + energyCost.ToString(ENERGY_COST_FORMAT, CultureInfo.InvariantCulture);
            return null;
        }

        /// <summary>값이 제외 패턴과 일치하는지 반환한다. '*'는 빈 문자열을 포함한 임의 문자열에 대응하고 나머지 문자는 정확히 일치해야 한다.</summary>
        internal static bool MatchesPattern(string value, string pattern)
        {
            int firstWildcard = pattern.IndexOf(WILDCARD);
            if (firstWildcard < 0) return value == pattern;
            if (!value.StartsWith(pattern.Substring(0, firstWildcard), StringComparison.Ordinal)) return false;
            int valueCursor = firstWildcard;
            int segmentStart = firstWildcard + 1;
            while (true)
            {
                int nextWildcard = pattern.IndexOf(WILDCARD, segmentStart);
                string segment = nextWildcard < 0 ? pattern.Substring(segmentStart)
                    : pattern.Substring(segmentStart, nextWildcard - segmentStart);
                if (nextWildcard < 0)
                {
                    // 마지막 구역은 값의 끝에 정확히 이어져야 한다.
                    int tailStart = value.Length - segment.Length;
                    return tailStart >= valueCursor && string.CompareOrdinal(value, tailStart, segment, 0, segment.Length) == 0;
                }
                if (segment.Length > 0)
                {
                    int found = value.IndexOf(segment, valueCursor, StringComparison.Ordinal);
                    if (found < 0) return false;
                    valueCursor = found + segment.Length;
                }
                segmentStart = nextWildcard + 1;
            }
        }

        /// <summary>CSV 사유 값에 쉼표가 있으면 따옴표로 감싸 반환한다.</summary>
        private static string QuoteReason(string reason)
        {
            return reason.IndexOf(CSV_SEPARATOR) >= 0 ? CSV_QUOTE + reason + CSV_QUOTE : reason;
        }
    }
}
