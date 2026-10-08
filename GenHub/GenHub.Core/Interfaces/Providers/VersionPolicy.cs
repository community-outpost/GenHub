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
    /// Include prerelease versions in addition to stable releases.
    /// </summary>
    IncludePrereleases,
}
