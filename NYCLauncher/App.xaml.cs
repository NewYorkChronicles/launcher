using System;
using System.Net;
using System.Threading;
using System.Windows;

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
