namespace GenHub.Core.Constants;

/// <summary>
/// Resource keys used by the local filter on curated catalogs in the Downloads browser.
/// </summary>
public static class CuratedCatalogFilterConstants
{
    /// <summary>
    /// The resource key of the chip that selects every category.
    /// </summary>
    public const string AllLabelKey = "Downloads.Browser.Filter.All";

    /// <summary>
    /// The resource key prefix of catalog category chip labels, followed by the category identifier.
    /// </summary>
    public const string CategoryLabelKeyPrefix = "Downloads.Browser.Filter.Category.";

    /// <summary>
    /// The resource key prefix of content type labels, followed by the content type identifier.
    /// </summary>
    public const string ContentTypeLabelKeyPrefix = "ContentType.";
}
