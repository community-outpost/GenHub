namespace GenHub.Core.Models.Events;

/// <summary>
/// Event arguments for replay file completion.
/// </summary>
public sealed class ReplayFileCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the completed file path.
    /// </summary>
    public required string FilePath { get; init; }
}
