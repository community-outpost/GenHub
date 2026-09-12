namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the strategy used when updating content.
/// </summary>
public enum UpdateStrategy
{
    /// <summary>
    /// Replaces the current version in existing profiles.
    /// </summary>
    ReplaceCurrent,

    /// <summary>
    /// Creates a new profile for the new version, keeping existing profiles intact.
    /// </summary>
    CreateNewProfile,
}
