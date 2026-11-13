namespace GenHub.Core.Models.Providers;

/// <summary>
/// Controls how catalog normalization and validation treat publisher-defined custom tabs.
/// </summary>
public enum CatalogTabHandling
{
    /// <summary>
    /// Drops tabs and cards that break the tab rules and fails only on structural tab problems.
    /// Used when a catalog is loaded for display.
    /// </summary>
    Sanitize,

    /// <summary>
    /// Keeps every tab and card as authored and skips the tab rules.
    /// Used when Publisher Studio imports an author's own catalog, so its strict validation can report the problems.
    /// </summary>
    Preserve,
}
