using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// A required dependency that the content selected for a profile does not have.
/// </summary>
public sealed record ContentDependencyGap
{
    /// <summary>
    /// Gets the player-facing name of the missing item.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the declared manifest ID of the missing item. Use it for diagnostics only.
    /// </summary>
    public required string DependencyId { get; init; }

    /// <summary>
    /// Gets the content type of the missing item.
    /// </summary>
    public required ContentType DependencyType { get; init; }

    /// <summary>
    /// Gets a value indicating whether GenHub can download the missing item itself.
    /// </summary>
    public required bool CanDownload { get; init; }

    /// <summary>
    /// Gets the player-facing name of the content that requires the item.
    /// </summary>
    public required string RequiredByName { get; init; }

    /// <summary>
    /// Gets the manifest ID of the content that requires the item. Use it for diagnostics only.
    /// </summary>
    public required string RequiredById { get; init; }
}
