using GenHub.Core.Constants;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using System;
using System.Linq;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Groups Community Outpost variant manifests by content code and release version so the
/// resolution/language variants of one release collapse into a single library card with a
/// variant picker, mirroring GeneralsOnline release grouping. Version scoping keeps
/// distinct releases on separate cards so the update flow keeps working, and grouping
/// applies exclusively to Community Outpost content: coincidental version equality must
/// never merge other publishers.
/// </summary>
internal static class CommunityOutpostVariantGrouping
{
    /// <summary>
    /// Builds the variant group id for a Community Outpost content release.
    /// </summary>
    /// <param name="contentType">The content type carried by the manifests.</param>
    /// <param name="contentCode">The GenPatcher content code (for example cbpr).</param>
    /// <param name="version">The release version. May be null for legacy manifests.</param>
    /// <returns>The group id, or null when the content code or version is missing. A missing version fails closed: distinct releases must never share a group id.</returns>
    internal static string? BuildVariantGroupId(ContentType contentType, string? contentCode, string? version)
    {
        if (string.IsNullOrWhiteSpace(contentCode))
        {
            return null;
        }

        var normalizedVersion = NormalizeVersion(version);
        if (normalizedVersion == null)
        {
            return null;
        }

        var normalizedCode = contentCode.Trim().ToLowerInvariant();
        var baseId = $"{CommunityOutpostConstants.PublisherType}.{contentType.ToString().ToLowerInvariant()}.{normalizedCode}";
        return $"{baseId}.{normalizedVersion}";
    }

    /// <summary>
    /// Builds the display name for a Community Outpost content family.
    /// </summary>
    /// <param name="contentCode">The GenPatcher content code (for example cbpr).</param>
    /// <returns>The registry display name, or null for unknown codes.</returns>
    internal static string? BuildVariantFamilyName(string? contentCode)
    {
        if (string.IsNullOrWhiteSpace(contentCode))
        {
            return null;
        }

        var normalized = GenPatcherContentRegistry.NormalizeContentCode(contentCode);
        if (string.IsNullOrEmpty(normalized) || !GenPatcherContentRegistry.IsKnownCode(normalized))
        {
            return null;
        }

        var displayName = GenPatcherContentRegistry.GetMetadata(normalized).DisplayName;
        return string.IsNullOrWhiteSpace(displayName) ? null : displayName;
    }

    /// <summary>
    /// Determines whether a stored manifest is Community Outpost content eligible for
    /// variant grouping.
    /// </summary>
    /// <param name="manifest">The manifest to inspect.</param>
    /// <returns>True for Community Outpost manifests; otherwise false.</returns>
    internal static bool IsCommunityOutpostManifest(ContentManifest? manifest)
    {
        if (manifest == null)
        {
            return false;
        }

        return string.Equals(manifest.OriginalProviderName, CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(manifest.Publisher?.PublisherType, CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether a stored manifest is one variant of a multi-variant release.
    /// Singles are excluded so they keep rendering as plain cards without a picker.
    /// </summary>
    /// <param name="manifest">The manifest to inspect.</param>
    /// <param name="contentCode">The resolved content code for the manifest.</param>
    /// <returns>True for variant manifests; otherwise false.</returns>
    internal static bool IsVariantManifest(ContentManifest? manifest, string? contentCode)
    {
        if (manifest == null || string.IsNullOrWhiteSpace(contentCode))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Metadata?.SelectedVariantId))
        {
            return true;
        }

        if (manifest.Metadata?.Tags?.Any(tag =>
            tag.StartsWith(ManifestTagConstants.VariantPrefix, StringComparison.OrdinalIgnoreCase) ||
            tag.StartsWith(ManifestTagConstants.SelectedVariantPrefix, StringComparison.OrdinalIgnoreCase) ||
            tag.StartsWith(ManifestTagConstants.RequestedVariantPrefix, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return true;
        }

        // Legacy manifests predate variant tagging; fall back to the manifest id suffix.
        var nameSegment = manifest.Id.Value.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return nameSegment?.StartsWith($"{contentCode.Trim()}-", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Resolves the variant content code for a Community Outpost variant manifest.
    /// </summary>
    /// <param name="manifest">The manifest to inspect.</param>
    /// <param name="contentCode">The resolved content code when the manifest is a variant.</param>
    /// <returns>True for Community Outpost variant manifests; otherwise false.</returns>
    internal static bool TryGetVariantContentCode(ContentManifest? manifest, out string? contentCode)
    {
        contentCode = null;
        if (!IsCommunityOutpostManifest(manifest))
        {
            return false;
        }

        contentCode = GetContentCode(manifest);
        return IsVariantManifest(manifest, contentCode);
    }

    /// <summary>
    /// Resolves the GenPatcher content code for a stored manifest.
    /// </summary>
    /// <param name="manifest">The manifest to inspect.</param>
    /// <returns>The lowercase content code, or null when it cannot be determined.</returns>
    internal static string? GetContentCode(ContentManifest? manifest)
    {
        if (manifest == null)
        {
            return null;
        }

        var tagged = manifest.Metadata?.Tags
            ?.FirstOrDefault(tag => tag.StartsWith(ManifestTagConstants.ContentCodePrefix, StringComparison.OrdinalIgnoreCase));
        if (tagged != null)
        {
            var code = tagged[ManifestTagConstants.ContentCodePrefix.Length..].Trim();
            if (!string.IsNullOrEmpty(code))
            {
                var normalizedTag = GenPatcherContentRegistry.NormalizeContentCode(code);
                if (!string.IsNullOrEmpty(normalizedTag) && GenPatcherContentRegistry.IsKnownCode(normalizedTag))
                {
                    return normalizedTag.ToLowerInvariant();
                }
            }
        }

        var nameSegment = manifest.Id.Value.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(nameSegment))
        {
            return null;
        }

        var normalized = GenPatcherContentRegistry.NormalizeContentCode(nameSegment);
        return string.IsNullOrEmpty(normalized) || !GenPatcherContentRegistry.IsKnownCode(normalized)
            ? null
            : normalized.ToLowerInvariant();
    }

    private static string? NormalizeVersion(string? version)
    {
        return string.IsNullOrWhiteSpace(version) ? null : version.Trim().ToLowerInvariant();
    }
}
