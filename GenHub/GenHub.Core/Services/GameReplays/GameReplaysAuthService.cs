using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameReplays;
using GenHub.Core.Models.GameReplays;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.GameReplays;

/// <summary>
/// OAuth 2.0 authentication service for GameReplays.
/// Handles login flow, token management, and user session.
/// </summary>
public class GameReplaysAuthService(
    IGameReplaysHttpClient httpClient,
    ILogger<GameReplaysAuthService> logger) : IGameReplaysAuthService
{
    private readonly ConcurrentDictionary<string, OAuthState> _states = new();

    private OAuthTokenResponse? _currentToken;
    private OAuthUserInfo? _currentUser;

    /// <inheritdoc/>
    public OperationResult<string> GetAuthorizationUrl()
    {
        try
        {
            var state = GenerateState();
            var authUrl = $"{GameReplaysConstants.OAuthAuthorizationEndpoint}?" +
                $"client_id={Uri.EscapeDataString(GameReplaysConstants.OAuthClientId)}&" +
                $"redirect_uri={Uri.EscapeDataString(GameReplaysConstants.OAuthRedirectUri)}&" +
                $"response_type=code&" +
                $"scope={Uri.EscapeDataString(GameReplaysConstants.OAuthScope)}&" +
                $"state={Uri.EscapeDataString(state)}";

            logger.LogDebug("Generated authorization URL with state: {State}", state);

            return OperationResult<string>.CreateSuccess(authUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating authorization URL");
            return OperationResult<string>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<OAuthTokenResponse>> ExchangeCodeForTokenAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Exchanging authorization code for access token");

            var tokenRequest = new
            {
                client_id = GameReplaysConstants.OAuthClientId,
                client_secret = GameReplaysConstants.OAuthClientSecret,
                code = code,
                grant_type = "authorization_code",
                redirect_uri = GameReplaysConstants.OAuthRedirectUri,
            };

            var response = await httpClient.PostJsonAsync<object, OAuthTokenResponse>(
                GameReplaysConstants.OAuthTokenEndpoint,
                tokenRequest,
                cancellationToken);

            if (!response.Success)
            {
                return OperationResult<OAuthTokenResponse>.CreateFailure(response.FirstError ?? "Failed to exchange code for token");
            }

            await SaveTokenAsync(response.Data);

            logger.LogDebug("Successfully exchanged authorization code for access token");

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error exchanging authorization code for token");
            return OperationResult<OAuthTokenResponse>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<OAuthTokenResponse>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Refreshing access token");

            var refreshRequest = new
            {
                client_id = GameReplaysConstants.OAuthClientId,
                client_secret = GameReplaysConstants.OAuthClientSecret,
                refresh_token = refreshToken,
                grant_type = "refresh_token",
            };

            var response = await httpClient.PostJsonAsync<object, OAuthTokenResponse>(
                GameReplaysConstants.OAuthTokenEndpoint,
                refreshRequest,
                cancellationToken);

            if (!response.Success)
            {
                return OperationResult<OAuthTokenResponse>.CreateFailure(response.FirstError ?? "Failed to refresh token");
            }

            await SaveTokenAsync(response.Data);

            logger.LogDebug("Successfully refreshed access token");

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error refreshing token");
            return OperationResult<OAuthTokenResponse>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<OAuthUserInfo>> GetUserInfoAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Fetching user info");

            var userInfoUrl = $"{GameReplaysConstants.OAuthResourceEndpoint}?access_token={Uri.EscapeDataString(accessToken)}";
            var response = await httpClient.GetJsonAsync<OAuthUserInfo>(userInfoUrl, cancellationToken);

            if (!response.Success)
            {
                return OperationResult<OAuthUserInfo>.CreateFailure(response.FirstError ?? "Failed to fetch user info");
            }

            logger.LogDebug("Successfully fetched user info: {UserId}", response.Data?.Id);

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching user info");
            return OperationResult<OAuthUserInfo>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public bool ValidateState(string state)
    {
        if (string.IsNullOrEmpty(state))
        {
            return false;
        }

        if (!_states.TryGetValue(state, out var oauthState))
        {
            return false;
        }

        // Check if state is expired
        if (oauthState.IsExpired)
        {
            _states.TryRemove(state, out _);
            return false;
        }

        // Remove used state
        _states.TryRemove(state, out _);

        return true;
    }

    /// <inheritdoc/>
    public string GenerateState()
    {
        var stateBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(stateBytes);
        }

        var state = Convert.ToHexString(stateBytes).ToLowerInvariant();
        var timestamp = DateTime.UtcNow;

        var oauthState = new OAuthState
        {
            State = state,
            CreatedAt = timestamp,
        };

        _states[state] = oauthState;

        // Clean up expired states
        var expiredStates = _states
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var expiredState in expiredStates)
        {
            _states.TryRemove(expiredState, out _);
        }

        logger.LogDebug("Generated OAuth state: {State}", state);

        return state;
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> SaveTokenAsync(OAuthTokenResponse tokenResponse)
    {
        try
        {
            _currentToken = tokenResponse;

            // Set auth cookie for HTTP requests
            var cookieValue = $"session={tokenResponse.AccessToken}";
            httpClient.SetAuthCookie(cookieValue);

            logger.LogDebug("Saved OAuth token");

            await Task.CompletedTask;
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving token");
            return OperationResult<bool>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<OAuthTokenResponse?>> LoadTokenAsync()
    {
        try
        {
            await Task.CompletedTask;

            return OperationResult<OAuthTokenResponse?>.CreateSuccess(_currentToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading token");
            return OperationResult<OAuthTokenResponse?>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> ClearTokenAsync()
    {
        try
        {
            _currentToken = null;
            _currentUser = null;
            httpClient.ClearAuthCookie();

            logger.LogDebug("Cleared OAuth token");

            await Task.CompletedTask;
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error clearing token");
            return OperationResult<bool>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public bool IsTokenExpired()
    {
        if (_currentToken == null)
        {
            return true;
        }

        return _currentToken.ExpiresAt <= DateTime.UtcNow.AddMinutes(5); // 5 minute buffer
    }

    /// <inheritdoc/>
    public string? GetAccessToken()
    {
        return _currentToken?.AccessToken;
    }

    /// <inheritdoc/>
    public string? GetRefreshToken()
    {
        return _currentToken?.RefreshToken;
    }

    /// <inheritdoc/>
    public OAuthUserInfo? GetCurrentUser()
    {
        return _currentUser;
    }

    /// <summary>
    /// Sets the current user info.
    /// </summary>
    /// <param name="userInfo">The user info to set.</param>
    public void SetCurrentUser(OAuthUserInfo userInfo)
    {
        _currentUser = userInfo;
        logger.LogDebug("Set current user: {UserId}", userInfo.Id);
    }
}
