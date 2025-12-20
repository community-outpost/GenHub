namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the update channel for receiving application updates.
/// </summary>
public enum UpdateChannel
{
    /// <summary>
    /// Stable releases only (GitHub Releases without prerelease tag).
    /// </summary>
    Stable,

    /// <summary>
    /// Alpha/beta/RC releases (GitHub Releases with prerelease identifiers).
    /// </summary>
    Prerelease,

    /// <summary>
    /// CI artifacts (requires GitHub PAT, for testers and developers).
    /// </summary>
    Artifacts,
}