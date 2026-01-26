namespace GenHub.Core.Interfaces.Providers;

/// <summary>
/// Defines the version filtering policy for content display.
/// </summary>
public enum VersionPolicy
{
    /// <summary>
    /// Show only the latest stable release (default).
    /// </summary>
    LatestStableOnly,

    /// <summary>
    /// Show all versions including older releases.
    /// </summary>
    AllVersions,

    /// <summary>
    /// Show the latest release including prereleases. Note: This returns only the single most recent release (which may be a prerelease), not all releases.
    /// </summary>
    LatestIncludingPrereleases,
}
