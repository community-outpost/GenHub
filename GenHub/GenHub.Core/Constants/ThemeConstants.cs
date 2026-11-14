using GenHub.Core.Models.Theming;
using System.Collections.Generic;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants and preset palettes for application color theming.
/// </summary>
public static class ThemeConstants
{
    /// <summary>Approved resting fill for primary actions under the default theme.</summary>
    public const string DefaultActionRestHex = "#7540B8";

    /// <summary>Approved pointer-over fill for primary actions under the default theme.</summary>
    public const string DefaultActionHoverHex = "#8651C9";

    /// <summary>Approved pressed fill for primary actions under the default theme.</summary>
    public const string DefaultActionPressedHex = "#65339F";

    /// <summary>Start of the default resting primary action gradient.</summary>
    public const string DefaultActionRestGradientStartHex = "#8241CF";

    /// <summary>End of the default resting primary action gradient.</summary>
    public const string DefaultActionRestGradientEndHex = "#6930B3";

    /// <summary>Start of the default pressed primary action gradient.</summary>
    public const string DefaultActionPressedGradientStartHex = "#7135BC";

    /// <summary>End of the default pressed primary action gradient.</summary>
    public const string DefaultActionPressedGradientEndHex = "#592696";

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
    /// Tiberium Lime theme.
    /// </summary>
    public static readonly ColorTheme TiberiumTheme = new()
    {
        Id = "Tiberium",
        DisplayName = "Tiberium Lime",
        PrimaryHex = "#84CC16",
        LightHex = "#A3E635",
        DarkHex = "#4D7C0F",
        GlowHex = "#3384CC16",
    };

    /// <summary>
    /// Deep Teal theme.
    /// </summary>
    public static readonly ColorTheme TealTheme = new()
    {
        Id = "Teal",
        DisplayName = "Deep Teal",
        PrimaryHex = "#14B8A6",
        LightHex = "#2DD4BF",
        DarkHex = "#0F766E",
        GlowHex = "#3314B8A6",
    };

    /// <summary>
    /// Electric Indigo theme.
    /// </summary>
    public static readonly ColorTheme IndigoTheme = new()
    {
        Id = "Indigo",
        DisplayName = "Electric Indigo",
        PrimaryHex = "#6366F1",
        LightHex = "#818CF8",
        DarkHex = "#4338CA",
        GlowHex = "#336366F1",
    };

    /// <summary>
    /// Blood Ruby theme.
    /// </summary>
    public static readonly ColorTheme RubyTheme = new()
    {
        Id = "Ruby",
        DisplayName = "Blood Ruby",
        PrimaryHex = "#F43F5E",
        LightHex = "#FB7185",
        DarkHex = "#BE123C",
        GlowHex = "#33F43F5E",
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
        TiberiumTheme,
        TealTheme,
        IndigoTheme,
        RubyTheme,
    ];
}
