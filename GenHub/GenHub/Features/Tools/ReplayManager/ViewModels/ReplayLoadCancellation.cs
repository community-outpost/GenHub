using System;
using System.Threading;

namespace GenHub.Features.Tools.ReplayManager.ViewModels;

/// <summary>
/// Owns the cancellation of the Replay Manager list load and its map availability scan.
/// Starting a load cancels the previous one, and disposal cancels the current load and any waiting reload.
/// </summary>
internal sealed class ReplayLoadCancellation : IDisposable
{
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly CancellationToken _disposeToken;
    private CancellationTokenSource? _current;
    private volatile bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayLoadCancellation"/> class.
    /// </summary>
    public ReplayLoadCancellation()
    {
        _disposeToken = _disposeCts.Token;
    }

    /// <summary>
    /// Gets a token that is canceled when the owner is disposed.
    /// </summary>
    public CancellationToken DisposeToken => _disposeToken;

    /// <summary>
    /// Gets a value indicating whether the owner has been disposed.
    /// </summary>
    public bool IsDisposed => _isDisposed;

    /// <summary>
    /// Starts a new load, canceling the previous one. The caller releases the returned source with <see cref="Release"/>.
    /// </summary>
    /// <returns>The token source for the new load, already canceled when the owner is disposed.</returns>
    public CancellationTokenSource Begin()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeToken);
        Cancel(Interlocked.Exchange(ref _current, cts));
        return cts;
    }

    /// <summary>
    /// Cancels the load in progress, if any.
    /// </summary>
    public void CancelCurrent() => Cancel(Volatile.Read(ref _current));

    /// <summary>
    /// Releases a token source returned by <see cref="Begin"/> once its load and scan have finished.
    /// </summary>
    /// <param name="cts">The token source to release.</param>
    public void Release(CancellationTokenSource cts)
    {
        Interlocked.CompareExchange(ref _current, null, cts);
        cts.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Cancel(Interlocked.Exchange(ref _current, null));
        Cancel(_disposeCts);
        _disposeCts.Dispose();
    }

    private static void Cancel(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The load already finished and released its token source.
        }
    }
}
