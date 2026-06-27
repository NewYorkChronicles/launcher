using System;
using System.IO;

namespace NYCLauncher.Core
{
    public class SettingsManager
    {
        public string GameDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game");
    }
}
