using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ReplayManager.ViewModels;

/// <summary>
/// Serializes Replay Manager list loads and owns their cancellation.
/// Starting a load cancels the previous one, and disposal cancels the current load and any reload waiting for its turn.
/// </summary>
internal sealed class ReplayLoadCancellation : IDisposable
{
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly CancellationToken _disposeToken;

    // Never disposed: SemaphoreSlim.Dispose drops queued waiters without completing them.
    private readonly SemaphoreSlim _turnLock = new(1, 1);
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
    /// Gets a value indicating whether the owner has been disposed.
    /// </summary>
    public bool IsDisposed => _isDisposed;

    /// <summary>
    /// Waits until no other load is running. Every successful wait must be paired with <see cref="EndTurn"/>.
    /// </summary>
    /// <returns><c>true</c> when the caller may run a load; <c>false</c> when the owner was disposed while waiting.</returns>
    public async Task<bool> WaitForTurnAsync()
    {
        try
        {
            await _turnLock.WaitAsync(_disposeToken);
            return true;
        }
        catch (OperationCanceledException) when (_disposeToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// Ends a turn started by a successful <see cref="WaitForTurnAsync"/>.
    /// </summary>
    public void EndTurn() => _turnLock.Release();

    /// <summary>
    /// Waits until no load holds a turn. After disposal this means every load has stopped.
    /// </summary>
    /// <returns>A task that completes when the running load, if any, has finished.</returns>
    public async Task WhenIdleAsync()
    {
        await _turnLock.WaitAsync(CancellationToken.None);
        _turnLock.Release();
    }

    /// <summary>
    /// Starts a new load, canceling the previous one. The caller releases the returned source with <see cref="Release"/>.
    /// </summary>
    /// <returns>The token source for the new load, already canceled when the owner is disposed.</returns>
    public CancellationTokenSource Begin()
    {
        var cts = CreateDisposeLinkedSource();
        Cancel(Interlocked.Exchange(ref _current, cts));
        return cts;
    }

    /// <summary>
    /// Creates a token source that is canceled when the owner is disposed, for background work outside a load.
    /// </summary>
    /// <returns>A token source the caller disposes.</returns>
    public CancellationTokenSource CreateDisposeLinkedSource() => CancellationTokenSource.CreateLinkedTokenSource(_disposeToken);

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
