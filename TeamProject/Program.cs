using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using TeamProject.Bot;
using TeamProject.Core;
using TeamProject.Economy;
using TeamProject.Input;
using TeamProject.Native;
using TeamProject.Setup;
using TeamProject.Vision;

namespace TeamProject
{
    /// <summary>
    /// MoonSweeperBot — бот, который сам играет в Moon Sweepers: чистит сектора спиралью от базы,
    /// разгружается в упаковщик и покупает улучшения.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Win32.SetProcessDPIAware(); // координаты экрана в физических пикселях
            Log.Init(Paths.Log);

            var command = args.Length > 0 ? args[0].ToLowerInvariant() : "menu";
            try
            {
                if (command == "menu") command = AskCommand();
                var cfg = BotConfig.Load(Paths.Config);
                var cal = Core.Calibration.Load(Paths.Calibration);

                switch (command)
                {
                    case "run": return RunBot(cfg, cal);
                    case "calibrate": Calibrator.Run(cal, cfg); return 0;
                    case "calibrate-turn": return WithControl(cfg, cal, ctx => Calibrator.CalibrateTurn(cal, cfg, ctx.Input, ctx.Compass));
                    case "test-hud": return TestHud(cfg, cal);
                    case "record-macro":
                        if (args.Length < 2) { Console.WriteLine("Укажите имя: record-macro <имя>"); return 1; }
                        MacroRecorder.Record(args[1]);
                        return 0;
                    case "play-macro":
                        if (args.Length < 2) { Console.WriteLine("Укажите имя: play-macro <имя>"); return 1; }
                        return WithControl(cfg, cal, ctx =>
                        {
                            if (!MacroPlayer.TryLoad(Path.Combine("macros", args[1] + ".json"), out var m))
                                throw new FileNotFoundException("Макрос не найден или пуст: " + args[1]);
                            Console.WriteLine("Переключитесь в игру — старт через 3 с.");
                            ctx.Input.Sleep(3000);
                            MacroPlayer.Play(ctx.Input, m);
                        });
                    case "reset-state":
                        if (File.Exists(Paths.State)) File.Move(Paths.State, Paths.State + ".bak", overwrite: true);
                        Console.WriteLine("Прогресс сброшен (старый файл: botstate.json.bak). Следующий run начнётся от базы.");
                        return 0;
                    default:
                        PrintHelp();
                        return 1;
                }
            }
            catch (OperationCanceledException)
            {
                Log.Warn("Остановлено.");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
                Debug.WriteLine(ex);
                File.AppendAllText(Paths.Log, ex + Environment.NewLine);
                return 2;
            }
        }

        private static string AskCommand()
        {
            PrintHelp();
            Console.Write("Введите команду (например run): ");
            var line = Console.ReadLine()?.Trim() ?? "";
            return line.Length == 0 ? "run" : line.ToLowerInvariant();
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"
MoonSweeperBot — автоигрок для Moon Sweepers

Команды:
  calibrate          — указать на экране области HUD (сектор, %, компас, деньги, подсказка)
  calibrate-turn     — автонастройка поворота мыши и шкалы-компаса (стоять лицом к базе)
  test-hud           — показывать в реальном времени, что бот видит (проверка калибровки)
  record-macro <имя> — записать макрос покупки (F12 — стоп)
  play-macro <имя>   — проиграть макрос для проверки
  run                — запустить бота (стартовать стоя у базы)
  reset-state        — сбросить сохранённый прогресс

Во время работы: F8 — пауза/продолжить, F9 — остановка, F10 — сохранить прогресс.
");
        }

        private static int RunBot(BotConfig cfg, Core.Calibration cal)
        {
            var state = BotState.Load(Paths.State);
            using var control = new BotControl(cfg.Keys.PauseHotkey, cfg.Keys.StopHotkey, cfg.Keys.SaveHotkey, cfg.GameProcessName, cfg.PauseWhenGameNotFocused);
            var ctx = new BotContext(cfg, cal, state, control);
            control.SaveRequested += () =>
            {
                ctx.SaveState();
                Log.Good("Прогресс сохранён.");
            };

            Log.Info($"OCR: {ctx.Ocr.LanguageTag}. Сохранённый прогресс: {state.CleanedSectors.Count} секторов, кольцо {state.Ring}.");
            if (control.FocusGame()) Log.Info("Окно игры активировано.");
            Log.Info($"Старт через {cfg.StartDelaySeconds} с — переключитесь в игру. F8 пауза, F9 стоп.");
            Thread.Sleep(Math.Max(0, cfg.StartDelaySeconds) * 1000);
            control.Start();

            try
            {
                new BotRunner(ctx).Run();
                return 0;
            }
            finally
            {
                ctx.Input.ReleaseAll();
                ctx.SaveState();
                Log.Info("Прогресс сохранён, все клавиши отпущены.");
            }
        }

        private static int WithControl(BotConfig cfg, Core.Calibration cal, Action<BotContext> action)
        {
            using var control = new BotControl(cfg.Keys.PauseHotkey, cfg.Keys.StopHotkey, cfg.Keys.SaveHotkey, "", false);
            var ctx = new BotContext(cfg, cal, BotState.Load(Paths.State), control);
            control.Start();
            try
            {
                action(ctx);
                return 0;
            }
            finally
            {
                ctx.Input.ReleaseAll();
            }
        }

        private static int TestHud(BotConfig cfg, Core.Calibration cal)
        {
            using var control = new BotControl(cfg.Keys.PauseHotkey, cfg.Keys.StopHotkey, cfg.Keys.SaveHotkey, "", false);
            var ctx = new BotContext(cfg, cal, BotState.Load(Paths.State), control);
            control.Start();
            Calibrator.SaveDebugImages(cal);
            Console.WriteLine($"OCR: {ctx.Ocr.LanguageTag}. Показываю, что видит бот. F9 — выход.");
            while (!control.StopRequested)
            {
                var sw = Stopwatch.StartNew();
                var hud = ctx.Hud.Read();
                var bearing = ctx.Compass.BaseBearing();
                var fill = ctx.Fill.Fraction();
                var money = ctx.Hud.ReadMoney();
                bool prompt = ctx.Hud.PackagerPromptVisible();
                double motion = ctx.Motion.MeasureDiff();
                Console.WriteLine(
                    $"{hud} | база: {(bearing == null ? "не видна" : bearing.Value.ToString("+0.0;-0.0") + "°")}" +
                    $" | контейнер: {(fill == null ? "-" : (fill.Value * 100).ToString("0") + "%")}" +
                    $" | деньги: {money?.ToString() ?? "?"} [{ctx.Hud.ReadMoneyText()}]" +
                    $" | упаковщик: {(prompt ? "ДА" : "нет")} [{ctx.Hud.ReadPromptText()}]" +
                    $" | движение: {motion:0.0} | {sw.ElapsedMilliseconds} мс");
                Thread.Sleep(500);
            }
            return 0;
        }
    }
}
