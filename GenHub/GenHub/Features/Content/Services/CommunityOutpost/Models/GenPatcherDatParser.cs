using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost.Models;

/// <summary>
/// Parser for the GenPatcher dl.dat file format.
/// The format consists of:
/// - Line 1: Version header (e.g., "2.13                ;;")
/// - Content lines: [4-char-code] [9-digit-padded-size] [mirror-name] [url].
/// </summary>
public class GenPatcherDatParser(ILogger logger)
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

    /// <summary>
    /// Gets all download URLs for a content item, ordered by preference.
    /// </summary>
    /// <param name="item">The content item.</param>
    /// <returns>List of download URLs ordered by preference.</returns>
    public static List<string> GetOrderedDownloadUrls(GenPatcherContentItem item)
    {
        var urls = new List<string>();
        var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add legi.cc mirrors first
        foreach (var mirror in item.Mirrors.Where(m => m.Name.Contains("legi", StringComparison.OrdinalIgnoreCase)))
        {
            if (addedUrls.Add(mirror.Url))
            {
                urls.Add(mirror.Url);
            }
        }

        // Add gentool.net mirrors second
        foreach (var mirror in item.Mirrors.Where(m => m.Name.Contains("gentool", StringComparison.OrdinalIgnoreCase)))
        {
            if (addedUrls.Add(mirror.Url))
            {
                urls.Add(mirror.Url);
            }
        }

        // Add remaining mirrors
        foreach (var mirror in item.Mirrors)
        {
            if (addedUrls.Add(mirror.Url))
            {
                urls.Add(mirror.Url);
            }
        }

        return urls;
    }

    /// <summary>
    /// Gets the preferred download URL for a content item.
    /// Prefers legi.cc mirrors, then gentool.net, then others.
    /// </summary>
    /// <param name="item">The content item.</param>
    /// <returns>The preferred download URL, or null if no mirrors are available.</returns>
    public static string? GetPreferredDownloadUrl(GenPatcherContentItem item)
    {
        if (item.Mirrors.Count == 0)
        {
            return null;
        }

        // Priority order: legi.cc > gentool.net > others
        var legiMirror = item.Mirrors.FirstOrDefault(m =>
            m.Name.Contains("legi", StringComparison.OrdinalIgnoreCase));
        if (legiMirror != null)
        {
            return legiMirror.Url;
        }

        var gentoolMirror = item.Mirrors.FirstOrDefault(m =>
            m.Name.Contains("gentool", StringComparison.OrdinalIgnoreCase));
        if (gentoolMirror != null)
        {
            return gentoolMirror.Url;
        }

        // Return first available mirror (use FirstOrDefault for null safety)
        return item.Mirrors.FirstOrDefault()?.Url;
    }

    /// <summary>
    /// Parses the content of a dl.dat file.
    /// </summary>
    /// <param name="content">The raw content of the dl.dat file.</param>
    /// <returns>A result containing the parsed catalog version and content items.</returns>
    public GenPatcherCatalog Parse(string content)
    {
        var catalog = new GenPatcherCatalog();

        if (string.IsNullOrEmpty(content))
        {
            logger.LogWarning("dl.dat content is empty");
            return catalog;
        }

        // Split into lines (handle both \r\n and \n)
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        logger.LogDebug("Parsing dl.dat with {LineCount} lines", lines.Length);

        // Dictionary to group mirrors by content code
        var contentByCode = new Dictionary<string, GenPatcherContentItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                continue;
            }

            // Check for version header
            var versionMatch = VersionLinePattern.Match(trimmedLine);
            if (versionMatch.Success)
            {
                catalog.CatalogVersion = versionMatch.Groups[1].Value;
                logger.LogInformation("dl.dat catalog version: {Version}", catalog.CatalogVersion);
                continue;
            }

            // Try to parse as content line
            var contentMatch = ContentLinePattern.Match(trimmedLine);
            if (!contentMatch.Success)
            {
                logger.LogDebug("Skipping unrecognized line: {Line}", trimmedLine.Length > 50 ? trimmedLine[..50] + "..." : trimmedLine);
                continue;
            }

            var code = contentMatch.Groups[1].Value.ToLowerInvariant();
            var sizeStr = contentMatch.Groups[2].Value;
            var mirrorName = contentMatch.Groups[3].Value;
            var url = contentMatch.Groups[4].Value.Trim();

            if (!long.TryParse(sizeStr, out var fileSize))
            {
                logger.LogWarning("Failed to parse file size '{Size}' for content code {Code}", sizeStr, code);
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

        // Log unique mirror count to show distinct mirrors, not total occurrences
        var uniqueMirrors = catalog.Items
            .SelectMany(i => i.Mirrors.Select(m => m.Url))
            .Distinct()
            .Count();

        logger.LogInformation(
            "Parsed {ItemCount} content items with {TotalMirrors} total mirrors ({UniqueMirrors} unique) from dl.dat",
            catalog.Items.Count,
            catalog.Items.Sum(i => i.Mirrors.Count),
            uniqueMirrors);

        return catalog;
    }
}
