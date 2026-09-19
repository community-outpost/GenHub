using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        if (manifest == null)
        {
            return false;
        }

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
        if (manifest.Files is { Count: > 0 })
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

    /// <summary>
    /// Gets every CAS hash a manifest links, across its flat file list and all artifact variants.
    /// The comparison is case-insensitive because hashes are hexadecimal and storage file names
    /// are normalized to lowercase while manifests may carry uppercase hex.
    /// </summary>
    /// <param name="manifest">The manifest to extract hashes from.</param>
    /// <returns>The linked content hashes.</returns>
    public static HashSet<string> GetContentAddressableHashes(ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectContentAddressableHashes(manifest.Files, hashes);

        if (manifest.Variants is { Count: > 0 })
        {
            foreach (var variant in manifest.Variants)
            {
                CollectContentAddressableHashes(variant?.Files, hashes);
            }
        }

        return hashes;
    }

    /// <summary>
    /// Determines whether the specified manifest file is an executable binary or script based on its file extension.
    /// </summary>
    /// <param name="file">The manifest file to check.</param>
    /// <returns><c>true</c> if the file has an executable extension; otherwise, <c>false</c>.</returns>
    public static bool IsExecutableFile(ManifestFile? file)
    {
        if (string.IsNullOrWhiteSpace(file?.RelativePath))
        {
            return false;
        }

        var ext = Path.GetExtension(file.RelativePath);
        return ProfileSharingConstants.ExecutableFileExtensions.Contains(ext);
    }

    /// <summary>
    /// Resolves the primary content manifest from a collection of candidate manifests based on
    /// the requested variant specified on the reference manifest (or its tags) and target game.
    /// </summary>
    /// <param name="manifests">The generated manifests.</param>
    /// <param name="referenceManifest">The package or reference manifest specifying variant/game metadata.</param>
    /// <returns>The best-matching content manifest, or <paramref name="referenceManifest"/> if <paramref name="manifests"/> is null or empty.</returns>
    public static ContentManifest? SelectPrimaryManifest(
        IReadOnlyList<ContentManifest>? manifests,
        ContentManifest? referenceManifest)
    {
        if (manifests == null || manifests.Count == 0)
        {
            return referenceManifest;
        }

        var requestedVariant = referenceManifest?.Metadata?.SelectedVariantId
            ?? ExtractTagValue(referenceManifest?.Metadata?.Tags, ManifestTagConstants.SelectedVariantPrefix)
            ?? ExtractTagValue(referenceManifest?.Metadata?.Tags, ManifestTagConstants.RequestedVariantPrefix)
            ?? ExtractTagValue(referenceManifest?.Metadata?.Tags, ManifestTagConstants.VariantPrefix);

        ContentManifest? primaryManifest = null;
        if (!string.IsNullOrEmpty(requestedVariant))
        {
            var variantTag = $"{ManifestTagConstants.VariantPrefix}{requestedVariant}";
            var selectedVariantTag = $"{ManifestTagConstants.SelectedVariantPrefix}{requestedVariant}";
            var variantSuffix = $"-{requestedVariant}";

            primaryManifest = manifests.FirstOrDefault(m =>
                string.Equals(m.Metadata?.SelectedVariantId, requestedVariant, StringComparison.OrdinalIgnoreCase))
              ?? manifests.FirstOrDefault(m =>
                m.Id.Value.EndsWith(variantSuffix, StringComparison.OrdinalIgnoreCase))
              ?? manifests.FirstOrDefault(m =>
                m.Metadata?.Tags?.Any(t => string.Equals(t, variantTag, StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(t, selectedVariantTag, StringComparison.OrdinalIgnoreCase)) == true);
        }

        if (primaryManifest == null && referenceManifest?.TargetGame != null)
        {
            primaryManifest = manifests.FirstOrDefault(m => m.TargetGame == referenceManifest.TargetGame);
        }

        return primaryManifest ?? manifests[0];
    }

    private static void CollectContentAddressableHashes(IEnumerable<ManifestFile>? files, HashSet<string> hashes)
    {
        if (files == null)
        {
            return;
        }

        foreach (var file in files.Where(file => file != null && !string.IsNullOrWhiteSpace(file.Hash)))
        {
            hashes.Add(file.Hash);
        }
    }

    /// <summary>
    /// Extracts the tag value following the specified prefix from a collection of tags.
    /// </summary>
    /// <param name="tags">The collection of metadata tags.</param>
    /// <param name="prefix">The prefix to search for.</param>
    /// <returns>The string after the prefix, or null if not found.</returns>
    private static string? ExtractTagValue(IEnumerable<string>? tags, string prefix)
    {
        var tag = tags?.FirstOrDefault(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return tag?[prefix.Length..];
    }
}
