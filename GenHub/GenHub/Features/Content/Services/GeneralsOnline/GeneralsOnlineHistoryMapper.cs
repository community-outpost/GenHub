using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Core.Services.Providers.VersionSchemes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Maps CRC catalog entries to Generals Online history releases for the downloads browser.
/// The CRC catalog records every known portable release, while the CDN manifest only
/// advertises the latest version. History cards reuse the standard Generals Online
/// pipeline (discoverer, resolver, provider, deliverer) so older executables install
/// through the same manifests, validation, and 404 retention handling as the latest.
/// </summary>
public static class GeneralsOnlineHistoryMapper
{
    private static readonly MmddyyQfeVersionScheme VersionScheme = new();

    /// <summary>
    /// Builds history search results from CRC catalog entries.
    /// </summary>
    /// <param name="entries">All CRC catalog entries, including non-Generals Online publishers.</param>
    /// <param name="excludeVersions">CDN current versions to exclude so none is listed twice.</param>
    /// <returns>History results ordered newest first. Never null.</returns>
    public static IReadOnlyList<ContentSearchResult> BuildHistoryResults(
        IReadOnlyList<CrcMappingEntry>? entries,
        IReadOnlyCollection<string>? excludeVersions = null)
    {
        if (entries == null || entries.Count == 0)
        {
            return [];
        }

        var selected = SelectPreferredEntries(entries, excludeVersions);
        return selected
            .Select(CreateHistoryResult)
            .OrderByDescending(result => result.LastUpdated ?? DateTime.MinValue)
            .ThenBy(result => result.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<CrcMappingEntry> SelectPreferredEntries(
        IReadOnlyList<CrcMappingEntry> entries,
        IReadOnlyCollection<string>? excludeVersions)
    {
        var exclusions = CollectExclusions(excludeVersions);
        var preferredByComponent = new Dictionary<int, CrcMappingEntry>();
        foreach (var entry in entries)
        {
            if (!IsHistoryCandidate(entry, exclusions.Versions))
            {
                continue;
            }

            var component = GameVersionHelper.GetGeneralsOnlineManifestIdComponent(entry.Version);
            if (component <= 0 || exclusions.Components.Contains(component) || !IsSupportedVersion(entry.Version) || HasUnencodableQfe(entry.Version))
            {
                continue;
            }

            if (!preferredByComponent.TryGetValue(component, out var existing))
            {
                preferredByComponent[component] = entry;
                continue;
            }

            if (IsEacVersion(existing.Version) && !IsEacVersion(entry.Version))
            {
                preferredByComponent[component] = entry;
            }
        }

        return [.. preferredByComponent.Values];
    }

    private static (HashSet<string> Versions, HashSet<int> Components) CollectExclusions(
        IReadOnlyCollection<string>? excludeVersions)
    {
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = new HashSet<int>();
        if (excludeVersions == null)
        {
            return (versions, components);
        }

        foreach (var excludeVersion in excludeVersions)
        {
            if (string.IsNullOrWhiteSpace(excludeVersion))
            {
                continue;
            }

            versions.Add(excludeVersion.Trim());
            var component = GameVersionHelper.GetGeneralsOnlineManifestIdComponent(excludeVersion);
            if (component > 0)
            {
                components.Add(component);
            }
        }

        return (versions, components);
    }

    private static bool IsHistoryCandidate(CrcMappingEntry? entry, HashSet<string> excludedVersions)
    {
        if (entry == null)
        {
            return false;
        }

        if (!string.Equals(entry.Publisher, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(entry.Version) || string.IsNullOrWhiteSpace(entry.CdnUrl))
        {
            return false;
        }

        if (!entry.CdnUrl.Trim().EndsWith(GeneralsOnlineConstants.PortableExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return excludedVersions.Count == 0 || !excludedVersions.Contains(entry.Version.Trim());
    }

    private static bool IsSupportedVersion(string version)
    {
        return VersionScheme.TryParse(version, out _);
    }

    private static bool HasUnencodableQfe(string version)
    {
        if (VersionScheme.TryParse(version, out var parsed) && parsed.Components.Count > 3)
        {
            return parsed.Components[3] > 9;
        }

        return false;
    }

    private static bool IsEacVersion(string version)
    {
        return version.Contains("_EAC", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? ResolveReleaseDate(CrcMappingEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.BuildDate) &&
            DateTime.TryParse(entry.BuildDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var buildDate))
        {
            return DateTime.SpecifyKind(buildDate, DateTimeKind.Utc);
        }

        if (VersionScheme.TryParse(entry.Version, out var parsed) && parsed.Components.Count >= 3)
        {
            return new DateTime((int)parsed.Components[0], (int)parsed.Components[1], (int)parsed.Components[2], 0, 0, 0, DateTimeKind.Utc);
        }

        return null;
    }

    private static ContentSearchResult CreateHistoryResult(CrcMappingEntry entry)
    {
        var version = entry.Version.Trim();
        var cdnUrl = entry.CdnUrl!.Trim();
        var releaseDate = ResolveReleaseDate(entry);

        var release = new GeneralsOnlineRelease
        {
            Version = version,
            VersionDate = releaseDate ?? DateTime.UnixEpoch,
            ReleaseDate = releaseDate ?? DateTime.UnixEpoch,
            PortableUrl = cdnUrl,
            PortableSize = null,
            Sha256 = null,
            Changelog = $"Generals Online {version}",
        };

        var searchResult = new ContentSearchResult
        {
            Id = $"{GeneralsOnlineConstants.ContentIdPrefix}{version}",
            Name = GeneralsOnlineConstants.ContentName,
            Description = release.Changelog,
            Version = version,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            ProviderName = GeneralsOnlineConstants.PublisherType,
            AuthorName = GeneralsOnlineConstants.PublisherName,
            IconUrl = GeneralsOnlineConstants.LogoSource,
            LastUpdated = releaseDate,
            DownloadSize = 0,
            RequiresResolution = true,
            ResolverId = GeneralsOnlineConstants.ResolverId,
            SourceUrl = GeneralsOnlineConstants.DownloadPageUrl,
            SelectedDownloadUrl = cdnUrl,
        };

        foreach (var tag in GeneralsOnlineConstants.Tags.Where(tag => !searchResult.Tags.Contains(tag)))
        {
            searchResult.Tags.Add(tag);
        }

        searchResult.SetData(release);
        return searchResult;
    }
}
