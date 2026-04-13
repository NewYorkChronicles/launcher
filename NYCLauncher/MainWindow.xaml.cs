using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Animation;
using CefSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NYCLauncher.Core;

namespace NYCLauncher
{
    public partial class MainWindow : Window
    {
        private LauncherBridge _bridge;
        private bool _shown;

        public MainWindow()
        {
            InitializeComponent();
            _bridge = new LauncherBridge(this, Browser);
            Browser.MenuHandler = new NoContextMenuHandler();
            Browser.JavascriptMessageReceived += OnMessage;
            Browser.IsBrowserInitializedChanged += OnBrowserReady;
        }

        private void DragBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left) DragMove();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { _bridge.KillGame(); base.OnClosing(e); }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (!Browser.IsBrowserInitialized) return;
            var host = Browser.GetBrowserHost();
            if (host == null) return;
            if (WindowState == WindowState.Minimized)
                host.WasHidden(true);
            else
                host.WasHidden(false);
        }

        private void OnBrowserReady(object s, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;
            Browser.LoadingStateChanged += OnLoadingState;
            Browser.Load("app://launcher/index.html");
        }

        private void OnLoadingState(object s, LoadingStateChangedEventArgs e)
        {
            if (!e.IsLoading && !_shown)
            {
                _shown = true;
                Dispatcher.InvokeAsync(() =>
                {
                    var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                    BeginAnimation(OpacityProperty, anim);
                });
            }
        }

        private void OnMessage(object sender, JavascriptMessageReceivedEventArgs e)
        {
            try
            {
                if (e.Message == null) return;
                Dictionary<string, object> dict;
                if (e.Message is Dictionary<string, object> d) dict = d;
                else if (e.Message is IDictionary<string, object> id) dict = id.ToDictionary(kv => kv.Key, kv => kv.Value);
                else dict = JObject.Parse(JsonConvert.SerializeObject(e.Message)).ToObject<Dictionary<string, object>>();
                if (dict == null || !dict.ContainsKey("action")) return;
                string action = dict["action"]?.ToString();
                if (!string.IsNullOrEmpty(action)) Dispatcher.InvokeAsync(() => _bridge.HandleMessage(action, dict));
            }
            catch { }
        }
    }

    public class NoContextMenuHandler : IContextMenuHandler
    {
        public void OnBeforeContextMenu(IWebBrowser b, IBrowser br, IFrame f, IContextMenuParams p, IMenuModel m) { m.Clear(); }
        public bool OnContextMenuCommand(IWebBrowser b, IBrowser br, IFrame f, IContextMenuParams p, CefMenuCommand c, CefEventFlags e) => false;
        public void OnContextMenuDismissed(IWebBrowser b, IBrowser br, IFrame f) { }
        public bool RunContextMenu(IWebBrowser b, IBrowser br, IFrame f, IContextMenuParams p, IMenuModel m, IRunContextMenuCallback c) => false;
    }
}
