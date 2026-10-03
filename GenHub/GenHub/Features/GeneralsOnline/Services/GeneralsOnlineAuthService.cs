using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GeneralsOnline.Services;

/// <summary>
/// Manages the Generals Online authentication lifecycle: silent refresh login,
/// browser game-code login, session state, and sign-out.
/// </summary>
/// <param name="apiClient">The Generals Online API client.</param>
/// <param name="tokenStorage">The refresh token storage.</param>
/// <param name="logger">The logger instance.</param>
public sealed class GeneralsOnlineAuthService(
    IGeneralsOnlineApiClient apiClient,
    IGeneralsOnlineTokenStorage tokenStorage,
    ILogger<GeneralsOnlineAuthService> logger) : IGeneralsOnlineAuthService
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly object _syncLock = new();
    private readonly Queue<GeneralsOnlineAuthState> _pendingStateEvents = new();
    private GeneralsOnlineAuthState _authState = GeneralsOnlineAuthState.Unauthenticated;
    private string? _sessionToken;
    private string? _displayName;
    private long? _userId;
    private string _webSocketUri = string.Empty;

    /// <inheritdoc />
    public GeneralsOnlineAuthState AuthState
    {
        get
        {
            lock (_syncLock)
            {
                return _authState;
            }
        }

        private set
        {
            lock (_syncLock)
            {
                _authState = value;
            }
        }
    }

    /// <inheritdoc />
    public string? CurrentDisplayName
    {
        get
        {
            lock (_syncLock)
            {
                return _displayName;
            }
        }
    }

    /// <inheritdoc />
    public long? CurrentUserId
    {
        get
        {
            lock (_syncLock)
            {
                return _userId;
            }
        }
    }

    /// <inheritdoc />
    public string? CurrentSessionToken
    {
        get
        {
            lock (_syncLock)
            {
                return _sessionToken;
            }
        }
    }

    /// <inheritdoc />
    public string WebSocketUri // skipcq: CS-A1000
    {
        get
        {
            lock (_syncLock)
            {
                return _webSocketUri;
            }
        }
    }

    /// <summary>
    /// Gets or sets the browser opener, replaceable in tests.
    /// </summary>
    internal Func<string, bool> BrowserOpener { get; set; } = TryOpenBrowser;

    /// <inheritdoc />
    public event EventHandler<GeneralsOnlineAuthState>? AuthStateChanged;

    /// <inheritdoc />
    public async Task<OperationResult<LoginResult>> TryLoginWithStoredTokenAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var stored = await tokenStorage.LoadTokenAsync();
            if (stored is null || stored.Length == 0)
            {
                logger.LogDebug("No stored Generals Online refresh token for silent login.");
                return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired);
            }

            var refreshToken = SecureStringHelper.ToUnsecureString(stored);
            var result = await apiClient.LoginWithTokenAsync(refreshToken, cancellationToken);
            if (!result.Success || result.Data is null)
            {
                if (IsDefiniteRejection(result))
                {
                    await ClearStoredTokenAsync();
                    SetState(GeneralsOnlineAuthState.Unauthenticated, null, null, null, string.Empty);
                }

                return OperationResult<LoginResult>.CreateFailure(result.Errors);
            }

            if (!result.Data.IsSuccess)
            {
                await ClearStoredTokenAsync();
                SetState(GeneralsOnlineAuthState.Unauthenticated, null, null, null, string.Empty);
                logger.LogWarning("Stored Generals Online refresh token was rejected.");
                return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired);
            }

            if (!await ApplyLoginSuccessAsync(result.Data))
            {
                return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorLoginFailed);
            }

            return OperationResult<LoginResult>.CreateSuccess(result.Data);
        }
        finally
        {
            _stateLock.Release();
            RaisePendingStateEvents();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<LoginResult>> LoginWithBrowserAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var code = await apiClient.GetLoginCodeAsync(cancellationToken);
            if (!code.Success || string.IsNullOrWhiteSpace(code.Data))
            {
                return OperationResult<LoginResult>.CreateFailure(code.Errors);
            }

            var loginUrl = string.Format(GeneralsOnlineConstants.LoginPageUrlFormat, Uri.EscapeDataString(code.Data));
            if (!BrowserOpener(loginUrl))
            {
                logger.LogWarning("Failed to open the browser for Generals Online sign-in.");
                return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorLoginFailed);
            }

            SetState(GeneralsOnlineAuthState.PendingBrowserLogin, null, null, null, string.Empty);
            try
            {
                return await PollLoginLoopAsync(code.Data, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                SetState(GeneralsOnlineAuthState.Unauthenticated, null, null, null, string.Empty);
                throw;
            }
        }
        finally
        {
            _stateLock.Release();
            RaisePendingStateEvents();
        }
    }

    /// <inheritdoc />
    public Task<string?> GetSessionTokenAsync()
    {
        return Task.FromResult(CurrentSessionToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            await ClearStoredTokenAsync();
            SetState(GeneralsOnlineAuthState.Unauthenticated, null, null, null, string.Empty);
            logger.LogInformation("Signed out of Generals Online.");
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            _stateLock.Release();
            RaisePendingStateEvents();
        }
    }

    private static bool IsDefiniteRejection(OperationResult<LoginResult> result)
    {
        return result.Errors.Any(error =>
            string.Equals(error, GeneralsOnlineConstants.ErrorLoginFailed, StringComparison.Ordinal));
    }

    private static bool TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<OperationResult<LoginResult>> PollLoginLoopAsync(string gameCode, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(GeneralsOnlineConstants.LoginPollTimeoutMinutes);
        var interval = TimeSpan.FromSeconds(GeneralsOnlineConstants.LoginPollIntervalSeconds);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var check = await apiClient.CheckLoginAsync(gameCode, cancellationToken);
            if (!check.Success || check.Data is null)
            {
                SetState(GeneralsOnlineAuthState.Unauthenticated, null, null, null, string.Empty);
                return OperationResult<LoginResult>.CreateFailure(check.Errors);
            }

            if (check.Data.IsSuccess)
            {
                if (!await ApplyLoginSuccessAsync(check.Data))
                {
                    return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorLoginFailed);
                }

                return OperationResult<LoginResult>.CreateSuccess(check.Data);
            }

            if (check.Data.IsFailed)
            {
                SetState(GeneralsOnlineAuthState.Unauthenticated, null, null, null, string.Empty);
                var error = string.IsNullOrWhiteSpace(check.Data.BanReason)
                    ? GeneralsOnlineConstants.ErrorLoginFailed
                    : GeneralsOnlineConstants.ErrorAccountBanned;
                return OperationResult<LoginResult>.CreateFailure(error);
            }

            await Task.Delay(interval, cancellationToken);
        }

        SetState(GeneralsOnlineAuthState.Unauthenticated, null, null, null, string.Empty);
        logger.LogWarning("Generals Online browser login timed out.");
        return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorLoginFailed);
    }

    private async Task<bool> ApplyLoginSuccessAsync(LoginResult login)
    {
        if (string.IsNullOrWhiteSpace(login.SessionToken))
        {
            logger.LogWarning("Generals Online login response had no session token.");
            await ClearStoredTokenAsync();
            SetState(GeneralsOnlineAuthState.Unauthenticated, null, null, null, string.Empty);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(login.RefreshToken))
        {
            try
            {
                await tokenStorage.SaveTokenAsync(SecureStringHelper.ToSecureString(login.RefreshToken));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or CryptographicException)
            {
                logger.LogWarning(ex, "Failed to persist the Generals Online refresh token.");
                SetState(GeneralsOnlineAuthState.Unauthenticated, null, null, null, string.Empty);
                return false;
            }
        }

        long? userId = login.UserId > 0 ? login.UserId : null;
        SetState(GeneralsOnlineAuthState.Authenticated, login.SessionToken, login.DisplayName, userId, login.WebSocketUri);
        logger.LogInformation("Signed in to Generals Online as {DisplayName}.", login.DisplayName);
        return true;
    }

    private async Task ClearStoredTokenAsync()
    {
        try
        {
            await tokenStorage.DeleteTokenAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to clear the stored Generals Online refresh token.");
        }
    }

    private void SetState(
        GeneralsOnlineAuthState state,
        string? sessionToken,
        string? displayName,
        long? userId,
        string webSocketUri)
    {
        lock (_syncLock)
        {
            _authState = state;
            _sessionToken = sessionToken;
            _displayName = displayName;
            _userId = userId;
            _webSocketUri = webSocketUri;
            _pendingStateEvents.Enqueue(state);
        }
    }

    private void RaisePendingStateEvents()
    {
        // Runs after the state lock is released: SemaphoreSlim is not
        // reentrant, so invoking subscribers under the lock would deadlock
        // any handler that calls back into the auth service.
        while (true)
        {
            GeneralsOnlineAuthState? next;
            lock (_syncLock)
            {
                next = _pendingStateEvents.Count > 0 ? _pendingStateEvents.Dequeue() : null;
            }

            if (next is null)
            {
                return;
            }

            AuthStateChanged?.Invoke(this, next.Value);
        }
    }
}
