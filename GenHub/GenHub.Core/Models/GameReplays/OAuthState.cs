using GenHub.Core.Constants;

namespace GenHub.Core.Models.GameReplays;

/// <summary>
/// Represents an OAuth state for CSRF protection.
/// </summary>
public class OAuthState
{
    /// <summary>
    /// Gets or sets the state token.
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when this state was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the redirect URI.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether this state has expired.
    /// </summary>
    public bool IsExpired => DateTime.UtcNow > CreatedAt.AddMinutes(
        GameReplaysConstants.OAuthStateValidityMinutes);
}
