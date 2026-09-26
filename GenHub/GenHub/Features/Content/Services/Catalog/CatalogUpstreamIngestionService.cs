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
        GameType itemTargetGame,
        ILogger log)
    {
        if (rule.TargetGame != GameType.Unknown && itemTargetGame != GameType.Unknown && rule.TargetGame != itemTargetGame)
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(assetName, rule.Pattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            log.LogWarning(ex, "Invalid asset rule regex pattern '{Pattern}'", rule.Pattern);
            return false;
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
        CatalogContentItem item,
        ILogger log)
    {
        foreach (var asset in assets)
        {
            var matchedRule = sync.AssetRules.FirstOrDefault(r =>
                IsAssetRuleMatch(asset.Name, r, item.TargetGame, log));

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
            });
        }
    }

    private async Task IngestSingleItemAsync(CatalogContentItem item, CancellationToken cancellationToken)
    {
        var sync = item.UpstreamSync;
        var provider = CatalogConstants.UpstreamProviders.Normalize(sync?.Provider ?? item.PublisherType);

        if (string.Equals(provider, CatalogConstants.UpstreamProviders.GitHubReleases, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, CatalogConstants.UpstreamProviders.TheSuperHackers, StringComparison.OrdinalIgnoreCase))
        {
            await IngestGitHubItemAsync(item, sync, cancellationToken);
        }
        else if (string.Equals(provider, CatalogConstants.UpstreamProviders.GeneralsOnline, StringComparison.OrdinalIgnoreCase))
        {
            await IngestGeneralsOnlineItemAsync(item, cancellationToken);
        }
        else if (string.Equals(provider, CatalogConstants.UpstreamProviders.CommunityOutpost, StringComparison.OrdinalIgnoreCase))
        {
            await IngestCommunityOutpostItemAsync(item, cancellationToken);
        }
    }

    private async Task IngestGitHubItemAsync(
        CatalogContentItem item,
        CatalogUpstreamSync? sync,
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

        var release = await gitHubClient.GetLatestReleaseAsync(parts[0], parts[1], cancellationToken);
        if (release == null)
        {
            return;
        }

        var version = NormalizeReleaseVersion(release);
        var synthesized = new ContentRelease
        {
            Version = version,
            ReleaseDate = release.PublishedAt?.UtcDateTime ?? DateTime.UtcNow,
            IsLatest = !release.IsPrerelease || string.Equals(sync?.Channel, "prerelease", StringComparison.OrdinalIgnoreCase),
            IsPrerelease = release.IsPrerelease,
            Changelog = release.Body,
        };

        if (sync?.AssetRules is { Count: > 0 })
        {
            PopulateArtifactsFromAssetRules(synthesized, release.Assets, sync, item, logger);
        }
        else
        {
            PopulateDefaultSuperHackersArtifacts(synthesized, release.Assets);
        }

        if (synthesized.Artifacts.Count > 0)
        {
            item.Releases.Clear();
            item.Releases.Add(synthesized);
        }
        else
        {
            logger.LogWarning("No artifacts matched upstream release '{Version}' for item '{ItemId}', keeping existing releases", version, item.Id);
        }
    }

    private async Task IngestGeneralsOnlineItemAsync(
        CatalogContentItem item,
        CancellationToken cancellationToken)
    {
        if (generalsOnlineDiscoverer == null)
        {
            return;
        }

        var discovery = await generalsOnlineDiscoverer.DiscoverAsync(new ContentSearchQuery(), cancellationToken);
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
            string.Equals(i.Name, item.Name, StringComparison.OrdinalIgnoreCase)) ??
            items.FirstOrDefault(i => item.TargetGame != GameType.Unknown && i.TargetGame == item.TargetGame);

        if (matched == null)
        {
            return;
        }

        var downloadUrl = matched.SelectedDownloadUrl ?? matched.SourceUrl;
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            logger.LogWarning("GeneralsOnline item '{ItemId}' has no usable download URL, keeping existing releases", item.Id);
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

    private async Task IngestCommunityOutpostItemAsync(
        CatalogContentItem item,
        CancellationToken cancellationToken)
    {
        if (communityOutpostDiscoverer == null)
        {
            return;
        }

        var discovery = await communityOutpostDiscoverer.DiscoverAsync(new ContentSearchQuery(), cancellationToken);
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
            string.Equals(i.Name, item.Name, StringComparison.OrdinalIgnoreCase)) ??
            items.FirstOrDefault(i => item.TargetGame != GameType.Unknown && i.TargetGame == item.TargetGame);

        if (matched == null)
        {
            return;
        }

        var downloadUrl = matched.SelectedDownloadUrl ?? matched.SourceUrl;
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            logger.LogWarning("CommunityOutpost item '{ItemId}' has no usable download URL, keeping existing releases", item.Id);
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
