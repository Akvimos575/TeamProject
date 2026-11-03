using System;
using System.Windows.Forms;

namespace TeamProject.Input
{
    /// <summary>Перевод названий клавиш из конфига ("W", "Space", "F8", "1", "LButton") в виртуальные коды.</summary>
    internal static class KeyMap
    {
        public static bool IsMouseButton(string key)
            => key.Equals("LButton", StringComparison.OrdinalIgnoreCase)
            || key.Equals("RButton", StringComparison.OrdinalIgnoreCase)
            || key.Equals("LMB", StringComparison.OrdinalIgnoreCase)
            || key.Equals("RMB", StringComparison.OrdinalIgnoreCase);

        public static bool IsLeftMouse(string key)
            => key.Equals("LButton", StringComparison.OrdinalIgnoreCase) || key.Equals("LMB", StringComparison.OrdinalIgnoreCase);

        public static Keys Parse(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Пустое имя клавиши");
            var n = name.Trim();
            if (n.Length == 1 && char.IsDigit(n[0])) n = "D" + n;
            switch (n.ToLowerInvariant())
            {
                case "ctrl": case "control": case "lctrl": n = "LControlKey"; break;
                case "shift": case "lshift": n = "LShiftKey"; break;
                case "alt": case "lalt": n = "LMenu"; break;
                case "esc": n = "Escape"; break;
                case "enter": n = "Return"; break;
                case "lmb": n = "LButton"; break;
                case "rmb": n = "RButton"; break;
            }
            if (Enum.TryParse<Keys>(n, ignoreCase: true, out var key)) return key;
            throw new ArgumentException($"Неизвестная клавиша \"{name}\". Используйте имена из System.Windows.Forms.Keys (W, Space, F8, LShiftKey, D1...).");
        }

        public static int VirtualKey(string name) => (int)Parse(name) & 0xFFFF;

        public static bool IsExtended(Keys k) => k switch
        {
            Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Insert or Keys.Delete
                or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.RControlKey
                or Keys.RMenu or Keys.Divide or Keys.NumLock => true,
            _ => false,
        };

        /// <summary>Имя клавиши для записи в макрос.</summary>
        public static string Name(Keys k)
        {
            var s = k.ToString();
            if (s.Length == 2 && s[0] == 'D' && char.IsDigit(s[1])) return s.Substring(1);
            return s;
        }
    }
}
