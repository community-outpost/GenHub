using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.Publishers;

/// <summary>
/// Service for fetching and processing publisher definitions.
/// </summary>
public class PublisherDefinitionService(
    IHttpClientFactory httpClientFactory,
    IPublisherCatalogParser catalogParser,
    ILogger<PublisherDefinitionService> logger) : IPublisherDefinitionService
{
    // Intentionally separate from PublisherJsonOptions.Definition: catalog payloads carry
    // string enums (contentType/targetGame) that require the enum converter below.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) },
    };

    /// <inheritdoc />
    public async Task<OperationResult<PublisherDefinition>> FetchDefinitionAsync(
        string definitionUrl,
        CancellationToken ct = default)
    {
        try
        {
            var normalizedUrl = CloudUrlHelper.NormalizeDirectDownloadUrl(definitionUrl);
            if (string.IsNullOrWhiteSpace(normalizedUrl) ||
                !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
            {
                return OperationResult<PublisherDefinition>.CreateFailure("Invalid definition URL");
            }

            var (definitionUrlSafe, definitionUrlFailure) = await NetworkSecurityHelper.IsSafeUrlAsync(normalizedUrl, ct);
            if (!definitionUrlSafe)
            {
                logger.LogWarning("Blocked unsafe definition URL {Url}: {Reason}", definitionUrl, definitionUrlFailure);
                return OperationResult<PublisherDefinition>.CreateFailure(definitionUrlFailure ?? "Definition URL is not allowed.");
            }

            using var client = httpClientFactory.CreateClient(CatalogConstants.CatalogHttpClientName);
            var fetchResult = await GetWithRedirectsAsync(client, uri, "Definition", ct);
            if (!fetchResult.Success || fetchResult.Data == null)
            {
                logger.LogWarning("Failed to fetch definition from {Url}: {Error}", definitionUrl, fetchResult.FirstError);
                return OperationResult<PublisherDefinition>.CreateFailure(fetchResult.Errors);
            }

            using var response = fetchResult.Data;
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to fetch definition from {Url}: {StatusCode}", definitionUrl, response.StatusCode);
                return OperationResult<PublisherDefinition>.CreateFailure(
                    $"Failed to fetch definition: {response.StatusCode}");
            }

            var streamResult = await ReadBoundedStreamAsync(response, CatalogConstants.MaxCatalogSizeBytes, "Definition", ct);
            if (!streamResult.Success || streamResult.Data == null)
            {
                return OperationResult<PublisherDefinition>.CreateFailure(streamResult.Errors);
            }

            using var memoryStream = streamResult.Data;
            var definition = await JsonSerializer.DeserializeAsync<PublisherDefinition>(memoryStream, JsonOptions, ct);

            if (definition == null)
            {
                return OperationResult<PublisherDefinition>.CreateFailure("Failed to deserialize publisher definition");
            }

            if (definition.SchemaVersion != CatalogConstants.DefinitionSchemaVersion)
            {
                logger.LogWarning(
                    "Unsupported publisher definition schema version {Version} from {Url}",
                    definition.SchemaVersion,
                    definitionUrl);
                return OperationResult<PublisherDefinition>.CreateFailure(
                    $"Unsupported publisher definition schema version {definition.SchemaVersion}; expected {CatalogConstants.DefinitionSchemaVersion}.");
            }

            // Ensure the definition URL is set correctly on the object if not specified by the publisher JSON
            if (string.IsNullOrEmpty(definition.DefinitionUrl))
            {
                definition.DefinitionUrl = normalizedUrl;
            }

            // V1 compatibility is owned by the PublisherDefinition.CatalogUrl/CatalogMirrors setters,
            // which materialize Catalogs[0] on deserialization, so no migration is needed here.
            return OperationResult<PublisherDefinition>.CreateSuccess(definition);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception fetching definition from {Url}", definitionUrl);
            return OperationResult<PublisherDefinition>.CreateFailure($"Exception fetching definition: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<PublisherCatalog>> FetchCatalogFromDefinitionAsync(
        PublisherDefinition definition,
        CancellationToken ct = default)
    {
        try
        {
            var catalogUrl = definition.CatalogUrl;
            if (string.IsNullOrWhiteSpace(catalogUrl) && definition.Catalogs.Count > 0)
            {
                catalogUrl = definition.Catalogs[0].Url;
            }

            if (string.IsNullOrWhiteSpace(catalogUrl))
            {
                return OperationResult<PublisherCatalog>.CreateFailure("Definition contains no catalog URL");
            }

            using var client = httpClientFactory.CreateClient(CatalogConstants.CatalogHttpClientName);
            var urlsToTry = new List<string> { catalogUrl };
            if (definition.CatalogMirrors != null)
            {
                var mirrors = definition.CatalogMirrors
                    .Where(mirror => !string.IsNullOrWhiteSpace(mirror))
                    .Take(CatalogConstants.MaxCatalogMirrorAttempts)
                    .ToList();
                if (definition.CatalogMirrors.Count > mirrors.Count)
                {
                    logger.LogDebug(
                        "Limiting catalog mirror attempts to {Max} of {Total} configured mirrors",
                        CatalogConstants.MaxCatalogMirrorAttempts,
                        definition.CatalogMirrors.Count);
                }

                urlsToTry.AddRange(mirrors);
            }

            foreach (var rawUrl in urlsToTry)
            {
                var catalog = await TryFetchAndParseCatalogUrlAsync(client, rawUrl, "Catalog", ct);
                if (catalog != null)
                {
                    return OperationResult<PublisherCatalog>.CreateSuccess(catalog);
                }
            }

            return OperationResult<PublisherCatalog>.CreateFailure("Failed to fetch catalog from all configured URLs and mirrors");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception fetching catalog for publisher {PublisherId}", definition.Publisher?.Id);
            return OperationResult<PublisherCatalog>.CreateFailure($"Exception fetching catalog: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> CheckForDefinitionUpdateAsync(
        PublisherSubscription subscription,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(subscription.DefinitionUrl))
            {
                return OperationResult<bool>.CreateSuccess(false);
            }

            var defResult = await FetchDefinitionAsync(subscription.DefinitionUrl, ct);
            if (!defResult.Success || defResult.Data == null)
            {
                return OperationResult<bool>.CreateFailure(defResult.Errors);
            }

            var remoteDef = defResult.Data;
            var hasUpdate = false;

            // Check if catalog URL changed
            if (!string.IsNullOrWhiteSpace(remoteDef.CatalogUrl) &&
                !string.Equals(subscription.CatalogUrl, remoteDef.CatalogUrl, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "Updating catalog URL for subscription {PublisherId} from {OldUrl} to {NewUrl}",
                    subscription.PublisherId,
                    subscription.CatalogUrl,
                    remoteDef.CatalogUrl);

                subscription.CatalogUrl = remoteDef.CatalogUrl;
                hasUpdate = true;
            }

            // Check if definition URL migrated
            if (remoteDef.PreviousDefinitionUrls != null &&
                remoteDef.PreviousDefinitionUrls.Contains(subscription.DefinitionUrl, StringComparer.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(remoteDef.DefinitionUrl) &&
                !string.Equals(subscription.DefinitionUrl, remoteDef.DefinitionUrl, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "Publisher {PublisherId} definition URL migrated from {OldUrl} to {NewUrl}",
                    subscription.PublisherId,
                    subscription.DefinitionUrl,
                    remoteDef.DefinitionUrl);

                subscription.DefinitionUrl = remoteDef.DefinitionUrl;
                hasUpdate = true;
            }

            return OperationResult<bool>.CreateSuccess(hasUpdate);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception checking for definition update");
            return OperationResult<bool>.CreateFailure($"Exception checking for update: {ex.Message}");
        }
    }

    private static async Task<OperationResult<MemoryStream>> ReadBoundedStreamAsync(
        HttpResponseMessage response,
        long maxSizeBytes,
        string resourceDescription,
        CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is { } headerLength &&
            headerLength > maxSizeBytes)
        {
            return OperationResult<MemoryStream>.CreateFailure(
                $"{resourceDescription} exceeds maximum size of {maxSizeBytes} bytes");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var memoryStream = new MemoryStream();
        var success = false;
        try
        {
            var buffer = new byte[HostingConstants.StreamCopyBufferSize];
            long totalRead = 0;
            var bytesRead = 0;

            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > maxSizeBytes)
                {
                    return OperationResult<MemoryStream>.CreateFailure(
                        $"{resourceDescription} exceeds maximum size of {maxSizeBytes} bytes");
                }

                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            }

            memoryStream.Position = 0;
            success = true;
            return OperationResult<MemoryStream>.CreateSuccess(memoryStream);
        }
        finally
        {
            if (!success)
            {
                await memoryStream.DisposeAsync();
            }
        }
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently or
            HttpStatusCode.Found or
            HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private async Task<OperationResult<HttpResponseMessage>> GetWithRedirectsAsync(
        HttpClient client,
        Uri initialUri,
        string resourceDescription,
        CancellationToken ct)
    {
        var currentUri = initialUri;
        HttpResponseMessage? response = null;
        try
        {
            for (var hop = 0; hop <= CatalogConstants.MaxCatalogRedirects; hop++)
            {
                response?.Dispose();
                using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!IsRedirectStatusCode(response.StatusCode))
                {
                    var finalResponse = response;
                    response = null;
                    return OperationResult<HttpResponseMessage>.CreateSuccess(finalResponse);
                }

                var location = response.Headers.Location;
                if (location == null)
                {
                    return OperationResult<HttpResponseMessage>.CreateFailure(
                        $"{resourceDescription} redirect response is missing a Location header.");
                }

                var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                var (hopSafe, hopFailure) = await NetworkSecurityHelper.IsSafeUrlAsync(nextUri.AbsoluteUri, ct);
                if (!hopSafe)
                {
                    logger.LogWarning(
                        "Blocked unsafe {Resource} redirect target {Target}: {Reason}",
                        resourceDescription,
                        nextUri.AbsoluteUri,
                        hopFailure);
                    return OperationResult<HttpResponseMessage>.CreateFailure(
                        hopFailure ?? $"{resourceDescription} was redirected to an unsafe URL.");
                }

                currentUri = nextUri;
            }

            return OperationResult<HttpResponseMessage>.CreateFailure(
                $"{resourceDescription} exceeded the maximum of {CatalogConstants.MaxCatalogRedirects} redirects.");
        }
        finally
        {
            response?.Dispose();
        }
    }

    private async Task<PublisherCatalog?> TryFetchAndParseCatalogUrlAsync(
        HttpClient client,
        string rawUrl,
        string logContext,
        CancellationToken ct)
    {
        var normalizedUrl = CloudUrlHelper.NormalizeDirectDownloadUrl(rawUrl);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return null;
        }

        var (catalogUrlSafe, ssrfReason) = await NetworkSecurityHelper.IsSafeUrlAsync(normalizedUrl, ct);
        if (!catalogUrlSafe)
        {
            if (!string.IsNullOrEmpty(ssrfReason))
            {
                logger.LogWarning("Blocked unsafe catalog URL {Url}: {Reason}", rawUrl, ssrfReason);
            }

            return null;
        }

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        try
        {
            var fetchResult = await GetWithRedirectsAsync(client, uri, logContext, ct);
            if (!fetchResult.Success || fetchResult.Data == null)
            {
                logger.LogWarning("Failed to fetch {Context} from {Url}: {Error}", logContext, rawUrl, fetchResult.FirstError);
                return null;
            }

            using var response = fetchResult.Data;
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to fetch {Context} from {Url}: {StatusCode}", logContext, rawUrl, response.StatusCode);
                return null;
            }

            var streamResult = await ReadBoundedStreamAsync(response, CatalogConstants.MaxCatalogSizeBytes, logContext, ct);
            if (!streamResult.Success || streamResult.Data == null)
            {
                return null;
            }

            using var memoryStream = streamResult.Data;
            var catalogJson = Encoding.UTF8.GetString(memoryStream.ToArray());
            var parseResult = await catalogParser.ParseCatalogAsync(catalogJson, ct);

            return parseResult.Success && parseResult.Data != null ? parseResult.Data : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error trying {Context} mirror {Url}", logContext, rawUrl);
            return null;
        }
    }
}
