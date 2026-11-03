using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TeamProject.Core
{
    /// <summary>Прогресс бота, сохраняется в botstate.json между запусками.</summary>
    public sealed class BotState
    {
        /// <summary>Сектор, в котором стоит база (определяется при первом запуске).</summary>
        public int? HomeSectorId { get; set; }

        /// <summary>Номер текущего кольца вокруг базы (0 — сектор базы).</summary>
        public int Ring { get; set; }
        /// <summary>Сектора, пройденные в текущем кольце (по порядку).</summary>
        public List<int> RingVisited { get; set; } = new List<int>();
        /// <summary>Текущее кольцо замкнулось — следующий шаг наружу.</summary>
        public bool RingClosed { get; set; }

        /// <summary>Сектор, где бот был при последнем сохранении (чтобы продолжить кольцо после перезапуска).</summary>
        public int? LastSectorId { get; set; }

        public HashSet<int> CleanedSectors { get; set; } = new HashSet<int>();
        /// <summary>Сектора, которые не удалось дочистить до порога (можно вернуться позже).</summary>
        public HashSet<int> IncompleteSectors { get; set; } = new HashSet<int>();

        /// <summary>Сколько уровней каждого улучшения куплено (по имени).</summary>
        public Dictionary<string, int> PurchasedLevels { get; set; } = new Dictionary<string, int>();
        /// <summary>Ширина полосы для текущего инструмента (после покупки нового инструмента).</summary>
        public int? CurrentLaneWidthMs { get; set; }
        public string? CurrentToolKey { get; set; }

        public DateTime? LastUnloadUtc { get; set; }
        public long LastKnownMoney { get; set; }

        public static BotState Load(string path)
        {
            if (!File.Exists(path)) return new BotState();
            try
            {
                return JsonSerializer.Deserialize<BotState>(File.ReadAllText(path), BotConfig.JsonOptions) ?? new BotState();
            }
            catch (JsonException)
            {
                // Повреждённый файл не должен ронять бота: начинаем заново, старый файл сохраняем рядом.
                File.Copy(path, path + ".bad", overwrite: true);
                return new BotState();
            }
        }

        public void Save(string path)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, BotConfig.JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }

        public int PurchasedLevel(string name) => PurchasedLevels.TryGetValue(name, out var v) ? v : 0;
    }

    /// <summary>Записанный макрос (например, покупка улучшения в меню).</summary>
    public sealed class Macro
    {
        public string Name { get; set; } = "";
        public List<MacroStep> Steps { get; set; } = new List<MacroStep>();

        public static Macro Load(string path)
            => JsonSerializer.Deserialize<Macro>(File.ReadAllText(path), BotConfig.JsonOptions) ?? new Macro();

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, BotConfig.JsonOptions));
        }
    }

    public sealed class MacroStep
    {
        /// <summary>Пауза перед шагом.</summary>
        public int DelayMs { get; set; }
        /// <summary>KeyDown, KeyUp, LeftDown, LeftUp, RightDown, RightUp.</summary>
        public string Kind { get; set; } = "";
        public string? Key { get; set; }
        /// <summary>Абсолютные координаты курсора для действий мыши.</summary>
        public int X { get; set; }
        public int Y { get; set; }

        public override string ToString() => $"+{DelayMs}ms {Kind} {Key} ({X},{Y})";
    }
}
