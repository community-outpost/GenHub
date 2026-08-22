using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Discovers content from any GenHub-schema <see cref="PublisherCatalog"/> pointed at by a
/// <see cref="Core.Models.Providers.PublisherSubscription"/>.
/// </summary>
/// <remarks>
/// One transient instance is configured per subscription (see Downloads sidebar). This is the
/// modular path that lets creators publish <c>catalog.json</c> without a custom discoverer class.
/// Built-in providers (GeneralsOnline, ModDB, …) keep their specialized discoverers; this class
/// covers user-subscribed catalogs and future definition-resolved catalog endpoints.
/// </remarks>
public class GenericCatalogDiscoverer(
    ILogger<GenericCatalogDiscoverer> logger,
    IHttpClientFactory httpClientFactory,
    IPublisherCatalogParser catalogParser,
    IVersionSelector versionSelector,
    IGitHubApiClient gitHubClient) : IContentDiscoverer
{
    private static readonly ConcurrentDictionary<string, (GitHubRelease Release, DateTime CachedAt)> ReleaseCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<GitHubRelease?>> PendingReleaseFetches = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private PublisherCatalog? _cachedCatalog;
    private Core.Models.Providers.PublisherSubscription? _subscription;

    /// <summary>
    /// Gets the unique identifier of the resolver used by this discoverer.
    /// </summary>
    public static string ResolverId => CatalogConstants.GenericCatalogResolverId;

    /// <summary>
    /// Clears the static dynamic release cache. Used for testing and cache invalidation.
    /// </summary>
    internal static void ClearReleaseCache()
    {
        ReleaseCache.Clear();
        PendingReleaseFetches.Clear();
    }

    private static bool MatchesQuery(CatalogContentItem content, ContentSearchQuery query, ContentRelease? release = null)
    {
        // Filter component-only catalog items from main grid display
        if (!content.IsStandalone)
        {
            return false;
        }

        // Filter by game type (allowing game-type variant artifacts to match)
        if (query.TargetGame.HasValue)
        {
            var targetGame = query.TargetGame.Value;
            var hasGameVariant = release?.Artifacts?.Any(a =>
                string.Equals(a.VariantAxis, "game-type", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(a.Variant, "generals", StringComparison.OrdinalIgnoreCase) ? GameType.Generals : GameType.ZeroHour) == targetGame) == true;

            if (content.TargetGame != targetGame && !hasGameVariant)
            {
                return false;
            }
        }

        // Filter by content type
        if (query.ContentType.HasValue && content.ContentType != query.ContentType.Value)
        {
            return false;
        }

        // Filter by search text
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var searchLower = query.SearchTerm.ToLowerInvariant();
            if (!content.Name.Contains(searchLower, StringComparison.OrdinalIgnoreCase) &&
                !content.Description.Contains(searchLower, StringComparison.OrdinalIgnoreCase) &&
                content.Tags.All(t => !t.Contains(searchLower, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Detects whether a release carries per-artifact variant hints. Returns the artifacts that
    /// declare a non-empty <see cref="ReleaseArtifact.VariantAxis"/> grouped by axis, when at
    /// least one axis has two or more artifacts. An empty list means the release should NOT be
    /// split (single card, original path).
    /// </summary>
    private static List<ReleaseArtifact> GetVariantArtifacts(ContentRelease release)
    {
        if (release.Artifacts == null || release.Artifacts.Count == 0)
        {
            return [];
        }

        var hinted = release.Artifacts
            .Where(a => !string.IsNullOrWhiteSpace(a.VariantAxis) && !string.IsNullOrWhiteSpace(a.Variant))
            .ToList();

        if (hinted.Count < 2)
        {
            return [];
        }

        var multiAxes = hinted
            .GroupBy(a => a.VariantAxis!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (multiAxes.Count == 0)
        {
            return [];
        }

        return hinted.Where(a => multiAxes.Contains(a.VariantAxis!)).ToList();
    }

    /// <summary>
    /// Guarantees exactly one variant is marked default. If the author declared one via
    /// <see cref="ReleaseArtifact.IsDefaultVariant"/> that is preserved; otherwise prefer a
    /// 1080p resolution variant, then any resolution variant, then the first option.
    /// </summary>
    private static void MarkDefaultVariant(List<ContentVariantInfo> variants)
    {
        if (variants.Count == 0)
        {
            return;
        }

        if (variants.Any(v => v.IsDefault))
        {
            // Keep the first author-declared default, clear the rest.
            var seen = false;
            foreach (var v in variants)
            {
                if (v.IsDefault && !seen)
                {
                    seen = true;
                }
                else
                {
                    v.IsDefault = false;
                }
            }

            return;
        }

        var chosen = variants.FirstOrDefault(v =>
                       v.Name.Contains("1080p", StringComparison.OrdinalIgnoreCase) ||
                       v.Name.Contains("1920x1080", StringComparison.OrdinalIgnoreCase))
                   ?? variants.FirstOrDefault(v => v.VariantType == "resolution")
                   ?? variants[0];
        chosen.IsDefault = true;
    }

    private static void AttachResolverMetadata(
        ContentSearchResult searchResult,
        PublisherCatalog catalog,
        CatalogContentItem contentItem,
        ContentRelease release)
    {
        searchResult.ResolverMetadata[CatalogConstants.CatalogItemJsonMetadataKey] = JsonSerializer.Serialize(contentItem);
        searchResult.ResolverMetadata[CatalogConstants.ReleaseJsonMetadataKey] = JsonSerializer.Serialize(release);
        searchResult.ResolverMetadata[CatalogConstants.PublisherProfileJsonMetadataKey] = JsonSerializer.Serialize(catalog.Publisher);
        searchResult.ResolverMetadata[CatalogConstants.CatalogContentIdMetadataKey] = contentItem.Id;

        if (catalog.Referrals is { Count: > 0 })
        {
            searchResult.ResolverMetadata[CatalogConstants.CatalogReferralsJsonMetadataKey] = JsonSerializer.Serialize(catalog.Referrals);
        }
    }

    private static IReadOnlyList<string> ResolveIncludedContentNames(
        ContentRelease release,
        IReadOnlyDictionary<string, string> contentNamesById)
    {
        var names = new List<string>();
        foreach (var dependency in release.Dependencies)
        {
            if (dependency.IsOptional ||
                string.IsNullOrWhiteSpace(dependency.ContentId) ||
                CatalogManifestIdentity.IsBaseGameDependency(dependency))
            {
                continue;
            }

            if (contentNamesById.TryGetValue(dependency.ContentId, out var catalogName) &&
                !string.IsNullOrWhiteSpace(catalogName))
            {
                names.Add(catalogName);
                continue;
            }

            names.Add(CatalogManifestIdentity.HumanizeContentId(dependency.ContentId));
        }

        return names;
    }

    private static GameType ResolveSiblingTargetGame(GameType defaultTargetGame, string axis, string variantLabel)
    {
        if (axis.Equals("game-type", StringComparison.OrdinalIgnoreCase))
        {
            if (variantLabel.Equals("Generals", StringComparison.OrdinalIgnoreCase))
            {
                return GameType.Generals;
            }

            if (variantLabel.Equals("Zero Hour", StringComparison.OrdinalIgnoreCase) ||
                variantLabel.Equals("ZeroHour", StringComparison.OrdinalIgnoreCase))
            {
                return GameType.ZeroHour;
            }
        }

        return defaultTargetGame;
    }

    private static List<ReleaseArtifact> BuildSiblingArtifacts(ReleaseArtifact artifact, ContentRelease resolvedRelease)
    {
        var siblingArtifacts = new List<ReleaseArtifact>
        {
            new ReleaseArtifact
            {
                Filename = artifact.Filename,
                DownloadUrl = artifact.DownloadUrl,
                Size = artifact.Size,
                Sha256 = artifact.Sha256,
                ContentType = artifact.ContentType,
                IsPrimary = true,
                VariantAxis = artifact.VariantAxis,
                Variant = artifact.Variant,
                IsDefaultVariant = artifact.IsDefaultVariant,
            },
        };

        if (resolvedRelease.Artifacts != null)
        {
            var otherAxisGroups = resolvedRelease.Artifacts
                .Where(a => !string.Equals(a.VariantAxis, artifact.VariantAxis, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(a.VariantAxis))
                .GroupBy(a => a.VariantAxis ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            foreach (var group in otherAxisGroups)
            {
                var defaultOther = group.FirstOrDefault(a => a.IsDefaultVariant) ?? group.First();
                siblingArtifacts.Add(new ReleaseArtifact
                {
                    Filename = defaultOther.Filename,
                    DownloadUrl = defaultOther.DownloadUrl,
                    Size = defaultOther.Size,
                    Sha256 = defaultOther.Sha256,
                    ContentType = defaultOther.ContentType,
                    IsPrimary = false,
                    VariantAxis = defaultOther.VariantAxis,
                    Variant = defaultOther.Variant,
                    IsDefaultVariant = defaultOther.IsDefaultVariant,
                });
            }

            foreach (var nonVariant in resolvedRelease.Artifacts.Where(a => string.IsNullOrWhiteSpace(a.VariantAxis)))
            {
                siblingArtifacts.Add(new ReleaseArtifact
                {
                    Filename = nonVariant.Filename,
                    DownloadUrl = nonVariant.DownloadUrl,
                    Size = nonVariant.Size,
                    Sha256 = nonVariant.Sha256,
                    ContentType = nonVariant.ContentType,
                    IsPrimary = false,
                    VariantAxis = nonVariant.VariantAxis,
                    Variant = nonVariant.Variant,
                    IsDefaultVariant = nonVariant.IsDefaultVariant,
                });
            }
        }

        return siblingArtifacts;
    }

    /// <inheritdoc />
    public string SourceName => _subscription?.PublisherName ?? "Generic Catalog";

    /// <inheritdoc />
    public string Description => _subscription != null
        ? $"Content from {_subscription.PublisherName}"
        : "Generic catalog-based content source";

    /// <inheritdoc />
    public bool IsEnabled => _subscription != null;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery | ContentSourceCapabilities.SupportsManifestGeneration;

    /// <summary>
    /// Configures this discoverer for a specific publisher subscription.
    /// </summary>
    /// <param name="subscription">The publisher subscription.</param>
    public void Configure(Core.Models.Providers.PublisherSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _subscription = subscription;
        logger.LogDebug("Configured discoverer for publisher: {PublisherId}", subscription.PublisherId);
    }

    /// <inheritdoc />
    public virtual async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (_subscription == null)
        {
            return OperationResult<ContentDiscoveryResult>.CreateFailure(
                "Discoverer not configured with subscription");
        }

        try
        {
            // Fetch and parse catalog
            var catalogResult = await FetchCatalogAsync(cancellationToken);
            if (!catalogResult.Success)
            {
                return OperationResult<ContentDiscoveryResult>.CreateFailure(catalogResult);
            }

            var catalog = catalogResult.Data!;
            _cachedCatalog = catalog;

            // Dynamically hydrate upstream releases (e.g. TheSuperHackers latest release)
            await HydrateDynamicReleasesAsync(catalog, cancellationToken);

            // Convert catalog items to search results
            var searchResults = ConvertCatalogToSearchResults(catalog, query).ToList();

            var result = new ContentDiscoveryResult
            {
                Items = searchResults,
                TotalItems = searchResults.Count,
                HasMoreItems = false, // All results returned at once from catalog
            };

            logger.LogInformation(
                "Discovered {Count} content items from publisher '{PublisherId}'",
                searchResults.Count,
                _subscription.PublisherId);

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(result);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(ex, "Content discovery cancelled by user for publisher '{PublisherId}'", _subscription.PublisherId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover content from publisher '{PublisherId}'", _subscription.PublisherId);
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

    private async Task HydrateDynamicReleasesAsync(
        PublisherCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (catalog.Content == null || catalog.Content.Count == 0)
        {
            return;
        }

        var dynamicItems = catalog.Content
            .Where(item => item.PublisherType?.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        if (dynamicItems.Count == 0)
        {
            return;
        }

        GitHubRelease? latestRelease = null;
        try
        {
            var cacheKey = $"{SuperHackersConstants.GeneralsGameCodeOwner}/{SuperHackersConstants.GeneralsGameCodeRepo}";
            if (ReleaseCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
            {
                latestRelease = cached.Release;
            }
            else
            {
                var fetchTask = PendingReleaseFetches.GetOrAdd(cacheKey, _ => FetchAndCacheReleaseAsync(
                    SuperHackersConstants.GeneralsGameCodeOwner,
                    SuperHackersConstants.GeneralsGameCodeRepo,
                    cacheKey));

                latestRelease = await fetchTask.WaitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch latest release from GitHub for SuperHackers dynamic catalog hydration");
        }

        if (latestRelease == null)
        {
            logger.LogWarning("Latest release could not be resolved from GitHub; skipping dynamic hydration for SuperHackers");
            return;
        }

        var cleanTag = latestRelease.TagName.TrimStart('v', 'V');
        var zhAsset = latestRelease.Assets?.FirstOrDefault(a =>
            a.Name.Contains("generalszh", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Contains("zerohour", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Contains("zero-hour", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Contains("_zh", StringComparison.OrdinalIgnoreCase));

        var genAsset = latestRelease.Assets?.FirstOrDefault(a =>
            a.Name.Contains("generals", StringComparison.OrdinalIgnoreCase) &&
            !a.Name.Contains("generalszh", StringComparison.OrdinalIgnoreCase) &&
            !a.Name.Contains("zerohour", StringComparison.OrdinalIgnoreCase) &&
            !a.Name.Contains("zero-hour", StringComparison.OrdinalIgnoreCase) &&
            !a.Name.Contains("_zh", StringComparison.OrdinalIgnoreCase));

        var hydratedItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in dynamicItems)
        {
            hydratedItemIds.Add(item.Id);
            var artifacts = new List<ReleaseArtifact>();

            if (zhAsset != null)
            {
                artifacts.Add(new ReleaseArtifact
                {
                    Filename = zhAsset.Name,
                    DownloadUrl = zhAsset.BrowserDownloadUrl,
                    Size = zhAsset.Size,
                    ContentType = "application/zip",
                    VariantAxis = "game-type",
                    Variant = "Zero Hour",
                    IsDefaultVariant = item.TargetGame == GameType.ZeroHour,
                    IsPrimary = item.TargetGame == GameType.ZeroHour,
                });
            }

            if (genAsset != null)
            {
                artifacts.Add(new ReleaseArtifact
                {
                    Filename = genAsset.Name,
                    DownloadUrl = genAsset.BrowserDownloadUrl,
                    Size = genAsset.Size,
                    ContentType = "application/zip",
                    VariantAxis = "game-type",
                    Variant = "Generals",
                    IsDefaultVariant = item.TargetGame == GameType.Generals,
                    IsPrimary = item.TargetGame == GameType.Generals,
                });
            }

            item.Releases =
            [
                new ContentRelease
                {
                    Version = cleanTag,
                    ReleaseDate = latestRelease.PublishedAt?.UtcDateTime ?? DateTime.UtcNow,
                    IsLatest = true,
                    IsPrerelease = false,
                    Changelog = latestRelease.Body ?? string.Empty,
                    Artifacts = artifacts,
                    Dependencies =
                    [
                        new CatalogDependency
                        {
                            PublisherId = "ea",
                            ContentId = item.TargetGame == GameType.Generals ? "generals" : "zerohour",
                            VersionConstraint = item.TargetGame == GameType.Generals ? "1.08" : "1.04",
                            ContentType = ContentType.GameInstallation.ToString(),
                            IsOptional = false,
                        },
                    ],
                },
            ];
        }

        // Also synchronize any ContentBundle dependencies targeting the hydrated sibling items
        foreach (var bundle in catalog.Content.Where(c => c.ContentType == ContentType.ContentBundle && c.Releases != null))
        {
            foreach (var release in bundle.Releases)
            {
                if (release.Dependencies == null) continue;
                foreach (var dep in release.Dependencies)
                {
                    if (!string.IsNullOrWhiteSpace(dep.ContentId) &&
                        (hydratedItemIds.Contains(dep.ContentId) ||
                         (dep.PublisherId?.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) == true &&
                          dep.VersionConstraint?.Equals("latest", StringComparison.OrdinalIgnoreCase) == true)))
                    {
                        dep.VersionConstraint = $">={cleanTag}";
                    }
                }
            }
        }
    }

    private async Task<GitHubRelease?> FetchAndCacheReleaseAsync(string owner, string repo, string cacheKey)
    {
        try
        {
            var release = await gitHubClient.GetLatestReleaseAsync(owner, repo, CancellationToken.None);
            if (release != null)
            {
                ReleaseCache[cacheKey] = (release, DateTime.UtcNow);
            }

            return release;
        }
        finally
        {
            PendingReleaseFetches.TryRemove(cacheKey, out _);
        }
    }

    private async Task<OperationResult<PublisherCatalog>> FetchCatalogAsync(CancellationToken cancellationToken)
    {
        if (_subscription == null)
        {
            return OperationResult<PublisherCatalog>.CreateFailure("Subscription is not configured");
        }

        try
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            if (!string.IsNullOrWhiteSpace(_subscription.DefinitionUrl))
            {
                throw new NotSupportedException("Definition-resolved catalogs (Publisher Studio) are not yet supported. Only direct CatalogUrl subscriptions are supported.");
            }

            logger.LogDebug("Fetching catalog from: {CatalogUrl}", _subscription.CatalogUrl);

            var catalogJson = await CatalogDocumentReader.ReadAsync(
                httpClient,
                _subscription.CatalogUrl,
                CatalogConstants.MaxCatalogSizeBytes,
                cancellationToken);

            // Parse catalog
            return await catalogParser.ParseCatalogAsync(catalogJson, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error fetching catalog");
            return OperationResult<PublisherCatalog>.CreateFailure($"Failed to fetch catalog: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Catalog fetch cancelled by user");
                throw new OperationCanceledException("Catalog fetch cancelled by user", ex, cancellationToken);
            }

            logger.LogWarning(ex, "Catalog fetch timed out");
            return OperationResult<PublisherCatalog>.CreateFailure("Catalog fetch timed out");
        }
    }

    private List<ContentSearchResult> ConvertCatalogToSearchResults(
        PublisherCatalog catalog,
        ContentSearchQuery query)
    {
        var results = new List<ContentSearchResult>();
        var catalogItemsById = catalog.Content
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var contentNamesById = catalogItemsById.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Name,
            StringComparer.OrdinalIgnoreCase);

        foreach (var contentItem in catalog.Content)
        {
            // Apply version filtering (default: latest only)
            var policy = query.IncludeOlderVersions
                ? VersionPolicy.AllVersions
                : VersionPolicy.LatestStableOnly;

            var selectedReleases = versionSelector.SelectReleases(contentItem.Releases, policy);

            foreach (var release in selectedReleases)
            {
                // Apply search filters
                if (!MatchesQuery(contentItem, query, release))
                {
                    continue;
                }

                // A release whose artifacts carry per-artifact variant hints (e.g. resolution) is
                // split into sibling cards sharing one VariantGroupId, so the downloads browser
                // collapses them into a single card with a variant picker — mirroring how the
                // GitHub topics discoverer handles multi-asset releases. Single-artifact and
                // non-variant releases take the original one-card path unchanged.
                var variantAxes = GetVariantArtifacts(release);

                if (variantAxes.Count > 0)
                {
                    var groupResults = CreateVariantGroupSearchResults(
                        catalog, contentItem, release, variantAxes, catalogItemsById);

                    if (query.TargetGame.HasValue)
                    {
                        results.AddRange(groupResults.Where(r => r.TargetGame == query.TargetGame.Value));
                    }
                    else
                    {
                        results.AddRange(groupResults);
                    }
                }
                else
                {
                    var searchResult = CreateSearchResult(
                        catalog, contentItem, release, contentNamesById, catalogItemsById);
                    if (!query.TargetGame.HasValue || searchResult.TargetGame == query.TargetGame.Value)
                    {
                        results.Add(searchResult);
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Builds a single <see cref="ContentSearchResult"/> for a non-variant catalog release.
    /// Encapsulates the original one-card-per-release path (badges, includes summary, resolver
    /// metadata) so the variant-splitting branch can reuse it for shared presentation logic.
    /// </summary>
    private ContentSearchResult CreateSearchResult(
        PublisherCatalog catalog,
        CatalogContentItem contentItem,
        ContentRelease release,
        IReadOnlyDictionary<string, string> contentNamesById,
        IReadOnlyDictionary<string, CatalogContentItem> catalogItemsById)
    {
        var resolvedRelease = CatalogBundleComponentBuilder.CloneReleaseWithResolvedTypes(
            release,
            contentItem,
            catalogItemsById);
        var declaredPublisher = CatalogManifestIdentity.ResolveDeclaredPublisherType(contentItem);

        var searchResult = new ContentSearchResult
        {
            Id = CatalogManifestIdentity.CreateContentId(
                declaredPublisher,
                contentItem.ContentType,
                contentItem.Id,
                release.Version),
            Name = contentItem.Name,
            Description = contentItem.Description,
            Version = release.Version,
            ContentType = contentItem.ContentType,
            TargetGame = contentItem.TargetGame,
            ProviderName = catalog.Publisher.Name,
            AuthorName = !string.IsNullOrWhiteSpace(contentItem.Metadata?.Author) ? contentItem.Metadata.Author : catalog.Publisher.Name,
            ResolverId = ResolverId,
            IconUrl = catalog.Publisher.AvatarUrl, // Default to publisher avatar
            BannerUrl = contentItem.Metadata?.BannerUrl,
            LastUpdated = release.ReleaseDate,
            RequiresResolution = true,
        };

        PopulatePresentation(searchResult, contentItem, release, contentNamesById);
        AttachResolverMetadata(searchResult, catalog, contentItem, resolvedRelease);

        if (contentItem.ContentType == ContentType.ContentBundle)
        {
            var components = CatalogBundleComponentBuilder.Build(catalog, contentItem, release);
            searchResult.ResolverMetadata[CatalogConstants.BundleComponentsJsonMetadataKey] =
                JsonSerializer.Serialize(components);
        }

        return searchResult;
    }

    /// <summary>
    /// Splits a variant release into one sibling card per variant artifact. Every sibling shares
    /// a <see cref="ContentSearchResult.VariantGroupId"/> and carries the full variant list so the
    /// downloads browser collapses them into one card with a dropdown. Each sibling's release JSON
    /// contains only its own artifact, so the resolver downloads exactly the chosen variant.
    /// </summary>
    private List<ContentSearchResult> CreateVariantGroupSearchResults(
        PublisherCatalog catalog,
        CatalogContentItem contentItem,
        ContentRelease release,
        List<ReleaseArtifact> variantArtifacts,
        IReadOnlyDictionary<string, CatalogContentItem> catalogItemsById)
    {
        var groupId = $"catalog.{catalog.Publisher.Id}.{contentItem.Id}.{release.Version}";
        var familyName = contentItem.Name;
        var resolvedRelease = CatalogBundleComponentBuilder.CloneReleaseWithResolvedTypes(
            release,
            contentItem,
            catalogItemsById);
        var declaredPublisher = CatalogManifestIdentity.ResolveDeclaredPublisherType(contentItem);

        var siblings = new List<(ContentSearchResult Result, ContentVariantInfo Info, ReleaseArtifact Artifact)>(variantArtifacts.Count);
        foreach (var artifact in variantArtifacts)
        {
            siblings.Add(BuildSingleVariantSibling(
                catalog,
                contentItem,
                release,
                resolvedRelease,
                artifact,
                groupId,
                familyName,
                declaredPublisher));
        }

        // Ensure exactly one default — prefer an author-declared IsDefaultVariant, else 1080p,
        // else the first sibling.
        var variantList = siblings.Select(s => s.Info).ToList();
        MarkDefaultVariant(variantList);

        var results = new List<ContentSearchResult>(siblings.Count);
        foreach (var (sibling, _, _) in siblings)
        {
            sibling.Variants = variantList;
            results.Add(sibling);
        }

        return results;
    }

    private (ContentSearchResult Result, ContentVariantInfo Info, ReleaseArtifact Artifact) BuildSingleVariantSibling(
        PublisherCatalog catalog,
        CatalogContentItem contentItem,
        ContentRelease originalRelease,
        ContentRelease resolvedRelease,
        ReleaseArtifact artifact,
        string groupId,
        string familyName,
        string declaredPublisher)
    {
        var variantLabel = artifact.Variant?.Trim() ?? string.Empty;
        var axis = artifact.VariantAxis?.Trim() ?? string.Empty;
        var siblingTargetGame = ResolveSiblingTargetGame(contentItem.TargetGame, axis, variantLabel);

        var sibling = new ContentSearchResult
        {
            Id = CatalogManifestIdentity.CreateVariantContentId(
                declaredPublisher,
                contentItem.ContentType,
                contentItem.Id,
                variantLabel,
                resolvedRelease.Version,
                axis),
            Name = $"{contentItem.Name} ({variantLabel})",
            Description = contentItem.Description,
            Version = resolvedRelease.Version,
            ContentType = contentItem.ContentType,
            TargetGame = siblingTargetGame,
            ProviderName = catalog.Publisher.Name,
            AuthorName = !string.IsNullOrWhiteSpace(contentItem.Metadata?.Author) ? contentItem.Metadata.Author : catalog.Publisher.Name,
            ResolverId = ResolverId,
            IconUrl = catalog.Publisher.AvatarUrl,
            BannerUrl = contentItem.Metadata?.BannerUrl,
            LastUpdated = resolvedRelease.ReleaseDate,
            RequiresResolution = true,
            DownloadSize = artifact.Size,
            VariantGroupId = groupId,
            VariantFamilyName = familyName,
        };

        PopulatePresentation(sibling, contentItem, resolvedRelease, contentNamesById: null);

        var siblingArtifacts = BuildSiblingArtifacts(artifact, resolvedRelease);
        sibling.DownloadSize = siblingArtifacts.Sum(a => a.Size);

        var singleArtifactRelease = new ContentRelease
        {
            Version = resolvedRelease.Version,
            ReleaseDate = resolvedRelease.ReleaseDate,
            IsPrerelease = resolvedRelease.IsPrerelease,
            IsLatest = resolvedRelease.IsLatest,
            Changelog = resolvedRelease.Changelog,
            Artifacts = siblingArtifacts,
            Dependencies = resolvedRelease.Dependencies?.Select(dep =>
            {
                if (CatalogManifestIdentity.IsBaseGameDependency(dep))
                {
                    return new CatalogDependency
                    {
                        PublisherId = dep.PublisherId ?? "ea",
                        ContentId = siblingTargetGame == GameType.Generals ? "generals" : "zerohour",
                        VersionConstraint = siblingTargetGame == GameType.Generals ? "1.08" : "1.04",
                        ContentType = ContentType.GameInstallation.ToString(),
                        IsOptional = dep.IsOptional,
                    };
                }

                return dep;
            }).ToList() ?? [],
        };

        AttachResolverMetadata(sibling, catalog, contentItem, singleArtifactRelease);

        if (contentItem.ContentType == ContentType.ContentBundle)
        {
            var components = CatalogBundleComponentBuilder.Build(catalog, contentItem, originalRelease);
            sibling.ResolverMetadata[CatalogConstants.BundleComponentsJsonMetadataKey] =
                JsonSerializer.Serialize(components);
        }

        var info = new ContentVariantInfo
        {
            Id = $"{axis}:{variantLabel}",
            Name = variantLabel,
            VariantType = axis,
            ManifestId = sibling.Id,
            IsDefault = artifact.IsDefaultVariant,
        };

        return (sibling, info, artifact);
    }

    /// <summary>
    /// Applies shared presentation metadata (screenshots, tags, badges, includes summary) to a
    /// search result. Variant siblings skip the includes summary (bundle contents are not
    /// per-variant).
    /// </summary>
    private void PopulatePresentation(
        ContentSearchResult searchResult,
        CatalogContentItem contentItem,
        ContentRelease release,
        IReadOnlyDictionary<string, string>? contentNamesById)
    {
        if (contentItem.Metadata?.ScreenshotUrls != null)
        {
            foreach (var url in contentItem.Metadata.ScreenshotUrls)
            {
                searchResult.ScreenshotUrls.Add(url);
            }
        }

        foreach (var tag in contentItem.Tags)
        {
            searchResult.Tags.Add(tag);
        }

        if (contentItem.Metadata?.PlayerCount is int playerCount && playerCount > 0)
        {
            ContentCardBadgeHelper.ApplyPlayerCount(searchResult, playerCount);
        }

        ContentCardBadgeHelper.ApplyCategory(searchResult, contentItem.Metadata?.Category);
        ContentCardBadgeHelper.PromoteFromTags(searchResult);

        if (contentNamesById != null)
        {
            ContentCardBadgeHelper.ApplyIncludesSummary(
                searchResult,
                ResolveIncludedContentNames(release, contentNamesById));
        }
    }
}
