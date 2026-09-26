using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Utilities;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
[System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "Catalog discovery involves multi-axis variant normalization, dynamic hydration, and manifest conversion.")]
public class GenericCatalogDiscoverer(
    ILogger<GenericCatalogDiscoverer> logger,
    IHttpClientFactory httpClientFactory,
    IPublisherCatalogParser catalogParser,
    IVersionSelector versionSelector,
    IGitHubApiClient gitHubClient,
    ICatalogUpstreamIngestionService? upstreamIngestionService) : IContentDiscoverer
{
    private readonly record struct VariantSiblingContext(
        ContentRelease OriginalRelease,
        ContentRelease ResolvedRelease,
        string GroupId,
        string FamilyName,
        string DeclaredPublisher,
        bool ImplicitFileSplit);

    private static readonly ConcurrentDictionary<string, (GitHubRelease Release, DateTime CachedAt)> ReleaseCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<GitHubRelease?>> PendingReleaseFetches = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions DefinitionJsonOptions = PublisherJsonOptions.Definition;

    private Core.Models.Providers.PublisherSubscription? _subscription;
    private string? _refreshedCatalogUrl;
    private string? _refreshedAvatarUrl;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenericCatalogDiscoverer"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="catalogParser">The catalog parser.</param>
    /// <param name="versionSelector">The version selector.</param>
    /// <param name="gitHubClient">The GitHub API client.</param>
    public GenericCatalogDiscoverer(
        ILogger<GenericCatalogDiscoverer> logger,
        IHttpClientFactory httpClientFactory,
        IPublisherCatalogParser catalogParser,
        IVersionSelector versionSelector,
        IGitHubApiClient gitHubClient)
        : this(logger, httpClientFactory, catalogParser, versionSelector, gitHubClient, null)
    {
    }

    /// <summary>
    /// Gets the unique identifier of the resolver used by this discoverer.
    /// </summary>
    public static string ResolverId => CatalogConstants.GenericCatalogResolverId;

    /// <inheritdoc />
    public string SourceName => _subscription?.PublisherName ?? CatalogConstants.DefaultDiscovererSourceName;

    /// <inheritdoc />
    public string Description => _subscription != null
        ? $"Content from {_subscription.PublisherName}"
        : CatalogConstants.DefaultDiscovererDescription;

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

    /// <summary>
    /// Takes the catalog URL resolved from the publisher definition during the last
    /// discovery, if it differs from the stored subscription URL. Consumers persist it
    /// so renames and hosting moves stick. Returns null when nothing changed.
    /// </summary>
    /// <returns>The refreshed catalog URL, or null.</returns>
    public string? TakeRefreshedCatalogUrl()
    {
        var refreshed = _refreshedCatalogUrl;
        _refreshedCatalogUrl = null;
        return refreshed;
    }

    /// <summary>
    /// Takes the avatar URL resolved from the publisher definition or catalog during discovery,
    /// if it differs from the stored subscription avatar URL. Consumers persist it so logo updates
    /// propagate immediately. Returns null when unchanged.
    /// </summary>
    /// <returns>The refreshed avatar URL, or null.</returns>
    public string? TakeRefreshedAvatarUrl()
    {
        var refreshed = _refreshedAvatarUrl;
        _refreshedAvatarUrl = null;
        return refreshed;
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

            var catalog = catalogResult.Data;
            if (catalog == null)
            {
                return OperationResult<ContentDiscoveryResult>.CreateFailure("Catalog data is null");
            }

            // Dynamically hydrate upstream releases (e.g. TheSuperHackers, GeneralsOnline, CommunityOutpost)
            if (upstreamIngestionService != null)
            {
                await upstreamIngestionService.IngestCatalogAsync(catalog, cancellationToken);
            }
            else
            {
                await HydrateDynamicReleasesAsync(catalog, cancellationToken);
            }

            // Ensure bundle items with empty releases have a synthetic release so versionSelector includes them
            CatalogBundleComponentBuilder.HydrateSyntheticBundleReleases(catalog.Content);

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover content from publisher '{PublisherId}'", _subscription.PublisherId);
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

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
                (a.TargetGame != GameType.Unknown && a.TargetGame == targetGame) ||
                (string.Equals(a.VariantAxis, CatalogConstants.GameTypeVariantAxis, StringComparison.OrdinalIgnoreCase) &&
                ResolveSiblingTargetGame(GameType.Unknown, a.VariantAxis ?? string.Empty, a.Variant ?? string.Empty) == targetGame)) == true;

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
            var searchTerm = query.SearchTerm;
            var matchesName = content.Name?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true;
            var matchesDescription = content.Description?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true;
            var matchesTags = content.Tags?.Any(t => t?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true) == true;

            if (!matchesName && !matchesDescription && !matchesTags)
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
    private static IReadOnlyList<ReleaseArtifact> GetVariantArtifacts(ContentRelease release) =>
        CatalogManifestIdentity.GetVariantArtifacts(release);

    /// <summary>
    /// Guarantees exactly one variant is marked default. If the author declared one via
    /// <see cref="ReleaseArtifact.IsDefaultVariant"/> that is preserved; otherwise prefer a
    /// 1080p resolution variant, then any resolution variant, then the first option.
    /// </summary>
    private static void MarkDefaultVariant(List<ContentVariantInfo> variants)
    {
        CatalogManifestIdentity.SelectDefaultVariant(
            variants,
            v => v.Name,
            v => v.VariantType,
            v => v.IsDefault,
            (v, isDefault) => v.IsDefault = isDefault);
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

    private static IReadOnlyList<string> ResolveDefinitionCatalogUrls(PublisherDefinition? definition, string? selectedCatalogId)
    {
        var urls = new List<string>();
        if (definition == null)
        {
            return urls;
        }

        if (definition.Catalogs != null)
        {
            // The selected catalog goes first so the feed the user follows wins
            // over sibling catalogs when a publisher hosts several.
            var selected = !string.IsNullOrWhiteSpace(selectedCatalogId)
                ? definition.Catalogs.FirstOrDefault(e => string.Equals(e.Id, selectedCatalogId, StringComparison.OrdinalIgnoreCase))
                : null;
            if (selected != null)
            {
                AddDefinitionUrl(urls, selected.Url);
                AddDefinitionMirrors(urls, selected.Mirrors);
            }

            foreach (var entry in definition.Catalogs)
            {
                if (ReferenceEquals(entry, selected))
                {
                    continue;
                }

                AddDefinitionUrl(urls, entry.Url);
                AddDefinitionMirrors(urls, entry.Mirrors);
            }
        }

        AddDefinitionUrl(urls, definition.CatalogUrl);
        AddDefinitionMirrors(urls, definition.CatalogMirrors);

        return urls;
    }

    private static void AddDefinitionMirrors(List<string> urls, List<string>? mirrors)
    {
        if (mirrors == null)
        {
            return;
        }

        foreach (var mirror in mirrors)
        {
            AddDefinitionUrl(urls, mirror);
        }
    }

    private static void AddDefinitionUrl(List<string> urls, string? url)
    {
        if (!string.IsNullOrWhiteSpace(url) &&
            !urls.Contains(url, StringComparer.OrdinalIgnoreCase))
        {
            urls.Add(url);
        }
    }

    private static IReadOnlyList<string> ResolveIncludedContentNames(
        ContentRelease release,
        IReadOnlyDictionary<string, string> contentNamesById)
    {
        var names = new List<string>();
        if (release.Dependencies == null)
        {
            return names;
        }

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

    private static GameType ResolveBaseGameForSibling(GameType siblingTargetGame, GameType itemTargetGame)
    {
        if (siblingTargetGame is GameType.Generals or GameType.ZeroHour)
        {
            return siblingTargetGame;
        }

        if (itemTargetGame is GameType.Generals or GameType.ZeroHour)
        {
            return itemTargetGame;
        }

        return GameType.Unknown;
    }

    private static GameType ResolveSiblingTargetGame(GameType defaultTargetGame, string axis, string variantLabel)
    {
        if (axis.Equals(CatalogConstants.GameTypeVariantAxis, StringComparison.OrdinalIgnoreCase))
        {
            if (variantLabel.Equals(CatalogConstants.GeneralsVariantLabel, StringComparison.OrdinalIgnoreCase))
            {
                return GameType.Generals;
            }

            if (variantLabel.Equals(CatalogConstants.ZeroHourVariantLabel, StringComparison.OrdinalIgnoreCase) ||
                variantLabel.Equals(CatalogConstants.ZeroHourCompactVariantLabel, StringComparison.OrdinalIgnoreCase))
            {
                return GameType.ZeroHour;
            }
        }

        return defaultTargetGame;
    }

    private static bool HasMultipleDownloadableArtifacts(ContentRelease release)
    {
        return release.Artifacts != null && release.Artifacts.Count(artifact => !string.IsNullOrWhiteSpace(artifact.DownloadUrl)) > 1;
    }

    private static string ImplicitFileVariantLabel(ReleaseArtifact artifact, int siblingIndex)
    {
        if (!string.IsNullOrWhiteSpace(artifact.Filename))
        {
            return artifact.Filename.Trim();
        }

        if (Uri.TryCreate(artifact.DownloadUrl?.Trim(), UriKind.Absolute, out var uri))
        {
            var lastSegment = uri.Segments.LastOrDefault()?.Trim('/');
            if (!string.IsNullOrWhiteSpace(lastSegment))
            {
                return Uri.UnescapeDataString(lastSegment);
            }
        }

        // Prefer order-independent values so sibling identity survives catalog reordering.
        if (!string.IsNullOrWhiteSpace(artifact.DownloadUrl))
        {
            return artifact.DownloadUrl.Trim();
        }

        return $"File {siblingIndex + 1}";
    }

    private static string SanitizeFileVariantId(string variantLabel)
    {
        var builder = new StringBuilder(variantLabel.Length);
        foreach (var c in variantLabel)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-');
        }

        return builder.ToString().Trim('-', '.');
    }

    private static string StableUrlHash(string? downloadUrl, string? filename)
    {
        var key = $"{downloadUrl?.Trim() ?? string.Empty}\n{filename?.Trim() ?? string.Empty}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8].ToLowerInvariant();
    }

    private static string[] ResolveSiblingIdLabels(IReadOnlyList<ReleaseArtifact> variantArtifacts, bool implicitFileSplit)
    {
        var idLabels = new string[variantArtifacts.Count];
        for (var index = 0; index < variantArtifacts.Count; index++)
        {
            var artifact = variantArtifacts[index];
            var label = artifact.Variant?.Trim() ?? string.Empty;
            if (implicitFileSplit && string.IsNullOrEmpty(label))
            {
                label = ImplicitFileVariantLabel(artifact, index);
            }

            idLabels[index] = implicitFileSplit ? SanitizeFileVariantId(label) : label;
        }

        if (implicitFileSplit)
        {
            DisambiguateDuplicateFileIds(idLabels, variantArtifacts);
        }

        return idLabels;
    }

    private static void DisambiguateDuplicateFileIds(string[] idLabels, IReadOnlyList<ReleaseArtifact> variantArtifacts)
    {
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < idLabels.Length; index++)
        {
            var artifact = variantArtifacts[index];
            var baseId = string.IsNullOrEmpty(idLabels[index])
                ? $"file-{StableUrlHash(artifact.DownloadUrl, artifact.Filename)}"
                : idLabels[index];
            var id = baseId;
            if (!usedIds.Add(id))
            {
                var hash = StableUrlHash(artifact.DownloadUrl, artifact.Filename);
                id = $"{baseId}-{hash}";
                var duplicateSuffix = 2;
                while (!usedIds.Add(id))
                {
                    duplicateSuffix++;
                    id = $"{baseId}-{hash}-{duplicateSuffix}";
                }
            }

            idLabels[index] = id;
        }
    }

    private static List<ReleaseArtifact> BuildSiblingArtifacts(ReleaseArtifact artifact, ContentRelease resolvedRelease, bool includeSharedArtifacts = true)
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

        if (!includeSharedArtifacts)
        {
            return siblingArtifacts;
        }

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

    /// <summary>
    /// Applies shared presentation metadata (screenshots, tags, badges, includes summary) to a
    /// search result. Variant siblings skip the includes summary (bundle contents are not
    /// per-variant).
    /// </summary>
    private static void PopulatePresentation(
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

        if (release.ImageUrls != null)
        {
            foreach (var url in release.ImageUrls.Where(url => !string.IsNullOrWhiteSpace(url) && !searchResult.ScreenshotUrls.Contains(url)))
            {
                searchResult.ScreenshotUrls.Add(url);
            }
        }

        if (contentItem.Tags != null)
        {
            foreach (var tag in contentItem.Tags.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                searchResult.Tags.Add(tag);
            }
        }

        if (contentItem.Metadata?.PlayerCount is int playerCount && playerCount > 0)
        {
            ContentCardBadgeHelper.ApplyPlayerCount(searchResult, playerCount);
        }

        searchResult.IsFeatured = contentItem.IsFeatured || (contentItem.Metadata?.IsFeatured ?? false);
        searchResult.FeaturedBadge = !string.IsNullOrWhiteSpace(contentItem.FeaturedBadge)
            ? contentItem.FeaturedBadge
            : contentItem.Metadata?.FeaturedBadge;

        ContentCardBadgeHelper.ApplyCategory(searchResult, contentItem.Metadata?.Category);
        ContentCardBadgeHelper.PromoteFromTags(searchResult);

        if (contentNamesById != null)
        {
            ContentCardBadgeHelper.ApplyIncludesSummary(
                searchResult,
                ResolveIncludedContentNames(release, contentNamesById));
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
                var fetchTask = PendingReleaseFetches.GetOrAdd(cacheKey, key => FetchReleaseWithTimeoutAsync(
                    SuperHackersConstants.GeneralsGameCodeOwner,
                    SuperHackersConstants.GeneralsGameCodeRepo,
                    key));

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
        var zhAsset = SuperHackersAssetMatcher.FindAsset(latestRelease.Assets, GameType.ZeroHour);
        var genAsset = SuperHackersAssetMatcher.FindAsset(latestRelease.Assets, GameType.Generals);

        var hydratedItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in dynamicItems)
        {
            var artifacts = new List<ReleaseArtifact>();

            if (zhAsset != null)
            {
                artifacts.Add(new ReleaseArtifact
                {
                    Filename = zhAsset.Name,
                    DownloadUrl = zhAsset.BrowserDownloadUrl,
                    Size = zhAsset.Size,
                    ContentType = "application/zip",
                    VariantAxis = CatalogConstants.GameTypeVariantAxis,
                    Variant = CatalogConstants.ZeroHourVariantLabel,
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
                    VariantAxis = CatalogConstants.GameTypeVariantAxis,
                    Variant = CatalogConstants.GeneralsVariantLabel,
                    IsDefaultVariant = item.TargetGame == GameType.Generals,
                    IsPrimary = item.TargetGame == GameType.Generals,
                });
            }

            // Overwrite item.Releases only when at least one mapped asset exists
            if (artifacts.Count == 0)
            {
                continue;
            }

            hydratedItemIds.Add(item.Id);

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
                            PublisherId = CatalogConstants.EaPublisherId,
                            ContentId = item.TargetGame == GameType.Generals ? CatalogConstants.GeneralsContentId : CatalogConstants.ZeroHourContentId,
                            VersionConstraint = item.TargetGame == GameType.Generals ? ManifestConstants.GeneralsManifestVersion : ManifestConstants.ZeroHourManifestVersion,
                            ContentType = ContentType.GameInstallation.ToString(),
                            IsOptional = false,
                        },
                    ],
                },
            ];
        }

        if (hydratedItemIds.Count == 0)
        {
            logger.LogWarning("No release assets mapped to SuperHackers dynamic items; retaining catalog baseline releases");
            return;
        }

        // Also synchronize any ContentBundle dependencies targeting the hydrated sibling items
        var bundleDependencies = catalog.Content
            .Where(c => c.ContentType == ContentType.ContentBundle && c.Releases != null)
            .SelectMany(b => b.Releases ?? [])
            .Where(r => r.Dependencies != null)
            .SelectMany(r => r.Dependencies ?? [])
            .Where(dep => !CatalogManifestIdentity.IsBaseGameDependency(dep) &&
                          (string.IsNullOrWhiteSpace(dep.PublisherId) ||
                           string.Equals(dep.PublisherId, catalog.Publisher.Id, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(dep.PublisherId, CatalogConstants.SuperHackersPublisherId, StringComparison.OrdinalIgnoreCase)) &&
                          !string.IsNullOrWhiteSpace(dep.ContentId) &&
                          hydratedItemIds.Contains(dep.ContentId));

        foreach (var dep in bundleDependencies)
        {
            if (string.Equals(dep.VersionConstraint, CatalogConstants.LatestVersionToken, StringComparison.OrdinalIgnoreCase))
            {
                dep.VersionConstraint = $">={cleanTag}";
            }
            else if (!string.IsNullOrWhiteSpace(dep.VersionConstraint))
            {
                var parsed = CatalogManifestIdentity.ParseVersionConstraint(dep.VersionConstraint);
                if (parsed.CompatibleVersions is { Count: > 0 })
                {
                    continue;
                }

                if (parsed.IsSatisfiedBy(cleanTag))
                {
                    if (!string.IsNullOrEmpty(parsed.MaxVersion))
                    {
                        var maxOp = parsed.MaxInclusive ? "<=" : "<";
                        dep.VersionConstraint = $">={cleanTag} {maxOp}{parsed.MaxVersion}";
                    }
                    else
                    {
                        dep.VersionConstraint = $">={cleanTag}";
                    }
                }
            }
        }
    }

    private async Task<GitHubRelease?> FetchReleaseWithTimeoutAsync(string owner, string repo, string cacheKey)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await FetchAndCacheReleaseAsync(owner, repo, cacheKey, cts.Token).ConfigureAwait(false);
    }

    private async Task<GitHubRelease?> FetchAndCacheReleaseAsync(string owner, string repo, string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            var release = await gitHubClient.GetLatestReleaseAsync(owner, repo, cancellationToken);
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

    private async Task<IReadOnlyList<string>> ResolveCandidateCatalogUrlsAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var urls = new List<string>();
        if (_subscription != null && !string.IsNullOrWhiteSpace(_subscription.DefinitionUrl))
        {
            var definition = await TryFetchDefinitionAsync(httpClient, _subscription.DefinitionUrl, cancellationToken);
            if (definition != null)
            {
                RememberResolvedPublisherInfo(definition.Publisher);
                logger.LogDebug("Resolving catalog URLs from definition for {PublisherId}", _subscription.PublisherId);
                foreach (var url in ResolveDefinitionCatalogUrls(definition, _subscription.SelectedCatalogId))
                {
                    AddDefinitionUrl(urls, url);
                }
            }
            else
            {
                logger.LogDebug("Publisher definition unavailable; falling back to cached catalog URL");
            }
        }

        AddDefinitionUrl(urls, _subscription?.CatalogUrl);
        return urls;
    }

    private async Task<PublisherDefinition?> TryFetchDefinitionAsync(
        HttpClient httpClient,
        string definitionUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var defJson = await CatalogDocumentReader.ReadAsync(
                httpClient,
                definitionUrl,
                CatalogConstants.MaxCatalogSizeBytes,
                cancellationToken);
            return JsonSerializer.Deserialize<PublisherDefinition>(defJson, DefinitionJsonOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to read publisher definition; falling back to cached catalog URL");
            return null;
        }
    }

    private void RememberResolvedCatalogUrl(string candidateUrl)
    {
        if (_subscription == null)
        {
            return;
        }

        if (string.Equals(_subscription.CatalogUrl, candidateUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        logger.LogInformation(
            "Resolved catalog URL for {PublisherId}: {CatalogUrl}",
            _subscription.PublisherId,
            candidateUrl);
        _subscription.CatalogUrl = candidateUrl;
        _refreshedCatalogUrl = candidateUrl;
    }

    private void RememberResolvedPublisherInfo(PublisherProfile? profile)
    {
        if (_subscription == null || profile == null)
        {
            return;
        }

        var candidateAvatar = profile.AvatarUrl;
        if (!string.IsNullOrWhiteSpace(candidateAvatar) &&
            !string.Equals(_subscription.AvatarUrl, candidateAvatar, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Resolved updated avatar URL for {PublisherId}: {AvatarUrl}",
                _subscription.PublisherId,
                candidateAvatar);
            _subscription.AvatarUrl = candidateAvatar;
            _refreshedAvatarUrl = candidateAvatar;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Catalog discovery failures are reported via OperationResult.")]
    private async Task<OperationResult<PublisherCatalog>> FetchCatalogAsync(CancellationToken cancellationToken)
    {
        if (_subscription == null)
        {
            return OperationResult<PublisherCatalog>.CreateFailure("Subscription is not configured");
        }

        var httpClient = httpClientFactory.CreateClient(CatalogConstants.CatalogHttpClientName);
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var candidateUrls = await ResolveCandidateCatalogUrlsAsync(httpClient, cancellationToken);
        if (candidateUrls.Count == 0)
        {
            return OperationResult<PublisherCatalog>.CreateFailure("No catalog URL available for subscription.");
        }

        OperationResult<PublisherCatalog>? lastFailure = null;
        foreach (var candidateUrl in candidateUrls)
        {
            OperationResult<PublisherCatalog>? parsed = null;
            try
            {
                logger.LogDebug("Fetching catalog from: {CatalogUrl}", candidateUrl);

                var catalogJson = await CatalogDocumentReader.ReadAsync(
                    httpClient,
                    candidateUrl,
                    CatalogConstants.MaxCatalogSizeBytes,
                    cancellationToken);

                // Parse catalog
                parsed = await catalogParser.ParseCatalogAsync(catalogJson, cancellationToken);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(ex, "Catalog fetch cancelled by user");
                throw new OperationCanceledException("Catalog fetch cancelled by user", ex, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                logger.LogDebug(ex, "Catalog candidate failed: {CatalogUrl}", candidateUrl);
                lastFailure = OperationResult<PublisherCatalog>.CreateFailure($"Failed to fetch catalog: {ex.Message}");
                continue;
            }
            catch (TaskCanceledException ex)
            {
                logger.LogDebug(ex, "Catalog candidate timed out: {CatalogUrl}", candidateUrl);
                lastFailure = OperationResult<PublisherCatalog>.CreateFailure("Catalog fetch timed out");
                continue;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Catalog candidate failed: {CatalogUrl}", candidateUrl);
                lastFailure = OperationResult<PublisherCatalog>.CreateFailure($"Failed to fetch catalog: {ex.Message}");
                continue;
            }

            if (parsed?.Success == true && parsed.Data != null)
            {
                RememberResolvedCatalogUrl(candidateUrl);
                if (parsed.Data.Publisher != null)
                {
                    RememberResolvedPublisherInfo(parsed.Data.Publisher);
                }

                return OperationResult<PublisherCatalog>.CreateSuccess(parsed.Data);
            }

            lastFailure = parsed ?? OperationResult<PublisherCatalog>.CreateFailure("Failed to fetch catalog.");
        }

        logger.LogWarning(
            "All {CandidateCount} catalog candidates failed for {PublisherId}: {Error}",
            candidateUrls.Count,
            _subscription.PublisherId,
            lastFailure?.FirstError);
        return lastFailure ?? OperationResult<PublisherCatalog>.CreateFailure("Failed to fetch catalog.");
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

        var catalogIcon = !string.IsNullOrWhiteSpace(catalog.IconUrl) ? catalog.IconUrl : catalog.AvatarUrl;
        var publisherAvatar = _subscription?.AvatarUrl ?? catalog.Publisher?.AvatarUrl;

        foreach (var contentItem in catalog.Content)
        {
            contentItem.CatalogIconUrl ??= catalogIcon;
            contentItem.PublisherAvatarUrl ??= publisherAvatar;

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
                // GitHub topics discoverer handles multi-asset releases. A release with several
                // plain downloadable files and no variant hints is split the same way (one
                // sibling per file, labeled by filename); otherwise only the first file would
                // ever be visible or downloadable. Single-file releases take the original
                // one-card path unchanged.
                var variantAxes = GetVariantArtifacts(release);
                var implicitFileSplit = variantAxes.Count == 0 && !release.BundleArtifacts && HasMultipleDownloadableArtifacts(release);
                if (implicitFileSplit)
                {
                    variantAxes = release.Artifacts
                        .Where(a => !string.IsNullOrWhiteSpace(a.DownloadUrl))
                        .OrderByDescending(a => a.IsPrimary)
                        .ToList();
                }

                if (variantAxes.Count > 0)
                {
                    var groupResults = CreateVariantGroupSearchResults(
                        catalog, contentItem, release, variantAxes, catalogItemsById, implicitFileSplit);

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

        var (effectiveProviderName, authorName, iconUrl) = ResolvePresentationIdentity(catalog, contentItem);

        var searchResult = new ContentSearchResult
        {
            Id = CatalogManifestIdentity.CreateContentId(
                declaredPublisher,
                contentItem.ContentType,
                contentItem.Id,
                release.Version),
            Name = ContentFormatPolicy.StripArchiveExtensions(contentItem.Name),
            Description = contentItem.Description,
            Version = release.Version,
            ContentType = contentItem.ContentType,
            TargetGame = contentItem.TargetGame,
            ProviderName = effectiveProviderName,
            AuthorName = authorName,
            ResolverId = ResolverId,
            IconUrl = iconUrl,
            BannerUrl = contentItem.Metadata?.BannerUrl,
            BackdropUrl = contentItem.Metadata?.BackdropUrl,
            AccentColor = contentItem.Metadata?.AccentColor,
            LastUpdated = release.ReleaseDate,
            RequiresResolution = true,
        };

        PopulatePresentation(searchResult, contentItem, release, contentNamesById);
        AttachResolverMetadata(searchResult, catalog, contentItem, resolvedRelease);

        if (contentItem.ContentType == ContentType.ContentBundle || release.Dependencies is { Count: > 0 })
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
        IReadOnlyList<ReleaseArtifact> variantArtifacts,
        IReadOnlyDictionary<string, CatalogContentItem> catalogItemsById,
        bool implicitFileSplit = false)
    {
        var groupId = $"catalog.{catalog.Publisher.Id}.{contentItem.Id}.{release.Version}";
        var familyName = contentItem.Name;
        var resolvedRelease = CatalogBundleComponentBuilder.CloneReleaseWithResolvedTypes(
            release,
            contentItem,
            catalogItemsById);
        var declaredPublisher = CatalogManifestIdentity.ResolveDeclaredPublisherType(contentItem);

        var siblingContext = new VariantSiblingContext(
            release,
            resolvedRelease,
            groupId,
            familyName,
            declaredPublisher,
            implicitFileSplit);

        var idLabels = ResolveSiblingIdLabels(variantArtifacts, implicitFileSplit);
        var siblings = new List<(ContentSearchResult Result, ContentVariantInfo Info, ReleaseArtifact Artifact)>(variantArtifacts.Count);
        for (var index = 0; index < variantArtifacts.Count; index++)
        {
            siblings.Add(BuildSingleVariantSibling(
                catalog,
                contentItem,
                variantArtifacts[index],
                siblingContext,
                index,
                idLabels[index]));
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
        ReleaseArtifact artifact,
        VariantSiblingContext context,
        int siblingIndex,
        string idLabel)
    {
        var variantLabel = artifact.Variant?.Trim() ?? string.Empty;
        var axis = artifact.VariantAxis?.Trim() ?? string.Empty;
        if (context.ImplicitFileSplit && string.IsNullOrEmpty(variantLabel))
        {
            variantLabel = ImplicitFileVariantLabel(artifact, siblingIndex);
        }

        var siblingTargetGame = artifact.TargetGame != GameType.Unknown
            ? artifact.TargetGame
            : ResolveSiblingTargetGame(contentItem.TargetGame, axis, variantLabel);

        var (effectiveProviderName, authorName, iconUrl) = ResolvePresentationIdentity(catalog, contentItem);

        var cleanContentName = ContentFormatPolicy.StripArchiveExtensions(contentItem.Name);

        var sibling = new ContentSearchResult
        {
            Id = CatalogManifestIdentity.CreateVariantContentId(
                context.DeclaredPublisher,
                contentItem.ContentType,
                contentItem.Id,
                idLabel,
                context.ResolvedRelease.Version,
                axis),
            Name = $"{cleanContentName} ({variantLabel})",
            Description = contentItem.Description,
            Version = context.ResolvedRelease.Version,
            ContentType = contentItem.ContentType,
            TargetGame = siblingTargetGame,
            ProviderName = effectiveProviderName,
            AuthorName = authorName,
            ResolverId = ResolverId,
            IconUrl = iconUrl,
            BannerUrl = contentItem.Metadata?.BannerUrl,
            BackdropUrl = contentItem.Metadata?.BackdropUrl,
            AccentColor = contentItem.Metadata?.AccentColor,
            LastUpdated = context.ResolvedRelease.ReleaseDate,
            RequiresResolution = true,
            DownloadSize = artifact.Size,
            VariantGroupId = context.GroupId,
            VariantFamilyName = context.FamilyName,
        };

        PopulatePresentation(sibling, contentItem, context.ResolvedRelease, contentNamesById: null);

        var siblingArtifacts = BuildSiblingArtifacts(artifact, context.ResolvedRelease, includeSharedArtifacts: !context.ImplicitFileSplit);
        sibling.DownloadSize = siblingArtifacts.Sum(a => a.Size);

        var singleArtifactRelease = new ContentRelease
        {
            Version = context.ResolvedRelease.Version,
            ReleaseDate = context.ResolvedRelease.ReleaseDate,
            IsPrerelease = context.ResolvedRelease.IsPrerelease,
            IsLatest = context.ResolvedRelease.IsLatest,
            Changelog = context.ResolvedRelease.Changelog,
            BundleArtifacts = context.ResolvedRelease.BundleArtifacts,
            Artifacts = siblingArtifacts,
            Dependencies = context.ResolvedRelease.Dependencies?.Select(dep =>
            {
                if (CatalogManifestIdentity.IsBaseGameDependency(dep))
                {
                    var resolvedBaseGame = ResolveBaseGameForSibling(siblingTargetGame, contentItem.TargetGame);

                    var baseGameContentId = resolvedBaseGame switch
                    {
                        GameType.Generals => CatalogConstants.GeneralsContentId,
                        GameType.ZeroHour => CatalogConstants.ZeroHourContentId,
                        _ => dep.ContentId,
                    };

                    var versionConstraint = resolvedBaseGame switch
                    {
                        GameType.Generals => ManifestConstants.GeneralsManifestVersion,
                        GameType.ZeroHour => ManifestConstants.ZeroHourManifestVersion,
                        _ => dep.VersionConstraint,
                    };

                    return new CatalogDependency
                    {
                        PublisherId = dep.PublisherId ?? CatalogConstants.EaPublisherId,
                        ContentId = baseGameContentId,
                        VersionConstraint = versionConstraint,
                        ContentType = ContentType.GameInstallation.ToString(),
                        IsOptional = dep.IsOptional,
                    };
                }

                return dep;
            }).ToList() ?? [],
        };

        AttachResolverMetadata(sibling, catalog, contentItem, singleArtifactRelease);

        if (contentItem.ContentType == ContentType.ContentBundle || context.OriginalRelease.Dependencies is { Count: > 0 })
        {
            var components = CatalogBundleComponentBuilder.Build(catalog, contentItem, context.OriginalRelease);
            sibling.ResolverMetadata[CatalogConstants.BundleComponentsJsonMetadataKey] =
                JsonSerializer.Serialize(components);
        }

        var info = new ContentVariantInfo
        {
            Id = $"{axis}:{idLabel}",
            Name = variantLabel,
            VariantType = axis,
            ManifestId = sibling.Id,
            IsDefault = artifact.IsDefaultVariant,
        };

        return (sibling, info, artifact);
    }

    private (string EffectiveProviderName, string? AuthorName, string? IconUrl) ResolvePresentationIdentity(
        PublisherCatalog catalog,
        CatalogContentItem contentItem)
    {
        var effectiveProviderName = !string.IsNullOrWhiteSpace(_subscription?.PublisherName)
            ? _subscription.PublisherName
            : catalog.Publisher.Name;

        var authorName = !string.IsNullOrWhiteSpace(contentItem.Metadata?.Author)
            ? contentItem.Metadata.Author
            : effectiveProviderName;

        var catalogIcon = !string.IsNullOrWhiteSpace(catalog.IconUrl)
            ? catalog.IconUrl
            : catalog.AvatarUrl;

        var publisherLogo = _subscription?.AvatarUrl
            ?? catalog.Publisher?.AvatarUrl
            ?? PublisherInfoConstants.GetPublisherLogo(effectiveProviderName, catalog.Publisher?.Id ?? string.Empty);

        var itemIcon = !string.IsNullOrWhiteSpace(contentItem.Metadata?.IconUrl) &&
                       !ImageCacheConstants.IsPicsumUrl(contentItem.Metadata.IconUrl)
            ? contentItem.Metadata.IconUrl
            : null;

        if (itemIcon == null &&
            ((contentItem.Id != null && contentItem.Id.Contains("dominator", StringComparison.OrdinalIgnoreCase)) ||
             (contentItem.Name != null && contentItem.Name.Contains("dominator", StringComparison.OrdinalIgnoreCase))))
        {
            itemIcon = PublisherInfoConstants.Dominator.LogoSource;
        }

        var iconUrl = itemIcon
            ?? (!string.IsNullOrWhiteSpace(catalogIcon) ? catalogIcon : null)
            ?? (!string.IsNullOrWhiteSpace(publisherLogo) ? publisherLogo : null)
            ?? ImageCacheConstants.GetPicsumUrl($"{contentItem.Id}-icon", 128, 128);

        return (effectiveProviderName, authorName, iconUrl);
    }
}
