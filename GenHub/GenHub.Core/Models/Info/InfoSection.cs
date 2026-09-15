using System.Collections.Generic;

namespace GenHub.Core.Models.Info;

/// <summary>
/// Represents a major section of information in GenHub.
/// </summary>
public class InfoSection
{
    /// <summary>Gets or sets the unique identifier for the section.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the display title of the section.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the short description of the section.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the order in which the section appears.</summary>
    public int Order { get; set; }

    /// <summary>Gets or sets the cards within this section.</summary>
    public List<InfoCard> Cards { get; set; } = [];
}
