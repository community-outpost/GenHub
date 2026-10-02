using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using System;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Groups GeneralsOnline manifests by release version and content type so variants of the same
/// content type (e.g., 60Hz and 30Hz game client) collapse into a single library card, while
/// distinct content types (GameClient, MapPack, Patch) remain separate.
/// GeneralsOnline versions are date-based (MMDDYY_QFE#), so identical version strings —
/// QFE included — always identify the same release. This grouping applies exclusively to
/// GeneralsOnline; other publishers may reuse coincidental version numbers across
/// unrelated content.
/// </summary>
internal static class GeneralsOnlineVariantGrouping
{
    /// <summary>
    /// Builds the variant group id for a GeneralsOnline release version and content type.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <param name="version">The full release version, QFE suffix included.</param>
    /// <returns>The group id, or null when the version cannot identify a release.</returns>
    internal static string? BuildVariantGroupId(ContentType contentType, string? version)
    {
        var normalized = NormalizeVersion(version);
        return normalized == null
            ? null
            : $"{GeneralsOnlineConstants.PublisherType}-{contentType.ToString().ToLowerInvariant()}-{normalized}";
    }

    /// <summary>
    /// Builds the display name for a GeneralsOnline release family and content type.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <param name="version">The full release version, QFE suffix included.</param>
    /// <param name="localizationService">Optional localization service.</param>
    /// <returns>The family display name.</returns>
    internal static string BuildVariantFamilyName(ContentType contentType, string? version, ILocalizationService? localizationService = null)
    {
        string typeDisplay = localizationService is not null &&
            localizationService.TryGetString($"ContentType.{contentType}", out var localized) &&
            !string.IsNullOrWhiteSpace(localized)
            ? localized
            : contentType switch
            {
                ContentType.GameClient => "Game Client",
                ContentType.MapPack => "Map Pack",
                ContentType.Patch => "Patch",
                _ => contentType.GetDisplayName(),
            };

        var normalized = NormalizeVersion(version);
        return normalized == null
            ? $"{GeneralsOnlineConstants.ContentName} {typeDisplay}"
            : $"{GeneralsOnlineConstants.ContentName} {typeDisplay} {version?.Trim()}";
    }

    /// <summary>
    /// Determines whether a stored manifest is GeneralsOnline content eligible for
    /// version grouping.
    /// </summary>
    /// <param name="manifest">The manifest to inspect.</param>
    /// <returns>True for GeneralsOnline manifests; otherwise false.</returns>
    internal static bool IsGeneralsOnlineManifest(ContentManifest? manifest)
    {
        if (manifest == null)
        {
            return false;
        }

        return string.Equals(manifest.OriginalProviderName, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(manifest.Publisher?.PublisherType, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var trimmed = version.Trim();
        if (string.Equals(trimmed, GeneralsOnlineConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed.ToLowerInvariant();
    }
}
