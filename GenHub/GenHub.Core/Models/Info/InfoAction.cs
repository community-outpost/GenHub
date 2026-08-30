namespace GenHub.Core.Models.Info;

/// <summary>
/// Represents an actionable item on an info card.
/// </summary>
public class InfoAction
{
    /// <summary>Gets or sets the display text for the action.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the action identifier or command parameter.</summary>
    public string ActionId { get; set; } = string.Empty;

    /// <summary>Gets or sets the icon for the action.</summary>
    public string? IconKey { get; set; }

    /// <summary>Gets or sets a value indicating whether this is a primary action.</summary>
    public bool IsPrimary { get; set; }
}
