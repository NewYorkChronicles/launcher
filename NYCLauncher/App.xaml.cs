using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows;
using CefSharp;
using CefSharp.Wpf;
using NYCLauncher.Core;

namespace NYCLauncher
{
    public partial class App : Application
    {
        public static Mutex AppMutex;
        private static EventWaitHandle _showEvent;
        private Thread _watchThread;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            Core.UpdateChecker.CleanOldFiles();
            bool created;
            AppMutex = new Mutex(true, "NYCLauncher_Single", out created);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

            if (!created)
            {
                try
                {
                    var ev = EventWaitHandle.OpenExisting("NYCLauncher_Show");
                    ev.Set();
                    ev.Dispose();
                }
                catch { }
                Shutdown();
                return;
            }

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "NYCLauncher_Show");
            _watchThread = new Thread(WatchForShow) { IsBackground = true };
            _watchThread.Start();

            var settings = new CefSettings
            {
                CachePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NYCLauncher", "cef_cache"),
                LogSeverity = LogSeverity.Disable
            };

            settings.RegisterScheme(new CefCustomScheme
            {
                SchemeName = "app",
                SchemeHandlerFactory = new ResourceSchemeHandlerFactory(),
                IsSecure = true,
                IsLocal = false,
                IsStandard = true,
                IsCorsEnabled = true
            });

            Cef.Initialize(settings);
        }

        private void WatchForShow()
        {
            while (_showEvent != null && _showEvent.WaitOne())
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var win = MainWindow;
                    if (win == null) return;
                    win.Show();
                    win.ShowInTaskbar = true;
                    win.WindowState = WindowState.Normal;
                    win.Activate();
                });
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            Cef.Shutdown();
            try { _showEvent?.Set(); _showEvent?.Dispose(); } catch { }
            _showEvent = null;
            if (AppMutex != null)
            {
                AppMutex.ReleaseMutex();
                AppMutex.Dispose();
            }
        }
    }
}
