namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// The full in-memory map document.
/// </summary>
public sealed class WorldBuilderMap
{
    /// <summary>Gets or sets the source file path.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the document has unsaved changes.</summary>
    public bool IsDirty { get; set; }

    /// <summary>Gets or sets the terrain data.</summary>
    public MapTerrainData Terrain { get; set; } = new();

    /// <summary>Gets the world dictionary.</summary>
    public MapDict World { get; } = new();

    /// <summary>Gets the player sides.</summary>
    public List<MapSideEntry> Sides { get; } = [];

    /// <summary>Gets the skirmish teams.</summary>
    public List<MapTeamEntry> Teams { get; } = [];

    /// <summary>Gets the map objects.</summary>
    public List<MapObjectEntry> Objects { get; } = [];

    /// <summary>Gets the polygon triggers.</summary>
    public List<MapTrigger> Triggers { get; } = [];

    /// <summary>Gets the per-player script lists.</summary>
    public List<ScriptListModel> Scripts { get; } = [];

    /// <summary>Gets the waypoint links.</summary>
    public List<MapWaypointLink> WaypointLinks { get; } = [];

    /// <summary>Gets or sets the lighting data.</summary>
    public MapLightingData Lighting { get; set; } = new();

    /// <summary>Gets or sets the embedded preview (when present).</summary>
    public MapPreviewData? Preview { get; set; }

    /// <summary>Gets the wave tracks from the .wak companion.</summary>
    public List<WaveTrackRecord> Waves { get; } = [];

    /// <summary>Gets the unknown top-level chunks retained verbatim for lossless round-trip, in original file order.</summary>
    public List<MapChunkNode> UnknownChunks { get; } = [];

    /// <summary>Gets the original top-level chunk labels in file order, used to replay chunk order on save.</summary>
    public List<string> ChunkOrder { get; } = [];

    /// <summary>Gets the original table-of-contents entries in file order, used to preserve chunk ids on save.</summary>
    public List<KeyValuePair<uint, string>> LabelTable { get; } = [];
}
