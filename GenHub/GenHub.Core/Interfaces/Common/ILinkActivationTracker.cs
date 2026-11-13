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
    /// Gets a token that is canceled when a link is received.
    /// </summary>
    CancellationToken LinkReceivedToken { get; }

    /// <summary>
    /// Records that a link was received.
    /// </summary>
    void RecordLink();

    /// <summary>
    /// Records that the operating system has finished launching GenHub and has delivered any link that started it.
    /// </summary>
    void MarkLaunchFinished();

    /// <summary>
    /// Waits until the operating system has finished launching GenHub.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>A task that completes once launching has finished.</returns>
    Task WaitForLaunchFinishedAsync(CancellationToken cancellationToken);
}
