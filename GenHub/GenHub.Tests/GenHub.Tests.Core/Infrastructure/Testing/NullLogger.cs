using Microsoft.Extensions.Logging;

namespace GenHub.Tests.Infrastructure.Testing;

/// <summary>
/// A null logger implementation for testing.
/// </summary>
/// <typeparam name="T">The logger category type.</typeparam>
public class NullLogger<T> : ILogger<T>
    where T : notnull
{
    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => false;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Do nothing - this is a null logger
    }
}
