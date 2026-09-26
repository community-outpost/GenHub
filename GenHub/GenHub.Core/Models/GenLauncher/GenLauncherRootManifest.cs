using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace GenHub.Core.Models.GenLauncher;

/// <summary>
/// Represents the root manifest of a GenLauncher repository (e.g. ReposModificationDataZH3.yaml).
/// </summary>
public class GenLauncherRootManifest
{
    private List<GenLauncherModDataEntry> _modDatas = [];
    private List<string> _originalGamePatches = [];
    private List<string> _originalGameAddons = [];
    private List<string> _globalAddonsData = [];

    /// <summary>Gets or sets the GenLauncher launcher version.</summary>
    [YamlMember(Alias = "LauncherVersion")]
    public string LauncherVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the launcher download link.</summary>
    [YamlMember(Alias = "DownloadLink")]
    public string DownloadLink { get; set; } = string.Empty;

    /// <summary>Gets or sets the list of mods in this catalog.</summary>
    [YamlMember(Alias = "modDatas")]
    public List<GenLauncherModDataEntry> ModDatas
    {
        get => _modDatas;
        set => _modDatas = value ?? [];
    }

    /// <summary>Gets or sets the list of patches for the original game.</summary>
    [YamlMember(Alias = "originalGamePatches")]
    public List<string> OriginalGamePatches
    {
        get => _originalGamePatches;
        set => _originalGamePatches = value ?? [];
    }

    /// <summary>Gets or sets the list of addons for the original game.</summary>
    [YamlMember(Alias = "originalGameAddons")]
    public List<string> OriginalGameAddons
    {
        get => _originalGameAddons;
        set => _originalGameAddons = value ?? [];
    }

    /// <summary>Gets or sets the list of global addons.</summary>
    [YamlMember(Alias = "globalAddonsData")]
    public List<string> GlobalAddonsData
    {
        get => _globalAddonsData;
        set => _globalAddonsData = value ?? [];
    }

    /// <summary>Gets or sets Vulkan repository data link, if present.</summary>
    [YamlMember(Alias = "VulkanReposData")]
    public string? VulkanReposData { get; set; }
}
