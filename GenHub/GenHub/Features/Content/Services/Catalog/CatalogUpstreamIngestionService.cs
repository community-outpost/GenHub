using GenHub.Core.Constants;
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

    private async Task IngestSingleItemAsync(CatalogContentItem item, CancellationToken cancellationToken)
    {
        var sync = item.UpstreamSync;
        var provider = sync?.Provider ?? item.PublisherType ?? string.Empty;

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

        var synthesized = new ContentRelease
        {
            Version = version,
            ReleaseDate = release.PublishedAt?.UtcDateTime ?? DateTime.UtcNow,
            IsLatest = !release.IsPrerelease,
            IsPrerelease = release.IsPrerelease,
            Changelog = release.Body,
        };

        if (sync?.AssetRules is { Count: > 0 })
        {
            foreach (var asset in release.Assets)
            {
                var matchedRule = sync.AssetRules.FirstOrDefault(r =>
                    Regex.IsMatch(asset.Name, r.Pattern, RegexOptions.IgnoreCase) &&
                    (r.TargetGame == GameType.Unknown || item.TargetGame == GameType.Unknown || r.TargetGame == item.TargetGame));

                if (matchedRule != null)
                {
                    synthesized.Artifacts.Add(new ReleaseArtifact
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
        else
        {
            // Default SuperHackers mapping if no rules provided
            foreach (var asset in release.Assets)
            {
                var isZh = asset.Name.Contains("zh-client", StringComparison.OrdinalIgnoreCase);
                var isGen = asset.Name.Contains("gen-client", StringComparison.OrdinalIgnoreCase);
                var isFull = asset.Name.Contains("full-client", StringComparison.OrdinalIgnoreCase);

                if (isZh || isGen || isFull)
                {
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

                    synthesized.Artifacts.Add(new ReleaseArtifact
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
        }

        if (synthesized.Artifacts.Count > 0)
        {
            item.Releases.Clear();
            item.Releases.Add(synthesized);
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
            i.TargetGame == item.TargetGame ||
            string.Equals(i.Name, item.Name, StringComparison.OrdinalIgnoreCase)) ?? items[0];

        var synthesized = new ContentRelease
        {
            Version = !string.IsNullOrWhiteSpace(matched.Version) ? matched.Version : "1.0.0",
            ReleaseDate = matched.LastUpdated ?? DateTime.UtcNow,
            IsLatest = true,
        };

        synthesized.Artifacts.Add(new ReleaseArtifact
        {
            Filename = $"{item.Id}-{synthesized.Version}.zip",
            DownloadUrl = matched.SelectedDownloadUrl ?? matched.SourceUrl ?? string.Empty,
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
            i.TargetGame == item.TargetGame ||
            string.Equals(i.Name, item.Name, StringComparison.OrdinalIgnoreCase)) ?? items[0];

        var synthesized = new ContentRelease
        {
            Version = !string.IsNullOrWhiteSpace(matched.Version) ? matched.Version : "1.0.0",
            ReleaseDate = matched.LastUpdated ?? DateTime.UtcNow,
            IsLatest = true,
        };

        synthesized.Artifacts.Add(new ReleaseArtifact
        {
            Filename = $"{item.Id}-{synthesized.Version}.zip",
            DownloadUrl = matched.SelectedDownloadUrl ?? matched.SourceUrl ?? string.Empty,
            Size = matched.DownloadSize,
            IsPrimary = true,
        });

        item.Releases.Clear();
        item.Releases.Add(synthesized);
    }
}
