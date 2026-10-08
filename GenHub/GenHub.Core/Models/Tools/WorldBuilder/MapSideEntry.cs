namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A player side with its property dictionary and build list.
/// </summary>
public sealed class MapSideEntry
{
    /// <summary>Gets the property dictionary.</summary>
    public MapDict Properties { get; } = new();

    /// <summary>Gets the pre-placed buildings.</summary>
    public List<MapBuildListEntry> BuildList { get; } = [];
}
