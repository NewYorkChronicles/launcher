using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Downloader;
using Newtonsoft.Json.Linq;

namespace NYCLauncher.Core
{
    public class UpdateInfo
    {
        public bool Available { get; set; }
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }
        public string Changelog { get; set; }
        public List<UpdateFile> Files { get; set; }
    }

    public class UpdateFile
    {
        public string Path { get; set; }
        public string Url { get; set; }
    }

    public class UpdateChecker
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly string VERSION_URL = Secrets.VERSION_URL;
        public const string CurrentVersion = "v1.4.1";

        public static void CleanOldFiles()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                foreach (var f in Directory.GetFiles(dir, "*.old"))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }

        public async Task<UpdateInfo> CheckAsync()
        {
            var info = new UpdateInfo { CurrentVersion = CurrentVersion };
            try
            {
                var json = await _http.GetStringAsync(VERSION_URL);
                var data = JObject.Parse(json);
                info.LatestVersion = data["version"]?.ToString() ?? CurrentVersion;
                info.Changelog = data["changelog"]?.ToString();
                info.Available = !string.Equals(info.LatestVersion, CurrentVersion, StringComparison.OrdinalIgnoreCase);
                if (!info.Available) return info;

                var arr = data["files"] as JArray;
                if (arr != null && arr.Count > 0)
                {
                    info.Files = new List<UpdateFile>();
                    foreach (var item in arr)
                        info.Files.Add(new UpdateFile { Path = item["path"]?.ToString(), Url = item["url"]?.ToString() });
                }
                else
                {
                    string url = data["url"]?.ToString();
                    if (!string.IsNullOrEmpty(url))
                        info.Files = new List<UpdateFile> { new UpdateFile { Path = System.IO.Path.GetFileName(Assembly.GetExecutingAssembly().Location), Url = url } };
                }
            }
            catch { info.Available = false; }
            return info;
        }

        public async Task DownloadAndApplyAsync(Action<int, string> onProgress)
        {
            var info = await CheckAsync();
            if (!info.Available || info.Files == null || info.Files.Count == 0) return;

            string exePath = Assembly.GetExecutingAssembly().Location;
            string exeDir = System.IO.Path.GetDirectoryName(exePath);

            for (int i = 0; i < info.Files.Count; i++)
            {
                var file = info.Files[i];
                string destPath = System.IO.Path.Combine(exeDir, file.Path);
                string tempPath = destPath + ".tmp";

                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

                var dl = new DownloadService(new DownloadConfiguration
                {
                    ChunkCount = 4, ParallelDownload = true, MaxTryAgainOnFailover = 3,
                    Timeout = 30000, RequestConfiguration = { KeepAlive = true, UserAgent = "NYCLauncher/1.0" }
                });
                int idx = i;
                dl.DownloadProgressChanged += (s, e) =>
                {
                    int pct = (int)((double)idx / info.Files.Count * 100 + e.ProgressPercentage / info.Files.Count);
                    onProgress?.Invoke(pct, $"Downloading update {info.LatestVersion}...");
                };
                await dl.DownloadFileTaskAsync(file.Url, tempPath);

                if (dl.Status == DownloadStatus.Failed || !File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                {
                    try { File.Delete(tempPath); } catch { }
                    throw new Exception("Download failed: " + file.Path);
                }

                string oldPath = destPath + ".old";
                try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }

                if (File.Exists(destPath))
                    File.Move(destPath, oldPath);

                File.Move(tempPath, destPath);
            }

            onProgress?.Invoke(100, "Restarting...");
            try { App.AppMutex?.ReleaseMutex(); App.AppMutex?.Dispose(); App.AppMutex = null; } catch { }
            await Task.Delay(500);
            var psi = new ProcessStartInfo { FileName = exePath, UseShellExecute = true, Verb = "runas" };
            try { Process.Start(psi); } catch { Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true }); }
            await Task.Delay(1000);
            Environment.Exit(0);
        }
    }
}
