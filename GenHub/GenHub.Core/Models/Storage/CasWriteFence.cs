using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Models.Storage;

/// <summary>
/// Coordinates CAS content imports with forced garbage collection. Imports hold shared
/// write access from the first blob store until references are tracked and the manifest
/// is persisted, which is the window during which newly stored blobs are invisible to
/// the GC live set. Forced collection holds an exclusive collection lease across the
/// whole sweep; imports starting mid-sweep wait until the lease is released.
/// </summary>
/// <remarks>
/// Only forced collection takes the collection lease. Non-forced collection needs no
/// guard because the grace period protects recently stored blobs.
/// </remarks>
public sealed class CasWriteFence
{
    private sealed class WriteLease(CasWriteFence fence) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            fence.ReleaseWrite();
        }
    }

    private sealed class CollectionLease(CasWriteFence fence) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            fence._roomEmpty.Release();
        }
    }

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly SemaphoreSlim _roomEmpty = new(1, 1);
    private int _readers;

    /// <summary>
    /// Gets a value indicating whether any content import currently holds write access.
    /// This is a point-in-time observation only and must not be used as a synchronization check.
    /// </summary>
    public bool HasActiveWrites => Volatile.Read(ref _readers) > 0;

    /// <summary>
    /// Holds shared write access until the returned lease is disposed. Waits while an
    /// exclusive collection lease is held.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A lease that releases write access when disposed.</returns>
    public async Task<IDisposable> TrackWriteAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_readers == 0)
            {
                await _roomEmpty.WaitAsync(cancellationToken);
            }

            _readers++;
        }
        finally
        {
            _mutex.Release();
        }

        return new WriteLease(this);
    }

    /// <summary>
    /// Tries to acquire the exclusive collection lease.
    /// </summary>
    /// <param name="timeout">How long to wait for in-flight imports to drain.</param>
    /// <param name="lease">The lease to hold across the collection sweep, or null when refused.</param>
    /// <returns>True when the lease was acquired; otherwise false.</returns>
    public bool TryAcquireCollectionLease(TimeSpan timeout, out IDisposable? lease)
    {
        if (!_roomEmpty.Wait(timeout))
        {
            lease = null;
            return false;
        }

        lease = new CollectionLease(this);
        return true;
    }

    private void ReleaseWrite()
    {
        _mutex.Wait();
        try
        {
            _readers--;
            if (_readers == 0)
            {
                _roomEmpty.Release();
            }
        }
        finally
        {
            _mutex.Release();
        }
    }
}
