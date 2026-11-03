using System;
using System.IO;
using TeamProject.Core;
using TeamProject.Input;
using TeamProject.Vision;

namespace TeamProject.Bot
{
    /// <summary>Пути к файлам рядом с программой.</summary>
    internal static class Paths
    {
        public static string BaseDir => AppContext.BaseDirectory;
        public static string Config => Path.Combine(BaseDir, "config.json");
        public static string Calibration => Path.Combine(BaseDir, "calibration.json");
        public static string State => Path.Combine(BaseDir, "botstate.json");
        public static string Log => Path.Combine(BaseDir, "bot.log");
        public static string MacrosDir => Path.Combine(BaseDir, "macros");

        public static string Resolve(string relative)
            => Path.IsPathRooted(relative) ? relative : Path.Combine(BaseDir, relative);
    }

    /// <summary>Всё, что нужно частям бота: настройки, состояние, ввод и «зрение».</summary>
    internal sealed class BotContext
    {
        public BotContext(BotConfig cfg, Calibration cal, BotState state, BotControl control)
        {
            Cfg = cfg;
            Cal = cal;
            State = state;
            Control = control;
            Input = new InputController(cfg, cal, control);
            Ocr = new OcrReader(cfg);
            Hud = new HudReader(Ocr, cfg, cal);
            Compass = new CompassReader(cal);
            Fill = new FillGauge(cfg, cal);
            Motion = new MotionDetector(cfg);
            Planner = new SpiralPlanner(state, cfg.CleanThresholdPercent, cfg.MaxRings);
        }

        public BotConfig Cfg { get; }
        public Calibration Cal { get; }
        public BotState State { get; }
        public BotControl Control { get; }
        public InputController Input { get; }
        public OcrReader Ocr { get; }
        public HudReader Hud { get; }
        public CompassReader Compass { get; }
        public FillGauge Fill { get; }
        public MotionDetector Motion { get; }
        public SpiralPlanner Planner { get; }

        public int LaneWidthMs => State.CurrentLaneWidthMs ?? Cfg.LaneWidthMs;

        public void SaveState()
        {
            try { State.Save(Paths.State); }
            catch (IOException ex) { Log.Warn("Не удалось сохранить состояние: " + ex.Message); }
        }
    }
}
