using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GeneralsOnline.Services;

/// <summary>
/// Client for interacting with the Generals Online API with strongly-typed responses.
/// Extensibility: Add new methods here when new API endpoints become available. Follow the pattern of
/// providing both typed methods (e.g., GetServiceStatsAsync) and raw JSON methods (e.g., GetServiceStatsJsonAsync).
/// </summary>
// TODO: Implement HTTP resilience patterns using Microsoft.Extensions.Http.Resilience to add retry policies,
// circuit breaker, and timeout handling for transient failures. This will improve reliability when the
// Generals Online service experiences temporary issues. Configure in DI registration with:
// services.AddHttpClient<IGeneralsOnlineApiClient, GeneralsOnlineApiClient>()
//     .AddStandardResilienceHandler(options => { /* configure retry, circuit breaker, etc. */ });
public class GeneralsOnlineApiClient : IGeneralsOnlineApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeneralsOnlineApiClient> _logger;
    private Func<Task<string?>>? _tokenProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="logger">The logger instance.</param>
    public GeneralsOnlineApiClient(
        HttpClient httpClient,
        ILogger<GeneralsOnlineApiClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void SetTokenProvider(Func<Task<string?>> tokenProvider)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    // ===== Service & Stats =====

    /// <inheritdoc />
    public async Task<ServiceStats?> GetServiceStatsAsync(CancellationToken cancellationToken = default)
    {
        var result = await GetServiceStatsJsonAsync(cancellationToken);
        return result.Success ? result.Data.DeserializeOrDefault<ServiceStats>() : null;
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetServiceStatsJsonAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync(GeneralsOnlineConstants.ServiceStatsEndpoint, cancellationToken);
    }

    // ===== Authentication =====

    /// <inheritdoc />
    public Task<bool> VerifyTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        // When real API is available, call the VerifyTokenEndpoint
        // var result = await GetAsync($"{GeneralsOnlineConstants.VerifyTokenEndpoint}?token={token}", cancellationToken);
        // return result.DeserializeOrDefault<TokenVerification>()?.IsValid ?? false;
        return Task.FromResult(!string.IsNullOrWhiteSpace(token));
    }

    // ===== Matches =====

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetActiveMatchesAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync(GeneralsOnlineConstants.MatchesEndpoint, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetMatchDetailsAsync(int matchId, CancellationToken cancellationToken = default)
    {
        return await GetAsync($"{GeneralsOnlineConstants.ViewMatchEndpoint}?match={matchId}", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetMatchHistoryAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync(GeneralsOnlineConstants.MatchHistoryEndpoint, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetPlayerMatchHistoryAsync(string playerName, CancellationToken cancellationToken = default)
    {
        return await GetAsync($"{GeneralsOnlineConstants.MatchHistoryEndpoint}?player={playerName}", cancellationToken);
    }

    // ===== Lobbies =====

    /// <inheritdoc />
    public async Task<List<LobbyInfo>> GetLobbiesAsync(CancellationToken cancellationToken = default)
    {
        var result = await GetLobbiesJsonAsync(cancellationToken);
        return result.Success ? result.Data.DeserializeList<LobbyInfo>() : new List<LobbyInfo>();
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetLobbiesJsonAsync(CancellationToken cancellationToken = default)
    {
        return await GetAuthenticatedAsync(GeneralsOnlineConstants.LobbiesEndpoint, cancellationToken);
    }

    // ===== Players =====

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetActivePlayersAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync(GeneralsOnlineConstants.PlayersEndpoint, cancellationToken);
    }

    // ===== Leaderboards =====

    /// <inheritdoc />
    public async Task<List<LeaderboardEntry>> GetLeaderboardAsync(string period = "daily", CancellationToken cancellationToken = default)
    {
        var result = await GetLeaderboardJsonAsync(period, cancellationToken);
        return result.Success ? result.Data.DeserializeList<LeaderboardEntry>() : new List<LeaderboardEntry>();
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetLeaderboardJsonAsync(string period = "daily", CancellationToken cancellationToken = default)
    {
        return await GetAsync($"{GeneralsOnlineConstants.LeaderboardsEndpoint}?type={period}", cancellationToken);
    }

    // ===== Authentication - Gamecode Flow =====

    /// <inheritdoc />
    public async Task<LoginResult?> CheckLoginAsync(string gameCode, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Checking login status for gamecode: {GameCode}", gameCode);

            var requestBody = new
            {
                code = gameCode,
                client_id = GeneralsOnlineConstants.ClientId,
            };

            var result = await PostAsync<LoginResult>(
                GeneralsOnlineConstants.CheckLoginEndpoint,
                requestBody,
                token: null,
                cancellationToken);

            if (result == null)
            {
                _logger.LogDebug("CheckLogin returned null for gamecode: {GameCode}", gameCode);
                return null;
            }

            _logger.LogDebug("CheckLogin result for gamecode {GameCode}: {State}", gameCode, result.Result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking login for gamecode {GameCode}", gameCode);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<LoginResult?> LoginWithTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            _logger.LogWarning("LoginWithToken called with empty refresh token");
            return null;
        }

        try
        {
            _logger.LogDebug("Validating refresh token via LoginWithToken");

            var requestBody = new
            {
                client_id = GeneralsOnlineConstants.ClientId,
            };

            var result = await PostAsync<LoginResult>(
                GeneralsOnlineConstants.LoginWithTokenEndpoint,
                requestBody,
                token: refreshToken,
                cancellationToken);

            if (result == null)
            {
                _logger.LogWarning("LoginWithToken returned null");
                return null;
            }

            if (result.IsSuccess)
            {
                _logger.LogInformation("LoginWithToken successful for user: {DisplayName}", result.DisplayName);
            }
            else
            {
                _logger.LogWarning("LoginWithToken failed with state: {State}", result.Result);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating refresh token");
            return null;
        }
    }

    // ===== Private Helper Methods =====
    // These methods handle the HTTP communication and error handling uniformly.
    // To add a new endpoint: just call GetAsync or GetAuthenticatedAsync with the URL.
    private async Task<OperationResult<string>> GetAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Fetching data from {Url}", url);
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = $"API request failed with status {response.StatusCode} for {url}";
                _logger.LogWarning(error);
                return OperationResult<string>.CreateFailure(error);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Successfully retrieved {Length} characters from {Url}", content.Length, url);
            return OperationResult<string>.CreateSuccess(content);
        }
        catch (HttpRequestException ex)
        {
            var error = $"HTTP error calling Generals Online API: {url}";
            _logger.LogError(ex, error);
            return OperationResult<string>.CreateFailure(error);
        }
        catch (Exception ex)
        {
            var error = $"Unexpected error calling Generals Online API: {url}";
            _logger.LogError(ex, error);
            return OperationResult<string>.CreateFailure(error);
        }
    }

    private async Task<OperationResult<string>> GetAuthenticatedAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            if (_tokenProvider == null)
            {
                var error = $"Token provider not configured for authenticated request to {url}";
                _logger.LogWarning(error);
                return OperationResult<string>.CreateFailure(error);
            }

            var token = await _tokenProvider().ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
            {
                var error = $"No authentication token available for {url}";
                _logger.LogWarning(error);
                return OperationResult<string>.CreateFailure(error);
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add(GeneralsOnlineConstants.AuthTokenHeader, token);

            _logger.LogDebug("Fetching authenticated data from {Url}", url);
            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = $"Authenticated API request failed with status {response.StatusCode} for {url}";
                _logger.LogWarning(error);
                return OperationResult<string>.CreateFailure(error);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Successfully retrieved {Length} characters from {Url}", content.Length, url);
            return OperationResult<string>.CreateSuccess(content);
        }
        catch (HttpRequestException ex)
        {
            var error = $"HTTP error calling authenticated Generals Online API: {url}";
            _logger.LogError(ex, error);
            return OperationResult<string>.CreateFailure(error);
        }
        catch (Exception ex)
        {
            var error = $"Unexpected error calling authenticated Generals Online API: {url}";
            _logger.LogError(ex, error);
            return OperationResult<string>.CreateFailure(error);
        }
    }

    private async Task<T?> PostAsync<T>(string url, object requestBody, string? token, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            _logger.LogDebug("Posting to {Url}", url);

            var jsonBody = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;

            // Set headers as per the example
            request.Headers.UserAgent.ParseAdd("GenHub");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("POST request failed with status {Status} for {Url}", response.StatusCode, url);
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return JsonSerializer.Deserialize<T>(responseBody);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize response from {Url}", url);
                return null;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error posting to {Url}", url);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error posting to {Url}", url);
            return null;
        }
    }
}
