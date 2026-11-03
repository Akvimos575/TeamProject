using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TeamProject.Core
{
    /// <summary>
    /// Разбор текста, распознанного OCR в HUD игры: номер сектора, процент, деньги.
    /// OCR часто путает символы (O↔0, l/I/|↔1), поэтому внутри «числовых» слов они исправляются.
    /// </summary>
    public static class HudParser
    {
        private static readonly Regex NumericishToken = new Regex(@"[0-9OoIl|]*[0-9][0-9OoIl|]*", RegexOptions.Compiled);
        private static readonly Regex PercentRx = new Regex(@"(\d{1,3}(?:[.,]\d+)?)\s*%", RegexOptions.Compiled);
        private static readonly Regex LabeledSectorRx = new Regex(
            @"(?:#|№|N[oº°]\.?|sector|sektor|region|сектор|регион)\s*[:#№]?\s*(\d{1,3}(?:[,  ]\d{3})+|\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex IntegerRx = new Regex(@"\d{1,3}(?:[,  ]\d{3})+|\d+", RegexOptions.Compiled);
        private static readonly Regex MoneyRx = new Regex(@"-?\d[\d,.\s  ']*", RegexOptions.Compiled);

        /// <summary>Исправляет типичные ошибки OCR в словах, содержащих цифры.</summary>
        public static string FixDigits(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            // Типичные «двойники» цифр заменяем только в токенах, где уже есть хотя бы одна цифра,
            // чтобы не испортить слова вроде «Sector».
            return NumericishToken.Replace(text, m =>
            {
                var sb = new StringBuilder(m.Value.Length);
                foreach (char c in m.Value)
                {
                    switch (c)
                    {
                        case 'O': case 'o': sb.Append('0'); break;
                        case 'I': case 'l': case '|': sb.Append('1'); break;
                        default: sb.Append(c); break;
                    }
                }
                return sb.ToString();
            });
        }

        /// <summary>Процент (0..100) из текста вида «87%», «87.5 %», «99,2%».</summary>
        public static double? ParsePercent(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var fixedText = FixDigits(text);
            var m = PercentRx.Match(fixedText);
            if (!m.Success) return null;
            var s = m.Groups[1].Value.Replace(',', '.');
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
            if (v < 0 || v > 100) return null;
            return v;
        }

        /// <summary>
        /// Номер сектора. Сначала ищется число после подписи («Sector 123», «#123», «Сектор 123»),
        /// иначе берётся первое целое число, не являющееся процентом.
        /// </summary>
        public static int? ParseSector(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var fixedText = FixDigits(text);
            var withoutPercent = PercentRx.Replace(fixedText, " ");

            var labeled = LabeledSectorRx.Match(withoutPercent);
            if (labeled.Success && TryParseInt(labeled.Groups[1].Value, out var id1)) return id1;

            var m = IntegerRx.Match(withoutPercent);
            while (m.Success)
            {
                if (TryParseInt(m.Value, out var id)) return id;
                m = m.NextMatch();
            }
            return null;
        }

        /// <summary>Деньги: «$12,345», «12 345 $», «€1.200». Возвращает целую часть.</summary>
        public static long? ParseMoney(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var fixedText = FixDigits(text);
            long? best = null;
            foreach (Match m in MoneyRx.Matches(fixedText))
            {
                var raw = m.Value.Trim();
                // Отбрасываем дробную часть вида ".50" или ",50" в конце.
                var frac = Regex.Match(raw, @"[.,](\d{1,2})$");
                if (frac.Success) raw = raw.Substring(0, frac.Index);
                var digits = new StringBuilder();
                foreach (char c in raw) if (char.IsDigit(c)) digits.Append(c);
                if (digits.Length == 0 || digits.Length > 15) continue;
                var v = long.Parse(digits.ToString(), CultureInfo.InvariantCulture);
                if (raw.StartsWith("-", StringComparison.Ordinal)) v = -v;
                if (best == null || Math.Abs(v) > Math.Abs(best.Value)) best = v;
            }
            return best;
        }

        /// <summary>Содержит ли текст подсказку (без учёта регистра и пробелов).</summary>
        public static bool ContainsPrompt(string? text, string? prompt)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(prompt)) return false;
            static string Squash(string s)
            {
                var sb = new StringBuilder(s.Length);
                foreach (char c in s) if (!char.IsWhiteSpace(c)) sb.Append(char.ToLowerInvariant(c));
                return sb.ToString();
            }
            return Squash(text).Contains(Squash(prompt), StringComparison.Ordinal);
        }

        private static bool TryParseInt(string s, out int value)
        {
            var digits = new StringBuilder();
            foreach (char c in s) if (char.IsDigit(c)) digits.Append(c);
            return int.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }
    }
}
