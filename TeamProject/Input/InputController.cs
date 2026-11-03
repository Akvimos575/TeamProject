using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using TeamProject.Core;
using TeamProject.Native;

namespace TeamProject.Input
{
    /// <summary>
    /// Эмуляция клавиатуры и мыши через SendInput (скан-коды — их понимают игры на DirectInput/RawInput).
    /// Все ожидания идут через <see cref="Sleep"/>, который учитывает паузу и остановку:
    /// на паузе зажатые клавиши отпускаются, после паузы — снова зажимаются.
    /// </summary>
    internal sealed class InputController
    {
        private readonly BotControl? _control;
        private readonly Calibration _cal;
        private readonly BotConfig _cfg;
        private readonly HashSet<string> _held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private double _countRemainder;

        public InputController(BotConfig cfg, Calibration cal, BotControl? control)
        {
            _cfg = cfg;
            _cal = cal;
            _control = control;
        }

        /// <summary>Отслеживаемый поворот камеры в градусах (сумма всех поворотов бота).</summary>
        public double Yaw { get; set; }

        public bool IsHeld(string key) => _held.Contains(key);

        // ---------- клавиатура и кнопки мыши ----------

        public void KeyDown(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            Checkpoint();
            SendKey(key, down: true);
            _held.Add(key);
        }

        public void KeyUp(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            SendKey(key, down: false);
            _held.Remove(key);
        }

        public void Tap(string key, int holdMs = 60)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            KeyDown(key);
            try { Sleep(holdMs); }
            finally { KeyUp(key); }
        }

        public void Hold(string key, int ms) => Tap(key, ms);

        /// <summary>Держит клавишу, пока условие не выполнится или не истечёт время. Возвращает true, если условие выполнилось.</summary>
        public bool HoldUntil(string key, int maxMs, Func<bool> condition, int pollMs)
        {
            KeyDown(key);
            try
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < maxMs)
                {
                    Sleep(pollMs);
                    if (condition()) return true;
                }
                return false;
            }
            finally { KeyUp(key); }
        }

        public void ReleaseAll()
        {
            foreach (var k in new List<string>(_held))
            {
                try { SendKey(k, down: false); } catch (ArgumentException) { }
            }
            _held.Clear();
        }

        private static void SendKey(string key, bool down)
        {
            if (KeyMap.IsMouseButton(key))
            {
                bool left = KeyMap.IsLeftMouse(key);
                uint flag = left
                    ? (down ? Win32.MOUSEEVENTF_LEFTDOWN : Win32.MOUSEEVENTF_LEFTUP)
                    : (down ? Win32.MOUSEEVENTF_RIGHTDOWN : Win32.MOUSEEVENTF_RIGHTUP);
                SendMouse(0, 0, flag);
                return;
            }

            var k = KeyMap.Parse(key);
            int vk = (int)k & 0xFFFF;
            ushort scan = (ushort)Win32.MapVirtualKey((uint)vk, Win32.MAPVK_VK_TO_VSC);
            uint flags = Win32.KEYEVENTF_SCANCODE;
            if (!down) flags |= Win32.KEYEVENTF_KEYUP;
            if (KeyMap.IsExtended(k)) flags |= Win32.KEYEVENTF_EXTENDEDKEY;

            var input = new Win32.INPUT
            {
                type = Win32.INPUT_KEYBOARD,
                U = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags } },
            };
            Win32.SendInput(1, new[] { input }, Marshal.SizeOf<Win32.INPUT>());
        }

        private static void SendMouse(int dx, int dy, uint flags)
        {
            var input = new Win32.INPUT
            {
                type = Win32.INPUT_MOUSE,
                U = new Win32.InputUnion { mi = new Win32.MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } },
            };
            Win32.SendInput(1, new[] { input }, Marshal.SizeOf<Win32.INPUT>());
        }

        // ---------- мышь ----------

        /// <summary>Относительное движение мыши (небольшими порциями, чтобы игра не теряла ввод).</summary>
        public void MoveMouse(int dx, int dy)
        {
            int chunk = Math.Max(1, _cfg.TurnChunkCounts);
            while (dx != 0 || dy != 0)
            {
                Checkpoint();
                int sx = Math.Sign(dx) * Math.Min(Math.Abs(dx), chunk);
                int sy = Math.Sign(dy) * Math.Min(Math.Abs(dy), chunk);
                SendMouse(sx, sy, Win32.MOUSEEVENTF_MOVE);
                dx -= sx;
                dy -= sy;
                if (_cfg.TurnChunkDelayMs > 0) Thread.Sleep(_cfg.TurnChunkDelayMs);
            }
        }

        /// <summary>Поворот камеры на заданный угол (вправо = +). Обновляет <see cref="Yaw"/>.</summary>
        public void Turn(double degrees)
        {
            if (!Angles.IsFinite(degrees) || Math.Abs(degrees) < 1e-6) return;
            double exact = degrees * _cal.MouseCountsPerDegree + _countRemainder;
            int counts = (int)Math.Round(exact);
            _countRemainder = exact - counts;
            MoveMouse(counts, 0);
            Yaw += degrees;
        }

        /// <summary>Повернуться к абсолютному (отслеживаемому) углу.</summary>
        public void TurnToYaw(double yaw) => Turn(Angles.Delta(Yaw, yaw));

        public void ClickAt(int x, int y, bool right = false, int holdMs = 60)
        {
            Checkpoint();
            Win32.SetCursorPos(x, y);
            Sleep(40);
            var btn = right ? "RButton" : "LButton";
            KeyDown(btn);
            Sleep(holdMs);
            KeyUp(btn);
        }

        public void SetCursor(int x, int y) => Win32.SetCursorPos(x, y);

        // ---------- ожидание, пауза, стоп ----------

        /// <summary>Пауза, во время которой обрабатываются горячие клавиши.</summary>
        public void Sleep(int ms)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                Checkpoint();
                long left = ms - sw.ElapsedMilliseconds;
                if (left <= 0) break;
                Thread.Sleep((int)Math.Min(left, 20));
            }
        }

        /// <summary>Проверка стопа/паузы. На паузе отпускает всё, после — восстанавливает зажатые клавиши.</summary>
        public void Checkpoint()
        {
            if (_control == null) return;
            if (_control.StopRequested)
            {
                ReleaseAll();
                _control.ThrowIfStopped();
            }
            if (!_control.Paused) return;

            var snapshot = new List<string>(_held);
            foreach (var k in snapshot) SendKey(k, down: false);
            while (_control.Paused)
            {
                if (_control.StopRequested)
                {
                    _held.Clear();
                    _control.ThrowIfStopped();
                }
                Thread.Sleep(50);
            }
            Thread.Sleep(300);
            foreach (var k in snapshot) SendKey(k, down: true);
        }
    }
}
