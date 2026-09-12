using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Info.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Discovers Generals Online releases by querying the CDN API.
/// Fetches catalog data and delegates parsing to <see cref="GeneralsOnlineJsonCatalogParser"/>.
/// </summary>
public class GeneralsOnlineDiscoverer(
    ILogger<GeneralsOnlineDiscoverer> logger,
    IProviderDefinitionLoader providerLoader,
    ICatalogParserFactory catalogParserFactory,
    IHttpClientFactory httpClientFactory,
    IGeneralsOnlinePatchNotesService? patchNotesService) : IContentDiscoverer
{
    private const string BaseUrl = GeneralsOnlineConstants.WebsiteUrl;
    private const string DefaultPatchNotesUrl = GeneralsOnlineConstants.PatchNotesUrl;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineDiscoverer"/> class without patch notes service.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="providerLoader">The provider definition loader.</param>
    /// <param name="catalogParserFactory">The catalog parser factory.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public GeneralsOnlineDiscoverer(
        ILogger<GeneralsOnlineDiscoverer> logger,
        IProviderDefinitionLoader providerLoader,
        ICatalogParserFactory catalogParserFactory,
        IHttpClientFactory httpClientFactory)
        : this(logger, providerLoader, catalogParserFactory, httpClientFactory, null)
    {
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
    public static void Dispose()
    {
        // No resources to dispose
    }

    /// <summary>
    /// Discovers Generals Online releases from CDN API using provider definition.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing discovered content.</returns>
    public virtual Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        return DiscoverAsync(provider: null, query, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ProviderDefinition? provider,
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Discovering Generals Online releases");

            // Get provider definition if not provided
            provider ??= providerLoader.GetProvider(GeneralsOnlineConstants.PublisherType);
            if (provider == null)
            {
                logger.LogError("Provider definition not found for {ProviderId}", GeneralsOnlineConstants.PublisherType);
                return OperationResult<ContentDiscoveryResult>.CreateFailure(
                    $"Provider definition '{GeneralsOnlineConstants.PublisherType}' not found. Ensure generalsonline.provider.json exists.");
            }

            logger.LogInformation(
                "Using provider configuration - CatalogUrl: {CatalogUrl}, CatalogFormat: {Format}",
                provider.Endpoints.CatalogUrl,
                provider.CatalogFormat);

            // Step 1: Fetch catalog data from CDN (Discoverer's responsibility)
            var catalogContent = await FetchCatalogDataAsync(provider, cancellationToken);
            if (catalogContent == null)
            {
                return OperationResult<ContentDiscoveryResult>.CreateFailure(
                    "Generals Online CDN is currently unavailable. Please try again later.");
            }

            // Step 2: Get the catalog parser for this provider's format
            var parser = catalogParserFactory.GetParser(provider.CatalogFormat);
            if (parser == null)
            {
                logger.LogError("No parser found for catalog format '{Format}'", provider.CatalogFormat);
                return OperationResult<ContentDiscoveryResult>.CreateFailure(
                    $"No catalog parser registered for format '{provider.CatalogFormat}'");
            }

            // Step 3: Parse the catalog content (Parser's responsibility - NO HTTP calls)
            var parseResult = await parser.ParseAsync(catalogContent, provider, cancellationToken);
            if (!parseResult.Success || parseResult.Data == null)
            {
                return OperationResult<ContentDiscoveryResult>.CreateFailure(
                    parseResult.FirstError ?? "Failed to parse catalog");
            }

            // Step 4: Apply search filters
            var results = parseResult.Data;
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                results = results.Where(r =>
                    r.Version?.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) == true ||
                    r.Name?.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) == true);
            }

            var list = results.ToList();

            // Step 5: Enrich releases with patch notes
            foreach (var item in list)
            {
                await EnrichWithPatchNotesAsync(item, provider, cancellationToken);
            }

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = list,
                TotalItems = list.Count,
                HasMoreItems = false,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover Generals Online releases");
            return OperationResult<ContentDiscoveryResult>.CreateFailure(
                $"Discovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Enriches a discovered release with formatted patch notes if needed.
    /// </summary>
    private async Task EnrichWithPatchNotesAsync(
        ContentSearchResult item,
        ProviderDefinition provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var release = item.GetData<GeneralsOnlineRelease>();
            var version = release?.Version ?? item.Version;
            if (string.IsNullOrWhiteSpace(version))
            {
                return;
            }

            var changelog = release?.Changelog;
            var needsPatchNotes = string.IsNullOrWhiteSpace(changelog) ||
                changelog.Equals("www.playgenerals.online", StringComparison.OrdinalIgnoreCase) ||
                changelog.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                changelog.Equals($"Generals Online {version}", StringComparison.OrdinalIgnoreCase);

            if (!needsPatchNotes)
            {
                return;
            }

            string? notes = null;
            if (patchNotesService != null)
            {
                notes = await patchNotesService.GetPatchNotesFormattedAsync(version, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(notes))
            {
                notes = await FetchDirectPatchNotesAsync(version, provider, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(notes))
            {
                item.Description = notes;
                if (release != null)
                {
                    var updatedRelease = new GeneralsOnlineRelease
                    {
                        Version = release.Version,
                        VersionDate = release.VersionDate,
                        ReleaseDate = release.ReleaseDate,
                        PortableUrl = release.PortableUrl,
                        PortableSize = release.PortableSize,
                        Sha256 = release.Sha256,
                        Changelog = notes,
                    };
                    item.SetData(updatedRelease);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not enrich release with patch notes");
        }
    }

    /// <summary>
    /// Directly fetches and parses patch notes HTML for the given version as a fallback.
    /// </summary>
    private async Task<string?> FetchDirectPatchNotesAsync(
        string version,
        ProviderDefinition provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var datePart = version.Split('_', StringSplitOptions.TrimEntries)[0];
            if (datePart.Length != 6 || !datePart.All(char.IsAsciiDigit))
            {
                return null;
            }

            var patchNotesBaseUrl = provider.Endpoints.GetEndpoint("patchNotesUrl") ?? DefaultPatchNotesUrl;
            var url = $"{patchNotesBaseUrl.TrimEnd('/')}/{datePart}";

            using var httpClient = httpClientFactory.CreateClient(GeneralsOnlineConstants.PublisherType);
            httpClient.Timeout = TimeSpan.FromSeconds(15);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(ApiConstants.BrowserUserAgent);
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            httpClient.DefaultRequestHeaders.Add("Referer", BaseUrl);

            var html = await httpClient.GetStringAsync(url, cancellationToken);
            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

            return GeneralsOnlinePatchNotesService.FormatPatchNotesDocument(document, datePart);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed direct patch notes fetch for version {Version}", version);
            return null;
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
            using var httpClient = httpClientFactory.CreateClient(GeneralsOnlineConstants.PublisherType);
            httpClient.Timeout = TimeSpan.FromSeconds(provider.Timeouts.CatalogTimeoutSeconds);

            var catalogUrl = provider.Endpoints.CatalogUrl;
            var latestVersionUrl = provider.Endpoints.GetEndpoint("latestVersionUrl");

            // Try manifest.json first (full API response)
            if (!string.IsNullOrEmpty(catalogUrl))
            {
                logger.LogDebug("Fetching catalog from {Url}", catalogUrl);
                try
                {
                    var response = await httpClient.GetAsync(catalogUrl, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            logger.LogInformation("Successfully fetched catalog from manifest.json");

                            // Wrap in metadata so parser knows the source
                            return $"{{\"source\":\"manifest\",\"data\":{json}}}";
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning(ex, "Failed to fetch manifest.json, trying latest.txt");
                }
            }

            // Fall back to latest.txt (simple version polling)
            if (!string.IsNullOrEmpty(latestVersionUrl))
            {
                logger.LogDebug("Fetching version from {Url}", latestVersionUrl);
                try
                {
                    var response = await httpClient.GetAsync(latestVersionUrl, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var version = await response.Content.ReadAsStringAsync(cancellationToken);
                        version = version?.Trim();
                        if (!string.IsNullOrWhiteSpace(version))
                        {
                            logger.LogInformation("Successfully fetched version from latest.txt: {Version}", version);

                            // Wrap in metadata so parser knows the source
                            return $"{{\"source\":\"latest\",\"version\":\"{version}\"}}";
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning(ex, "Failed to fetch latest.txt");
                }
            }

            logger.LogWarning("Generals Online CDN is unreachable");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch Generals Online catalog");
            return null;
        }
    }
}
