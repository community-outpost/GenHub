namespace GenHub.Core.Constants;

/// <summary>
/// Constants for publisher catalog system.
/// </summary>
public static class CatalogConstants
{
    /// <summary>
    /// Current catalog schema version.
    /// </summary>
    public const int CatalogSchemaVersion = 1;

    /// <summary>
    /// Resolver ID for generic catalog resolver.
    /// </summary>
    public const string GenericCatalogResolverId = "generic-catalog";

    /// <summary>
    /// Default catalog cache expiration in hours.
    /// </summary>
    public const int DefaultCatalogCacheExpirationHours = 24;

    /// <summary>
    /// File name for the catalog manifest (catalog.json).
    /// </summary>
    public const string CatalogManifestFileName = "catalog.json";

    /// <summary>
    /// File name for the catalog signature file (catalog.json.sig).
    /// </summary>
    public const string CatalogManifestSignatureFileName = "catalog.json.sig";

    /// <summary>
    /// File name for catalog metadata.
    /// </summary>
    [Obsolete("Use CatalogManifestFileName instead")]
    public const string CatalogMetaFileName = CatalogManifestFileName;

    /// <summary>
    /// File name for catalog signature.
    /// </summary>
    [Obsolete("Use CatalogManifestSignatureFileName instead")]
    public const string CatalogSignatureFileName = CatalogManifestSignatureFileName;
}