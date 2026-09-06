using System.Collections.Generic;
using GenHub.Core.Models.Content;

namespace GenHub.Core.Models.Results.Content;

/// <summary>
/// Represents the result of a content discovery operation, including items and pagination metadata.
/// </summary>
public class ContentDiscoveryResult
{
    /// <summary>
    /// Gets or initializes the discovered content items.
    /// </summary>
    public IEnumerable<ContentSearchResult> Items { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether there are more items available to load.
    /// </summary>
    public bool HasMoreItems { get; init; }

    /// <summary>
    /// Gets or initializes the total number of items available, if known.
    /// </summary>
    public int? TotalItems { get; init; }

    /// <summary>
    /// Gets a value indicating whether discovery was blocked by a bot/Cloudflare challenge requiring user verification.
    /// </summary>
    public bool ChallengeDetected { get; init; }
}
