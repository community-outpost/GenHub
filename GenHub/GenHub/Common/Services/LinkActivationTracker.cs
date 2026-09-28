using GenHub.Core.Interfaces.Common;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Common.Services;

/// <summary>
/// In-memory implementation of <see cref="ILinkActivationTracker"/>.
/// </summary>
/// <param name="launchLinkWait">How long <see cref="WaitForLaunchLinkAsync"/> waits for a link that arrives after startup.</param>
public sealed class LinkActivationTracker(TimeSpan launchLinkWait) : ILinkActivationTracker
{
    /// <summary>
    /// How long to wait on macOS, where a link that cold starts GenHub arrives as an Apple Event once the app finishes launching.
    /// </summary>
    public static readonly TimeSpan MacLaunchLinkWait = TimeSpan.FromSeconds(1.5);

    private readonly TaskCompletionSource _linkReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkActivationTracker"/> class with the wait for the current platform.
    /// </summary>
    public LinkActivationTracker()
        : this(OperatingSystem.IsMacOS() ? MacLaunchLinkWait : TimeSpan.Zero)
    {
    }

    /// <inheritdoc/>
    public bool HasReceivedLink => _linkReceived.Task.IsCompleted;

    /// <inheritdoc/>
    public void RecordLink() => _linkReceived.TrySetResult();

    /// <inheritdoc/>
    public async Task<bool> WaitForLaunchLinkAsync(CancellationToken cancellationToken)
    {
        if (HasReceivedLink || launchLinkWait <= TimeSpan.Zero)
        {
            return HasReceivedLink;
        }

        try
        {
            await _linkReceived.Task.WaitAsync(launchLinkWait, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // No link arrived during the launch window.
        }

        return HasReceivedLink;
    }
}
