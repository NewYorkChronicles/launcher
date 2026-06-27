using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using K4os.Hash.xxHash;
using Newtonsoft.Json;

namespace NYCLauncher.Core
{
    public class ChunkedInstaller : IDisposable
    {
        private const int CHUNK_SIZE = 1048576;
        private const int PARALLEL_CHUNKS = 6;
        private readonly string _apiBase;
        private readonly string _cdnBase;
        private readonly HttpClient _http;
        private CancellationTokenSource _cts;
        private HashSet<string> _modFiles;
        private volatile bool _paused;

        public ChunkedInstaller()
        {
            _apiBase = Secrets.API_BASE;
            _cdnBase = Secrets.CDN_BASE;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("NYCLauncher/1.0");
        }

        public bool IsPaused => _paused;

        public void Cancel() { try { _cts?.Cancel(); } catch { } }

        public void TogglePause() { _paused = !_paused; }
        public void Pause() { _paused = true; }
        public void Resume() { _paused = false; }

        public void Dispose()
        {
            try { _cts?.Dispose(); } catch { }
            try { _http?.Dispose(); } catch { }
        }

        public async Task InstallAsync(string path, string dir, Action<int, int, long, long, string, string> onProgress)
        {
            _cts = new CancellationTokenSource();
            _modFiles = LoadModFiles();

            Manifest manifest;
            try
            {
                var res = await _http.GetAsync(_apiBase + "/api/files/manifest?path=" + Uri.EscapeDataString(path), _cts.Token);
                res.EnsureSuccessStatusCode();
                manifest = JsonConvert.DeserializeObject<Manifest>(await res.Content.ReadAsStringAsync());
            }
            catch (Exception e)
            {
                throw new Exception("Could not reach update server: " + e.Message);
            }

            if (manifest == null || manifest.Files == null || manifest.Files.Count == 0)
                throw new Exception("Update server returned empty manifest.");
            if (manifest.Missing > 0)
                throw new Exception("Server missing chunk manifests for " + manifest.Missing + " files. Re-run chunk-cdn.js.");

            string chunkBase = _cdnBase.TrimEnd('/') + (manifest.ChunkBase ?? "/chunks");

            // Skip files the user installed via the mod system — they shadow CDN entries.
            var plans = new List<PatchPlan>();
            long totalNewBytes = 0;
            int totalChunks = 0;
            foreach (var kv in manifest.Files)
            {
                _cts.Token.ThrowIfCancellationRequested();
                if (_modFiles.Contains(kv.Key)) continue;
                if (!ValidateEntry(kv.Value))
                    throw new Exception("Malformed manifest entry: " + kv.Key);

                var plan = await Task.Run(() => PlanFile(kv.Key, kv.Value, dir));
                plans.Add(plan);
                totalNewBytes += plan.ChangedIndices.Count * (long)CHUNK_SIZE;
                totalChunks += plan.ChangedIndices.Count;
            }

            if (totalChunks == 0)
            {
                // Zero-byte files and shrinks still need create/truncate even with no chunk downloads.
                foreach (var p in plans)
                    if (p.NeedsTruncate || p.NeedsCreate) await Task.Run(() => MaterializeEmpty(p));
                onProgress?.Invoke(plans.Count, plans.Count, 0, 0, "0 B/s", "");
                return;
            }

            long doneBytes = 0;
            int doneChunks = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastBytes = 0;
            double lastT = 0;

            foreach (var plan in plans)
            {
                _cts.Token.ThrowIfCancellationRequested();
                if (plan.ChangedIndices.Count == 0)
                {
                    if (plan.NeedsTruncate || plan.NeedsCreate) await Task.Run(() => MaterializeEmpty(plan));
                    continue;
                }
                await ApplyPatchAsync(plan, chunkBase, (chunkBytes) =>
                {
                    doneBytes += chunkBytes;
                    doneChunks++;
                    double el = sw.Elapsed.TotalSeconds;
                    if (el - lastT < 0.2) return;
                    double spd = (doneBytes - lastBytes) / (el - lastT);
                    lastBytes = doneBytes; lastT = el;
                    string eta = totalNewBytes > 0 && spd > 0 ? Fmt((totalNewBytes - doneBytes) / spd) : "";
                    onProgress?.Invoke(doneChunks, totalChunks, doneBytes, totalNewBytes, Spd(spd), eta);
                });
            }
        }

        private static bool ValidateEntry(FileEntry fe)
        {
            if (fe == null || fe.Chunks == null) return false;
            if (fe.Size < 0 || fe.ChunkSize != CHUNK_SIZE) return false;
            long expected = (fe.Size + CHUNK_SIZE - 1) / CHUNK_SIZE;
            if (fe.Size == 0 && fe.Chunks.Length != 0) return false;
            if (fe.Size > 0 && fe.Chunks.Length != expected) return false;
            return true;
        }

        private PatchPlan PlanFile(string rel, FileEntry fe, string dir)
        {
            string target = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            string patch = target + ".patch";
            Directory.CreateDirectory(Path.GetDirectoryName(target));

            string source = File.Exists(patch) ? patch : (File.Exists(target) ? target : null);
            var current = source != null ? ComputeChunkHashes(source) : new List<string>();

            var changed = new List<int>();
            for (int i = 0; i < fe.Chunks.Length; i++)
            {
                if (i >= current.Count || !string.Equals(current[i], fe.Chunks[i], StringComparison.OrdinalIgnoreCase))
                    changed.Add(i);
            }

            return new PatchPlan
            {
                Target = target,
                Patch = patch,
                Source = source,
                Size = fe.Size,
                Chunks = fe.Chunks,
                ChangedIndices = changed,
                NeedsCreate = source == null,
                NeedsTruncate = source != null && new FileInfo(source).Length != fe.Size
            };
        }

        private static void MaterializeEmpty(PatchPlan plan)
        {
            if (plan.Source != null && !string.Equals(plan.Source, plan.Patch, StringComparison.OrdinalIgnoreCase))
                File.Copy(plan.Source, plan.Patch, true);
            else if (plan.Source == null)
                using (File.Create(plan.Patch)) { }
            using (var fs = new FileStream(plan.Patch, FileMode.Open, FileAccess.Write, FileShare.None))
                fs.SetLength(plan.Size);
            FinalizePatch(plan);
        }

        private async Task ApplyPatchAsync(PatchPlan plan, string chunkBase, Action<long> onChunkDone)
        {
            if (plan.Source != null && !string.Equals(plan.Source, plan.Patch, StringComparison.OrdinalIgnoreCase))
                File.Copy(plan.Source, plan.Patch, true);
            else if (plan.Source == null)
                using (File.Create(plan.Patch)) { }

            using (var fs = new FileStream(plan.Patch, FileMode.Open, FileAccess.Write, FileShare.None))
                fs.SetLength(plan.Size);

            var sem = new SemaphoreSlim(PARALLEL_CHUNKS);
            var tasks = new List<Task>();
            foreach (var idx in plan.ChangedIndices)
            {
                _cts.Token.ThrowIfCancellationRequested();
                while (_paused) await Task.Delay(150, _cts.Token);
                await sem.WaitAsync(_cts.Token);
                int chunkIdx = idx;
                string expectedHash = plan.Chunks[chunkIdx];
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        long offset = (long)chunkIdx * CHUNK_SIZE;
                        int wantLen = (int)Math.Min(CHUNK_SIZE, plan.Size - offset);
                        byte[] data = await DownloadChunkAsync(chunkBase, expectedHash);
                        if (data.Length != wantLen)
                            throw new Exception("Chunk size mismatch (want " + wantLen + ", got " + data.Length + "): " + expectedHash);
                        string actual = HashBytes(data);
                        if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                            throw new Exception("Chunk hash mismatch (want " + expectedHash + ", got " + actual + ")");
                        using (var fs = new FileStream(plan.Patch, FileMode.Open, FileAccess.Write, FileShare.Write))
                        {
                            fs.Seek(offset, SeekOrigin.Begin);
                            await fs.WriteAsync(data, 0, data.Length);
                        }
                        onChunkDone?.Invoke(data.Length);
                    }
                    finally { sem.Release(); }
                }, _cts.Token));
            }
            await Task.WhenAll(tasks);
            FinalizePatch(plan);
        }

        private static void FinalizePatch(PatchPlan plan)
        {
            try { if (File.Exists(plan.Target)) File.Delete(plan.Target); } catch { }
            File.Move(plan.Patch, plan.Target);
        }

        private async Task<byte[]> DownloadChunkAsync(string chunkBase, string hash)
        {
            string url = chunkBase + "/" + hash.Substring(0, 2) + "/" + hash + ".chunk";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var res = await _http.GetAsync(url, _cts.Token);
                    res.EnsureSuccessStatusCode();
                    return await res.Content.ReadAsByteArrayAsync();
                }
                catch when (attempt < 2)
                {
                    await Task.Delay(250 * (attempt + 1), _cts.Token);
                }
            }
            throw new Exception("Failed to fetch chunk: " + hash);
        }

        private static List<string> ComputeChunkHashes(string filePath)
        {
            var result = new List<string>();
            var buf = new byte[CHUNK_SIZE];
            using (var fs = File.OpenRead(filePath))
            {
                while (true)
                {
                    int total = 0;
                    while (total < buf.Length)
                    {
                        int n = fs.Read(buf, total, buf.Length - total);
                        if (n <= 0) break;
                        total += n;
                    }
                    if (total == 0) break;
                    result.Add(HashBytes(buf, 0, total));
                    if (total < buf.Length) break;
                }
            }
            return result;
        }

        private static HashSet<string> LoadModFiles()
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

        private static string HashBytes(byte[] data) => HashBytes(data, 0, data.Length);
        private static string HashBytes(byte[] data, int offset, int length)
        {
            var h = new XXH64();
            h.Update(data, offset, length);
            return h.Digest().ToString("x16");
        }

        private static string Spd(double b) => b >= 1_048_576 ? $"{b / 1_048_576:F1} MB/s" : b >= 1024 ? $"{b / 1024:F1} KB/s" : $"{b:F0} B/s";
        private static string Fmt(double s) => s < 60 ? $"~{(int)s}s" : s < 3600 ? $"~{(int)(s / 60)}m" : $"~{(int)(s / 3600)}h";

        private class Manifest
        {
            [JsonProperty("chunkBase")] public string ChunkBase { get; set; }
            [JsonProperty("files")] public Dictionary<string, FileEntry> Files { get; set; }
            [JsonProperty("missing")] public int Missing { get; set; }
        }

        private class FileEntry
        {
            [JsonProperty("size")] public long Size { get; set; }
            [JsonProperty("chunkSize")] public int ChunkSize { get; set; }
            [JsonProperty("chunks")] public string[] Chunks { get; set; }
        }

        private class PatchPlan
        {
            public string Target;
            public string Patch;
            public string Source;
            public long Size;
            public string[] Chunks;
            public List<int> ChangedIndices;
            public bool NeedsCreate;
            public bool NeedsTruncate;
        }
    }
}
