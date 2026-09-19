namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Determines whether GenHub can create storage at a filesystem location.
/// </summary>
public interface IStorageWritabilityProbe
{
    /// <summary>
    /// Checks whether a directory can be created at, or files written into, the given path.
    /// </summary>
    /// <remarks>
    /// A successful check creates the storage directory when it does not already exist and leaves
    /// that directory in place. Callers should account for this filesystem side effect.
    /// </remarks>
    /// <param name="storagePath">The storage path to check.</param>
    /// <returns><c>true</c> when the location accepts writes; otherwise, <c>false</c>.</returns>
    bool CanCreateStorageAt(string storagePath);

    /// <summary>
    /// Discards any cached result for a storage path so the next check probes the filesystem again.
    /// </summary>
    /// <param name="storagePath">The storage path to re-probe, or <c>null</c> to discard every cached result.</param>
    void Invalidate(string? storagePath = null);
}
