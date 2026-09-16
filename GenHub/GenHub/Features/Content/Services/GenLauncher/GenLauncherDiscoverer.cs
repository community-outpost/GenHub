using GenHub.Core.Constants;
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
using System.Linq;
using System.Net.Http;
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
public class GenLauncherDiscoverer(
    IHttpClientFactory httpClientFactory,
    IProviderDefinitionLoader providerLoader,
    GenLauncherCatalogParser catalogParser,
    ILogger<GenLauncherDiscoverer> logger)
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
            var semaphore = new SemaphoreSlim(6, 6);

            foreach (var game in targetGames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var gameItems = await DiscoverGameCatalogAsync(client, provider, game, semaphore, cancellationToken);
                allItems.AddRange(gameItems);
            }

            var resultList = FilterDiscoveredItems(allItems, query);
            var discoveryResult = new ContentDiscoveryResult
            {
                Items = resultList,
                TotalItems = resultList.Count,
                HasMoreItems = false,
            };
            return OperationResult<ContentDiscoveryResult>.CreateSuccess(discoveryResult);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during GenLauncher content discovery");
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"GenLauncher discovery failed: {ex.Message}");
        }
    }

    private static List<GameType> DetermineTargetGames(GameType? queryGame)
    {
        if (queryGame == GameType.Generals)
        {
            return [GameType.Generals];
        }

        if (queryGame == GameType.ZeroHour)
        {
            return [GameType.ZeroHour];
        }

        return [GameType.ZeroHour, GameType.Generals];
    }

    private static string? CleanImageUrl(string? rawUrl) => ContentCardBadgeHelper.CleanImageUrl(rawUrl);

    private static string? ResolveIconUrl(string? rawUrl, string? fallbackUrl) => ContentCardBadgeHelper.ResolveIconUrl(rawUrl, fallbackUrl);

    private static List<ContentSearchResult> FilterDiscoveredItems(
        IEnumerable<ContentSearchResult> allItems,
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
                item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (item.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                item.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        return filtered.ToList();
    }

    private static void EnrichSearchResult(
        ContentSearchResult result,
        GenLauncherVersionManifest manifest,
        string manifestUrl,
        string? fallbackIconUrl = null)
    {
        result.SetData(manifest);

        if (!string.IsNullOrWhiteSpace(manifest.Version))
        {
            result.Version = manifest.Version;
        }

        var resolvedIcon = ResolveIconUrl(manifest.UIImageSourceLink, fallbackIconUrl ?? result.IconUrl);
        if (!string.IsNullOrWhiteSpace(resolvedIcon))
        {
            result.IconUrl = resolvedIcon;
        }

        if (!string.IsNullOrWhiteSpace(manifest.SimpleDownloadLink) && IsValidHttpUrl(manifest.SimpleDownloadLink, out _))
        {
            result.SelectedDownloadUrl = manifest.SimpleDownloadLink;
            result.ResolverMetadata["simpleDownloadLink"] = manifest.SimpleDownloadLink;
        }

        result.Description = BuildDescription(manifest);

        result.ResolverMetadata["yamlUrl"] = manifestUrl;
        if (!string.IsNullOrWhiteSpace(manifest.S3HostLink))
        {
            result.ResolverMetadata["s3Host"] = manifest.S3HostLink;
        }

        if (!string.IsNullOrWhiteSpace(manifest.S3BucketName))
        {
            result.ResolverMetadata["s3Bucket"] = manifest.S3BucketName;
        }

        if (!string.IsNullOrWhiteSpace(manifest.S3FolderName))
        {
            result.ResolverMetadata["s3Folder"] = manifest.S3FolderName;
        }
    }

    private static string BuildDescription(GenLauncherVersionManifest manifest)
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
            parts.Add($"News: {manifest.NewsLink}");
        }

        if (!string.IsNullOrWhiteSpace(manifest.DiscordLink))
        {
            parts.Add($"Discord: {manifest.DiscordLink}");
        }

        if (!string.IsNullOrWhiteSpace(manifest.ModDBLink))
        {
            parts.Add($"ModDB: {manifest.ModDBLink}");
        }

        if (!string.IsNullOrWhiteSpace(manifest.SupportLink))
        {
            parts.Add($"Support: {manifest.SupportLink}");
        }

        return string.Join("\n\n", parts);
    }

    private static bool IsValidHttpUrl(string url, [NotNullWhen(true)] out Uri? uri) =>
        ImageCacheService.IsSafeRemoteUrl(url, out uri);

    private static async Task<long?> TryCalculateHeadSizeAsync(
        HttpClient client,
        string? downloadUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl) || !IsValidHttpUrl(downloadUrl, out var uri))
        {
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        using var req = new HttpRequestMessage(HttpMethod.Head, uri);
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentLength.HasValue && resp.Content.Headers.ContentLength.Value > 0)
        {
            return resp.Content.Headers.ContentLength.Value;
        }

        return null;
    }

    private async Task<List<ContentSearchResult>> DiscoverGameCatalogAsync(
        HttpClient client,
        ProviderDefinition? provider,
        GameType game,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        var items = new List<ContentSearchResult>();
        var catalogUrl = game == GameType.Generals
            ? provider?.Endpoints.GetEndpoint("generalsCatalogUrl") ?? GenLauncherConstants.GeneralsCatalogUrl
            : provider?.Endpoints.GetEndpoint("zeroHourCatalogUrl") ?? GenLauncherConstants.ZeroHourCatalogUrl;

        var rootYaml = await FetchStringWithCacheAsync(client, catalogUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(rootYaml))
        {
            logger.LogWarning("Could not fetch root catalog from {Url}", catalogUrl);
            return items;
        }

        GenLauncherRootManifest rootManifest;
        try
        {
            rootManifest = catalogParser.ParseRootCatalog(rootYaml);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse root catalog YAML for {Game}", game);
            return items;
        }

        var modTasks = rootManifest.ModDatas.Select(async modEntry =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await ProcessModEntryAsync(client, modEntry, game, cancellationToken);
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

        return items;
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

        if (!string.IsNullOrWhiteSpace(modEntry.ModLink) && !IsValidHttpUrl(modEntry.ModLink, out _))
        {
            logger.LogWarning("Rejecting mod {ModName} with unsafe ModLink: {Url}", modEntry.ModName, modEntry.ModLink);
            return results;
        }

        var modSlug = GenLauncherCatalogParser.Slugify(modEntry.ModName);
        var mainResult = new ContentSearchResult
        {
            Id = $"genlauncher-{game.ToString().ToLowerInvariant()}-{modSlug}",
            Name = modEntry.ModName,
            ContentType = ContentType.Mod,
            TargetGame = game,
            ProviderName = PublisherTypeConstants.GenLauncher,
            ResolverId = GenLauncherConstants.PublisherId,
            SourceUrl = modEntry.ModLink,
            RequiresResolution = true,
            VariantGroupId = modSlug,
            VariantFamilyName = modEntry.ModName,
        };

        mainResult.Tags.Add("genlauncher");
        mainResult.Tags.Add("mod");
        mainResult.Tags.Add(game.ToString().ToLowerInvariant());

        var mainManifest = await TryFetchAndEnrichManifestAsync(
            client,
            modEntry.ModLink,
            modEntry.ModName,
            mainResult,
            cancellationToken);

        var variants = new List<ContentVariantInfo>
        {
            new()
            {
                Id = "base",
                Name = $"{modEntry.ModName} (Base)",
                ManifestId = mainResult.Id,
                IsDefault = true,
            },
        };

        var filesSections = new List<ContentSection>();
        if (mainManifest != null)
        {
            var mainSizeBytes = await TryCalculateDownloadSizeAsync(client, mainManifest, cancellationToken);
            if (mainSizeBytes.HasValue && mainSizeBytes.Value > 0)
            {
                mainResult.DownloadSize = mainSizeBytes.Value;
            }

            filesSections.Add(new DownloadableFile(
                Name: $"{modEntry.ModName} {mainManifest.Version}".Trim(),
                Version: mainManifest.Version,
                SizeBytes: mainSizeBytes,
                DownloadUrl: mainManifest.SimpleDownloadLink,
                FileSectionType: FileSectionType.Downloads,
                Description: BuildDescription(mainManifest),
                ThumbnailUrl: ResolveIconUrl(mainManifest.UIImageSourceLink, mainResult.IconUrl)));
        }

        var modContext = new ModProcessingContext(
            client,
            game,
            modEntry.ModName,
            modSlug,
            mainResult.IconUrl,
            results,
            variants,
            filesSections);

        await ProcessModChildrenAsync(
            modContext,
            modEntry.ModPatches,
            ContentType.Patch,
            FileSectionType.Downloads,
            cancellationToken);

        await ProcessModChildrenAsync(
            modContext,
            modEntry.ModAddons,
            ContentType.Addon,
            FileSectionType.Addons,
            cancellationToken);

        mainResult.Variants = variants;
        if (filesSections.Count > 0)
        {
            mainResult.ParsedPageData = new ParsedWebPage(
                new Uri(string.IsNullOrWhiteSpace(modEntry.ModLink) ? GenLauncherConstants.WebsiteUrl : modEntry.ModLink),
                new GlobalContext(modEntry.ModName, "GenLauncher Community", null, PublisherTypeConstants.GenLauncher),
                filesSections,
                PageType.Detail);
        }

        results.Insert(0, mainResult);
        return results;
    }

    private async Task<GenLauncherVersionManifest?> TryFetchAndEnrichManifestAsync(
        HttpClient client,
        string? modLink,
        string modName,
        ContentSearchResult mainResult,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modLink))
        {
            return null;
        }

        var manifestYaml = await FetchStringWithCacheAsync(client, modLink, cancellationToken);
        if (string.IsNullOrWhiteSpace(manifestYaml))
        {
            return null;
        }

        try
        {
            var mainManifest = catalogParser.ParseVersionManifest(manifestYaml);
            EnrichSearchResult(mainResult, mainManifest, modLink);
            return mainManifest;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse version manifest for mod {ModName}", modName);
            return null;
        }
    }

    private async Task ProcessModChildrenAsync(
        ModProcessingContext context,
        IEnumerable<string>? urls,
        ContentType contentType,
        FileSectionType sectionType,
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

        var childTasks = urlList.Select(async url =>
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
        });

        var childResults = await Task.WhenAll(childTasks);
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

            context.FilesSections.Add(new DownloadableFile(
                Name: item.Name,
                Version: item.Version,
                SizeBytes: sizeBytes,
                DownloadUrl: item.SourceUrl,
                FileSectionType: sectionType,
                Description: item.Description,
                ThumbnailUrl: item.IconUrl ?? context.ParentIconUrl));
        }
    }

    private async Task<ContentSearchResult?> ProcessChildManifestAsync(
        ChildManifestContext context,
        string manifestUrl,
        ContentType defaultType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
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

        if (!string.IsNullOrWhiteSpace(versionManifest.SimpleDownloadLink) && !IsValidHttpUrl(versionManifest.SimpleDownloadLink, out _))
        {
            logger.LogWarning("Rejecting child manifest {Name} with unsafe download link: {Url}", versionManifest.Name, versionManifest.SimpleDownloadLink);
            return null;
        }

        var slug = GenLauncherCatalogParser.Slugify(versionManifest.Name);
        var actualType = versionManifest.ModificationType != null
            ? GenLauncherCatalogParser.MapContentType(versionManifest.GetParsedType())
            : defaultType;

        var result = new ContentSearchResult
        {
            Id = $"genlauncher-{context.Game.ToString().ToLowerInvariant()}-{slug}",
            Name = versionManifest.Name,
            Version = versionManifest.Version,
            ContentType = actualType,
            TargetGame = context.Game,
            ProviderName = PublisherTypeConstants.GenLauncher,
            ResolverId = GenLauncherConstants.PublisherId,
            SourceUrl = versionManifest.SimpleDownloadLink ?? manifestUrl,
            IconUrl = ResolveIconUrl(versionManifest.UIImageSourceLink, context.ParentIconUrl),
            RequiresResolution = true,
            VariantGroupId = context.ParentModSlug,
            VariantFamilyName = context.ParentModName,
        };

        result.Tags.Add("genlauncher");
        result.Tags.Add(actualType.ToString().ToLowerInvariant());
        result.Tags.Add(context.Game.ToString().ToLowerInvariant());

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

    private async Task<long?> TryCalculateS3SizeAsync(
        HttpClient client,
        GenLauncherVersionManifest manifest,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifest.S3HostLink) || string.IsNullOrWhiteSpace(manifest.S3BucketName))
        {
            return null;
        }

        var queryUrl = GenLauncherS3XmlParser.BuildS3QueryUrl(
            manifest.S3HostLink,
            manifest.S3BucketName,
            manifest.S3FolderName);

        var xml = await FetchStringWithCacheAsync(client, queryUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        var entries = GenLauncherS3XmlParser.ParseListBucketResult(
            xml,
            manifest.S3FolderName ?? string.Empty,
            manifest.S3HostLink,
            manifest.S3BucketName);

        if (entries.Count > 0)
        {
            var totalSize = entries.Sum(e => e.Size);
            if (totalSize > 0)
            {
                return totalSize;
            }
        }

        return null;
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

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
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
