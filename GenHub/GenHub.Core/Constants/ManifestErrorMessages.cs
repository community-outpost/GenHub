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
    /// Error message for exporting a profile whose manifest has no variant for the current host.
    /// {0} is the manifest name and {1} is the host runtime identifier.
    /// </summary>
    public const string CannotExportNoHostVariant = "Cannot export '{0}': no variant supports this host ({1}).";
}
