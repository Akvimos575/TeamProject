using System;
using System.IO;
using System.Threading;
using TeamProject.Bot;
using TeamProject.Core;
using TeamProject.Input;
using TeamProject.Native;
using TeamProject.Vision;

namespace TeamProject.Setup
{
    /// <summary>
    /// Интерактивная калибровка: пользователь наводит курсор на нужные места экрана игры и нажимает F6
    /// (F7 — пропустить шаг и оставить прежнее значение). Результат — calibration.json.
    /// </summary>
    internal static class Calibrator
    {
        private const int VK_F6 = 0x75, VK_F7 = 0x76, VK_F9 = 0x78;

        public static void Run(Core.Calibration cal, BotConfig cfg)
        {
            Console.WriteLine();
            Console.WriteLine("=== Калибровка экрана ===");
            Console.WriteLine("Переключитесь в игру (оконный режим без рамки). Наводите курсор мыши на указанные места");
            Console.WriteLine("и нажимайте F6. F7 — пропустить шаг (оставить прежнее значение). F9 — прервать.");
            Console.WriteLine("Совет: откройте Escape-меню игры, чтобы курсор был свободен, но HUD оставался виден.");
            Console.WriteLine();

            cal.SectorRegion = AskRegion("Область с НОМЕРОМ СЕКТОРА (правый верхний угол)", cal.SectorRegion);
            cal.PercentRegion = AskRegion("Область с ПРОЦЕНТОМ очистки (F7 — если он в той же области, что и номер)", cal.PercentRegion, allowClear: true);
            cal.CompassRegion = AskRegion("Вся ШКАЛА-КОМПАС вверху экрана", cal.CompassRegion);
            var center = AskPoint("ЦЕНТР шкалы-компаса (отметка направления взгляда). F7 — центр области", allowSkip: true);
            if (center != null) cal.CompassCenterX = center.Value.X;
            var marker = AskColor("МЕТКА БАЗЫ на шкале (наведите точно на цветную метку)", cal.BaseMarkerColor);
            if (marker != null) cal.BaseMarkerColor = marker;
            cal.MoneyRegion = AskRegion("Область с ДЕНЬГАМИ (F7 — пропустить, тогда покупки не работают)", cal.MoneyRegion);
            cal.FillRegion = AskRegion("ШКАЛА ЗАПОЛНЕНИЯ контейнера инструмента (F7 — разгрузка по таймеру)", cal.FillRegion);
            if (!cal.FillRegion.IsEmpty)
            {
                var fill = AskColor("ЗАПОЛНЕННАЯ часть шкалы контейнера (цвет)", cal.FillColor);
                if (fill != null) cal.FillColor = fill;
            }
            Console.WriteLine("Для следующего шага подойдите к упаковщику так, чтобы появилась подсказка «Load packager» (E).");
            cal.PromptRegion = AskRegion("Область ПОДСКАЗКИ взаимодействия с упаковщиком", cal.PromptRegion);

            cal.Save(Paths.Calibration);
            Console.WriteLine($"Сохранено: {Paths.Calibration}");
            SaveDebugImages(cal);
            Console.WriteLine("Теперь выполните: MoonSweeperBot calibrate-turn (стоя лицом к базе), затем test-hud для проверки.");
        }

        public static void SaveDebugImages(Core.Calibration cal)
        {
            var dir = Path.Combine(Paths.BaseDir, "debug");
            Directory.CreateDirectory(dir);
            void Save(string name, ScreenRect r)
            {
                if (r.IsEmpty) return;
                try { ScreenCapture.SavePng(r, Path.Combine(dir, name + ".png")); }
                catch (Exception ex) { Console.WriteLine($"  не удалось сохранить {name}: {ex.Message}"); }
            }
            Save("sector", cal.SectorRegion);
            Save("percent", cal.PercentRegion);
            Save("compass", cal.CompassRegion);
            Save("money", cal.MoneyRegion);
            Save("fill", cal.FillRegion);
            Save("prompt", cal.PromptRegion);
            Console.WriteLine($"Снимки областей сохранены в {dir} — проверьте, что на них нужный текст целиком.");
        }

        private static ScreenRect AskRegion(string title, ScreenRect current, bool allowClear = false)
        {
            Console.WriteLine($"[{title}] сейчас: {current}");
            var a = AskPoint("  левый верхний угол", allowSkip: true);
            if (a == null) return allowClear ? new ScreenRect() : current;
            var b = AskPoint("  правый нижний угол", allowSkip: false)!;
            var r = ScreenRect.FromCorners(a.Value.X, a.Value.Y, b.Value.X, b.Value.Y);
            Console.WriteLine($"  → {r}");
            return r;
        }

        private static ColorSpec? AskColor(string title, ColorSpec current)
        {
            Console.WriteLine($"[{title}] сейчас: {current}");
            var p = AskPoint("  наведите на цвет", allowSkip: true);
            if (p == null) return null;
            // Среднее по 3×3 пикселям.
            int r = 0, g = 0, b = 0, n = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var c = Win32.ScreenPixel(p.Value.X + dx, p.Value.Y + dy);
                r += c.R; g += c.G; b += c.B; n++;
            }
            var spec = new ColorSpec { R = r / n, G = g / n, B = b / n, Tolerance = current.IsSet ? current.Tolerance : 40 };
            Console.WriteLine($"  → {spec}");
            return spec;
        }

        private static (int X, int Y)? AskPoint(string title, bool allowSkip)
        {
            Console.Write($"{title}: F6{(allowSkip ? " / F7 пропустить" : "")} ... ");
            while (true)
            {
                if (Win32.IsKeyDown(VK_F9)) throw new OperationCanceledException("Калибровка прервана.");
                if (Win32.IsKeyDown(VK_F6))
                {
                    var p = Win32.CursorPosition();
                    WaitRelease(VK_F6);
                    Console.WriteLine($"({p.X}, {p.Y})");
                    return p;
                }
                if (allowSkip && Win32.IsKeyDown(VK_F7))
                {
                    WaitRelease(VK_F7);
                    Console.WriteLine("пропущено");
                    return null;
                }
                Thread.Sleep(20);
            }
        }

        private static void WaitRelease(int vk)
        {
            while (Win32.IsKeyDown(vk)) Thread.Sleep(10);
            Thread.Sleep(80);
        }

        /// <summary>
        /// Автокалибровка поворота: сколько отсчётов мыши в 1°, и сколько градусов в 1 пикселе шкалы.
        /// Бот поворачивается полный круг и ловит момент, когда метка базы вернулась в центр шкалы.
        /// </summary>
        public static void CalibrateTurn(Core.Calibration cal, BotConfig cfg, InputController input, CompassReader compass)
        {
            if (!compass.IsConfigured) throw new InvalidOperationException("Сначала выполните calibrate (шкала и цвет метки).");
            int center = cal.EffectiveCompassCenterX;
            var region = cal.CompassRegion;

            Console.WriteLine("Встаньте так, чтобы метка базы была видна на шкале. Переключитесь в игру — старт через 5 с.");
            input.Sleep(5000);

            var x0 = compass.MarkerScreenX() ?? throw new InvalidOperationException("Метка базы не видна на шкале. Повернитесь к базе и проверьте цвет метки (test-hud).");

            // 1. Пикселей шкалы на один отсчёт мыши.
            int c = 40;
            double k = 0;
            while (c <= 3000)
            {
                input.MoveMouse(c, 0);
                input.Sleep(250);
                var x1 = compass.MarkerScreenX();
                input.MoveMouse(-c, 0);
                input.Sleep(250);
                if (x1 != null && Math.Abs(x1.Value - x0) >= 8)
                {
                    k = (x1.Value - x0) / c;
                    break;
                }
                c *= 2;
            }
            if (k == 0) throw new InvalidOperationException("Метка не сдвигается при повороте мыши. Игра в фокусе? Курсор не в меню?");
            Console.WriteLine($"Сдвиг метки: {k:0.0000} px на отсчёт мыши.");
            if (k > 0) Console.WriteLine("Внимание: метка движется в сторону поворота — шкала инвертирована, результат может быть неточным.");

            // 2. Навести метку в центр.
            for (int i = 0; i < 6; i++)
            {
                var x = compass.MarkerScreenX();
                if (x == null) break;
                int e = (int)Math.Round(-(x.Value - center) / k);
                if (Math.Abs(e) < 2) break;
                input.MoveMouse(e, 0);
                input.Sleep(200);
            }

            // 3. Полный оборот вправо шагами, пока метка не вернётся в центр.
            int step = Math.Max(5, (int)(region.Width / 6.0 / Math.Abs(k)));
            long total = 0;
            bool hidden = false;
            double? prev = compass.MarkerScreenX();
            long counts360 = 0;
            while (total < 2_000_000)
            {
                input.MoveMouse(step, 0);
                total += step;
                input.Sleep(120);
                var x = compass.MarkerScreenX();
                bool wrapped = x != null && prev != null && Math.Abs(x.Value - prev.Value) > region.Width / 2.0;
                if (x == null) hidden = true;
                else if ((hidden || wrapped) && total > step * 2)
                {
                    counts360 = total + (long)Math.Round(-(x.Value - center) / k);
                    break;
                }
                prev = x;
            }
            if (counts360 <= 0) throw new InvalidOperationException("Не удалось измерить полный оборот.");

            input.MoveMouse((int)(counts360 - total), 0);
            cal.MouseCountsPerDegree = counts360 / 360.0;
            cal.CompassDegreesPerPixel = 1.0 / (Math.Abs(k) * cal.MouseCountsPerDegree);
            cal.Save(Paths.Calibration);
            Console.WriteLine($"Готово: {cal.MouseCountsPerDegree:0.000} отсчётов мыши на 1°, {cal.CompassDegreesPerPixel:0.000}° на пиксель шкалы.");
            Console.WriteLine($"Шкала охватывает примерно ±{compass.HalfSpanDeg:0}° от центра.");
        }
    }
}
