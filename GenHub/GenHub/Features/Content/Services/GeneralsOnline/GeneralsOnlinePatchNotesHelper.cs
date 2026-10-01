using GenHub.Core.Constants;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results.Content;
using System;

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

        if (string.Equals(trimmed, "www.playgenerals.online", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            var rawVersion = version.Trim();
            var noPrefix = rawVersion.TrimStart('v', 'V');

            if (string.Equals(trimmed, $"Generals Online {rawVersion}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"Generals Online {noPrefix}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"GeneralsOnline {rawVersion}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"GeneralsOnline {noPrefix}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"GeneralsOnline {rawVersion} portable release", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, $"GeneralsOnline {noPrefix} portable release", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Generic single-line placeholder without changelog bullets or details
        if (trimmed.StartsWith("Generals Online", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Contains('\n') &&
            trimmed.Length < 60)
        {
            return true;
        }

        return false;
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
}
