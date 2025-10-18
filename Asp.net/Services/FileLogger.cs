using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;

namespace Asp.net.Services;

/// <summary>
/// Custom file logger để ghi logs ra file cho server
/// </summary>
public class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string _logFilePath;
    private static readonly object _lock = new object();

    public FileLogger(string categoryName, string logFilePath)
    {
        _categoryName = categoryName;
        _logFilePath = logFilePath;

        // Tạo directory nếu chưa có
        var directory = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var logEntry = new StringBuilder();
        logEntry.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ");
        logEntry.Append($"[{logLevel}] ");
        logEntry.Append($"[{_categoryName}] ");
        logEntry.AppendLine(formatter(state, exception));

        if (exception != null)
        {
            logEntry.AppendLine($"Exception: {exception}");
        }

        // Thread-safe file writing
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, logEntry.ToString());
            }
            catch
            {
                // Ignore errors writing to log file
            }
        }
    }
}

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;

    public FileLoggerProvider(string logFilePath)
    {
        _logFilePath = logFilePath;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, _logFilePath);
    }

    public void Dispose()
    {
    }
}
