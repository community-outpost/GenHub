using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers already-downloaded content from the local manifest pool.
/// Backs the "My Downloads" sidebar entry in the downloads browser so users can
/// browse, filter, and manage their downloaded content without a network connection.
/// </summary>
/// <param name="manifestPool">The manifest pool holding acquired content.</param>
/// <param name="logger">The logger instance.</param>
public sealed class DownloadedContentDiscoverer(
    IContentManifestPool manifestPool,
    ILogger<DownloadedContentDiscoverer> logger) : IContentDiscoverer
{
    private const int FallbackPageSize = 24;

    /// <inheritdoc />
    public string SourceName => PublisherTypeConstants.Downloaded;

    /// <inheritdoc />
    public string Description => "Browses content already downloaded to local storage.";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.DirectSearch |
        ContentSourceCapabilities.SupportsManifestGeneration;

    /// <inheritdoc />
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var allResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!allResult.Success)
        {
            logger.LogWarning("Failed to read downloaded manifests: {Error}", allResult.FirstError);
            return OperationResult<ContentDiscoveryResult>.CreateFailure(
                $"Failed to read downloaded content: {allResult.FirstError}");
        }

        var matches = (allResult.Data ?? [])
            .Where(manifest => MatchesQuery(manifest, query))
            .OrderBy(manifest => manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(manifest => manifest.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(manifest => manifest.Id.Value, StringComparer.OrdinalIgnoreCase)
            .Select(ToSearchResult)
            .ToList();

        var take = query.Take > 0 ? query.Take : FallbackPageSize;
        var page = query.Page.GetValueOrDefault(1);
        if (page < 1)
        {
            page = 1;
        }

        var skip = Math.Max(0, query.Skip + ((page - 1) * take));
        var pageItems = matches.Skip(skip).Take(take).ToList();

        logger.LogDebug(
            "Downloaded content discovery: {Total} stored, {Returned} returned (page {Page})",
            matches.Count,
            pageItems.Count,
            page);

        return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
        {
            Items = pageItems,
            HasMoreItems = skip + pageItems.Count < matches.Count,
            TotalItems = matches.Count,
        });
    }

    private static bool MatchesQuery(ContentManifest manifest, ContentSearchQuery query)
    {
        if (manifest is null)
        {
            return false;
        }

        // Installation bookkeeping is launcher-managed (metadata-only, deterministically
        // regenerated), never a user download, so it stays out of the library.
        if (manifest.ContentType == ContentType.GameInstallation)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm) &&
            !manifest.Name.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) &&
            !manifest.Id.Value.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.ContentType.HasValue && manifest.ContentType != query.ContentType.Value)
        {
            return false;
        }

        if (query.TargetGame.HasValue &&
            query.TargetGame.Value != GameType.Unknown &&
            manifest.TargetGame != query.TargetGame.Value)
        {
            return false;
        }

        return true;
    }

    private static ContentSearchResult ToSearchResult(ContentManifest manifest)
    {
        var releaseDate = manifest.Metadata?.ReleaseDate;
        var result = new ContentSearchResult
        {
            Id = manifest.Id.Value,
            Name = manifest.Name,
            Description = manifest.Metadata?.Description ?? string.Empty,
            Version = manifest.Version,
            ContentType = manifest.ContentType,
            TargetGame = manifest.TargetGame,
            ProviderName = ResolveProviderName(manifest),
            AuthorName = manifest.Publisher?.Name ?? "Unknown",
            IconUrl = manifest.Metadata?.IconUrl,
            BannerUrl = manifest.Metadata?.CoverUrl,
            LastUpdated = releaseDate is null || releaseDate.Value == default ? null : releaseDate,
            DownloadSize = manifest.Files?.Sum(file => file.Size) ?? 0,
            Data = manifest,
            RequiresResolution = false,
            SourceUrl = manifest.SourcePath,
            VariantGroupId = manifest.Metadata?.VariantGroupId,
            VariantFamilyName = manifest.Metadata?.VariantFamilyName,
        };

        if (manifest.Metadata?.ScreenshotUrls is { Count: > 0 } screenshots)
        {
            foreach (var screenshot in screenshots)
            {
                result.ScreenshotUrls.Add(screenshot);
            }
        }

        if (manifest.Metadata?.Tags is { Count: > 0 } tags)
        {
            foreach (var tag in tags)
            {
                result.Tags.Add(tag);
            }
        }

        return result;
    }

    private static string ResolveProviderName(ContentManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.OriginalProviderName))
        {
            return manifest.OriginalProviderName;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Publisher?.PublisherType))
        {
            return manifest.Publisher.PublisherType;
        }

        return PublisherTypeConstants.Downloaded;
    }
}
