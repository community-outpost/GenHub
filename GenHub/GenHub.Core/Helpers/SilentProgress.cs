namespace GenHub.Core.Helpers;

/// <summary>
/// Explicit no-op progress sink for background acquisitions that intentionally show no user notification.
/// Prefer this over passing a null progress so silent headless operation is a deliberate choice
/// rather than an accidental gap. Use it only when the download is a sub-step of a larger
/// operation that already notifies the user of the overall outcome.
/// </summary>
/// <typeparam name="T">The progress payload type.</typeparam>
public sealed class SilentProgress<T> : IProgress<T>
{
    /// <summary>
    /// Gets the shared silent progress instance.
    /// </summary>
    public static SilentProgress<T> Instance { get; } = new();

    /// <inheritdoc />
    public void Report(T value)
    {
    }
}
