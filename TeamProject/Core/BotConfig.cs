using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamProject.Core
{
    /// <summary>Прямоугольная область экрана (в физических пикселях).</summary>
    public sealed class ScreenRect
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        [JsonIgnore] public bool IsEmpty => Width <= 0 || Height <= 0;
        [JsonIgnore] public int CenterX => X + Width / 2;
        [JsonIgnore] public int CenterY => Y + Height / 2;

        public static ScreenRect FromCorners(int x1, int y1, int x2, int y2) => new ScreenRect
        {
            X = Math.Min(x1, x2),
            Y = Math.Min(y1, y2),
            Width = Math.Abs(x2 - x1) + 1,
            Height = Math.Abs(y2 - y1) + 1,
        };

        public override string ToString() => IsEmpty ? "(не задано)" : $"x={X} y={Y} {Width}x{Height}";
    }

    /// <summary>Цвет с допуском (для поиска метки базы и шкалы заполнения).</summary>
    public sealed class ColorSpec
    {
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }
        /// <summary>Максимальное отклонение по каждому каналу.</summary>
        public int Tolerance { get; set; } = 40;

        [JsonIgnore] public bool IsSet => R != 0 || G != 0 || B != 0;

        public bool Matches(int r, int g, int b)
            => Math.Abs(r - R) <= Tolerance && Math.Abs(g - G) <= Tolerance && Math.Abs(b - B) <= Tolerance;

        public override string ToString() => IsSet ? $"RGB({R},{G},{B}) ±{Tolerance}" : "(не задан)";
    }

    /// <summary>Улучшение/предмет в списке покупок (по порядку приоритета).</summary>
    public sealed class UpgradeItem
    {
        public string Name { get; set; } = "";
        /// <summary>Цена одного уровня. null — неизвестна, пункт пропускается.</summary>
        public long? Cost { get; set; }
        /// <summary>Сколько уровней покупать.</summary>
        public int Levels { get; set; } = 1;
        /// <summary>Файл записанного макроса покупки (относительно папки программы).</summary>
        public string Macro { get; set; } = "";
        public bool Enabled { get; set; } = true;
        /// <summary>Новая ширина полосы чистки после покупки (если это новый инструмент).</summary>
        public int? LaneWidthMs { get; set; }
        /// <summary>Клавиша выбора инструмента после покупки (например «2»).</summary>
        public string? SelectToolKey { get; set; }
        public string? Note { get; set; }
    }

    public sealed class KeyBindings
    {
        public string Forward { get; set; } = "W";
        public string Back { get; set; } = "S";
        public string Left { get; set; } = "A";
        public string Right { get; set; } = "D";
        public string Jump { get; set; } = "Space";
        public string Pack { get; set; } = "Q";
        public string Interact { get; set; } = "E";
        public string SectorBorders { get; set; } = "G";
        /// <summary>
        /// Как работает клавиша границ секторов: "Toggle" — включается один раз при старте;
        /// "Scan" — нажимается каждый раз, когда номер сектора не читается (если HUD показывает его временно).
        /// "None" — не нажимать.
        /// </summary>
        public string SectorBordersMode { get; set; } = "Toggle";
        /// <summary>Фиксация поворота щётки (пусто — не нажимать).</summary>
        public string LockBrush { get; set; } = "";
        public string PauseHotkey { get; set; } = "F8";
        public string StopHotkey { get; set; } = "F9";
        public string SaveHotkey { get; set; } = "F10";
    }

    /// <summary>Настройки бота (config.json). Калибровка экрана хранится отдельно в calibration.json.</summary>
    public sealed class BotConfig
    {
        // --- общее ---
        /// <summary>Имя процесса игры без .exe. Пусто — не проверять, что игра в фокусе.</summary>
        public string GameProcessName { get; set; } = "";
        public int StartDelaySeconds { get; set; } = 5;
        public bool PauseWhenGameNotFocused { get; set; } = true;

        public KeyBindings Keys { get; set; } = new KeyBindings();

        // --- OCR ---
        /// <summary>Язык OCR Windows (должен быть установлен в системе). Пусто — язык профиля.</summary>
        public string OcrLanguage { get; set; } = "en-US";
        public int OcrScale { get; set; } = 3;
        /// <summary>Порог бинаризации 0..255 (0 — без бинаризации).</summary>
        public int OcrThreshold { get; set; } = 0;
        public bool OcrInvert { get; set; } = false;

        // --- HUD ---
        /// <summary>"Cleaned" — HUD показывает % очистки; "DustRemaining" — % оставшейся пыли.</summary>
        public string PercentMeaning { get; set; } = "Cleaned";
        /// <summary>Сектор считается очищенным при этом проценте очистки.</summary>
        public double CleanThresholdPercent { get; set; } = 99.0;
        /// <summary>Если после всех проходов достигнут этот процент — уходим дальше.</summary>
        public double SoftThresholdPercent { get; set; } = 97.0;

        // --- стратегия ---
        /// <summary>"Left" или "Right": куда двигаться по кругу вокруг базы.</summary>
        public string CircleDirection { get; set; } = "Left";
        /// <summary>Небольшой наклон курса к базе при обходе кольца (компенсирует снос наружу).</summary>
        public double TangentInwardBiasDeg { get; set; } = 6;
        /// <summary>Максимум колец вокруг базы (0 — без ограничения).</summary>
        public int MaxRings { get; set; } = 0;

        // --- чистка ---
        public int LaneWidthMs { get; set; } = 350;
        public int LaneMaxMs { get; set; } = 20000;
        public int StrafeToEdgeMaxMs { get; set; } = 10000;
        public int BackoffMaxMs { get; set; } = 3000;
        public int MaxPasses { get; set; } = 3;
        public int SectorTimeoutMinutes { get; set; } = 20;
        /// <summary>Держать ЛКМ во время чистки.</summary>
        public bool HoldUseButton { get; set; } = true;

        // --- навигация ---
        public int PollIntervalMs { get; set; } = 120;
        public int NavCorrectEveryMs { get; set; } = 1000;
        public int WalkToNextSectorTimeoutMs { get; set; } = 40000;
        public int HomeApproachMaxMs { get; set; } = 25000;
        public int TurnChunkCounts { get; set; } = 80;
        public int TurnChunkDelayMs { get; set; } = 3;
        public double FaceToleranceDeg { get; set; } = 3;
        public int StuckCheckMs { get; set; } = 1500;
        public double StuckDiffThreshold { get; set; } = 1.5;

        // --- разгрузка ---
        /// <summary>"FillGauge" — по шкале заполнения, "Timer" — по таймеру, "Never" — не разгружаться.</summary>
        public string UnloadMode { get; set; } = "Timer";
        public double FillUnloadFraction { get; set; } = 0.95;
        /// <summary>"Pixels" — доля пикселей цвета шкалы; "Bar" — самый правый закрашенный столбец.</summary>
        public string FillMeasure { get; set; } = "Bar";
        public double UnloadEveryMinutes { get; set; } = 5;
        /// <summary>Нажимать Q (упаковать пыль) перед разгрузкой.</summary>
        public bool PackBeforeUnload { get; set; } = false;
        public int InteractHoldMs { get; set; } = 300;
        public int InteractRepeat { get; set; } = 2;
        /// <summary>Часть текста подсказки у упаковщика (ищется OCR в PromptRegion).</summary>
        public string PackagerPromptText { get; set; } = "packager";
        public int PackagerSearchRings { get; set; } = 3;
        public int PackagerSearchStepMs { get; set; } = 500;

        // --- покупки ---
        public bool ShopEnabled { get; set; } = true;
        /// <summary>Покупать строго по порядку (не перепрыгивать дорогой пункт ради дешёвого).</summary>
        public bool ShopStrictOrder { get; set; } = true;
        public List<UpgradeItem> Upgrades { get; set; } = new List<UpgradeItem>();

        [JsonIgnore] public bool PercentIsDustRemaining => string.Equals(PercentMeaning, "DustRemaining", StringComparison.OrdinalIgnoreCase);
        [JsonIgnore] public bool CircleLeft => !string.Equals(CircleDirection, "Right", StringComparison.OrdinalIgnoreCase);

        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static BotConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                var cfg = new BotConfig();
                File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOptions));
                return cfg;
            }
            return JsonSerializer.Deserialize<BotConfig>(File.ReadAllText(path), JsonOptions) ?? new BotConfig();
        }

        /// <summary>Привести процент из HUD к «проценту очистки».</summary>
        public double ToCleanedPercent(double hudPercent) => PercentIsDustRemaining ? 100.0 - hudPercent : hudPercent;
    }

    /// <summary>Калибровка под экран пользователя (calibration.json, пишется командой calibrate).</summary>
    public sealed class Calibration
    {
        public ScreenRect SectorRegion { get; set; } = new ScreenRect();
        /// <summary>Если пусто — процент ищется в SectorRegion.</summary>
        public ScreenRect PercentRegion { get; set; } = new ScreenRect();
        public ScreenRect MoneyRegion { get; set; } = new ScreenRect();
        public ScreenRect CompassRegion { get; set; } = new ScreenRect();
        public ScreenRect FillRegion { get; set; } = new ScreenRect();
        public ScreenRect PromptRegion { get; set; } = new ScreenRect();

        public ColorSpec BaseMarkerColor { get; set; } = new ColorSpec();
        public ColorSpec FillColor { get; set; } = new ColorSpec();
        public int MinMarkerPixels { get; set; } = 4;

        /// <summary>Сколько градусов поворота соответствует 1 пикселю шкалы-компаса.</summary>
        public double CompassDegreesPerPixel { get; set; } = 0.5;
        /// <summary>Абсолютный X пикселя шкалы, соответствующий направлению взгляда. 0 — центр области.</summary>
        public int CompassCenterX { get; set; } = 0;
        /// <summary>Сколько «отсчётов» мыши (SendInput) нужно на 1 градус поворота.</summary>
        public double MouseCountsPerDegree { get; set; } = 10;

        public static Calibration Load(string path)
        {
            if (!File.Exists(path)) return new Calibration();
            return JsonSerializer.Deserialize<Calibration>(File.ReadAllText(path), BotConfig.JsonOptions) ?? new Calibration();
        }

        public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, BotConfig.JsonOptions));

        [JsonIgnore] public ScreenRect EffectivePercentRegion => PercentRegion.IsEmpty ? SectorRegion : PercentRegion;
        [JsonIgnore] public int EffectiveCompassCenterX => CompassCenterX > 0 ? CompassCenterX : CompassRegion.CenterX;
    }
}
