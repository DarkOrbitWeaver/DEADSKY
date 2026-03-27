using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace DEADSKY.Core.Logging;

/// <summary>
/// Centralized logging system for DEADSKY game.
/// Thread-safe, timestamped, with automatic log rotation.
/// </summary>
public static class GameLogger
{
    private static readonly ConcurrentQueue<LogEntry> _logQueue = new();
    private static readonly object _fileLock = new();
    private static string? _currentLogPath;
    private static bool _isInitialized;
    private static Thread? _writerThread;
    private static volatile bool _shouldStop;
    private static readonly Stopwatch _gameTimer = Stopwatch.StartNew();

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }

    private record LogEntry(DateTime Timestamp, double GameTime, LogLevel Level, string Category, string Message, string? StackTrace = null);

    public static void Initialize(string? customLogPath = null)
    {
        if (_isInitialized) return;

        var logsFolder = customLogPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DEADSKY",
            "Logs");

        Directory.CreateDirectory(logsFolder);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        _currentLogPath = Path.Combine(logsFolder, $"log_{timestamp}.txt");

        _shouldStop = false;
        _writerThread = new Thread(WriterThreadLoop)
        {
            IsBackground = true,
            Name = "GameLogger Writer"
        };
        _writerThread.Start();

        _isInitialized = true;

        Info("SYSTEM", $"GameLogger initialized. Log file: {_currentLogPath}");
        Info("SYSTEM", $"OS: {Environment.OSVersion}, .NET: {Environment.Version}");
    }

    public static void Shutdown()
    {
        if (!_isInitialized) return;

        Info("SYSTEM", "GameLogger shutting down...");
        _shouldStop = true;
        _writerThread?.Join(2000);
        FlushRemaining();
        _isInitialized = false;
    }

    private static void WriterThreadLoop()
    {
        while (!_shouldStop)
        {
            if (_logQueue.TryDequeue(out var entry))
            {
                WriteToFile(entry);
            }
            else
            {
                Thread.Sleep(50);
            }
        }
    }

    private static void WriteToFile(LogEntry entry)
    {
        if (_currentLogPath == null) return;

        try
        {
            lock (_fileLock)
            {
                var line = FormatLogEntry(entry);
                File.AppendAllText(_currentLogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Silently fail to avoid recursive logging
        }
    }

    private static void FlushRemaining()
    {
        while (_logQueue.TryDequeue(out var entry))
        {
            WriteToFile(entry);
        }
    }

    private static string FormatLogEntry(LogEntry entry)
    {
        var sb = new StringBuilder();
        sb.Append($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
        sb.Append($"[T+{entry.GameTime:F3}s] ");
        sb.Append($"[{entry.Level,-8}] ");
        sb.Append($"[{entry.Category,-12}] ");
        sb.Append(entry.Message);

        if (!string.IsNullOrEmpty(entry.StackTrace))
        {
            sb.AppendLine();
            sb.Append("  Stack: ");
            sb.Append(entry.StackTrace.Replace(Environment.NewLine, Environment.NewLine + "         "));
        }

        return sb.ToString();
    }

    private static void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        if (!_isInitialized)
        {
            Console.WriteLine($"[{level}] [{category}] {message}");
            return;
        }

        var stackTrace = exception?.StackTrace;
        if (exception != null)
        {
            message = $"{message} | Exception: {exception.GetType().Name}: {exception.Message}";
        }

        var entry = new LogEntry(
            DateTime.Now,
            _gameTimer.Elapsed.TotalSeconds,
            level,
            category,
            message,
            stackTrace
        );

        _logQueue.Enqueue(entry);

        // Also write critical errors to console immediately
        if (level >= LogLevel.Error)
        {
            Console.Error.WriteLine(FormatLogEntry(entry));
        }
    }

    public static void Debug(string category, string message) => Log(LogLevel.Debug, category, message);
    public static void Info(string category, string message) => Log(LogLevel.Info, category, message);
    public static void Warning(string category, string message) => Log(LogLevel.Warning, category, message);
    public static void Error(string category, string message, Exception? exception = null) => Log(LogLevel.Error, category, message, exception);
    public static void Critical(string category, string message, Exception? exception = null) => Log(LogLevel.Critical, category, message, exception);
}
