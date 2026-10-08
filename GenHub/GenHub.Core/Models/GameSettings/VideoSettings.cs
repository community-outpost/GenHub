namespace GenHub.Core.Models.GameSettings;

/// <summary>
/// Video settings from the Options.ini file.
/// </summary>
public class VideoSettings
{
    /// <summary>Gets or sets the screen resolution width.</summary>
    public int ResolutionWidth { get; set; } = 800;

    /// <summary>Gets or sets the screen resolution height.</summary>
    public int ResolutionHeight { get; set; } = 600;

    /// <summary>Gets or sets a value indicating whether the game runs in windowed mode.</summary>
    public bool Windowed { get; set; } = false;

    /// <summary>Gets or sets the texture reduction level (0-3, higher = lower quality). Corresponds to "TextureReduction" in Options.ini.</summary>
    public int TextureReduction { get; set; } = 0;

    /// <summary>Gets or sets the anti-aliasing mode. Corresponds to "AntiAliasing" in Options.ini.</summary>
    public int AntiAliasing { get; set; } = 1;

    /// <summary>Gets or sets a value indicating whether shadow volumes are enabled. Corresponds to "UseShadowVolumes" in Options.ini.</summary>
    public bool UseShadowVolumes { get; set; } = false;

    /// <summary>Gets or sets a value indicating whether shadow decals are enabled. Corresponds to "UseShadowDecals" in Options.ini.</summary>
    public bool UseShadowDecals { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether extra animations are enabled.</summary>
    public bool ExtraAnimations { get; set; } = true;

    /// <summary>Gets or sets the gamma correction value (50-150 range).</summary>
    public int Gamma { get; set; } = 100;

    /// <summary>Gets or sets a value indicating whether the alternate mouse setup is enabled.</summary>
    public bool AlternateMouseSetup { get; set; } = false;

    /// <summary>Gets or sets a value indicating whether heat effects are enabled (performance intensive).</summary>
    public bool HeatEffects { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether building occlusion (behind buildings) is enabled.</summary>
    public bool BuildingOcclusion { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether props are shown.</summary>
    public bool ShowProps { get; set; } = true;

    /// <summary>Gets or sets additional video properties not explicitly defined. Used to preserve game-specific settings.</summary>
    public Dictionary<string, string> AdditionalProperties { get; set; } = [];
}
