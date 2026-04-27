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
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers base game manifests from CSV catalogs.
/// Supports multi-language discovery for Generals and Zero Hour.
/// </summary>
public class CSVDiscoverer : IContentDiscoverer, IDisposable
{
    private enum CsvCatalogSourceKind
    {
        IndexJson,
        ConfiguredCatalogs,
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<CSVDiscoverer> _logger;
    private readonly CsvCatalogConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private List<CsvCatalogRegistryEntry>? _cachedEntries;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CSVDiscoverer"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configProvider">The configuration provider service.</param>
    /// <param name="httpClientFactory">The HTTP client factory used for remote index sources.</param>
    public CSVDiscoverer(
        ILogger<CSVDiscoverer> logger,
        IConfigurationProviderService configProvider,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _config = configProvider.GetCsvCatalogConfiguration() ?? new CsvCatalogConfiguration();
        _httpClientFactory = httpClientFactory;
    }

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
        try
        {
            // If ContentType is specified and NOT GameInstallation, return empty result
            // This discoverer ONLY provides base game installations
            if (query.ContentType.HasValue && query.ContentType.Value != ContentType.GameInstallation)
            {
                return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult());
            }

            var entries = await LoadCatalogEntriesAsync(cancellationToken);
            var results = new List<ContentSearchResult>();

            // Filter by GameType if specified
            var filteredEntries = entries;
            if (query.TargetGame.HasValue)
            {
                string? targetGameStr = query.TargetGame.Value switch
                {
                    GameType.Generals => CsvConstants.GeneralsGameType,
                    GameType.ZeroHour => CsvConstants.ZeroHourGameType,
                    _ => null,
                };

                if (targetGameStr is null)
                {
                    _logger.LogWarning("Unsupported game type encountered: {GameType}. Returning no results.", query.TargetGame.Value);
                    return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult());
                }

                filteredEntries = filteredEntries.Where(e => e.GameType.Equals(targetGameStr, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            foreach (var entry in filteredEntries)
            {
                var normalizedQueryLanguage = !string.IsNullOrWhiteSpace(query.Language)
                    ? NormalizeLanguage(query.Language)
                    : null;
                var normalizedEntryLanguages = (entry.SupportedLanguages ?? [])
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(NormalizeLanguage)
                    .ToList();

                List<string> languagesToInclude;
                if (string.IsNullOrWhiteSpace(query.Language) || normalizedQueryLanguage == CsvConstants.AllLanguagesFilter)
                {
                    languagesToInclude = normalizedEntryLanguages;
                }
                else if (normalizedEntryLanguages.Contains(CsvConstants.AllLanguagesFilter))
                {
                    languagesToInclude = [normalizedQueryLanguage!];
                }
                else
                {
                    languagesToInclude = normalizedEntryLanguages
                        .Where(l => l == normalizedQueryLanguage)
                        .ToList();
                }

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
                        _logger.LogWarning(ex, "Failed to create search result for entry {Game} {Version} {Language}", entry.GameType, entry.Version, language);
                    }
                }
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
            _logger.LogError(ex, "Failed to discover CSV catalogs");
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Disposes the resources used by the <see cref="CSVDiscoverer"/> instance.
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
            if (disposing)
            {
                _cacheLock.Dispose();
            }

            _disposed = true;
        }
    }

    private static string NormalizeLanguage(string language)
    {
        var trimmed = language.Trim();
        if (string.Equals(trimmed, CsvConstants.AllLanguagesFilter, StringComparison.OrdinalIgnoreCase))
        {
            return CsvConstants.AllLanguagesFilter;
        }

        return trimmed.ToUpperInvariant();
    }

    private static List<CsvCatalogRegistryEntry> GetValidCatalogEntries(IEnumerable<CsvCatalogRegistryEntry>? entries)
    {
        return entries?
            .Where(e => e != null && e.IsActive && !string.IsNullOrWhiteSpace(e.Url) && !string.IsNullOrWhiteSpace(e.GameType) && !string.IsNullOrWhiteSpace(e.Version))
            .ToList() ?? [];
    }

    private async Task<List<CsvCatalogRegistryEntry>> LoadCatalogEntriesAsync(CancellationToken cancellationToken)
    {
        // Return cached entries if available
        var cached = Volatile.Read(ref _cachedEntries);
        if (cached != null)
        {
            return cached;
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
                        _logger.LogInformation("Loaded {Count} valid CSV catalog entries from {Source}", loadedEntries.Count, source.Description);
                        break;
                    }

                    _logger.LogWarning("No valid active CSV catalog entries found in {Source}", source.Description);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load CSV catalog entries from {Source}", source.Description);
                }
            }

            _cachedEntries = loadedEntries;
            return _cachedEntries;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private IEnumerable<CsvCatalogSource> GetCatalogSources()
    {
        var configuredSource = _config.IndexFilePath?.Trim();
        var seenIndexSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var indexSource in new[] { CsvConstants.DefaultIndexFileUrl, configuredSource })
        {
            if (string.IsNullOrWhiteSpace(indexSource) || !seenIndexSources.Add(indexSource))
            {
                continue;
            }

            yield return CsvCatalogSource.FromIndex(indexSource);
        }

        if (_config.CsvValidationCatalogs != null && _config.CsvValidationCatalogs.Count > 0)
        {
            yield return CsvCatalogSource.FromConfiguredCatalogs(_config.CsvValidationCatalogs);
        }
    }

    private async Task<List<CsvCatalogRegistryEntry>> LoadEntriesFromIndexAsync(string indexSource, CancellationToken cancellationToken)
    {
        var json = await LoadIndexJsonAsync(indexSource, cancellationToken);
        var index = JsonSerializer.Deserialize<CsvCatalogRegistryIndex>(json, JsonOptions);

        if (index?.Entries == null || index.Entries.Count == 0)
        {
            _logger.LogWarning("No CSV catalog entries found in index.json from {Source}", indexSource);
            return [];
        }

        return GetValidCatalogEntries(index.Entries);
    }

    private async Task<string> LoadIndexJsonAsync(string indexPath, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(indexPath, UriKind.Absolute, out var indexUri) &&
            (indexUri.Scheme == Uri.UriSchemeHttp || indexUri.Scheme == Uri.UriSchemeHttps))
        {
            var httpClient = _httpClientFactory.CreateClient(string.Empty);
            return await httpClient.GetStringAsync(indexUri, cancellationToken);
        }

        return await File.ReadAllTextAsync(indexPath, cancellationToken);
    }

    private ContentSearchResult? CreateSearchResult(CsvCatalogRegistryEntry entry, string language)
    {
        if (!Enum.TryParse<GameType>(entry.GameType, true, out var gameType))
        {
            _logger.LogWarning("Invalid game type in catalog entry: {GameType}", entry.GameType);
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
            DownloadSize = 0,
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
}
