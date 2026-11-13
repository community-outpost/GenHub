using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GeneralsOnline.Services;

/// <summary>
/// Typed HTTP client for the official Generals Online backend REST API.
/// </summary>
/// <param name="httpClientFactory">The HTTP client factory.</param>
/// <param name="logger">The logger instance.</param>
public sealed class GeneralsOnlineApiClient(
    IHttpClientFactory httpClientFactory,
    ILogger<GeneralsOnlineApiClient> logger) : IGeneralsOnlineApiClient
{
    private const string BearerScheme = "Bearer";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private Func<Task<string?>>? _tokenProvider;

    /// <inheritdoc />
    public void SetTokenProvider(Func<Task<string?>> tokenProvider)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetLoginCodeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await GetApiClient().GetAsync(BuildUrl(GeneralsOnlineConstants.LoginCodeEndpoint), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Generals Online login code request failed with {Status}.", response.StatusCode);
                return OperationResult<string>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
            }

            var payload = await response.Content.ReadFromJsonAsync<GeneralsOnlineLoginCodeResult>(JsonOptions, cancellationToken);
            if (payload is null || !payload.Success || string.IsNullOrWhiteSpace(payload.LoginCode))
            {
                logger.LogWarning("Generals Online login code response was empty.");
                return OperationResult<string>.CreateFailure(GeneralsOnlineConstants.ErrorLoginFailed);
            }

            return OperationResult<string>.CreateSuccess(payload.LoginCode.Trim());
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Generals Online backend unreachable while issuing a login code.");
            return OperationResult<string>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Generals Online login code request timed out.");
            return OperationResult<string>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Generals Online login code response was malformed.");
            return OperationResult<string>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<LoginResult>> CheckLoginAsync(string gameCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameCode))
        {
            return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorLoginFailed);
        }

        try
        {
            using var response = await GetApiClient().PostAsJsonAsync(
                BuildUrl(GeneralsOnlineConstants.CheckLoginEndpoint),
                new { client_id = GeneralsOnlineConstants.ClientId, code = gameCode.Trim() },
                JsonOptions,
                cancellationToken);
            return await ReadLoginResultAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Generals Online backend unreachable while checking login.");
            return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Generals Online login check timed out.");
            return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Generals Online login check response was malformed.");
            return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<LoginResult>> LoginWithTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(GeneralsOnlineConstants.LoginWithTokenEndpoint));
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, refreshToken.Trim());
            request.Content = JsonContent.Create(
                new { client_id = GeneralsOnlineConstants.ClientId },
                options: JsonOptions);
            using var response = await GetApiClient().SendAsync(request, cancellationToken);
            return await ReadLoginResultAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Generals Online backend unreachable during token refresh.");
            return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Generals Online token refresh timed out.");
            return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Generals Online token refresh response was malformed.");
            return OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<GeneralsOnlineLobbiesResult>> GetLobbiesAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetSessionTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(GeneralsOnlineConstants.LobbiesEndpoint));
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);
            using var response = await GetApiClient().SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("Generals Online session expired while listing lobbies.");
                return OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Generals Online lobby list failed with {Status}.", response.StatusCode);
                await LogFailureBodyAsync(response, "lobby list", cancellationToken);
                if ((response.StatusCode == HttpStatusCode.InternalServerError
                    || response.StatusCode == HttpStatusCode.Forbidden)
                    && await IsEmptyLobbiesBodyAsync(response, cancellationToken))
                {
                    return OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorLobbiesForbidden);
                }

                return OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorLobbiesUnavailable);
            }

            var payload = await response.Content.ReadFromJsonAsync<GeneralsOnlineLobbiesResult>(JsonOptions, cancellationToken);
            if (payload?.Lobbies is null)
            {
                logger.LogWarning("Generals Online lobby list response had no lobbies.");
                return OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorLobbiesUnavailable);
            }

            return OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(payload);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Generals Online backend unreachable while listing lobbies.");
            return OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Generals Online lobby list timed out.");
            return OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Generals Online lobby list response was malformed.");
            return OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<GeneralsOnlineLobby?>> GetLobbyDetailsAsync(long lobbyId, CancellationToken cancellationToken = default)
    {
        var token = await GetSessionTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return OperationResult<GeneralsOnlineLobby?>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired);
        }

        try
        {
            var url = BuildUrl(string.Format(GeneralsOnlineConstants.LobbyByIdFormat, lobbyId));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);
            using var response = await GetApiClient().SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("Generals Online session expired while reading lobby {LobbyId}.", lobbyId);
                return OperationResult<GeneralsOnlineLobby?>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return OperationResult<GeneralsOnlineLobby?>.CreateSuccess(null);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Generals Online lobby {LobbyId} failed with {Status}.", lobbyId, response.StatusCode);
                await LogFailureBodyAsync(response, "lobby read", cancellationToken);
                return OperationResult<GeneralsOnlineLobby?>.CreateFailure(GeneralsOnlineConstants.ErrorLobbiesUnavailable);
            }

            var payload = await response.Content.ReadFromJsonAsync<GeneralsOnlineLobbyResult>(JsonOptions, cancellationToken);
            return OperationResult<GeneralsOnlineLobby?>.CreateSuccess(payload?.Lobby);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Generals Online backend unreachable while reading lobby {LobbyId}.", lobbyId);
            return OperationResult<GeneralsOnlineLobby?>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Generals Online lobby read timed out.");
            return OperationResult<GeneralsOnlineLobby?>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Generals Online lobby response was malformed.");
            return OperationResult<GeneralsOnlineLobby?>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<GeneralsOnlineRoom>>> GetRoomsAsync(CancellationToken cancellationToken = default)
    {
        var result = await GetJsonAsync<GeneralsOnlineRoomsResult>(
            GeneralsOnlineConstants.RoomsEndpoint, "network rooms", authenticated: true, cancellationToken);
        if (!result.Success || result.Data?.Rooms is null)
        {
            return OperationResult<IReadOnlyList<GeneralsOnlineRoom>>.CreateFailure(
                result.Errors.FirstOrDefault() ?? GeneralsOnlineConstants.ErrorServiceUnavailable);
        }

        return OperationResult<IReadOnlyList<GeneralsOnlineRoom>>.CreateSuccess(result.Data.Rooms);
    }

    /// <inheritdoc />
    public async Task<OperationResult<GeneralsOnlinePublicCounts>> GetPublicCountsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await GetApiClient().GetAsync(BuildUrl(GeneralsOnlineConstants.MonitoringBasicStatsEndpoint), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Generals Online basic stats failed with {Status}.", response.StatusCode);
                await LogFailureBodyAsync(response, "basic stats", cancellationToken);
                return OperationResult<GeneralsOnlinePublicCounts>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
            }

            var payload = await response.Content.ReadFromJsonAsync<GeneralsOnlinePublicCounts>(JsonOptions, cancellationToken);
            if (payload is null)
            {
                logger.LogWarning("Generals Online basic stats response was empty.");
                return OperationResult<GeneralsOnlinePublicCounts>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
            }

            return OperationResult<GeneralsOnlinePublicCounts>.CreateSuccess(payload);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Generals Online basic stats unreachable.");
            return OperationResult<GeneralsOnlinePublicCounts>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Generals Online basic stats timed out.");
            return OperationResult<GeneralsOnlinePublicCounts>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Generals Online basic stats response was malformed.");
            return OperationResult<GeneralsOnlinePublicCounts>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (NotSupportedException ex)
        {
            logger.LogWarning(ex, "Generals Online basic stats response was malformed.");
            return OperationResult<GeneralsOnlinePublicCounts>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<GeneralsOnlineServiceUptime>> GetServiceUptimeAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<GeneralsOnlineServiceUptime>(
            GeneralsOnlineConstants.MonitoringUptimeEndpoint, "service uptime", authenticated: false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<GeneralsOnlineDailyStats>> GetGlobalStatsAsync(CancellationToken cancellationToken = default)
    {
        var result = await GetJsonAsync<GeneralsOnlineGlobalStatsResult>(
            GeneralsOnlineConstants.GlobalStatsEndpoint, "global stats", authenticated: true, cancellationToken);
        if (!result.Success || result.Data?.GlobalStats is null)
        {
            return OperationResult<GeneralsOnlineDailyStats>.CreateFailure(
                result.Errors.FirstOrDefault() ?? GeneralsOnlineConstants.ErrorServiceUnavailable);
        }

        return OperationResult<GeneralsOnlineDailyStats>.CreateSuccess(result.Data.GlobalStats);
    }

    /// <inheritdoc />
    public async Task<OperationResult<GeneralsOnlinePlayerStats>> GetPlayerStatsAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            return OperationResult<GeneralsOnlinePlayerStats>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }

        var endpoint = string.Format(GeneralsOnlineConstants.PlayerStatsByIdFormat, userId);
        var result = await GetJsonAsync<GeneralsOnlinePlayerStatsResult>(
            endpoint, "player stats", authenticated: true, cancellationToken);
        if (!result.Success || result.Data?.Stats is null)
        {
            return OperationResult<GeneralsOnlinePlayerStats>.CreateFailure(
                result.Errors.FirstOrDefault() ?? GeneralsOnlineConstants.ErrorServiceUnavailable);
        }

        return OperationResult<GeneralsOnlinePlayerStats>.CreateSuccess(result.Data.Stats);
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetMotdAsync(CancellationToken cancellationToken = default)
    {
        var result = await GetJsonAsync<GeneralsOnlineMotdResult>(
            GeneralsOnlineConstants.MotdEndpoint, "message of the day", authenticated: true, cancellationToken);
        if (!result.Success || result.Data?.Motd is null)
        {
            return OperationResult<string>.CreateFailure(
                result.Errors.FirstOrDefault() ?? GeneralsOnlineConstants.ErrorServiceUnavailable);
        }

        return OperationResult<string>.CreateSuccess(result.Data.Motd.Trim());
    }

    /// <inheritdoc />
    public Task<OperationResult<GeneralsOnlineFriendsResult>> GetFriendsAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<GeneralsOnlineFriendsResult>(
            GeneralsOnlineConstants.SocialFriendsEndpoint, "friends list", authenticated: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<GeneralsOnlineBlockedResult>> GetBlockedAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<GeneralsOnlineBlockedResult>(
            GeneralsOnlineConstants.SocialBlockedEndpoint, "blocked list", authenticated: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<GeneralsOnlineActiveUsersResult>> GetActiveUsersAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<GeneralsOnlineActiveUsersResult>(
            GeneralsOnlineConstants.UsersActiveEndpoint, "active users", authenticated: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<GeneralsOnlineMe>> GetMeAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<GeneralsOnlineMe>(
            GeneralsOnlineConstants.UsersMeEndpoint, "current user", authenticated: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> SendFriendRequestAsync(long targetUserId, CancellationToken cancellationToken = default)
    {
        return SendEmptyAsync(
            HttpMethod.Put,
            string.Format(CultureInfo.InvariantCulture, GeneralsOnlineConstants.SocialFriendRequestFormat, targetUserId),
            "friend request send",
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> AcceptFriendRequestAsync(long targetUserId, CancellationToken cancellationToken = default)
    {
        return SendEmptyAsync(
            HttpMethod.Post,
            string.Format(CultureInfo.InvariantCulture, GeneralsOnlineConstants.SocialFriendRequestFormat, targetUserId),
            "friend request accept",
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> RejectFriendRequestAsync(long targetUserId, CancellationToken cancellationToken = default)
    {
        return SendEmptyAsync(
            HttpMethod.Delete,
            string.Format(CultureInfo.InvariantCulture, GeneralsOnlineConstants.SocialFriendRequestFormat, targetUserId),
            "friend request reject",
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> RemoveFriendAsync(long targetUserId, CancellationToken cancellationToken = default)
    {
        return SendEmptyAsync(
            HttpMethod.Delete,
            string.Format(CultureInfo.InvariantCulture, GeneralsOnlineConstants.SocialFriendFormat, targetUserId),
            "friend remove",
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> BlockUserAsync(long targetUserId, CancellationToken cancellationToken = default)
    {
        return SendEmptyAsync(
            HttpMethod.Put,
            string.Format(CultureInfo.InvariantCulture, GeneralsOnlineConstants.SocialBlockedUserFormat, targetUserId),
            "user block",
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> UnblockUserAsync(long targetUserId, CancellationToken cancellationToken = default)
    {
        return SendEmptyAsync(
            HttpMethod.Delete,
            string.Format(CultureInfo.InvariantCulture, GeneralsOnlineConstants.SocialBlockedUserFormat, targetUserId),
            "user unblock",
            cancellationToken);
    }

    private static string BuildUrl(string endpoint)
    {
        var baseUrl = Environment.GetEnvironmentVariable(GeneralsOnlineConstants.RestApiBaseUrlEnvVar);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = GeneralsOnlineConstants.DefaultRestApiBaseUrl;
        }

        return baseUrl.TrimEnd('/') + endpoint;
    }

    private static async Task<OperationResult<LoginResult>> ReadLoginResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        LoginResult? payload = null;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<LoginResult>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            payload = null;
        }
        catch (NotSupportedException)
        {
            payload = null;
        }

        if (response.IsSuccessStatusCode && payload is not null)
        {
            return OperationResult<LoginResult>.CreateSuccess(payload);
        }

        var error = response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Locked
            ? GeneralsOnlineConstants.ErrorLoginFailed
            : GeneralsOnlineConstants.ErrorServiceUnavailable;
        return OperationResult<LoginResult>.CreateFailure(error);
    }

    private static async Task<bool> IsEmptyLobbiesBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            // Probe the raw JSON: the backend denies launcher sessions with a
            // 500 (or a normalized 403) carrying an empty lobbies result
            // (null or an empty array). Error middleware objects without a
            // lobbies property are not denials and stay generic failures.
            // The key matches case-insensitively, like the payload deserializer.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals("lobbies", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return property.Value.ValueKind == JsonValueKind.Null
                    || (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() == 0);
            }

            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<OperationResult<TPayload>> GetJsonAsync<TPayload>(
        string endpoint,
        string operation,
        bool authenticated,
        CancellationToken cancellationToken)
        where TPayload : class
    {
        string? token = null;
        if (authenticated)
        {
            token = await GetSessionTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                return OperationResult<TPayload>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired);
            }
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(endpoint));
            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);
            }

            using var response = await GetApiClient().SendAsync(request, cancellationToken);
            if (authenticated && response.StatusCode == HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("Generals Online session expired while reading {Operation}.", operation);
                return OperationResult<TPayload>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Generals Online {Operation} failed with {Status}.", operation, response.StatusCode);
                await LogFailureBodyAsync(response, operation, cancellationToken);
                return OperationResult<TPayload>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
            }

            var payload = await response.Content.ReadFromJsonAsync<TPayload>(JsonOptions, cancellationToken);
            if (payload is null)
            {
                logger.LogWarning("Generals Online {Operation} response was empty.", operation);
                return OperationResult<TPayload>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
            }

            return OperationResult<TPayload>.CreateSuccess(payload);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Generals Online backend unreachable while reading {Operation}.", operation);
            return OperationResult<TPayload>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Generals Online {Operation} read timed out.", operation);
            return OperationResult<TPayload>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Generals Online {Operation} response was malformed.", operation);
            return OperationResult<TPayload>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
    }

    private async Task<OperationResult<bool>> SendEmptyAsync(
        HttpMethod method,
        string endpoint,
        string operation,
        CancellationToken cancellationToken)
    {
        var token = await GetSessionTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return OperationResult<bool>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired);
        }

        try
        {
            using var request = new HttpRequestMessage(method, BuildUrl(endpoint));
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);
            using var response = await GetApiClient().SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("Generals Online session expired while running {Operation}.", operation);
                return OperationResult<bool>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Generals Online {Operation} failed with {Status}.", operation, response.StatusCode);
                await LogFailureBodyAsync(response, operation, cancellationToken);
                return OperationResult<bool>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Generals Online backend unreachable while running {Operation}.", operation);
            return OperationResult<bool>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Generals Online {Operation} timed out.", operation);
            return OperationResult<bool>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable);
        }
    }

    private async Task<string?> GetSessionTokenAsync()
    {
        if (_tokenProvider is null)
        {
            return null;
        }

        try
        {
            return await _tokenProvider();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            logger.LogWarning(ex, "Generals Online session token provider failed.");
            return null;
        }
    }

    private HttpClient GetApiClient()
    {
        var client = httpClientFactory.CreateClient(nameof(GeneralsOnlineApiClient));
        client.Timeout = TimeSpan.FromSeconds(GeneralsOnlineConstants.HttpTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ApiConstants.DefaultUserAgent);
        return client;
    }

    private async Task LogFailureBodyAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            var preview = body.Length > GeneralsOnlineConstants.ErrorBodyPreviewLength
                ? body[..GeneralsOnlineConstants.ErrorBodyPreviewLength]
                : body;
            logger.LogWarning("Generals Online {Operation} error body: {Body}.", operation, OnlineLogScrubber.Scrub(preview));
        }
        catch (HttpRequestException)
        {
            // The status line already names the failure.
        }
        catch (OperationCanceledException)
        {
            // The status line already names the failure.
        }
    }
}
