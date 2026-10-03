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
}
