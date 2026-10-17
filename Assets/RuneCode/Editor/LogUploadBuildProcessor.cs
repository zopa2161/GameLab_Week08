using System;
using System.IO;

using UnityEngine;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace RuneCode
{
    /// <summary>Windows 빌드 결과에 로컬 또는 프로젝트 공용 전송 설정을 검증해 포함한다.</summary>
    public sealed class LogUploadBuildProcessor : IPostprocessBuildWithReport
    {
        private const string SETTINGS_FILE_NAME = "LogUploadSettings.json";

        public int callbackOrder => 10000;

        [Serializable]
        private sealed class UploadSettings
        {
            [SerializeField] private bool _isEnabled;
            [SerializeField] private string _endpoint;
            [SerializeField] private string _uploadToken;

            public bool IsEnabled => _isEnabled;
            public string Endpoint => _endpoint;
            public string UploadToken => _uploadToken;
        }

        /// <summary>Windows 빌드에 바탕화면 설정을 우선 적용하고 없으면 프로젝트 공용 설정을 검증해 복사한다.</summary>
        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows
                && report.summary.platform != BuildTarget.StandaloneWindows64) return;

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string source = Path.Combine(desktop, "PlayLogWorkbench", "GameConfig", SETTINGS_FILE_NAME);
            if (!File.Exists(source)) source = Path.Combine(Application.streamingAssetsPath, SETTINGS_FILE_NAME);
            if (!File.Exists(source))
            {
                throw new BuildFailedException("온라인 수집 설정이 없습니다. 프로젝트의 Assets/StreamingAssets/LogUploadSettings.json을 함께 받아 다시 빌드하세요.");
            }

            try
            {
                string json = File.ReadAllText(source);
                UploadSettings settings = JsonUtility.FromJson<UploadSettings>(json);
                if (settings == null || !settings.IsEnabled
                    || (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out Uri endpoint)
                        || endpoint.Scheme != "https" || string.IsNullOrEmpty(settings.UploadToken)
                        || settings.UploadToken.Length < 32))
                    throw new InvalidDataException("자동 수집이 꺼져 있거나 수집 주소·전송 키가 올바르지 않습니다.");
                if (settings.IsEnabled && new Uri(settings.Endpoint).Host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("임시 수집 주소입니다. SetupCloudCollection.cmd로 고정 수집함 연결을 완료한 뒤 빌드하세요.");

                string player = Path.GetFullPath(report.summary.outputPath);
                string dataFolder = Path.Combine(Path.GetDirectoryName(player),
                    Path.GetFileNameWithoutExtension(player) + "_Data", "StreamingAssets");
                Directory.CreateDirectory(dataFolder);
                File.WriteAllText(Path.Combine(dataFolder, SETTINGS_FILE_NAME), json);
                Debug.Log("온라인 수집 설정을 게임 빌드에 자동으로 포함했습니다. 설정 파일을 직접 복사할 필요가 없습니다.");
            }
            catch (Exception error)
            {
                throw new BuildFailedException("온라인 수집 설정을 빌드에 포함하지 못했습니다. "
                    + (error is InvalidDataException ? error.Message : error.GetType().Name));
            }
        }
    }
}
