using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.ModDB;

/// <summary>
/// Maps ModDB categories to ContentType enum values.
/// </summary>
public static class ModDBCategoryMapper
{
    /// <summary>
    /// Maps a ModDB category code to a ContentType.
    /// </summary>
    /// <param name="categoryCode">The ModDB category code (e.g., "2" for Full Version).</param>
    /// <returns>The mapped ContentType.</returns>
    public static ContentType MapCategory(string? categoryCode)
    {
        return categoryCode switch
        {
            // Releases (Mods)
            "2" => ContentType.Mod,  // Full Version
            "3" => ContentType.Mod,  // Demo
            "4" => ContentType.Mod,  // Patch (mod release/update)
            "28" => ContentType.Mod, // Script (mod script/release)
            "29" => ContentType.Addon, // Trainer

            // Media
            "7" => ContentType.Video, // Trailer
            "8" => ContentType.Video, // Movie
            "9" => ContentType.Addon, // Music
            "25" => ContentType.Addon, // Audio
            "10" => ContentType.Addon, // Wallpaper

            // Tools
            "20" => ContentType.ModdingTool, // Archive Tool
            "13" => ContentType.ModdingTool, // Graphics Tool
            "14" => ContentType.ModdingTool, // Mapping Tool
            "15" => ContentType.ModdingTool, // Modelling Tool
            "16" => ContentType.ModdingTool, // Installer Tool
            "17" => ContentType.ModdingTool, // Server Tool
            "18" => ContentType.ModdingTool, // IDE
            "19" => ContentType.ModdingTool, // SDK
            "26" => ContentType.ModdingTool, // Source Code

            // Miscellaneous
            "22" => ContentType.Addon, // Guide
            "23" => ContentType.Addon, // Tutorial
            "30" => ContentType.LanguagePack, // Language Pack
            "24" => ContentType.Addon, // Other

            // Addons - Maps
            "101" => ContentType.Map, // Multiplayer Map
            "102" => ContentType.Map, // Singleplayer Map
            "103" => ContentType.Map, // Prefab

            // Addons - Models
            "106" => ContentType.Addon, // Player Model
            "132" => ContentType.Addon, // Prop Model
            "107" => ContentType.Addon, // Vehicle Model
            "108" => ContentType.Addon, // Weapon Model
            "131" => ContentType.Addon, // Model Pack

            // Addons - Skins
            "112" => ContentType.Addon, // Player Skin
            "133" => ContentType.Addon, // Prop Skin
            "113" => ContentType.Addon, // Vehicle Skin
            "114" => ContentType.Addon, // Weapon Skin
            "134" => ContentType.Addon, // Skin Pack

            // Addons - Audio
            "117" => ContentType.Addon, // Music
            "119" => ContentType.Addon, // Player Audio
            "138" => ContentType.LanguagePack, // Language Sounds
            "118" => ContentType.Addon, // Audio Pack

            // Addons - Graphics
            "124" => ContentType.Addon, // Decal
            "136" => ContentType.Addon, // Effects GFX
            "125" => ContentType.Addon, // GUI
            "126" => ContentType.Addon, // HUD
            "128" => ContentType.Addon, // Sprite
            "129" => ContentType.Addon, // Texture

            // Default
            _ => ContentType.Addon,
        };
    }

    /// <summary>
    /// Maps a friendly category name (from text scraping) to ContentType.
    /// </summary>
    /// <param name="categoryName">The category name (e.g., "Full Version", "Multiplayer Map").</param>
    /// <returns>The mapped ContentType.</returns>
    public static ContentType MapCategoryByName(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return ContentType.Addon;
        }

        var lower = categoryName.ToLowerInvariant();

        return lower switch
        {
            var s when s.Contains("full version") => ContentType.Mod,
            var s when s.Contains("demo") => ContentType.Mod,
            var s when s.Contains("patch") => ContentType.Mod,
            var s when s.Contains("script") => ContentType.Mod,
            var s when s.Contains("trainer") => ContentType.Addon,

            var s when s.Contains("tool") => ContentType.ModdingTool,
            var s when s.Contains("sdk") => ContentType.ModdingTool,
            var s when s.Contains("ide") => ContentType.ModdingTool,
            var s when s.Contains("source code") => ContentType.ModdingTool,

            var s when s.Contains("trailer") => ContentType.Video,
            var s when s.Contains("movie") => ContentType.Video,
            var s when s.Contains("video") => ContentType.Video,

            var s when s.Contains("multiplayer map") => ContentType.Map,
            var s when s.Contains("singleplayer map") => ContentType.Map,
            var s when s.Contains("map") => ContentType.Map,
            var s when s.Contains("prefab") => ContentType.Map,

            var s when s.Contains("skin") => ContentType.Addon,
            var s when s.Contains("gui") => ContentType.Addon,
            var s when s.Contains("hud") => ContentType.Addon,

            var s when s.Contains("language") => ContentType.LanguagePack,

            _ => ContentType.Addon,
        };
    }
}
