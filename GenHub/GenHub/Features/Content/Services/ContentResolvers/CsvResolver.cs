using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
    private sealed record CsvContentLoadResult(string Content, bool ShouldCache, byte[] RawBytes);

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

            if (discoveredItem.ResolverMetadata.TryGetValue(CsvConstants.Sha256MetadataKey, out var expectedSha256) &&
                !string.IsNullOrWhiteSpace(expectedSha256))
            {
                var actualHash = Convert.ToHexString(SHA256.HashData(loadResult.Data.RawBytes)).ToLowerInvariant();
                if (!string.Equals(actualHash, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogError(
                        "CSV catalog SHA-256 integrity verification failed for {SourceUrl}. Expected: {Expected}, Actual: {Actual}",
                        discoveredItem.SourceUrl,
                        expectedSha256,
                        actualHash);

                    return OperationResult<ContentManifest>.CreateFailure(
                        $"CSV catalog integrity check failed for {discoveredItem.SourceUrl}");
                }

                logger.LogDebug(
                    "CSV catalog SHA-256 verified successfully for {SourceUrl}",
                    discoveredItem.SourceUrl);
            }

            var gameTypeStr = GetGameTypeString(discoveredItem);
            var languageStr = GetLanguageString(discoveredItem);
            var version = GetVersionString(discoveredItem);

            var matchingEntries = ParseAndFilterCsv(loadResult.Data.Content, gameTypeStr, languageStr);
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

            if (loadResult.Data.ShouldCache && catalogCache != null)
            {
                await catalogCache.StoreAsync(discoveredItem.SourceUrl, loadResult.Data.Content, cancellationToken);
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

    private static bool IsRecoverableRemoteFailure(Exception exception, CancellationToken cancellationToken)
    {
        return exception is HttpRequestException or IOException ||
            (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);
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

    private async Task<OperationResult<CsvContentLoadResult>> LoadCsvContentAsync(string sourceUrl, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                return OperationResult<CsvContentLoadResult>.CreateFailure($"CSV catalog must use HTTPS: {uri}");
            }

            var cached = catalogCache == null
                ? null
                : await catalogCache.ReadAsync(sourceUrl, cancellationToken);
            if (cached?.IsFresh == true)
            {
                var cachedBytes = Encoding.UTF8.GetBytes(cached.Content);
                return OperationResult<CsvContentLoadResult>.CreateSuccess(new CsvContentLoadResult(cached.Content, false, cachedBytes));
            }

            try
            {
                var httpClient = httpClientFactory.CreateClient(string.Empty);
                using var response = await httpClient.GetAsync(uri, cancellationToken);
                response.EnsureSuccessStatusCode();
                var responseUri = response.RequestMessage?.RequestUri;
                if (responseUri?.Scheme != Uri.UriSchemeHttps)
                {
                    throw new HttpRequestException($"CSV catalog redirect must use HTTPS: {responseUri}");
                }

                var rawBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var content = Encoding.UTF8.GetString(rawBytes).TrimStart('\uFEFF');
                return OperationResult<CsvContentLoadResult>.CreateSuccess(new CsvContentLoadResult(content, true, rawBytes));
            }
            catch (Exception ex) when (cached != null && IsRecoverableRemoteFailure(ex, cancellationToken))
            {
                logger.LogWarning(ex, "Remote CSV catalog {SourceUrl} is unavailable; using stale cached content", sourceUrl);
                var cachedBytes = Encoding.UTF8.GetBytes(cached.Content);
                return OperationResult<CsvContentLoadResult>.CreateSuccess(new CsvContentLoadResult(cached.Content, false, cachedBytes));
            }
        }

        var resolvedPath = Path.IsPathRooted(sourceUrl)
            ? sourceUrl
            : Path.GetFullPath(sourceUrl);

        if (!File.Exists(resolvedPath))
        {
            return OperationResult<CsvContentLoadResult>.CreateFailure($"CSV file not found at: {resolvedPath}");
        }

        var fileBytes = await File.ReadAllBytesAsync(resolvedPath, cancellationToken);
        var fileContent = Encoding.UTF8.GetString(fileBytes).TrimStart('\uFEFF');
        return OperationResult<CsvContentLoadResult>.CreateSuccess(new CsvContentLoadResult(fileContent, false, fileBytes));
    }
}
