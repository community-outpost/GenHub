using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

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
    /// Determines whether a remote asset is installable content as opposed to release
    /// notes or documentation sharing the release. Stricter than
    /// <see cref="IsUnderstoodAsset"/>: archives, runnable files, Flatpak bundles, and
    /// standalone game-data files count, while a <c>patch-notes.txt</c> beside them does not.
    /// </summary>
    /// <param name="fileName">The asset file name.</param>
    /// <returns><c>true</c> when the asset can be a download card on its own.</returns>
    public static bool IsContentAsset(string? fileName)
    {
        if (!IsUnderstoodAsset(fileName) || IsDocumentationFileName(fileName))
        {
            return false;
        }

        if (IsArchiveContainer(fileName)
            || ExecutableFileClassifier.RequiresExecutePermissionFromName(fileName!)
            || fileName!.EndsWith(ContentFormatConstants.FlatpakExtension, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ContentFormatConstants.StandaloneContentExtensions
            .Any(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether an asset file name represents documentation rather than installable content.
    /// </summary>
    /// <param name="fileName">The asset file name.</param>
    /// <returns><c>true</c> when the file name is recognized documentation.</returns>
    public static bool IsDocumentationFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (IsArchiveContainer(fileName)
            || ContentFormatConstants.StandaloneContentExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var bareName = Path.GetFileNameWithoutExtension(fileName);
        return ContentFormatConstants.KnownDocumentationFileNames
            .Any(doc => doc.Equals(bareName, StringComparison.OrdinalIgnoreCase));
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

        var extension = Path.GetExtension(fileName);
        return ContentFormatConstants.UnderstoodArchiveExtensions
            .Any(known => extension.Equals(known, StringComparison.OrdinalIgnoreCase));
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

        var extension = Path.GetExtension(StripArchiveExtensions(fileName));
        return ContentFormatConstants.GuidedRejectionExtensions
            .Any(rejected => extension.Equals(rejected, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds the guided-rejection failure message for an asset, or <c>null</c> when the
    /// asset is understood.
    /// </summary>
    /// <param name="fileName">The rejected asset file name.</param>
    /// <param name="localizationService">Optional localization service for user-facing text.</param>
    /// <returns>An actionable message, or <c>null</c> when the asset is understood.</returns>
    public static string? GetRejectionMessage(string? fileName, ILocalizationService? localizationService = null)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !IsGuidedRejection(fileName))
        {
            return null;
        }

        var extension = Path.GetExtension(StripArchiveExtensions(fileName)).ToLowerInvariant();
        var guidance = ContentFormatConstants.GetRejectionGuidance(extension, localizationService);
        if (localizationService?.TryGetString(ContentFormatConstants.RejectionCannotInstallDirectlyKey, out var localized, fileName, guidance) == true)
        {
            return localized;
        }

        return string.Format(CultureInfo.InvariantCulture, ContentFormatConstants.RejectionCannotInstallDirectlyFallback, fileName, guidance);
    }

    /// <summary>
    /// Partitions remote assets into usable content and guided rejections, so resolvers fail
    /// once with a single actionable message instead of per-file errors. Blank names are
    /// dropped: they are never usable content and carry no guidance either.
    /// </summary>
    /// <param name="assetNames">The remote asset file names.</param>
    /// <param name="localizationService">Optional localization service for user-facing text.</param>
    /// <returns>Usable assets and rejection messages in input order.</returns>
    public static (IReadOnlyList<string> Usable, IReadOnlyList<string> Rejections) PartitionUsableAssets(
        IEnumerable<string> assetNames,
        ILocalizationService? localizationService = null)
    {
        ArgumentNullException.ThrowIfNull(assetNames);

        var usable = new List<string>();
        var rejections = new List<string>();
        foreach (var assetName in assetNames)
        {
            if (!IsUnderstoodAsset(assetName))
            {
                var rejection = GetRejectionMessage(assetName, localizationService);
                if (rejection is not null)
                {
                    rejections.Add(rejection);
                }

                continue;
            }

            usable.Add(assetName);
        }

        return (usable, rejections);
    }

    private static string StripArchiveExtensions(string fileName)
    {
        var stripped = fileName;
        while (IsArchiveContainer(stripped))
        {
            var withoutExtension = Path.GetFileNameWithoutExtension(stripped);

            // Extensionless remains are a bare file name, not a nested container.
            if (string.IsNullOrEmpty(Path.GetExtension(withoutExtension)))
            {
                return withoutExtension;
            }

            stripped = withoutExtension;
        }

        return stripped;
    }
}
