namespace GenHub.Core.Interfaces.Providers;

/// <summary>
/// Compares publisher version strings using the version scheme declared by that
/// publisher's provider definition.
/// </summary>
public interface IContentVersionComparer
{
    /// <summary>
    /// Compares two versions published by the same publisher.
    /// </summary>
    /// <param name="version1">The first version.</param>
    /// <param name="version2">The second version.</param>
    /// <param name="publisherType">The publisher whose scheme applies.</param>
    /// <returns>A negative value, zero, or a positive value as <paramref name="version1"/> is older, equal, or newer.</returns>
    int Compare(string? version1, string? version2, string? publisherType);

    /// <summary>
    /// Determines whether a candidate version supersedes the baseline version.
    /// </summary>
    /// <param name="candidate">The version being offered.</param>
    /// <param name="baseline">The version currently held.</param>
    /// <param name="publisherType">The publisher whose scheme applies.</param>
    /// <returns><c>true</c> if <paramref name="candidate"/> is newer.</returns>
    bool IsNewer(string? candidate, string? baseline, string? publisherType);

    /// <summary>
    /// Gets the version scheme for a publisher, for use as a LINQ ordering comparer.
    /// </summary>
    /// <param name="publisherType">The publisher whose scheme applies.</param>
    /// <returns>The publisher's version scheme.</returns>
    IVersionScheme GetScheme(string? publisherType);
}
