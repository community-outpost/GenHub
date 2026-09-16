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
            var normalizedUrl = CloudUrlHelper.NormalizeDirectDownloadUrl(catalogUrl);
            if (string.IsNullOrWhiteSpace(normalizedUrl) ||
                !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return OperationResult<PublisherCatalog>.CreateFailure("Catalog URL must be a valid absolute HTTP or HTTPS URL.");
            }

            if (uri.IsLoopback ||
                uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<PublisherCatalog>.CreateFailure("Loopback and local addresses are not allowed for catalog sources.");
            }

            if ((IPAddress.TryParse(uri.DnsSafeHost, out var ip) || IPAddress.TryParse(uri.Host, out ip)) && !IsSafeIpAddress(ip))
            {
                return OperationResult<PublisherCatalog>.CreateFailure("Loopback, private, and local addresses are not allowed for catalog sources.");
            }

            var httpClient = httpClientFactory.CreateClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(HostingConstants.CatalogFetchTimeoutSeconds));
            var ct = timeoutCts.Token;

            logger.LogDebug("Fetching external catalog from: {CatalogUrl}", catalogUrl);

            var response = await httpClient.GetAsync(normalizedUrl, ct);
            response.EnsureSuccessStatusCode();

            // Check size limit with bounded stream read
            if (response.Content.Headers.ContentLength > CatalogConstants.MaxCatalogSizeBytes)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(
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
                    return OperationResult<PublisherCatalog>.CreateFailure(
                        $"External catalog exceeds maximum size of {CatalogConstants.MaxCatalogSizeBytes} bytes");
                }

                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            }

            var catalogJson = Encoding.UTF8.GetString(memoryStream.ToArray());

            logger.LogDebug("Parsing fetched external catalog ({SizeBytes} bytes)", totalRead);

            var parseResult = await catalogParser.ParseCatalogAsync(catalogJson, cancellationToken);
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

            VersionConstraint? constraint = null;
            if (!string.IsNullOrEmpty(dependency.ExactVersion))
            {
                constraint = VersionConstraint.Exact(dependency.ExactVersion);
            }
            else if (!string.IsNullOrEmpty(dependency.MinVersion) || !string.IsNullOrEmpty(dependency.MaxVersion))
            {
                constraint = new VersionConstraint
                {
                    MinVersion = dependency.MinVersion,
                    MinInclusive = dependency.MinInclusive,
                    MaxVersion = dependency.MaxVersion,
                    MaxInclusive = dependency.MaxInclusive,
                };
            }

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

    private static bool IsDependencySatisfied(ContentDependency dependency, string? installedVersion)
    {
        VersionConstraint? installedConstraint = null;
        if (!string.IsNullOrEmpty(dependency.ExactVersion))
        {
            installedConstraint = VersionConstraint.Exact(dependency.ExactVersion);
        }
        else if (!string.IsNullOrEmpty(dependency.MinVersion) || !string.IsNullOrEmpty(dependency.MaxVersion))
        {
            installedConstraint = new VersionConstraint
            {
                MinVersion = dependency.MinVersion,
                MinInclusive = dependency.MinInclusive,
                MaxVersion = dependency.MaxVersion,
                MaxInclusive = dependency.MaxInclusive,
            };
        }

        return installedConstraint == null || installedConstraint.IsSatisfiedBy(installedVersion);
    }

    private static bool IsSafeIpAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length switch
        {
            4 => IsSafeIPv4(bytes),
            16 => IsSafeIPv6(bytes),
            _ => false,
        };
    }

    private static bool IsSafeIPv4(byte[] b)
    {
        return (b[0], b[1], b[2]) switch
        {
            (0 or 10 or 127, _, _) => false,
            (>= 224, _, _) => false,
            (100, >= 64 and <= 127, _) => false,
            (169, 254, _) => false,
            (172, >= 16 and <= 31, _) => false,
            (192, 0, 0 or 2) => false,
            (192, 168, _) => false,
            (198, 18 or 19, _) => false,
            (198, 51, 100) => false,
            (203, 0, 113) => false,
            _ => true,
        };
    }

    private static bool IsSafeIPv6(byte[] b)
    {
        if (b.Take(15).All(x => x == 0) && b[15] == 1)
        {
            return false;
        }

        if (b.All(x => x == 0))
        {
            return false;
        }

        if ((b[0] & 0xfe) == 0xfc)
        {
            return false;
        }

        if (b[0] == 0xfe && (b[1] & 0xc0) == 0x80)
        {
            return false;
        }

        return true;
    }

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
