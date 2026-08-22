using System.Collections.Generic;
using GenHub.Core.Models.Theming;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants and preset palettes for application color theming.
/// </summary>
public static class ThemeConstants
{
    /// <summary>
    /// Default Void Purple theme.
    /// </summary>
    public static readonly ColorTheme DefaultTheme = new()
    {
        Id = "Purple",
        DisplayName = "Void Purple",
        PrimaryHex = "#A855F7",
        LightHex = "#C084FC",
        DarkHex = "#7E22CE",
        GlowHex = "#33A855F7",
    };

    /// <summary>
    /// Generals Command Orange theme.
    /// </summary>
    public static readonly ColorTheme GeneralsTheme = new()
    {
        Id = "Generals",
        DisplayName = "Generals Orange",
        PrimaryHex = "#F97316",
        LightHex = "#FB923C",
        DarkHex = "#C2410C",
        GlowHex = "#33F97316",
    };

    /// <summary>
    /// Zero Hour Tactical Cyan theme.
    /// </summary>
    public static readonly ColorTheme ZeroHourTheme = new()
    {
        Id = "ZeroHour",
        DisplayName = "Zero Hour Cyan",
        PrimaryHex = "#06B6D4",
        LightHex = "#22D3EE",
        DarkHex = "#0E7490",
        GlowHex = "#3306B6D4",
    };

    /// <summary>
    /// Emerald Toxic Green theme.
    /// </summary>
    public static readonly ColorTheme EmeraldTheme = new()
    {
        Id = "Emerald",
        DisplayName = "Emerald Green",
        PrimaryHex = "#10B981",
        LightHex = "#34D399",
        DarkHex = "#047857",
        GlowHex = "#3310B981",
    };

    /// <summary>
    /// NOD Crimson Red theme.
    /// </summary>
    public static readonly ColorTheme CrimsonTheme = new()
    {
        Id = "Crimson",
        DisplayName = "Crimson Red",
        PrimaryHex = "#EF4444",
        LightHex = "#F87171",
        DarkHex = "#B91C1C",
        GlowHex = "#33EF4444",
    };

    /// <summary>
    /// Cyber Amber Gold theme.
    /// </summary>
    public static readonly ColorTheme AmberTheme = new()
    {
        Id = "Amber",
        DisplayName = "Cyber Amber",
        PrimaryHex = "#F59E0B",
        LightHex = "#FBBF24",
        DarkHex = "#B45309",
        GlowHex = "#33F59E0B",
    };

    /// <summary>
    /// Cobalt Navy Blue theme.
    /// </summary>
    public static readonly ColorTheme CobaltTheme = new()
    {
        Id = "Cobalt",
        DisplayName = "Cobalt Blue",
        PrimaryHex = "#3B82F6",
        LightHex = "#60A5FA",
        DarkHex = "#1D4ED8",
        GlowHex = "#333B82F6",
    };

    /// <summary>
    /// Neon Rose Pink theme.
    /// </summary>
    public static readonly ColorTheme RoseTheme = new()
    {
        Id = "Rose",
        DisplayName = "Neon Rose",
        PrimaryHex = "#EC4899",
        LightHex = "#F472B6",
        DarkHex = "#BE185D",
        GlowHex = "#33EC4899",
    };

    /// <summary>
    /// Gets all available color themes.
    /// </summary>
    public static readonly IReadOnlyList<ColorTheme> AllThemes =
    [
        DefaultTheme,
        GeneralsTheme,
        ZeroHourTheme,
        EmeraldTheme,
        CrimsonTheme,
        AmberTheme,
        CobaltTheme,
        RoseTheme,
    ];
}
