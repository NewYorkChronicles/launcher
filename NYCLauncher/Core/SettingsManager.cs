using System;
using System.IO;
using Newtonsoft.Json;

namespace NYCLauncher.Core
{
    public class LauncherSettings
    {
        public bool VerifyOnLaunch { get; set; } = true;
    }

    public class SettingsManager
    {
        private readonly string _dir;
        private readonly string _path;

        public SettingsManager()
        {
            _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NYCLauncher");
            _path = Path.Combine(_dir, "settings.json");
        }

        public string GameDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game");

        public LauncherSettings Load()
        {
            try
            {
                if (File.Exists(_path))
                    return JsonConvert.DeserializeObject<LauncherSettings>(File.ReadAllText(_path)) ?? new LauncherSettings();
            }
            catch { }
            return new LauncherSettings();
        }

        public void Save(LauncherSettings s)
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_path, JsonConvert.SerializeObject(s, Formatting.Indented));
        }
    }
}
