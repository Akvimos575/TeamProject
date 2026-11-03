using System;
using System.Diagnostics;
using TeamProject.Cleaning;
using TeamProject.Core;
using TeamProject.Economy;
using TeamProject.Navigation;

namespace TeamProject.Bot
{
    /// <summary>
    /// Главный цикл бота:
    /// 1) определить сектор; 2) если он не дочищен — чистить змейкой;
    /// 3) иначе идти дальше по спирали (по кругу влево, после замыкания кольца — наружу);
    /// 4) когда контейнер полон — сходить к базе, сгрузить пыль в упаковщик, купить улучшения, вернуться.
    /// </summary>
    internal sealed class BotRunner
    {
        private readonly BotContext _c;
        private readonly Navigator _nav;
        private readonly SectorCleaner _cleaner;
        private readonly Unloader _unloader;
        private readonly UpgradeShop _shop;
        private DateTime _lastUnloadUtc = DateTime.UtcNow;
        private int _cleanedThisSession;

        public BotRunner(BotContext context)
        {
            _c = context;
            _nav = new Navigator(context);
            _cleaner = new SectorCleaner(context, NeedUnload);
            _unloader = new Unloader(context);
            _shop = new UpgradeShop(context);
        }

        public void Run()
        {
            var cfg = _c.Cfg;
            if (!_c.Compass.IsConfigured) throw new InvalidOperationException("Шкала-компас не откалибрована (calibrate).");
            if (_c.Cal.SectorRegion.IsEmpty) throw new InvalidOperationException("Область номера сектора не откалибрована (calibrate).");

            if (string.Equals(cfg.Keys.SectorBordersMode, "Toggle", StringComparison.OrdinalIgnoreCase))
            {
                _c.Input.Tap(cfg.Keys.SectorBorders);
                _c.Input.Sleep(400);
            }
            if (!string.IsNullOrWhiteSpace(_c.State.CurrentToolKey)) _c.Input.Tap(_c.State.CurrentToolKey!);

            var start = ReadSector() ?? throw new InvalidOperationException(
                "Не удаётся прочитать номер сектора. Проверьте SectorRegion командой test-hud.");

            if (_c.State.HomeSectorId == null)
            {
                _c.State.HomeSectorId = start;
                _c.Planner.RestartFromHome();
                Log.Good($"Сектор базы: {start} (бот запущен у базы).");
            }
            else if (_c.State.LastSectorId == start && _c.State.Ring > 0)
            {
                Log.Info($"Продолжаю с кольца {_c.State.Ring} из сектора {start}.");
            }
            else if (start != _c.State.HomeSectorId)
            {
                Log.Info("Бот не там, где остановился в прошлый раз — иду к базе и начинаю спираль заново (очищенное пропускается).");
                _nav.GoHome();
                _c.Planner.RestartFromHome();
            }
            else
            {
                _c.Planner.RestartFromHome();
            }
            _c.SaveState();

            var saveTimer = Stopwatch.StartNew();
            int unreadable = 0;

            while (true)
            {
                _c.Input.Checkpoint();

                var sector = ReadSector();
                if (sector == null)
                {
                    if (++unreadable >= 10) throw new InvalidOperationException("Номер сектора не читается слишком долго.");
                    Log.Warn("Номер сектора не прочитан — небольшой шаг и повтор.");
                    _c.Input.Hold(_c.Cfg.Keys.Forward, 300);
                    continue;
                }
                unreadable = 0;
                _c.State.LastSectorId = sector;

                var pct = _c.Hud.ReadCleanedPercent(3);
                var move = _c.Planner.Decide(sector.Value, pct);
                Log.Info($"Сектор {sector}: {(pct?.ToString("0.0") ?? "?")}% | кольцо {_c.Planner.Ring}, пройдено в кольце {_c.Planner.RingVisited.Count}/{SpiralPlanner.ExpectedRingSize(_c.Planner.Ring)} → {move}");

                switch (move)
                {
                    case PlannerMove.CleanHere:
                        var result = _cleaner.Clean(sector.Value);
                        switch (result)
                        {
                            case CleanResult.Done:
                                _c.Planner.MarkCleaned(sector.Value, complete: true);
                                _cleanedThisSession++;
                                Log.Good($"Сектор {sector} очищен. За сессию: {_cleanedThisSession}, всего: {_c.State.CleanedSectors.Count}.");
                                break;
                            case CleanResult.Incomplete:
                                _c.Planner.MarkCleaned(sector.Value, complete: false);
                                break;
                            case CleanResult.NeedUnload:
                                UnloadTrip(sector.Value);
                                break;
                            case CleanResult.LeftSector:
                                Log.Warn($"Потерял сектор {sector} во время чистки — переопределяю положение.");
                                break;
                        }
                        _c.SaveState();
                        break;

                    case PlannerMove.Tangential:
                    {
                        double bearing = SpiralPlanner.TangentBaseBearing(_c.Cfg.CircleLeft, _c.Cfg.TangentInwardBiasDeg);
                        var next = _nav.WalkUntilSectorChange(bearing, sector.Value, _c.Cfg.WalkToNextSectorTimeoutMs);
                        if (next == null)
                        {
                            Log.Warn("По кругу пройти не удалось — пробую сместиться наружу.");
                            next = _nav.WalkUntilSectorChange(180, sector.Value, _c.Cfg.WalkToNextSectorTimeoutMs);
                        }
                        if (next != null) _c.Planner.OnEnterSector(next.Value);
                        break;
                    }

                    case PlannerMove.Outward:
                    {
                        var next = _nav.WalkUntilSectorChange(180, sector.Value, _c.Cfg.WalkToNextSectorTimeoutMs);
                        if (next != null)
                        {
                            _c.Planner.BeginNextRing(next.Value);
                            Log.Good($"Начинаю кольцо {_c.Planner.Ring} (сектор {next}).");
                        }
                        _c.SaveState();
                        break;
                    }

                    case PlannerMove.Finished:
                        Log.Good($"Достигнут предел колец ({_c.Cfg.MaxRings}). Готово.");
                        _c.SaveState();
                        return;
                }

                if (move != PlannerMove.CleanHere && NeedUnload())
                {
                    var here = ReadSector();
                    if (here != null) UnloadTrip(here.Value);
                }

                if (saveTimer.Elapsed.TotalSeconds > 30)
                {
                    _c.SaveState();
                    saveTimer.Restart();
                }
            }
        }

        /// <summary>Номер сектора; при неудаче — нажать клавишу сканирования (если включено) и повторить.</summary>
        private int? ReadSector()
        {
            var s = _c.Hud.ReadSectorStable();
            if (s != null) return s;
            if (string.Equals(_c.Cfg.Keys.SectorBordersMode, "Scan", StringComparison.OrdinalIgnoreCase))
            {
                _c.Input.Tap(_c.Cfg.Keys.SectorBorders);
                _c.Input.Sleep(500);
                s = _c.Hud.ReadSectorStable();
            }
            return s;
        }

        private bool NeedUnload()
        {
            var cfg = _c.Cfg;
            if (string.Equals(cfg.UnloadMode, "Never", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(cfg.UnloadMode, "FillGauge", StringComparison.OrdinalIgnoreCase))
            {
                var f = _c.Fill.Fraction();
                if (f != null) return f.Value >= cfg.FillUnloadFraction;
            }
            return (DateTime.UtcNow - _lastUnloadUtc).TotalMinutes >= cfg.UnloadEveryMinutes;
        }

        /// <summary>Сходить к упаковщику, разгрузиться, купить улучшения и вернуться в сектор.</summary>
        private void UnloadTrip(int returnSector)
        {
            Log.Info("Контейнер полон — иду разгружаться.");
            _c.Input.KeyUp("LButton");

            _nav.FaceBaseAt(0);
            _nav.Bearing();
            double? polar = _nav.LastPolarDeg;
            int ring = _c.State.Ring;

            if (!_nav.GoHome()) Log.Warn("До базы дойти не удалось — пробую разгрузиться здесь.");
            _unloader.Unload();
            _lastUnloadUtc = DateTime.UtcNow;
            _shop.TryBuy();
            _c.SaveState();

            if (polar != null && returnSector != _c.State.HomeSectorId)
            {
                if (!_nav.ReturnTo(returnSector, polar.Value, ring))
                    Log.Warn($"Не нашёл сектор {returnSector} на обратном пути — продолжаю спираль с текущего места.");
            }
        }
    }
}
