using System;

namespace GenHub.Core.Helpers;

/// <summary>
/// A synchronous implementation of <see cref="IProgress{T}"/> that invokes the callback directly on the reporting thread.
/// </summary>
/// <typeparam name="T">The type of progress update value.</typeparam>
/// <param name="handler">The action to execute synchronously when progress is reported.</param>
public sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly Action<T> _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <inheritdoc />
    public void Report(T value) => _handler(value);
}
