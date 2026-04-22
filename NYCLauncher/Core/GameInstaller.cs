using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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

            var installed = LoadInstalled(path);
            var needed = new List<string>();
            var modFiles = whitelist ? ModFiles() : null;

            foreach (var kv in manifest)
            {
                if (SkipExt.Contains(Path.GetExtension(kv.Key)) || SkipNames.Contains(Path.GetFileName(kv.Key))) continue;
                if (whitelist && modFiles.Contains(kv.Key)) continue;

                string lp = Path.Combine(dir, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(lp)) { needed.Add(kv.Key); continue; }

                long diskSize = new FileInfo(lp).Length;
                if (diskSize != kv.Value.Size) { needed.Add(kv.Key); continue; }

                installed.TryGetValue(kv.Key, out var rec);
                if (rec == null)
                {
                    installed[kv.Key] = new InstalledRec { Size = kv.Value.Size, Etag = kv.Value.Etag };
                    continue;
                }
                if (rec.Size != kv.Value.Size || rec.Etag != kv.Value.Etag)
                    needed.Add(kv.Key);
            }

            if (whitelist)
            {
                var allowed = new HashSet<string>(manifest.Keys, StringComparer.OrdinalIgnoreCase);
                await Task.Run(() => Clean(dir, allowed, modFiles));
            }

            if (needed.Count == 0) { SaveInstalled(path, installed); return; }

            long totalSize = 0;
            foreach (var f in needed) totalSize += manifest[f].Size;
            await Download(needed, manifest, $"{CDN_BASE}/{path}", dir, totalSize, onProgress, installed);
            SaveInstalled(path, installed);
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
        private static readonly HashSet<string> SkipExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".log", ".set", ".flag", ".tmp" };
        private static readonly HashSet<string> SkipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "settings.xml", "chatboxpresets.xml", "core.log.flag" };

        private void Clean(string dir, HashSet<string> allowed, HashSet<string> modFiles)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                bool skip = false;
                foreach (var sd in SkipDirs) if (rel.StartsWith(sd, StringComparison.OrdinalIgnoreCase)) { skip = true; break; }
                if (skip) continue;
                if (allowed.Contains(rel) || SkipExt.Contains(Path.GetExtension(rel)) || SkipNames.Contains(Path.GetFileName(rel))) continue;
                if (rel.StartsWith("MTA/config/", StringComparison.OrdinalIgnoreCase) || modFiles.Contains(rel)) continue;
                try { File.Delete(f); } catch { }
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

        private async Task Download(List<string> files, Dictionary<string, MEntry> manifest, string cdn, string dir, long totalSize, Action<int, int, long, long, string, string> onProgress, Dictionary<string, InstalledRec> installed)
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
                string tmpPath = lp + ".tmp";
                string url = cdn + "/" + fp;
                long wantSize = manifest[fp].Size;
                Directory.CreateDirectory(Path.GetDirectoryName(lp));

                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }

                _dl = new DownloadService(new DownloadConfiguration
                {
                    ChunkCount = 4,
                    ParallelDownload = true,
                    MaxTryAgainOnFailover = 3,
                    Timeout = 15000,
                    BufferBlockSize = 65536,
                    ReserveStorageSpaceBeforeStartingDownload = false,
                    RequestConfiguration = { KeepAlive = true, UserAgent = "NYCLauncher/1.0" }
                });
                long prevDone = doneBytes;
                int fileIndex = i;
                _dl.DownloadProgressChanged += (s, e) =>
                {
                    double el = globalSw.Elapsed.TotalSeconds;
                    if (el - globalLt < 0.2) return;
                    long currentTotal = prevDone + e.ReceivedBytesSize;
                    double spd = (currentTotal - globalLb) / (el - globalLt);
                    globalLb = currentTotal; globalLt = el;
                    string eta = totalSize > 0 && spd > 0 ? Fmt((totalSize - currentTotal) / spd) : "";
                    onProgress?.Invoke(fileIndex + 1, total, currentTotal, totalSize, Spd(spd), eta);
                };

                await _dl.DownloadFileTaskAsync(url, tmpPath, _cts.Token);

                if (_dl.Status != DownloadStatus.Completed)
                {
                    try { File.Delete(tmpPath); } catch { }
                    throw new Exception("Download " + _dl.Status + ": " + fp);
                }

                long got = File.Exists(tmpPath) ? new FileInfo(tmpPath).Length : -1;
                if (got != wantSize)
                {
                    try { File.Delete(tmpPath); } catch { }
                    throw new Exception("Size mismatch " + got + "/" + wantSize + ": " + fp);
                }

                try { if (File.Exists(lp)) File.Delete(lp); } catch { }
                File.Move(tmpPath, lp);

                installed[fp] = new InstalledRec { Size = wantSize, Etag = manifest[fp].Etag };
                doneBytes += wantSize;
            }
        }

        private static string InstalledPath(string path) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NYCLauncher", "installed." + path + ".json");

        private static Dictionary<string, InstalledRec> LoadInstalled(string path)
        {
            try
            {
                var p = InstalledPath(path);
                if (!File.Exists(p)) return new Dictionary<string, InstalledRec>(StringComparer.OrdinalIgnoreCase);
                var d = JsonConvert.DeserializeObject<Dictionary<string, InstalledRec>>(File.ReadAllText(p));
                return d != null ? new Dictionary<string, InstalledRec>(d, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, InstalledRec>(StringComparer.OrdinalIgnoreCase);
            }
            catch { return new Dictionary<string, InstalledRec>(StringComparer.OrdinalIgnoreCase); }
        }

        private static void SaveInstalled(string path, Dictionary<string, InstalledRec> installed)
        {
            try
            {
                var p = InstalledPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                string tmp = p + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(installed));
                if (File.Exists(p)) File.Delete(p);
                File.Move(tmp, p);
            }
            catch { }
        }

        private static string Spd(double b) => b >= 1_048_576 ? $"{b / 1_048_576:F1} MB/s" : b >= 1024 ? $"{b / 1024:F1} KB/s" : $"{b:F0} B/s";
        private static string Fmt(double s) => s < 60 ? $"~{(int)s}s" : s < 3600 ? $"~{(int)(s / 60)}m" : $"~{(int)(s / 3600)}h";

        private class MEntry { [JsonProperty("size")] public long Size { get; set; } [JsonProperty("etag")] public string Etag { get; set; } }
        private class InstalledRec { [JsonProperty("size")] public long Size { get; set; } [JsonProperty("etag")] public string Etag { get; set; } }
    }
}
