using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents a project template for creating new ModBuilder projects.
/// </summary>
public class ProjectTemplate
{
    private const string DefaultBundleItemsConfig = "config/ModBundleItems.json";
    private const string DefaultBundlePacksConfig = "config/ModBundlePacks.json";

    /// <summary>
    /// Gets the empty project template.
    /// </summary>
    public static ProjectTemplate Empty => new()
    {
        Name = "Empty",
        Description = "Empty project with no default configurations",
        CreateSampleFiles = false,
    };

    /// <summary>
    /// Gets the Generals Community Patch 2.0 template.
    /// </summary>
    public static ProjectTemplate GeneralsGamePatch2 => new()
    {
        Name = "Generals Game Patch 2",
        Description = "TheSuperHackers Community Patch 2.0 with full balance and bugfix INI rules",
        DefaultBundleConfigs = new List<string>
        {
            DefaultBundleItemsConfig,
            DefaultBundlePacksConfig,
        },
        CreateSampleFiles = true,
    };

    /// <summary>
    /// Gets the hotkeys template.
    /// </summary>
    public static ProjectTemplate Hotkeys => new()
    {
        Name = "Hotkeys",
        Description = "Legionnaire QWERTY hotkey layout and control bar indicator overlays",
        DefaultBundleConfigs = new List<string>
        {
            DefaultBundleItemsConfig,
            DefaultBundlePacksConfig,
        },
        CreateSampleFiles = true,
    };

    /// <summary>
    /// Gets the improved menus template.
    /// </summary>
    public static ProjectTemplate ImprovedMenus => new()
    {
        Name = "Improved Menus",
        Description = "16:9 widescreen menu overhauls and custom UI windows (.wnd)",
        DefaultBundleConfigs = new List<string>
        {
            DefaultBundleItemsConfig,
            DefaultBundlePacksConfig,
        },
        CreateSampleFiles = true,
    };

    /// <summary>
    /// Gets the imported BIG archive project template.
    /// </summary>
    public static ProjectTemplate ImportedBig => new()
    {
        Name = "Imported BIG Mod",
        Description = "Project imported from existing .BIG archive(s) with unpacked game files",
        DefaultBundleConfigs = new List<string>
        {
            DefaultBundleItemsConfig,
            DefaultBundlePacksConfig,
        },
        CreateSampleFiles = false,
    };

    /// <summary>
    /// Gets or sets the template name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the template description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the default bundle configurations to include.
    /// </summary>
    public List<string> DefaultBundleConfigs { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether to create sample files.
    /// </summary>
    public bool CreateSampleFiles { get; set; } = false;
}
