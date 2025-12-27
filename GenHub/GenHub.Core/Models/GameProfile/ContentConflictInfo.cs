using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Information about content conflicts when attempting to add content to a profile.
/// </summary>
public class ContentConflictInfo
{
    /// <summary>
    /// Gets or sets a value indicating whether there is a conflict.
    /// </summary>
    public bool HasConflict { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the conflict is due to game client exclusivity.
    /// </summary>
    public bool IsGameClientConflict { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the conflict is due to exclusive content types.
    /// </summary>
    public bool IsExclusiveContentConflict { get; set; }

    /// <summary>
    /// Gets or sets the manifest ID of the conflicting content.
    /// </summary>
    public string? ConflictingContentId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the conflicting content.
    /// </summary>
    public string? ConflictingContentName { get; set; }

    /// <summary>
    /// Gets or sets the content type of the conflicting content.
    /// </summary>
    public ContentType ConflictingContentType { get; set; }

    /// <summary>
    /// Gets or sets a user-friendly message describing the conflict.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the conflict can be auto-resolved by swapping.
    /// </summary>
    public bool CanAutoResolve { get; set; }

    /// <summary>
    /// Creates a result indicating no conflict.
    /// </summary>
    /// <returns>A <see cref="ContentConflictInfo"/> with no conflict.</returns>
    public static ContentConflictInfo NoConflict()
    {
        return new ContentConflictInfo { HasConflict = false };
    }

    /// <summary>
    /// Creates a result indicating a game client conflict.
    /// </summary>
    /// <param name="existingClientId">The existing game client ID.</param>
    /// <param name="existingClientName">The existing game client name.</param>
    /// <returns>A <see cref="ContentConflictInfo"/> with game client conflict details.</returns>
    public static ContentConflictInfo GameClientConflict(string existingClientId, string? existingClientName)
    {
        return new ContentConflictInfo
        {
            HasConflict = true,
            IsGameClientConflict = true,
            ConflictingContentId = existingClientId,
            ConflictingContentName = existingClientName,
            ConflictingContentType = ContentType.GameClient,
            Message = $"This will replace the current game client: {existingClientName ?? existingClientId}",
            CanAutoResolve = true,
        };
    }

    /// <summary>
    /// Creates a result indicating an exclusive content conflict.
    /// </summary>
    /// <param name="existingContentId">The existing content ID.</param>
    /// <param name="existingContentName">The existing content name.</param>
    /// <param name="contentType">The content type.</param>
    /// <returns>A <see cref="ContentConflictInfo"/> with exclusive content conflict details.</returns>
    public static ContentConflictInfo ExclusiveContentConflict(
        string existingContentId,
        string? existingContentName,
        ContentType contentType)
    {
        return new ContentConflictInfo
        {
            HasConflict = true,
            IsExclusiveContentConflict = true,
            ConflictingContentId = existingContentId,
            ConflictingContentName = existingContentName,
            ConflictingContentType = contentType,
            Message = $"This will replace the existing {contentType}: {existingContentName ?? existingContentId}",
            CanAutoResolve = true,
        };
    }
}