using GenHub.Core.Models.Enums;

namespace GenHub.Core.Interfaces.Storage;

/// <summary>
/// Resolves which CAS pool to use based on content type.
/// </summary>
public interface ICasPoolResolver
{
    /// <summary>
    /// Resolves the appropriate CAS pool for a content type.
    /// </summary>
    /// <param name="contentType">The content type to resolve.</param>
    /// <returns>The pool type to use for this content.</returns>
    CasPoolType ResolvePool(ContentType contentType);

    /// <summary>
    /// Gets the root path for the specified pool type.
    /// </summary>
    /// <param name="poolType">The pool type.</param>
    /// <returns>The root path for the pool.</returns>
    string GetPoolRootPath(CasPoolType poolType);

    /// <summary>
    /// Gets the root path for the pool that handles the specified content type.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <returns>The root path for the appropriate pool.</returns>
    string GetPoolRootPath(ContentType contentType);

    /// <summary>
    /// Gets the previous installation-pool roots retained for read-only lookup.
    /// </summary>
    /// <returns>The legacy roots, or an empty list when none are configured.</returns>
    IReadOnlyList<string> GetLegacyInstallationPoolRootPaths();

    /// <summary>
    /// Checks if the installation pool is configured and available.
    /// </summary>
    /// <returns>True if the installation pool is available, false otherwise.</returns>
    bool IsInstallationPoolAvailable();
}
