using GenHub.Core.Constants;
using GenHub.Core.Helpers;
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
                var missingDep = await CheckDependencyAsync(dependency, cancellationToken);
                if (missingDep != null)
                {
                    missingDependencies.Add(missingDep);
                }
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
            var urlValidation = ValidateCatalogUrl(catalogUrl, out var normalizedUrl);
            if (!urlValidation.Success)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(urlValidation.FirstError ?? "Invalid catalog URL.");
            }

            var httpClient = httpClientFactory.CreateClient(CatalogConstants.CatalogHttpClientName);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(HostingConstants.CatalogFetchTimeoutSeconds));
            var ct = timeoutCts.Token;

            logger.LogDebug("Fetching external catalog from: {CatalogUrl}", catalogUrl);

            var response = await httpClient.GetAsync(normalizedUrl, ct);
            response.EnsureSuccessStatusCode();

            var readResult = await ReadBoundedCatalogStringAsync(response, ct);
            if (!readResult.Success || readResult.Data == null)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(readResult.FirstError ?? "Failed to read external catalog.");
            }

            logger.LogDebug("Parsing fetched external catalog ({SizeBytes} bytes)", readResult.Data.Length);

            var parseResult = await catalogParser.ParseCatalogAsync(readResult.Data, cancellationToken);
            if (!parseResult.Success || parseResult.Data == null)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(
                    $"Failed to parse external catalog: {parseResult.FirstError}");
            }

            return OperationResult<PublisherCatalog>.CreateSuccess(parseResult.Data);
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
            if (!TryParseDependencyId(dependency.Id.Value, out var publisherId, out var contentName))
            {
                logger.LogWarning("Dependency ID {DependencyId} has invalid format", dependency.Id);
                return OperationResult<ContentSearchResult?>.CreateFailure($"Invalid dependency ID format: {dependency.Id}");
            }

            // Look up publisher subscription
            var subscriptionResult = await subscriptionStore.GetSubscriptionAsync(publisherId, cancellationToken);
            if (!subscriptionResult.Success || subscriptionResult.Data == null)
            {
                logger.LogWarning("Publisher {PublisherId} not found in subscriptions", publisherId);
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

            var constraint = CreateVersionConstraint(dependency);

            var candidateReleases = matchingContent.Releases
                .Where(r => constraint == null || constraint.IsSatisfiedBy(r.Version));

            // Get the latest release matching constraints
            var latestRelease = candidateReleases
                .Where(r => r.IsLatest && !r.IsPrerelease)
                .OrderByDescending(r => r.ReleaseDate)
                .FirstOrDefault()
                ?? candidateReleases
                    .Where(r => !r.IsPrerelease)
                    .OrderByDescending(r => r.ReleaseDate)
                    .FirstOrDefault()
                ?? candidateReleases
                    .OrderByDescending(r => r.ReleaseDate)
                    .FirstOrDefault();

            if (latestRelease == null)
            {
                logger.LogWarning(
                    "No release found matching version constraints for content {ContentName}",
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

    private static OperationResult<bool> ValidateCatalogUrl(string catalogUrl, out string normalizedUrl)
    {
        normalizedUrl = CloudUrlHelper.NormalizeDirectDownloadUrl(catalogUrl);
        if (!NetworkSecurityHelper.IsSafeUrl(normalizedUrl, out var failureReason))
        {
            return OperationResult<bool>.CreateFailure(failureReason ?? "Catalog URL must be a valid absolute HTTP or HTTPS URL.");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static async Task<OperationResult<string>> ReadBoundedCatalogStringAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > CatalogConstants.MaxCatalogSizeBytes)
        {
            return OperationResult<string>.CreateFailure(
                $"External catalog exceeds maximum size of {CatalogConstants.MaxCatalogSizeBytes} bytes");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memoryStream = new MemoryStream();
        var buffer = new byte[HostingConstants.StreamCopyBufferSize];
        long totalRead = 0;
        var bytesRead = 0;

        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > CatalogConstants.MaxCatalogSizeBytes)
            {
                return OperationResult<string>.CreateFailure(
                    $"External catalog exceeds maximum size of {CatalogConstants.MaxCatalogSizeBytes} bytes");
            }

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
        }

        return OperationResult<string>.CreateSuccess(Encoding.UTF8.GetString(memoryStream.ToArray()));
    }

    private static bool TryParseDependencyId(string? id, out string publisherId, out string contentName)
    {
        publisherId = string.Empty;
        contentName = string.Empty;

        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (id.Contains(':'))
        {
            var colonParts = id.Split(':');
            if (colonParts.Length == 2 && !string.IsNullOrWhiteSpace(colonParts[0]) && !string.IsNullOrWhiteSpace(colonParts[1]))
            {
                publisherId = colonParts[0];
                contentName = colonParts[1];
                return true;
            }

            return false;
        }

        if (id.Contains('.'))
        {
            var dotParts = id.Split('.');
            if (dotParts.Length >= 5)
            {
                publisherId = dotParts[2];
                contentName = string.Join('.', dotParts.Skip(4));
                return !string.IsNullOrWhiteSpace(publisherId) && !string.IsNullOrWhiteSpace(contentName);
            }

            if (dotParts.Length == 3)
            {
                publisherId = dotParts[0];
                contentName = dotParts[2];
                return !string.IsNullOrWhiteSpace(publisherId) && !string.IsNullOrWhiteSpace(contentName);
            }
        }

        return false;
    }

    private static VersionConstraint? CreateVersionConstraint(ContentDependency dependency)
    {
        if (!string.IsNullOrEmpty(dependency.ExactVersion))
        {
            return VersionConstraint.Exact(dependency.ExactVersion);
        }

        if (!string.IsNullOrEmpty(dependency.MinVersion) || !string.IsNullOrEmpty(dependency.MaxVersion))
        {
            return new VersionConstraint
            {
                MinVersion = dependency.MinVersion,
                MinInclusive = dependency.MinInclusive,
                MaxVersion = dependency.MaxVersion,
                MaxInclusive = dependency.MaxInclusive,
            };
        }

        return null;
    }

    private static bool IsDependencySatisfied(ContentDependency dependency, string? installedVersion)
    {
        var installedConstraint = CreateVersionConstraint(dependency);
        return installedConstraint == null || installedConstraint.IsSatisfiedBy(installedVersion);
    }

    private static bool IsSafeIpAddress(IPAddress address) => NetworkSecurityHelper.IsSafeIpAddress(address);

    private async Task<MissingDependency?> CheckDependencyAsync(
        ContentDependency dependency,
        CancellationToken cancellationToken)
    {
        var existingManifest = await manifestPool.GetManifestAsync(dependency.Id, cancellationToken);
        if (existingManifest.Success && existingManifest.Data != null)
        {
            if (IsDependencySatisfied(dependency, existingManifest.Data.Version))
            {
                logger.LogDebug(
                    "Dependency {DependencyId} (v{Version}) is already installed and satisfies constraints",
                    dependency.Id,
                    existingManifest.Data.Version);
                return null;
            }

            logger.LogInformation(
                "Installed dependency {DependencyId} (v{Version}) does not satisfy constraints; attempting to resolve matching version",
                dependency.Id,
                existingManifest.Data.Version);
        }

        return await ResolveMissingDependencyAsync(dependency, cancellationToken);
    }

    private async Task<MissingDependency> ResolveMissingDependencyAsync(
        ContentDependency dependency,
        CancellationToken cancellationToken)
    {
        var missingDep = new MissingDependency
        {
            Dependency = dependency,
        };

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

        return missingDep;
    }
}
