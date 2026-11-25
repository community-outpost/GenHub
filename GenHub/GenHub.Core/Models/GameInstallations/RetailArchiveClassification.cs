namespace GenHub.Core.Models.GameInstallations;

/// <summary>
/// Which games' retail archives a directory holds.
/// </summary>
/// <param name="HasGeneralsArchives">Whether any canonical Generals archive is present.</param>
/// <param name="HasZeroHourArchives">Whether any Zero Hour archive is present.</param>
/// <remarks>
/// Both flags true is a real state, not a conflict: a combined directory (for example a
/// flat native deploy holding <c>INI.big</c> and <c>INIZH.big</c> side by side) carries
/// retail data for both games and must be treated as one installation with both paths set.
/// </remarks>
public readonly record struct RetailArchiveClassification(bool HasGeneralsArchives, bool HasZeroHourArchives)
{
    /// <summary>
    /// Gets a value indicating whether either game's archives are present.
    /// </summary>
    public bool HasAnyGame => HasGeneralsArchives || HasZeroHourArchives;
}
