using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentDeliverers;

/// <summary>
/// CSV content deliverer that downloads and parses CSV files to create content manifests.
/// Implements <see cref="IContentResolver"/> for CSV-based content discovery and resolution.
/// </summary>
/// <remarks>
/// This class is responsible for resolving discovered CSV content items into full <see cref="ContentManifest"/> objects.
/// It downloads CSV files from URLs specified in the discovery metadata, parses the content, and creates
/// <see cref="ManifestFile"/> entries for each valid file entry in the CSV.
/// The resolver supports filtering by game type and version to ensure only relevant files are included.
/// </remarks>
public class CSVDeliverer(HttpClient httpClient, ILogger<CSVDeliverer> logger) : IContentResolver
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ILogger<CSVDeliverer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Gets the resolver identifier for CSV content.
    /// </summary>
    /// <returns>A string identifier used to match this resolver with discovered content.</returns>
    /// <remarks>
    /// This identifier is used by the content discovery system to associate discovered
    /// <see cref="ContentSearchResult"/> items with this resolver implementation.
    /// The value "CSVResolver" indicates that this resolver handles CSV-based content.
    /// </remarks>
    public string ResolverId => CsvConstants.CsvResolverId;

    /// <summary>
    /// Resolve a discovered CSV item into a <see cref="ContentManifest"/> by downloading and parsing the CSV,
    /// filtering rows by game, and producing <see cref="ManifestFile"/> entries.
    /// </summary>
    /// <param name="discoveredItem">The discovered content item containing CSV metadata.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing the resolved <see cref="ContentManifest"/>.</returns>
    /// <remarks>
    /// This method performs the following steps:
    /// <list type="number">
    /// <item>Extracts the CSV URL from the <paramref name="discoveredItem"/> using multiple fallback strategies</item>
    /// <item>Determines the target game type from discovery metadata</item>
    /// <item>Downloads the CSV file from the extracted URL</item>
    /// <item>Parses the CSV content line by line, validating each entry</item>
    /// <item>Filters entries based on game type and version criteria</item>
    /// <item>Creates <see cref="ManifestFile"/> objects for valid entries</item>
    /// <item>Constructs and returns a complete <see cref="ContentManifest"/></item>
    /// </list>
    /// The method supports cancellation through the <paramref name="cancellationToken"/> and handles
    /// various error conditions gracefully by returning appropriate failure results.
    /// </remarks>
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        if (discoveredItem == null)
        {
            return OperationResult<ContentManifest>.CreateFailure("discoveredItem cannot be null");
        }

        try
        {
            // Obtain CSV URL from common locations
            string? csvUrl = null;

            // Try ResolverMetadata (common pattern in other discoverers)
            try
            {
                // Some ContentSearchResult implementations expose a dictionary property named ResolverMetadata
                // We'll attempt to read it via dynamic/Reflection style if typed property isn't available.
                var resolverMetadata = discoveredItem.GetType().GetProperty("ResolverMetadata")?.GetValue(discoveredItem);
                if (resolverMetadata is IDictionary<string, string> dict && dict.TryGetValue("csvUrl", out var url))
                {
                    csvUrl = url;
                }
            }
            catch
            {
                // Ignore reflection failures; we'll try other locations below
            }

            // Fallback to SourceUrl
            if (string.IsNullOrWhiteSpace(csvUrl) && !string.IsNullOrWhiteSpace(discoveredItem.SourceUrl))
            {
                csvUrl = discoveredItem.SourceUrl;
            }

            // Fallback to generic Metadata dictionary if present
            if (string.IsNullOrWhiteSpace(csvUrl))
            {
                try
                {
                    var metadataProperty = discoveredItem.GetType().GetProperty("Metadata");
                    if (metadataProperty != null)
                    {
                        var metadataValue = metadataProperty.GetValue(discoveredItem);
                        if (metadataValue is IDictionary<string, string> metadataDict && metadataDict.TryGetValue("csvUrl", out var url))
                        {
                            csvUrl = url;
                        }
                    }
                }
                catch
                {
                    // Ignore
                }
            }

            if (string.IsNullOrWhiteSpace(csvUrl))
            {
                _logger.LogError(CsvConstants.CsvUrlMissingLog, discoveredItem?.Id);
                return OperationResult<ContentManifest>.CreateFailure(CsvConstants.CsvUrlNotProvidedError);
            }

            // Determine requested target game (if the discoverer set it)
            var targetGame = discoveredItem.TargetGame != default ? discoveredItem.TargetGame : (GameType?)null;

            // Also allow resolver metadata 'game' fallback
            try
            {
                var resolverMetadata = discoveredItem.GetType().GetProperty("ResolverMetadata")?.GetValue(discoveredItem);
                if ((targetGame == null) && resolverMetadata is IDictionary<string, string> dict && dict.TryGetValue("game", out var gameString))
                {
                    if (Enum.TryParse<GameType>(gameString, true, out var parsedGame))
                    {
                        targetGame = parsedGame;
                    }
                }
            }
            catch
            {
                // Ignore
            }

            // Download CSV as stream
            using var response = await _httpClient.GetAsync(csvUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(CsvConstants.CsvDownloadFailedLog, csvUrl, response.StatusCode);
                return OperationResult<ContentManifest>.CreateFailure($"{CsvConstants.CsvDownloadFailedError}: {response.StatusCode}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            // Parse CSV streaming
            var files = new List<ManifestFile>();
            bool isFirstLine = true;
            long lineIndex = 0;

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync();
                lineIndex++;

                if (line == null)
                {
                    break;
                }

                if (isFirstLine)
                {
                    isFirstLine = false; // Skip header line
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // Split into up to 6 parts: relPath,size,md5,sha256,version,language
                var parts = line.Split(new[] { CsvConstants.CsvDelimiter }, CsvConstants.MaxCsvColumns, StringSplitOptions.None);
                if (parts.Length < CsvConstants.MinCsvColumns)
                {
                    _logger.LogWarning(CsvConstants.MalformedCsvLineWarning, lineIndex);
                    continue;
                }

                var relativePath = parts[CsvConstants.RelativePathColumnIndex].Trim();
                var sizeString = parts[CsvConstants.SizeColumnIndex].Trim();
                var md5 = parts[CsvConstants.Md5ColumnIndex].Trim();
                var sha256 = parts[CsvConstants.Sha256ColumnIndex].Trim();
                string? entryVersion = parts.Length >= CsvConstants.VersionColumnIndex + 1 ? parts[CsvConstants.VersionColumnIndex].Trim() : null;
                string? entryLanguage = parts.Length >= CsvConstants.LanguageColumnIndex + 1 ? parts[CsvConstants.LanguageColumnIndex].Trim() : null;

                if (string.IsNullOrEmpty(relativePath))
                {
                    _logger.LogWarning(CsvConstants.EmptyRelativePathWarning, lineIndex);
                    continue;
                }

                if (!long.TryParse(sizeString, out var size))
                {
                    _logger.LogWarning(CsvConstants.SizeParseFailedWarning, relativePath, lineIndex);
                    continue;
                }

                // Decide whether this entry belongs to the requested game
                if (targetGame != null)
                {
                    bool include = false;

                    // Prefer explicit version column if present
                    if (!string.IsNullOrWhiteSpace(entryVersion))
                    {
                        var versionLower = entryVersion.ToLowerInvariant();
                        if (targetGame == GameType.Generals && (versionLower.Contains("1.08") || versionLower.Contains("generals") || versionLower.Contains("generals1.08") || versionLower.Contains("generals_1.08")))
                        {
                            include = true;
                        }
                        else if (targetGame == GameType.ZeroHour &&
                                 (versionLower.Contains("1.04") || versionLower.Contains("zerohour") || versionLower.Contains("zero hour") || versionLower.Contains("zero-hour")))
                        {
                            include = true;
                        }
                    }
                    else
                    {
                        // Fallback: infer from relative path keywords
                        var pathLower = relativePath.ToLowerInvariant();
                        if (targetGame == GameType.Generals && (pathLower.Contains("generals") || pathLower.Contains("generals_") || pathLower.Contains("generals1.08")))
                        {
                            include = true;
                        }

                        if (targetGame == GameType.ZeroHour && (pathLower.Contains("zerohour") || pathLower.Contains("zero") || pathLower.Contains("zh_") || pathLower.Contains("zero_hour")))
                        {
                            include = true;
                        }
                    }

                    // If no rule matched, skip this row (we avoid mixing games).
                    if (!include)
                    {
                        continue;
                    }
                }

                // TargetGame is null or Unknown -> include all rows

                // Create ManifestFile (store sha256 as Hash by default)
                var manifestFile = new ManifestFile
                {
                    RelativePath = relativePath,
                    Size = size,
                    Hash = sha256 ?? string.Empty,
                    SourceType = ContentSourceType.Unknown,
                    IsRequired = true,
                };

                files.Add(manifestFile);
            }

            // Build ContentManifest
            var manifest = new ContentManifest
            {
                // Name/Version/Publisher etc can be set from discoveredItem if available
                Name = discoveredItem.Name ?? "CSV Generated Manifest",
                Version = discoveredItem.Version ?? string.Empty,
                ContentType = discoveredItem.ContentType != default ? discoveredItem.ContentType : default,
                TargetGame = discoveredItem.TargetGame != default ? discoveredItem.TargetGame : (targetGame ?? default),
                Files = files,
            };

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(CsvConstants.OperationCancelledLog);
            return OperationResult<ContentManifest>.CreateFailure(CsvConstants.OperationCancelledError);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, CsvConstants.CsvResolveFailedLog, discoveredItem?.Id);
            return OperationResult<ContentManifest>.CreateFailure($"{CsvConstants.CsvResolveFailedError}: {ex.Message}");
        }
    }
}
