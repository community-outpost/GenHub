namespace GenHub.Core.Models.Enums;

/// <summary>
/// Specifies how game profiles are presented on the launcher screen.
/// </summary>
public enum ProfileLayout
{
    /// <summary>A searchable profile list beside a focused launch page.</summary>
    Library = 0,

    /// <summary>A gallery of profile cards with launch actions on each card.</summary>
    Cards = 1,
}
