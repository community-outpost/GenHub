namespace GenHub.Core.Models.Content;

/// <summary>
/// Contains pagination metadata returned by discoverers.
/// </summary>
public class PaginationMetadata
{
    private int _currentPage = 1;
    private int _pageSize = 20;

    /// <summary>
    /// Gets or sets a value indicating whether there are more pages available.
    /// </summary>
    public bool HasMorePages { get; set; }

    /// <summary>
    /// Gets or sets the total number of pages available (if known).
    /// </summary>
    public int? TotalPages { get; set; }

    /// <summary>
    /// Gets or sets the current page number. Must be at least 1.
    /// </summary>
    public int CurrentPage
    {
        get => _currentPage;
        set => _currentPage = Math.Max(1, value);
    }

    /// <summary>
    /// Gets or sets the number of items per page. Must be at least 1.
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Max(1, value);
    }

    /// <summary>
    /// Gets or sets the total number of items (if known).
    /// </summary>
    public int? TotalItems { get; set; }
}
