using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Resolves dependencies that may come from different publishers.
/// Handles cross-publisher dependency resolution and catalog fetching.
/// </summary>
public class CrossPublisherDependencyResolver(
    ILogger<CrossPublisherDependencyResolver> logger,
    IContentManifestPool manifestPool,
    IPublisherSubscriptionStore subscriptionStore,
    IPublisherCatalogParser catalogParser,
    IHttpClientFactory httpClientFactory) : ICrossPublisherDependencyResolver
{
    private readonly ILogger<CrossPublisherDependencyResolver> _logger = logger;
    private readonly IContentManifestPool _manifestPool = manifestPool;
    private readonly IPublisherSubscriptionStore _subscriptionStore = subscriptionStore;
    private readonly IPublisherCatalogParser _catalogParser = catalogParser;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    /// <inheritdoc />
    public async Task<OperationResult<IEnumerable<MissingDependency>>> CheckMissingDependenciesAsync(
        ContentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var missingDependencies = new List<MissingDependency>();

            foreach (var dependency in manifest.Dependencies)
            {
                // Check if dependency is already installed
                var existingManifest = await _manifestPool.GetManifestAsync(dependency.Id, cancellationToken);
                if (existingManifest.Success && existingManifest.Data != null)
                {
                    // Dependency is already installed, skip
                    _logger.LogDebug("Dependency {DependencyId} is already installed", dependency.Id);
                    continue;
                }

                // Dependency is missing, try to resolve it
                var missingDep = new MissingDependency
                {
                    Dependency = dependency,
                };

                // Try to find the dependency content
                var findResult = await FindDependencyContentAsync(dependency, cancellationToken);
                if (findResult.Success && findResult.Data != null)
                {
                    missingDep.ResolvableContent = findResult.Data;
                    _logger.LogInformation(
                        "Found resolvable content for dependency {DependencyId}: {ContentName}",
                        dependency.Id,
                        findResult.Data.Name);
                }
                else
                {
                    _logger.LogWarning(
                        "Could not find resolvable content for dependency {DependencyId}",
                        dependency.Id);
                }

                missingDependencies.Add(missingDep);
            }

            _logger.LogInformation(
                "Found {MissingCount} missing dependencies out of {TotalCount} total",
                missingDependencies.Count,
                manifest.Dependencies.Count);

            return OperationResult<IEnumerable<MissingDependency>>.CreateSuccess(missingDependencies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check missing dependencies");
            return OperationResult<IEnumerable<MissingDependency>>.CreateFailure(
                $"Failed to check dependencies: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<PublisherCatalog>> FetchExternalCatalogAsync(
        string catalogUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            _logger.LogDebug("Fetching external catalog from: {CatalogUrl}", catalogUrl);

            var response = await httpClient.GetAsync(catalogUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            // Check size limit
            if (response.Content.Headers.ContentLength > CatalogConstants.MaxCatalogSizeBytes)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(
                    $"Catalog exceeds maximum size of {CatalogConstants.MaxCatalogSizeBytes} bytes");
            }

            var catalogJson = await response.Content.ReadAsStringAsync(cancellationToken);

            // Parse catalog
            var parseResult = await _catalogParser.ParseCatalogAsync(catalogJson, cancellationToken);
            if (!parseResult.Success)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(parseResult);
            }

            _logger.LogInformation(
                "Successfully fetched catalog for publisher {PublisherId}",
                parseResult.Data!.Publisher.Id);

            return parseResult;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching external catalog");
            return OperationResult<PublisherCatalog>.CreateFailure($"Failed to fetch catalog: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Catalog fetch timed out");
            return OperationResult<PublisherCatalog>.CreateFailure("Catalog fetch timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching external catalog");
            return OperationResult<PublisherCatalog>.CreateFailure($"Unexpected error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentSearchResult?>> FindDependencyContentAsync(
        ContentDependency dependency,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Extract publisher ID from dependency ID
            // Dependency ID format: schemaVersion.userVersion.publisher.contentType.contentName
            var idParts = dependency.Id.Value.Split('.');
            if (idParts.Length < 5)
            {
                return OperationResult<ContentSearchResult?>.CreateFailure(
                    $"Invalid dependency ID format: {dependency.Id}");
            }

            var publisherId = idParts[2];
            var contentName = idParts[4];

            _logger.LogDebug(
                "Searching for dependency: Publisher={PublisherId}, Content={ContentName}",
                publisherId,
                contentName);

            // Check if we're subscribed to this publisher
            var subscriptionResult = await _subscriptionStore.GetSubscriptionAsync(publisherId, cancellationToken);
            if (!subscriptionResult.Success || subscriptionResult.Data == null)
            {
                _logger.LogWarning(
                    "Not subscribed to publisher {PublisherId} for dependency {DependencyId}",
                    publisherId,
                    dependency.Id);
                return OperationResult<ContentSearchResult?>.CreateSuccess(null);
            }

            // Fetch the publisher's catalog
            var catalogResult = await FetchExternalCatalogAsync(
                subscriptionResult.Data.CatalogUrl,
                cancellationToken);

            if (!catalogResult.Success)
            {
                return OperationResult<ContentSearchResult?>.CreateFailure(catalogResult);
            }

            var catalog = catalogResult.Data!;

            // Find matching content in catalog
            var matchingContent = catalog.Content.FirstOrDefault(c =>
                c.Id.Equals(contentName, StringComparison.OrdinalIgnoreCase));

            if (matchingContent == null)
            {
                _logger.LogWarning(
                    "Content {ContentName} not found in publisher {PublisherId} catalog",
                    contentName,
                    publisherId);
                return OperationResult<ContentSearchResult?>.CreateSuccess(null);
            }

            // Get the latest release
            var latestRelease = matchingContent.Releases
                .Where(r => r.IsLatest && !r.IsPrerelease)
                .OrderByDescending(r => r.ReleaseDate)
                .FirstOrDefault();

            if (latestRelease == null)
            {
                _logger.LogWarning(
                    "No stable release found for content {ContentName}",
                    contentName);
                return OperationResult<ContentSearchResult?>.CreateSuccess(null);
            }

            // Create ContentSearchResult
            var searchResult = new ContentSearchResult
            {
                Id = dependency.Id.Value,
                Name = matchingContent.Name,
                Description = matchingContent.Description,
                Version = latestRelease.Version,
                ContentType = matchingContent.ContentType,
                TargetGame = matchingContent.TargetGame,
                ProviderName = catalog.Publisher.Name,
                AuthorName = matchingContent.Metadata?.Author ?? catalog.Publisher.Name,
                ResolverId = CatalogConstants.GenericCatalogResolverId,
                IconUrl = catalog.Publisher.AvatarUrl,
                BannerUrl = matchingContent.Metadata?.BannerUrl,
                LastUpdated = latestRelease.ReleaseDate,
                RequiresResolution = true,
            };

            // Add resolver metadata
            searchResult.ResolverMetadata["catalogItemJson"] = System.Text.Json.JsonSerializer.Serialize(matchingContent);
            searchResult.ResolverMetadata["releaseJson"] = System.Text.Json.JsonSerializer.Serialize(latestRelease);
            searchResult.ResolverMetadata["publisherProfileJson"] = System.Text.Json.JsonSerializer.Serialize(catalog.Publisher);

            _logger.LogInformation(
                "Found dependency content: {ContentName} v{Version}",
                searchResult.Name,
                searchResult.Version);

            return OperationResult<ContentSearchResult?>.CreateSuccess(searchResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find dependency content");
            return OperationResult<ContentSearchResult?>.CreateFailure($"Failed to find dependency: {ex.Message}");
        }
    }
}
