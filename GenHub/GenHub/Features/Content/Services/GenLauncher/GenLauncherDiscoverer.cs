using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Discovers GenLauncher content by querying the root YAML catalogs for Zero Hour and Generals,
/// traversing child manifests, and mapping to ContentSearchResult objects.
/// </summary>
public class GenLauncherDiscoverer : IContentDiscoverer
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProviderDefinitionLoader _providerLoader;
    private readonly GenLauncherCatalogParser _catalogParser;
    private readonly ILogger<GenLauncherDiscoverer> _logger;
    private readonly ConcurrentDictionary<string, (DateTime CachedAt, string Content)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="GenLauncherDiscoverer"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory for creating HTTP clients.</param>
    /// <param name="providerLoader">Loader for provider definitions.</param>
    /// <param name="catalogParser">Parser for GenLauncher catalog YAML documents.</param>
    /// <param name="logger">Logger instance.</param>
    public GenLauncherDiscoverer(
        IHttpClientFactory httpClientFactory,
        IProviderDefinitionLoader providerLoader,
        GenLauncherCatalogParser catalogParser,
        ILogger<GenLauncherDiscoverer> logger)
    {
        _httpClientFactory = httpClientFactory;
        _providerLoader = providerLoader;
        _catalogParser = catalogParser;
        _logger = logger;
    }

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
            provider ??= _providerLoader.GetProvider(GenLauncherConstants.PublisherId);
            var client = _httpClientFactory.CreateClient(PublisherTypeConstants.GenLauncher);
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
            _logger.LogError(ex, "Error during GenLauncher content discovery");
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

    private static string? CleanImageUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        if (rawUrl.StartsWith("https://cdn.discordapp.com/attachments/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return rawUrl;
    }

    private static string? ResolveIconUrl(string? rawUrl, string? fallbackUrl)
    {
        var cleaned = CleanImageUrl(rawUrl);
        return !string.IsNullOrWhiteSpace(cleaned) ? cleaned : CleanImageUrl(fallbackUrl);
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
            _logger.LogWarning("Could not fetch root catalog from {Url}", catalogUrl);
            return items;
        }

        GenLauncherRootManifest rootManifest;
        try
        {
            rootManifest = _catalogParser.ParseRootCatalog(rootYaml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse root catalog YAML for {Game}", game);
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
            _logger.LogWarning("Rejecting mod {ModName} with unsafe ModLink: {Url}", modEntry.ModName, modEntry.ModLink);
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

        GenLauncherVersionManifest? mainManifest = null;
        if (!string.IsNullOrWhiteSpace(modEntry.ModLink))
        {
            var manifestYaml = await FetchStringWithCacheAsync(client, modEntry.ModLink, cancellationToken);
            if (!string.IsNullOrWhiteSpace(manifestYaml))
            {
                try
                {
                    mainManifest = _catalogParser.ParseVersionManifest(manifestYaml);
                    EnrichSearchResult(mainResult, mainManifest, modEntry.ModLink);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse version manifest for mod {ModName}", modEntry.ModName);
                }
            }
        }

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

        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = await ProcessChildManifestAsync(
                context.Client,
                url,
                context.Game,
                contentType,
                context.ModName,
                context.ModSlug,
                context.ParentIconUrl,
                cancellationToken);
            if (item == null)
            {
                continue;
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

    private sealed record ModProcessingContext(
        HttpClient Client,
        GameType Game,
        string ModName,
        string ModSlug,
        string? ParentIconUrl,
        List<ContentSearchResult> Results,
        List<ContentVariantInfo> Variants,
        List<ContentSection> FilesSections);

    private async Task<ContentSearchResult?> ProcessChildManifestAsync(
        HttpClient client,
        string manifestUrl,
        GameType game,
        ContentType defaultType,
        string parentModName,
        string parentModSlug,
        string? parentIconUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            return null;
        }

        var yaml = await FetchStringWithCacheAsync(client, manifestUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }

        GenLauncherVersionManifest versionManifest;
        try
        {
            versionManifest = _catalogParser.ParseVersionManifest(yaml);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse child version manifest from {Url}", manifestUrl);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(versionManifest.SimpleDownloadLink) && !IsValidHttpUrl(versionManifest.SimpleDownloadLink, out _))
        {
            _logger.LogWarning("Rejecting child manifest {Name} with unsafe download link: {Url}", versionManifest.Name, versionManifest.SimpleDownloadLink);
            return null;
        }

        var slug = GenLauncherCatalogParser.Slugify(versionManifest.Name);
        var actualType = versionManifest.ModificationType != null
            ? GenLauncherCatalogParser.MapContentType(versionManifest.GetParsedType())
            : defaultType;

        var result = new ContentSearchResult
        {
            Id = $"genlauncher-{game.ToString().ToLowerInvariant()}-{slug}",
            Name = versionManifest.Name,
            Version = versionManifest.Version,
            ContentType = actualType,
            TargetGame = game,
            ProviderName = PublisherTypeConstants.GenLauncher,
            ResolverId = GenLauncherConstants.PublisherId,
            SourceUrl = versionManifest.SimpleDownloadLink ?? manifestUrl,
            IconUrl = ResolveIconUrl(versionManifest.UIImageSourceLink, parentIconUrl),
            RequiresResolution = true,
            VariantGroupId = parentModSlug,
            VariantFamilyName = parentModName,
        };

        result.Tags.Add("genlauncher");
        result.Tags.Add(actualType.ToString().ToLowerInvariant());
        result.Tags.Add(game.ToString().ToLowerInvariant());

        EnrichSearchResult(result, versionManifest, manifestUrl, parentIconUrl);
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

        var tasks = urls.Select(async url =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await ProcessChildManifestAsync(client, url, game, contentType, familyName, familySlug, null, cancellationToken);
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

    private static bool IsValidHttpUrl(string url, out Uri? uri)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (uri.IsLoopback ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IPAddress.TryParse(uri.DnsSafeHost, out var ip) || IPAddress.TryParse(uri.Host, out ip))
        {
            return IsSafeIpAddress(ip);
        }

        return true;
    }

    private static bool IsSafeIpAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None))
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        return ip.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => IsSafeIPv4(bytes),
            System.Net.Sockets.AddressFamily.InterNetworkV6 => IsSafeIPv6(bytes),
            _ => false,
        };
    }

    private static bool IsSafeIPv4(byte[] b)
    {
        return (b[0], b[1], b[2]) switch
        {
            (0 or 10 or 127, _, _) => false,
            (>= 224, _, _) => false,
            (100, >= 64 and <= 127, _) => false,
            (169, 254, _) => false,
            (172, >= 16 and <= 31, _) => false,
            (192, 0, 0 or 2) => false,
            (192, 168, _) => false,
            (198, 18 or 19, _) => false,
            (198, 51, 100) => false,
            (203, 0, 113) => false,
            _ => true,
        };
    }

    private static bool IsSafeIPv6(byte[] b)
    {
        if (b.Take(15).All(x => x == 0) && b[15] == 1)
        {
            return false;
        }

        if (b.All(x => x == 0))
        {
            return false;
        }

        if ((b[0] & 0xfe) == 0xfc)
        {
            return false;
        }

        if (b[0] == 0xfe && (b[1] & 0xc0) == 0x80)
        {
            return false;
        }

        if (b[0] == 0xff)
        {
            return false;
        }

        if (b.Take(10).All(x => x == 0) && b[10] == 0xff && b[11] == 0xff)
        {
            return IsSafeIPv4([b[12], b[13], b[14], b[15]]);
        }

        return true;
    }

    private async Task<long?> TryCalculateDownloadSizeAsync(
        HttpClient client,
        GenLauncherVersionManifest manifest,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(manifest.S3HostLink) && !string.IsNullOrWhiteSpace(manifest.S3BucketName))
            {
                var queryUrl = GenLauncherS3XmlParser.BuildS3QueryUrl(
                    manifest.S3HostLink,
                    manifest.S3BucketName,
                    manifest.S3FolderName);

                var xml = await FetchStringWithCacheAsync(client, queryUrl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(xml))
                {
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
                }
            }

            if (!string.IsNullOrWhiteSpace(manifest.SimpleDownloadLink) &&
                Uri.TryCreate(manifest.SimpleDownloadLink, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(3));
                using var req = new HttpRequestMessage(HttpMethod.Head, uri);
                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentLength.HasValue && resp.Content.Headers.ContentLength.Value > 0)
                {
                    return resp.Content.Headers.ContentLength.Value;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to probe download size for {Name}", manifest.Name);
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
            _logger.LogWarning("Rejecting unsafe or non-HTTP URL: {Url}", url);
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
                _logger.LogWarning("HTTP GET failed with {StatusCode} for {Url}", response.StatusCode, url);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            _cache[url] = (DateTime.UtcNow, content);
            return content;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception fetching URL {Url}", url);
            return null;
        }
    }
}
