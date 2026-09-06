using System.Globalization;
using GenHub.Core.Constants;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Fail-closed gate for manifests that declare artifact variants.
/// </summary>
/// <remarks>
/// Variants are expressed by <see cref="ContentManifest.Variants"/> and resolved by
/// <see cref="ManifestVariantResolver"/>, but the consumers that act on a manifest —
/// deliverers, validators, CAS reference counting and garbage collection — still read
/// <see cref="ContentManifest.Files"/> directly. A manifest declaring variants would be
/// accepted and then mishandled by every one of them: <c>Files</c> is empty for a
/// variant manifest, so content would appear to install successfully while delivering
/// nothing, and reference counting would record the wrong set of blobs.
/// <para>
/// Rejecting at ingestion is therefore deliberate and temporary. It is the only
/// protection until the consumers are migrated to the resolved-variant model, and it
/// should be removed as part of that migration rather than relaxed piecemeal.
/// </para>
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

        // Checked independently rather than as one condition. Declared version alone is
        // not trustworthy — a manifest can carry variants while still claiming version 1 —
        // and variants alone are not the only signal, since a format-2 manifest may use
        // other version-2 features this pipeline equally cannot handle.
        var declaresVariants = manifest.Variants.Count > 0;
        var declaresVariantFormat =
            int.TryParse(
                manifest.SchemaVersion,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var declaredFormat)
            && declaredFormat >= ManifestConstants.VariantsManifestFormatVersion;

        if (!declaresVariants && !declaresVariantFormat)
        {
            return true;
        }

        var cause = declaresVariants
            ? $"declares {manifest.Variants.Count} artifact variant(s)"
            : $"declares manifest format version {manifest.SchemaVersion}";

        rejectionReason =
            $"Manifest '{manifest.Id.Value}' {cause}, which requires manifest format version " +
            $"{ManifestConstants.VariantsManifestFormatVersion} and is not yet accepted. " +
            "Variant manifests are rejected until the content pipeline is migrated to the " +
            "resolved-variant model; publish a manifest without variants in the meantime.";

        return false;
    }
}
