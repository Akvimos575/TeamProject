using System;
using TeamProject.Core;

namespace TeamProject.Vision
{
    /// <summary>
    /// Шкала-компас вверху экрана: находит метку базы по цвету и переводит её смещение от центра
    /// шкалы в угол (градусы, база справа = +).
    /// </summary>
    internal sealed class CompassReader
    {
        private readonly Calibration _cal;

        public CompassReader(Calibration cal) => _cal = cal;

        public bool IsConfigured => !_cal.CompassRegion.IsEmpty && _cal.BaseMarkerColor.IsSet;

        /// <summary>Сколько градусов видно от центра шкалы до её края.</summary>
        public double HalfSpanDeg
        {
            get
            {
                var r = _cal.CompassRegion;
                int c = _cal.EffectiveCompassCenterX;
                double px = Math.Min(c - r.X, r.X + r.Width - 1 - c);
                return Math.Max(1, px * _cal.CompassDegreesPerPixel);
            }
        }

        /// <summary>Экранный X метки базы или null, если метка не видна.</summary>
        public double? MarkerScreenX()
        {
            if (!IsConfigured) return null;
            var f = ScreenCapture.Capture(_cal.CompassRegion);
            var cx = ImageMath.FindMarkerCenterX(f.Bgra, f.Width, f.Height, f.Stride, _cal.BaseMarkerColor, _cal.MinMarkerPixels);
            return cx == null ? (double?)null : _cal.CompassRegion.X + cx.Value;
        }

        /// <summary>Угол на базу относительно взгляда или null, если метки нет на шкале.</summary>
        public double? BaseBearing()
        {
            var x = MarkerScreenX();
            if (x == null) return null;
            return ImageMath.BearingFromCompassX(x.Value, _cal.EffectiveCompassCenterX, _cal.CompassDegreesPerPixel);
        }
    }

    /// <summary>Шкала заполнения контейнера инструмента.</summary>
    internal sealed class FillGauge
    {
        private readonly Calibration _cal;
        private readonly BotConfig _cfg;

        public FillGauge(BotConfig cfg, Calibration cal)
        {
            _cfg = cfg;
            _cal = cal;
        }

        public bool IsConfigured => !_cal.FillRegion.IsEmpty && _cal.FillColor.IsSet;

        /// <summary>Заполненность 0..1 или null, если шкала не настроена.</summary>
        public double? Fraction()
        {
            if (!IsConfigured) return null;
            var f = ScreenCapture.Capture(_cal.FillRegion);
            bool bar = !string.Equals(_cfg.FillMeasure, "Pixels", StringComparison.OrdinalIgnoreCase);
            return ImageMath.FillFraction(f.Bgra, f.Width, f.Height, f.Stride, _cal.FillColor, bar);
        }
    }

    /// <summary>
    /// Определение «застревания»: если при движении вперёд картинка в центре экрана почти не меняется
    /// дольше заданного времени, значит персонаж упёрся в препятствие.
    /// </summary>
    internal sealed class MotionDetector
    {
        private readonly BotConfig _cfg;
        private readonly ScreenRect _region;
        private Frame? _last;
        private DateTime _lastMotionUtc = DateTime.UtcNow;

        public MotionDetector(BotConfig cfg)
        {
            _cfg = cfg;
            var screen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            int w = screen.Width / 3, h = screen.Height / 3;
            // Нижняя-центральная часть экрана: там земля, которая «едет» при движении.
            _region = new ScreenRect { X = screen.X + (screen.Width - w) / 2, Y = screen.Y + screen.Height / 2, Width = w, Height = h };
        }

        public void Reset()
        {
            _last = null;
            _lastMotionUtc = DateTime.UtcNow;
        }

        /// <summary>Вызывать периодически во время движения. Возвращает true, если персонаж застрял.</summary>
        public bool CheckStuck()
        {
            var f = ScreenCapture.Capture(_region);
            if (_last != null)
            {
                var diff = ImageMath.MeanAbsDiff(_last.Bgra, f.Bgra, f.Width, f.Height, f.Stride, step: 6);
                if (diff >= _cfg.StuckDiffThreshold) _lastMotionUtc = DateTime.UtcNow;
            }
            else
            {
                _lastMotionUtc = DateTime.UtcNow;
            }
            _last = f;
            return (DateTime.UtcNow - _lastMotionUtc).TotalMilliseconds > _cfg.StuckCheckMs;
        }

        /// <summary>Текущая разница кадров (для подбора StuckDiffThreshold в режиме test-hud).</summary>
        public double MeasureDiff()
        {
            var f = ScreenCapture.Capture(_region);
            double d = _last == null ? 0 : ImageMath.MeanAbsDiff(_last.Bgra, f.Bgra, f.Width, f.Height, f.Stride, step: 6);
            _last = f;
            return d;
        }
    }
}
