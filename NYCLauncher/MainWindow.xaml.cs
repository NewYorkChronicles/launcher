using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NYCLauncher.Core;

namespace NYCLauncher
{
    public partial class MainWindow : Window
    {
        public LauncherBridge Bridge { get; private set; }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWCP_ROUND = 2;
        private const int DWMSBT_MAINWINDOW = 2;

        public MainWindow()
        {
            InitializeComponent();
            Bridge = new LauncherBridge(this);
            Loaded += (s, e) => Bridge.AfterUIReady();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;

            int dark = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref dark, sizeof(int));

            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

            int backdrop = DWMSBT_MAINWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            Bridge.KillGame();
            base.OnClosing(e);
        }

        public void SetServerStatus(bool online, int players, int maxPlayers)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (online)
                {
                    StatusText.Text = "Online";
                    StatusText.Foreground = (Brush)FindResource("Green");
                    StatusDot.Fill = (Brush)FindResource("Green");
                    PlayersRun.Text = players.ToString();
                    MaxPlayersRun.Text = maxPlayers.ToString();
                }
                else
                {
                    StatusText.Text = "Offline";
                    StatusText.Foreground = (Brush)FindResource("RedHover");
                    StatusDot.Fill = (Brush)FindResource("RedHover");
                    PlayersRun.Text = "0";
                    MaxPlayersRun.Text = "—";
                }
            });
        }

        public void SetProgress(int pct, string status, string speedText, string etaText)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                StatusLabel.Text = status ?? "";
                ProgressPct.Text = pct + "%";
                SpeedText.Text = speedText ?? "—";
                EtaText.Text = etaText ?? "—";
                var track = (System.Windows.Controls.Border)ProgressFillContainer.Parent;
                ProgressFillContainer.Width = track.ActualWidth * (pct / 100.0);
            });
        }

        public void SetStatusText(string status)
        {
            Dispatcher.InvokeAsync(() => StatusLabel.Text = status ?? "");
        }

        public void SetReady()
        {
            Dispatcher.InvokeAsync(() =>
            {
                StatusLabel.Text = "Ready to play";
                ProgressPct.Text = "—";
                SpeedText.Text = "—";
                EtaText.Text = "—";
                ProgressFillContainer.Width = 0;
                LaunchButton.IsEnabled = true;
            });
        }

        public void SetLaunchEnabled(bool enabled)
        {
            Dispatcher.InvokeAsync(() => LaunchButton.IsEnabled = enabled);
        }

        public void SetVersion(string v)
        {
            Dispatcher.InvokeAsync(() => VersionText.Text = string.IsNullOrEmpty(v) ? "v—" : v);
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            Bridge.Play();
        }

        private void Website_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://newyorkchronicles.online");
        }

        private void Discord_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://discord.newyorkchronicles.online");
        }

        private void Forum_Click(object sender, MouseButtonEventArgs e)
        {
            OpenUrl("https://forum.newyorkchronicles.online");
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch { }
        }
    }
}
