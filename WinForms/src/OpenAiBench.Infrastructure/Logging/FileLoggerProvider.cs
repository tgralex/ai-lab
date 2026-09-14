using Microsoft.Extensions.Logging;
using OpenAiBench.Infrastructure.OpenAi;

namespace OpenAiBench.Infrastructure.Logging;

/// <summary>Minimal rolling-daily-file logger. Never receives API keys — callers must redact secrets before logging.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly LogLevel _minLevel;
    private readonly object _writeLock = new();

    public FileLoggerProvider(string logDirectory, LogLevel minLevel = LogLevel.Information)
    {
        _logDirectory = logDirectory;
        _minLevel = minLevel;
        Directory.CreateDirectory(_logDirectory);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string categoryName, LogLevel level, string message, Exception? exception)
    {
        if (level < _minLevel)
        {
            return;
        }

        var path = Path.Combine(_logDirectory, $"app-{DateTime.UtcNow:yyyy-MM-dd}.log");
        var line = $"{DateTimeOffset.UtcNow:O} [{level}] {categoryName}: {SecretRedactor.Redact(message)}";
        if (exception is not null)
        {
            line += Environment.NewLine + SecretRedactor.Redact(exception.ToString());
        }

        lock (_writeLock)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _categoryName;

        public FileLogger(FileLoggerProvider provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _provider._minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            _provider.Write(_categoryName, logLevel, formatter(state, exception), exception);
        }
    }
}
