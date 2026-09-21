using GenHub.Core.Constants;
using GenHub.Core.Models.Manifest;
using System;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Groups GeneralsOnline manifests by release version so the game client, game data patch,
/// and QuickMatch mappack of one release collapse into a single library card with variants.
/// GeneralsOnline versions are date-based (MMDDYY_QFE#), so identical version strings —
/// QFE included — always identify the same release. This grouping applies exclusively to
/// GeneralsOnline; other publishers may reuse coincidental version numbers across
/// unrelated content.
/// </summary>
internal static class GeneralsOnlineVariantGrouping
{
    /// <summary>
    /// Builds the variant group id for a GeneralsOnline release version.
    /// </summary>
    /// <param name="version">The full release version, QFE suffix included.</param>
    /// <returns>The group id, or null when the version cannot identify a release.</returns>
    internal static string? BuildVariantGroupId(string? version)
    {
        var normalized = NormalizeVersion(version);
        return normalized == null ? null : $"{GeneralsOnlineConstants.PublisherType}-{normalized}";
    }

    /// <summary>
    /// Builds the display name for a GeneralsOnline release family.
    /// </summary>
    /// <param name="version">The full release version, QFE suffix included.</param>
    /// <returns>The family display name.</returns>
    internal static string BuildVariantFamilyName(string? version)
    {
        var normalized = NormalizeVersion(version);
        return normalized == null
            ? GeneralsOnlineConstants.ContentName
            : $"{GeneralsOnlineConstants.ContentName} {version?.Trim()}";
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
