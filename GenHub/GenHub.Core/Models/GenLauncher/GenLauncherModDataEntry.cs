using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace GenHub.Core.Models.GenLauncher;

/// <summary>
/// Represents a mod entry in the GenLauncher root catalog.
/// </summary>
public class GenLauncherModDataEntry
{
    /// <summary>Gets or sets the mod name.</summary>
    [YamlMember(Alias = "ModName")]
    public string ModName { get; set; } = string.Empty;

    /// <summary>Gets or sets the URL to the mod's version manifest YAML.</summary>
    [YamlMember(Alias = "ModLink")]
    public string ModLink { get; set; } = string.Empty;

    private List<string> _modPatches = [];
    private List<string> _modAddons = [];

    /// <summary>Gets or sets the list of patch manifest URLs.</summary>
    [YamlMember(Alias = "ModPatches")]
    public List<string> ModPatches
    {
        get => _modPatches;
        set => _modPatches = value ?? [];
    }

    /// <summary>Gets or sets the list of addon manifest URLs.</summary>
    [YamlMember(Alias = "ModAddons")]
    public List<string> ModAddons
    {
        get => _modAddons;
        set => _modAddons = value ?? [];
    }
}
