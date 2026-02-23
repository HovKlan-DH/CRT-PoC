using System;
using System.IO;

namespace CRT
{
    public enum LogCategory
    {
        Info,
        Warning,
        Critical
    }

    public static class Logger
    {
        private static string _logFilePath = string.Empty;
        private static readonly object _lock = new();

        // ###########################################################################################
        // Resolves the log file path next to the executable and overwrites any previous log content.
        // Must be called once at application startup before any logging takes place.
        // ###########################################################################################
        public static void Initialize()
        {
            var exePath = Environment.ProcessPath ?? string.Empty;
            var directory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            var baseName = Path.GetFileNameWithoutExtension(exePath);

            _logFilePath = Path.Combine(directory, $"{baseName}.log");

            try
            {
                File.WriteAllText(_logFilePath, string.Empty);
            }
            catch
            {
                _logFilePath = string.Empty;
            }
        }

        // ###########################################################################################
        // Writes an Info-level log entry.
        // ###########################################################################################
        public static void Info(string message) => Write(LogCategory.Info, message);

        // ###########################################################################################
        // Writes a Warning-level log entry.
        // ###########################################################################################
        public static void Warning(string message) => Write(LogCategory.Warning, message);

        // ###########################################################################################
        // Writes a Critical-level log entry.
        // ###########################################################################################
        public static void Critical(string message) => Write(LogCategory.Critical, message);

        // ###########################################################################################
        // Formats and appends a single timestamped log line to the log file in a thread-safe manner.
        // ###########################################################################################
        private static void Write(LogCategory category, string message)
        {
            if (string.IsNullOrEmpty(_logFilePath))
                return;

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {category,-8} {message}";

            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
                }
                catch
                {
                    // Silently absorb write failures to avoid disrupting the application
                }
            }
        }
    }
}