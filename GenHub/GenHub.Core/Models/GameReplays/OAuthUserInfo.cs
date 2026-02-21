namespace GenHub.Core.Models.GameReplays;

/// <summary>
/// Represents user information from GameReplays OAuth.
/// </summary>
public class OAuthUserInfo
{
    /// <summary>
    /// Gets or sets the user ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string MembersDisplayName { get; set; } = string.Empty;
}
