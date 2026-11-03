using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using TeamProject.Bot;
using TeamProject.Core;
using TeamProject.Input;
using TeamProject.Native;

namespace TeamProject.Setup
{
    /// <summary>
    /// Запись макроса (например, покупки улучшения): нажатия клавиш и клики мышью с координатами.
    /// F12 — закончить запись.
    /// </summary>
    internal static class MacroRecorder
    {
        private static readonly Keys[] Watched = BuildWatched();

        private static Keys[] BuildWatched()
        {
            var list = new List<Keys>();
            for (var k = Keys.A; k <= Keys.Z; k++) list.Add(k);
            for (var k = Keys.D0; k <= Keys.D9; k++) list.Add(k);
            for (var k = Keys.F1; k <= Keys.F7; k++) list.Add(k);
            list.AddRange(new[]
            {
                Keys.Escape, Keys.Tab, Keys.Return, Keys.Space, Keys.Back, Keys.LShiftKey, Keys.LControlKey,
                Keys.LMenu, Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.LButton, Keys.RButton,
            });
            return list.ToArray();
        }

        public static void Record(string name)
        {
            var path = Path.Combine(Paths.MacrosDir, name + ".json");
            Console.WriteLine($"Запись макроса «{name}» начнётся через 3 с. Переключитесь в игру и сделайте покупку");
            Console.WriteLine("(открыть меню → выбрать улучшение → купить → закрыть меню). F12 — закончить.");
            Thread.Sleep(3000);
            Console.WriteLine("Запись...");

            var macro = new Macro { Name = name };
            var down = new HashSet<Keys>();
            var sw = Stopwatch.StartNew();
            long last = 0;
            const int VK_F12 = 0x7B;

            while (!Win32.IsKeyDown(VK_F12))
            {
                foreach (var k in Watched)
                {
                    bool isDown = Win32.IsKeyDown((int)k);
                    bool was = down.Contains(k);
                    if (isDown == was) continue;
                    if (isDown) down.Add(k); else down.Remove(k);

                    var pos = Win32.CursorPosition();
                    string kind;
                    string? key = null;
                    if (k == Keys.LButton) kind = isDown ? "LeftDown" : "LeftUp";
                    else if (k == Keys.RButton) kind = isDown ? "RightDown" : "RightUp";
                    else
                    {
                        kind = isDown ? "KeyDown" : "KeyUp";
                        key = KeyMap.Name(k);
                    }
                    long now = sw.ElapsedMilliseconds;
                    var step = new MacroStep { DelayMs = (int)(now - last), Kind = kind, Key = key, X = pos.X, Y = pos.Y };
                    last = now;
                    macro.Steps.Add(step);
                    Console.WriteLine("  " + step);
                }
                Thread.Sleep(5);
            }

            // Отпускания, которые не успели записаться, добавляем принудительно.
            foreach (var k in down)
            {
                var pos = Win32.CursorPosition();
                macro.Steps.Add(k switch
                {
                    Keys.LButton => new MacroStep { Kind = "LeftUp", X = pos.X, Y = pos.Y },
                    Keys.RButton => new MacroStep { Kind = "RightUp", X = pos.X, Y = pos.Y },
                    _ => new MacroStep { Kind = "KeyUp", Key = KeyMap.Name(k) },
                });
            }

            macro.Save(path);
            Console.WriteLine($"Сохранено {macro.Steps.Count} шагов: {path}");
            Console.WriteLine($"Укажите в config.json для улучшения: \"Macro\": \"macros/{name}.json\" и его цену \"Cost\".");
        }
    }
}
