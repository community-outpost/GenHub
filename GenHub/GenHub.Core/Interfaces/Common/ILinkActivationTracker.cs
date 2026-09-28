using System.Threading;
using System.Threading.Tasks;

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

    /// <summary>
    /// Waits for a link that the operating system delivers after startup, such as the Apple Event of a macOS cold start from a link.
    /// Completes as soon as a link is received or once the platform's launch link window has passed.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns><see langword="true"/> when a link has been received.</returns>
    Task<bool> WaitForLaunchLinkAsync(CancellationToken cancellationToken);
}
