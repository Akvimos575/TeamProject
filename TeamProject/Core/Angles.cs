using System;

namespace TeamProject.Core
{
    /// <summary>Арифметика углов в градусах.</summary>
    public static class Angles
    {
        /// <summary>Приводит угол к диапазону (-180; 180].</summary>
        public static double Normalize180(double degrees)
        {
            double a = degrees % 360.0;
            if (a > 180.0) a -= 360.0;
            if (a <= -180.0) a += 360.0;
            return a;
        }

        /// <summary>Приводит угол к диапазону [0; 360).</summary>
        public static double Normalize360(double degrees)
        {
            double a = degrees % 360.0;
            if (a < 0) a += 360.0;
            return a;
        }

        /// <summary>Кратчайший поворот от угла <paramref name="from"/> к углу <paramref name="to"/>.</summary>
        public static double Delta(double from, double to) => Normalize180(to - from);

        /// <summary>
        /// Полярный угол игрока вокруг базы (в мировых градусах, по отсчёту yaw бота).
        /// yaw — текущий поворот камеры, bearingToBase — угол на базу относительно взгляда
        /// (положительный = база справа). Направление «от базы к игроку» = yaw + bearing + 180.
        /// </summary>
        public static double PolarAroundBase(double yaw, double bearingToBase)
            => Normalize360(yaw + bearingToBase + 180.0);

        /// <summary>
        /// На сколько градусов (вправо = +) надо повернуться, чтобы база оказалась под углом
        /// <paramref name="desiredBaseBearing"/> относительно взгляда.
        /// desiredBaseBearing: 0 — смотреть на базу, 180 — спиной к базе, +90 — база справа.
        /// </summary>
        public static double TurnToPutBaseAt(double currentBaseBearing, double desiredBaseBearing)
            => Normalize180(currentBaseBearing - desiredBaseBearing);

        public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        public static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));
    }
}
