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
    /// <summary>
/// Selects releases that match the specified version display policy.
/// </summary>
/// <param name="releases">The collection of content releases to filter.</param>
/// <param name="policy">The version selection policy to apply when filtering.</param>
/// <returns>An enumerable of content releases from <paramref name="releases"/> that conform to <paramref name="policy"/>.</returns>
    IEnumerable<ContentRelease> SelectReleases(IEnumerable<ContentRelease> releases, VersionPolicy policy);

    /// <summary>
    /// Gets the latest stable release from a collection.
    /// </summary>
    /// <param name="releases">All available releases.</param>
    /// <summary>
/// Retrieves the most recent stable content release from the provided collection.
/// </summary>
/// <param name="releases">Collection of content releases to search.</param>
/// <returns>The most recent stable <see cref="ContentRelease"/>, or <c>null</c> if none exist.</returns>
    ContentRelease? GetLatestStable(IEnumerable<ContentRelease> releases);

    /// <summary>
    /// Gets the latest release (including prereleases) from a collection.
    /// </summary>
    /// <param name="releases">All available releases.</param>
    /// <summary>
/// Retrieves the latest release from the provided collection, including prereleases.
/// </summary>
/// <param name="releases">Collection of releases to search.</param>
/// <returns>The latest ContentRelease from the collection, or null if none exist.</returns>
    ContentRelease? GetLatest(IEnumerable<ContentRelease> releases);
}