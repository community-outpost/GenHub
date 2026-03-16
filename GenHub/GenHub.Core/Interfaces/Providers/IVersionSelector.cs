using GenHub.Core.Models.Providers;

namespace GenHub.Core.Interfaces.Providers;

/// <summary>
/// Filters content releases based on version display policy.
/// </summary>
public interface IVersionSelector
{
    /// <summary>
    /// Selects releases based on the specified policy.
    /// </summary>
    /// <param name="releases">All available releases.</param>
    /// <param name="policy">The version selection policy.</param>
    /// <returns>Filtered releases according to policy.</returns>
    IEnumerable<ContentRelease> SelectReleases(IEnumerable<ContentRelease> releases, VersionPolicy policy);

    /// <summary>
    /// Gets the latest stable release from a collection.
    /// </summary>
    /// <param name="releases">All available releases.</param>
    /// <returns>The latest stable release, or null if none exist.</returns>
    ContentRelease? GetLatestStable(IEnumerable<ContentRelease> releases);

    /// <summary>
    /// Gets the latest release (including prereleases) from a collection.
    /// </summary>
    /// <param name="releases">All available releases.</param>
    /// <returns>The latest release, or null if none exist.</returns>
    ContentRelease? GetLatest(IEnumerable<ContentRelease> releases);
}
