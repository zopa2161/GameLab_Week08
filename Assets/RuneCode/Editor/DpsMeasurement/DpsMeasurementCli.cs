using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

using UnityEngine;

using UnityEditor;

namespace RuneCode.Editor
{
    /// <summary>
    /// DPS 측정 일괄 실행의 진입점이다. 배치 명령행(-executeMethod RuneCode.Editor.DpsMeasurementCli.Run)과
    /// 에디터 메뉴가 같은 내부 실행 흐름을 공유하고 출력 폴더에 결과 파일을 쓴다. 에디터 종료(Exit)는 배치에서만 호출한다.
    /// </summary>
    public static class DpsMeasurementCli
    {
        private const string LOG_PREFIX = "[DpsMeasurement]";
        private const string SUITE_PATH_DEFAULT = "Assets/RuneCode/Editor/DpsMeasurement/Data/dps_suite.json";
        private const string EXCLUSIONS_PATH_DEFAULT = "Assets/RuneCode/Editor/DpsMeasurement/Data/dps_exclusions.json";
        private const string OUTPUT_ROOT_FOLDER = "DpsMeasurement";
        private const string TIMESTAMP_FORMAT = "yyyyMMdd-HHmmss";
        private const string RESULTS_JSON_FILE_NAME = "results.json";
        private const string RESULTS_CSV_FILE_NAME = "results.csv";
        private const string SUMMARY_CSV_FILE_NAME = "summary.csv";
        private const string COMPARISON_CSV_FILE_NAME = "comparison.csv";
        private const string ARG_SUITE = "-dpsSuite";
        private const string ARG_EXCLUSIONS = "-dpsExclusions";
        private const string ARG_OUT = "-dpsOut";
        private const string ARG_COMPARE = "-dpsCompare";
        private const string ARG_RESUME = "-dpsResume";
        private const string ARG_FILTER = "-dpsFilter";
        private const string ARG_LIMIT = "-dpsLimit";
        private const string MENU_RUN_ALL = "Tools/RuneCode/DPS Measurement/Run All";
        private const string MENU_RUN_SAMPLE = "Tools/RuneCode/DPS Measurement/Run Sample (20 builds)";
        private const string MENU_RESUME = "Tools/RuneCode/DPS Measurement/Resume...";
        private const int SAMPLE_BUILD_COUNT = 20;
        private const int PROGRESS_LOG_INTERVAL = 100;
        private const double ESTIMATED_SECONDS_PER_JOB = 0.3;

        /// <summary>
        /// 배치 진입점이다. 명령행 인수 -dpsSuite, -dpsExclusions, -dpsOut, -dpsCompare, -dpsResume, -dpsFilter, -dpsLimit으로
        /// 입력·출력을 바꿀 수 있고 끝나면 Exit(0), 예외가 나면 LogException 후 Exit(1)로 에디터를 종료한다.
        /// </summary>
        public static void Run()
        {
            try
            {
                string[] arguments = Environment.GetCommandLineArgs();
                string suitePath = Argument(arguments, ARG_SUITE, SUITE_PATH_DEFAULT);
                string exclusionsPath = Argument(arguments, ARG_EXCLUSIONS, EXCLUSIONS_PATH_DEFAULT);
                string comparePath = Argument(arguments, ARG_COMPARE, null);
                string filterPattern = Argument(arguments, ARG_FILTER, null);
                int limit = ParseLimit(Argument(arguments, ARG_LIMIT, null));
                string resumeFolder = Argument(arguments, ARG_RESUME, null);
                bool isResume = resumeFolder != null;
                string outputFolder = isResume ? resumeFolder : Argument(arguments, ARG_OUT, DefaultOutputFolder());
                var timer = Stopwatch.StartNew();
                Func<int, int, bool> onProgress = (done, total) =>
                {
                    if (total > 0 && (done % PROGRESS_LOG_INTERVAL == 0 || done == total)) LogProgress(done, total, timer.Elapsed);
                    return true;
                };
                ExecuteRun(PrepareBuilds(suitePath, exclusionsPath, filterPattern, limit), outputFolder, isResume, comparePath, onProgress);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>전체 조합을 측정하는 에디터 메뉴다. 확인 대화상자를 통과하면 취소 가능한 진행 바로 실행하고 끝나면 출력 폴더를 연다.</summary>
        [MenuItem(MENU_RUN_ALL)]
        public static void RunAllFromMenu()
        {
            RunFromMenu(0, null);
        }

        /// <summary>앞 20개 조합만 측정하는 에디터 메뉴다. 진행 방식은 Run All과 같다.</summary>
        [MenuItem(MENU_RUN_SAMPLE)]
        public static void RunSampleFromMenu()
        {
            RunFromMenu(SAMPLE_BUILD_COUNT, null);
        }

        /// <summary>이전 출력 폴더를 골라 측정을 이어서 진행하는 에디터 메뉴다. 폴더를 고르지 않으면 아무 것도 하지 않는다.</summary>
        [MenuItem(MENU_RESUME)]
        public static void ResumeFromMenu()
        {
            string resumeFolder = EditorUtility.OpenFolderPanel("DPS 측정 출력 폴더 선택", Path.Combine(Application.persistentDataPath, OUTPUT_ROOT_FOLDER), "");
            if (string.IsNullOrEmpty(resumeFolder)) return;
            RunFromMenu(0, resumeFolder);
        }

        /// <summary>
        /// 에디터 메뉴 공용 실행 흐름이다. 조합을 준비하고 확인 대화상자를 통과하면 취소 가능한 진행 바로 측정해 그때까지의 결과를 쓴 뒤
        /// 출력 폴더를 탐색기에서 연다. 진행 바는 실행을 중단하거나 예외가 나도 finally에서 정리한다.
        /// </summary>
        private static void RunFromMenu(int limit, string resumeFolder)
        {
            try
            {
                bool isResume = resumeFolder != null;
                string outputFolder = isResume ? resumeFolder : DefaultOutputFolder();
                RunPreparation preparation = PrepareBuilds(SUITE_PATH_DEFAULT, EXCLUSIONS_PATH_DEFAULT, null, limit);
                if (!ConfirmRun(preparation, isResume)) return;
                ExecuteRun(preparation, outputFolder, isResume, null, CreateCancelableProgress());
                EditorUtility.RevealInFinder(outputFolder);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// 게임 데이터를 읽고 측정 입력·제외 목록을 로드한 뒤 조합을 생성해 와일드카드 필터와 개수 한도를 적용한다.
        /// 제외 목록 파일이 없으면 빈 규칙으로 진행한다.
        /// </summary>
        private static RunPreparation PrepareBuilds(string suitePath, string exclusionsPath, string filterPattern, int limit)
        {
            GameData.Load();
            MeasurementSuite suite = JsonUtility.FromJson<MeasurementSuite>(File.ReadAllText(suitePath));
            DpsExclusionList exclusions = DpsBuildGenerator.LoadExclusions(exclusionsPath);
            DpsGenerationResult generated = DpsBuildGenerator.Generate(suite, exclusions);
            return new RunPreparation(suite, generated, ApplyFilterAndLimit(generated.Builds, filterPattern, limit));
        }

        /// <summary>
        /// 조합 목록 파일을 출력 폴더에 쓰고 일괄 측정을 실행한 뒤 results.json, results.csv, summary.csv를 쓴다.
        /// comparePath가 있으면 이전 results.json에서 이전 DPS를 읽어 results.csv 비교 열에 넣고 comparison.csv도 쓴다.
        /// isResume이면 출력 폴더의 부분 결과를 이어받는다.
        /// </summary>
        private static void ExecuteRun(RunPreparation preparation, string outputFolder, bool isResume, string comparePath, Func<int, int, bool> onProgress)
        {
            DpsBuildGenerator.WriteFiles(preparation.Generated, outputFolder);
            MeasurementResultSet results = DpsBatchRunner.Run(preparation.Suite, preparation.Builds, outputFolder, isResume, onProgress);
            IReadOnlyDictionary<string, double> previousDps = comparePath == null ? null : DpsResultComparer.LoadPreviousDps(comparePath);
            DpsResultWriter.WriteJson(results, Path.Combine(outputFolder, RESULTS_JSON_FILE_NAME));
            DpsResultWriter.WriteCsv(results, Path.Combine(outputFolder, RESULTS_CSV_FILE_NAME), previousDps);
            DpsSummaryWriter.Write(results, Path.Combine(outputFolder, SUMMARY_CSV_FILE_NAME));
            if (comparePath != null) DpsResultComparer.WriteComparisonCsv(results, previousDps, Path.Combine(outputFolder, COMPARISON_CSV_FILE_NAME));
            UnityEngine.Debug.Log(LOG_PREFIX + " 출력 폴더: " + outputFolder);
        }

        /// <summary>빌드 ID 와일드카드 패턴으로 걸러낸 뒤 앞에서부터 limit개만 남긴다. 패턴이 null이면 걸러내지 않고 limit이 0 이하면 개수를 제한하지 않는다.</summary>
        private static List<SpellBuild> ApplyFilterAndLimit(IReadOnlyList<SpellBuild> builds, string filterPattern, int limit)
        {
            var filtered = new List<SpellBuild>();
            foreach (SpellBuild build in builds)
                if (filterPattern == null || DpsBuildGenerator.MatchesPattern(build.BuildId, filterPattern)) filtered.Add(build);
            if (limit > 0 && filtered.Count > limit) filtered.RemoveRange(limit, filtered.Count - limit);
            return filtered;
        }

        /// <summary>에디터 진행 바를 표시하는 콜백을 만든다. 사용자가 취소하면 false를 반환해 측정을 중단한다.</summary>
        private static Func<int, int, bool> CreateCancelableProgress()
        {
            return (done, total) => !EditorUtility.DisplayCancelableProgressBar("DPS 측정",
                LOG_PREFIX + " " + done + "/" + total, total == 0 ? 0f : done / (float)total);
        }

        /// <summary>조합 수 × 조건 수와 작업당 약 0.3초 추정으로 예상 시간을 안내하고 실행 여부를 묻는다.</summary>
        private static bool ConfirmRun(RunPreparation preparation, bool isResume)
        {
            int jobCount = preparation.Builds.Count * preparation.Suite.Conditions.Count;
            string estimatedTime = FormatDuration(TimeSpan.FromSeconds(jobCount * ESTIMATED_SECONDS_PER_JOB));
            string message = (isResume ? "선택한 폴더에서 측정을 이어서 진행합니다" : "새 출력 폴더에 측정을 진행합니다")
                + ".\n조합 " + preparation.Builds.Count + "개 × 조건 " + preparation.Suite.Conditions.Count + "개 = 총 " + jobCount
                + "회, 예상 시간 약 " + estimatedTime + ".\n계속할까요?";
            return EditorUtility.DisplayDialog("DPS 측정", message, "실행", "취소");
        }

        /// <summary>완료 수·총수·퍼센트·경과 시간·남은 예상 시간을 로그로 남긴다. 남은 예상은 완료 수가 0보다 클 때 계산한다.</summary>
        private static void LogProgress(int done, int total, TimeSpan elapsed)
        {
            double ratio = (double)done / total;
            TimeSpan remaining = TimeSpan.FromSeconds(elapsed.TotalSeconds * (total - done) / done);
            UnityEngine.Debug.Log(LOG_PREFIX + " " + done + "/" + total + " (" + Math.Round(ratio * 100) + "%) 경과 "
                + FormatDuration(elapsed) + " 남은 예상 " + FormatDuration(remaining));
        }

        /// <summary>시간을 "hh:mm:ss" 문자열로 만든다. 시간 자릿수는 100시간을 넘으면 늘어난다.</summary>
        private static string FormatDuration(TimeSpan time)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", (int)time.TotalHours, time.Minutes, time.Seconds);
        }

        /// <summary>-dpsLimit 값을 정수로 바꾼다. 값이 없으면 0(제한 없음)을 반환하고 형식이 틀리면 예외로 배치를 실패시킨다.</summary>
        private static int ParseLimit(string text)
        {
            if (text == null) return 0;
            return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        /// <summary>명령행에서 옵션 이름 다음의 인수를 반환하고 없으면 기본값을 반환한다.</summary>
        private static string Argument(string[] arguments, string name, string fallback)
        {
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : fallback;
        }

        /// <summary>기본 출력 폴더를 persistentDataPath/DpsMeasurement/현재 로컬 시각 타임스탬프 경로로 만든다.</summary>
        private static string DefaultOutputFolder()
        {
            return Path.Combine(Application.persistentDataPath, OUTPUT_ROOT_FOLDER,
                DateTime.Now.ToString(TIMESTAMP_FORMAT, CultureInfo.InvariantCulture));
        }

        /// <summary>측정 실행 준비 결과다. 측정 입력, 생성 결과 전체와 필터·한도가 적용된 측정 대상을 보관한다.</summary>
        private sealed class RunPreparation
        {
            private readonly MeasurementSuite _suite;
            private readonly DpsGenerationResult _generated;
            private readonly List<SpellBuild> _builds;

            public MeasurementSuite Suite => _suite;
            public DpsGenerationResult Generated => _generated;
            public List<SpellBuild> Builds => _builds;

            /// <summary>측정 입력, 생성 결과와 측정 대상 목록으로 준비 결과를 만든다.</summary>
            public RunPreparation(MeasurementSuite suite, DpsGenerationResult generated, List<SpellBuild> builds)
            {
                _suite = suite;
                _generated = generated;
                _builds = builds;
            }
        }
    }
}
