using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;
using UnityEngine.Networking;

namespace RuneCode
{
    /// <summary>완료한 사용성 테스트를 설정된 수집 주소로 보내고 성공 기록을 로컬에 보관한다.</summary>
    public sealed class UsabilityLogUploader : MonoBehaviour
    {
        private const int MAX_BYTES = 32 * 1024 * 1024;
        private const int MAX_RAW_BYTES = 64 * 1024 * 1024;
        private Settings _settings;
        private Action _finalizeSession;
        private UnityWebRequest _request;
        private bool _isSending;
        private bool _isQuitting;
        private bool _canQuit;

        [Serializable]
        private sealed class Settings
        {
            [SerializeField] private bool _isEnabled;
            [SerializeField] private string _endpoint;
            [SerializeField] private string _uploadToken;

            public bool IsEnabled => _isEnabled;
            public string Endpoint => _endpoint;
            public string UploadToken => _uploadToken;
        }

        /// <summary>에디터를 제외하고 StreamingAssets 설정이 켜진 빌드에서 전송기와 종료 시 세션 마감 동작을 연결한다.</summary>
        public static void AttachIfConfigured(GameObject owner, Action finalizeSession)
        {
            if (Application.isEditor) return;
            string path = Path.Combine(Application.streamingAssetsPath, "LogUploadSettings.json");
            try
            {
                if (!File.Exists(path)) return;
                Settings settings = JsonUtility.FromJson<Settings>(File.ReadAllText(path));
                if (settings == null || !settings.IsEnabled) return;
                if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out Uri endpoint)
                    || (endpoint.Scheme != "https" && !(endpoint.IsLoopback && endpoint.Scheme == "http"))
                    || string.IsNullOrEmpty(settings.UploadToken) || settings.UploadToken.Length < 32)
                    throw new InvalidDataException("로그 전송 주소 또는 전송 키를 확인하세요.");
                UsabilityLogUploader uploader = owner.AddComponent<UsabilityLogUploader>();
                uploader._settings = settings;
                uploader._finalizeSession = finalizeSession;
            }
            catch (Exception error)
            {
                Debug.LogWarning("로그 자동 전송 설정을 읽지 못했습니다: " + error.GetType().Name);
            }
        }

        void OnEnable()
        {
            Application.wantsToQuit += HandleWantsToQuit;
        }

        IEnumerator Start()
        {
            while (true)
            {
                if (!_isQuitting) yield return SendPending(float.PositiveInfinity);
                yield return new WaitForSecondsRealtime(15f);
            }
        }

        void OnDisable()
        {
            Application.wantsToQuit -= HandleWantsToQuit;
            StopAllCoroutines();
            _request?.Abort();
            _request?.Dispose();
            _request = null;
            _isSending = false;
        }

        /// <summary>종료 요청에서 세션을 먼저 마감하고 최대 6초간 전송한 뒤 게임 종료를 허용한다.</summary>
        private bool HandleWantsToQuit()
        {
            if (_canQuit || _settings == null) return true;
            if (!_isQuitting)
            {
                _isQuitting = true;
                _finalizeSession();
                StartCoroutine(FinishAndQuit());
            }
            return false;
        }

        /// <summary>기존 전송과 남은 세션을 제한 시간 안에서 처리하고 실제 종료를 요청한다.</summary>
        private IEnumerator FinishAndQuit()
        {
            float deadline = Time.realtimeSinceStartup + 6f;
            while (_isSending && Time.realtimeSinceStartup < deadline) yield return null;
            if (!_isSending) yield return SendPending(deadline);
            _request?.Abort();
            _canQuit = true;
            Application.Quit();
        }

        /// <summary>플레이어 빌드 표시가 있는 완료 세션만 최신순으로 전송하며 실패한 세션은 다음 실행 또는 주기에 재시도한다.</summary>
        private IEnumerator SendPending(float deadline)
        {
            if (_isSending || _settings == null) yield break;
            string root = Path.Combine(Application.persistentDataPath, "UsabilityTests");
            string[] folders;
            try
            {
                folders = Directory.Exists(root) ? Directory.GetDirectories(root)
                    .OrderByDescending(Directory.GetLastWriteTimeUtc).ToArray() : Array.Empty<string>();
            }
            catch (IOException) { yield break; }
            _isSending = true;
            foreach (string folder in folders)
            {
                if (Time.realtimeSinceStartup >= deadline) break;
                if (Path.GetFileName(folder) == LocalTelemetry.UsabilityTestSessionId
                    || !File.Exists(Path.Combine(folder, "summary.csv"))
                    || !File.Exists(Path.Combine(folder, "player-session.marker"))) continue;
                byte[] content;
                string stamp;
                try
                {
                    content = BuildArchive(folder, out stamp);
                    if (content == null) continue;
                }
                catch (Exception error)
                {
                    Debug.LogWarning("로그 압축 보류: " + error.GetType().Name);
                    continue;
                }
                using (UnityWebRequest request = new UnityWebRequest(_settings.Endpoint, "POST"))
                {
                    _request = request;
                    request.uploadHandler = new UploadHandlerRaw(content);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Authorization", "Bearer " + _settings.UploadToken);
                    request.SetRequestHeader("Content-Type", "application/zip");
                    request.SetRequestHeader("User-Agent", "PlayLogGame/1.0");
                    request.SetRequestHeader("X-PlayLog-Source", "player-v1");
                    request.timeout = 5;
                    request.redirectLimit = 0;
                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    while (!operation.isDone && Time.realtimeSinceStartup < deadline) yield return null;
                    if (!operation.isDone) request.Abort();
                    if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
                    {
                        try { File.WriteAllText(Path.Combine(folder, "upload.receipt"), stamp); }
                        catch (IOException) { Debug.LogWarning("로그 수신 완료, 로컬 전송 기록 저장은 보류했습니다."); }
                    }
                    else Debug.LogWarning("로그 전송 보류. 로컬 로그를 보존하고 다음 실행 때 재시도합니다.");
                    _request = null;
                }
            }
            _isSending = false;
        }

        /// <summary>허용된 세션 파일을 크기 제한 내 ZIP으로 만들고 이미 수신된 동일 파일이면 null을 반환한다.</summary>
        private byte[] BuildArchive(string folder, out string stamp)
        {
            string[] names = { "events.jsonl", "session.json", "summary.csv", "combats.csv" };
            string[] files = names.Select(name => Path.Combine(folder, name)).Where(File.Exists).ToArray();
            string builds = Path.Combine(folder, "builds");
            if (Directory.Exists(builds)) files = files.Concat(Directory.GetFiles(builds, "*.json")).ToArray();
            Array.Sort(files, StringComparer.Ordinal);
            StringBuilder signature = new StringBuilder(_settings.UploadToken);
            long size = 0;
            foreach (string file in files)
            {
                FileInfo info = new FileInfo(file);
                size += info.Length;
                signature.Append(file).Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
            }
            using (SHA256 hash = SHA256.Create())
                stamp = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString())));
            string receipt = Path.Combine(folder, "upload.receipt");
            if (File.Exists(receipt) && File.ReadAllText(receipt) == stamp) return null;
            if (size > MAX_RAW_BYTES || files.Length > 500) throw new InvalidDataException("로그 용량 제한 초과");
            using (MemoryStream output = new MemoryStream())
            {
                using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                    foreach (string file in files)
                        archive.CreateEntryFromFile(file, file.Substring(folder.Length + 1).Replace('\\', '/'), System.IO.Compression.CompressionLevel.Fastest);
                if (output.Length > MAX_BYTES) throw new InvalidDataException("압축 로그 용량 제한 초과");
                return output.ToArray();
            }
        }
    }
}
