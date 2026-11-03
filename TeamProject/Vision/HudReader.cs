using System;
using TeamProject.Core;

namespace TeamProject.Vision
{
    /// <summary>Снимок HUD: номер сектора и процент очистки.</summary>
    internal readonly struct HudSnapshot
    {
        public HudSnapshot(int? sector, double? cleanedPercent, string rawText)
        {
            Sector = sector;
            CleanedPercent = cleanedPercent;
            RawText = rawText;
        }

        public int? Sector { get; }
        /// <summary>Процент очистки (уже приведённый, если HUD показывает остаток пыли).</summary>
        public double? CleanedPercent { get; }
        public string RawText { get; }

        public override string ToString()
            => $"сектор={(Sector?.ToString() ?? "?")} очищено={(CleanedPercent?.ToString("0.0") ?? "?")}%  [OCR: {RawText}]";
    }

    /// <summary>Чтение HUD игры: сектор, %, деньги, подсказка взаимодействия.</summary>
    internal sealed class HudReader
    {
        private readonly OcrReader _ocr;
        private readonly BotConfig _cfg;
        private readonly Calibration _cal;

        public HudReader(OcrReader ocr, BotConfig cfg, Calibration cal)
        {
            _ocr = ocr;
            _cfg = cfg;
            _cal = cal;
        }

        /// <summary>Одно чтение сектора и процента (без фильтрации).</summary>
        public HudSnapshot Read()
        {
            var sectorText = _ocr.Read(_cal.SectorRegion);
            var percentText = _cal.PercentRegion.IsEmpty ? sectorText : _ocr.Read(_cal.PercentRegion);
            var sector = HudParser.ParseSector(sectorText);
            var pct = HudParser.ParsePercent(percentText);
            double? cleaned = pct == null ? (double?)null : _cfg.ToCleanedPercent(pct.Value);
            var raw = _cal.PercentRegion.IsEmpty ? sectorText : sectorText + " | " + percentText;
            return new HudSnapshot(sector, cleaned, raw);
        }

        public int? ReadSectorOnce() => HudParser.ParseSector(_ocr.Read(_cal.SectorRegion));

        /// <summary>
        /// Надёжное чтение номера сектора: читает до <paramref name="maxReads"/> раз,
        /// пока два чтения не совпадут.
        /// </summary>
        public int? ReadSectorStable(int maxReads = 5)
        {
            var filter = new StableReading<int>(window: maxReads, requiredVotes: 2);
            for (int i = 0; i < maxReads; i++)
            {
                var v = filter.Push(ReadSectorOnce());
                if (v != null) return v;
            }
            return null;
        }

        /// <summary>Процент очистки: медиана из нескольких чтений.</summary>
        public double? ReadCleanedPercent(int reads = 3)
        {
            var region = _cal.EffectivePercentRegion;
            var values = new System.Collections.Generic.List<double>();
            for (int i = 0; i < reads; i++)
            {
                var p = HudParser.ParsePercent(_ocr.Read(region));
                if (p != null) values.Add(_cfg.ToCleanedPercent(p.Value));
            }
            if (values.Count == 0) return null;
            values.Sort();
            return values[values.Count / 2];
        }

        public long? ReadMoney()
        {
            if (_cal.MoneyRegion.IsEmpty) return null;
            var filter = new StableReading<long>(window: 4, requiredVotes: 2);
            for (int i = 0; i < 4; i++)
            {
                var v = filter.Push(HudParser.ParseMoney(_ocr.Read(_cal.MoneyRegion)));
                if (v != null) return v;
            }
            return null;
        }

        /// <summary>Видна ли подсказка взаимодействия с упаковщиком.</summary>
        public bool PackagerPromptVisible()
        {
            if (_cal.PromptRegion.IsEmpty) return false;
            return HudParser.ContainsPrompt(_ocr.Read(_cal.PromptRegion), _cfg.PackagerPromptText);
        }

        public string ReadPromptText() => _cal.PromptRegion.IsEmpty ? "" : _ocr.Read(_cal.PromptRegion);
        public string ReadMoneyText() => _cal.MoneyRegion.IsEmpty ? "" : _ocr.Read(_cal.MoneyRegion);
    }
}
