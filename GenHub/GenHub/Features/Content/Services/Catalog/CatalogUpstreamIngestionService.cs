using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Content.Services.GitHub;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Service responsible for autonomously querying upstream providers and hydrating dynamic catalog items.
/// </summary>
public class CatalogUpstreamIngestionService(
    IGitHubApiClient gitHubClient,
    ILogger<CatalogUpstreamIngestionService> logger,
    GeneralsOnlineDiscoverer? generalsOnlineDiscoverer = null,
    CommunityOutpostDiscoverer? communityOutpostDiscoverer = null) : ICatalogUpstreamIngestionService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
    private static readonly ConcurrentDictionary<string, (GitHubRelease Release, DateTime CachedAt)> GitHubReleaseCache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task IngestCatalogAsync(PublisherCatalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (catalog.Content == null || catalog.Content.Count == 0)
        {
            return;
        }

        var discoveryCache = new Dictionary<IContentDiscoverer, OperationResult<ContentDiscoveryResult>>();
        foreach (var item in catalog.Content)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await IngestSingleItemAsync(item, discoveryCache, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to ingest upstream releases for catalog item '{ItemId}'", item.Id);
            }
        }

        // Hydrate bundle releases if empty
        CatalogBundleComponentBuilder.HydrateSyntheticBundleReleases(catalog.Content);
    }

    /// <summary>
    /// Clears the in-memory GitHub release cache (primarily for unit tests).
    /// </summary>
    internal static void ClearReleaseCache() => GitHubReleaseCache.Clear();

    private static string NormalizeReleaseVersion(GitHubRelease release)
    {
        var rawVersion = release.TagName;
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            rawVersion = release.Name ?? "1.0.0";
        }

        var version = rawVersion;
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase) && version.Length > 1 && char.IsDigit(version[1]))
        {
            version = version[1..];
        }

        return version;
    }

    private static bool IsAssetRuleMatch(
        string assetName,
        CatalogUpstreamAssetRule rule,
        ILogger log)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(assetName, rule.Pattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (ArgumentException)
        {
            try
            {
                var convertedGlob = "^" + Regex.Escape(rule.Pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                return Regex.IsMatch(assetName, convertedGlob, RegexOptions.IgnoreCase, RegexTimeout);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Invalid asset rule pattern '{Pattern}'", rule.Pattern);
                return false;
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            log.LogWarning(ex, "Asset rule regex match timed out for pattern '{Pattern}' on asset '{AssetName}'", rule.Pattern, assetName);
            return false;
        }
    }

    private static void PopulateArtifactsFromAssetRules(
        ContentRelease release,
        IEnumerable<GitHubReleaseAsset> assets,
        CatalogUpstreamSync sync,
        ILogger log)
    {
        foreach (var asset in assets)
        {
            var matchedRule = sync.AssetRules.FirstOrDefault(r =>
                IsAssetRuleMatch(asset.Name, r, log));

            if (matchedRule != null)
            {
                release.Artifacts.Add(new ReleaseArtifact
                {
                    Filename = asset.Name,
                    DownloadUrl = asset.BrowserDownloadUrl,
                    Size = asset.Size,
                    Variant = matchedRule.Variant,
                    VariantAxis = sync.VariantAxis ?? CatalogConstants.GameTypeVariantAxis,
                    IsDefaultVariant = matchedRule.IsDefault,
                    IsPrimary = matchedRule.IsDefault,
                    TargetGame = matchedRule.TargetGame,
                });
            }
        }
    }

    private static void PopulateDefaultSuperHackersArtifacts(
        ContentRelease release,
        IEnumerable<GitHubReleaseAsset> assets)
    {
        foreach (var asset in assets)
        {
            if (string.IsNullOrWhiteSpace(asset.Name))
            {
                continue;
            }

            var isFull = asset.Name.Contains(SuperHackersConstants.FullClientAssetMarker, StringComparison.OrdinalIgnoreCase);
            var isZh = !isFull && (asset.Name.Contains(SuperHackersConstants.ZeroHourClientAssetMarker, StringComparison.OrdinalIgnoreCase)
                || SuperHackersAssetMatcher.IsZeroHourAssetName(asset.Name));
            var isGen = !isFull && !isZh && (asset.Name.Contains(SuperHackersConstants.GeneralsClientAssetMarker, StringComparison.OrdinalIgnoreCase)
                || SuperHackersAssetMatcher.IsGeneralsAssetName(asset.Name));

            if (!isZh && !isGen && !isFull)
            {
                continue;
            }

            string variant;
            if (isZh)
            {
                variant = "Zero Hour";
            }
            else if (isGen)
            {
                variant = "Generals";
            }
            else
            {
                variant = "Zero Hour + Generals";
            }

            release.Artifacts.Add(new ReleaseArtifact
            {
                Filename = asset.Name,
                DownloadUrl = asset.BrowserDownloadUrl,
                Size = asset.Size,
                Variant = variant,
                VariantAxis = CatalogConstants.GameTypeVariantAxis,
                IsDefaultVariant = isZh,
                IsPrimary = isZh,
                TargetGame = ResolveArtifactTargetGame(isZh, isGen),
            });
        }
    }

    private static GameType ResolveArtifactTargetGame(bool isZh, bool isGen)
    {
        if (isZh)
        {
            return GameType.ZeroHour;
        }

        if (isGen)
        {
            return GameType.Generals;
        }

        return GameType.Unknown;
    }

    private static void PopulateGenericGitHubArtifacts(
        ContentRelease release,
        IEnumerable<GitHubReleaseAsset> assets)
    {
        var isFirst = true;
        foreach (var asset in assets)
        {
            release.Artifacts.Add(new ReleaseArtifact
            {
                Filename = asset.Name,
                DownloadUrl = asset.BrowserDownloadUrl,
                Size = asset.Size,
                ContentType = asset.ContentType,
                IsPrimary = isFirst,
            });
            isFirst = false;
        }
    }

    private static ContentRelease SynthesizeGitHubRelease(
        GitHubRelease release,
        bool isTrackPrerelease,
        CatalogUpstreamSync? sync,
        string? provider,
        string? repository,
        CatalogContentItem item,
        ILogger logger)
    {
        var version = NormalizeReleaseVersion(release);
        var synthesized = new ContentRelease
        {
            Version = version,
            ReleaseDate = release.PublishedAt?.UtcDateTime ?? DateTime.UtcNow,
            IsLatest = !release.IsPrerelease || isTrackPrerelease,
            IsPrerelease = release.IsPrerelease,
            Changelog = release.Body,
        };

        if (sync?.AssetRules is { Count: > 0 })
        {
            PopulateArtifactsFromAssetRules(synthesized, release.Assets, sync, logger);
        }
        else if (item.ContentType == ContentType.GameClient &&
            IsDefaultSuperHackersRepository(provider, repository))
        {
            // The zh/gen/full-client filename filter only fits game-client items.
            // Other content types tracking the official repo (for example a mod
            // following weekly game-code releases) hydrate every asset instead
            // of matching nothing and keeping zero releases.
            PopulateDefaultSuperHackersArtifacts(synthesized, release.Assets);
        }
        else
        {
            PopulateGenericGitHubArtifacts(synthesized, release.Assets);
        }

        return synthesized;
    }

    private static bool IsDefaultSuperHackersRepository(string? provider, string? repository)
    {
        if (!string.Equals(provider, CatalogConstants.UpstreamProviders.TheSuperHackers, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The zh/gen/full-client filename filter only fits the official game-code repo.
        // Custom repositories under this provider hydrate every release asset instead
        // of silently matching nothing.
        var defaultRepository = CatalogConstants.UpstreamProviders.DefaultSuperHackersRepository;
        return string.IsNullOrWhiteSpace(repository) ||
            string.Equals(repository.Trim(), defaultRepository, StringComparison.OrdinalIgnoreCase);
    }

    private static void AttachEaBaseGameDependency(
        CatalogContentItem item,
        ContentRelease synthesized)
    {
        if (item.ContentType != ContentType.GameClient)
        {
            return;
        }

        var isZhOrGen = item.TargetGame is GameType.Generals or GameType.ZeroHour;

        if (!isZhOrGen)
        {
            return;
        }

        var isGenerals = item.TargetGame == GameType.Generals;
        var baseGameId = isGenerals ? CatalogConstants.GeneralsContentId : CatalogConstants.ZeroHourContentId;
        var baseGameVersion = isGenerals ? ManifestConstants.GeneralsManifestVersion : ManifestConstants.ZeroHourManifestVersion;

        synthesized.Dependencies.Add(new CatalogDependency
        {
            PublisherId = CatalogConstants.EaPublisherId,
            ContentId = baseGameId,
            VersionConstraint = baseGameVersion,
            ContentType = ContentType.GameInstallation.ToString(),
            IsOptional = false,
        });
    }

    private static void PreserveReleaseDependenciesAndMetadata(
        CatalogContentItem item,
        ContentRelease synthesized)
    {
        AttachEaBaseGameDependency(item, synthesized);

        if (item.Releases.Count > 0)
        {
            var firstRel = item.Releases[0];
            if (string.IsNullOrWhiteSpace(synthesized.Changelog))
            {
                synthesized.Changelog = firstRel.Changelog;
            }

            foreach (var dep in (firstRel.Dependencies ?? []).Where(dep =>
                dep != null &&
                !synthesized.Dependencies.Any(d => string.Equals(d.ContentId, dep.ContentId, StringComparison.OrdinalIgnoreCase))))
            {
                synthesized.Dependencies.Add(new CatalogDependency
                {
                    PublisherId = dep.PublisherId,
                    ContentId = dep.ContentId,
                    VersionConstraint = dep.VersionConstraint,
                    ContentType = dep.ContentType,
                    IsOptional = dep.IsOptional,
                    DefinitionUrl = dep.DefinitionUrl,
                });
            }
        }
    }

    private static ContentSearchResult? FindMatchingDiscoveryItem(
        IReadOnlyList<ContentSearchResult> items,
        CatalogContentItem item,
        CatalogUpstreamSync? sync)
    {
        if (!string.IsNullOrWhiteSpace(sync?.ContentCode))
        {
            // An explicit feed key fails closed: never fuzzy-guess when the publisher pinned an entry.
            return items.FirstOrDefault(i => IsContentCodeMatch(i, sync.ContentCode.Trim()));
        }

        var exact = items.FirstOrDefault(i =>
            string.Equals(i.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        exact ??= items.FirstOrDefault(i =>
            string.Equals(i.Name, item.Name, StringComparison.OrdinalIgnoreCase));
        if (exact != null || string.IsNullOrWhiteSpace(item.Name))
        {
            return exact;
        }

        // Containment matching is ambiguous when several discovered items contain the
        // catalog name (or vice versa), so only a single fuzzy candidate is trusted.
        var fuzzy = items.Where(i => !string.IsNullOrWhiteSpace(i.Name) &&
            (item.Name.Contains(i.Name, StringComparison.OrdinalIgnoreCase) || i.Name.Contains(item.Name, StringComparison.OrdinalIgnoreCase)))
            .Take(2)
            .ToList();
        return fuzzy.Count == 1 ? fuzzy[0] : null;
    }

    private static bool IsContentCodeMatch(ContentSearchResult candidate, string contentCode)
    {
        if (candidate.ResolverMetadata.TryGetValue(CommunityOutpostCatalogConstants.ContentCodeKey, out var metadataCode) &&
            string.Equals(metadataCode, contentCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(candidate.Id, contentCode, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Name, contentCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Feed ids are multi-segment paths ending with the content code (e.g. 1.0.communityoutpost.addon.hlei).
        var lastSeparator = candidate.Id?.LastIndexOf('.') ?? -1;
        return lastSeparator >= 0 &&
            string.Equals(candidate.Id?[(lastSeparator + 1)..], contentCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDiscoveryVersion(ContentSearchResult matched)
    {
        // Feed producers always populate Version (GenPatcherDatCatalogParser falls back
        // to DefaultMetadataVersion), so an empty version only means an unknown one.
        if (!string.IsNullOrWhiteSpace(matched.Version))
        {
            return matched.Version;
        }

        return "1.0.0";
    }

    private async Task IngestSingleItemAsync(
        CatalogContentItem item,
        Dictionary<IContentDiscoverer, OperationResult<ContentDiscoveryResult>> discoveryCache,
        CancellationToken cancellationToken)
    {
        var sync = item.UpstreamSync;
        var declaredProvider = CatalogConstants.UpstreamProviders.DeclaredProvider(sync?.Provider, item.PublisherType);
        var provider = CatalogConstants.UpstreamProviders.Normalize(declaredProvider);

        if (string.Equals(provider, CatalogConstants.UpstreamProviders.GitHubReleases, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, CatalogConstants.UpstreamProviders.TheSuperHackers, StringComparison.OrdinalIgnoreCase))
        {
            await IngestGitHubItemAsync(item, sync, provider, cancellationToken);
        }
        else if (string.Equals(provider, CatalogConstants.UpstreamProviders.GeneralsOnline, StringComparison.OrdinalIgnoreCase))
        {
            await IngestFromDiscovererAsync(generalsOnlineDiscoverer, "GeneralsOnline", item, discoveryCache, cancellationToken);
        }
        else if (string.Equals(provider, CatalogConstants.UpstreamProviders.CommunityOutpost, StringComparison.OrdinalIgnoreCase))
        {
            await IngestFromDiscovererAsync(communityOutpostDiscoverer, "CommunityOutpost", item, discoveryCache, cancellationToken);
        }
    }

    private async Task<GitHubRelease?> FetchGitHubReleaseAsync(
        string owner,
        string repoName,
        bool isTrackPrerelease,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        if (GitHubReleaseCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.CachedAt) < CacheTtl)
        {
            return cached.Release;
        }

        GitHubRelease? release;
        if (isTrackPrerelease)
        {
            release = await FetchNewestReleaseAsync(owner, repoName, allowPrerelease: true, cancellationToken);
        }
        else
        {
            release = await gitHubClient.GetLatestReleaseAsync(owner, repoName, cancellationToken);
            release ??= await FetchNewestReleaseAsync(owner, repoName, allowPrerelease: false, cancellationToken);
        }

        if (release != null)
        {
            GitHubReleaseCache[cacheKey] = (release, DateTime.UtcNow);
        }

        return release;
    }

    private async Task<GitHubRelease?> FetchNewestReleaseAsync(
        string owner,
        string repoName,
        bool allowPrerelease,
        CancellationToken cancellationToken)
    {
        var allReleases = await gitHubClient.GetReleasesAsync(owner, repoName, cancellationToken);
        return allReleases?
            .Where(r => !r.IsDraft && (allowPrerelease || !r.IsPrerelease))
            .OrderByDescending(r => r.PublishedAt ?? r.CreatedAt)
            .FirstOrDefault();
    }

    private async Task IngestGitHubItemAsync(
        CatalogContentItem item,
        CatalogUpstreamSync? sync,
        string? provider,
        CancellationToken cancellationToken)
    {
        var repo = sync?.Repository;
        if (string.IsNullOrWhiteSpace(repo))
        {
            repo = CatalogConstants.UpstreamProviders.DefaultSuperHackersRepository;
        }

        var parts = repo.Split('/');
        if (parts.Length != 2)
        {
            logger.LogWarning("Invalid GitHub repository format '{Repo}' on item '{ItemId}'", repo, item.Id);
            return;
        }

        var isTrackPrerelease = CatalogConstants.UpstreamChannels.IsPrereleaseChannel(sync?.Channel);
        var cacheKey = $"{repo}:{sync?.Channel ?? CatalogConstants.UpstreamChannels.Stable}";

        var release = await FetchGitHubReleaseAsync(parts[0], parts[1], isTrackPrerelease, cacheKey, cancellationToken);
        if (release == null)
        {
            logger.LogWarning("No upstream release found for repository '{Repo}' on item '{ItemId}'", repo, item.Id);
            return;
        }

        var synthesized = SynthesizeGitHubRelease(release, isTrackPrerelease, sync, provider, repo, item, logger);
        PreserveReleaseDependenciesAndMetadata(item, synthesized);

        if (synthesized.Artifacts.Count > 0)
        {
            item.Releases.Clear();
            item.Releases.Add(synthesized);
        }
        else
        {
            logger.LogWarning("No artifacts matched upstream release '{Version}' for item '{ItemId}', keeping existing releases", synthesized.Version, item.Id);
        }
    }

    private async Task IngestFromDiscovererAsync(
        IContentDiscoverer? discoverer,
        string providerDisplayName,
        CatalogContentItem item,
        Dictionary<IContentDiscoverer, OperationResult<ContentDiscoveryResult>> discoveryCache,
        CancellationToken cancellationToken)
    {
        if (discoverer == null)
        {
            return;
        }

        if (!discoveryCache.TryGetValue(discoverer, out var discovery))
        {
            discovery = await discoverer.DiscoverAsync(new ContentSearchQuery(), cancellationToken);
            if (discovery.Success && discovery.Data?.Items != null)
            {
                discoveryCache[discoverer] = discovery;
            }
        }

        if (!discovery.Success || discovery.Data?.Items == null)
        {
            return;
        }

        var items = discovery.Data.Items.ToList();
        if (items.Count == 0)
        {
            return;
        }

        var matched = FindMatchingDiscoveryItem(items, item, item.UpstreamSync);
        if (matched == null)
        {
            logger.LogWarning(
                "No discovered item matched catalog item '{ItemId}' (contentCode '{ContentCode}'); keeping existing releases",
                item.Id,
                item.UpstreamSync?.ContentCode);
            return;
        }

        var (downloadUrl, downloadSize) = ResolveDiscoveryDownloadUrl(matched, providerDisplayName, item.Id);
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return;
        }

        var synthesized = new ContentRelease
        {
            Version = ResolveDiscoveryVersion(matched),
            ReleaseDate = matched.LastUpdated ?? DateTime.UtcNow,
            IsLatest = true,
        };

        synthesized.Artifacts.Add(new ReleaseArtifact
        {
            Filename = $"{item.Id}-{synthesized.Version}.zip",
            DownloadUrl = downloadUrl,
            Size = downloadSize,
            IsPrimary = true,
        });

        PreserveReleaseDependenciesAndMetadata(item, synthesized);

        item.Releases.Clear();
        item.Releases.Add(synthesized);
    }

    private (string? DownloadUrl, long DownloadSize) ResolveDiscoveryDownloadUrl(
        ContentSearchResult matched,
        string providerDisplayName,
        string itemId)
    {
        string? downloadUrl = null;
        var downloadSize = matched.DownloadSize;

        if (matched.Data is GeneralsOnlineRelease goRelease)
        {
            downloadUrl = goRelease.PortableUrl;
            if (goRelease.PortableSize.HasValue && goRelease.PortableSize.Value > 0)
            {
                downloadSize = goRelease.PortableSize.Value;
            }
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            downloadUrl = !string.IsNullOrWhiteSpace(matched.SelectedDownloadUrl)
                ? matched.SelectedDownloadUrl
                : matched.SourceUrl;
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            logger.LogWarning("{Provider} item '{ItemId}' has no usable download URL, keeping existing releases", providerDisplayName, itemId);
        }

        return (downloadUrl, downloadSize);
    }
}
