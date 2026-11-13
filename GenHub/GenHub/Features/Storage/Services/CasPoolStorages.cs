using GenHub.Core.Interfaces.Storage;
using System.Collections.Generic;

namespace GenHub.Features.Storage.Services;

/// <summary>
/// Shared CAS pool enumeration so statistics, validation, audit, and garbage collection
/// span the same set of storages. Reads and writes route through every pool, so all of
/// these must too; enumerating only the default storage underreports the real footprint.
/// </summary>
internal static class CasPoolStorages
{
    /// <summary>
    /// Gets every storage that must be enumerated: all pool storages when a pool manager
    /// is available, otherwise the default storage alone.
    /// </summary>
    /// <param name="poolManager">The pool manager, or null when pooling is not configured.</param>
    /// <param name="defaultStorage">The default storage used without a pool manager.</param>
    /// <returns>The storages to enumerate.</returns>
    public static IReadOnlyList<ICasStorage> GetStoragesForEnumeration(ICasPoolManager? poolManager, ICasStorage defaultStorage)
    {
        if (poolManager != null)
        {
            poolManager.EnsureAllPoolsInitialized();
            return poolManager.GetAllStorages();
        }

        return [defaultStorage];
    }
}
