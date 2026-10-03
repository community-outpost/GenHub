using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Procedural map generation settings (QT MapGen Genesis equivalent).
/// </summary>
public sealed class MapGenSettings
{
    /// <summary>Gets or sets the random seed driving the whole generation.</summary>
    public int Seed { get; set; }

    /// <summary>Gets or sets the playable width in cells (excluding border).</summary>
    public int PlayableWidth { get; set; } = WorldBuilderConstants.MapGen.DefaultWidth;

    /// <summary>Gets or sets the playable height in cells (excluding border).</summary>
    public int PlayableHeight { get; set; } = WorldBuilderConstants.MapGen.DefaultHeight;

    /// <summary>Gets or sets the border size in cells.</summary>
    public int Border { get; set; } = 10;

    /// <summary>Gets or sets the player count (2 or 4).</summary>
    public int NumPlayers { get; set; } = WorldBuilderConstants.MapGen.DefaultPlayers;

    /// <summary>Gets or sets the base height every cell starts at.</summary>
    public int BaseHeight { get; set; } = 100;

    /// <summary>Gets or sets the tree density percent.</summary>
    public int TreeDensity { get; set; } = 50;

    /// <summary>Gets or sets the cliff density percent.</summary>
    public int CliffDensity { get; set; } = 50;

    /// <summary>Gets or sets a value indicating whether cliffs are generated.</summary>
    public bool DoCliffs { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether textures are painted.</summary>
    public bool DoTextures { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether trees are placed.</summary>
    public bool DoTrees { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether rocks are placed.</summary>
    public bool DoRocks { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether skirmish sides are added.</summary>
    public bool DoPlayers { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether supplies are placed near starts.</summary>
    public bool DoSupplies { get; set; } = true;

    /// <summary>Gets or sets the noise amplitude in height units.</summary>
    public int NoiseHeight { get; set; } = 40;

    /// <summary>Gets or sets the noise elevation.</summary>
    public float NoiseElevation { get; set; } = 1.0f;

    /// <summary>Gets or sets the noise base frequency.</summary>
    public float NoiseJaggedness { get; set; } = 0.05f;

    /// <summary>Gets or sets the noise persistence.</summary>
    public float NoiseRuggedness { get; set; } = 0.5f;

    /// <summary>Gets or sets the noise octave count.</summary>
    public int NoiseOctaves { get; set; } = 4;

    /// <summary>Gets or sets the road routing mode.</summary>
    public MapGenRoadMode RoadMode { get; set; } = MapGenRoadMode.None;

    /// <summary>Gets or sets the road template placed between starts.</summary>
    public string RoadTemplate { get; set; } = WorldBuilderConstants.MapGen.DefaultRoadTemplate;

    /// <summary>Gets or sets the open-ground texture class painted by the generator.</summary>
    public string GroundTexture { get; set; } = WorldBuilderConstants.MapGen.DefaultGroundTexture;

    /// <summary>Gets or sets the cliff texture class painted by the generator.</summary>
    public string CliffTexture { get; set; } = WorldBuilderConstants.MapGen.DefaultCliffTexture;

    /// <summary>Gets or sets the supply source template placed near starts.</summary>
    public string SupplyTemplate { get; set; } = "SupplyDock";

    /// <summary>Gets the tree templates scattered as woodland.</summary>
    public List<string> TreeTemplates { get; } = [];

    /// <summary>Gets the rock templates scattered as props.</summary>
    public List<string> RockTemplates { get; } = [];
}
