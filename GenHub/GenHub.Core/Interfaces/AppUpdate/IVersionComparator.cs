namespace GenHub.Core.Interfaces.AppUpdate;

/// <summary>
/// Provides version comparison functionality for semantic version strings.
/// </summary>
public interface IVersionComparator
{
    /// <summary>
    /// Compares two version strings using semantic versioning rules.
    /// </summary>
    /// <param name="versionA">The first version to compare.</param>
    /// <param name="versionB">The second version to compare.</param>
    /// <returns>
    /// Less than zero if versionA is less than versionB;
    /// zero if they are equal;
    /// greater than zero if versionA is greater than versionB.
    /// </returns>
    int Compare(string versionA, string versionB);

    /// <summary>
    /// Determines if the candidate version is newer than the current version.
    /// </summary>
    /// <param name="current">The current version string.</param>
    /// <param name="candidate">The candidate version string.</param>
    /// <returns>True if candidate is newer; otherwise, false.</returns>
    bool IsNewer(string current, string candidate);
}
