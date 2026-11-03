using System;
using System.Diagnostics;
using System.Threading;
using TeamProject.Bot;
using TeamProject.Native;

namespace TeamProject.Input
{
    /// <summary>
    /// Горячие клавиши (пауза/стоп/сохранение) и проверка, что окно игры активно.
    /// Опрос клавиатуры идёт в отдельном потоке, поэтому F9 срабатывает всегда.
    /// </summary>
    internal sealed class BotControl : IDisposable
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly int _pauseVk, _stopVk, _saveVk;
        private readonly string _gameProcess;
        private readonly bool _pauseOnFocusLoss;
        private Thread? _thread;
        private volatile bool _userPaused;
        private volatile bool _focusLost;
        private int[] _gamePids = Array.Empty<int>();
        private DateTime _pidsRefreshed = DateTime.MinValue;

        public BotControl(string pauseKey, string stopKey, string saveKey, string gameProcess, bool pauseOnFocusLoss)
        {
            _pauseVk = KeyMap.VirtualKey(pauseKey);
            _stopVk = KeyMap.VirtualKey(stopKey);
            _saveVk = KeyMap.VirtualKey(saveKey);
            _gameProcess = gameProcess ?? "";
            _pauseOnFocusLoss = pauseOnFocusLoss && !string.IsNullOrWhiteSpace(gameProcess);
        }

        public event Action? SaveRequested;

        public CancellationToken Token => _cts.Token;
        public bool StopRequested => _cts.IsCancellationRequested;
        public bool Paused => _userPaused || _focusLost;

        public void Start()
        {
            _thread = new Thread(Poll) { IsBackground = true, Name = "BotHotkeys" };
            _thread.Start();
        }

        public void RequestStop() => _cts.Cancel();

        /// <summary>Бросает OperationCanceledException, если нажат стоп.</summary>
        public void ThrowIfStopped() => _cts.Token.ThrowIfCancellationRequested();

        private void Poll()
        {
            bool pausePrev = false, savePrev = false;
            while (!_cts.IsCancellationRequested)
            {
                if (Win32.IsKeyDown(_stopVk))
                {
                    Log.Warn("Нажата клавиша остановки — бот останавливается.");
                    _cts.Cancel();
                    break;
                }

                bool p = Win32.IsKeyDown(_pauseVk);
                if (p && !pausePrev)
                {
                    _userPaused = !_userPaused;
                    Log.Warn(_userPaused ? "ПАУЗА (повторное нажатие — продолжить)" : "Продолжаем");
                }
                pausePrev = p;

                bool s = Win32.IsKeyDown(_saveVk);
                if (s && !savePrev) SaveRequested?.Invoke();
                savePrev = s;

                if (_pauseOnFocusLoss)
                {
                    bool lost = !IsGameFocused();
                    if (lost != _focusLost)
                    {
                        _focusLost = lost;
                        Log.Warn(lost ? "Окно игры не активно — пауза." : "Окно игры снова активно.");
                    }
                }

                Thread.Sleep(30);
            }
        }

        public bool IsGameFocused()
        {
            if (string.IsNullOrWhiteSpace(_gameProcess)) return true;
            if ((DateTime.UtcNow - _pidsRefreshed).TotalSeconds > 3)
            {
                try
                {
                    var procs = Process.GetProcessesByName(_gameProcess);
                    _gamePids = Array.ConvertAll(procs, p => p.Id);
                    foreach (var p in procs) p.Dispose();
                }
                catch (InvalidOperationException) { _gamePids = Array.Empty<int>(); }
                _pidsRefreshed = DateTime.UtcNow;
            }
            var hwnd = Win32.GetForegroundWindow();
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            return Array.IndexOf(_gamePids, (int)pid) >= 0;
        }

        /// <summary>Пытается сделать окно игры активным.</summary>
        public bool FocusGame()
        {
            if (string.IsNullOrWhiteSpace(_gameProcess)) return false;
            var procs = Process.GetProcessesByName(_gameProcess);
            try
            {
                foreach (var p in procs)
                {
                    if (p.MainWindowHandle != IntPtr.Zero && Win32.SetForegroundWindow(p.MainWindowHandle)) return true;
                }
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }
            return false;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _thread?.Join(500);
            _cts.Dispose();
        }
    }
}
