using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Downloader;

namespace NYCLauncher.Core
{
    public class DownloadManager
    {
        private DownloadService _downloader;
        private CancellationTokenSource _cts;

        private static DownloadConfiguration CreateConfig()
        {
            return new DownloadConfiguration
            {
                ChunkCount = 4,
                ParallelDownload = true,
                MaxTryAgainOnFailover = 5,
                Timeout = 30000,
                BufferBlockSize = 8192,
                RequestConfiguration =
                {
                    KeepAlive = true,
                    UserAgent = "NYCLauncher/1.0"
                }
            };
        }

        public async Task DownloadFileAsync(string url, string destPath, Action<int, string, string> onProgress)
        {
            _cts = new CancellationTokenSource();
            Directory.CreateDirectory(Path.GetDirectoryName(destPath));

            _downloader = new DownloadService(CreateConfig());

            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastBytes = 0;
            double lastTime = 0;

            _downloader.DownloadProgressChanged += (sender, e) =>
            {
                double elapsed = sw.Elapsed.TotalSeconds;
                if (elapsed - lastTime >= 0.25)
                {
                    double speed = (e.ReceivedBytesSize - lastBytes) / (elapsed - lastTime);
                    lastBytes = e.ReceivedBytesSize;
                    lastTime = elapsed;

                    int percent = (int)e.ProgressPercentage;
                    string speedStr = FormatSpeed(speed);
                    string eta = e.TotalBytesToReceive > 0 && speed > 0
                        ? FormatTime((e.TotalBytesToReceive - e.ReceivedBytesSize) / speed)
                        : "calculating...";

                    onProgress?.Invoke(percent, speedStr, eta);
                }
            };

            await _downloader.DownloadFileTaskAsync(url, destPath, _cts.Token);

            if (_downloader.Status == DownloadStatus.Failed)
                throw new Exception("Download failed");

            onProgress?.Invoke(100, "0 B/s", "done");
        }

        public void Cancel()
        {
            _cts?.Cancel();
            _downloader?.CancelAsync();
        }

        private static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec >= 1_073_741_824) return $"{bytesPerSec / 1_073_741_824:F1} GB/s";
            if (bytesPerSec >= 1_048_576) return $"{bytesPerSec / 1_048_576:F1} MB/s";
            if (bytesPerSec >= 1024) return $"{bytesPerSec / 1024:F1} KB/s";
            return $"{bytesPerSec:F0} B/s";
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 60) return $"~{(int)seconds}s remaining";
            if (seconds < 3600) return $"~{(int)(seconds / 60)}m remaining";
            return $"~{(int)(seconds / 3600)}h remaining";
        }
    }
}
