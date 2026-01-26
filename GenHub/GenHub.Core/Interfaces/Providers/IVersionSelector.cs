using GenHub.Core.Models.Providers;

namespace GenHub.Core.Interfaces.Providers;

/// <summary>
/// Filters content releases based on version display policy.
/// </summary>
public interface IVersionSelector
{
    /// <summary>
    /// Selects releases that match the specified version display policy.
    /// </summary>
    /// <param name="releases">The collection of content releases to filter.</param>
    /// <param name="policy">The version selection policy to apply when filtering.</param>
    /// <returns>An enumerable of content releases from <paramref name="releases"/> that conform to <paramref name="policy"/>.</returns>
    IEnumerable<ContentRelease> SelectReleases(IEnumerable<ContentRelease> releases, VersionPolicy policy);

    /// <summary>
    /// Gets the latest stable release from the provided collection.
    /// </summary>
    /// <param name="releases">The available releases.</param>
    /// <returns>The latest stable release, or null if none exist.</returns>
    ContentRelease? GetLatestStable(IEnumerable<ContentRelease> releases);

    /// <summary>
    /// Gets the latest release from the provided collection.
    /// </summary>
    /// <param name="releases">The available releases to select from.</param>
    /// <returns>The latest release, or null if none exist.</returns>
    ContentRelease? GetLatest(IEnumerable<ContentRelease> releases);
}