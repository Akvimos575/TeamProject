using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using TeamProject.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace TeamProject.Vision
{
    /// <summary>
    /// Распознавание текста встроенным OCR Windows 10/11 (Windows.Media.Ocr).
    /// Перед распознаванием область увеличивается и (опционально) бинаризуется — мелкий шрифт HUD
    /// так распознаётся заметно надёжнее.
    /// </summary>
    internal sealed class OcrReader
    {
        private readonly OcrEngine _engine;
        private readonly BotConfig _cfg;

        public OcrReader(BotConfig cfg)
        {
            _cfg = cfg;
            OcrEngine? engine = null;
            if (!string.IsNullOrWhiteSpace(cfg.OcrLanguage))
            {
                var lang = new Language(cfg.OcrLanguage);
                if (OcrEngine.IsLanguageSupported(lang)) engine = OcrEngine.TryCreateFromLanguage(lang);
            }
            engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
            _engine = engine ?? throw new InvalidOperationException(
                "Не удалось создать OCR Windows. Установите языковой пакет (Параметры → Время и язык → Язык → English) " +
                "или укажите в config.json установленный язык в OcrLanguage.");
            LanguageTag = _engine.RecognizerLanguage.LanguageTag;
        }

        public string LanguageTag { get; }

        public string Read(ScreenRect region)
        {
            if (region.IsEmpty) return string.Empty;
            using var src = ScreenCapture.CaptureBitmap(region);
            return Read(src);
        }

        public string Read(Bitmap src)
        {
            int maxDim = (int)OcrEngine.MaxImageDimension;
            double scale = Math.Max(1, _cfg.OcrScale);
            scale = Math.Min(scale, maxDim / (double)Math.Max(src.Width, src.Height));
            int w = Math.Max(1, (int)(src.Width * scale));
            int h = Math.Max(1, (int)(src.Height * scale));

            using var scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, w, h);
            }

            var frame = ScreenCapture.ToFrame(scaled);
            Preprocess(frame);

            // Windows OCR плохо читает текст вплотную к краю — добавляем рамку из фона.
            const int pad = 12;
            int pw = w + pad * 2, ph = h + pad * 2;
            var padded = new byte[pw * ph * 4];
            byte bg = frame.Bgra.Length >= 4 ? frame.Bgra[0] : (byte)0;
            for (int i = 0; i < padded.Length; i += 4)
            {
                padded[i] = bg; padded[i + 1] = bg; padded[i + 2] = bg; padded[i + 3] = 255;
            }
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(frame.Bgra, y * frame.Stride, padded, ((y + pad) * pw + pad) * 4, w * 4);

            using var sb = SoftwareBitmap.CreateCopyFromBuffer(padded.AsBuffer(), BitmapPixelFormat.Bgra8, pw, ph, BitmapAlphaMode.Premultiplied);
            var result = _engine.RecognizeAsync(sb).AsTask().GetAwaiter().GetResult();
            return string.Join(" ", result.Lines.Select(l => l.Text));
        }

        /// <summary>Перевод в оттенки серого, инверсия и порог по настройкам.</summary>
        private void Preprocess(Frame f)
        {
            bool gray = _cfg.OcrThreshold > 0 || _cfg.OcrInvert;
            if (!gray) return;
            var b = f.Bgra;
            for (int y = 0; y < f.Height; y++)
            {
                int row = y * f.Stride;
                for (int x = 0; x < f.Width; x++)
                {
                    int i = row + x * 4;
                    int l = (b[i] * 29 + b[i + 1] * 150 + b[i + 2] * 77) >> 8;
                    if (_cfg.OcrInvert) l = 255 - l;
                    if (_cfg.OcrThreshold > 0) l = l >= _cfg.OcrThreshold ? 0 : 255; // тёмный текст на светлом фоне
                    b[i] = b[i + 1] = b[i + 2] = (byte)l;
                    b[i + 3] = 255;
                }
            }
        }
    }
}
