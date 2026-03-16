namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Community Outpost catalog parsing and metadata keys.
/// </summary>
public static class CommunityOutpostCatalogConstants
{
    /// <summary>The catalog format identifier for GenPatcher .dat files.</summary>
    public const string CatalogFormat = "genpatcher-dat";

    /// <summary>Default version string when version is unknown.</summary>
    public const string UnknownVersion = "unknown";

    /// <summary>Default base URL for making relative URLs absolute.</summary>
    public const string DefaultBaseUrl = "https://legi.cc/patch";

    /// <summary>Metadata key for the content code.</summary>
    public const string ContentCodeKey = "contentCode";

    /// <summary>Metadata key for the catalog version.</summary>
    public const string CatalogVersionKey = "catalogVersion";

    /// <summary>Metadata key for the file size.</summary>
    public const string FileSizeKey = "fileSize";

    /// <summary>Metadata key for the content category.</summary>
    public const string CategoryKey = "category";

    /// <summary>Metadata key for the install target.</summary>
    public const string InstallTargetKey = "installTarget";

    /// <summary>Metadata key for the mirror URLs (JSON serialized).</summary>
    public const string MirrorUrlsKey = "mirrorUrls";

    /// <summary>Metadata key for the mirror names display string.</summary>
    public const string MirrorsKey = "mirrors";

    /// <summary>Endpoint key for the patch page URL.</summary>
    public const string PatchPageUrlEndpoint = "patchPageUrl";

    /// <summary>Default version for content metadata.</summary>
    public const string DefaultMetadataVersion = "1.0";
}
