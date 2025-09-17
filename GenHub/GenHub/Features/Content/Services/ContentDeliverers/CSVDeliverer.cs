using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentDeliverers

{
    public class CSVResolver : IContentResolver
    {
        // Per requirement
        public string ResolverId => "CSVResolver";

        private readonly HttpClient _httpClient;
        private readonly ILogger<CSVResolver> _logger;

        public CSVResolver(HttpClient httpClient, ILogger<CSVResolver> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Resolve a discovered CSV item into a ContentManifest by downloading and parsing the CSV,
        /// filtering rows by game, and producing ManifestFile entries.
        /// </summary>
        public async Task<OperationResult<ContentManifest>> ResolveAsync(
            ContentSearchResult discoveredItem,
            CancellationToken cancellationToken = default)
        {
            if (discoveredItem == null)
                return OperationResult<ContentManifest>.CreateFailure("discoveredItem cannot be null");

            try
            {
                // 1) Obtain CSV URL from common locations
                string? csvUrl = null;

                // try ResolverMetadata (common pattern in other discoverers)
                try
                {
                    // Some ContentSearchResult implementations expose a dictionary property named ResolverMetadata
                    // We'll attempt to read it via dynamic/Reflection style if typed property isn't available.
                    var rm = discoveredItem.GetType().GetProperty("ResolverMetadata")?.GetValue(discoveredItem);
                    if (rm is IDictionary<string, string> dict && dict.TryGetValue("csvUrl", out var v1))
                        csvUrl = v1;
                }
                catch
                {
                    // ignore reflection failures; we'll try other locations below
                }

                // fallback to SourceUrl
                if (string.IsNullOrWhiteSpace(csvUrl) && !string.IsNullOrWhiteSpace(discoveredItem.SourceUrl))
                    csvUrl = discoveredItem.SourceUrl;

                // fallback to generic Metadata dictionary if present
                if (string.IsNullOrWhiteSpace(csvUrl))
                {
                    try
                    {
                        var metaProp = discoveredItem.GetType().GetProperty("Metadata");
                        if (metaProp != null)
                        {
                            var metaVal = metaProp.GetValue(discoveredItem);
                            if (metaVal is IDictionary<string, string> metaDict && metaDict.TryGetValue("csvUrl", out var v2))
                                csvUrl = v2;
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                if (string.IsNullOrWhiteSpace(csvUrl))
                {
                    _logger.LogError("CSVResolver: csvUrl not provided in discovered item (Id={ContentId})", discoveredItem?.Id);
                    return OperationResult<ContentManifest>.CreateFailure("CSV URL not provided by discoverer");
                }

                // 2) Determine requested target game (if the discoverer set it)
                var targetGame = discoveredItem.TargetGame != default ? discoveredItem.TargetGame : (GameType?)null;


                // Also allow resolver metadata 'game' fallback
                try
                {
                    var rm = discoveredItem.GetType().GetProperty("ResolverMetadata")?.GetValue(discoveredItem);
                    if ((targetGame == null) && rm is IDictionary<string, string> dict2 && dict2.TryGetValue("game", out var gameStr))
                    {
                        if (Enum.TryParse<GameType>(gameStr, true, out var parsedGame))
                            targetGame = parsedGame;
                    }
                }
                catch
{
                    // ignore
                }
                
                // 3) Download CSV as stream
                using var resp = await _httpClient.GetAsync(csvUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogError("CSVResolver: failed to download CSV from {Url} - status {Status}", csvUrl, resp.StatusCode);
                    return OperationResult<ContentManifest>.CreateFailure($"Failed to download CSV: {resp.StatusCode}");
                }

                using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);

                // 4) Parse CSV streaming
                var files = new List<ManifestFile>();
                bool firstLine = true;
                long lineIndex = 0;

                while (!reader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = await reader.ReadLineAsync();
                    lineIndex++;

                    if (line == null) break;
                    if (firstLine)
                    {
                        firstLine = false; // skip header line
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Split into up to 6 parts: relPath,size,md5,sha256,version,language
                    var parts = line.Split(new[] { ',' }, 6, StringSplitOptions.None);
                    if (parts.Length < 4)
                    {
                        _logger.LogWarning("CSVResolver: skipping malformed CSV line {LineIndex} (not enough columns)", lineIndex);
                        continue;
                    }

                    var relPath = parts[0].Trim();
                    var sizeStr = parts[1].Trim();
                    var md5 = parts[2].Trim();
                    var sha256 = parts[3].Trim();
                    string? entryVersion = parts.Length >= 5 ? parts[4].Trim() : null;
                    string? entryLanguage = parts.Length >= 6 ? parts[5].Trim() : null;

                    if (string.IsNullOrEmpty(relPath))
                    {
                        _logger.LogWarning("CSVResolver: skipping empty relPath at line {LineIndex}", lineIndex);
                        continue;
                    }

                    if (!long.TryParse(sizeStr, out var size))
                    {
                        _logger.LogWarning("CSVResolver: skipping {RelPath} because size parse failed at line {LineIndex}", relPath, lineIndex);
                        continue;
                    }

                    // 5) decide whether this entry belongs to the requested game
                    if (targetGame != null )
                    {
                        bool include = false;

                        // Prefer explicit version column if present
                        if (!string.IsNullOrWhiteSpace(entryVersion))
                        {
                            var vlow = entryVersion.ToLowerInvariant();
                            if (targetGame == GameType.Generals &&(vlow.Contains("1.08") ||  vlow.Contains("generals")||  vlow.Contains("generals1.08") || vlow.Contains("generals_1.08")))
                            {
                                include = true;
                            }
                            else if (targetGame == GameType.ZeroHour &&
                         (vlow.Contains("1.04") || vlow.Contains("zerohour")|| vlow.Contains("zero hour") || vlow.Contains("zero-hour")))
                            {
                                include = true;
                            }
                        }
                        else
                        {
                            // fallback: infer from relative path keywords
                            var pathLow = relPath.ToLowerInvariant();
                            if (targetGame == GameType.Generals && (pathLow.Contains("generals") ||  pathLow.Contains("generals_") || pathLow.Contains("generals1.08")))
                                include = true;
                            if (targetGame == GameType.ZeroHour && (pathLow.Contains("zerohour") || pathLow.Contains("zero") || pathLow.Contains("zh_") || pathLow.Contains("zero_hour")))
                                include = true;
                        }

                        // If no rule matched, skip this row (we avoid mixing games).
                        if (!include) continue;
                    }
                    // else: targetGame is null or Unknown -> include all rows

                    // 6) create ManifestFile (store sha256 as Hash by default)
                    var mf = new ManifestFile
                    {
                        RelativePath = relPath,
                        Size = size,
                        Hash = sha256 ?? string.Empty,
                        SourceType = ContentSourceType.Unknown,
                        IsRequired = true
                    };

                    files.Add(mf);
                }

                // 7) build ContentManifest
                var manifest = new ContentManifest
                {
                    // Name/Version/Publisher etc can be set from discoveredItem if available
                    Name = discoveredItem.Name ?? "CSV Generated Manifest",
                    Version = discoveredItem.Version ?? string.Empty,
                    ContentType = discoveredItem.ContentType != default ? discoveredItem.ContentType : default,
                    TargetGame = discoveredItem.TargetGame != default ? discoveredItem.TargetGame : (targetGame ?? default),
                    Files = files
                };

                return OperationResult<ContentManifest>.CreateSuccess(manifest);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("CSVResolver: operation was cancelled");
                return OperationResult<ContentManifest>.CreateFailure("Operation canceled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CSVResolver: failed to resolve CSV discovered item (Id={ContentId})", discoveredItem?.Id);
                return OperationResult<ContentManifest>.CreateFailure($"CSV resolve failed: {ex.Message}");
            }
        }
    }
}