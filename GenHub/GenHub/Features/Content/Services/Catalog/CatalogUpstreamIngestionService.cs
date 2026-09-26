using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Content.Services.GitHub;
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

        foreach (var item in catalog.Content)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await IngestSingleItemAsync(item, cancellationToken);
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
                    VariantAxis = sync.VariantAxis ?? "game-type",
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
            var isZh = asset.Name.Contains("zh-client", StringComparison.OrdinalIgnoreCase);
            var isGen = asset.Name.Contains("gen-client", StringComparison.OrdinalIgnoreCase);
            var isFull = asset.Name.Contains("full-client", StringComparison.OrdinalIgnoreCase);

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
                VariantAxis = "game-type",
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

    private static bool IsTrackPrereleaseChannel(string? channel) =>
        string.Equals(channel, "prerelease", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(channel, "nightly", StringComparison.OrdinalIgnoreCase);

    private static ContentRelease SynthesizeGitHubRelease(
        GitHubRelease release,
        bool isTrackPrerelease,
        CatalogUpstreamSync? sync,
        string? provider,
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
        else if (string.Equals(provider, CatalogConstants.UpstreamProviders.TheSuperHackers, StringComparison.OrdinalIgnoreCase))
        {
            PopulateDefaultSuperHackersArtifacts(synthesized, release.Assets);
        }
        else
        {
            PopulateGenericGitHubArtifacts(synthesized, release.Assets);
        }

        return synthesized;
    }

    private static void AttachEaBaseGameDependency(
        CatalogContentItem item,
        ContentRelease synthesized,
        string? provider)
    {
        if (item.ContentType != ContentType.GameClient)
        {
            return;
        }

        var isZhOrGen = item.TargetGame is GameType.Generals or GameType.ZeroHour;
        var isSuperHackers = string.Equals(provider, CatalogConstants.UpstreamProviders.TheSuperHackers, StringComparison.OrdinalIgnoreCase);

        if (!isZhOrGen && !isSuperHackers)
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

    private async Task IngestSingleItemAsync(CatalogContentItem item, CancellationToken cancellationToken)
    {
        var sync = item.UpstreamSync;
        var declaredProvider = !string.IsNullOrWhiteSpace(sync?.Provider) ? sync.Provider : item.PublisherType;
        var provider = CatalogConstants.UpstreamProviders.Normalize(declaredProvider);

        if (string.Equals(provider, CatalogConstants.UpstreamProviders.GitHubReleases, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, CatalogConstants.UpstreamProviders.TheSuperHackers, StringComparison.OrdinalIgnoreCase))
        {
            await IngestGitHubItemAsync(item, sync, provider, cancellationToken);
        }
        else if (string.Equals(provider, CatalogConstants.UpstreamProviders.GeneralsOnline, StringComparison.OrdinalIgnoreCase))
        {
            await IngestFromDiscovererAsync(generalsOnlineDiscoverer, "GeneralsOnline", item, cancellationToken);
        }
        else if (string.Equals(provider, CatalogConstants.UpstreamProviders.CommunityOutpost, StringComparison.OrdinalIgnoreCase))
        {
            await IngestFromDiscovererAsync(communityOutpostDiscoverer, "CommunityOutpost", item, cancellationToken);
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
            release = await FetchNewestReleaseAsync(owner, repoName, cancellationToken);
        }
        else
        {
            release = await gitHubClient.GetLatestReleaseAsync(owner, repoName, cancellationToken);
            release ??= await FetchNewestReleaseAsync(owner, repoName, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var allReleases = await gitHubClient.GetReleasesAsync(owner, repoName, cancellationToken);
        return allReleases?
            .Where(r => !r.IsDraft)
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
            repo = $"{SuperHackersConstants.GeneralsGameCodeOwner}/{SuperHackersConstants.GeneralsGameCodeRepo}";
        }

        var parts = repo.Split('/');
        if (parts.Length != 2)
        {
            logger.LogWarning("Invalid GitHub repository format '{Repo}' on item '{ItemId}'", repo, item.Id);
            return;
        }

        var isTrackPrerelease = IsTrackPrereleaseChannel(sync?.Channel);
        var cacheKey = $"{repo}:{sync?.Channel ?? "stable"}";

        var release = await FetchGitHubReleaseAsync(parts[0], parts[1], isTrackPrerelease, cacheKey, cancellationToken);
        if (release == null)
        {
            return;
        }

        var synthesized = SynthesizeGitHubRelease(release, isTrackPrerelease, sync, provider, logger);
        AttachEaBaseGameDependency(item, synthesized, provider);

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
        CancellationToken cancellationToken)
    {
        if (discoverer == null)
        {
            return;
        }

        var discovery = await discoverer.DiscoverAsync(new ContentSearchQuery(), cancellationToken);
        if (!discovery.Success || discovery.Data?.Items == null)
        {
            return;
        }

        var items = discovery.Data.Items.ToList();
        if (items.Count == 0)
        {
            return;
        }

        var matched = items.FirstOrDefault(i =>
            string.Equals(i.Id, item.Id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i.Name, item.Name, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(i.Name) &&
             (item.Name.Contains(i.Name, StringComparison.OrdinalIgnoreCase) || i.Name.Contains(item.Name, StringComparison.OrdinalIgnoreCase))));

        if (matched == null)
        {
            return;
        }

        var downloadUrl = matched.SelectedDownloadUrl ?? matched.SourceUrl;
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            logger.LogWarning("{Provider} item '{ItemId}' has no usable download URL, keeping existing releases", providerDisplayName, item.Id);
            return;
        }

        var synthesized = new ContentRelease
        {
            Version = !string.IsNullOrWhiteSpace(matched.Version) ? matched.Version : "1.0.0",
            ReleaseDate = matched.LastUpdated ?? DateTime.UtcNow,
            IsLatest = true,
        };

        synthesized.Artifacts.Add(new ReleaseArtifact
        {
            Filename = $"{item.Id}-{synthesized.Version}.zip",
            DownloadUrl = downloadUrl,
            Size = matched.DownloadSize,
            IsPrimary = true,
        });

        item.Releases.Clear();
        item.Releases.Add(synthesized);
    }
}
