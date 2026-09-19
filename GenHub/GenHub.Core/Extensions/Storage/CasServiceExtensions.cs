using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using System.Collections.Generic;
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
}
