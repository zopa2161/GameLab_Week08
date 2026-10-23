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
    /// 에디터 메뉴가 같은 내부 실행 흐름을 공유하고 결과를 프로젝트 안 Results~ 폴더에 쓴 뒤 뷰어 데이터를 갱신한다.
    /// 측정 조합은 Data/Sets의 조합 셋(DpsBuildSet)에서 만들고 메뉴는 기본 셋(full)을, 조합 셋 편집 창은 고른 셋을 쓴다.
    /// RebuildViewerData는 저장된 측정 폴더의 뷰어 데이터를 다시 만드는 배치 진입점이다. 에디터 종료(Exit)는 배치에서만 호출한다.
    /// </summary>
    public static class DpsMeasurementCli
    {
        private const string LOG_PREFIX = "[DpsMeasurement]";
        private const string RESULTS_ROOT = "Assets/RuneCode/Editor/DpsMeasurement/Results~";
        private const string VIEWER_PATH = "Assets/RuneCode/Editor/DpsMeasurement/Viewer~/dps_viewer.html";
        private const string TIMESTAMP_FORMAT = "yyyyMMdd-HHmmss";
        private const string RESULTS_JSON_FILE_NAME = "results.json";
        private const string RESULTS_CSV_FILE_NAME = "results.csv";
        private const string SUMMARY_CSV_FILE_NAME = "summary.csv";
        private const string COMPARISON_CSV_FILE_NAME = "comparison.csv";
        private const string ARG_SET = "-dpsSet";
        private const string ARG_OUT = "-dpsOut";
        private const string ARG_COMPARE = "-dpsCompare";
        private const string ARG_RESUME = "-dpsResume";
        private const string ARG_FILTER = "-dpsFilter";
        private const string ARG_LIMIT = "-dpsLimit";
        private const string ARG_FORCE = "-dpsForce";
        private const string MENU_RUN_ALL = "Tools/RuneCode/DPS Measurement/Run All";
        private const string MENU_RUN_SAMPLE = "Tools/RuneCode/DPS Measurement/Run Sample (20 builds)";
        private const string MENU_RESUME = "Tools/RuneCode/DPS Measurement/Resume...";
        private const string MENU_OPEN_VIEWER = "Tools/RuneCode/DPS Measurement/Open Viewer";
        private const string MENU_REBUILD_VIEWER_DATA = "Tools/RuneCode/DPS Measurement/Rebuild Viewer Data";
        private const int SAMPLE_BUILD_COUNT = 20;
        private const int PROGRESS_LOG_INTERVAL = 100;
        private const double ESTIMATED_SECONDS_PER_JOB = 0.3;

        /// <summary>
        /// 배치 진입점이다. 명령행 인수 -dpsSet(조합 셋 이름, 기본 full), -dpsOut, -dpsCompare, -dpsResume, -dpsFilter, -dpsLimit으로
        /// 입력·출력을 바꿀 수 있고 끝나면 Exit(0), 예외가 나면 LogException 후 Exit(1)로 에디터를 종료한다.
        /// -dpsResume이면 그 폴더의 셋 사본을 쓰고 -dpsSet은 무시한다.
        /// </summary>
        public static void Run()
        {
            try
            {
                string[] arguments = Environment.GetCommandLineArgs();
                string setName = Argument(arguments, ARG_SET, DpsBuildSetStore.DEFAULT_SET_NAME);
                string comparePath = Argument(arguments, ARG_COMPARE, null);
                string filterPattern = Argument(arguments, ARG_FILTER, null);
                int limit = ParseLimit(Argument(arguments, ARG_LIMIT, null));
                string resumeFolder = Argument(arguments, ARG_RESUME, null);
                bool isResume = resumeFolder != null;
                string outputFolder = isResume ? resumeFolder : Argument(arguments, ARG_OUT, DefaultOutputFolder(setName));
                var timer = Stopwatch.StartNew();
                Func<int, int, bool> onProgress = (done, total) =>
                {
                    if (total > 0 && (done % PROGRESS_LOG_INTERVAL == 0 || done == total)) LogProgress(done, total, timer.Elapsed);
                    return true;
                };
                DpsBuildSet set = isResume ? LoadResumeSet(resumeFolder) : DpsBuildSetStore.Load(setName);
                ExecuteRun(PrepareBuilds(set, filterPattern, limit), outputFolder, isResume, comparePath, onProgress);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// 뷰어 데이터 재생성 배치 진입점이다. 명령행에 -dpsForce가 있으면 viewer_data.js가 이미 있는 측정 폴더도 다시 만들고
        /// 끝나면 Exit(0), 예외가 나면 LogException 후 Exit(1)로 에디터를 종료한다.
        /// </summary>
        public static void RebuildViewerData()
        {
            try
            {
                RebuildViewerData(Array.IndexOf(Environment.GetCommandLineArgs(), ARG_FORCE) >= 0);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>기본 조합 셋(full)의 전체 조합을 측정하는 에디터 메뉴다. 확인 대화상자를 통과하면 취소 가능한 진행 바로 실행하고 끝나면 뷰어를 연다.</summary>
        [MenuItem(MENU_RUN_ALL)]
        public static void RunAllFromMenu()
        {
            RunSetFromEditor(DpsBuildSetStore.DEFAULT_SET_NAME, 0);
        }

        /// <summary>기본 조합 셋(full)의 앞 20개 조합만 측정하는 에디터 메뉴다. 진행 방식은 Run All과 같다.</summary>
        [MenuItem(MENU_RUN_SAMPLE)]
        public static void RunSampleFromMenu()
        {
            RunSetFromEditor(DpsBuildSetStore.DEFAULT_SET_NAME, SAMPLE_BUILD_COUNT);
        }

        /// <summary>
        /// 이전 측정 폴더를 Results~에서 골라 측정을 이어서 진행하는 에디터 메뉴다. 그 폴더의 셋 사본으로 조합을 다시 만들고
        /// 사본이 없는 예전 측정이면 기본 셋을 쓴다. 폴더를 고르지 않으면 아무 것도 하지 않는다.
        /// </summary>
        [MenuItem(MENU_RESUME)]
        public static void ResumeFromMenu()
        {
            string resumeFolder = EditorUtility.OpenFolderPanel("DPS 측정 출력 폴더 선택", ProjectPath(RESULTS_ROOT), "");
            if (string.IsNullOrEmpty(resumeFolder)) return;
            RunFromMenu(LoadResumeSet(resumeFolder), 0, resumeFolder, true);
        }

        /// <summary>
        /// 조합 셋 이름으로 새 측정을 실행한다. limit이 0보다 크면 앞에서부터 limit개 조합만 측정한다.
        /// 결과는 Results~/시각-셋이름 폴더에 쓰고 끝나면 뷰어를 연다. 조합 셋 편집 창과 메뉴가 함께 쓴다.
        /// </summary>
        internal static void RunSetFromEditor(string setName, int limit)
        {
            RunFromMenu(DpsBuildSetStore.Load(setName), limit, DefaultOutputFolder(setName), false);
        }

        /// <summary>조합 셋 하나에 들어갈 측정 작업 수(조합 수 × 조건 수)로 예상 시간 문자열을 만든다. 편집 창 미리보기에서 쓴다.</summary>
        internal static string EstimateDuration(int buildCount, int conditionCount)
        {
            return FormatDuration(TimeSpan.FromSeconds(buildCount * conditionCount * ESTIMATED_SECONDS_PER_JOB));
        }

        /// <summary>측정 결과 뷰어를 시스템 기본 브라우저로 여는 에디터 메뉴다.</summary>
        [MenuItem(MENU_OPEN_VIEWER)]
        public static void OpenViewerFromMenu()
        {
            OpenViewer();
        }

        /// <summary>저장된 측정 폴더의 뷰어 데이터를 다시 만드는 에디터 메뉴다. viewer_data.js가 이미 있는 폴더는 건너뛴다.</summary>
        [MenuItem(MENU_REBUILD_VIEWER_DATA)]
        public static void RebuildViewerDataFromMenu()
        {
            RebuildViewerData(false);
        }

        /// <summary>
        /// 에디터 공용 실행 흐름이다. 조합 셋으로 조합을 준비하고 확인 대화상자를 통과하면 취소 가능한 진행 바로 측정해
        /// 그때까지의 결과를 outputFolder에 쓴 뒤 뷰어를 브라우저로 연다. 진행 바는 실행을 중단하거나 예외가 나도 finally에서 정리한다.
        /// </summary>
        private static void RunFromMenu(DpsBuildSet set, int limit, string outputFolder, bool isResume)
        {
            try
            {
                RunPreparation preparation = PrepareBuilds(set, null, limit);
                if (!ConfirmRun(preparation, isResume)) return;
                ExecuteRun(preparation, outputFolder, isResume, null, CreateCancelableProgress());
                OpenViewer();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// 게임 데이터를 읽고 조합 셋의 생성 규칙·제외 규칙으로 조합을 생성해 와일드카드 필터와 개수 한도를 적용한다.
        /// </summary>
        private static RunPreparation PrepareBuilds(DpsBuildSet set, string filterPattern, int limit)
        {
            GameData.Load();
            DpsGenerationResult generated = DpsBuildGenerator.Generate(set.Suite, set.Exclusions);
            return new RunPreparation(set, generated, ApplyFilterAndLimit(generated.Builds, filterPattern, limit));
        }

        /// <summary>재개할 측정 폴더의 셋 사본(build_set.json)을 읽는다. 사본이 없는 예전 측정 폴더면 기본 셋(full)을 읽는다.</summary>
        private static DpsBuildSet LoadResumeSet(string resumeFolder)
        {
            string copyPath = Path.Combine(resumeFolder, DpsBuildSetStore.OUTPUT_COPY_FILE_NAME);
            return File.Exists(copyPath) ? DpsBuildSetStore.LoadFile(copyPath) : DpsBuildSetStore.Load(DpsBuildSetStore.DEFAULT_SET_NAME);
        }

        /// <summary>
        /// 셋 사본과 조합 목록 파일을 출력 폴더에 쓰고 일괄 측정을 실행한 뒤 results.json, results.csv, summary.csv를 쓴다.
        /// comparePath가 있으면 이전 results.json에서 이전 DPS를 읽어 results.csv 비교 열에 넣고 comparison.csv도 쓴다.
        /// isResume이면 출력 폴더의 부분 결과를 이어받는다. 끝에서 viewer_data.js를 만들고 출력 폴더가 Results~ 바로 아래면 index.js도 갱신한다.
        /// </summary>
        private static void ExecuteRun(RunPreparation preparation, string outputFolder, bool isResume, string comparePath, Func<int, int, bool> onProgress)
        {
            DpsBuildSetStore.WriteOutputCopy(preparation.Set, outputFolder);
            DpsBuildGenerator.WriteFiles(preparation.Generated, outputFolder);
            MeasurementResultSet results = DpsBatchRunner.Run(preparation.Suite, preparation.Builds, outputFolder, isResume, onProgress);
            IReadOnlyDictionary<string, double> previousDps = comparePath == null ? null : DpsResultComparer.LoadPreviousDps(comparePath);
            DpsResultWriter.WriteJson(results, Path.Combine(outputFolder, RESULTS_JSON_FILE_NAME));
            DpsResultWriter.WriteCsv(results, Path.Combine(outputFolder, RESULTS_CSV_FILE_NAME), previousDps);
            DpsSummaryWriter.Write(results, Path.Combine(outputFolder, SUMMARY_CSV_FILE_NAME));
            if (comparePath != null) DpsResultComparer.WriteComparisonCsv(results, previousDps, Path.Combine(outputFolder, COMPARISON_CSV_FILE_NAME));
            DpsViewerDataWriter.Write(results, outputFolder);
            if (IsDirectChildOfResultsRoot(outputFolder)) DpsViewerDataWriter.RebuildIndex(ProjectPath(RESULTS_ROOT));
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

        /// <summary>기본 출력 폴더를 프로젝트 안 Results~/현재 로컬 시각 타임스탬프-셋 이름 경로로 만든다.</summary>
        private static string DefaultOutputFolder(string setName)
        {
            return Path.Combine(ProjectPath(RESULTS_ROOT), DateTime.Now.ToString(TIMESTAMP_FORMAT, CultureInfo.InvariantCulture) + "-" + setName);
        }

        /// <summary>프로젝트 루트(Assets의 부모 폴더) 아래 상대 경로의 전체 경로를 만든다.</summary>
        private static string ProjectPath(string relativePath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, relativePath);
        }

        /// <summary>폴더가 Results~의 바로 아래 폴더인지 전체 경로끼리 대소문자 무시로 비교해 판별한다.</summary>
        private static bool IsDirectChildOfResultsRoot(string folder)
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(folder));
            return parent != null && string.Equals(parent, Path.GetFullPath(ProjectPath(RESULTS_ROOT)), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>뷰어 HTML을 file URI로 바꿔 시스템 기본 브라우저로 연다.</summary>
        private static void OpenViewer()
        {
            Application.OpenURL(new Uri(ProjectPath(VIEWER_PATH)).AbsoluteUri);
        }

        /// <summary>
        /// Results~의 하위 폴더 중 results.json이 있고 viewer_data.js가 없는 폴더에 뷰어 데이터를 만들고(isForced면 있는 폴더도 다시 만들고)
        /// index.js를 다시 쓴다. 처리한 폴더 수를 로그로 남기고 Results~가 없으면 만든다.
        /// </summary>
        private static void RebuildViewerData(bool isForced)
        {
            string resultsRoot = ProjectPath(RESULTS_ROOT);
            Directory.CreateDirectory(resultsRoot);
            int processedCount = 0;
            foreach (string folder in Directory.GetDirectories(resultsRoot))
            {
                if (!File.Exists(Path.Combine(folder, RESULTS_JSON_FILE_NAME))) continue;
                if (!isForced && File.Exists(Path.Combine(folder, DpsViewerDataWriter.VIEWER_DATA_FILE_NAME))) continue;
                DpsViewerDataWriter.WriteFromFolder(folder);
                processedCount++;
            }
            DpsViewerDataWriter.RebuildIndex(resultsRoot);
            UnityEngine.Debug.Log(LOG_PREFIX + " 뷰어 데이터를 만든 측정 폴더 수: " + processedCount);
        }

        /// <summary>측정 실행 준비 결과다. 조합 셋, 생성 결과 전체와 필터·한도가 적용된 측정 대상을 보관한다.</summary>
        private sealed class RunPreparation
        {
            private readonly DpsBuildSet _set;
            private readonly DpsGenerationResult _generated;
            private readonly List<SpellBuild> _builds;

            public DpsBuildSet Set => _set;
            public MeasurementSuite Suite => _set.Suite;
            public DpsGenerationResult Generated => _generated;
            public List<SpellBuild> Builds => _builds;

            /// <summary>조합 셋, 생성 결과와 측정 대상 목록으로 준비 결과를 만든다.</summary>
            public RunPreparation(DpsBuildSet set, DpsGenerationResult generated, List<SpellBuild> builds)
            {
                _set = set;
                _generated = generated;
                _builds = builds;
            }
        }
    }
}
