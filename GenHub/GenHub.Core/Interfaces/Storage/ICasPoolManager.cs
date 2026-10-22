using GenHub.Core.Models.Enums;

namespace GenHub.Core.Interfaces.Storage;

/// <summary>
/// Manages multiple CAS storage pools for content-type-based routing.
/// </summary>
public interface ICasPoolManager
{
    /// <summary>
    /// Gets the storage instance for the specified pool type.
    /// </summary>
    /// <param name="poolType">The pool type.</param>
    /// <returns>The CAS storage instance for the pool.</returns>
    ICasStorage GetStorage(CasPoolType poolType);

    /// <summary>
    /// Gets the storage instance for the specified content type.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <returns>The CAS storage instance for the appropriate pool.</returns>
    ICasStorage GetStorage(ContentType contentType);

    /// <summary>
    /// Gets all active storage pools.
    /// </summary>
    /// <returns>Read-only list of all storage instances.</returns>
    IReadOnlyList<ICasStorage> GetAllStorages();

    /// <summary>
    /// Gets the pool resolver used by this manager.
    /// </summary>
    ICasPoolResolver PoolResolver { get; }

    /// <summary>
    /// Ensures both primary and installation pools are initialized and ready to use.
    /// This method should be called before operations that might span both pools.
    /// </summary>
    void EnsureAllPoolsInitialized();

    /// <summary>
    /// Forces reinitialization of the Installation pool.
    /// Call this after the InstallationPoolRootPath has been updated in settings.
    /// </summary>
    void ReinitializeInstallationPool();
}
