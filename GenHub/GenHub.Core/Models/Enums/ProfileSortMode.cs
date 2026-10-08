namespace GenHub.Core.Models.Enums;

/// <summary>
/// Specifies the sorting strategy for game profiles on the launcher screen.
/// </summary>
public enum ProfileSortMode
{
    /// <summary>Sort by last played time, most recent first.</summary>
    LastPlayed = 0,

    /// <summary>Sort by creation date, newest first.</summary>
    DateCreated = 1,

    /// <summary>Sort alphabetically by profile name (A to Z).</summary>
    Alphabetical = 2,

    /// <summary>Sort reverse-alphabetically by profile name (Z to A).</summary>
    AlphabeticalDesc = 3,

    /// <summary>User-defined custom layout order.</summary>
    Free = 4,
}
