namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents progress information for content storage operations.
/// </summary>
public readonly struct ContentStorageProgress
{
    /// <summary>
    /// Gets the number of files processed so far.
    /// </summary>
    public int ProcessedCount { get; init; }

    /// <summary>
    /// Gets the total number of files to process.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Gets the currently processed file name.
    /// </summary>
    public string? CurrentFileName { get; init; }

    /// <summary>
    /// Gets the current phase of the storage operation. Defaults to <see cref="ContentStoragePhase.Storing"/>.
    /// </summary>
    public ContentStoragePhase Phase { get; init; }

    /// <summary>
    /// Gets the percentage complete (0-100).
    /// </summary>
    public readonly double Percentage => TotalCount > 0 ? (double)ProcessedCount / TotalCount * 100 : 0;
}
