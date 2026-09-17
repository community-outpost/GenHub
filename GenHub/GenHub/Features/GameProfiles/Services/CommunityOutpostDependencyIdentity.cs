using GenHub.Core.Constants;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using System;
using System.Linq;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Identifies semantic Community Outpost dependencies without weakening exact-ID semantics for
/// other publishers.
/// </summary>
public static class CommunityOutpostDependencyIdentity
{
    /// <summary>
    /// Extracts Community Outpost's stable catalog content code from a concrete manifest ID.
    /// </summary>
    /// <param name="manifestId">The raw manifest identifier string.</param>
    /// <param name="contentType">When successful, the extracted content type segment.</param>
    /// <param name="contentCode">When successful, the resolved Community Outpost content code.</param>
    /// <returns>True if a content code could be determined; otherwise, false.</returns>
    public static bool TryGetCommunityOutpostContentCode(
        string manifestId,
        out string contentType,
        out string contentCode)
    {
        contentType = string.Empty;
        contentCode = string.Empty;
        var idParts = manifestId.Split('.');
        if (idParts.Length != 5 ||
            !idParts[2].Equals(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        contentType = idParts[3];
        var nameSegment = idParts[4];
        var metadata = GenPatcherContentRegistry.GetMetadata(nameSegment);
        if (metadata.ContentType != ContentType.UnknownContentType)
        {
            contentCode = metadata.ContentCode;
            return true;
        }

        var dashIndex = nameSegment.IndexOf('-');
        var codePrefix = dashIndex > 0 ? nameSegment[..dashIndex] : nameSegment;
        var prefixMetadata = GenPatcherContentRegistry.GetMetadata(codePrefix);
        if (prefixMetadata.ContentType != ContentType.UnknownContentType)
        {
            contentCode = prefixMetadata.ContentCode;
            return true;
        }

        // Fail-closed: do not guess unknown 4-character prefixes
        return false;
    }

    /// <summary>
    /// Gets the authoritative Community Outpost code recorded in a manifest, falling back to
    /// its canonical identifier for older manifests that predate the metadata tag.
    /// </summary>
    /// <param name="manifest">The content manifest to evaluate.</param>
    /// <returns>The resolved Community Outpost content code, or an empty string if none matched.</returns>
    public static string GetCommunityOutpostContentCode(ContentManifest manifest)
    {
        var contentCodeTag = manifest.Metadata?.Tags?
            .FirstOrDefault(tag => tag.StartsWith(ManifestTagConstants.ContentCodePrefix, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(contentCodeTag))
        {
            var tagValue = contentCodeTag[ManifestTagConstants.ContentCodePrefix.Length..];
            var directMeta = GenPatcherContentRegistry.GetMetadata(tagValue);
            if (directMeta.ContentType != ContentType.UnknownContentType)
            {
                return directMeta.ContentCode;
            }

            var dashIdx = tagValue.IndexOf('-');
            if (dashIdx > 0)
            {
                var prefix = tagValue[..dashIdx];
                var prefixMeta = GenPatcherContentRegistry.GetMetadata(prefix);
                if (prefixMeta.ContentType != ContentType.UnknownContentType)
                {
                    return prefixMeta.ContentCode;
                }
            }

            return tagValue;
        }

        return TryGetCommunityOutpostContentCode(manifest.Id.Value, out _, out var contentCode)
            ? contentCode
            : string.Empty;
    }
}
