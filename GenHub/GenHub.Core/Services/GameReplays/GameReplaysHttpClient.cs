using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameReplays;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.GameReplays;

/// <summary>
/// HTTP client implementation for GameReplays API and web scraping.
/// Handles cookie management and HTTP requests.
/// </summary>
public class GameReplaysHttpClient(
    HttpClient httpClient,
    ILogger<GameReplaysHttpClient> logger) : IGameReplaysHttpClient
{
    private const string CookieHeader = "Cookie";

    private string? _authCookie;

    /// <inheritdoc/>
    public async Task<OperationResult<string>> GetHtmlAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Fetching HTML from URL: {Url}", url);

            var response = await httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            logger.LogDebug("Successfully fetched HTML from URL: {Url}", url);

            return OperationResult<string>.CreateSuccess(html);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error fetching HTML from URL: {Url}", url);
            return OperationResult<string>.CreateFailure($"HTTP error: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Request timeout fetching HTML from URL: {Url}", url);
            return OperationResult<string>.CreateFailure("Request timeout");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching HTML from URL: {Url}", url);
            return OperationResult<string>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> PostFormAsync(
        string url,
        Dictionary<string, string> formData,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Posting form data to URL: {Url}", url);

            var content = new FormUrlEncodedContent(formData);
            var response = await httpClient.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            logger.LogDebug("Successfully posted form data to URL: {Url}", url);

            return OperationResult<string>.CreateSuccess(responseContent);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error posting form data to URL: {Url}", url);
            return OperationResult<string>.CreateFailure($"HTTP error: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Request timeout posting form data to URL: {Url}", url);
            return OperationResult<string>.CreateFailure("Request timeout");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error posting form data to URL: {Url}", url);
            return OperationResult<string>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<T>> GetJsonAsync<T>(
        string url,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Fetching JSON from URL: {Url}", url);

            var response = await httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (result == null)
            {
                return OperationResult<T>.CreateFailure("Failed to deserialize JSON response");
            }

            logger.LogDebug("Successfully fetched JSON from URL: {Url}", url);

            return OperationResult<T>.CreateSuccess(result);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error fetching JSON from URL: {Url}", url);
            return OperationResult<T>.CreateFailure($"HTTP error: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Request timeout fetching JSON from URL: {Url}", url);
            return OperationResult<T>.CreateFailure("Request timeout");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "JSON deserialization error from URL: {Url}", url);
            return OperationResult<T>.CreateFailure($"JSON error: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching JSON from URL: {Url}", url);
            return OperationResult<T>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<TResponse>> PostJsonAsync<TRequest, TResponse>(
        string url,
        TRequest data,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Posting JSON to URL: {Url}", url);

            var json = JsonSerializer.Serialize(data);
            var content = new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);

            var response = await httpClient.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<TResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (result == null)
            {
                return OperationResult<TResponse>.CreateFailure("Failed to deserialize JSON response");
            }

            logger.LogDebug("Successfully posted JSON to URL: {Url}", url);

            return OperationResult<TResponse>.CreateSuccess(result);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error posting JSON to URL: {Url}", url);
            return OperationResult<TResponse>.CreateFailure($"HTTP error: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Request timeout posting JSON to URL: {Url}", url);
            return OperationResult<TResponse>.CreateFailure("Request timeout");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "JSON serialization/deserialization error for URL: {Url}", url);
            return OperationResult<TResponse>.CreateFailure($"JSON error: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error posting JSON to URL: {Url}", url);
            return OperationResult<TResponse>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void SetAuthCookie(string cookieValue)
    {
        _authCookie = cookieValue;
        httpClient.DefaultRequestHeaders.Remove(CookieHeader);
        httpClient.DefaultRequestHeaders.Add(CookieHeader, cookieValue);
        logger.LogDebug("Set authentication cookie");
    }

    /// <inheritdoc/>
    public void ClearAuthCookie()
    {
        _authCookie = null;
        httpClient.DefaultRequestHeaders.Remove(CookieHeader);
        logger.LogDebug("Cleared authentication cookie");
    }

    /// <inheritdoc/>
    public string? GetAuthCookie()
    {
        return _authCookie;
    }

    private static bool Initialize(HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ApiConstants.DefaultUserAgent);
        client.Timeout = TimeSpan.FromSeconds(GameReplaysConstants.DefaultRequestTimeoutSeconds);
        return true;
    }
}
