using System.Collections.Generic;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Info;

/// <summary>
/// Represents a single information card within a section.
/// </summary>
public class InfoCard
{
    /// <summary>Gets or sets the title of the card.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the main content or description.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Gets or sets the type of card (Concept, HowTo, etc.).</summary>
    public InfoCardType Type { get; set; } = InfoCardType.Concept;

    /// <summary>Gets or sets a value indicating whether the card can be expanded for more details.</summary>
    public bool IsExpandable { get; set; }

    /// <summary>Gets or sets the detailed content shown when expanded.</summary>
    public string? DetailedContent { get; set; }

    /// <summary>Gets or sets the list of actions available on this card.</summary>
    public List<InfoAction> Actions { get; set; } = [];
}
