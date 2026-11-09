using Microsoft.Extensions.Logging;

namespace ClimbOn.Client.Tests;

// Every log entry the client writes in a test: where it came from, how severe, and the exception it carried.
public sealed class LogRecorder
{
    private readonly List<LogEntry> entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (entries)
            {
                return [.. entries];
            }
        }
    }

    internal void Add(LogEntry entry)
    {
        lock (entries)
        {
            entries.Add(entry);
        }
    }
}

public sealed record LogEntry(string Category, LogLevel Level, Exception? Exception);

public sealed class RecordingLogger<T>(LogRecorder recorder) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        recorder.Add(new LogEntry(typeof(T).Name, logLevel, exception));
}
