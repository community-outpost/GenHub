using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents a query for searching content across providers.
/// </summary>
public class ContentSearchQuery
{
    /// <summary>
    /// Gets or sets the primary search term.
    /// </summary>
    public string SearchTerm { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of content to filter by.
    /// </summary>
    public ContentType? ContentType { get; set; }

    /// <summary>
    /// Gets or sets the target game to filter by.
    /// </summary>
    public GameType? TargetGame { get; set; }

    /// <summary>
    /// Gets or sets a list of tags to filter by.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Gets or sets the author's name to filter by.
    /// </summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the minimum last updated date.
    /// </summary>
    public DateTime? MinDate { get; set; }

    /// <summary>
    /// Gets or sets the maximum last updated date.
    /// </summary>
    public DateTime? MaxDate { get; set; }

    /// <summary>
    /// Gets the number of results to skip for pagination.
    /// </summary>
    public int Skip { get; init; } = 0;

    /// <summary>
    /// Gets the number of results to take for pagination.
    /// </summary>
    public int Take { get; init; } = 50;

    /// <summary>
    /// Gets or sets the order to sort the results by.
    /// </summary>
    public ContentSortField SortOrder { get; set; } = ContentSortField.Relevance;

    /// <summary>
    /// Gets or sets a value indicating whether to include already installed content in the results.
    /// </summary>
    public bool IncludeInstalled { get; set; } = true;

    /// <summary>
    /// Gets or sets number of players filter by.
    /// </summary>
    public int? NumberOfPlayers { get; set; }

    /// <summary>
    /// Gets or sets page number.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets sort value.
    /// </summary>
    public string Sort { get; set; } = string.Empty;
}