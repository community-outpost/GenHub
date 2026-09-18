using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using Octokit;
using System;
using System.IO;
using System.Net.Http;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GitHub.Services;

/// <summary>
/// Authenticates the user with GitHub using the OAuth 2.0 device authorization flow (RFC 8628).
/// </summary>
/// <param name="gitHubClient">The Octokit GitHub client.</param>
/// <param name="tokenStorage">The secure token storage, or null when unavailable on this platform.</param>
/// <param name="logger">The logger.</param>
public class GitHubAuthService(
    IGitHubClient gitHubClient,
    IGitHubTokenStorage? tokenStorage,
    ILogger<GitHubAuthService> logger) : IGitHubAuthService
{
    private readonly object _syncLock = new();
    private GitHubUserProfile? _currentUser;
    private bool _sessionSignedOut;

    /// <inheritdoc />
    public bool IsAuthenticated
    {
        get
        {
            lock (_syncLock)
            {
                if (_sessionSignedOut)
                {
                    return false;
                }
            }

            return HasClientCredentials() || tokenStorage?.HasToken() == true || HasEnvironmentToken();
        }
    }

    /// <inheritdoc />
    public GitHubUserProfile? CurrentUser
    {
        get
        {
            lock (_syncLock)
            {
                return _sessionSignedOut ? null : _currentUser;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<GitHubAuthStateChangedEventArgs>? AuthStateChanged;

    /// <inheritdoc />
    public async Task<OperationResult<GitHubDeviceCodeResponse>> InitiateLoginAsync(CancellationToken cancellationToken = default)
    {
        var clientId = ResolveClientId();
        if (string.IsNullOrEmpty(clientId))
        {
            return OperationResult<GitHubDeviceCodeResponse>.CreateFailure(
                $"GitHub OAuth client ID is not configured. Set the {GitHubConstants.OAuthClientIdEnvVar} environment variable.");
        }

        try
        {
            var request = new OauthDeviceFlowRequest(clientId)
            {
                Scopes = { GitHubConstants.OAuthScopePublicRepo, GitHubConstants.OAuthScopeReadUser },
            };
            var response = await gitHubClient.Oauth.InitiateDeviceFlow(request, cancellationToken).ConfigureAwait(false);
            return OperationResult<GitHubDeviceCodeResponse>.CreateSuccess(new GitHubDeviceCodeResponse(
                response.DeviceCode,
                response.UserCode,
                response.VerificationUri,
                response.ExpiresIn,
                response.Interval));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to initiate GitHub device flow");
            return OperationResult<GitHubDeviceCodeResponse>.CreateFailure($"GitHub request failed: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to initiate GitHub device flow");
            return OperationResult<GitHubDeviceCodeResponse>.CreateFailure($"Network request failed: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "GitHub device flow initiation timed out");
            return OperationResult<GitHubDeviceCodeResponse>.CreateFailure("GitHub request timed out. Check your connection and try again.");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<GitHubUserProfile>> WaitForAuthorizationAsync(
        GitHubDeviceCodeResponse deviceCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deviceCode);
        if (string.IsNullOrEmpty(deviceCode.DeviceCode))
        {
            throw new ArgumentException("Device code must not be empty.", nameof(deviceCode));
        }

        var clientId = ResolveClientId();
        if (string.IsNullOrEmpty(clientId))
        {
            return OperationResult<GitHubUserProfile>.CreateFailure(
                $"GitHub OAuth client ID is not configured. Set the {GitHubConstants.OAuthClientIdEnvVar} environment variable.");
        }

        try
        {
            var oauthResponse = new OauthDeviceFlowResponse(
                deviceCode.DeviceCode,
                deviceCode.UserCode,
                deviceCode.VerificationUri,
                deviceCode.ExpiresInSeconds,
                deviceCode.PollingIntervalSeconds);
            var token = await gitHubClient.Oauth
                .CreateAccessTokenForDeviceFlow(clientId, oauthResponse, cancellationToken)
                .ConfigureAwait(false);

            await PersistLoginAsync(token.AccessToken).ConfigureAwait(false);

            var profile = await FetchUserProfileAsync(cancellationToken).ConfigureAwait(false);
            if (profile == null)
            {
                return OperationResult<GitHubUserProfile>.CreateFailure(
                    "Signed in, but the GitHub profile could not be loaded. Check your connection and reopen Settings.");
            }

            SetCurrentUser(profile);
            RaiseAuthStateChanged(true, profile);
            logger.LogInformation("Signed in to GitHub as {Login}", profile.Login);
            return OperationResult<GitHubUserProfile>.CreateSuccess(profile);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException ex)
        {
            var terminalError = GetDeviceFlowTerminalError(ex);
            if (terminalError != null)
            {
                logger.LogInformation("GitHub device authorization failed: {Error}", terminalError);
                return OperationResult<GitHubUserProfile>.CreateFailure(terminalError);
            }

            logger.LogWarning(ex, "GitHub device authorization polling failed");
            return OperationResult<GitHubUserProfile>.CreateFailure($"GitHub request failed: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "GitHub device authorization polling failed");
            return OperationResult<GitHubUserProfile>.CreateFailure($"Network request failed: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "GitHub device authorization polling timed out");
            return OperationResult<GitHubUserProfile>.CreateFailure("GitHub request timed out. Check your connection and try again.");
        }
    }

    /// <inheritdoc />
    public async Task<GitHubUserProfile?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            return null;
        }

        lock (_syncLock)
        {
            if (_currentUser != null)
            {
                return _currentUser;
            }
        }

        var profile = await FetchUserProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile != null)
        {
            SetCurrentUser(profile);
        }

        return profile;
    }

    /// <inheritdoc />
    public Task<SecureString?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        lock (_syncLock)
        {
            if (_sessionSignedOut)
            {
                return Task.FromResult<SecureString?>(null);
            }
        }

        return LoadAccessTokenAsync();
    }

    /// <inheritdoc />
    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        lock (_syncLock)
        {
            _sessionSignedOut = true;
            _currentUser = null;
        }

        return ClearLoginAsync();
    }

    private static string ResolveClientId()
    {
        var overrideClientId = Environment.GetEnvironmentVariable(GitHubConstants.OAuthClientIdEnvVar);
        if (!string.IsNullOrWhiteSpace(overrideClientId))
        {
            return overrideClientId.Trim();
        }

        return GitHubConstants.DefaultOAuthClientId;
    }

    private static bool HasEnvironmentToken()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(GitHubConstants.GenHubTokenEnvVar))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(GitHubConstants.GitHubTokenEnvVar));
    }

    private static string? GetDeviceFlowTerminalError(ApiException ex)
    {
        // Octokit polls internally and throws for terminal device-flow states with
        // "{error}: {description}\n{uri}", so a denial or expiry never returns a token.
        var message = ex.Message;
        if (message.StartsWith(GitHubConstants.DeviceFlowErrorAccessDenied, StringComparison.OrdinalIgnoreCase))
        {
            return "GitHub authorization was denied. Approve the request in your browser to sign in.";
        }

        if (message.StartsWith(GitHubConstants.DeviceFlowErrorExpiredToken, StringComparison.OrdinalIgnoreCase))
        {
            return "The device code expired before approval. Try signing in again.";
        }

        return null;
    }

    private bool HasClientCredentials()
    {
        return gitHubClient is GitHubClient concreteClient
            && concreteClient.Credentials is { } credentials
            && credentials != Credentials.Anonymous;
    }

    private void SetClientCredentials(Credentials credentials)
    {
        if (gitHubClient is GitHubClient concreteClient)
        {
            concreteClient.Credentials = credentials;
        }
        else
        {
            logger.LogWarning("GitHub client does not support setting credentials");
        }
    }

    private async Task PersistLoginAsync(string accessToken)
    {
        using var secureToken = SecureStringHelper.ToSecureString(accessToken);
        if (tokenStorage != null)
        {
            await tokenStorage.SaveTokenAsync(secureToken).ConfigureAwait(false);
        }
        else
        {
            logger.LogWarning("No token storage available; GitHub login lasts for this session only");
        }

        SetClientCredentials(new Credentials(accessToken));
        lock (_syncLock)
        {
            _sessionSignedOut = false;
        }
    }

    private async Task ClearLoginAsync()
    {
        if (tokenStorage != null)
        {
            try
            {
                await tokenStorage.DeleteTokenAsync().ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Failed to delete stored GitHub token");
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "Failed to delete stored GitHub token");
            }
        }

        SetClientCredentials(Credentials.Anonymous);
        RaiseAuthStateChanged(false, null);
        logger.LogInformation("Signed out of GitHub");
    }

    private async Task<SecureString?> LoadAccessTokenAsync()
    {
        if (tokenStorage?.HasToken() == true)
        {
            try
            {
                var storedToken = await tokenStorage.LoadTokenAsync().ConfigureAwait(false);
                if (storedToken is { Length: > 0 })
                {
                    return storedToken;
                }

                storedToken?.Dispose();
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Failed to load stored GitHub token");
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "Failed to load stored GitHub token");
            }
        }

        var environmentToken = Environment.GetEnvironmentVariable(GitHubConstants.GenHubTokenEnvVar)
            ?? Environment.GetEnvironmentVariable(GitHubConstants.GitHubTokenEnvVar);
        if (!string.IsNullOrEmpty(environmentToken))
        {
            return SecureStringHelper.ToSecureString(environmentToken);
        }

        if (gitHubClient is GitHubClient concreteClient
            && concreteClient.Credentials is { } credentials
            && credentials != Credentials.Anonymous
            && !string.IsNullOrEmpty(credentials.Password))
        {
            return SecureStringHelper.ToSecureString(credentials.Password);
        }

        return null;
    }

    private async Task<GitHubUserProfile?> FetchUserProfileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await EnsureClientCredentialsAsync().ConfigureAwait(false);
            var user = await gitHubClient.User.Current().ConfigureAwait(false);
            return new GitHubUserProfile(user.Login, user.Id, user.Name, user.AvatarUrl, user.HtmlUrl);
        }
        catch (AuthorizationException ex)
        {
            logger.LogWarning(ex, "GitHub token was rejected while loading the user profile");
            return null;
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to load GitHub user profile");
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to load GitHub user profile");
            return null;
        }
    }

    private async Task EnsureClientCredentialsAsync()
    {
        if (HasClientCredentials())
        {
            return;
        }

        using var token = await LoadAccessTokenAsync().ConfigureAwait(false);
        if (token is { Length: > 0 })
        {
            SetClientCredentials(new Credentials(SecureStringHelper.ToUnsecureString(token)));
        }
    }

    private void SetCurrentUser(GitHubUserProfile profile)
    {
        lock (_syncLock)
        {
            _currentUser = profile;
        }
    }

    private void RaiseAuthStateChanged(bool isAuthenticated, GitHubUserProfile? user)
    {
        AuthStateChanged?.Invoke(this, new GitHubAuthStateChangedEventArgs(isAuthenticated, user));
    }
}
