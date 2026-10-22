using System.IO;

namespace Textream.Windows.Common;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public class Logger
{
    private static readonly object LockObj = new();
    private static string? _logFilePath;
    private readonly string _category;

    public Logger(string category)
    {
        _category = category;
        EnsureLogFileInitialized();
    }

    private static void EnsureLogFileInitialized()
    {
        if (_logFilePath != null) return;
        lock (LockObj)
        {
            if (_logFilePath != null) return;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDir = Path.Combine(appData, "Textream", "logs");
            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, "textream.log");
        }
    }

    public void Log(LogLevel level, string message, Exception? ex = null)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLine = $"[{timestamp}] [{level.ToString().ToUpperInvariant()}] [{_category}] {message}";
        if (ex != null)
        {
            logLine += Environment.NewLine + ex;
        }

        System.Diagnostics.Debug.WriteLine(logLine);
        Console.WriteLine(logLine);

        if (_logFilePath == null) return;
        lock (LockObj)
        {
            try
            {
                File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
            }
            catch
            {
                // Fallback gracefully without throwing
            }
        }
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message, Exception? ex = null) => Log(LogLevel.Warning, message, ex);
    public void Error(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
}

public static class AppLogger
{
    private static readonly Logger Instance = new("App");
    public static void Debug(string message) => Instance.Debug(message);
    public static void Info(string message) => Instance.Info(message);
    public static void Warn(string message, Exception? ex = null) => Instance.Warning(message, ex);
    public static void Warning(string message, Exception? ex = null) => Instance.Warning(message, ex);
    public static void Error(string message, Exception? ex = null) => Instance.Error(message, ex);
}
