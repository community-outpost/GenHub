using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Discovers Generals Online releases by querying the CDN API.
/// Fetches catalog data and delegates parsing to <see cref="GeneralsOnlineJsonCatalogParser"/>.
/// </summary>
public class GeneralsOnlineDiscoverer : IContentDiscoverer
{
    private readonly ILogger<GeneralsOnlineDiscoverer> _logger;
    private readonly IProviderDefinitionLoader _providerLoader;
    private readonly ICatalogParserFactory _catalogParserFactory;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineDiscoverer"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic information.</param>
    /// <param name="providerLoader">Provider definition loader.</param>
    /// <param name="catalogParserFactory">Factory for getting catalog parsers.</param>
    /// <param name="httpClientFactory">HTTP client factory for fetching catalog data.</param>
    public GeneralsOnlineDiscoverer(
        ILogger<GeneralsOnlineDiscoverer> logger,
        IProviderDefinitionLoader providerLoader,
        ICatalogParserFactory catalogParserFactory,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _providerLoader = providerLoader;
        _catalogParserFactory = catalogParserFactory;
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string SourceName => GeneralsOnlineConstants.PublisherType;

    /// <inheritdoc />
    public string Description => "Discovers Generals Online releases from official CDN";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <summary>
    /// Disposes resources used by the discoverer.
    /// </summary>
    public void Dispose()
    {
        // No resources to dispose
    }

    /// <summary>
    /// Discovers Generals Online releases from CDN API using provider definition.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing discovered content.</returns>
    public Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        return DiscoverAsync(provider: null, query, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ProviderDefinition? provider,
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Discovering Generals Online releases");

            // Get provider definition if not provided
            provider ??= _providerLoader.GetProvider(GeneralsOnlineConstants.PublisherType);
            if (provider == null)
            {
                _logger.LogError("Provider definition not found for {ProviderId}", GeneralsOnlineConstants.PublisherType);
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    $"Provider definition '{GeneralsOnlineConstants.PublisherType}' not found. Ensure generalsonline.provider.json exists.");
            }

            _logger.LogInformation(
                "Using provider configuration - CatalogUrl: {CatalogUrl}, CatalogFormat: {Format}",
                provider.Endpoints.CatalogUrl,
                provider.CatalogFormat);

            // Step 1: Fetch catalog data from CDN (Discoverer's responsibility)
            var catalogContent = await FetchCatalogDataAsync(provider, cancellationToken);
            if (catalogContent == null)
            {
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    "Generals Online CDN is currently unavailable. Please try again later.");
            }

            // Step 2: Get the catalog parser for this provider's format
            var parser = _catalogParserFactory.GetParser(provider.CatalogFormat);
            if (parser == null)
            {
                _logger.LogError("No parser found for catalog format '{Format}'", provider.CatalogFormat);
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    $"No catalog parser registered for format '{provider.CatalogFormat}'");
            }

            // Step 3: Parse the catalog content (Parser's responsibility - NO HTTP calls)
            var parseResult = await parser.ParseAsync(catalogContent, provider, cancellationToken);
            if (!parseResult.Success || parseResult.Data == null)
            {
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    parseResult.FirstError ?? "Failed to parse catalog");
            }

            // Step 4: Apply search filters
            var results = parseResult.Data;
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                results = results.Where(r =>
                    (r.Version?.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (r.Name?.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover Generals Online releases");
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                $"Discovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches catalog data from Generals Online CDN.
    /// Tries manifest.json first, falls back to latest.txt.
    /// </summary>
    /// <param name="provider">The provider configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// JSON string containing catalog data, or null if CDN is unreachable.
    /// Format: JSON object with "source" field indicating which endpoint responded.
    /// </returns>
    private async Task<string?> FetchCatalogDataAsync(
        ProviderDefinition provider,
        CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient(GeneralsOnlineConstants.PublisherType);
            httpClient.Timeout = TimeSpan.FromSeconds(provider.Timeouts.CatalogTimeoutSeconds);

            var catalogUrl = provider.Endpoints.CatalogUrl;
            var latestVersionUrl = provider.Endpoints.GetEndpoint("latestVersionUrl");

            // Try manifest.json first (full API response)
            if (!string.IsNullOrEmpty(catalogUrl))
            {
                _logger.LogDebug("Fetching catalog from {Url}", catalogUrl);
                try
                {
                    var response = await httpClient.GetAsync(catalogUrl, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            _logger.LogInformation("Successfully fetched catalog from manifest.json");

                            // Wrap in metadata so parser knows the source
                            return $"{{\"source\":\"manifest\",\"data\":{json}}}";
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch manifest.json, trying latest.txt");
                }
            }

            // Fall back to latest.txt (simple version polling)
            if (!string.IsNullOrEmpty(latestVersionUrl))
            {
                _logger.LogDebug("Fetching version from {Url}", latestVersionUrl);
                try
                {
                    var response = await httpClient.GetAsync(latestVersionUrl, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var version = await response.Content.ReadAsStringAsync(cancellationToken);
                        version = version?.Trim();
                        if (!string.IsNullOrWhiteSpace(version))
                        {
                            _logger.LogInformation("Successfully fetched version from latest.txt: {Version}", version);

                            // Wrap in metadata so parser knows the source
                            return $"{{\"source\":\"latest\",\"version\":\"{version}\"}}";
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch latest.txt");
                }
            }

            _logger.LogWarning("Generals Online CDN is unreachable");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Generals Online catalog");
            return null;
        }
    }
}
