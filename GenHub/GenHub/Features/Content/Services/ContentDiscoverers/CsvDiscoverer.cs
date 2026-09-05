using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers base game manifests from CSV catalogs.
/// Supports multi-language discovery for Generals and Zero Hour.
/// </summary>
public class CsvDiscoverer(
    ILogger<CsvDiscoverer> logger,
    IConfigurationProviderService configProvider,
    IHttpClientFactory httpClientFactory,
    CsvCatalogCache? catalogCache = null) : IContentDiscoverer, IDisposable
{
    private enum CsvCatalogSourceKind
    {
        IndexJson,
        ConfiguredCatalogs,
    }

    private sealed record CsvCatalogSource(
        CsvCatalogSourceKind Kind,
        string Description,
        IReadOnlyList<CsvCatalogRegistryEntry>? ConfiguredEntries)
    {
        public static CsvCatalogSource FromIndex(string source)
        {
            return new CsvCatalogSource(CsvCatalogSourceKind.IndexJson, source, null);
        }

        public static CsvCatalogSource FromConfiguredCatalogs(IReadOnlyList<CsvCatalogRegistryEntry> entries)
        {
            return new CsvCatalogSource(CsvCatalogSourceKind.ConfiguredCatalogs, "CsvValidationCatalogs configuration", entries);
        }
    }

    private sealed record CsvIndexContent(string Content, bool ShouldCache);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly CsvCatalogConfiguration _config = configProvider?.GetCsvCatalogConfiguration() ?? new CsvCatalogConfiguration();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private List<CsvCatalogRegistryEntry>? _cachedEntries;
    private bool _disposed;

    /// <inheritdoc />
    public string SourceName => CsvConstants.SourceName;

    /// <inheritdoc />
    public string Description => CsvConstants.Description;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.DirectSearch;

    /// <inheritdoc />
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query == null)
        {
            return OperationResult<ContentDiscoveryResult>.CreateFailure("Search query cannot be null.");
        }

        try
        {
            // If ContentType is specified and NOT GameInstallation, return empty result
            // This discoverer only provides base game installations
            if (query.ContentType.HasValue && query.ContentType.Value != ContentType.GameInstallation)
            {
                return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult());
            }

            var entries = await LoadCatalogEntriesAsync(cancellationToken);
            if (!TryFilterByGameType(entries, query.TargetGame, out var filteredEntries))
            {
                return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult());
            }

            var queryLanguage = ContentSearchQuery.NormalizeLanguage(query.Language);
            var results = new List<ContentSearchResult>();

            foreach (var entry in filteredEntries)
            {
                AddSearchResultsForEntry(results, entry, query.Language, queryLanguage);
            }

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = results,
                TotalItems = results.Count,
                HasMoreItems = false,
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover CSV catalogs");
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Disposes the resources used by the <see cref="CsvDiscoverer"/> instance.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Performs the actual disposal of resources.
    /// </summary>
    /// <param name="disposing">Indicates whether the method is being called from the Dispose method (true) or from a finalizer (false).</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing)
            {
                _cacheLock.Dispose();
            }
        }
    }

    private static List<CsvCatalogRegistryEntry> GetValidCatalogEntries(IEnumerable<CsvCatalogRegistryEntry>? entries)
    {
        return entries?
            .Where(e => e != null && e.IsActive && !string.IsNullOrWhiteSpace(e.Url) && !string.IsNullOrWhiteSpace(e.GameType) && !string.IsNullOrWhiteSpace(e.Version))
            .ToList() ?? [];
    }

    private static IReadOnlyList<string> GetLanguagesToInclude(
        CsvCatalogRegistryEntry entry,
        string? rawQueryLanguage,
        string queryLanguage)
    {
        var rawLanguages = entry.SupportedLanguages is { Count: > 0 }
            ? entry.SupportedLanguages
            : [CsvConstants.AllLanguagesFilter];

        var normalizedEntryLanguages = rawLanguages
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(ContentSearchQuery.NormalizeLanguage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(rawQueryLanguage) || string.Equals(queryLanguage, CsvConstants.AllLanguagesFilter, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedEntryLanguages;
        }

        if (normalizedEntryLanguages.Any(l => string.Equals(l, CsvConstants.AllLanguagesFilter, StringComparison.OrdinalIgnoreCase)))
        {
            return [queryLanguage];
        }

        return normalizedEntryLanguages
            .Where(l => string.Equals(l, queryLanguage, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private bool TryFilterByGameType(
        IReadOnlyList<CsvCatalogRegistryEntry> entries,
        GameType? targetGame,
        out IReadOnlyList<CsvCatalogRegistryEntry> filteredEntries)
    {
        if (!targetGame.HasValue)
        {
            filteredEntries = entries;
            return true;
        }

        string? targetGameStr = targetGame.Value switch
        {
            GameType.Generals => CsvConstants.GeneralsGameType,
            GameType.ZeroHour => CsvConstants.ZeroHourGameType,
            _ => null,
        };

        if (targetGameStr is null)
        {
            logger.LogWarning("Unsupported game type encountered: {GameType}. Returning no results.", targetGame.Value);
            filteredEntries = [];
            return false;
        }

        filteredEntries = entries
            .Where(e => e.GameType.Equals(targetGameStr, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return true;
    }

    private void AddSearchResultsForEntry(
        List<ContentSearchResult> results,
        CsvCatalogRegistryEntry entry,
        string? rawQueryLanguage,
        string queryLanguage)
    {
        var languagesToInclude = GetLanguagesToInclude(entry, rawQueryLanguage, queryLanguage);

        foreach (var language in languagesToInclude)
        {
            try
            {
                var result = CreateSearchResult(entry, language);
                if (result != null)
                {
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create search result for entry {Game} {Version} {Language}", entry.GameType, entry.Version, language);
            }
        }
    }

    private async Task<List<CsvCatalogRegistryEntry>> LoadCatalogEntriesAsync(CancellationToken cancellationToken)
    {
        // Return cached entries if available
        var cached = Volatile.Read(ref _cachedEntries);
        if (cached != null)
        {
            return cached;
        }

        if (_disposed)
        {
            return [];
        }

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedEntries != null)
            {
                return _cachedEntries;
            }

            List<CsvCatalogRegistryEntry> loadedEntries = [];

            foreach (var source in GetCatalogSources())
            {
                try
                {
                    loadedEntries = source.Kind == CsvCatalogSourceKind.IndexJson
                        ? await LoadEntriesFromIndexAsync(source.Description, cancellationToken)
                        : GetValidCatalogEntries(source.ConfiguredEntries);

                    if (loadedEntries.Count > 0)
                    {
                        logger.LogInformation("Loaded {Count} valid CSV catalog entries from {Source}", loadedEntries.Count, source.Description);
                        break;
                    }

                    logger.LogWarning("No valid active CSV catalog entries found in {Source}", source.Description);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to load CSV catalog entries from {Source}", source.Description);
                }
            }

            if (loadedEntries.Count > 0)
            {
                _cachedEntries = loadedEntries;
                return _cachedEntries;
            }

            return [];
        }
        finally
        {
            if (!_disposed)
            {
                _cacheLock.Release();
            }
        }
    }

    private IEnumerable<CsvCatalogSource> GetCatalogSources()
    {
        var configuredSource = _config.IndexFilePath?.Trim();
        var seenIndexSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var indexSource in new[] { configuredSource, CsvConstants.DefaultIndexFileUrl })
        {
            if (string.IsNullOrWhiteSpace(indexSource) || !seenIndexSources.Add(indexSource))
            {
                continue;
            }

            yield return CsvCatalogSource.FromIndex(indexSource);
        }

        if (_config.CsvValidationCatalogs is { Count: > 0 })
        {
            yield return CsvCatalogSource.FromConfiguredCatalogs(_config.CsvValidationCatalogs);
        }
    }

    private async Task<List<CsvCatalogRegistryEntry>> LoadEntriesFromIndexAsync(string indexSource, CancellationToken cancellationToken)
    {
        var indexContent = await LoadIndexJsonAsync(indexSource, cancellationToken);
        var index = JsonSerializer.Deserialize<CsvCatalogRegistryIndex>(indexContent.Content, JsonOptions);

        if (index?.Entries == null || index.Entries.Count == 0)
        {
            logger.LogWarning("No CSV catalog entries found in index.json from {Source}", indexSource);
            return [];
        }

        var entries = GetValidCatalogEntries(index.Entries);
        if (entries.Count > 0 && indexContent.ShouldCache && catalogCache != null)
        {
            await catalogCache.StoreAsync(indexSource, indexContent.Content, cancellationToken);
        }

        return entries;
    }

    private async Task<CsvIndexContent> LoadIndexJsonAsync(string indexPath, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(indexPath, UriKind.Absolute, out var indexUri) &&
            (indexUri.Scheme == Uri.UriSchemeHttp || indexUri.Scheme == Uri.UriSchemeHttps))
        {
            var cached = catalogCache == null
                ? null
                : await catalogCache.ReadAsync(indexPath, cancellationToken);
            if (cached?.IsFresh == true)
            {
                return new CsvIndexContent(cached.Content, false);
            }

            try
            {
                var httpClient = httpClientFactory.CreateClient(string.Empty);
                var content = await httpClient.GetStringAsync(indexUri, cancellationToken);
                return new CsvIndexContent(content, true);
            }
            catch (Exception ex) when (cached != null && IsRecoverableRemoteFailure(ex, cancellationToken))
            {
                logger.LogWarning(ex, "Remote CSV index {IndexPath} is unavailable; using stale cached content", indexPath);
                return new CsvIndexContent(cached.Content, false);
            }
        }

        var resolvedPath = Path.IsPathRooted(indexPath)
            ? indexPath
            : Path.GetFullPath(indexPath);

        var localContent = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
        return new CsvIndexContent(localContent, false);
    }

    private bool IsRecoverableRemoteFailure(Exception exception, CancellationToken cancellationToken)
    {
        return exception is HttpRequestException or IOException ||
            (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);
    }

    private ContentSearchResult? CreateSearchResult(CsvCatalogRegistryEntry entry, string language)
    {
        if (!Enum.TryParse<GameType>(entry.GameType, true, out var gameType) ||
            gameType == GameType.Unknown ||
            !Enum.IsDefined(gameType))
        {
            logger.LogWarning("Invalid game type in catalog entry: {GameType}", entry.GameType);
            return null;
        }

        var canonicalGameType = gameType switch
        {
            GameType.Generals => CsvConstants.GeneralsGameType,
            GameType.ZeroHour => CsvConstants.ZeroHourGameType,
            _ => entry.GameType,
        };

        var contentName = $"{canonicalGameType}-{entry.Version}-{language}";

        var id = ManifestIdGenerator.GeneratePublisherContentId(
            PublisherTypeConstants.CsvRegistry,
            ContentType.GameInstallation,
            contentName);

        var result = new ContentSearchResult
        {
            Id = id,
            Name = $"{canonicalGameType} {entry.Version} ({language})",
            Description = $"Base game installation files for {canonicalGameType} v{entry.Version}. Language: {language}",
            Version = entry.Version,
            ContentType = ContentType.GameInstallation,
            TargetGame = gameType,
            ProviderName = SourceName,
            RequiresResolution = true,
            ResolverId = CsvConstants.ResolverId,
            SourceUrl = entry.Url,
            DownloadSize = entry.TotalSizeBytes,
        };

        result.ResolverMetadata[CsvConstants.CsvUrlMetadataKey] = entry.Url;
        result.ResolverMetadata[CsvConstants.GameTypeMetadataKey] = canonicalGameType;
        result.ResolverMetadata[CsvConstants.VersionMetadataKey] = entry.Version;
        result.ResolverMetadata[CsvConstants.LanguageMetadataKey] = language;

        if (entry.FileCount.HasValue)
        {
            result.ResolverMetadata[CsvConstants.FileCountMetadataKey] = entry.FileCount.Value.ToString();
        }

        return result;
    }
}
