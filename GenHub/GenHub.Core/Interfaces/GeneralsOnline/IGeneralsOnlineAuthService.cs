using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GeneralsOnline;

/// <summary>
/// Manages the Generals Online authentication lifecycle: silent refresh login,
/// browser game-code login, session state, and sign-out.
/// </summary>
public interface IGeneralsOnlineAuthService
{
    /// <summary>
    /// Gets the current authentication state.
    /// </summary>
    GeneralsOnlineAuthState AuthState { get; }

    /// <summary>
    /// Gets the signed-in display name, or null when unauthenticated.
    /// </summary>
    string? CurrentDisplayName { get; }

    /// <summary>
    /// Gets the signed-in user id, or null when unauthenticated.
    /// </summary>
    long? CurrentUserId { get; }

    /// <summary>
    /// Gets the live session token, or null when unauthenticated.
    /// </summary>
    string? CurrentSessionToken { get; }

    /// <summary>
    /// Gets the WebSocket URI issued with the session, or empty when unauthenticated.
    /// </summary>
    string WebSocketUri { get; } // skipcq: CS-A1000

    /// <summary>
    /// Occurs when the authentication state changes.
    /// </summary>
    event EventHandler<GeneralsOnlineAuthState>? AuthStateChanged;

    /// <summary>
    /// Attempts a silent login with the stored refresh token.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The login result on success; a failure result names the cause.</returns>
    Task<OperationResult<LoginResult>> TryLoginWithStoredTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a browser login: issues a code, opens the login page, and polls
    /// until the user authorizes, the timeout lapses, or cancellation is requested.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The login result on success; a failure result names the cause.</returns>
    Task<OperationResult<LoginResult>> LoginWithBrowserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current session token for authenticated API calls.
    /// </summary>
    /// <returns>The session token, or null when unauthenticated.</returns>
    Task<string?> GetSessionTokenAsync();

    /// <summary>
    /// Signs out by clearing the session and the stored refresh token.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task<OperationResult<bool>> LogoutAsync(CancellationToken cancellationToken = default);
}
