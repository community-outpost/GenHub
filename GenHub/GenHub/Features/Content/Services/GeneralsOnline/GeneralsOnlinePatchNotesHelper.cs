using GenHub.Core.Constants;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results.Content;
using System;
using System.Linq;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Helper methods for identifying Generals Online content and determining whether patch notes are needed.
/// </summary>
public static class GeneralsOnlinePatchNotesHelper
{
    /// <summary>
    /// Determines whether the specified search result represents Generals Online content.
    /// </summary>
    /// <param name="searchResult">The content search result to inspect.</param>
    /// <returns><c>true</c> if the search result belongs to Generals Online; otherwise, <c>false</c>.</returns>
    public static bool IsGeneralsOnline(ContentSearchResult? searchResult)
    {
        if (searchResult == null)
        {
            return false;
        }

        return string.Equals(searchResult.ProviderName, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(searchResult.AuthorName, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(searchResult.ResolverId, GeneralsOnlineConstants.ResolverId, StringComparison.OrdinalIgnoreCase) ||
               searchResult.GetData<GeneralsOnlineRelease>() != null;
    }

    /// <summary>
    /// Determines whether the content description or changelog is a placeholder requiring on-demand patch notes retrieval.
    /// </summary>
    /// <param name="description">The existing description or changelog.</param>
    /// <param name="version">The release version, if known.</param>
    /// <returns><c>true</c> if real patch notes should be fetched; otherwise, <c>false</c>.</returns>
    public static bool NeedsPatchNotes(string? description, string? version)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return true;
        }

        var trimmed = description.Trim();

        if (IsGeneralsOnlineDomainOrUrl(trimmed))
        {
            return true;
        }

        if (string.Equals(trimmed, GeneralsOnlineConstants.PublisherDisplayName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, GeneralsOnlineConstants.PublisherName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            var rawVersion = version.Trim();
            var noPrefix = StripVersionPrefix(rawVersion);

            if (string.Equals(trimmed, $"{GeneralsOnlineConstants.PublisherDisplayName} {rawVersion}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"{GeneralsOnlineConstants.PublisherDisplayName} {noPrefix}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"{GeneralsOnlineConstants.PublisherName} {rawVersion}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"{GeneralsOnlineConstants.PublisherName} {noPrefix}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"{GeneralsOnlineConstants.PublisherName} {rawVersion} (portable)", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"{GeneralsOnlineConstants.PublisherName} {noPrefix} (portable)", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"{GeneralsOnlineConstants.PublisherDisplayName} {rawVersion} (portable)", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"{GeneralsOnlineConstants.PublisherDisplayName} {noPrefix} (portable)", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"{GeneralsOnlineConstants.PublisherName} {rawVersion}{GeneralsOnlineConstants.PortableReleaseSuffix}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"{GeneralsOnlineConstants.PublisherName} {noPrefix}{GeneralsOnlineConstants.PortableReleaseSuffix}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"{GeneralsOnlineConstants.PublisherDisplayName} {rawVersion}{GeneralsOnlineConstants.PortableReleaseSuffix}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"{GeneralsOnlineConstants.PublisherDisplayName} {noPrefix}{GeneralsOnlineConstants.PortableReleaseSuffix}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether two version strings match, ignoring single 'v' or 'V' prefix and case.
    /// </summary>
    /// <param name="versionA">The first version string.</param>
    /// <param name="versionB">The second version string.</param>
    /// <returns><c>true</c> if both versions match; otherwise, <c>false</c>.</returns>
    public static bool VersionsMatch(string? versionA, string? versionB)
    {
        if (string.IsNullOrWhiteSpace(versionA) || string.IsNullOrWhiteSpace(versionB))
        {
            return false;
        }

        var cleanA = StripVersionPrefix(versionA.Trim());
        var cleanB = StripVersionPrefix(versionB.Trim());

        if (string.IsNullOrEmpty(cleanA) || string.IsNullOrEmpty(cleanB))
        {
            return false;
        }

        return string.Equals(cleanA, cleanB, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a clone of the specified <see cref="GeneralsOnlineRelease"/> with an updated changelog description.
    /// </summary>
    /// <param name="release">The source release to clone.</param>
    /// <param name="changelog">The updated changelog text.</param>
    /// <returns>A new <see cref="GeneralsOnlineRelease"/> instance with the updated changelog.</returns>
    public static GeneralsOnlineRelease WithChangelog(this GeneralsOnlineRelease release, string changelog)
    {
        ArgumentNullException.ThrowIfNull(release);

        return new GeneralsOnlineRelease
        {
            Version = release.Version,
            VersionDate = release.VersionDate,
            ReleaseDate = release.ReleaseDate,
            PortableUrl = release.PortableUrl,
            PortableSize = release.PortableSize,
            Sha256 = release.Sha256,
            Changelog = changelog,
        };
    }

    private static bool IsGeneralsOnlineDomainOrUrl(string value)
    {
        if (GeneralsOnlineConstants.KnownDomains.Any(domain => string.Equals(value, domain, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var host = uri.Host;
            return GeneralsOnlineConstants.KnownDomains.Any(domain => host.Equals(domain, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static string StripVersionPrefix(string version)
    {
        if (version.StartsWith('v') || version.StartsWith('V'))
        {
            return version[1..];
        }

        return version;
    }
}
