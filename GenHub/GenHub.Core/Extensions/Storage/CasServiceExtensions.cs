using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Extensions.Storage;

/// <summary>
/// Extension methods for <see cref="ICasService"/>.
/// </summary>
public static class CasServiceExtensions
{
    /// <summary>
    /// Checks whether content with the given hash exists in CAS, trying the content-type pool
    /// first and falling back to a pool-agnostic lookup when the typed lookup fails.
    /// </summary>
    /// <param name="casService">The CAS service.</param>
    /// <param name="hash">The content hash to check.</param>
    /// <param name="contentType">The content type for pool routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the object exists in either lookup; otherwise, false.</returns>
    public static async Task<bool> ExistsInAnyPoolAsync(
        this ICasService casService,
        string hash,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        var existsResult = await casService.ExistsAsync(hash, contentType, cancellationToken).ConfigureAwait(false);
        if (existsResult is { Success: true })
        {
            return existsResult.Data;
        }

        var fallbackResult = await casService.ExistsAsync(hash, cancellationToken).ConfigureAwait(false);
        return fallbackResult is { Success: true, Data: true };
    }

    /// <summary>
    /// Returns the required content-addressable files whose objects are missing from CAS.
    /// A required entry counts as missing when its hash is empty or its object is absent
    /// from every pool.
    /// </summary>
    /// <param name="casService">The CAS service.</param>
    /// <param name="manifest">The manifest to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The required entries with missing CAS objects; empty when all are present.</returns>
    public static async Task<IReadOnlyList<ManifestFile>> GetMissingRequiredCasFilesAsync(
        this ICasService casService,
        ContentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var missingFiles = new List<ManifestFile>();
        if (manifest.Files == null)
        {
            return missingFiles;
        }

        foreach (var file in manifest.Files.Where(f => f.SourceType == ContentSourceType.ContentAddressable && f.IsRequired))
        {
            var exists = !string.IsNullOrEmpty(file.Hash) &&
                await casService.ExistsInAnyPoolAsync(file.Hash, manifest.ContentType, cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                missingFiles.Add(file);
            }
        }

        return missingFiles;
    }

    /// <summary>
    /// Collects display names ("ManifestName (relative/path)") for every required
    /// content-addressable file whose object is missing from CAS across the given manifests.
    /// </summary>
    /// <param name="casService">The CAS service.</param>
    /// <param name="manifests">The manifests to check.</param>
    /// <param name="onMissingFile">Optional callback invoked for each missing file, e.g. for logging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The display names of the missing files; empty when all required objects are present.</returns>
    public static async Task<IReadOnlyList<string>> CollectMissingRequiredCasDisplayNamesAsync(
        this ICasService casService,
        IEnumerable<ContentManifest> manifests,
        Action<ContentManifest, ManifestFile>? onMissingFile = null,
        CancellationToken cancellationToken = default)
    {
        var missingFiles = new List<string>();
        foreach (var manifest in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestDisplayName = !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : manifest.Id.Value;
            var missingCasFiles = await casService.GetMissingRequiredCasFilesAsync(manifest, cancellationToken).ConfigureAwait(false);
            foreach (var file in missingCasFiles)
            {
                missingFiles.Add($"{manifestDisplayName} ({file.RelativePath})");
                onMissingFile?.Invoke(manifest, file);
            }
        }

        return missingFiles;
    }

    /// <summary>
    /// Builds the user-facing failure message describing missing required CAS objects.
    /// </summary>
    /// <param name="missingFiles">The missing file display names.</param>
    /// <param name="messageFormat">The localized message format ({0} is the count, {1} is a sample list).</param>
    /// <returns>The failure message listing the missing object count and a sample.</returns>
    public static string BuildMissingCasObjectsMessage(IEnumerable<string> missingFiles, string messageFormat)
    {
        var distinctMissing = missingFiles.Distinct().ToList();
        return string.Format(CultureInfo.InvariantCulture, messageFormat, distinctMissing.Count, string.Join(", ", distinctMissing.Take(5)));
    }

    /// <summary>
    /// Verifies that every required content-addressable file across the given manifests exists in CAS.
    /// </summary>
    /// <param name="casService">The CAS service.</param>
    /// <param name="manifests">The manifests to check.</param>
    /// <param name="missingObjectsMessageFormat">The localized failure message format ({0} is the count, {1} is a sample list).</param>
    /// <param name="onMissingFile">Optional callback invoked for each missing file, e.g. for logging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when all required objects are present; otherwise, a failure listing the missing objects.</returns>
    public static async Task<OperationResult<bool>> VerifyRequiredCasContentAvailableAsync(
        this ICasService casService,
        IEnumerable<ContentManifest> manifests,
        string missingObjectsMessageFormat,
        Action<ContentManifest, ManifestFile>? onMissingFile = null,
        CancellationToken cancellationToken = default)
    {
        var missingFiles = await casService.CollectMissingRequiredCasDisplayNamesAsync(manifests, onMissingFile, cancellationToken).ConfigureAwait(false);
        if (missingFiles.Count > 0)
        {
            return OperationResult<bool>.CreateFailure(BuildMissingCasObjectsMessage(missingFiles, missingObjectsMessageFormat));
        }

        return OperationResult<bool>.CreateSuccess(true);
    }
}
