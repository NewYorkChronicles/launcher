using System;
using System.Net.Http;
using System.Threading;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace NYCLauncher.Core
{
    public class LauncherBridge
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private readonly MainWindow _window;
        private readonly SettingsManager _settings;
        private readonly GameLauncher _game;
        private readonly UpdateChecker _updater;
        private ChunkedInstaller _installer;
        private System.Threading.Timer _statusPoll;
        private readonly EventWaitHandle _relaunchEvent;

        public LauncherBridge(MainWindow window)
        {
            _window = window;
            _settings = new SettingsManager();
            _game = new GameLauncher(_settings);
            _updater = new UpdateChecker();
            _game.GameExited += OnGameExited;
            _relaunchEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "NYCLauncher_Relaunch");
        }

        private void OnGameExited()
        {
            if (_relaunchEvent.WaitOne(0))
            {
                _window.Dispatcher.InvokeAsync(() => Relaunch());
                return;
            }
            _window.Dispatcher.InvokeAsync(() =>
            {
                _window.Show();
                _window.WindowState = WindowState.Normal;
                _window.Activate();
                _window.SetReady();
            });
        }

        private void Relaunch()
        {
            if (_game.IsRunning) return;
            if (_game.Launch())
            {
                _window.SetStatusText("Restarting…");
                _window.WindowState = WindowState.Minimized;
            }
            else
            {
                _window.Show();
                _window.WindowState = WindowState.Normal;
                _window.Activate();
                _window.SetReady();
            }
        }

        public void AfterUIReady()
        {
            CheckForUpdate();
            PollServerStatus();
            _statusPoll = new System.Threading.Timer(_ => PollServerStatus(), null,
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        public void KillGame() => _game.Kill();

        public async void Play()
        {
            if (_game.IsRunning)
            {
                _window.SetStatusText("Game is already running.");
                return;
            }
            _window.SetLaunchEnabled(false);
            _window.SetStatusText("Checking game files…");

            try
            {
                bool hadUpdates = false;
                Action<int, int, long, long, string, string> progress = (cur, total, dl, sz, spd, eta) =>
                {
                    hadUpdates = true;
                    int pct = sz > 0 ? (int)(dl * 100L / sz) : 0;
                    _window.SetProgress(
                        pct,
                        "Downloading · " + FmtSize(dl) + " / " + FmtSize(sz),
                        spd ?? "—",
                        eta ?? "—");
                };

                _installer = new ChunkedInstaller();
                try
                {
                    await _installer.InstallAsync("game", _settings.GameDir, progress);
                }
                finally
                {
                    _installer.Dispose();
                    _installer = null;
                }

                if (hadUpdates) _window.SetProgress(100, "Update complete", "—", "—");

                if (_game.Launch())
                {
                    _window.SetStatusText("Launching…");
                    _window.Dispatcher.InvokeAsync(() => _window.WindowState = WindowState.Minimized);
                }
                else
                {
                    _window.SetStatusText("Could not launch game.");
                    _window.SetLaunchEnabled(true);
                }
            }
            catch (OperationCanceledException)
            {
                _window.SetStatusText("Download cancelled.");
                _window.SetLaunchEnabled(true);
            }
            catch (Exception ex)
            {
                _window.SetStatusText(ex.Message);
                _window.SetLaunchEnabled(true);
            }
        }

        private async void CheckForUpdate()
        {
            try
            {
                var info = await _updater.CheckAsync();
                _window.SetVersion(info.CurrentVersion ?? "v—");
                if (!info.Available) return;
                _window.SetStatusText("Updating launcher to " + info.LatestVersion + "…");
                _window.SetLaunchEnabled(false);
                await _updater.DownloadAndApplyAsync((pct, status) =>
                    _window.SetProgress(pct, status, "—", "—"));
            }
            catch (Exception ex)
            {
                _window.SetStatusText("Update failed: " + ex.Message);
                _window.SetLaunchEnabled(true);
            }
        }

        private async void PollServerStatus()
        {
            try
            {
                var res = await _http.GetAsync(Secrets.STATUS_URL);
                var json = await res.Content.ReadAsStringAsync();
                var d = JObject.Parse(json);
                bool online = d.Value<bool?>("online") ?? false;
                int players = d.Value<int?>("players") ?? 0;
                int max = d.Value<int?>("maxPlayers") ?? 0;
                _window.SetServerStatus(online, players, max);
            }
            catch
            {
                _window.SetServerStatus(false, 0, 0);
            }
        }

        private static string FmtSize(long b)
        {
            if (b >= 1073741824) return (b / 1073741824d).ToString("F1") + " GB";
            if (b >= 1048576) return (b / 1048576d).ToString("F1") + " MB";
            if (b >= 1024) return (b / 1024).ToString() + " KB";
            return b + " B";
        }
    }
}
