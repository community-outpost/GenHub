using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.IO;

namespace GenHub.Core.Utilities;

/// <summary>
/// Single policy for which remote content formats may become manifests. Serves both the
/// discovery filter (what is listed as content) and the resolver guard (what is resolved
/// into a manifest), so a format rejected in one place cannot slip through the other.
/// <para>
/// Understood inputs are archive containers (extracted before detection) plus bare
/// single-file assets (handled as-is). Guided-rejection formats fail once with actionable
/// guidance and never produce manifests.
/// </para>
/// </summary>
public static class ContentFormatPolicy
{
    /// <summary>
    /// Determines whether a remote asset may become manifest content.
    /// </summary>
    /// <param name="fileName">The asset file name.</param>
    /// <returns>
    /// <c>true</c> for archive containers and bare single-file assets; <c>false</c> for
    /// guided-rejection formats.
    /// </returns>
    public static bool IsUnderstoodAsset(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        return !IsGuidedRejection(fileName);
    }

    /// <summary>
    /// Determines whether an asset is an archive container the pipeline extracts.
    /// </summary>
    /// <param name="fileName">The asset file name.</param>
    /// <returns><c>true</c> when the extension is a known archive container.</returns>
    public static bool IsArchiveContainer(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var lowerName = fileName.ToLowerInvariant();
        foreach (var extension in ContentFormatConstants.UnderstoodArchiveExtensions)
        {
            if (lowerName.EndsWith(extension, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether an asset needs external tooling and must be rejected with guidance.
    /// </summary>
    /// <param name="fileName">The asset file name.</param>
    /// <returns><c>true</c> for disk images, installers, and container packages.</returns>
    public static bool IsGuidedRejection(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        foreach (var rejected in ContentFormatConstants.GuidedRejectionExtensions)
        {
            if (extension.Equals(rejected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the guided-rejection failure message for an asset, or <c>null</c> when the
    /// asset is understood.
    /// </summary>
    /// <param name="fileName">The rejected asset file name.</param>
    /// <returns>An actionable message, or <c>null</c> when the asset is understood.</returns>
    public static string? GetRejectionMessage(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !IsGuidedRejection(fileName))
        {
            return null;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return $"'{fileName}' cannot be installed directly. {ContentFormatConstants.GetRejectionGuidance(extension)}";
    }

    /// <summary>
    /// Partitions remote assets into usable content and guided rejections, so resolvers fail
    /// once with a single actionable message instead of per-file errors.
    /// </summary>
    /// <param name="assetNames">The remote asset file names.</param>
    /// <returns>Usable assets and rejection messages in input order.</returns>
    public static (IReadOnlyList<string> Usable, IReadOnlyList<string> Rejections) PartitionUsableAssets(
        IEnumerable<string> assetNames)
    {
        ArgumentNullException.ThrowIfNull(assetNames);

        var usable = new List<string>();
        var rejections = new List<string>();
        foreach (var assetName in assetNames)
        {
            var rejection = GetRejectionMessage(assetName);
            if (rejection is null)
            {
                usable.Add(assetName);
            }
            else
            {
                rejections.Add(rejection);
            }
        }

        return (usable, rejections);
    }
}
