using System.Globalization;
using System.Text;

namespace Tandem.Core.Logging;

/// <summary>
/// Minimal file logger. Messages are redacted before they are written: no user names, no
/// claude:// links, no command lines. Callers log profile ids, never display names.
/// </summary>
public static class Log
{
    private const int KeepDays = 10;
    private static readonly object Gate = new();
    private static string? _directory;

    public static string? Directory => _directory;

    public static void Initialize(string logsDirectory)
    {
        lock (Gate)
        {
            _directory = logsDirectory;
            try
            {
                System.IO.Directory.CreateDirectory(logsDirectory);
                PruneOldFiles(logsDirectory);
            }
            catch (Exception)
            {
                // Logging must never break the app.
            }
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var dir = _directory;
        if (dir is null)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(level).Append("] ")
            .Append(Redactor.Redact(message));
        if (exception is not null)
        {
            line.AppendLine().Append("    ").Append(Redactor.Redact(exception.ToString()).Replace("\n", "\n    "));
        }

        line.AppendLine();
        var file = Path.Combine(dir, $"tandem-{DateTime.Now:yyyyMMdd}.log");
        lock (Gate)
        {
            try
            {
                File.AppendAllText(file, line.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
                // Ignore: a locked or read-only log file must not affect the app.
            }
        }
    }

    private static void PruneOldFiles(string dir)
    {
        var cutoff = DateTime.Now.AddDays(-KeepDays);
        foreach (var file in System.IO.Directory.EnumerateFiles(dir, "tandem-*.log"))
        {
            if (File.GetLastWriteTime(file) < cutoff)
            {
                File.Delete(file);
            }
        }
    }
}
