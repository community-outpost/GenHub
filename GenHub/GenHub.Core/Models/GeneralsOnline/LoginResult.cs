using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents the result of a login operation from the Generals Online API.
/// Used for both CheckLogin and LoginWithToken responses.
/// </summary>
public class LoginResult
{
    /// <summary>
    /// Gets or sets the login result state.
    /// </summary>
    [JsonPropertyName("result")]
    public PendingLoginState Result { get; set; } = PendingLoginState.None;

    /// <summary>
    /// Gets or sets the session token for authenticated API calls.
    /// Only populated on successful login.
    /// </summary>
    [JsonPropertyName("session_token")]
    public string SessionToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the refresh token for persistent authentication.
    /// Should be stored securely (encrypted with DPAPI).
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's unique identifier.
    /// </summary>
    [JsonPropertyName("user_id")]
    public long UserId { get; set; } = -1;

    /// <summary>
    /// Gets or sets the user's display name.
    /// </summary>
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the WebSocket URI for real-time communication.
    /// </summary>
    [JsonPropertyName("ws_uri")]
    public string WebSocketUri { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the login was successful.
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Result == PendingLoginState.LoginSuccess;

    /// <summary>
    /// Gets a value indicating whether the login is still pending (waiting for browser auth).
    /// </summary>
    [JsonIgnore]
    public bool IsPending => Result == PendingLoginState.Waiting;

    /// <summary>
    /// Gets a value indicating whether the login failed.
    /// </summary>
    [JsonIgnore]
    public bool IsFailed => Result == PendingLoginState.LoginFailed;
}
