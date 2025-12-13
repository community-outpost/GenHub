namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Represents a conflict rule between content items.
/// Defines how conflicts should be detected and resolved.
/// </summary>
public class ConflictRule
{
    /// <summary>
    /// Gets or sets the ID of the conflicting content.
    /// </summary>
    public ManifestId ConflictingContentId { get; set; } = ManifestId.Create("1.0.unknown.unknown.placeholder");

    /// <summary>
    /// Gets or sets the type of conflict.
    /// </summary>
    public ConflictType ConflictType { get; set; } = ConflictType.HardConflict;

    /// <summary>
    /// Gets or sets the resolution strategy for this conflict.
    /// </summary>
    public ConflictResolutionStrategy ResolutionStrategy { get; set; } = ConflictResolutionStrategy.Block;

    /// <summary>
    /// Gets or sets a user-facing explanation of why this conflict exists.
    /// </summary>
    public string? ConflictReason { get; set; }

    /// <summary>
    /// Gets or sets a message explaining what happens when this conflict is resolved.
    /// </summary>
    public string? ResolutionMessage { get; set; }

    /// <summary>
    /// Gets or sets the version range that causes the conflict.
    /// If null, all versions of the conflicting content cause a conflict.
    /// </summary>
    public VersionConstraint? ConflictVersionRange { get; set; }

    /// <summary>
    /// Checks if a given manifest ID and version triggers this conflict rule.
    /// </summary>
    /// <param name="manifestId">The manifest ID to check.</param>
    /// <param name="version">The version to check.</param>
    /// <returns>True if the manifest triggers this conflict.</returns>
    public bool IsTriggeredBy(ManifestId manifestId, string? version)
    {
        // Check if the manifest ID matches
        if (!ConflictingContentId.Equals(manifestId))
        {
            return false;
        }

        // If no version constraint, the conflict applies to all versions
        if (ConflictVersionRange == null)
        {
            return true;
        }

        // Check if the version falls within the conflict range
        return ConflictVersionRange.IsSatisfiedBy(version);
    }
}
