using GenHub.Core.Interfaces.Common;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Common.Services;

/// <summary>
/// In-memory implementation of <see cref="ILinkActivationTracker"/>.
/// </summary>
/// <remarks>
/// On macOS a link that cold starts GenHub arrives as an Apple Event while AppKit finishes launching,
/// so the macOS host marks the launch finished from AppKit's launch callback. Other platforms pass
/// links as startup arguments, so their launch counts as finished immediately.
/// </remarks>
public sealed class LinkActivationTracker : ILinkActivationTracker, IDisposable
{
    private readonly CancellationTokenSource _linkReceived = new();
    private readonly TaskCompletionSource _launchFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkActivationTracker"/> class.
    /// </summary>
    public LinkActivationTracker()
        : this(launchFinished: !OperatingSystem.IsMacOS())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkActivationTracker"/> class.
    /// </summary>
    /// <param name="launchFinished">Whether the launch already counts as finished.</param>
    internal LinkActivationTracker(bool launchFinished)
    {
        if (launchFinished)
        {
            MarkLaunchFinished();
        }
    }

    /// <inheritdoc/>
    public bool HasReceivedLink => _linkReceived.IsCancellationRequested;

    /// <inheritdoc/>
    public CancellationToken LinkReceivedToken => _linkReceived.Token;

    /// <inheritdoc/>
    public void RecordLink()
    {
        try
        {
            _linkReceived.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The session is shutting down.
        }
    }

    /// <inheritdoc/>
    public void MarkLaunchFinished() => _launchFinished.TrySetResult();

    /// <inheritdoc/>
    public Task WaitForLaunchFinishedAsync(CancellationToken cancellationToken) =>
        _launchFinished.Task.WaitAsync(cancellationToken);

    /// <inheritdoc/>
    public void Dispose() => _linkReceived.Dispose();
}
