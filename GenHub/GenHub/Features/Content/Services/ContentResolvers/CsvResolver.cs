using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentResolvers;

/// <summary>
/// Resolves CSV catalog search results into complete content manifests.
/// </summary>
public class CsvResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<CsvResolver> logger,
    CsvCatalogCache? catalogCache = null) : IContentResolver
{
    private static readonly CsvConfiguration CsvConfig = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        MissingFieldFound = null,
        HeaderValidated = null,
        BadDataFound = null,
    };

    /// <inheritdoc />
    public string ResolverId => CsvConstants.ResolverId;

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        if (discoveredItem == null)
        {
            return OperationResult<ContentManifest>.CreateFailure("Discovered content item cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(discoveredItem.SourceUrl))
        {
            return OperationResult<ContentManifest>.CreateFailure("Discovered content source URL is missing.");
        }

        try
        {
            logger.LogInformation("Resolving CSV catalog manifest from {SourceUrl}", discoveredItem.SourceUrl);

            var loadResult = await LoadCsvContentAsync(discoveredItem.SourceUrl, cancellationToken);
            if (!loadResult.Success || loadResult.Data == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(loadResult.Errors);
            }

            var gameTypeStr = GetGameTypeString(discoveredItem);
            var languageStr = GetLanguageString(discoveredItem);
            var version = GetVersionString(discoveredItem);

            var matchingEntries = ParseAndFilterCsv(loadResult.Data, gameTypeStr, languageStr);
            if (matchingEntries.Count == 0)
            {
                logger.LogWarning(
                    "No matching files found in CSV catalog at {SourceUrl} for game {GameType} and language {Language}",
                    discoveredItem.SourceUrl,
                    gameTypeStr,
                    languageStr);
                return OperationResult<ContentManifest>.CreateFailure(
                    $"No matching files found in CSV catalog for {gameTypeStr} ({languageStr}).");
            }

            var isRemote = Uri.TryCreate(discoveredItem.SourceUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

            var manifestFiles = matchingEntries.Select(e => CreateManifestFile(e, isRemote)).ToList();
            var manifest = BuildManifest(discoveredItem, gameTypeStr, version, languageStr, manifestFiles);

            logger.LogInformation(
                "Successfully resolved CSV catalog manifest {ManifestId} with {FileCount} files",
                manifest.Id.Value,
                manifest.Files.Count);

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve CSV catalog manifest from {SourceUrl}", discoveredItem.SourceUrl);
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<ContentManifest>> ResolveAsync(
        ProviderDefinition? provider,
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        return ResolveAsync(discoveredItem, cancellationToken);
    }

    private static string GetGameTypeString(ContentSearchResult item)
    {
        if (item.ResolverMetadata.TryGetValue(CsvConstants.GameTypeMetadataKey, out var gameType) && !string.IsNullOrWhiteSpace(gameType))
        {
            return gameType;
        }

        return item.TargetGame switch
        {
            GameType.Generals => CsvConstants.GeneralsGameType,
            GameType.ZeroHour => CsvConstants.ZeroHourGameType,
            _ => item.TargetGame != GameType.Unknown ? item.TargetGame.ToString() : string.Empty,
        };
    }

    private static string GetLanguageString(ContentSearchResult item)
    {
        if (item.ResolverMetadata.TryGetValue(CsvConstants.LanguageMetadataKey, out var language) && !string.IsNullOrWhiteSpace(language))
        {
            return ContentSearchQuery.NormalizeLanguage(language);
        }

        return CsvConstants.AllLanguagesFilter;
    }

    private static string GetVersionString(ContentSearchResult item)
    {
        if (item.ResolverMetadata.TryGetValue(CsvConstants.VersionMetadataKey, out var version) && !string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        return !string.IsNullOrWhiteSpace(item.Version) ? item.Version : "1.0";
    }

    private static List<CsvCatalogEntry> ParseAndFilterCsv(string csvContent, string targetGame, string targetLanguage)
    {
        using var stringReader = new StringReader(csvContent);
        using var csvReader = new CsvReader(stringReader, CsvConfig);

        var records = csvReader.GetRecords<CsvCatalogEntry>().ToList();
        var matchingEntries = new List<CsvCatalogEntry>();

        foreach (var record in records)
        {
            if (IsUnsafeRelativePath(record.RelativePath))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(targetGame) &&
                !string.Equals(record.GameType, targetGame, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (MatchesLanguage(record.Language, targetLanguage))
            {
                matchingEntries.Add(record);
            }
        }

        return matchingEntries;
    }

    private static bool IsUnsafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        if (Path.IsPathRooted(path) || path.StartsWith('/') || path.StartsWith('\\'))
        {
            return true;
        }

        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            return true;
        }

        return path.Contains("..", StringComparison.Ordinal);
    }

    private static bool MatchesLanguage(string? entryLanguage, string targetLanguage)
    {
        if (string.Equals(targetLanguage, CsvConstants.AllLanguagesFilter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(entryLanguage) ||
            string.Equals(entryLanguage, CsvConstants.AllLanguagesFilter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedEntryLang = ContentSearchQuery.NormalizeLanguage(entryLanguage);
        return string.Equals(normalizedEntryLang, targetLanguage, StringComparison.OrdinalIgnoreCase);
    }

    private static ManifestFile CreateManifestFile(CsvCatalogEntry entry, bool isRemote)
    {
        var hasSha256 = !string.IsNullOrWhiteSpace(entry.Sha256);
        var hash = hasSha256 ? entry.Sha256 : string.Empty;

        var hasValidDownloadUrl = isRemote &&
            !string.IsNullOrWhiteSpace(entry.DownloadUrl) &&
            Uri.TryCreate(entry.DownloadUrl, UriKind.Absolute, out var url) &&
            (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);

        ContentSourceType sourceType;
        if (!isRemote)
        {
            sourceType = ContentSourceType.LocalFile;
        }
        else if (hasValidDownloadUrl)
        {
            sourceType = ContentSourceType.RemoteDownload;
        }
        else
        {
            sourceType = ContentSourceType.GameInstallation;
        }

        return new ManifestFile
        {
            RelativePath = entry.RelativePath,
            Size = entry.Size,
            Hash = hash,
            SourceType = sourceType,
            InstallTarget = ContentInstallTarget.Workspace,
            IsRequired = entry.IsRequired,
            DownloadUrl = hasValidDownloadUrl ? entry.DownloadUrl : null,
            IsExecutable = entry.RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static GameType ResolveTargetGame(ContentSearchResult discoveredItem, string gameTypeStr)
    {
        if (discoveredItem.TargetGame != GameType.Unknown)
        {
            return discoveredItem.TargetGame;
        }

        if (Enum.TryParse<GameType>(gameTypeStr, true, out var gt))
        {
            return gt;
        }

        return GameType.Unknown;
    }

    private static ContentManifest BuildManifest(
        ContentSearchResult discoveredItem,
        string gameTypeStr,
        string version,
        string languageStr,
        IReadOnlyList<ManifestFile> files)
    {
        var targetGame = ResolveTargetGame(discoveredItem, gameTypeStr);

        var contentName = $"{gameTypeStr}-{version}-{languageStr}";
        var manifestId = !string.IsNullOrWhiteSpace(discoveredItem.Id)
            ? new ManifestId(discoveredItem.Id)
            : new ManifestId(ManifestIdGenerator.GeneratePublisherContentId(
                PublisherTypeConstants.CsvRegistry,
                ContentType.GameInstallation,
                contentName));

        var manifest = new ContentManifest
        {
            Id = manifestId,
            Name = discoveredItem.Name,
            Version = version,
            ContentType = ContentType.GameInstallation,
            TargetGame = targetGame,
            Publisher = new PublisherInfo
            {
                PublisherType = PublisherTypeConstants.CsvRegistry,
                Name = CsvConstants.SourceName,
            },
            Metadata = new ContentMetadata
            {
                Description = discoveredItem.Description ?? string.Empty,
                ReleaseDate = DateTime.UtcNow,
            },
            OriginalProviderName = CsvConstants.SourceName,
            OriginalContentId = discoveredItem.Id,
            SourcePath = discoveredItem.SourceUrl,
            Files = files.ToList(),
        };

        return manifest;
    }

    private async Task<OperationResult<string>> LoadCsvContentAsync(string sourceUrl, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var cached = catalogCache == null
                ? null
                : await catalogCache.ReadAsync(sourceUrl, cancellationToken);
            if (cached?.IsFresh == true)
            {
                return OperationResult<string>.CreateSuccess(cached.Content);
            }

            try
            {
                var httpClient = httpClientFactory.CreateClient(string.Empty);
                var content = await httpClient.GetStringAsync(uri, cancellationToken);
                if (catalogCache != null)
                {
                    await catalogCache.StoreAsync(sourceUrl, content, cancellationToken);
                }

                return OperationResult<string>.CreateSuccess(content);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && cached != null)
            {
                logger.LogWarning("Remote CSV catalog {SourceUrl} timed out; using stale cached content", sourceUrl);
                return OperationResult<string>.CreateSuccess(cached.Content);
            }
            catch (HttpRequestException) when (cached != null)
            {
                logger.LogWarning("Remote CSV catalog {SourceUrl} is unavailable; using stale cached content", sourceUrl);
                return OperationResult<string>.CreateSuccess(cached.Content);
            }
            catch (IOException) when (cached != null)
            {
                logger.LogWarning("Remote CSV catalog {SourceUrl} could not be read; using stale cached content", sourceUrl);
                return OperationResult<string>.CreateSuccess(cached.Content);
            }
        }

        var resolvedPath = Path.IsPathRooted(sourceUrl)
            ? sourceUrl
            : Path.GetFullPath(sourceUrl);

        if (!File.Exists(resolvedPath))
        {
            return OperationResult<string>.CreateFailure($"CSV file not found at: {resolvedPath}");
        }

        var fileContent = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
        return OperationResult<string>.CreateSuccess(fileContent);
    }
}
