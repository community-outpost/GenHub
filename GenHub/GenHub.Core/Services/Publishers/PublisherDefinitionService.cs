using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Publishers;

/// <summary>
/// Service for fetching and processing publisher definitions.
/// </summary>
public class PublisherDefinitionService : IPublisherDefinitionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) },
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPublisherCatalogParser _catalogParser;
    private readonly ILogger<PublisherDefinitionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherDefinitionService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory for making HTTP requests.</param>
    /// <param name="catalogParser">The catalog parser for parsing publisher catalogs.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public PublisherDefinitionService(
        IHttpClientFactory httpClientFactory,
        IPublisherCatalogParser catalogParser,
        ILogger<PublisherDefinitionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _catalogParser = catalogParser;
        _logger = logger;
    }

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

            using var client = _httpClientFactory.CreateClient("PublisherDefinition");
            var response = await client.GetAsync(uri, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch definition from {Url}: {StatusCode}", definitionUrl, response.StatusCode);
                return OperationResult<PublisherDefinition>.CreateFailure(
                    $"Failed to fetch definition: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var definition = JsonSerializer.Deserialize<PublisherDefinition>(json, JsonOptions);

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
                    Mirrors = definition.CatalogMirrors ?? new List<string>()
                });
                _logger.LogInformation("Migrated V1 definition to V2 format for publisher {PublisherId}", definition.Publisher?.Id);
            }

            return OperationResult<PublisherDefinition>.CreateSuccess(definition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception fetching definition from {Url}", definitionUrl);
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
            var urlsToTry = new System.Collections.Generic.List<string> { definition.CatalogUrl };
            if (definition.CatalogMirrors != null)
            {
                urlsToTry.AddRange(definition.CatalogMirrors);
            }

            using var client = _httpClientFactory.CreateClient("PublisherCatalog");
            var errors = new System.Collections.Generic.List<string>();

            foreach (var url in urlsToTry)
            {
                if (string.IsNullOrWhiteSpace(url)) continue;

                try
                {
                    _logger.LogInformation("Attempting to fetch catalog from: {Url}", url);

                    var response = await client.GetAsync(url, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        errors.Add($"Failed to fetch from {url}: {response.StatusCode}");
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync(ct);
                    var parseResult = await _catalogParser.ParseCatalogAsync(json, ct);

                    if (parseResult.Success)
                    {
                        return parseResult;
                    }
                    else
                    {
                        errors.Add($"Failed to parse catalog from {url}: {string.Join(", ", parseResult.Errors)}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception fetching/parsing catalog from {Url}", url);
                    errors.Add($"Exception processing {url}: {ex.Message}");
                }
            }

            return OperationResult<PublisherCatalog>.CreateFailure(errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error in FetchCatalogFromDefinitionAsync");
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
                _logger.LogInformation(
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for definition update for {PublisherId}", subscription.PublisherId);
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
            using var client = _httpClientFactory.CreateClient("PublisherCatalog");
            var errors = new List<string>();

            foreach (var catalogEntry in definition.Catalogs)
            {
                var urlsToTry = new List<string> { catalogEntry.Url };
                urlsToTry.AddRange(catalogEntry.Mirrors);

                bool fetched = false;
                foreach (var url in urlsToTry)
                {
                    if (string.IsNullOrWhiteSpace(url)) continue;

                    try
                    {
                        _logger.LogInformation("Fetching catalog '{CatalogId}' from: {Url}", catalogEntry.Id, url);

                        var response = await client.GetAsync(url, ct);
                        if (!response.IsSuccessStatusCode) continue;

                        var json = await response.Content.ReadAsStringAsync(ct);
                        var parseResult = await _catalogParser.ParseCatalogAsync(json, ct);

                        if (parseResult.Success && parseResult.Data != null)
                        {
                            results[catalogEntry.Id] = parseResult.Data;
                            fetched = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fetch catalog '{CatalogId}' from {Url}", catalogEntry.Id, url);
                    }
                }

                if (!fetched)
                {
                    errors.Add($"Failed to fetch catalog '{catalogEntry.Name}' ({catalogEntry.Id})");
                }
            }

            if (results.Count == 0)
            {
                return OperationResult<Dictionary<string, PublisherCatalog>>.CreateFailure(errors);
            }

            return OperationResult<Dictionary<string, PublisherCatalog>>.CreateSuccess(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error in FetchAllCatalogsAsync");
            return OperationResult<Dictionary<string, PublisherCatalog>>.CreateFailure($"Critical error: {ex.Message}");
        }
    }
}
