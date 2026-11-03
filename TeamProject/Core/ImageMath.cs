using System;

namespace TeamProject.Core
{
    /// <summary>
    /// Простой анализ пикселей в формате BGRA (4 байта на пиксель), без зависимостей от Windows.
    /// </summary>
    public static class ImageMath
    {
        /// <summary>
        /// Находит центр (по X) самого крупного вертикального «столбика» пикселей нужного цвета.
        /// Возвращает null, если подходящих пикселей меньше <paramref name="minPixels"/>.
        /// </summary>
        public static double? FindMarkerCenterX(byte[] bgra, int width, int height, int stride, ColorSpec color, int minPixels)
        {
            if (!color.IsSet) return null;
            var columns = new int[width];
            int total = 0;
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x * 4;
                    if (color.Matches(bgra[i + 2], bgra[i + 1], bgra[i]))
                    {
                        columns[x]++;
                        total++;
                    }
                }
            }
            if (total < Math.Max(1, minPixels)) return null;

            // Ищем самую «тяжёлую» связную группу столбцов (метку), чтобы одиночные шумовые пиксели
            // в другом месте шкалы не сдвигали центр.
            int bestStart = -1, bestEnd = -1, bestSum = 0;
            int start = -1, sum = 0, gap = 0;
            const int maxGap = 2;
            for (int x = 0; x <= width; x++)
            {
                int c = x < width ? columns[x] : 0;
                if (c > 0)
                {
                    if (start < 0) { start = x; sum = 0; }
                    sum += c;
                    gap = 0;
                }
                else if (start >= 0)
                {
                    gap++;
                    if (gap > maxGap || x == width)
                    {
                        if (sum > bestSum) { bestSum = sum; bestStart = start; bestEnd = x - gap; }
                        start = -1; gap = 0;
                    }
                }
            }
            if (bestSum < Math.Max(1, minPixels)) return null;

            double weighted = 0;
            for (int x = bestStart; x <= bestEnd; x++) weighted += x * (double)columns[x];
            return weighted / bestSum;
        }

        /// <summary>
        /// Заполненность шкалы. "Bar": доля ширины до самого правого столбца, где больше половины
        /// пикселей нужного цвета. "Pixels": доля всех пикселей нужного цвета.
        /// </summary>
        public static double FillFraction(byte[] bgra, int width, int height, int stride, ColorSpec color, bool barMode)
        {
            if (!color.IsSet || width <= 0 || height <= 0) return 0;
            int matchedTotal = 0, rightmost = -1;
            for (int x = 0; x < width; x++)
            {
                int colMatched = 0;
                for (int y = 0; y < height; y++)
                {
                    int i = y * stride + x * 4;
                    if (color.Matches(bgra[i + 2], bgra[i + 1], bgra[i])) colMatched++;
                }
                matchedTotal += colMatched;
                if (colMatched * 2 >= height) rightmost = x;
            }
            return barMode
                ? (rightmost + 1) / (double)width
                : matchedTotal / (double)(width * height);
        }

        /// <summary>Средняя абсолютная разница яркости двух кадров одинакового размера (0..255).</summary>
        public static double MeanAbsDiff(byte[] a, byte[] b, int width, int height, int stride, int step = 4)
        {
            if (a.Length != b.Length) return double.MaxValue;
            long sum = 0;
            int n = 0;
            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    int i = y * stride + x * 4;
                    int la = (a[i] + a[i + 1] * 2 + a[i + 2]) >> 2;
                    int lb = (b[i] + b[i + 1] * 2 + b[i + 2]) >> 2;
                    sum += Math.Abs(la - lb);
                    n++;
                }
            }
            return n == 0 ? 0 : sum / (double)n;
        }

        /// <summary>Угол на объект по смещению его метки от центра шкалы (вправо = +).</summary>
        public static double BearingFromCompassX(double markerScreenX, double centerScreenX, double degreesPerPixel)
            => (markerScreenX - centerScreenX) * degreesPerPixel;
    }
}
