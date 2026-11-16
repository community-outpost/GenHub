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
    private readonly SemaphoreSlim _credentialGate = new(1, 1);
    private GitHubUserProfile? _currentUser;
    private bool _sessionSignedOut;
    private bool _sessionExpired;
    private bool _credentialUnusable;
    private volatile int _credentialGeneration;

    /// <inheritdoc />
    public bool IsAuthenticated
    {
        get
        {
            lock (_syncLock)
            {
                if (_sessionSignedOut || _sessionExpired || _credentialUnusable)
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
                return _sessionSignedOut || _sessionExpired || _credentialUnusable ? null : _currentUser;
            }
        }
    }

    /// <inheritdoc />
    public bool IsSessionExpired
    {
        get
        {
            lock (_syncLock)
            {
                return _sessionExpired;
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

        // A new login supersedes any previous sign-out, so the post-poll guard in
        // WaitForAuthorizationAsync only aborts when sign-out happens during this attempt.
        BeginLoginAttempt();

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

            // Stage the credentials in memory so the profile fetch below is authenticated.
            // Nothing is persisted until the profile fetch and the sign-out guard both pass,
            // keeping the visible state, the event stream, and the stored token atomic.
            SetClientCredentials(new Credentials(token.AccessToken));

            var profile = await FetchUserProfileAsync(cancellationToken).ConfigureAwait(false);
            if (profile == null)
            {
                SetClientCredentials(Credentials.Anonymous);
                return OperationResult<GitHubUserProfile>.CreateFailure(
                    "Signed in, but the GitHub profile could not be loaded. Check your connection and reopen Settings.");
            }

            var persistError = await TryPersistLoginAsync(token.AccessToken).ConfigureAwait(false);
            if (persistError != null)
            {
                SetClientCredentials(Credentials.Anonymous);
                return OperationResult<GitHubUserProfile>.CreateFailure(persistError);
            }

            ClearSessionErrorState();
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
            if (_sessionSignedOut || _sessionExpired || _credentialUnusable)
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
            _sessionExpired = false;
            _credentialUnusable = false;
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

    private async Task<string?> TryPersistLoginAsync(string accessToken)
    {
        // A concurrent sign-out always wins over an in-flight device flow poll.
        if (IsSessionSignedOut())
        {
            return "GitHub sign-in was cancelled.";
        }

        // Hold the gate from the save through the generation bump so a concurrent
        // rejected-token cleanup either finishes before this save or observes the new
        // generation and leaves the fresh token alone.
        await _credentialGate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var secureToken = SecureStringHelper.ToSecureString(accessToken);
            if (tokenStorage != null)
            {
                try
                {
                    await tokenStorage.SaveTokenAsync(secureToken).ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Failed to save GitHub token after device authorization");
                    return $"GitHub sign-in succeeded, but the token could not be saved: {ex.Message}";
                }
                catch (UnauthorizedAccessException ex)
                {
                    logger.LogWarning(ex, "Failed to save GitHub token after device authorization");
                    return $"GitHub sign-in succeeded, but the token could not be saved: {ex.Message}";
                }
            }
            else
            {
                logger.LogWarning("No token storage available; GitHub login lasts for this session only");
            }

            if (IsSessionSignedOut())
            {
                // Sign-out ran while the save was in flight. Remove the file this attempt
                // just wrote so a later launch does not resurrect a signed-out session.
                await DeleteStoredTokenBestEffortAsync().ConfigureAwait(false);
                return "GitHub sign-in was cancelled.";
            }

            // A fresh credential supersedes any in-flight profile fetch: a late rejection
            // for the previous credential must neither re-latch an error state nor delete
            // the token just saved.
            Interlocked.Increment(ref _credentialGeneration);
            return null;
        }
        finally
        {
            _credentialGate.Release();
        }
    }

    private async Task DeleteStoredTokenBestEffortAsync()
    {
        if (tokenStorage == null)
        {
            return;
        }

        try
        {
            await tokenStorage.DeleteTokenAsync().ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to roll back the stored GitHub token after a cancelled sign-in");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Failed to roll back the stored GitHub token after a cancelled sign-in");
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
        var credentialGeneration = _credentialGeneration;
        var hasCredential = false;
        try
        {
            hasCredential = await EnsureClientCredentialsAsync().ConfigureAwait(false);
            var user = await gitHubClient.User.Current().ConfigureAwait(false);
            return new GitHubUserProfile(user.Login, user.Id, user.Name, user.AvatarUrl, user.HtmlUrl);
        }
        catch (AuthorizationException ex)
        {
            if (hasCredential)
            {
                logger.LogWarning(ex, "GitHub token was rejected while loading the user profile");
                await MarkSessionExpiredAsync(credentialGeneration).ConfigureAwait(false);
            }
            else
            {
                logger.LogWarning(ex, "GitHub profile lookup was rejected without a credential to present");
                MarkCredentialUnusable(credentialGeneration);
            }

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
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Timed out while loading the GitHub user profile");
            return null;
        }
    }

    private void BeginLoginAttempt()
    {
        // An abandoned login attempt must not clear the expired flag: only a successful
        // sign-in or an explicit sign-out does, so the UI keeps reporting the rejected
        // session instead of flipping back to a signed in state it cannot honor.
        lock (_syncLock)
        {
            _sessionSignedOut = false;
        }
    }

    private bool IsSessionSignedOut()
    {
        lock (_syncLock)
        {
            return _sessionSignedOut;
        }
    }

    private async Task MarkSessionExpiredAsync(int credentialGeneration)
    {
        // Hold the gate across the check and the cleanup so a concurrent sign-in either
        // persists fully before this check (and is then left alone) or only after this
        // cleanup has finished (and then survives it).
        await _credentialGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_syncLock)
            {
                // An explicit sign-out always wins: a slow in-flight profile fetch must not
                // re-latch the expired flag after the user has deliberately signed out.
                // A rejection is also ignored once a newer credential was saved, so a stale
                // failure can neither resurrect the expired state nor delete the fresh token.
                if (_sessionExpired || _sessionSignedOut || credentialGeneration != _credentialGeneration)
                {
                    return;
                }

                _sessionExpired = true;
                _currentUser = null;
            }

            // Drop the rejected credential from the shared client so later API calls fail fast
            // as anonymous instead of replaying a token GitHub already refused.
            SetClientCredentials(Credentials.Anonymous);

            // Remove the rejected token from storage so the next launch does not reload and
            // re-reject the same known-bad credential, showing the expired state every time.
            await DeleteStoredTokenBestEffortAsync().ConfigureAwait(false);
            RaiseAuthStateChanged(false, null);
        }
        finally
        {
            _credentialGate.Release();
        }
    }

    private void MarkCredentialUnusable(int credentialGeneration)
    {
        lock (_syncLock)
        {
            // A stored credential exists but could not be loaded, and no credential was
            // presented: report signed out without deleting a file that was never rejected
            // and without raising the expired state reserved for refused credentials.
            // Stale failures and post-sign-out failures stay silent; sign-out and the
            // first failure already notified consumers.
            if (_credentialUnusable || _sessionExpired || _sessionSignedOut || credentialGeneration != _credentialGeneration)
            {
                return;
            }

            _credentialUnusable = true;
            _currentUser = null;
        }

        RaiseAuthStateChanged(false, null);
    }

    private void ClearSessionErrorState()
    {
        lock (_syncLock)
        {
            _sessionExpired = false;
            _credentialUnusable = false;
        }
    }

    private async Task<bool> EnsureClientCredentialsAsync()
    {
        if (HasClientCredentials())
        {
            return true;
        }

        using var token = await LoadAccessTokenAsync().ConfigureAwait(false);
        if (token is { Length: > 0 })
        {
            SetClientCredentials(new Credentials(SecureStringHelper.ToUnsecureString(token)));
            return true;
        }

        return false;
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
