using System;
using System.Diagnostics;
using TeamProject.Bot;

namespace TeamProject.Cleaning
{
    internal enum CleanResult
    {
        /// <summary>Сектор дочищен до порога.</summary>
        Done,
        /// <summary>Все проходы сделаны, но порог не достигнут.</summary>
        Incomplete,
        /// <summary>Контейнер полон — надо разгрузиться и вернуться.</summary>
        NeedUnload,
        /// <summary>Бот потерял сектор (вышел и не смог вернуться).</summary>
        LeftSector,
    }

    /// <summary>
    /// Чистка сектора «змейкой»: от левого края длинные полосы вперёд до границы сектора,
    /// сдвиг вбок на ширину полосы, разворот, обратно. Граница определяется по смене номера сектора в HUD.
    /// Повороты чередуются (+180/−180), поэтому ошибка калибровки мыши не накапливается.
    /// Если после прохода сектор не дочищен — второй проход поперёк (поворот на 90°).
    /// </summary>
    internal sealed class SectorCleaner
    {
        private readonly BotContext _c;
        private readonly Func<bool> _needUnload;

        public SectorCleaner(BotContext context, Func<bool> needUnload)
        {
            _c = context;
            _needUnload = needUnload;
        }

        public double? LastPercent { get; private set; }

        private enum PassResult { Done, PassComplete, NeedUnload, LeftSector, Timeout }

        public CleanResult Clean(int sectorId)
        {
            var cfg = _c.Cfg;
            var deadline = Stopwatch.StartNew();
            bool holdUse = cfg.HoldUseButton;
            Log.Info($"Чищу сектор {sectorId} (ширина полосы {_c.LaneWidthMs} мс).");
            try
            {
                if (holdUse) _c.Input.KeyDown("LButton");
                if (!string.IsNullOrWhiteSpace(cfg.Keys.LockBrush)) _c.Input.Tap(cfg.Keys.LockBrush);

                bool timedOut = false;
                for (int pass = 0; pass < Math.Max(1, cfg.MaxPasses) && !timedOut; pass++)
                {
                    if (pass > 0)
                    {
                        // Поперечный проход: +90, затем −90 (возврат к исходной ориентации), и т.д.
                        double t = pass % 2 == 1 ? 90 : -90;
                        _c.Input.Turn(t);
                        Log.Info($"Сектор {sectorId}: проход {pass + 1} (поперёк).");
                    }

                    var r = SweepPass(sectorId, deadline);
                    switch (r)
                    {
                        case PassResult.Done: return CleanResult.Done;
                        case PassResult.NeedUnload: return CleanResult.NeedUnload;
                        case PassResult.LeftSector: return CleanResult.LeftSector;
                        case PassResult.Timeout: timedOut = true; break;
                    }
                }

                LastPercent = _c.Hud.ReadCleanedPercent(3);
                if (LastPercent != null && LastPercent >= cfg.CleanThresholdPercent) return CleanResult.Done;
                if (LastPercent != null && LastPercent >= cfg.SoftThresholdPercent)
                {
                    Log.Info($"Сектор {sectorId}: {LastPercent:0.0}% — достаточно (мягкий порог).");
                    return CleanResult.Done;
                }
                Log.Warn($"Сектор {sectorId} не дочищен: {LastPercent?.ToString("0.0") ?? "?"}%.");
                return CleanResult.Incomplete;
            }
            finally
            {
                if (holdUse) _c.Input.KeyUp("LButton");
            }
        }

        private PassResult SweepPass(int sectorId, Stopwatch deadline)
        {
            var k = _c.Cfg.Keys;

            // 1. К левому краю сектора.
            if (!StrafeToEdge(sectorId, k.Left, k.Right)) return PassResult.LeftSector;

            int lanes = 0;
            while (true)
            {
                // 2. Полоса вперёд до границы.
                if (!RunLane(sectorId)) return PassResult.LeftSector;
                lanes++;

                LastPercent = _c.Hud.ReadCleanedPercent(1);
                if (LastPercent != null && LastPercent >= _c.Cfg.CleanThresholdPercent)
                {
                    Log.Good($"Сектор {sectorId} дочищен: {LastPercent:0.0}% ({lanes} полос).");
                    return PassResult.Done;
                }
                if (_needUnload()) return PassResult.NeedUnload;
                if (deadline.Elapsed.TotalMinutes > _c.Cfg.SectorTimeoutMinutes)
                {
                    Log.Warn($"Сектор {sectorId}: истекло время ({_c.Cfg.SectorTimeoutMinutes} мин).");
                    return PassResult.Timeout;
                }

                // 3. Сдвиг вбок в одну и ту же сторону мира: после нечётной полосы — вправо,
                //    после чётной (мы развёрнуты на 180°) — влево.
                string shiftKey = lanes % 2 == 1 ? k.Right : k.Left;
                string backKey = lanes % 2 == 1 ? k.Left : k.Right;
                _c.Input.Hold(shiftKey, _c.LaneWidthMs);
                var s = _c.Hud.ReadSectorStable(3);
                if (s != null && s != sectorId)
                {
                    // Дошли до противоположного края — проход закончен.
                    if (!_c.Input.HoldUntil(backKey, _c.Cfg.BackoffMaxMs, () => _c.Hud.ReadSectorOnce() == sectorId, _c.Cfg.PollIntervalMs))
                        return PassResult.LeftSector;
                    if (lanes % 2 == 0) _c.Input.Turn(-180); // вернуть исходную ориентацию (сумма поворотов = 0)
                    return PassResult.PassComplete;
                }

                // 4. Разворот (чередуем направление, чтобы не копить ошибку).
                _c.Input.Turn(lanes % 2 == 1 ? 180 : -180);
            }
        }

        /// <summary>Идёт вперёд до границы сектора, затем чуть назад внутрь. false — сектор потерян.</summary>
        private bool RunLane(int sectorId)
        {
            var cfg = _c.Cfg;
            int votes = 0;
            bool exited = false;
            var sw = Stopwatch.StartNew();
            _c.Motion.Reset();
            _c.Input.KeyDown(cfg.Keys.Forward);
            try
            {
                while (sw.ElapsedMilliseconds < cfg.LaneMaxMs)
                {
                    _c.Input.Sleep(cfg.PollIntervalMs);
                    var s = _c.Hud.ReadSectorOnce();
                    if (s != null && s != sectorId)
                    {
                        if (++votes >= 2) { exited = true; break; }
                    }
                    else votes = 0;

                    if (_c.Motion.CheckStuck()) break; // препятствие внутри сектора — считаем концом полосы
                }
            }
            finally
            {
                _c.Input.KeyUp(cfg.Keys.Forward);
            }

            if (!exited) return true;
            bool back = _c.Input.HoldUntil(cfg.Keys.Back, cfg.BackoffMaxMs, () => _c.Hud.ReadSectorOnce() == sectorId, cfg.PollIntervalMs);
            if (back) _c.Input.Hold(cfg.Keys.Back, 120);
            return back;
        }

        /// <summary>Сдвиг вбок до края сектора и немного обратно внутрь.</summary>
        private bool StrafeToEdge(int sectorId, string towardKey, string backKey)
        {
            int votes = 0;
            bool reached = _c.Input.HoldUntil(towardKey, _c.Cfg.StrafeToEdgeMaxMs, () =>
            {
                var s = _c.Hud.ReadSectorOnce();
                votes = s != null && s != sectorId ? votes + 1 : 0;
                return votes >= 2;
            }, _c.Cfg.PollIntervalMs);

            if (!reached) return true; // край не найден (упёрлись) — начинаем отсюда
            bool back = _c.Input.HoldUntil(backKey, _c.Cfg.BackoffMaxMs, () => _c.Hud.ReadSectorOnce() == sectorId, _c.Cfg.PollIntervalMs);
            if (back) _c.Input.Hold(backKey, _c.LaneWidthMs / 2);
            return back;
        }
    }
}
