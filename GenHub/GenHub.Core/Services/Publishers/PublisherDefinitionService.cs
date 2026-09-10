using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Publishers;

/// <summary>
/// Service for fetching and processing publisher definitions.
/// </summary>
public class PublisherDefinitionService(
    IHttpClientFactory httpClientFactory,
    IPublisherCatalogParser catalogParser,
    ILogger<PublisherDefinitionService> logger) : IPublisherDefinitionService
{
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

            using var client = httpClientFactory.CreateClient("PublisherDefinition");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);

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

            // Ensure the definition URL is set correctly on the object if not specified by the publisher JSON
            if (string.IsNullOrEmpty(definition.DefinitionUrl))
            {
                definition.DefinitionUrl = normalizedUrl;
            }

            // V1 to V2 migration: if CatalogUrl is set but Catalogs is empty, populate Catalogs
            if (definition.SchemaVersion <= 1 && definition.Catalogs.Count == 0 && !string.IsNullOrEmpty(definition.CatalogUrl))
            {
                definition.Catalogs.Add(new CatalogEntry
                {
                    Id = "default",
                    Name = "Content",
                    Url = definition.CatalogUrl,
                    Mirrors = definition.CatalogMirrors ?? new List<string>(),
                });
                logger.LogInformation("Migrated V1 definition to V2 format for publisher {PublisherId}", definition.Publisher?.Id);
            }

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

            using var client = httpClientFactory.CreateClient("PublisherCatalog");
            var urlsToTry = new List<string> { catalogUrl };
            if (definition.CatalogMirrors != null)
            {
                urlsToTry.AddRange(definition.CatalogMirrors);
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
            if (!string.Equals(subscription.CatalogUrl, remoteDef.CatalogUrl, StringComparison.OrdinalIgnoreCase))
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
            if (remoteDef.PreviousDefinitionUrls.Contains(subscription.DefinitionUrl, StringComparer.OrdinalIgnoreCase) &&
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

    /// <inheritdoc />
    public async Task<OperationResult<Dictionary<string, PublisherCatalog>>> FetchAllCatalogsAsync(
        PublisherDefinition definition,
        CancellationToken ct = default)
    {
        try
        {
            var results = new Dictionary<string, PublisherCatalog>();

            // Handle V1 definitions (single catalog via CatalogUrl)
            if (definition.Catalogs.Count == 0 && !string.IsNullOrEmpty(definition.CatalogUrl))
            {
                var catalogResult = await FetchCatalogFromDefinitionAsync(definition, ct);
                if (catalogResult.Success && catalogResult.Data != null)
                {
                    results["default"] = catalogResult.Data;
                }
                else
                {
                    return OperationResult<Dictionary<string, PublisherCatalog>>.CreateFailure(catalogResult);
                }

                return OperationResult<Dictionary<string, PublisherCatalog>>.CreateSuccess(results);
            }

            // Handle V2 definitions (multiple catalogs)
            using var client = httpClientFactory.CreateClient("PublisherCatalog");
            var errors = new List<string>();

            foreach (var catalogEntry in definition.Catalogs)
            {
                var (id, catalog, error) = await TryFetchCatalogEntryAsync(client, catalogEntry, ct);
                if (catalog != null && id != null)
                {
                    results[id] = catalog;
                }
                else if (!string.IsNullOrEmpty(error))
                {
                    errors.Add(error);
                }
            }

            if (results.Count == 0)
            {
                return OperationResult<Dictionary<string, PublisherCatalog>>.CreateFailure(errors);
            }

            return OperationResult<Dictionary<string, PublisherCatalog>>.CreateSuccess(results);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Critical error in FetchAllCatalogsAsync");
            return OperationResult<Dictionary<string, PublisherCatalog>>.CreateFailure($"Critical error: {ex.Message}");
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
            int bytesRead;

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

    private async Task<PublisherCatalog?> TryFetchAndParseCatalogUrlAsync(
        HttpClient client,
        string rawUrl,
        string logContext,
        CancellationToken ct)
    {
        var normalizedUrl = CloudUrlHelper.NormalizeDirectDownloadUrl(rawUrl);
        if (string.IsNullOrWhiteSpace(normalizedUrl) ||
            !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
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

    private async Task<(string? Id, PublisherCatalog? Catalog, string? Error)> TryFetchCatalogEntryAsync(
        HttpClient client,
        CatalogEntry catalogEntry,
        CancellationToken ct)
    {
        var urlsToTry = new List<string> { catalogEntry.Url };
        urlsToTry.AddRange(catalogEntry.Mirrors);

        foreach (var rawUrl in urlsToTry)
        {
            var catalog = await TryFetchAndParseCatalogUrlAsync(client, rawUrl, $"Catalog {catalogEntry.Id}", ct);
            if (catalog != null)
            {
                return (catalogEntry.Id, catalog, null);
            }
        }

        return (null, null, $"Failed to fetch catalog '{catalogEntry.Name}' ({catalogEntry.Id}) from all URLs");
    }
}
