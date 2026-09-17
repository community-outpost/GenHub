using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GenLauncher;
using GenHub.Core.Models.Parsers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Initializes a new instance of the <see cref="GenLauncherDiscoverer"/> class.
/// Discovers GenLauncher content by querying the root YAML catalogs for Zero Hour and Generals,
/// traversing child manifests, and mapping to ContentSearchResult objects.
/// </summary>
/// <param name="httpClientFactory">Factory for creating HTTP clients.</param>
/// <param name="providerLoader">Loader for provider definitions.</param>
/// <param name="catalogParser">Parser for GenLauncher catalog YAML documents.</param>
/// <param name="logger">Logger instance.</param>
/// <param name="localizationService">Optional localization service.</param>
public class GenLauncherDiscoverer(
    IHttpClientFactory httpClientFactory,
    IProviderDefinitionLoader providerLoader,
    GenLauncherCatalogParser catalogParser,
    ILogger<GenLauncherDiscoverer> logger,
    ILocalizationService? localizationService = null)
    : IContentDiscoverer
{
    private const int MaxCacheEntries = 200;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, (DateTime CachedAt, string Content)> _cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record ModProcessingContext(
        HttpClient Client,
        GameType Game,
        string ModName,
        string ModSlug,
        string? ParentIconUrl,
        List<ContentSearchResult> Results,
        List<ContentVariantInfo> Variants,
        List<ContentSection> FilesSections);

    private sealed record ChildManifestContext(
        HttpClient Client,
        GameType Game,
        string ParentModName,
        string ParentModSlug,
        string? ParentIconUrl);

    /// <summary>
    /// Gets the unique discoverer identifier.
    /// </summary>
    public static string DiscovererId => GenLauncherConstants.PublisherId;

    /// <inheritdoc/>
    public string SourceName => PublisherTypeConstants.GenLauncher;

    /// <inheritdoc/>
    public string Description => GenLauncherConstants.DiscovererDescription;

    /// <inheritdoc/>
    public bool IsEnabled => true;

    /// <inheritdoc/>
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc/>
    public Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        return DiscoverAsync(null, query, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ProviderDefinition? provider,
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            provider ??= providerLoader.GetProvider(GenLauncherConstants.PublisherId);
            var client = httpClientFactory.CreateClient(PublisherTypeConstants.GenLauncher);
            var targetGames = DetermineTargetGames(query.TargetGame);

            var allItems = new List<ContentSearchResult>();
            using var semaphore = new SemaphoreSlim(GenLauncherConstants.DefaultCatalogConcurrency, GenLauncherConstants.DefaultCatalogConcurrency);

            var failedCatalogs = new List<string>();
            foreach (var game in targetGames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var catalogResult = await DiscoverGameCatalogAsync(client, provider, game, semaphore, cancellationToken);
                if (!catalogResult.Success)
                {
                    failedCatalogs.Add(catalogResult.FirstError ?? $"Failed to discover catalog for {game}");
                }
                else if (catalogResult.Data != null)
                {
                    allItems.AddRange(catalogResult.Data);
                }
            }

            if (failedCatalogs.Count == targetGames.Count)
            {
                return OperationResult<ContentDiscoveryResult>.CreateFailure(
                    $"GenLauncher discovery failed: {string.Join("; ", failedCatalogs)}");
            }

            var filteredItems = FilterDiscoveredItems(allItems, query);

            var result = new ContentDiscoveryResult
            {
                Items = filteredItems,
                TotalItems = filteredItems.Count,
                HasMoreItems = false,
            };

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover GenLauncher content");
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"GenLauncher discovery failed: {ex.Message}");
        }
    }

    private static List<GameType> DetermineTargetGames(GameType? requestedGame)
    {
        if (requestedGame.HasValue && requestedGame.Value != GameType.Unknown)
        {
            return [requestedGame.Value];
        }

        return [GameType.ZeroHour, GameType.Generals];
    }

    private static List<ContentSearchResult> FilterDiscoveredItems(
        List<ContentSearchResult> allItems,
        ContentSearchQuery query)
    {
        var filtered = allItems.AsEnumerable();

        if (query.ContentType.HasValue && query.ContentType.Value != ContentType.UnknownContentType)
        {
            filtered = filtered.Where(item => item.ContentType == query.ContentType.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            filtered = filtered.Where(item =>
                string.Equals(item.Id, term, StringComparison.OrdinalIgnoreCase) ||
                item.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (item.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                item.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        return filtered.ToList();
    }

    private static string? CleanImageUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        if (rawUrl.StartsWith(ContentConstants.DiscordAttachmentCdnPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return IsValidHttpUrl(rawUrl, out _) ? rawUrl : null;
    }

    private static string? ResolveIconUrl(string? primaryUrl, string? fallbackUrl)
    {
        var cleaned = CleanImageUrl(primaryUrl);
        return !string.IsNullOrWhiteSpace(cleaned) ? cleaned : CleanImageUrl(fallbackUrl);
    }

    private static bool IsValidHttpUrl(string? url, [NotNullWhen(true)] out Uri? uri)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            uri = null;
            return false;
        }

        return ImageCacheService.IsSafeRemoteUrl(url, out uri);
    }

    private static async Task<long?> TryCalculateHeadSizeAsync(
        HttpClient client,
        string? downloadUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl) || !IsValidHttpUrl(downloadUrl, out var uri))
        {
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(GenLauncherConstants.ProbeTimeout);

            using var req = new HttpRequestMessage(HttpMethod.Head, uri);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentLength.HasValue && resp.Content.Headers.ContentLength.Value > 0)
            {
                return resp.Content.Headers.ContentLength.Value;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Ignore probe failures or probe timeouts
        }

        return null;
    }

    private static string BuildResultId(
        string gameToken,
        string typeToken,
        string slug,
        string? parentModSlug,
        string? version)
    {
        if (string.IsNullOrEmpty(parentModSlug))
        {
            return $"genlauncher-{gameToken}-{slug}";
        }

        if (!string.Equals(slug, parentModSlug, StringComparison.OrdinalIgnoreCase))
        {
            return $"genlauncher-{gameToken}-{parentModSlug}-{typeToken}-{slug}";
        }

        var suffix = !string.IsNullOrWhiteSpace(version)
            ? GenLauncherCatalogParser.Slugify(version)
            : $"{slug}-release";

        return $"genlauncher-{gameToken}-{parentModSlug}-{typeToken}-{suffix}";
    }

    private static string ResolveSourceUrl(string? modLink, string? parentManifestUrl)
    {
        var rawSourceUrl = !string.IsNullOrWhiteSpace(modLink)
            ? modLink
            : parentManifestUrl ?? string.Empty;

        return !string.IsNullOrWhiteSpace(rawSourceUrl) && IsValidHttpUrl(rawSourceUrl, out _)
            ? rawSourceUrl
            : string.Empty;
    }

    private static string? ResolveFileDownloadUrl(string? simpleDownloadLink, string? fallbackUrl)
    {
        if (!string.IsNullOrWhiteSpace(simpleDownloadLink) &&
            IsValidHttpUrl(simpleDownloadLink, out _) &&
            !GenLauncherConstants.IsYamlDescriptorPath(simpleDownloadLink))
        {
            return simpleDownloadLink;
        }

        if (!string.IsNullOrWhiteSpace(fallbackUrl) &&
            IsValidHttpUrl(fallbackUrl, out _) &&
            !GenLauncherConstants.IsYamlDescriptorPath(fallbackUrl))
        {
            return fallbackUrl;
        }

        return null;
    }

    private static string ResolveArchiveFileName(GenLauncherVersionManifest manifest, string defaultName)
    {
        if (!string.IsNullOrWhiteSpace(manifest.SimpleDownloadLink) &&
            IsValidHttpUrl(manifest.SimpleDownloadLink, out var uri))
        {
            var fn = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fn) &&
                !GenLauncherConstants.IsYamlDescriptorPath(fn))
            {
                return Uri.UnescapeDataString(fn);
            }
        }

        var safeName = !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : defaultName;
        return $"{safeName.Trim()}.zip";
    }

    private string GetLocalizedString(string key, string fallback)
    {
        return localizationService?.GetString(key) ?? fallback;
    }

    private string BuildDescription(GenLauncherVersionManifest manifest)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(manifest.Name))
        {
            var header = !string.IsNullOrWhiteSpace(manifest.Version)
                ? $"{manifest.Name} v{manifest.Version}"
                : manifest.Name;
            parts.Add(header);
        }

        if (!string.IsNullOrWhiteSpace(manifest.NewsLink))
        {
            var prefix = GetLocalizedString("GenLauncher.Prefix.News", "News");
            parts.Add($"{prefix}: {manifest.NewsLink}");
        }

        if (!string.IsNullOrWhiteSpace(manifest.DiscordLink))
        {
            var prefix = GetLocalizedString("GenLauncher.Prefix.Discord", "Discord");
            parts.Add($"{prefix}: {manifest.DiscordLink}");
        }

        if (!string.IsNullOrWhiteSpace(manifest.ModDBLink))
        {
            var prefix = GetLocalizedString("GenLauncher.Prefix.ModDb", "ModDB");
            parts.Add($"{prefix}: {manifest.ModDBLink}");
        }

        if (!string.IsNullOrWhiteSpace(manifest.SupportLink))
        {
            var prefix = GetLocalizedString("GenLauncher.Prefix.Support", "Support");
            parts.Add($"{prefix}: {manifest.SupportLink}");
        }

        return string.Join("\n\n", parts);
    }

    private void EnrichSearchResult(
        ContentSearchResult result,
        GenLauncherVersionManifest manifest,
        string manifestUrl,
        string? fallbackIconUrl = null)
    {
        result.SetData(manifest);

        var finalIconUrl = ResolveIconUrl(manifest.UIImageSourceLink, fallbackIconUrl);
        if (!string.IsNullOrEmpty(finalIconUrl))
        {
            result.IconUrl = finalIconUrl;
        }

        if (!string.IsNullOrEmpty(manifest.NewsLink))
        {
            result.ResolverMetadata[GenLauncherConstants.NewsLinkMetadataKey] = manifest.NewsLink;
        }

        if (!string.IsNullOrEmpty(manifest.SupportLink))
        {
            result.ResolverMetadata[GenLauncherConstants.SupportLinkMetadataKey] = manifest.SupportLink;
        }

        if (!string.IsNullOrEmpty(manifest.DiscordLink))
        {
            result.ResolverMetadata[GenLauncherConstants.DiscordLinkMetadataKey] = manifest.DiscordLink;
        }

        if (!string.IsNullOrEmpty(manifest.ModDBLink))
        {
            result.ResolverMetadata[GenLauncherConstants.ModDbLinkMetadataKey] = manifest.ModDBLink;
        }

        if (!string.IsNullOrEmpty(manifest.DependenceName))
        {
            result.ResolverMetadata[GenLauncherConstants.DependenceNameMetadataKey] = manifest.DependenceName;
        }

        if (!string.IsNullOrEmpty(manifest.S3HostLink))
        {
            result.ResolverMetadata[GenLauncherConstants.S3HostLinkMetadataKey] = manifest.S3HostLink;
            result.ResolverMetadata[GenLauncherConstants.S3HostMetadataKey] = manifest.S3HostLink;
        }

        if (!string.IsNullOrEmpty(manifest.S3BucketName))
        {
            result.ResolverMetadata[GenLauncherConstants.S3BucketNameMetadataKey] = manifest.S3BucketName;
            result.ResolverMetadata[GenLauncherConstants.S3BucketMetadataKey] = manifest.S3BucketName;
        }

        if (!string.IsNullOrEmpty(manifest.S3FolderName))
        {
            result.ResolverMetadata[GenLauncherConstants.S3FolderNameMetadataKey] = manifest.S3FolderName;
            result.ResolverMetadata[GenLauncherConstants.S3FolderMetadataKey] = manifest.S3FolderName;
        }

        if (!string.IsNullOrEmpty(manifest.S3HostPublicKey))
        {
            result.ResolverMetadata[GenLauncherConstants.S3HostPublicKeyMetadataKey] = manifest.S3HostPublicKey;
        }

        if (!string.IsNullOrEmpty(manifest.S3HostSecretKey))
        {
            result.ResolverMetadata[GenLauncherConstants.S3HostSecretKeyMetadataKey] = manifest.S3HostSecretKey;
        }

        if (!string.IsNullOrEmpty(manifest.SimpleDownloadLink))
        {
            result.ResolverMetadata[GenLauncherConstants.SimpleDownloadLinkMetadataKey] = manifest.SimpleDownloadLink;
            if (IsValidHttpUrl(manifest.SimpleDownloadLink, out _))
            {
                result.SelectedDownloadUrl = manifest.SimpleDownloadLink;
            }
        }

        result.ResolverMetadata[GenLauncherConstants.YamlUrlMetadataKey] = manifestUrl;
        result.Description = BuildDescription(manifest);
    }

    private async Task<OperationResult<List<ContentSearchResult>>> DiscoverGameCatalogAsync(
        HttpClient client,
        ProviderDefinition? provider,
        GameType game,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        var items = new List<ContentSearchResult>();
        var catalogUrl = game == GameType.Generals
            ? provider?.Endpoints.Custom.GetValueOrDefault("generalsCatalogUrl", GenLauncherConstants.GeneralsCatalogUrl)
            : provider?.Endpoints.Custom.GetValueOrDefault("zeroHourCatalogUrl", GenLauncherConstants.ZeroHourCatalogUrl);

        catalogUrl ??= game == GameType.Generals
            ? GenLauncherConstants.GeneralsCatalogUrl
            : GenLauncherConstants.ZeroHourCatalogUrl;

        var rootYaml = await FetchStringWithCacheAsync(client, catalogUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(rootYaml))
        {
            logger.LogWarning("Could not fetch root catalog from {Url}", catalogUrl);
            return OperationResult<List<ContentSearchResult>>.CreateFailure($"Could not fetch root catalog from {catalogUrl}");
        }

        GenLauncherRootManifest rootManifest;
        try
        {
            rootManifest = catalogParser.ParseRootCatalog(rootYaml);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse root catalog YAML for {Game}", game);
            return OperationResult<List<ContentSearchResult>>.CreateFailure($"Failed to parse root catalog YAML for {game}: {ex.Message}");
        }

        if (rootManifest.ModDatas != null)
        {
            var modTasks = rootManifest.ModDatas.Select(async modEntry =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await ProcessModEntryAsync(client, modEntry, game, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to process mod entry {ModName} for {Game}", modEntry.ModName, game);
                    return new List<ContentSearchResult>();
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var modResults = await Task.WhenAll(modTasks);
            foreach (var modResultList in modResults)
            {
                items.AddRange(modResultList);
            }
        }

        var originalPatches = await ProcessUrlListAsync(
            client,
            rootManifest.OriginalGamePatches,
            game,
            ContentType.Patch,
            $"{game} Official Patches",
            semaphore,
            cancellationToken);
        items.AddRange(originalPatches);

        var originalAddons = await ProcessUrlListAsync(
            client,
            rootManifest.OriginalGameAddons,
            game,
            ContentType.Addon,
            $"{game} Official Addons",
            semaphore,
            cancellationToken);
        items.AddRange(originalAddons);

        var globalAddons = await ProcessUrlListAsync(
            client,
            rootManifest.GlobalAddonsData,
            game,
            ContentType.Addon,
            $"{game} Global Addons",
            semaphore,
            cancellationToken);
        items.AddRange(globalAddons);

        return OperationResult<List<ContentSearchResult>>.CreateSuccess(items);
    }

    private async Task<(GenLauncherVersionManifest? Manifest, string? IconUrl, long? SizeBytes)> FetchParentManifestAsync(
        HttpClient client,
        string manifestUrl,
        string modName,
        CancellationToken cancellationToken)
    {
        var parentYaml = await FetchStringWithCacheAsync(client, manifestUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(parentYaml))
        {
            return (null, null, null);
        }

        try
        {
            var parentManifest = catalogParser.ParseVersionManifest(parentYaml);
            var parentIconUrl = parentManifest.UIImageSourceLink;
            var parentSizeBytes = await TryCalculateDownloadSizeAsync(client, parentManifest, cancellationToken);
            return (parentManifest, parentIconUrl, parentSizeBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse parent version manifest for {Name}", modName);
            return (null, null, null);
        }
    }

    private void AttachParsedPageData(
        ContentSearchResult parentResult,
        ModProcessingContext context,
        string modName,
        string? modLink)
    {
        if (context.FilesSections.Count == 0)
        {
            return;
        }

        var pageUri = !string.IsNullOrWhiteSpace(modLink) && IsValidHttpUrl(modLink, out var validUri)
            ? validUri
            : new Uri(GenLauncherConstants.WebsiteUrl);
        var communityContext = GetLocalizedString("GenLauncher.Context.Community", "GenLauncher Community");
        parentResult.ParsedPageData = new ParsedWebPage(
            pageUri,
            new GlobalContext(modName, communityContext, null, PublisherTypeConstants.GenLauncher),
            context.FilesSections,
            PageType.Detail);
    }

    private ContentSearchResult CreateParentModResult(
        ModProcessingContext context,
        GenLauncherModDataEntry modEntry,
        GenLauncherVersionManifest? parentManifest,
        string? parentManifestUrl,
        long? parentSizeBytes)
    {
        var parentResult = new ContentSearchResult
        {
            Id = $"genlauncher-{context.Game.ToString().ToLowerInvariant()}-{context.ModSlug}",
            Name = modEntry.ModName,
            ContentType = ContentType.Mod,
            TargetGame = context.Game,
            ProviderName = PublisherTypeConstants.GenLauncher,
            ResolverId = GenLauncherConstants.PublisherId,
            SourceUrl = ResolveSourceUrl(modEntry.ModLink, parentManifestUrl),
            IconUrl = ResolveIconUrl(parentManifest?.UIImageSourceLink, null),
            RequiresResolution = true,
            VariantGroupId = context.ModSlug,
            VariantFamilyName = modEntry.ModName,
        };

        parentResult.Tags.Add("genlauncher");
        parentResult.Tags.Add("mod");
        parentResult.Tags.Add(context.Game.ToString().ToLowerInvariant());

        if (parentManifest != null && parentManifestUrl != null)
        {
            EnrichSearchResult(parentResult, parentManifest, parentManifestUrl);
            parentResult.Version = parentManifest.Version;
            if (parentManifest.Deprecated)
            {
                parentResult.Tags.Add("deprecated");
            }
        }

        if (parentSizeBytes.HasValue && parentSizeBytes.Value > 0)
        {
            parentResult.DownloadSize = parentSizeBytes.Value;
        }

        if (!string.IsNullOrWhiteSpace(parentManifest?.SimpleDownloadLink) &&
            IsValidHttpUrl(parentManifest.SimpleDownloadLink, out _))
        {
            parentResult.SelectedDownloadUrl = parentManifest.SimpleDownloadLink;
        }

        context.Variants.Insert(0, new ContentVariantInfo
        {
            Id = context.ModSlug,
            Name = parentManifest?.Name ?? modEntry.ModName,
            ManifestId = parentResult.Id,
            IsDefault = true,
        });

        if (parentManifest != null)
        {
            var fileName = ResolveArchiveFileName(parentManifest, modEntry.ModName);
            var fileUrl = ResolveFileDownloadUrl(parentManifest.SimpleDownloadLink, null);
            context.FilesSections.Insert(0, new DownloadableFile(
                Name: $"{modEntry.ModName} {parentManifest.Version}".Trim(),
                Version: parentManifest.Version,
                SizeBytes: parentSizeBytes,
                DownloadUrl: fileUrl,
                FileSectionType: FileSectionType.Downloads,
                Description: BuildDescription(parentManifest),
                ThumbnailUrl: ResolveIconUrl(parentManifest.UIImageSourceLink, parentResult.IconUrl),
                Filename: fileName));
        }

        parentResult.Variants = context.Variants;
        AttachParsedPageData(parentResult, context, modEntry.ModName, modEntry.ModLink);

        return parentResult;
    }

    private async Task<List<ContentSearchResult>> ProcessModEntryAsync(
        HttpClient client,
        GenLauncherModDataEntry modEntry,
        GameType game,
        CancellationToken cancellationToken)
    {
        var results = new List<ContentSearchResult>();
        if (string.IsNullOrWhiteSpace(modEntry.ModName))
        {
            return results;
        }

        var modSlug = GenLauncherCatalogParser.Slugify(modEntry.ModName);
        var variants = new List<ContentVariantInfo>();
        var filesSections = new List<ContentSection>();

        GenLauncherVersionManifest? parentManifest = null;
        string? parentManifestUrl = null;
        string? parentIconUrl = null;
        long? parentSizeBytes = null;

        if (!string.IsNullOrWhiteSpace(modEntry.ModLink))
        {
            if (!IsValidHttpUrl(modEntry.ModLink, out _))
            {
                logger.LogWarning("Rejecting mod entry {ModName} with unsafe or invalid ModLink: {Url}", modEntry.ModName, modEntry.ModLink);
                return results;
            }

            parentManifestUrl = modEntry.ModLink;
            (parentManifest, parentIconUrl, parentSizeBytes) = await FetchParentManifestAsync(
                client, parentManifestUrl, modEntry.ModName, cancellationToken);
        }

        var context = new ModProcessingContext(
            client,
            game,
            modEntry.ModName,
            modSlug,
            parentIconUrl,
            results,
            variants,
            filesSections);

        await ProcessModChildUrlsAsync(
            context,
            modEntry.ModPatches,
            ContentType.Patch,
            cancellationToken);

        await ProcessModChildUrlsAsync(
            context,
            modEntry.ModAddons,
            ContentType.Addon,
            cancellationToken);

        var parentResult = CreateParentModResult(
            context,
            modEntry,
            parentManifest,
            parentManifestUrl,
            parentSizeBytes);

        results.Insert(0, parentResult);
        return results;
    }

    private async Task ProcessModChildUrlsAsync(
        ModProcessingContext context,
        IEnumerable<string>? urls,
        ContentType contentType,
        CancellationToken cancellationToken)
    {
        if (urls == null)
        {
            return;
        }

        var urlList = urls.ToList();
        if (urlList.Count == 0)
        {
            return;
        }

        var childContext = new ChildManifestContext(
            context.Client,
            context.Game,
            context.ModName,
            context.ModSlug,
            context.ParentIconUrl);

        using var semaphore = new SemaphoreSlim(6, 6);
        var childTasks = urlList.Select(async url =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = await ProcessChildManifestAsync(
                    childContext,
                    url,
                    contentType,
                    cancellationToken);
                if (item == null)
                {
                    return ((ContentSearchResult?)null, (long?)null);
                }

                long? sizeBytes = null;
                if (item.Data is GenLauncherVersionManifest childManifest)
                {
                    sizeBytes = await TryCalculateDownloadSizeAsync(context.Client, childManifest, cancellationToken);
                    if (sizeBytes.HasValue && sizeBytes.Value > 0)
                    {
                        item.DownloadSize = sizeBytes.Value;
                    }
                }

                return ((ContentSearchResult?)item, sizeBytes);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var childResults = await Task.WhenAll(childTasks);
        var sectionType = contentType == ContentType.Patch ? FileSectionType.Downloads : FileSectionType.Addons;
        foreach (var (item, sizeBytes) in childResults)
        {
            if (item == null)
            {
                continue;
            }

            context.Results.Add(item);
            var slug = GenLauncherCatalogParser.Slugify(item.Name);
            context.Variants.Add(new ContentVariantInfo
            {
                Id = slug,
                Name = item.Name,
                ManifestId = item.Id,
                IsDefault = false,
            });

            var childManifest = item.Data as GenLauncherVersionManifest;
            var fileName = childManifest != null
                ? ResolveArchiveFileName(childManifest, item.Name)
                : $"{item.Name}.zip";
            var fileDownloadUrl = item.SelectedDownloadUrl;

            context.FilesSections.Add(new DownloadableFile(
                Name: item.Name,
                Version: item.Version,
                SizeBytes: sizeBytes,
                DownloadUrl: fileDownloadUrl,
                FileSectionType: sectionType,
                Description: item.Description,
                ThumbnailUrl: item.IconUrl ?? context.ParentIconUrl,
                Filename: fileName));
        }
    }

    private async Task<ContentSearchResult?> ProcessChildManifestAsync(
        ChildManifestContext context,
        string manifestUrl,
        ContentType defaultType,
        CancellationToken cancellationToken)
    {
        if (!IsValidHttpUrl(manifestUrl, out _))
        {
            logger.LogWarning("Rejecting child manifest with unsafe or non-HTTP URL: {Url}", manifestUrl);
            return null;
        }

        var yaml = await FetchStringWithCacheAsync(context.Client, manifestUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }

        GenLauncherVersionManifest versionManifest;
        try
        {
            versionManifest = catalogParser.ParseVersionManifest(yaml);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse child version manifest from {Url}", manifestUrl);
            return null;
        }

        if (string.IsNullOrWhiteSpace(versionManifest.Name))
        {
            logger.LogWarning("Rejecting child manifest from {Url} with missing or empty Name", manifestUrl);
            return null;
        }

        if (versionManifest.GetParsedType() == GenLauncherModificationType.Advertising)
        {
            logger.LogDebug("Skipping advertising entry: {Name}", versionManifest.Name);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(versionManifest.SimpleDownloadLink) && !IsValidHttpUrl(versionManifest.SimpleDownloadLink, out _))
        {
            logger.LogWarning("Rejecting child manifest {Name} with unsafe download link: {Url}", versionManifest.Name, versionManifest.SimpleDownloadLink);
            return null;
        }

        var slug = GenLauncherCatalogParser.Slugify(versionManifest.Name);
        var actualType = versionManifest.ModificationType != null
            ? GenLauncherCatalogParser.MapContentType(versionManifest.GetParsedType())
            : defaultType;

        var gameToken = context.Game == GameType.ZeroHour ? "zerohour" : "generals";
        var typeToken = actualType.ToString().ToLowerInvariant();
        var resultId = BuildResultId(
            gameToken,
            typeToken,
            slug,
            context.ParentModSlug,
            versionManifest.Version);

        var result = new ContentSearchResult
        {
            Id = resultId,
            Name = versionManifest.Name,
            Version = versionManifest.Version,
            ContentType = actualType,
            TargetGame = context.Game,
            ProviderName = PublisherTypeConstants.GenLauncher,
            ResolverId = GenLauncherConstants.PublisherId,
            SourceUrl = manifestUrl,
            IconUrl = ResolveIconUrl(versionManifest.UIImageSourceLink, context.ParentIconUrl),
            RequiresResolution = true,
            VariantGroupId = context.ParentModSlug,
            VariantFamilyName = context.ParentModName,
        };

        result.Tags.Add("genlauncher");
        result.Tags.Add(actualType.ToString().ToLowerInvariant());
        result.Tags.Add(context.Game.ToString().ToLowerInvariant());
        if (versionManifest.Deprecated)
        {
            result.Tags.Add("deprecated");
        }

        EnrichSearchResult(result, versionManifest, manifestUrl, context.ParentIconUrl);
        return result;
    }

    private async Task<List<ContentSearchResult>> ProcessUrlListAsync(
        HttpClient client,
        List<string> urls,
        GameType game,
        ContentType contentType,
        string familyName,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        var results = new List<ContentSearchResult>();
        if (urls == null || urls.Count == 0)
        {
            return results;
        }

        var familySlug = GenLauncherCatalogParser.Slugify(familyName);
        var childContext = new ChildManifestContext(client, game, familyName, familySlug, null);

        var tasks = urls.Select(async url =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await ProcessChildManifestAsync(childContext, url, contentType, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var items = await Task.WhenAll(tasks);
        results.AddRange(items.OfType<ContentSearchResult>());

        return results;
    }

    private async Task<long?> TryCalculateDownloadSizeAsync(
        HttpClient client,
        GenLauncherVersionManifest manifest,
        CancellationToken cancellationToken)
    {
        try
        {
            var s3Size = await TryCalculateS3SizeAsync(client, manifest, cancellationToken);
            if (s3Size.HasValue)
            {
                return s3Size.Value;
            }

            return await TryCalculateHeadSizeAsync(client, manifest.SimpleDownloadLink, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to probe download size for {Name}", manifest.Name);
            return null;
        }
    }

    private async Task<(long PageSize, bool IsTruncated, string? NextMarker)?> FetchS3PageSizeAsync(
        HttpClient client,
        GenLauncherVersionManifest manifest,
        string? currentMarker,
        CancellationToken cancellationToken)
    {
        var hasExplicitKeys = !string.IsNullOrWhiteSpace(manifest.S3HostPublicKey) && !string.IsNullOrWhiteSpace(manifest.S3HostSecretKey);

        string? xml = null;
        var usedAuth = true;

        if (hasExplicitKeys)
        {
            var queryUrl = GenLauncherS3XmlParser.BuildS3QueryUrl(
                manifest.S3HostLink!,
                manifest.S3BucketName!,
                manifest.S3FolderName!,
                currentMarker,
                manifest.S3HostPublicKey,
                manifest.S3HostSecretKey,
                useAuth: true);

            xml = await FetchStringWithCacheAsync(client, queryUrl, cancellationToken);
            usedAuth = true;
        }
        else
        {
            // Try unsigned query first since ~70% of GenLauncher MinIO buckets (rotr, forgenerals, contra, zhreborn, etc.) are public anonymous
            var unsignedQueryUrl = GenLauncherS3XmlParser.BuildS3QueryUrl(
                manifest.S3HostLink!,
                manifest.S3BucketName!,
                manifest.S3FolderName!,
                currentMarker,
                publicKey: null,
                secretKey: null,
                useAuth: false);

            xml = await FetchStringWithCacheAsync(client, unsignedQueryUrl, cancellationToken);
            if (!string.IsNullOrWhiteSpace(xml) && !xml.Contains("<Error>"))
            {
                usedAuth = false;
            }
            else
            {
                // Fallback to signed query using default InSave credentials (for improved-ai, tpotw, cncpowerplay)
                var signedQueryUrl = GenLauncherS3XmlParser.BuildS3QueryUrl(
                    manifest.S3HostLink!,
                    manifest.S3BucketName!,
                    manifest.S3FolderName!,
                    currentMarker,
                    manifest.S3HostPublicKey,
                    manifest.S3HostSecretKey,
                    useAuth: true);

                xml = await FetchStringWithCacheAsync(client, signedQueryUrl, cancellationToken);
                usedAuth = true;
            }
        }

        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            var entries = GenLauncherS3XmlParser.ParseListBucketResult(
                xml,
                manifest.S3FolderName!,
                manifest.S3HostLink!,
                manifest.S3BucketName!,
                out var isTruncated,
                out var nextMarker,
                manifest.S3HostPublicKey,
                manifest.S3HostSecretKey,
                useAuth: usedAuth);

            var pageSize = entries.Sum(e => e.Size);
            return (pageSize, isTruncated, nextMarker);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse S3 page size XML for host={Host}, bucket={Bucket}, prefix={Prefix}", manifest.S3HostLink, manifest.S3BucketName, manifest.S3FolderName);
            return null;
        }
    }

    private async Task<long?> TryCalculateS3SizeAsync(
        HttpClient client,
        GenLauncherVersionManifest manifest,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifest.S3HostLink) ||
            string.IsNullOrWhiteSpace(manifest.S3BucketName) ||
            string.IsNullOrWhiteSpace(manifest.S3FolderName))
        {
            return null;
        }

        long totalSize = 0;
        string? nextMarker = null;
        var seenMarkers = new HashSet<string>(StringComparer.Ordinal);
        var pageCount = 0;
        const int maxPages = GenLauncherConstants.MaxS3SizePages;
        var reachedTerminalPage = false;

        while (pageCount++ < maxPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageResult = await FetchS3PageSizeAsync(client, manifest, nextMarker, cancellationToken);
            if (!pageResult.HasValue)
            {
                logger.LogWarning("Failed to fetch S3 page for size calculation of {Name}", manifest.Name);
                return null;
            }

            var (pageSize, isTruncated, marker) = pageResult.Value;
            totalSize += pageSize;

            if (!isTruncated)
            {
                reachedTerminalPage = true;
                break;
            }

            if (string.IsNullOrEmpty(marker) || !seenMarkers.Add(marker))
            {
                logger.LogWarning("S3 pagination truncated without valid marker for size calculation of {Name}", manifest.Name);
                return null;
            }

            nextMarker = marker;
        }

        if (!reachedTerminalPage)
        {
            logger.LogWarning("S3 pagination exceeded max page limit ({MaxPages}) during size calculation for {Name}", maxPages, manifest.Name);
            return null;
        }

        return totalSize > 0 ? totalSize : null;
    }

    private async Task<string?> FetchStringWithCacheAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!IsValidHttpUrl(url, out _))
        {
            logger.LogWarning("Rejecting unsafe or non-HTTP URL: {Url}", url);
            return null;
        }

        if (_cache.TryGetValue(url, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
        {
            return cached.Content;
        }

        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("HTTP GET failed with {StatusCode} for {Url}", response.StatusCode, url);
                return null;
            }

            var maxBytes = GenLauncherConstants.MaxCatalogResponseBodyBytes;
            if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > maxBytes)
            {
                logger.LogWarning("Response body size {Length} from {Url} exceeds limit {Max}", response.Content.Headers.ContentLength.Value, url, maxBytes);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var ms = new MemoryStream();
            var buffer = new byte[GenLauncherConstants.DefaultBufferSize];
            long totalBytesRead = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                totalBytesRead += read;
                if (totalBytesRead > maxBytes)
                {
                    logger.LogWarning("Response body from {Url} exceeded limit {Max} bytes", url, maxBytes);
                    return null;
                }

                await ms.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            var content = Encoding.UTF8.GetString(ms.ToArray());
            StoreInCache(url, content);
            return content;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Exception fetching URL {Url}", url);
            return null;
        }
    }

    private void StoreInCache(string url, string content)
    {
        if (_cache.Count >= MaxCacheEntries)
        {
            var now = DateTime.UtcNow;
            foreach (var key in _cache.Keys)
            {
                if (_cache.TryGetValue(key, out var entry) && now - entry.CachedAt >= CacheTtl)
                {
                    _cache.TryRemove(key, out _);
                }
            }

            if (_cache.Count >= MaxCacheEntries)
            {
                var oldestKeys = _cache
                    .OrderBy(p => p.Value.CachedAt)
                    .Take(_cache.Count - MaxCacheEntries + 1)
                    .Select(p => p.Key)
                    .ToList();

                foreach (var key in oldestKeys)
                {
                    _cache.TryRemove(key, out _);
                }
            }
        }

        _cache[url] = (DateTime.UtcNow, content);
    }
}
