namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Map generator road routing modes (WBMapGenRoadMode equivalent).
/// </summary>
public enum MapGenRoadMode
{
    /// <summary>No generated roads.</summary>
    None = 0,

    /// <summary>Roads connecting consecutive start positions in a ring.</summary>
    Starts = 1,

    /// <summary>Start-to-start roads routed via supply docks.</summary>
    Supplies = 2,
}
