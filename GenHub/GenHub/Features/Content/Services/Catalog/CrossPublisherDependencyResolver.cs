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
                var existingManifest = await manifestPool.GetManifestAsync(dependency.Id, cancellationToken);
                if (existingManifest.Success && existingManifest.Data != null)
                {
                    // Dependency is already installed, skip
                    logger.LogDebug("Dependency {DependencyId} is already installed", dependency.Id);
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
                    logger.LogInformation(
                        "Found resolvable content for dependency {DependencyId}: {ContentName}",
                        dependency.Id,
                        findResult.Data.Name);
                }
                else
                {
                    logger.LogWarning(
                        "Could not find resolvable content for dependency {DependencyId}",
                        dependency.Id);
                }

                missingDependencies.Add(missingDep);
            }

            logger.LogInformation(
                "Found {MissingCount} missing dependencies out of {TotalCount} total",
                missingDependencies.Count,
                manifest.Dependencies.Count);

            return OperationResult<IEnumerable<MissingDependency>>.CreateSuccess(missingDependencies);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check missing dependencies");
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
            var httpClient = httpClientFactory.CreateClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(HostingConstants.CatalogFetchTimeoutSeconds));
            var ct = timeoutCts.Token;

            logger.LogDebug("Fetching external catalog from: {CatalogUrl}", catalogUrl);

            var response = await httpClient.GetAsync(catalogUrl, ct);
            response.EnsureSuccessStatusCode();

            // Check size limit with bounded stream read
            if (response.Content.Headers.ContentLength > CatalogConstants.MaxCatalogSizeBytes)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(
                    $"Catalog exceeds maximum size of {CatalogConstants.MaxCatalogSizeBytes} bytes");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var memoryStream = new System.IO.MemoryStream();
            var buffer = new byte[81920];
            var bytesRead = 0;
            long totalBytesRead = 0;
            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                totalBytesRead += bytesRead;
                if (totalBytesRead > CatalogConstants.MaxCatalogSizeBytes)
                {
                    return OperationResult<PublisherCatalog>.CreateFailure(
                        $"Catalog exceeds maximum size of {CatalogConstants.MaxCatalogSizeBytes} bytes");
                }

                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            }

            var catalogJson = System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());

            // Parse catalog
            var parseResult = await catalogParser.ParseCatalogAsync(catalogJson, ct);
            if (!parseResult.Success)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(parseResult);
            }

            if (parseResult.Data == null)
            {
                return OperationResult<PublisherCatalog>.CreateFailure("Catalog parser returned null data");
            }

            logger.LogInformation(
                "Successfully fetched catalog for publisher {PublisherId}",
                parseResult.Data.Publisher.Id);

            return parseResult;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error fetching external catalog");
            return OperationResult<PublisherCatalog>.CreateFailure($"Failed to fetch catalog: {ex.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Catalog fetch timed out after {Timeout} seconds", HostingConstants.CatalogFetchTimeoutSeconds);
            return OperationResult<PublisherCatalog>.CreateFailure("Catalog fetch timed out");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error fetching external catalog");
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
            var idParts = dependency.Id.Value?.Split('.') ?? [];
            if (idParts.Length < 5)
            {
                return OperationResult<ContentSearchResult?>.CreateFailure(
                    $"Invalid dependency ID format: {dependency.Id.Value}");
            }

            var publisherId = idParts[2];
            var contentName = idParts[4];

            logger.LogDebug(
                "Searching for dependency: Publisher={PublisherId}, Content={ContentName}",
                publisherId,
                contentName);

            // Check if we're subscribed to this publisher
            var subscriptionResult = await subscriptionStore.GetSubscriptionAsync(publisherId, cancellationToken);
            if (!subscriptionResult.Success || subscriptionResult.Data == null)
            {
                logger.LogWarning(
                    "Not subscribed to publisher {PublisherId} for dependency {DependencyId}",
                    publisherId,
                    dependency.Id);
                return OperationResult<ContentSearchResult?>.CreateSuccess(null);
            }

            // Fetch the publisher's catalog
            var catalogResult = await FetchExternalCatalogAsync(
                subscriptionResult.Data.CatalogUrl,
                cancellationToken);

            if (!catalogResult.Success || catalogResult.Data == null)
            {
                return OperationResult<ContentSearchResult?>.CreateFailure(catalogResult);
            }

            var catalog = catalogResult.Data;

            // Find matching content in catalog
            var matchingContent = catalog.Content.FirstOrDefault(c =>
                c.Id.Equals(contentName, StringComparison.OrdinalIgnoreCase));

            if (matchingContent == null)
            {
                logger.LogWarning(
                    "Content {ContentName} not found in publisher {PublisherId} catalog",
                    contentName,
                    publisherId);
                return OperationResult<ContentSearchResult?>.CreateSuccess(null);
            }

            // Get the latest release
            var latestRelease = matchingContent.Releases
                .Where(r => r.IsLatest && !r.IsPrerelease)
                .OrderByDescending(r => r.ReleaseDate)
                .FirstOrDefault()
                ?? matchingContent.Releases
                    .Where(r => !r.IsPrerelease)
                    .OrderByDescending(r => r.ReleaseDate)
                    .FirstOrDefault()
                ?? matchingContent.Releases
                    .OrderByDescending(r => r.ReleaseDate)
                    .FirstOrDefault();

            if (latestRelease == null)
            {
                logger.LogWarning(
                    "No stable release found for content {ContentName}",
                    contentName);
                return OperationResult<ContentSearchResult?>.CreateSuccess(null);
            }

            // Create ContentSearchResult
            var searchResult = new ContentSearchResult
            {
                Id = dependency.Id.Value ?? string.Empty,
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

            logger.LogInformation(
                "Found dependency content: {ContentName} v{Version}",
                searchResult.Name,
                searchResult.Version);

            return OperationResult<ContentSearchResult?>.CreateSuccess(searchResult);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to find dependency content");
            return OperationResult<ContentSearchResult?>.CreateFailure($"Failed to find dependency: {ex.Message}");
        }
    }
}
