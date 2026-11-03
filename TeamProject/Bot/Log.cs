using System;
using System.IO;

namespace TeamProject.Bot
{
    /// <summary>Лог в консоль и в файл bot.log рядом с программой.</summary>
    internal static class Log
    {
        private static readonly object Sync = new object();
        private static string? _path;

        public static void Init(string path) => _path = path;

        public static void Info(string message) => Write("INFO", message, ConsoleColor.Gray);
        public static void Good(string message) => Write("OK  ", message, ConsoleColor.Green);
        public static void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow);
        public static void Error(string message) => Write("ERR ", message, ConsoleColor.Red);

        private static void Write(string level, string message, ConsoleColor color)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} {level} {message}";
            lock (Sync)
            {
                var old = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(line);
                Console.ForegroundColor = old;
                if (_path != null)
                {
                    try { File.AppendAllText(_path, line + Environment.NewLine); }
                    catch (IOException) { /* лог не должен ронять бота */ }
                }
            }
        }
    }
}
