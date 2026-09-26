namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Records whether this session has received a genhub:// link or share file to handle.
/// </summary>
public interface ILinkActivationTracker
{
    /// <summary>
    /// Gets a value indicating whether a link has been received during this session.
    /// </summary>
    bool HasReceivedLink { get; }

    /// <summary>
    /// Records that a link was received.
    /// </summary>
    void RecordLink();
}
