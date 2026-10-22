namespace GenHub.Core.Constants;

/// <summary>
/// Error messages for manifests whose files or platform variants cannot be stored, retrieved or validated.
/// Messages with placeholders are composite format strings.
/// </summary>
public static class ManifestErrorMessages
{
    /// <summary>
    /// Error message for a manifest with a null variant, file entry or file collection.
    /// </summary>
    public const string NullFileEntries = "Manifest contains a null variant or file entry or file collection";

    /// <summary>
    /// Error message for storing a manifest that has no variant for the current host.
    /// </summary>
    public const string NoHostVariant = "Manifest has no variant for this host";

    /// <summary>
    /// Error message for retrieving a manifest that has no variant for the current host.
    /// {0} is the manifest ID and {1} is the host runtime identifier.
    /// </summary>
    public const string NoHostVariantForManifest = "Manifest {0} has no variant for this host ({1})";

    /// <summary>
    /// Error message for a required file whose object is missing from CAS. {0} is the relative path.
    /// </summary>
    public const string RequiredFileUnavailableInCas = "Required file {0} is unavailable in CAS";

    /// <summary>
    /// Validation message for a manifest with no variant for the current host. {0} is the host runtime identifier.
    /// </summary>
    public const string NoHostVariantForValidation = "Manifest has no variant supporting this host ({0}).";

    /// <summary>
    /// Validation message for a file entry without a relative path during integrity checks.
    /// </summary>
    public const string ManifestFileMissingRelativePath = "Manifest file is missing its RelativePath.";

    /// <summary>
    /// Validation message for a null variant. {0} is the variant index.
    /// </summary>
    public const string VariantIsNull = "Variant at index {0} is null.";

    /// <summary>
    /// Validation message for a null file collection. {0} is the location suffix, such as " in variant 1", or empty for the flat list.
    /// </summary>
    public const string FileCollectionIsNull = "Manifest Files collection{0} is null.";

    /// <summary>
    /// Validation message for a null file entry. {0} is the file index and {1} the location suffix.
    /// </summary>
    public const string FileEntryIsNull = "File at index {0}{1} is null.";

    /// <summary>
    /// Validation message for a file entry without a relative path. {0} is the file index and {1} the location suffix.
    /// </summary>
    public const string FileEntryMissingRelativePath = "File at index {0}{1} is missing its RelativePath.";

    /// <summary>
    /// Validation message for a file whose path resolves outside the content directory.
    /// {0} is the relative path.
    /// </summary>
    public const string InvalidFilePathOutsideContentDirectory = "Invalid file path (outside content directory): {0}";

    /// <summary>
    /// Validation message for a CAS existence check failure.
    /// {0} is the hash and {1} is the error reason.
    /// </summary>
    public const string CasCheckFailedForHash = "CAS check failed for hash {0}: {1}";

    /// <summary>
    /// Validation message for a ContentAddressable file entry missing its hash.
    /// {0} is the relative path.
    /// </summary>
    public const string ContentAddressableMissingHash = "ContentAddressable file missing hash: {0}";

    /// <summary>
    /// Validation message for a file that does not exist.
    /// {0} is the relative path.
    /// </summary>
    public const string FileNotFound = "File not found: {0}";

    /// <summary>
    /// Validation message for a file whose hash does not match the manifest.
    /// {0} is the relative path.
    /// </summary>
    public const string HashMismatchForFile = "Hash mismatch for file: {0}";

    /// <summary>
    /// Validation message when the target content directory does not exist.
    /// {0} is the content path.
    /// </summary>
    public const string ContentDirectoryDoesNotExist = "Content directory does not exist: {0}";

    /// <summary>
    /// Validation message for an extraneous file detected in the content directory.
    /// {0} is the relative path.
    /// </summary>
    public const string ExtraneousFileDetected = "Extraneous file detected (not in manifest): {0}";

    /// <summary>
    /// Validation message for a manifest missing an ID.
    /// </summary>
    public const string ManifestIdMissing = "Manifest Id is missing.";

    /// <summary>
    /// Validation message for a manifest missing a name.
    /// </summary>
    public const string ManifestNameMissing = "Manifest Name is missing.";

    /// <summary>
    /// Validation message for a manifest missing a version.
    /// </summary>
    public const string ManifestVersionMissing = "Manifest Version is missing.";

    /// <summary>
    /// Validation message for a manifest containing no files.
    /// </summary>
    public const string ManifestContainsNoFiles = "Manifest contains no files.";

    /// <summary>
    /// Format string for variant location suffix in file structure validation.
    /// {0} is the variant index.
    /// </summary>
    public const string VariantLocationSuffix = " in variant {0}";

    /// <summary>
    /// English fallback error message for exporting a profile whose manifest has no variant for the current host.
    /// Used as fallback anchor for <see cref="ProfileSharingConstants.CannotExportNoHostVariantErrorKey"/>.
    /// {0} is the manifest name and {1} is the host runtime identifier.
    /// </summary>
    public const string CannotExportNoHostVariant = "Cannot export '{0}': no variant supports this host ({1}).";

    /// <summary>
    /// Resource key for <see cref="UnsupportedManifestFormatVersion"/>.
    /// </summary>
    public const string UnsupportedManifestFormatVersionKey = "Manifest.Format.Unsupported";

    /// <summary>
    /// English fallback when a manifest declares a newer format than this build supports.
    /// {0} is the manifest ID, {1} the declared format and {2} the highest supported format.
    /// </summary>
    public const string UnsupportedManifestFormatVersion = "Manifest '{0}' declares format version {1}, but this version of GenHub supports up to format version {2}. Update GenHub to install it.";
}
