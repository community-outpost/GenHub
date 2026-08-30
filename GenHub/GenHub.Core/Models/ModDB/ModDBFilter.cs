namespace GenHub.Core.Models.ModDB;

/// <summary>
/// Represents filter parameters for ModDB content discovery.
/// </summary>
public class ModDBFilter
{
    /// <summary>Gets or sets the search keyword.</summary>
    public string? Keyword { get; set; }

    /// <summary>Gets or sets the category filter (downloads section).</summary>
    public string? Category { get; set; }

    /// <summary>Gets or sets the addon category filter (addons section).</summary>
    public string? AddonCategory { get; set; }

    /// <summary>Gets or sets the timeframe filter.</summary>
    public string? Timeframe { get; set; }

    /// <summary>Gets or sets the licence filter.</summary>
    public string? Licence { get; set; }

    /// <summary>Gets or sets the sort parameter.</summary>
    public string? Sort { get; set; }

    /// <summary>Gets or sets the page number (1-based).</summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Builds a query string from the filter parameters.
    /// </summary>
    /// <returns>Query string (e.g., "?kw=test&amp;category=2").</returns>
    public string ToQueryString()
    {
        var parameters = new List<string>();

        if (!string.IsNullOrWhiteSpace(Keyword))
        {
            parameters.Add($"kw={Uri.EscapeDataString(Keyword)}");
        }

        if (!string.IsNullOrWhiteSpace(Category))
        {
            parameters.Add($"category={Category}");
        }

        if (!string.IsNullOrWhiteSpace(AddonCategory))
        {
            parameters.Add($"categoryaddon={AddonCategory}");
        }

        if (!string.IsNullOrWhiteSpace(Timeframe))
        {
            parameters.Add($"timeframe={Timeframe}");
        }

        if (!string.IsNullOrWhiteSpace(Licence))
        {
            parameters.Add($"licence={Licence}");
        }

        if (!string.IsNullOrWhiteSpace(Sort))
        {
            parameters.Add($"sort={Sort}");
        }

        if (parameters.Count > 0)
        {
            parameters.Insert(0, "filter=t");
        }

        return parameters.Count > 0 ? "?" + string.Join("&", parameters) : string.Empty;
    }
}
