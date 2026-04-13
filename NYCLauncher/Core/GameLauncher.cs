using System;
using System.Diagnostics;
using System.IO;

namespace NYCLauncher.Core
{
    public class GameLauncher
    {
        private static readonly string SERVER_HOST = Secrets.SERVER_HOST;
        private static readonly int SERVER_PORT = Secrets.SERVER_PORT;

        private readonly SettingsManager _settings;
        private Process _gameProcess;

        public event Action GameExited;

        public GameLauncher(SettingsManager settings)
        {
            _settings = settings;
        }

        public bool IsRunning
        {
            get { try { return _gameProcess != null && !_gameProcess.HasExited; } catch { _gameProcess = null; return false; } }
        }

        public bool Launch()
        {
            return StartGame(SERVER_HOST, SERVER_PORT);
        }


        public void Kill()
        {
            try
            {
                if (_gameProcess != null && !_gameProcess.HasExited)
                {
                    // Use taskkill /T to kill process tree without WMI
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/PID {_gameProcess.Id} /T /F",
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = false
                    })?.WaitForExit(3000);
                }
            }
            catch { }
            _gameProcess = null;
        }

        private bool StartGame(string host, int port)
        {
            if (IsRunning) return false;

            string exe = Path.Combine(_settings.GameDir, "game.exe");
            if (!File.Exists(exe)) return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"nyc://{host}:{port}",
                    WorkingDirectory = _settings.GameDir,
                    UseShellExecute = false
                };
                psi.EnvironmentVariables["NYC_LAUNCHER_AUTH"] = "1";

                _gameProcess = Process.Start(psi);
                _gameProcess.EnableRaisingEvents = true;
                _gameProcess.Exited += (s, e) => { _gameProcess = null; GameExited?.Invoke(); };
                return true;
            }
            catch
            {
                _gameProcess = null;
                return false;
            }
        }
    }
}
