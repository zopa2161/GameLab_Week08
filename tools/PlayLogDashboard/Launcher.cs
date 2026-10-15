using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PlayLogDashboard
{
    internal static class Program
    {
        /// <summary>폴더별 중복 실행을 방지하고 로그 분석 도구 실행 창을 표시한다.</summary>
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string key;
            using (SHA256 hash = SHA256.Create())
                key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace("-", "").Substring(0, 20);
            if (Array.IndexOf(args, "--stop") >= 0)
            {
                try
                {
                    using (EventWaitHandle signal = EventWaitHandle.OpenExisting("Local\\PlayLogDashboard-Stop-" + key))
                        signal.Set();
                }
                catch (WaitHandleCannotBeOpenedException) { }
                return;
            }
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\PlayLogDashboard-" + key, out created))
            {
                if (!created)
                {
                    MessageBox.Show("이미 실행 중입니다. 작업 표시줄의 플레이 로그 분석 창에서 브라우저 열기를 누르세요.", "플레이 로그 분석");
                    return;
                }
                try { Application.Run(new DashboardWindow(root, key, Array.IndexOf(args, "--no-browser") >= 0)); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }

    internal sealed class DashboardWindow : Form
    {
        private readonly string _root;
        private readonly bool _isBrowserSuppressed;
        private readonly object _logLock = new object();
        private readonly Label _statusLabel;
        private readonly LinkLabel _urlLink;
        private readonly Button _openButton;
        private readonly StreamWriter _log;
        private readonly EventWaitHandle _stopSignal;

        private Process _server;
        private RegisteredWaitHandle _stopRegistration;
        private string _url;
        private bool _isClosing;

        /// <summary>배포 폴더와 인스턴스 키를 받아 시작·브라우저 열기·종료 UI와 실행 로그를 준비한다.</summary>
        internal DashboardWindow(string root, string key, bool isBrowserSuppressed)
        {
            _root = root;
            _isBrowserSuppressed = isBrowserSuppressed;
            _stopSignal = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\PlayLogDashboard-Stop-" + key);
            string logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlayLogDashboard", key);
            Directory.CreateDirectory(logFolder);
            _log = new StreamWriter(Path.Combine(logFolder, "launcher.log"), false, Encoding.UTF8) { AutoFlush = true };
            Text = "플레이 로그 분석";
            ClientSize = new Size(540, 235);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Malgun Gothic", 10F);
            _statusLabel = new Label { Left = 24, Top = 22, Width = 490, Height = 54, Text = "분석 도구를 준비하고 있습니다..." };
            _urlLink = new LinkLabel { Left = 24, Top = 82, Width = 490, Height = 30 };
            _urlLink.LinkClicked += delegate { OpenBrowser(); };
            _openButton = new Button { Left = 24, Top = 124, Width = 236, Height = 42, Text = "브라우저 열기", Enabled = false };
            _openButton.Click += delegate { OpenBrowser(); };
            Button closeButton = new Button { Left = 280, Top = 124, Width = 236, Height = 42, Text = "종료" };
            closeButton.Click += delegate { Close(); };
            Label hint = new Label { Left = 24, Top = 184, Width = 490, Height = 40, Text = "이 창을 닫으면 분석 서버도 종료됩니다. 파일은 이 PC에서 처리됩니다." };
            Controls.AddRange(new Control[] { _statusLabel, _urlLink, _openButton, closeButton, hint });
            Shown += async delegate
            {
                _stopRegistration = ThreadPool.RegisterWaitForSingleObject(_stopSignal, delegate
                {
                    try { if (!_isClosing) BeginInvoke((Action)Close); }
                    catch (InvalidOperationException) { }
                }, null, Timeout.Infinite, true);
                await StartServerAsync();
            };
            FormClosing += delegate { StopServer(); };
        }

        /// <summary>포함된 Python으로 로컬 서버를 실행하고 준비 상태를 확인한 뒤 브라우저를 연다.</summary>
        private async Task StartServerAsync()
        {
            try
            {
                string python = Path.Combine(_root, "runtime", "python.exe");
                string app = Path.Combine(_root, "app", "app.py");
                if (!File.Exists(python) || !File.Exists(app))
                    throw new FileNotFoundException("runtime 또는 app 폴더가 없습니다. 전체 폴더를 복사하거나 압축 해제하세요.");
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                _url = "http://127.0.0.1:" + port;
                ProcessStartInfo info = new ProcessStartInfo(python,
                    "-B -m streamlit run \"" + app + "\" --server.address 127.0.0.1 --server.port " + port
                    + " --server.headless true --server.fileWatcherType none --browser.gatherUsageStats false")
                {
                    WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                info.EnvironmentVariables["PYTHONNOUSERSITE"] = "1";
                _server = new Process { StartInfo = info };
                _server.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { WriteLog(e.Data); };
                _server.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { WriteLog(e.Data); };
                WriteLog("URL=" + _url);
                _server.Start();
                _server.BeginOutputReadLine();
                _server.BeginErrorReadLine();
                bool isReady = false;
                for (int attempt = 0; attempt < 90 && !_isClosing; attempt++)
                {
                    if (_server.HasExited) throw new InvalidOperationException("서버가 시작되지 않았습니다. launcher.log를 확인하세요.");
                    isReady = await Task.Run(() => IsHealthy(_url));
                    if (isReady) break;
                    await Task.Delay(500);
                }
                if (_isClosing) return;
                if (!isReady) throw new TimeoutException("서버 준비 시간이 초과되었습니다. 종료 후 다시 실행하세요.");
                _statusLabel.Text = "실행 중입니다. 브라우저에서 JSONL 파일을 넣어 분석하세요.";
                _urlLink.Text = _url;
                _openButton.Enabled = true;
                WriteLog("READY");
                if (!_isBrowserSuppressed) OpenBrowser();
            }
            catch (Exception error)
            {
                if (_isClosing) return;
                WriteLog(error.ToString());
                _statusLabel.Text = "실행 실패: " + error.Message;
                MessageBox.Show(this, error.Message + "\n\n로그: " + ((FileStream)_log.BaseStream).Name, "실행 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>로컬 주소의 상태 확인 응답이 정상인지 반환한다.</summary>
        private static bool IsHealthy(string url)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url + "/_stcore/health");
                request.Proxy = null;
                request.Timeout = 1000;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    return response.StatusCode == HttpStatusCode.OK;
            }
            catch (WebException) { return false; }
        }

        /// <summary>준비된 로컬 주소를 기본 브라우저에 열고 실패 시 주소를 안내한다.</summary>
        private void OpenBrowser()
        {
            if (string.IsNullOrEmpty(_url) || !_openButton.Enabled) return;
            try { Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true }); }
            catch (Exception error) { MessageBox.Show(this, "브라우저에서 다음 주소를 여세요.\n" + _url + "\n" + error.Message); }
        }

        /// <summary>서버의 출력 한 줄을 인스턴스별 로그에 기록한다.</summary>
        private void WriteLog(string line)
        {
            if (line == null) return;
            lock (_logLock)
                if (!_isClosing) _log.WriteLine(DateTime.Now.ToString("s") + " " + line);
        }

        /// <summary>이 창이 시작한 서버만 종료하고 로그 자원을 해제한다.</summary>
        private void StopServer()
        {
            _isClosing = true;
            if (_stopRegistration != null) _stopRegistration.Unregister(null);
            _stopSignal.Dispose();
            if (_server != null)
            {
                try
                {
                    if (!_server.HasExited) { _server.Kill(); _server.WaitForExit(3000); }
                }
                catch (InvalidOperationException) { }
                finally { _server.Dispose(); }
            }
            lock (_logLock) _log.Dispose();
        }
    }
}
