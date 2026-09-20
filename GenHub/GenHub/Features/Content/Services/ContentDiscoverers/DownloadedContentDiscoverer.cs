using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.GeneralsOnline;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Offline discoverer over the local manifest pool ("My Downloads" library).
/// Never touches the network for discovery; artwork bytes are warmed into local
/// storage in the background so cards render offline on later visits.
/// </summary>
/// <param name="manifestPool">The manifest pool holding acquired content.</param>
/// <param name="artworkService">Service resolving and warming local artwork.</param>
/// <param name="logger">The logger instance.</param>
public sealed class DownloadedContentDiscoverer(
    IContentManifestPool manifestPool,
    IContentArtworkService artworkService,
    ILogger<DownloadedContentDiscoverer> logger) : IContentDiscoverer
{
    private const int FallbackPageSize = 24;

    /// <inheritdoc />
    public string SourceName => PublisherTypeConstants.Downloaded;

    /// <inheritdoc />
    public string Description => "Content already stored in the local manifest pool.";

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

        var manifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!manifestsResult.Success || manifestsResult.Data == null)
        {
            logger.LogWarning("Failed to read downloaded manifests: {Error}", manifestsResult.FirstError);
            return OperationResult<ContentDiscoveryResult>.CreateFailure(
                manifestsResult.FirstError ?? "Unable to read downloaded content.");
        }

        var filtered = manifestsResult.Data.Where(manifest => MatchesQuery(manifest, query)).ToList();
        var total = filtered.Count;

        var take = query.Take > 0 ? query.Take : FallbackPageSize;
        var page = query.Page.GetValueOrDefault(1);
        if (page < 1)
        {
            page = 1;
        }

        var skip = Math.Max(0, query.Skip + ((page - 1) * take));
        var pageItems = filtered
            .OrderBy(manifest => manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(manifest => manifest.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(manifest => manifest.Id.Value, StringComparer.OrdinalIgnoreCase)
            .Skip(skip)
            .Take(take)
            .ToList();

        logger.LogDebug(
            "Downloaded content discovery: {Total} stored, {Returned} returned (page {Page})",
            total,
            pageItems.Count,
            page);

        PrefetchPageArtwork(pageItems, cancellationToken);

        // Map defensively: one malformed manifest must not fail the whole library page.
        var items = new List<ContentSearchResult>(pageItems.Count);
        foreach (var manifest in pageItems)
        {
            try
            {
                items.Add(ToSearchResult(manifest));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping downloaded manifest {ManifestId} that failed to map to a search result", manifest.Id.Value);
            }
        }

        return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
        {
            Items = items,
            HasMoreItems = skip + pageItems.Count < total,
            TotalItems = total,
        });
    }

    private static bool MatchesQuery(ContentManifest manifest, ContentSearchQuery query)
    {
        if (manifest is null)
        {
            return false;
        }

        // Launcher-managed manifests (installation bookkeeping and locally detected
        // game clients) are deterministically regenerated, never user downloads,
        // so they stay out of the library.
        if (ManifestHelper.IsLauncherManagedManifest(manifest))
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

        if (query.TargetGame.HasValue && query.TargetGame.Value != GameType.Unknown &&
            manifest.TargetGame != query.TargetGame.Value)
        {
            return false;
        }

        return true;
    }

    private static bool IsRemoteUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

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

    private static string? ResolvePublisherLogo(ContentManifest manifest)
    {
        return NullIfEmpty(PublisherInfoConstants.GetPublisherLogo(
            manifest.Publisher?.PublisherType,
            manifest.Id.Value));
    }

    private static string? ResolveVariantGroupId(ContentManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.Metadata?.VariantGroupId))
        {
            return manifest.Metadata.VariantGroupId;
        }

        // Legacy GeneralsOnline pool entries predate version grouping; derive it so the game
        // client, game data patch, and mappack of one release collapse into one card. Other
        // publishers are untouched: coincidental version equality must never merge them.
        if (GeneralsOnlineVariantGrouping.IsGeneralsOnlineManifest(manifest))
        {
            return GeneralsOnlineVariantGrouping.BuildVariantGroupId(manifest.Version);
        }

        return null;
    }

    private static string? ResolveVariantFamilyName(ContentManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.Metadata?.VariantFamilyName))
        {
            return manifest.Metadata.VariantFamilyName;
        }

        if (GeneralsOnlineVariantGrouping.IsGeneralsOnlineManifest(manifest))
        {
            return GeneralsOnlineVariantGrouping.BuildVariantFamilyName(manifest.Version);
        }

        return null;
    }

    private static string? ResolveCoverFallback(ContentManifest manifest)
    {
        if (manifest.ContentType == ContentType.GameClient)
        {
            return manifest.TargetGame == GameType.Generals
                ? ContentArtworkConstants.GeneralsCoverSource
                : ContentArtworkConstants.ZeroHourCoverSource;
        }

        return PublisherInfoConstants.GetPublisherCover(
            manifest.Publisher?.PublisherType,
            manifest.Id.Value);
    }

    private void PrefetchPageArtwork(IReadOnlyList<ContentManifest> pageItems, CancellationToken cancellationToken)
    {
        var candidates = pageItems
            .Where(manifest => IsRemoteUrl(manifest.Metadata?.IconUrl) || IsRemoteUrl(manifest.Metadata?.CoverUrl))
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        _ = Task.Run(
            async () =>
            {
                try
                {
                    foreach (var manifest in candidates)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await artworkService.PrefetchArtworkAsync(manifest, cancellationToken);
                    }
                }
                catch (OperationCanceledException ex)
                {
                    logger.LogDebug(ex, "Artwork prefetch cancelled for {Count} candidates", candidates.Count);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Background artwork prefetch failed for {Count} candidates", candidates.Count);
                }
            },
            cancellationToken);
    }

    private ContentSearchResult ToSearchResult(ContentManifest manifest)
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
            IconUrl = ResolveIconUrl(manifest),
            BannerUrl = ResolveCoverUrl(manifest),
            LastUpdated = releaseDate is null || releaseDate.Value == default ? null : releaseDate,
            DownloadSize = manifest.Files?.Sum(file => file.Size) ?? 0,
            Data = manifest,
            RequiresResolution = false,
            SourceUrl = manifest.SourcePath,
            VariantGroupId = ResolveVariantGroupId(manifest),
            VariantFamilyName = ResolveVariantFamilyName(manifest),
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

    private string? ResolveIconUrl(ContentManifest manifest)
    {
        return artworkService.GetLocalArtworkPath(manifest.Id.Value, ContentArtworkKind.Icon)
            ?? NullIfEmpty(manifest.Metadata?.IconUrl)
            ?? ResolvePublisherLogo(manifest);
    }

    private string? ResolveCoverUrl(ContentManifest manifest)
    {
        return artworkService.GetLocalArtworkPath(manifest.Id.Value, ContentArtworkKind.Cover)
            ?? NullIfEmpty(manifest.Metadata?.CoverUrl)
            ?? ResolveCoverFallback(manifest);
    }
}
