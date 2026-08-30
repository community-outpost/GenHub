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
    /// Filename for subscriptions storage.
    /// </summary>
    public const string SubscriptionFileName = "subscriptions.json";

    /// <summary>
    /// Resolver ID for generic catalog resolver.
    /// </summary>
    public const string GenericCatalogResolverId = "generic-catalog";

    /// <summary>
    /// Default catalog cache expiration in hours.
    /// </summary>
    public const int DefaultCatalogCacheExpirationHours = 24;

    /// <summary>
    /// Maximum catalog size in bytes (10 MB).
    /// </summary>
    public const long MaxCatalogSizeBytes = 10 * 1024 * 1024;
}
