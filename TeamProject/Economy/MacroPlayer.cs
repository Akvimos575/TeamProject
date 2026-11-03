using System;
using System.IO;
using TeamProject.Bot;
using TeamProject.Core;
using TeamProject.Input;

namespace TeamProject.Economy
{
    /// <summary>Проигрывание записанного макроса (клики по меню, нажатия клавиш).</summary>
    internal static class MacroPlayer
    {
        public static bool TryLoad(string file, out Macro macro)
        {
            macro = new Macro();
            var path = Paths.Resolve(file);
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(path)) return false;
            try
            {
                macro = Macro.Load(path);
                return macro.Steps.Count > 0;
            }
            catch (Exception ex) when (ex is IOException || ex is System.Text.Json.JsonException)
            {
                Log.Warn($"Не удалось прочитать макрос {file}: {ex.Message}");
                return false;
            }
        }

        public static void Play(InputController input, Macro macro)
        {
            Log.Info($"Макрос «{macro.Name}»: {macro.Steps.Count} шагов.");
            try
            {
                foreach (var step in macro.Steps)
                {
                    if (step.DelayMs > 0) input.Sleep(Math.Min(step.DelayMs, 10000));
                    switch (step.Kind)
                    {
                        case "KeyDown": if (step.Key != null) input.KeyDown(step.Key); break;
                        case "KeyUp": if (step.Key != null) input.KeyUp(step.Key); break;
                        case "LeftDown": input.SetCursor(step.X, step.Y); input.Sleep(30); input.KeyDown("LButton"); break;
                        case "LeftUp": input.SetCursor(step.X, step.Y); input.KeyUp("LButton"); break;
                        case "RightDown": input.SetCursor(step.X, step.Y); input.Sleep(30); input.KeyDown("RButton"); break;
                        case "RightUp": input.SetCursor(step.X, step.Y); input.KeyUp("RButton"); break;
                        default: Log.Warn($"Неизвестный шаг макроса: {step.Kind}"); break;
                    }
                }
            }
            finally
            {
                input.ReleaseAll();
            }
        }
    }
}
