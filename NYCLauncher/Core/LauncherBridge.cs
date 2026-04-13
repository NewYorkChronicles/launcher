using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Windows;
using CefSharp;
using CefSharp.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NYCLauncher.Core
{
    public class LauncherBridge
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private readonly MainWindow _window;
        private readonly ChromiumWebBrowser _browser;
        private readonly SettingsManager _settings;
        private readonly GameLauncher _game;
        private readonly UpdateChecker _updater;

        public LauncherBridge(MainWindow window, ChromiumWebBrowser browser)
        {
            _window = window;
            _browser = browser;
            _settings = new SettingsManager();
            _game = new GameLauncher(_settings);
            _updater = new UpdateChecker();
            _game.GameExited += () => _window.Dispatcher.InvokeAsync(() =>
            {
                _window.Show();
                _window.WindowState = WindowState.Normal;
                _window.Activate();
                Js("setReady()");
            });
            bool updateChecked = false;
            _browser.FrameLoadEnd += (s, e) =>
            {
                if (e.Frame.IsMain && !updateChecked)
                {
                    updateChecked = true;
                    _window.Dispatcher.InvokeAsync(() => CheckForUpdate());
                }
            };
        }

        private async void CheckForUpdate()
        {
            try
            {
                var info = await _updater.CheckAsync();
                Js($"setVersion({JsonConvert.SerializeObject(info.CurrentVersion)})");
                if (!info.Available) return;
                Js($"onUpdateAvailable({JsonConvert.SerializeObject(info.LatestVersion)},{JsonConvert.SerializeObject(info.Changelog ?? "")})");
                await _updater.DownloadAndApplyAsync(
                    (pct, status) => _window.Dispatcher.InvokeAsync(() =>
                    {
                        Js($"onUpdateProgress({pct},{JsonConvert.SerializeObject(status)})");
                    })
                );
            }
            catch (Exception ex)
            {
                Js($"onUpdateStatus({JsonConvert.SerializeObject("Update failed: " + ex.Message)})");
                Js("setReady()");
            }
        }

        public void KillGame() => _game.Kill();

        public void HandleMessage(string action, Dictionary<string, object> msg)
        {
            switch (action)
            {
                case "play": HandlePlay(); break;
                case "settings": HandleShowSettings(); break;
                case "getSettings": HandleGetSettings(); break;
                case "saveSettings": HandleSaveSettings(msg); break;
                case "getServerStatus": HandleGetServerStatus(); break;
                case "getUpdates": HandleGetUpdates(); break;
                case "checkUpdate": HandleCheckUpdate(); break;
                case "browse": HandleBrowse(); break;
                case "openUrl": HandleOpenUrl(msg); break;
                case "closeApp": _window.Dispatcher.Invoke(() => Application.Current.Shutdown()); break;
                case "minimizeApp": _window.Dispatcher.Invoke(() => _window.WindowState = WindowState.Minimized); break;
            }
        }

        private void Js(string script)
        {
            if (_browser.IsBrowserInitialized) _browser.ExecuteScriptAsync(script);
        }

        private void HandleShowSettings()
        {
            var s = _settings.Load();
            string json = JsonConvert.SerializeObject(s, _camelCase);
            Js($"onSettingsLoaded({json})");
            Js("showSettings()");
            Js($"onGamePath({JsonConvert.SerializeObject(_settings.GameDir)})");
        }

        private void HandleGetSettings()
        {
            var s = _settings.Load();
            Js($"onSettingsLoaded({JsonConvert.SerializeObject(s, _camelCase)})");
            Js($"onGamePath({JsonConvert.SerializeObject(_settings.GameDir)})");
        }

        private void HandleSaveSettings(Dictionary<string, object> msg)
        {
            try
            {
                object v;
                string json = msg.TryGetValue("settings", out v) && v != null ? v.ToString() : "{}";
                var incoming = JsonConvert.DeserializeObject<LauncherSettings>(json);
                var s = _settings.Load();
                s.VerifyOnLaunch = incoming.VerifyOnLaunch;
                _settings.Save(s);
            }
            catch { }
        }

        private async void HandlePlay()
        {
            if (_game.IsRunning)
            {
                Js("onPlayResult(false,'Game is already running.')");
                return;
            }
            Js("onGameCheckStart()");
            try
            {
                var installer = new GameInstaller(_settings.GameDir);
                bool hadUpdates = false;
                await installer.InstallAsync((cur, total, dl, sz, spd, eta) =>
                {
                    if (!hadUpdates) { hadUpdates = true; _window.Dispatcher.InvokeAsync(() => Js("onGameDownloadStart()")); }
                    _window.Dispatcher.InvokeAsync(() =>
                        Js($"onGameDownloadProgress({cur},{total},{dl},{sz},{JsonConvert.SerializeObject(spd)},{JsonConvert.SerializeObject(eta)})"));
                });
                _window.Dispatcher.InvokeAsync(() =>
                {
                    if (hadUpdates) Js("onGameDownloadComplete()");
                    if (_game.Launch())
                    {
                        Js("onPlayResult(true,'Launching...')");
                        _window.WindowState = WindowState.Minimized;
                    }
                    else Js("onPlayResult(false,'Could not launch game.')");
                });
            }
            catch (Exception ex)
            {
                string m = ex is OperationCanceledException ? "Connection timed out." : ex.Message;
                _window.Dispatcher.InvokeAsync(() => Js($"onPlayResult(false,{JsonConvert.SerializeObject(m)})"));
            }
        }

        private async void HandleGetUpdates()
        {
            try
            {
                var res = await _http.GetAsync(Secrets.API_BASE + "/api/updates?limit=10");
                var json = await res.Content.ReadAsStringAsync();
                Js($"onUpdates({json})");
            }
            catch { Js("onUpdates({updates:[]})"); }
        }

        private async void HandleGetServerStatus()
        {
            try
            {
                var res = await _http.GetAsync(Secrets.STATUS_URL);
                var json = await res.Content.ReadAsStringAsync();
                Js($"onServerStatus({json})");
            }
            catch { Js("onServerStatus({online:false,players:0,maxPlayers:0})"); }
        }

        private async void HandleCheckUpdate()
        {
            try
            {
                var r = await _updater.CheckAsync();
                Js($"onUpdateCheck({JsonConvert.SerializeObject(r, _camelCase)})");
            }
            catch { Js("onUpdateCheck({available:false})"); }
        }

        private void HandleBrowse()
        {
            _window.Dispatcher.Invoke(() =>
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select MTA:SA game folder" };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    Js($"onGamePath({JsonConvert.SerializeObject(dlg.SelectedPath)})");
                dlg.Dispose();
            });
        }

        private void HandleOpenUrl(Dictionary<string, object> msg)
        {
            object v;
            string url = msg.TryGetValue("url", out v) && v != null ? v.ToString() : "";
            if (!string.IsNullOrEmpty(url) && url.StartsWith("https://"))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
        }

        private static readonly JsonSerializerSettings _camelCase = new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        };
    }
}
