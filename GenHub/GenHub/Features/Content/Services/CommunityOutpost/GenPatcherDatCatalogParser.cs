using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.CommunityOutpost.Models;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Parses the GenPatcher dl.dat catalog format into content search results.
/// The format consists of:
/// - Line 1: Version header (e.g., "2.13                ;;")
/// - Content lines: [4-char-code] [9-digit-padded-size] [mirror-name] [url].
/// Uses <see cref="GenPatcherContentRegistry"/> for metadata resolution.
/// </summary>
public class GenPatcherDatCatalogParser : ICatalogParser
{
    /// <summary>
    /// Regex pattern to match content lines.
    /// Groups: 1=code, 2=size, 3=mirror, 4=url.
    /// </summary>
    private static readonly Regex ContentLinePattern = new(
        @"^(\w{4})\s+(\d+)\s+(\S+)\s+(.+)$",
        RegexOptions.Compiled);

    /// <summary>
    /// Regex pattern to match the version header line.
    /// </summary>
    private static readonly Regex VersionLinePattern = new(
        @"^([\d\.]+)\s+;;$",
        RegexOptions.Compiled);

    private readonly ILogger<GenPatcherDatCatalogParser> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenPatcherDatCatalogParser"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public GenPatcherDatCatalogParser(ILogger<GenPatcherDatCatalogParser> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string CatalogFormat => "genpatcher-dat";

    /// <inheritdoc/>
    public Task<OperationResult<IEnumerable<ContentSearchResult>>> ParseAsync(
        string catalogContent,
        ProviderDefinition provider,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var results = new List<ContentSearchResult>();

            if (string.IsNullOrEmpty(catalogContent))
            {
                _logger.LogWarning("Catalog content is empty");
                return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results));
            }

            // Parse the dl.dat content
            var catalog = ParseDatContent(catalogContent);

            if (catalog.Items.Count == 0)
            {
                _logger.LogWarning("No items found in catalog");
                return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results));
            }

            _logger.LogInformation(
                "Parsed {ItemCount} items from GenPatcher catalog (version {Version})",
                catalog.Items.Count,
                catalog.CatalogVersion);

            // Convert items to ContentSearchResult using GenPatcherContentRegistry
            foreach (var item in catalog.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var searchResult = ConvertToContentSearchResult(item, catalog.CatalogVersion, provider);
                if (searchResult != null)
                {
                    results.Add(searchResult);
                }
            }

            _logger.LogInformation("Converted {Count} catalog items to search results", results.Count);
            return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse GenPatcher catalog");
            return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure($"Failed to parse catalog: {ex.Message}"));
        }
    }

    /// <summary>
    /// Makes a URL absolute if it's relative.
    /// </summary>
    /// <param name="url">The URL to check.</param>
    /// <param name="baseUrl">The base URL to prepend if the URL is relative.</param>
    /// <returns>An absolute URL.</returns>
    private static string MakeUrlAbsolute(string url, string baseUrl)
    {
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        var trimmedBase = baseUrl.TrimEnd('/');
        return $"{trimmedBase}/{url.TrimStart('/')}";
    }

    /// <summary>
    /// Parses the raw dl.dat content into a catalog structure.
    /// </summary>
    private ParsedCatalog ParseDatContent(string content)
    {
        var catalog = new ParsedCatalog();
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var contentByCode = new Dictionary<string, GenPatcherContentItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                continue;
            }

            // Check for version header
            var versionMatch = VersionLinePattern.Match(trimmedLine);
            if (versionMatch.Success)
            {
                catalog.CatalogVersion = versionMatch.Groups[1].Value;
                _logger.LogDebug("dl.dat catalog version: {Version}", catalog.CatalogVersion);
                continue;
            }

            // Try to parse as content line
            var contentMatch = ContentLinePattern.Match(trimmedLine);
            if (!contentMatch.Success)
            {
                _logger.LogDebug("Skipping unrecognized line: {Line}", trimmedLine.Length > 50 ? trimmedLine[..50] + "..." : trimmedLine);
                continue;
            }

            var code = contentMatch.Groups[1].Value.ToLowerInvariant();
            var sizeStr = contentMatch.Groups[2].Value;
            var mirrorName = contentMatch.Groups[3].Value;
            var url = contentMatch.Groups[4].Value.Trim();

            if (!long.TryParse(sizeStr, out var fileSize))
            {
                _logger.LogWarning("Failed to parse file size '{Size}' for content code {Code}", sizeStr, code);
                continue;
            }

            // Get or create content item
            if (!contentByCode.TryGetValue(code, out var contentItem))
            {
                contentItem = new GenPatcherContentItem
                {
                    ContentCode = code,
                    FileSize = fileSize,
                };
                contentByCode[code] = contentItem;
            }

            // Add mirror
            contentItem.Mirrors.Add(new GenPatcherMirror
            {
                Name = mirrorName,
                Url = url,
            });
        }

        catalog.Items = contentByCode.Values.ToList();

        _logger.LogDebug(
            "Parsed {ItemCount} content items with {TotalMirrors} total mirrors",
            catalog.Items.Count,
            catalog.Items.Sum(i => i.Mirrors.Count));

        return catalog;
    }

    /// <summary>
    /// Converts a parsed content item to a ContentSearchResult using GenPatcherContentRegistry.
    /// </summary>
    private ContentSearchResult? ConvertToContentSearchResult(
        GenPatcherContentItem item,
        string catalogVersion,
        ProviderDefinition provider)
    {
        try
        {
            // Get metadata from GenPatcherContentRegistry
            var metadata = GenPatcherContentRegistry.GetMetadata(item.ContentCode);

            // Skip unknown content
            if (metadata.ContentType == ContentType.UnknownContentType)
            {
                _logger.LogDebug("No metadata found for content code {Code}, skipping", item.ContentCode);
                return null;
            }

            // Skip official patches (104*, 108*) - language-specific patches that clutter the UI
            if (metadata.Category == GenPatcherContentCategory.OfficialPatch)
            {
                _logger.LogDebug("Skipping official patch {Code} - not shown in UI", item.ContentCode);
                return null;
            }

            // Get download URL with mirror preference from provider
            var preferredUrl = GetPreferredDownloadUrl(item, provider);
            if (string.IsNullOrEmpty(preferredUrl))
            {
                _logger.LogWarning("No download URLs available for content code {Code}", item.ContentCode);
                return null;
            }

            // Make URL absolute using provider's patchPageUrl
            var baseUrl = provider.Endpoints.GetEndpoint("patchPageUrl") ?? "https://legi.cc/patch";
            preferredUrl = MakeUrlAbsolute(preferredUrl, baseUrl);

            var result = new ContentSearchResult
            {
                Id = $"{provider.ProviderId}.{item.ContentCode}",
                Name = metadata.DisplayName,
                Description = metadata.Description ?? string.Empty,
                Version = metadata.Version ?? "1.0",
                ContentType = metadata.ContentType,
                TargetGame = metadata.TargetGame,
                ProviderName = provider.PublisherType,
                AuthorName = provider.DisplayName,
                SourceUrl = preferredUrl,
                DownloadSize = item.FileSize,
                RequiresResolution = true,
                ResolverId = provider.ProviderId,
                LastUpdated = DateTime.Now,
            };

            // Add default tags from provider
            foreach (var tag in provider.DefaultTags)
            {
                if (!result.Tags.Contains(tag))
                {
                    result.Tags.Add(tag);
                }
            }

            // Add category as a tag
            result.Tags.Add(metadata.Category.ToString().ToLowerInvariant());

            // Add language tag if applicable
            if (!string.IsNullOrEmpty(metadata.LanguageCode))
            {
                result.Tags.Add(metadata.LanguageCode);
            }

            // Store metadata for resolver
            result.ResolverMetadata["contentCode"] = item.ContentCode;
            result.ResolverMetadata["catalogVersion"] = catalogVersion;
            result.ResolverMetadata["fileSize"] = item.FileSize.ToString();
            result.ResolverMetadata["category"] = metadata.Category.ToString();
            result.ResolverMetadata["installTarget"] = metadata.InstallTarget.ToString();

            // Store all mirror URLs as JSON for fallback support
            var absoluteUrls = item.Mirrors
                .Select(m => MakeUrlAbsolute(m.Url, baseUrl))
                .ToList();
            result.ResolverMetadata["mirrorUrls"] = JsonSerializer.Serialize(absoluteUrls);
            result.ResolverMetadata["mirrors"] = string.Join(", ", item.Mirrors.Select(m => m.Name));

            _logger.LogDebug(
                "Created ContentSearchResult for {Code}: {Name} ({ContentType}, {Game})",
                item.ContentCode,
                metadata.DisplayName,
                metadata.ContentType,
                metadata.TargetGame);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert content item {Code} to search result", item.ContentCode);
            return null;
        }
    }

    /// <summary>
    /// Gets the preferred download URL based on provider's mirror preference.
    /// </summary>
    /// <param name="item">The content item with available mirrors.</param>
    /// <param name="provider">The provider definition with mirror preferences.</param>
    /// <returns>The preferred download URL, or null if no mirrors available.</returns>
    private string? GetPreferredDownloadUrl(GenPatcherContentItem item, ProviderDefinition provider)
    {
        if (item.Mirrors.Count == 0)
        {
            return null;
        }

        // If provider has mirror preference, use that order
        if (provider.MirrorPreference.Count > 0)
        {
            foreach (var preferredMirror in provider.MirrorPreference)
            {
                var mirror = item.Mirrors.FirstOrDefault(m =>
                    m.Name.Contains(preferredMirror, StringComparison.OrdinalIgnoreCase));

                if (mirror != null)
                {
                    return mirror.Url;
                }
            }
        }

        // Also check provider endpoint mirrors for priority
        if (provider.Endpoints.Mirrors.Count > 0)
        {
            var orderedMirrors = provider.Endpoints.Mirrors.OrderBy(m => m.Priority).ToList();
            foreach (var mirrorEndpoint in orderedMirrors)
            {
                var mirror = item.Mirrors.FirstOrDefault(m =>
                    m.Name.Contains(mirrorEndpoint.Name, StringComparison.OrdinalIgnoreCase));

                if (mirror != null)
                {
                    return mirror.Url;
                }
            }
        }

        // Fall back to first available mirror
        return item.Mirrors.First().Url;
    }

    /// <summary>
    /// Represents a parsed catalog.
    /// </summary>
    private class ParsedCatalog
    {
        public string CatalogVersion { get; set; } = "unknown";

        public List<GenPatcherContentItem> Items { get; set; } = new();
    }
}
