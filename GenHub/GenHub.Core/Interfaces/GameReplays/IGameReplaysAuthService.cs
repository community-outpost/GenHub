using GenHub.Core.Models.GameReplays;
using GenHub.Core.Models.Results;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GameReplays;

/// <summary>
/// OAuth 2.0 authentication service for GameReplays.
/// Handles login flow, token management, and user session.
/// </summary>
public interface IGameReplaysAuthService
{
    /// <summary>
    /// Gets the authorization URL for OAuth 2.0 login.
    /// </summary>
    /// <returns>Result containing the authorization URL.</returns>
    OperationResult<string> GetAuthorizationUrl();

    /// <summary>
    /// Exchanges authorization code for access token.
    /// </summary>
    /// <param name="code">The authorization code from callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the OAuth token response.</returns>
    Task<OperationResult<OAuthTokenResponse>> ExchangeCodeForTokenAsync(
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the access token using refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the new OAuth token response.</returns>
    Task<OperationResult<OAuthTokenResponse>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets user info using the access token.
    /// </summary>
    /// <param name="accessToken">The access token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the user info.</returns>
    Task<OperationResult<OAuthUserInfo>> GetUserInfoAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the OAuth state parameter for CSRF protection.
    /// </summary>
    /// <param name="state">The state parameter from callback.</param>
    /// <returns>True if state is valid, false otherwise.</returns>
    bool ValidateState(string state);

    /// <summary>
    /// Generates a new OAuth state parameter.
    /// </summary>
    /// <returns>The generated state string.</returns>
    string GenerateState();

    /// <summary>
    /// Saves the OAuth token response to secure storage.
    /// </summary>
    /// <param name="tokenResponse">The token response to save.</param>
    /// <returns>Result indicating success or failure.</returns>
    Task<OperationResult<bool>> SaveTokenAsync(OAuthTokenResponse tokenResponse);

    /// <summary>
    /// Loads the OAuth token response from secure storage.
    /// </summary>
    /// <returns>Result containing the token response or null if not found.</returns>
    Task<OperationResult<OAuthTokenResponse?>> LoadTokenAsync();

    /// <summary>
    /// Clears the saved OAuth token from secure storage.
    /// </summary>
    /// <returns>Result indicating success or failure.</returns>
    Task<OperationResult<bool>> ClearTokenAsync();

    /// <summary>
    /// Checks if the current token is expired or needs refresh.
    /// </summary>
    /// <returns>True if token is expired, false otherwise.</returns>
    bool IsTokenExpired();

    /// <summary>
    /// Gets the current access token.
    /// </summary>
    /// <returns>The access token or null if not authenticated.</returns>
    string? GetAccessToken();

    /// <summary>
    /// Gets the current refresh token.
    /// </summary>
    /// <returns>The refresh token or null if not available.</returns>
    string? GetRefreshToken();

    /// <summary>
    /// Gets the current authenticated user info.
    /// </summary>
    /// <returns>The user info or null if not authenticated.</returns>
    OAuthUserInfo? GetCurrentUser();

    /// <summary>
    /// Sets the current user info.
    /// </summary>
    /// <param name="userInfo">The user info to set.</param>
    void SetCurrentUser(OAuthUserInfo userInfo);
}
