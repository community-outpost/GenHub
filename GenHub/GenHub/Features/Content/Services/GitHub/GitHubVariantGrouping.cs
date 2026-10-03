using GenHub.Core.Constants;
using GenHub.Core.Models.Manifest;
using System;

namespace GenHub.Features.Content.Services.GitHub;

/// <summary>
/// Groups GitHub manifests by release so the per-game downloads of one multi-variant
/// release (such as the Generals and Zero Hour clients of a SuperHackers weekly)
/// collapse into a single library card with variants. Only manifests whose original
/// content id carries a trailing game-type suffix are grouped; single-asset releases
/// keep a null group id and render as plain cards.
/// </summary>
internal static class GitHubVariantGrouping
{
    private const char IdSeparator = '.';

    private const int MinVariantIdSegments = 5;

    /// <summary>
    /// Tries to build the variant group id identifying the release a stored GitHub
    /// manifest was downloaded from.
    /// </summary>
    /// <param name="manifest">The manifest to inspect.</param>
    /// <param name="groupId">The shared release group id when the manifest is a recognized multi-variant download.</param>
    /// <returns>True when a group id was derived; otherwise false.</returns>
    internal static bool TryGetVariantGroupId(ContentManifest? manifest, out string? groupId)
    {
        groupId = null;

        var segments = SplitOriginalId(manifest?.OriginalContentId);
        if (segments == null || !IsGameTypeSuffix(segments[^1]))
        {
            return false;
        }

        groupId = string.Join(IdSeparator, segments, 0, segments.Length - 1);
        return true;
    }

    /// <summary>
    /// Builds the display name for a GitHub release family.
    /// </summary>
    /// <param name="manifest">The manifest to inspect.</param>
    /// <returns>The release version shared by the grouped variants, or null when it is missing.</returns>
    internal static string? BuildVariantFamilyName(ContentManifest? manifest)
    {
        return string.IsNullOrWhiteSpace(manifest?.Version) ? null : manifest.Version.Trim();
    }

    private static string[]? SplitOriginalId(string? originalContentId)
    {
        if (string.IsNullOrWhiteSpace(originalContentId))
        {
            return null;
        }

        var segments = originalContentId.Trim().Split(IdSeparator);
        if (segments.Length < MinVariantIdSegments ||
            !string.Equals(segments[0], PublisherTypeConstants.GitHub, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return segments;
    }

    private static bool IsGameTypeSuffix(string? segment)
    {
        return string.Equals(segment, SuperHackersConstants.GeneralsSuffix, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, SuperHackersConstants.ZeroHourSuffix, StringComparison.OrdinalIgnoreCase);
    }
}
