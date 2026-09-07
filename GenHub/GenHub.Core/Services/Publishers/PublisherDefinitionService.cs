using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
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
            if (string.IsNullOrWhiteSpace(definitionUrl) ||
                !Uri.TryCreate(definitionUrl, UriKind.Absolute, out var uri))
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

            if (response.Content.Headers.ContentLength is { } headerLength &&
                headerLength > CatalogConstants.MaxCatalogSizeBytes)
            {
                return OperationResult<PublisherDefinition>.CreateFailure(
                    $"Definition exceeds maximum size of {CatalogConstants.MaxCatalogSizeBytes} bytes");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var memoryStream = new MemoryStream();
            var buffer = new byte[HostingConstants.StreamCopyBufferSize];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
            {
                totalBytesRead += bytesRead;
                if (totalBytesRead > CatalogConstants.MaxCatalogSizeBytes)
                {
                    return OperationResult<PublisherDefinition>.CreateFailure(
                        $"Definition exceeds maximum size of {CatalogConstants.MaxCatalogSizeBytes} bytes");
                }

                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            }

            memoryStream.Position = 0;
            var definition = await JsonSerializer.DeserializeAsync<PublisherDefinition>(memoryStream, JsonOptions, ct);

            if (definition == null)
            {
                return OperationResult<PublisherDefinition>.CreateFailure("Failed to deserialize publisher definition");
            }

            // Ensure the definition URL is set correctly on the object
            definition.DefinitionUrl = definitionUrl;

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
            var urlsToTry = new List<string> { definition.CatalogUrl };
            if (definition.CatalogMirrors != null)
            {
                urlsToTry.AddRange(definition.CatalogMirrors);
            }

            using var client = httpClientFactory.CreateClient("PublisherCatalog");
            var errors = new List<string>();

            foreach (var url in urlsToTry)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                logger.LogInformation("Attempting to fetch catalog from: {Url}", url);
                var result = await FetchAndParseCatalogAsync(client, url, ct);
                if (result.Success && result.Data != null)
                {
                    return result;
                }

                errors.AddRange(result.Errors);
            }

            return OperationResult<PublisherCatalog>.CreateFailure(errors);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Critical error in FetchCatalogFromDefinitionAsync");
            return OperationResult<PublisherCatalog>.CreateFailure($"Critical error: {ex.Message}");
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
                return OperationResult<bool>.CreateSuccess(false); // No definition URL, no updates
            }

            var fetchResult = await FetchDefinitionAsync(subscription.DefinitionUrl, ct);
            if (!fetchResult.Success)
            {
                return OperationResult<bool>.CreateFailure(fetchResult);
            }

            var definition = fetchResult.Data;
            bool updated = false;

            // Check if catalog URL has changed
            if (!string.Equals(subscription.CatalogUrl, definition.CatalogUrl, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "Updating catalog URL for subscription {PublisherId} from {OldUrl} to {NewUrl}",
                    subscription.PublisherId,
                    subscription.CatalogUrl,
                    definition.CatalogUrl);

                subscription.CatalogUrl = definition.CatalogUrl;
                updated = true;
            }

            // Potentially update other metadata here if we expand PublisherSubscription
            // e.g. Name, Description updates could be propagated
            return OperationResult<bool>.CreateSuccess(updated);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking for definition update for {PublisherId}", subscription.PublisherId);
            return OperationResult<bool>.CreateFailure($"Error checking update: {ex.Message}");
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

    private async Task<OperationResult<PublisherCatalog>> FetchAndParseCatalogAsync(
        HttpClient client,
        string url,
        CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                return OperationResult<PublisherCatalog>.CreateFailure($"Failed to fetch from {url}: {response.StatusCode}");
            }

            if (response.Content.Headers.ContentLength is { } headerLength &&
                headerLength > CatalogConstants.MaxCatalogSizeBytes)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(
                    $"Catalog from {url} exceeds maximum size of {CatalogConstants.MaxCatalogSizeBytes} bytes");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var memoryStream = new MemoryStream();
            var buffer = new byte[HostingConstants.StreamCopyBufferSize];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
            {
                totalBytesRead += bytesRead;
                if (totalBytesRead > CatalogConstants.MaxCatalogSizeBytes)
                {
                    return OperationResult<PublisherCatalog>.CreateFailure(
                        $"Catalog from {url} exceeds maximum size of {CatalogConstants.MaxCatalogSizeBytes} bytes");
                }

                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            }

            memoryStream.Position = 0;
            using var reader = new StreamReader(memoryStream, System.Text.Encoding.UTF8);
            var json = await reader.ReadToEndAsync(ct);

            var parseResult = await catalogParser.ParseCatalogAsync(json, ct);
            if (!parseResult.Success)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(
                    $"Failed to parse catalog from {url}: {string.Join(", ", parseResult.Errors)}");
            }

            return parseResult;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Exception fetching/parsing catalog from {Url}", url);
            return OperationResult<PublisherCatalog>.CreateFailure($"Exception processing {url}: {ex.Message}");
        }
    }

    private async Task<(string? CatalogId, PublisherCatalog? Catalog, string? Error)> TryFetchCatalogEntryAsync(
        HttpClient client,
        CatalogEntry catalogEntry,
        CancellationToken ct)
    {
        var urlsToTry = new List<string> { catalogEntry.Url };
        if (catalogEntry.Mirrors != null)
        {
            urlsToTry.AddRange(catalogEntry.Mirrors);
        }

        foreach (var url in urlsToTry)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            logger.LogInformation("Fetching catalog '{CatalogId}' from: {Url}", catalogEntry.Id, url);
            var result = await FetchAndParseCatalogAsync(client, url, ct);
            if (result.Success && result.Data != null)
            {
                return (catalogEntry.Id, result.Data, null);
            }
        }

        return (catalogEntry.Id, null, $"Failed to fetch catalog '{catalogEntry.Name}' ({catalogEntry.Id})");
    }
}
