namespace GenHub.Core.Models.GameReplays;

/// <summary>
/// Represents an OAuth token response from GameReplays.
/// </summary>
public class OAuthTokenResponse
{
    /// <summary>
    /// Gets or sets the access token.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the token type (e.g., "Bearer").
    /// </summary>
    public string TokenType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the token expiration time in seconds.
    /// </summary>
    public int ExpiresIn { get; set; }

    /// <summary>
    /// Gets or sets the refresh token.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the granted scopes.
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the token response was created/received.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the expiration timestamp.
    /// </summary>
    public DateTime ExpiresAt => CreatedAt.AddSeconds(ExpiresIn);
}
