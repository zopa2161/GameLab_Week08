using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

namespace RuneCode.Editor
{
    /// <summary>
    /// 조합 목록과 측정 입력으로 조합 × 조건 측정을 모두 실행하는 일괄 실행기다.
    /// 각 결과를 results.jsonl에 한 줄씩 즉시 기록해 중간에 중단돼도 끝낸 작업을 잃지 않고, 같은 파일로 이어서 실행할 수 있다.
    /// GameData.Load()는 호출자 책임이며 Run을 부르기 전에 완료돼 있어야 한다.
    /// </summary>
    public static class DpsBatchRunner
    {
        public const string PARTIAL_FILE_NAME = "results.jsonl";

        /// <summary>
        /// builds 순서 × suite.Conditions 순서(조합 바깥, 조건 안쪽)로 MeasurementRunner.Run을 실행하고
        /// 지금까지의 결과로 MeasurementResultSet을 반환한다. 각 결과를 JsonUtility 한 줄로 outputFolder/results.jsonl에
        /// 덧붙이고 작업마다 flush한다. isResume이 true면 기존 파일에서 DpsResultKey가 같은 결과를 읽어 다시 실행하지 않고
        /// 그 결과를 그대로 쓰며, JSON으로 해석되지 않는 깨진 줄은 무시하고 해당 작업을 다시 실행한다.
        /// isResume이 false인데 파일이 이미 있으면 InvalidOperationException을 던진다.
        /// 작업 하나가 예외를 던지면 Debug.LogException으로 남기고 다음 작업을 계속하며, 실패한 작업은 결과에 넣지 않고
        /// 실패 수를 마지막에 로그로 남긴다. onProgress(완료 수, 총 수)를 작업마다 호출해 false를 반환하면 즉시 중단한다.
        /// </summary>
        public static MeasurementResultSet Run(MeasurementSuite suite, IReadOnlyList<SpellBuild> builds, string outputFolder, bool isResume, Func<int, int, bool> onProgress)
        {
            DateTime startedAtUtc = DateTime.UtcNow;
            string dataVersion = DpsDataVersion.Compute();
            string partialPath = Path.Combine(outputFolder, PARTIAL_FILE_NAME);
            bool isPartialFileExists = File.Exists(partialPath);
            if (!isResume && isPartialFileExists)
                throw new InvalidOperationException("결과 파일이 이미 있습니다. 이어서 실행하려면 isResume을 true로 하거나 파일을 먼저 옮기세요: " + partialPath);

            var reusedResults = new List<MeasurementResult>();
            var completedByKey = new Dictionary<string, MeasurementResult>(StringComparer.Ordinal);
            if (isResume && isPartialFileExists)
                LoadCompletedResults(partialPath, reusedResults, completedByKey);

            Directory.CreateDirectory(outputFolder);
            var set = new MeasurementResultSet(dataVersion, startedAtUtc.ToString("o", CultureInfo.InvariantCulture), suite);
            int totalJobs = builds.Count * suite.Conditions.Count;
            int finishedJobs = 0;
            int failedJobs = 0;
            bool isCancelled = false;
            // 해석된 줄만 다시 써서 깨진 마지막 줄을 잘라 내고, 이어서 새 결과를 덧붙인다.
            using (StreamWriter writer = new StreamWriter(partialPath, false, new UTF8Encoding(false)))
            {
                foreach (MeasurementResult reused in reusedResults)
                    writer.WriteLine(JsonUtility.ToJson(reused));

                foreach (SpellBuild build in builds)
                {
                    foreach (MeasurementCondition condition in suite.Conditions)
                    {
                        if (!completedByKey.TryGetValue(DpsResultKey.Create(build.BuildId, condition.ConditionId), out MeasurementResult result))
                        {
                            try
                            {
                                result = MeasurementRunner.Run(suite, build, condition);
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                failedJobs++;
                            }
                            if (result != null)
                            {
                                writer.WriteLine(JsonUtility.ToJson(result));
                                writer.Flush();
                            }
                        }
                        if (result != null) set.Results.Add(result);
                        finishedJobs++;
                        if (onProgress != null && !onProgress(finishedJobs, totalJobs))
                        {
                            isCancelled = true;
                            break;
                        }
                    }
                    if (isCancelled) break;
                }
            }
            if (failedJobs > 0)
                Debug.LogError("측정에 실패한 작업 수: " + failedJobs + " / " + totalJobs);
            return set;
        }

        /// <summary>
        /// 부분 실행 파일의 줄을 MeasurementResult로 읽어 파일 순서 목록과 키 조회표에 채운다.
        /// JSON으로 해석되지 않는 줄(중단으로 깨진 마지막 줄)은 무시한다.
        /// </summary>
        private static void LoadCompletedResults(string partialPath, List<MeasurementResult> reusedResults, Dictionary<string, MeasurementResult> completedByKey)
        {
            foreach (string line in File.ReadAllLines(partialPath))
            {
                if (string.IsNullOrEmpty(line)) continue;
                MeasurementResult result;
                try
                {
                    result = JsonUtility.FromJson<MeasurementResult>(line);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                if (result == null) continue;
                reusedResults.Add(result);
                completedByKey[DpsResultKey.Create(result.BuildId, result.ConditionId)] = result;
            }
        }
    }
}
