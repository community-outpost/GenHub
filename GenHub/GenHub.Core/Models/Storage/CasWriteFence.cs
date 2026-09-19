using System;
using System.Threading;

namespace GenHub.Core.Models.Storage;

/// <summary>
/// Tracks in-flight CAS content imports so forced garbage collection can refuse to run
/// while blobs are being written. Imports hold the fence from the first blob store until
/// references are tracked and the manifest is persisted, which is the window during which
/// newly stored blobs are invisible to the GC live set.
/// </summary>
/// <remarks>
/// This is a one-way guard: forced collection refuses to start while an import holds the
/// fence, but imports starting after collection began are not blocked. Forced collection
/// runs only from user-confirmed destructive actions, so the residual window requires the
/// user to start an import mid-scan. Non-forced collection needs no guard because the
/// grace period protects recently stored blobs.
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
            Interlocked.Decrement(ref fence._activeWrites);
        }
    }

    private int _activeWrites;

    /// <summary>
    /// Gets a value indicating whether any content import currently holds the fence.
    /// </summary>
    public bool HasActiveWrites => Volatile.Read(ref _activeWrites) > 0;

    /// <summary>
    /// Holds the fence until the returned lease is disposed.
    /// </summary>
    /// <returns>A lease that releases the fence when disposed.</returns>
    public IDisposable TrackWrite()
    {
        Interlocked.Increment(ref _activeWrites);
        return new WriteLease(this);
    }
}
