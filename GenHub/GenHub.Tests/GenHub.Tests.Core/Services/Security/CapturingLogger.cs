using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace GenHub.Tests.Core.Services.Security;

/// <summary>
/// Logger that records every rendered message and exception text, so tests can assert
/// that nothing sensitive reaches the log output.
/// </summary>
/// <typeparam name="T">The category type.</typeparam>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> _entries = new();

    /// <summary>
    /// Gets the captured log entries, including any exception text.
    /// </summary>
    public IReadOnlyCollection<string> Entries => _entries;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _entries.Enqueue($"{logLevel}: {formatter(state, exception)} {exception}");
    }
}
