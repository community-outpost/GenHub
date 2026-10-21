namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A team entry backed by its property dictionary.
/// </summary>
public sealed class MapTeamEntry
{
    /// <summary>Gets the property dictionary.</summary>
    public MapDict Properties { get; } = new();
}
