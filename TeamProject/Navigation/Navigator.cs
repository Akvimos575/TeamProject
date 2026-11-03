using System;
using System.Diagnostics;
using TeamProject.Bot;
using TeamProject.Core;

namespace TeamProject.Navigation
{
    /// <summary>
    /// Перемещение по Луне на основе двух «датчиков»: метки базы на шкале-компасе (направление)
    /// и номера сектора в HUD (где мы). Поворот камеры — мышью через SendInput.
    /// </summary>
    internal sealed class Navigator
    {
        private readonly BotContext _c;
        private readonly Random _rng = new Random();

        public Navigator(BotContext context) => _c = context;

        /// <summary>Полярный угол вокруг базы при последнем чтении компаса (в системе отсчёта Yaw бота).</summary>
        public double? LastPolarDeg { get; private set; }

        private string Fwd => _c.Cfg.Keys.Forward;

        /// <summary>Угол на базу с обновлением <see cref="LastPolarDeg"/>.</summary>
        public double? Bearing()
        {
            var b = _c.Compass.BaseBearing();
            if (b != null) LastPolarDeg = Angles.PolarAroundBase(_c.Input.Yaw, b.Value);
            return b;
        }

        /// <summary>
        /// Повернуться так, чтобы база была под углом <paramref name="desired"/> к взгляду
        /// (0 — лицом к базе, 180 — спиной, −90 — база слева). Если нужный угол вне шкалы,
        /// бот сначала точно наводится на базу, а затем поворачивается «вслепую».
        /// </summary>
        public bool FaceBaseAt(double desired)
        {
            desired = Angles.Normalize180(desired);
            double half = _c.Compass.HalfSpanDeg;
            bool blind = Math.Abs(desired) > half - 5;
            double target = blind ? 0 : desired;
            double searched = 0;

            for (int i = 0; i < 16; i++)
            {
                var b = Bearing();
                if (b == null)
                {
                    if (searched >= 360)
                    {
                        Log.Warn("Метка базы не найдена на шкале после полного оборота.");
                        return false;
                    }
                    double step = Angles.Clamp(2 * half - 10, 30, 120);
                    _c.Input.Turn(step);
                    searched += step;
                    _c.Input.Sleep(80);
                    continue;
                }

                double d = Angles.TurnToPutBaseAt(b.Value, target);
                if (Math.Abs(d) <= _c.Cfg.FaceToleranceDeg)
                {
                    if (blind) _c.Input.Turn(Angles.TurnToPutBaseAt(b.Value, desired));
                    return true;
                }
                _c.Input.Turn(d);
                _c.Input.Sleep(50);
            }
            return true;
        }

        /// <summary>
        /// Идти, держа базу под углом <paramref name="desiredBaseBearing"/>, пока номер сектора не сменится.
        /// Возвращает новый сектор или null (таймаут / не удалось выбраться).
        /// </summary>
        public int? WalkUntilSectorChange(double desiredBaseBearing, int fromSector, int timeoutMs)
        {
            if (!FaceBaseAt(desiredBaseBearing)) Log.Warn("Иду без точной ориентации по базе.");
            var sw = Stopwatch.StartNew();
            long lastCorrect = 0;
            int? candidate = null;
            int votes = 0, unsticks = 0;
            _c.Motion.Reset();
            _c.Input.KeyDown(Fwd);
            try
            {
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    _c.Input.Sleep(_c.Cfg.PollIntervalMs);
                    var s = _c.Hud.ReadSectorOnce();
                    if (s != null && s != fromSector)
                    {
                        if (s == candidate) votes++;
                        else { candidate = s; votes = 1; }
                        if (votes >= 2) return s;
                    }
                    else
                    {
                        candidate = null;
                        votes = 0;
                    }

                    if (sw.ElapsedMilliseconds - lastCorrect >= _c.Cfg.NavCorrectEveryMs)
                    {
                        FaceBaseAt(desiredBaseBearing);
                        lastCorrect = sw.ElapsedMilliseconds;
                    }

                    if (_c.Motion.CheckStuck())
                    {
                        if (++unsticks > 6) { Log.Warn("Слишком много застреваний подряд."); return null; }
                        Unstick();
                        FaceBaseAt(desiredBaseBearing);
                        _c.Input.KeyDown(Fwd);
                    }
                }
                Log.Warn($"Не дошёл до следующего сектора за {timeoutMs / 1000} с.");
                return null;
            }
            finally
            {
                _c.Input.KeyUp(Fwd);
            }
        }

        /// <summary>
        /// Дойти до базы по метке на шкале. Прибытие: мы в секторе базы и метка «ушла» за спину
        /// (прошли её), либо упёрлись в постройки базы, либо истекло HomeApproachMaxMs.
        /// </summary>
        public bool GoHome()
        {
            var home = _c.State.HomeSectorId;
            Log.Info($"Иду к базе (сектор базы {home?.ToString() ?? "?"}).");
            FaceBaseAt(0);
            var sw = Stopwatch.StartNew();
            Stopwatch? inHomeSw = null;
            long lastCorrect = 0;
            int unsticks = 0;
            var sector = new StableReading<int>(3, 2);
            _c.Motion.Reset();
            _c.Input.KeyDown(Fwd);
            try
            {
                while (sw.Elapsed.TotalMinutes < 8)
                {
                    _c.Input.Sleep(_c.Cfg.PollIntervalMs);
                    var s = sector.Push(_c.Hud.ReadSectorOnce());
                    bool inHome = home == null || s == home;
                    if (inHome && inHomeSw == null) inHomeSw = Stopwatch.StartNew();

                    var b = Bearing();
                    if (inHome && (b == null || Math.Abs(b.Value) > 70))
                    {
                        Log.Good("Пришёл к базе (метка позади).");
                        return true;
                    }
                    if (inHomeSw != null && inHomeSw.ElapsedMilliseconds > _c.Cfg.HomeApproachMaxMs)
                    {
                        Log.Good("Пришёл к базе (по времени в секторе базы).");
                        return true;
                    }

                    if (b == null || sw.ElapsedMilliseconds - lastCorrect >= _c.Cfg.NavCorrectEveryMs)
                    {
                        FaceBaseAt(0);
                        lastCorrect = sw.ElapsedMilliseconds;
                    }

                    if (_c.Motion.CheckStuck())
                    {
                        if (inHome)
                        {
                            Log.Good("Пришёл к базе (упёрся в постройки).");
                            return true;
                        }
                        if (++unsticks > 10) return false;
                        Unstick();
                        FaceBaseAt(0);
                        _c.Input.KeyDown(Fwd);
                    }
                }
                Log.Warn("Не удалось дойти до базы за 8 минут.");
                return false;
            }
            finally
            {
                _c.Input.KeyUp(Fwd);
            }
        }

        /// <summary>
        /// Вернуться от базы в сектор <paramref name="targetSector"/>: идём от базы в направлении
        /// полярного угла <paramref name="polarDeg"/>, запомненного перед уходом на базу.
        /// </summary>
        public bool ReturnTo(int targetSector, double polarDeg, int ring)
        {
            Log.Info($"Возвращаюсь в сектор {targetSector}.");
            _c.Input.TurnToYaw(polarDeg);
            int timeout = 20000 + Math.Max(1, ring) * 15000;
            var sw = Stopwatch.StartNew();
            var sector = new StableReading<int>(3, 2);
            int unsticks = 0;
            _c.Motion.Reset();
            _c.Input.KeyDown(Fwd);
            try
            {
                while (sw.ElapsedMilliseconds < timeout)
                {
                    _c.Input.Sleep(_c.Cfg.PollIntervalMs);
                    if (sector.Push(_c.Hud.ReadSectorOnce()) == targetSector) return true;
                    if (_c.Motion.CheckStuck())
                    {
                        if (++unsticks > 8) return false;
                        Unstick();
                        _c.Input.TurnToYaw(polarDeg);
                        _c.Input.KeyDown(Fwd);
                    }
                }
                return false;
            }
            finally
            {
                _c.Input.KeyUp(Fwd);
            }
        }

        /// <summary>Выбраться из застревания: назад, прыжок, поворот, вперёд с прыжком.</summary>
        public void Unstick()
        {
            Log.Warn("Застрял — пробую обойти препятствие.");
            var k = _c.Cfg.Keys;
            _c.Input.KeyUp(k.Forward);
            _c.Input.Hold(k.Back, 600);
            double angle = (_rng.Next(2) == 0 ? -1 : 1) * _rng.Next(45, 100);
            _c.Input.Turn(angle);
            _c.Input.KeyDown(k.Forward);
            _c.Input.Tap(k.Jump, 80);
            _c.Input.Sleep(900);
            _c.Input.KeyUp(k.Forward);
            _c.Motion.Reset();
        }
    }
}
