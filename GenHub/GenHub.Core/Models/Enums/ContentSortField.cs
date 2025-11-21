namespace GenHub.Core.Models.Enums;

/// <summary>
/// Specifies the sorting order for content search results.
/// </summary>
public enum ContentSortField
{
    /// <summary>
    /// No explicit sort order specified (default).
    /// </summary>
    None,

    /// <summary>
    /// Sort by relevance to the search query.
    /// </summary>
    Relevance,

    /// <summary>
    /// Sort by content name.
    /// </summary>
    Name,

    /// <summary>
    /// Sort by creation date.
    /// </summary>
    DateCreated,

    /// <summary>
    /// Sort by last updated date.
    /// </summary>
    DateUpdated,

    /// <summary>
    /// Sort by download count.
    /// </summary>
    DownloadCount,

    /// <summary>
    /// Sort by user rating.
    /// </summary>
    Rating,

    /// <summary>
    /// Sort by file size.
    /// </summary>
    Size,
}