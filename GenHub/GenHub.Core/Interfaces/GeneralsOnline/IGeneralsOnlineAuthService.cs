using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GeneralsOnline;

namespace GenHub.Core.Interfaces.GeneralsOnline;

/// <summary>
/// Interface for Generals Online authentication and credential management.
/// </summary>
public interface IGeneralsOnlineAuthService
{
    /// <summary>
    /// Gets an observable that emits the current authentication status.
    /// </summary>
    IObservable<bool> IsAuthenticated { get; }

    /// <summary>
    /// Gets the current authentication token, if any.
    /// </summary>
    string? CurrentToken { get; }

    /// <summary>
    /// Gets the current session token for API calls, if authenticated.
    /// </summary>
    string? CurrentSessionToken { get; }

    /// <summary>
    /// Gets the currently logged in user's display name, if authenticated.
    /// </summary>
    string? CurrentDisplayName { get; }

    /// <summary>
    /// Gets the currently logged in user's ID, if authenticated.
    /// </summary>
    long? CurrentUserId { get; }

    /// <summary>
    /// Validates the presence and content of the credential file.
    /// </summary>
    /// <returns>True if a token is found in the file, otherwise false.</returns>
    Task<bool> ValidateCredentialsAsync();

    /// <summary>
    /// Verifies the provided token with the server.
    /// </summary>
    /// <param name="token">The token to verify.</param>
    /// <returns>True if the token is valid, otherwise false.</returns>
    Task<bool> VerifyTokenWithServerAsync(string token);

    /// <summary>
    /// Gets the authentication token if available.
    /// </summary>
    /// <returns>The token string or null.</returns>
    Task<string?> GetAuthTokenAsync();

    /// <summary>
    /// Initializes the service and starts monitoring for credential changes.
    /// Attempts silent login if credentials exist.
    /// </summary>
    /// <returns>A task representing the asynchronous initialization operation.</returns>
    Task InitializeAsync();

    /// <summary>
    /// Saves a refresh token to the credentials file.
    /// </summary>
    /// <param name="refreshToken">The refresh token to save.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveRefreshTokenAsync(string refreshToken);

    /// <summary>
    /// Generates a random gamecode for browser login flow.
    /// </summary>
    /// <returns>A random alphanumeric gamecode.</returns>
    string GenerateGameCode();

    /// <summary>
    /// Gets the login URL with the specified gamecode for browser authentication.
    /// </summary>
    /// <param name="gameCode">The gamecode to include in the URL.</param>
    /// <returns>The full login URL.</returns>
    string GetLoginUrl(string gameCode);

    /// <summary>
    /// Attempts to login with stored credentials (silent login).
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The login result, or null if no credentials are stored or login fails.</returns>
    Task<LoginResult?> TryLoginWithStoredCredentialsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a successful login result by storing credentials and updating state.
    /// </summary>
    /// <param name="loginResult">The login result to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ProcessLoginSuccessAsync(LoginResult loginResult);

    /// <summary>
    /// Logs out the current user by clearing credentials and state.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task LogoutAsync();
}
