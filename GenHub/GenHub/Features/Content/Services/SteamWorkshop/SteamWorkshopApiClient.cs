using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Client for the public Steam remote storage endpoint (ISteamRemoteStorage/GetPublishedFileDetails)
/// used by the Steam Workshop downloader to query manifest IDs and file details without requiring an API key.
/// </summary>
/// <param name="httpClientFactory">Factory for creating HTTP clients.</param>
/// <param name="logger">The logger.</param>
public class SteamWorkshopApiClient(
    IHttpClientFactory httpClientFactory,
    ILogger<SteamWorkshopApiClient> logger)
{
    private const int SteamResultOk = 1;
    private const int MaxErrorSnippetLength = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Fetches details for a single published file through the public Steam remote storage endpoint.
    /// Does not require an API key.
    /// </summary>
    /// <param name="publishedFileId">The published file ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The file details.</returns>
    public async Task<OperationResult<SteamWorkshopFileJson>> GetPublishedFileDetailsAsync(
        string publishedFileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedFileId);

        var parameters = new Dictionary<string, string>
        {
            [SteamWorkshopConstants.ItemCountParam] = "1",
            [string.Format(SteamWorkshopConstants.PublishedFileIdParamFormat, 0)] = publishedFileId,
        };

        using var content = new FormUrlEncodedContent(parameters);
        var result = await SendApiRequestAsync<SteamWorkshopQueryResponse>(
            (client, token) => client.PostAsync(SteamWorkshopConstants.GetPublishedFileDetailsEndpoint, content, token),
            "details lookup",
            cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            return OperationResult<SteamWorkshopFileJson>.CreateFailure(result.Errors);
        }

        var file = result.Data.Response?.PublishedFileDetails?.FirstOrDefault(item => item.Result == SteamResultOk);
        if (file == null)
        {
            return OperationResult<SteamWorkshopFileJson>.CreateFailure(
                $"Steam Workshop item {publishedFileId} was not found.");
        }

        return OperationResult<SteamWorkshopFileJson>.CreateSuccess(file);
    }

    private static async Task<string> ReadErrorSnippetAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return "request failed";
        }

        return HtmlTextHelper.CleanToSingleLine(body, MaxErrorSnippetLength);
    }

    private HttpClient CreateClient()
    {
        return httpClientFactory.CreateClient(SteamWorkshopConstants.PublisherType);
    }

    private async Task<OperationResult<TResponse>> SendApiRequestAsync<TResponse>(
        Func<HttpClient, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        string operationName,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        try
        {
            var client = CreateClient();
            using var response = await sendAsync(client, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var snippet = await ReadErrorSnippetAsync(response, cancellationToken).ConfigureAwait(false);
                logger.LogWarning(
                    "Steam Web API {Operation} failed with status {StatusCode}: {Snippet}",
                    operationName,
                    (int)response.StatusCode,
                    snippet);
                return OperationResult<TResponse>.CreateFailure(
                    $"Steam Web API {operationName} failed ({(int)response.StatusCode}): {snippet}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize<TResponse>(payload, JsonOptions);
            if (parsed == null)
            {
                return OperationResult<TResponse>.CreateFailure("Steam Web API returned an unexpected response.");
            }

            return OperationResult<TResponse>.CreateSuccess(parsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Steam Web API {Operation} failed", operationName);
            return OperationResult<TResponse>.CreateFailure($"Steam Web API {operationName} failed: {ex.Message}");
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Steam Web API {Operation} failed while reading the response", operationName);
            return OperationResult<TResponse>.CreateFailure($"Steam Web API {operationName} failed: {ex.Message}");
        }
    }
}
