namespace GenHub.Core.Models.Enums;

/// <summary>
/// Specifies the sorting order for content search results.
/// </summary>
public enum ContentSortField
{
    /// <summary>
    /// No explicit sort order specified (default).
    /// </summary>
    None = 0,

    /// <summary>
    /// Sort by relevance to the search query.
    /// </summary>
    Relevance = 1,

    /// <summary>
    /// Sort by content name.
    /// </summary>
    Name = 2,

    /// <summary>
    /// Sort by creation date.
    /// </summary>
    DateCreated = 3,

    /// <summary>
    /// Sort by last updated date.
    /// </summary>
    DateUpdated = 4,

    /// <summary>
    /// Sort by download count.
    /// </summary>
    DownloadCount = 5,

    /// <summary>
    /// Sort by user rating.
    /// </summary>
    Rating = 6,

    /// <summary>
    /// Sort by file size.
    /// </summary>
    Size = 7,
}
