using GenHub.Core.Models.Tools.ReplayManager;

namespace GenHub.Core.Models.Events;

/// <summary>
/// Event arguments for replay completion.
/// </summary>
public sealed class ReplayCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the profile ID.
    /// </summary>
    public required string ProfileId { get; init; }

    /// <summary>
    /// Gets the saved replay file path.
    /// </summary>
    public required string SavedFilePath { get; init; }

    /// <summary>
    /// Gets the replay metadata.
    /// </summary>
    public required ReplayMetadata Metadata { get; init; }
}
