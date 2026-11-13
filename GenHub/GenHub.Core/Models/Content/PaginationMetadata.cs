namespace GenHub.Core.Models.Content;

/// <summary>
/// Contains pagination metadata returned by discoverers.
/// </summary>
public class PaginationMetadata
{
    /// <summary>
    /// Gets or sets a value indicating whether there are more pages available.
    /// </summary>
    public bool HasMorePages { get; set; }

    /// <summary>
    /// Gets or sets the total number of pages available (if known).
    /// </summary>
    public int? TotalPages { get; set; }

    /// <summary>
    /// Gets or sets the current page number.
    /// </summary>
    public int CurrentPage { get; set; }

    /// <summary>
    /// Gets or sets the number of items per page.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Gets or sets the total number of items (if known).
    /// </summary>
    public int? TotalItems { get; set; }
}
