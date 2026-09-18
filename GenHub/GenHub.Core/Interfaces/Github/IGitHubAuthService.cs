using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Results;
using System;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GitHub;

/// <summary>
/// Authenticates the user with GitHub using the OAuth 2.0 device authorization flow (RFC 8628).
/// The access token is persisted with <see cref="IGitHubTokenStorage"/> and the
/// GENHUB_GITHUB_TOKEN / GITHUB_TOKEN environment variables stay supported as a headless fallback.
/// </summary>
public interface IGitHubAuthService
{
    /// <summary>
    /// Gets a value indicating whether GitHub API calls are currently authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the currently signed in user profile, or null when signed out or not yet loaded.
    /// </summary>
    GitHubUserProfile? CurrentUser { get; }

    /// <summary>
    /// Occurs when the authentication state changes through sign-in or sign-out.
    /// </summary>
    event EventHandler<GitHubAuthStateChangedEventArgs>? AuthStateChanged;

    /// <summary>
    /// Starts a new device authorization flow and returns the user code to display.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The device code response, or a failure when the flow cannot start.</returns>
    Task<OperationResult<GitHubDeviceCodeResponse>> InitiateLoginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls GitHub until the user approves or denies the device code, then persists the access token.
    /// </summary>
    /// <param name="deviceCode">The device code response returned by <see cref="InitiateLoginAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The signed in user profile, or a failure when authorization is denied, expires, or errors.</returns>
    Task<OperationResult<GitHubUserProfile>> WaitForAuthorizationAsync(
        GitHubDeviceCodeResponse deviceCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current user profile, fetching it from the API when authenticated but not yet loaded.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The user profile, or null when signed out or the lookup fails.</returns>
    Task<GitHubUserProfile?> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the access token for direct GitHub API calls.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored or environment-provided token, or null when unavailable.</returns>
    Task<SecureString?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs out by deleting the stored token and clearing local credentials.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the sign-out operation.</returns>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}
