using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper methods for working with content manifests.
/// </summary>
public static class ManifestHelper
{
    /// <summary>
    /// Determines if a manifest is from a CDN download (not local detection).
    /// Local detection manifests have version "Auto-Updated", Publisher.Name = "Retail Installation",
    /// or their ID starts with "1.0." (version 0).
    /// </summary>
    /// <param name="manifest">The manifest to check.</param>
    /// <returns>True if the manifest represents downloaded content.</returns>
    public static bool IsDownloadedManifest(ContentManifest manifest)
    {
        if (manifest == null) return false;

        // Local detection manifests have version "Auto-Updated"
        if (string.Equals(manifest.Version, GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Local detection manifests have Publisher.Name = "Retail Installation"
        if (string.Equals(manifest.Publisher?.Name, PublisherInfoConstants.Retail.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check if files indicate downloaded content (ContentAddressable source type with hashes)
        if (manifest.Files != null && manifest.Files.Count > 0)
        {
            return manifest.Files.Any(f =>
                f.SourceType == ContentSourceType.ContentAddressable &&
                !string.IsNullOrEmpty(f.Hash));
        }

        return false;
    }

    /// <summary>
    /// Standardizes error message formatting from a collection of error strings.
    /// </summary>
    /// <param name="errors">The collection of error messages.</param>
    /// <returns>A single formatted error string.</returns>
    public static string FormatErrors(IEnumerable<string>? errors) =>
        errors?.Any() == true ? string.Join(", ", errors) : "Unknown error";
}
