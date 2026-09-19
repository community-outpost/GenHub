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

        PrefetchPageArtwork(pageItems);

        return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
        {
            Items = pageItems.Select(ToSearchResult),
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

    private static string? ResolvePublisherLogo(string? publisherType)
    {
        if (string.IsNullOrWhiteSpace(publisherType))
        {
            return null;
        }

        string logo;
        if (publisherType.Equals(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase))
        {
            logo = PublisherInfoConstants.GeneralsOnline.LogoSource;
        }
        else if (publisherType.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase))
        {
            logo = PublisherInfoConstants.TheSuperHackers.LogoSource;
        }
        else if (publisherType.Equals(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            logo = PublisherInfoConstants.CommunityOutpost.LogoSource;
        }
        else if (publisherType.Equals(ModDBConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            logo = PublisherInfoConstants.ModDB.LogoSource;
        }
        else if (publisherType.Equals(CNCLabsConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            logo = PublisherInfoConstants.CNCLabs.LogoSource;
        }
        else if (publisherType.Equals(GitHubTopicsConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            logo = PublisherInfoConstants.GitHub.LogoSource;
        }
        else if (publisherType.Equals(AODMapsConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            logo = PublisherInfoConstants.AODMaps.LogoSource;
        }
        else if (publisherType.Equals(PublisherTypeConstants.GenLauncher, StringComparison.OrdinalIgnoreCase))
        {
            logo = PublisherInfoConstants.GenLauncher.LogoSource;
        }
        else
        {
            return null;
        }

        return NullIfEmpty(logo);
    }

    private static string? ResolveCoverFallback(ContentManifest manifest)
    {
        if (manifest.ContentType == ContentType.GameClient)
        {
            return manifest.TargetGame == GameType.Generals
                ? ContentArtworkConstants.GeneralsCoverSource
                : ContentArtworkConstants.ZeroHourCoverSource;
        }

        var publisherType = manifest.Publisher?.PublisherType;
        if (string.Equals(publisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase))
        {
            return GeneralsOnlineConstants.CoverSource;
        }

        if (string.Equals(publisherType, CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            return CommunityOutpostConstants.CoverSource;
        }

        return null;
    }

    private void PrefetchPageArtwork(IReadOnlyList<ContentManifest> pageItems)
    {
        var candidates = pageItems
            .Where(manifest => IsRemoteUrl(manifest.Metadata?.IconUrl) || IsRemoteUrl(manifest.Metadata?.CoverUrl))
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            foreach (var manifest in candidates)
            {
                await artworkService.PrefetchArtworkAsync(manifest, CancellationToken.None);
            }
        });
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

    private string? ResolveIconUrl(ContentManifest manifest)
    {
        return artworkService.GetLocalArtworkPath(manifest.Id.Value, ContentArtworkKind.Icon)
            ?? NullIfEmpty(manifest.Metadata?.IconUrl)
            ?? ResolvePublisherLogo(manifest.Publisher?.PublisherType);
    }

    private string? ResolveCoverUrl(ContentManifest manifest)
    {
        return artworkService.GetLocalArtworkPath(manifest.Id.Value, ContentArtworkKind.Cover)
            ?? NullIfEmpty(manifest.Metadata?.CoverUrl)
            ?? ResolveCoverFallback(manifest);
    }
}
