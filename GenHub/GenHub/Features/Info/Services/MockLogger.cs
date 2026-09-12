using System;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Info.Services;

/// <summary>
/// Mock logger.
/// </summary>
/// <typeparam name="T">The type being logged.</typeparam>
public class MockLogger<T> : ILogger<T>
{
    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => false;

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
    }
}
