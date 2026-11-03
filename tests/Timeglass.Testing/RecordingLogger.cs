using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Timeglass.Testing;

/// <summary>A logger that keeps every entry at every level, for asserting on what gets logged.</summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}

/// <summary>One recorded log entry, with the message formatted.</summary>
public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
