using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Downloader;
using Newtonsoft.Json;

namespace NYCLauncher.Core
{
    public class GameInstaller
    {
        private static readonly string API_BASE = Secrets.API_BASE;
        private static readonly string CDN_BASE = Secrets.CDN_BASE;
        private readonly string _gameDir;
        private CancellationTokenSource _cts;
        private DownloadService _dl;

        public GameInstaller(string gameDir) { _gameDir = gameDir; }

        public void Cancel() { _cts?.Cancel(); _dl?.CancelAsync(); }

        public async Task InstallAsync(Action<int, int, long, long, string, string> onProgress)
        {
            _cts = new CancellationTokenSource();
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
            {
                await Sync(http, "game", _gameDir, onProgress, true);
                try { await Sync(http, "cache", Path.Combine(_gameDir, "mods", "deathmatch", "cache"), onProgress, false); } catch { }
            }
        }

        private async Task Sync(HttpClient http, string path, string dir, Action<int, int, long, long, string, string> onProgress, bool whitelist)
        {
            var manifest = await Fetch(http, path);
            if (manifest == null) { if (whitelist) throw new Exception("Could not reach update server."); return; }
            if (manifest.Count == 0) return;
            _cts.Token.ThrowIfCancellationRequested();

            var needed = new List<string>();
            if (whitelist)
            {
                var local = await Task.Run(() => Scan(dir));
                var modFiles = ModFiles();
                foreach (var kv in manifest)
                {
                    if (SkipExt.Contains(Path.GetExtension(kv.Key)) || SkipNames.Contains(Path.GetFileName(kv.Key))) continue;
                    if (modFiles.Contains(kv.Key)) continue;
                    if (!local.TryGetValue(kv.Key, out var lf) || lf.Size != kv.Value.Size)
                        needed.Add(kv.Key);
                    else if (!string.IsNullOrEmpty(kv.Value.Hash) && HashFile(lf.FullPath) != kv.Value.Hash)
                        needed.Add(kv.Key);
                }
                var allowed = new HashSet<string>(manifest.Keys, StringComparer.OrdinalIgnoreCase);
                await Task.Run(() => Clean(dir, local, allowed, modFiles));
            }
            else
            {
                foreach (var kv in manifest)
                {
                    string lp = Path.Combine(dir, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(lp))
                        needed.Add(kv.Key);
                    else if (new FileInfo(lp).Length != kv.Value.Size)
                        needed.Add(kv.Key);
                    else if (!string.IsNullOrEmpty(kv.Value.Hash) && HashFile(lp) != kv.Value.Hash)
                        needed.Add(kv.Key);
                }
            }
            if (needed.Count == 0) return;
            long totalSize = 0;
            foreach (var f in needed) { if (manifest.ContainsKey(f)) totalSize += manifest[f].Size; }
            await Download(needed, manifest, $"{CDN_BASE}/{path}", dir, totalSize, onProgress);
        }

        private async Task<Dictionary<string, MEntry>> Fetch(HttpClient http, string path)
        {
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    var res = await http.GetAsync($"{API_BASE}/api/manifest?path={path}", _cts.Token);
                    res.EnsureSuccessStatusCode();
                    return JsonConvert.DeserializeObject<Dictionary<string, MEntry>>(await res.Content.ReadAsStringAsync());
                }
                catch when (i == 0) { }
            }
            return null;
        }

        private static readonly string[] SkipDirs = { "mods/deathmatch/cache", "mods/deathmatch/resource-cache", "mods/deathmatch/logs", "mods/deathmatch/dumps", "mods/deathmatch/priv" };
        private static readonly HashSet<string> SkipExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".log", ".set", ".flag" };
        private static readonly HashSet<string> SkipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "settings.xml", "chatboxpresets.xml", "core.log.flag" };

        private Dictionary<string, LFile> Scan(string dir)
        {
            var r = new Dictionary<string, LFile>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(dir)) return r;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                bool skip = false;
                foreach (var sd in SkipDirs) if (rel.StartsWith(sd, StringComparison.OrdinalIgnoreCase)) { skip = true; break; }
                if (!skip) r[rel] = new LFile { Size = new FileInfo(f).Length, FullPath = f };
            }
            return r;
        }

        private void Clean(string dir, Dictionary<string, LFile> local, HashSet<string> allowed, HashSet<string> modFiles)
        {
            foreach (var kv in local)
            {
                if (allowed.Contains(kv.Key) || SkipExt.Contains(Path.GetExtension(kv.Key)) || SkipNames.Contains(Path.GetFileName(kv.Key))) continue;
                if (kv.Key.StartsWith("MTA/config/", StringComparison.OrdinalIgnoreCase) || modFiles.Contains(kv.Key)) continue;
                try { File.Delete(Path.Combine(dir, kv.Key.Replace('/', Path.DirectorySeparatorChar))); } catch { }
            }
        }

        private static HashSet<string> ModFiles()
        {
            var r = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NYCLauncher", "installed_mods");
                if (!Directory.Exists(d)) return r;
                foreach (var f in Directory.GetFiles(d, "*.files"))
                    foreach (var l in File.ReadAllLines(f)) { string t = l.Trim(); if (t.Length > 0) r.Add(t); }
            }
            catch { }
            return r;
        }

        private async Task Download(List<string> files, Dictionary<string, MEntry> manifest, string cdn, string dir, long totalSize, Action<int, int, long, long, string, string> onProgress)
        {
            int total = files.Count;
            long doneBytes = 0;
            var globalSw = System.Diagnostics.Stopwatch.StartNew();
            long globalLb = 0; double globalLt = 0;
            for (int i = 0; i < total; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                string fp = files[i];
                string lp = Path.Combine(dir, fp.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(lp));
                try { if (File.Exists(lp)) File.Delete(lp); } catch { }
                _dl = new DownloadService(new DownloadConfiguration { ChunkCount = 4, ParallelDownload = true, MaxTryAgainOnFailover = 3, Timeout = 15000, BufferBlockSize = 65536, RequestConfiguration = { KeepAlive = true, UserAgent = "NYCLauncher/1.0" } });
                long prevDone = doneBytes;
                _dl.DownloadProgressChanged += (s, e) =>
                {
                    double el = globalSw.Elapsed.TotalSeconds;
                    if (el - globalLt < 0.2) return;
                    long currentTotal = prevDone + e.ReceivedBytesSize;
                    double spd = (currentTotal - globalLb) / (el - globalLt);
                    globalLb = currentTotal; globalLt = el;
                    string eta = totalSize > 0 && spd > 0 ? Fmt((totalSize - currentTotal) / spd) : "";
                    onProgress?.Invoke(i + 1, total, currentTotal, totalSize, Spd(spd), eta);
                };
                await _dl.DownloadFileTaskAsync(cdn + "/" + fp, lp, _cts.Token);
                if (_dl.Status != DownloadStatus.Completed) throw new Exception("Download " + _dl.Status + ": " + fp);
                if (manifest.ContainsKey(fp))
                {
                    long got = File.Exists(lp) ? new FileInfo(lp).Length : -1;
                    long want = manifest[fp].Size;
                    if (got != want)
                    {
                        try { File.Delete(lp); } catch { }
                        throw new Exception("Size mismatch " + got + "/" + want + ": " + fp);
                    }
                }
                doneBytes += manifest.ContainsKey(fp) ? manifest[fp].Size : 0;
            }
        }

        private static string HashFile(string path)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (var fs = File.OpenRead(path))
                {
                    var hash = md5.ComputeHash(fs);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch { return ""; }
        }

        private static string Spd(double b) => b >= 1_048_576 ? $"{b / 1_048_576:F1} MB/s" : b >= 1024 ? $"{b / 1024:F1} KB/s" : $"{b:F0} B/s";
        private static string Fmt(double s) => s < 60 ? $"~{(int)s}s" : s < 3600 ? $"~{(int)(s / 60)}m" : $"~{(int)(s / 3600)}h";

        private struct MEntry { [JsonProperty("size")] public long Size; [JsonProperty("hash")] public string Hash; }
        private struct LFile { public long Size; public string FullPath; }
    }
}
