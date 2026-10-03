using GenHub.Core.Constants;
using System.Globalization;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Rejects manifests that declare a format version newer than this build understands.
/// </summary>
/// <remarks>
/// Every consumer resolves files through <see cref="ManifestVariantResolver"/>, so manifests
/// with artifact variants (format version <see cref="ManifestConstants.VariantsManifestFormatVersion"/>)
/// are accepted. A newer format may carry features this pipeline cannot handle, so it is
/// rejected rather than installed with those features silently ignored.
/// </remarks>
public static class ManifestIngestionGate
{
    /// <summary>
    /// Determines whether a manifest may be ingested.
    /// </summary>
    /// <param name="manifest">The manifest to check.</param>
    /// <param name="rejectionReason">
    /// When the manifest is rejected, a message naming the manifest and the reason;
    /// otherwise <c>null</c>.
    /// </param>
    /// <returns><c>true</c> when the manifest may be ingested; otherwise <c>false</c>.</returns>
    public static bool TryAccept(ContentManifest? manifest, out string? rejectionReason)
    {
        rejectionReason = null;

        if (manifest is null)
        {
            return true;
        }

        if (!int.TryParse(manifest.SchemaVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var declaredFormat)
            || declaredFormat <= ManifestConstants.MaxSupportedManifestFormatVersion)
        {
            return true;
        }

        rejectionReason = string.Format(
            CultureInfo.InvariantCulture,
            ManifestErrorMessages.UnsupportedManifestFormatVersion,
            manifest.Id.Value,
            manifest.SchemaVersion,
            ManifestConstants.MaxSupportedManifestFormatVersion);
        return false;
    }
}
