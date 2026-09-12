using System;
using System.Linq;
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
    /// <param name="categoryCode">The ModDB category code (e.g., &quot;2&quot; for Full Version).</param>
    /// <returns>The mapped ContentType.</returns>
    public static ContentType MapCategory(string? categoryCode)
    {
        return categoryCode switch
        {
            // Releases (Mods)
            "2" => ContentType.Mod,  // Full Version
            "3" => ContentType.Mod,  // Demo
            "4" => ContentType.Patch, // Patch
            "28" => ContentType.Patch, // Script
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
            "112" => ContentType.Skin, // Player Skin
            "133" => ContentType.Skin, // Prop Skin
            "113" => ContentType.Skin, // Vehicle Skin
            "114" => ContentType.Skin, // Weapon Skin
            "134" => ContentType.Skin, // Skin Pack

            // Addons - Audio
            "117" => ContentType.Addon, // Music
            "119" => ContentType.Addon, // Player Audio
            "138" => ContentType.LanguagePack, // Language Sounds
            "118" => ContentType.Addon, // Audio Pack

            // Addons - Graphics
            "124" => ContentType.Addon, // Decal
            "136" => ContentType.Addon, // Effects GFX
            "125" => ContentType.Skin, // GUI
            "126" => ContentType.Skin, // HUD
            "128" => ContentType.Addon, // Sprite
            "129" => ContentType.Addon, // Texture

            // Default
            _ => ContentType.Addon,
        };
    }

    /// <summary>
    /// High-priority keyword rules evaluated before mod match heuristics.
    /// </summary>
    private static readonly (string[] Substrings, ContentType Type)[] PreModKeywordRules =
    [
        (["game client", "gameclient"], ContentType.GameClient),
        (["game installation", "gameinstallation"], ContentType.GameInstallation),
        (["content bundle", "contentbundle"], ContentType.ContentBundle),
        (["executable", "exe"], ContentType.Executable),
        (["trailer", "movie", "video"], ContentType.Video),
        (["tool", "sdk", "source code"], ContentType.ModdingTool),
    ];

    /// <summary>
    /// Post-mod keyword rules evaluated after mod match heuristics but before generic map/mission fallbacks.
    /// </summary>
    private static readonly (string[] Substrings, ContentType Type)[] PostModKeywordRules =
    [
        (["patch", "script"], ContentType.Patch),
        (["trainer"], ContentType.Addon),
        (["multiplayer map", "singleplayer map"], ContentType.Map),
        (["map pack", "mappack"], ContentType.MapPack),
    ];

    /// <summary>
    /// Fallback keyword rules evaluated after bare 'maps' token matching.
    /// </summary>
    private static readonly (string[] Substrings, ContentType Type)[] FallbackKeywordRules =
    [
        (["map", "prefab"], ContentType.Map),
        (["mission"], ContentType.Mission),
        (["skin", "gui", "hud"], ContentType.Skin),
        (["language"], ContentType.LanguagePack),
    ];

    /// <summary>
    /// Maps a friendly category name (from text scraping or provider metadata) to ContentType.
    /// </summary>
    /// <param name="categoryName">The category name (e.g., &quot;Full Version&quot;, &quot;GameClient&quot;, &quot;Multiplayer Map&quot;).</param>
    /// <returns>The mapped ContentType.</returns>
    public static ContentType MapCategoryByName(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return ContentType.Addon;
        }

        var trimmed = categoryName.Trim();
        if (TryMapExactEnum(trimmed, out var exactParsed))
        {
            return exactParsed;
        }

        var lower = trimmed.ToLowerInvariant();
        return MapByKeywords(lower);
    }

    private static bool TryMapExactEnum(string trimmed, out ContentType result)
    {
        var normalized = trimmed.Replace(" ", string.Empty).Replace("-", string.Empty);
        if (normalized.Length > 0 &&
            !char.IsDigit(normalized[0]) &&
            Enum.TryParse<ContentType>(normalized, ignoreCase: true, out var exactParsed) &&
            Enum.IsDefined(exactParsed) &&
            exactParsed != ContentType.UnknownContentType)
        {
            result = exactParsed;
            return true;
        }

        result = ContentType.Addon;
        return false;
    }

    private static ContentType MapByKeywords(string lower)
    {
        if (TryMatchRule(PreModKeywordRules, lower, out var preModType))
        {
            return preModType;
        }

        if (IsIdeMatch(lower))
        {
            return ContentType.ModdingTool;
        }

        if (lower.Contains("full version", StringComparison.Ordinal) ||
            lower.Contains("demo", StringComparison.Ordinal) ||
            IsModMatch(lower))
        {
            return ContentType.Mod;
        }

        if (TryMatchRule(PostModKeywordRules, lower, out var postModType))
        {
            return postModType;
        }

        if (lower == "maps")
        {
            return ContentType.MapPack;
        }

        if (TryMatchRule(FallbackKeywordRules, lower, out var fallbackType))
        {
            return fallbackType;
        }

        return ContentType.Addon;
    }

    private static bool TryMatchRule((string[] Substrings, ContentType Type)[] rules, string lower, out ContentType matchedType)
    {
        foreach (var (keywords, type) in rules)
        {
            if (keywords.Any(keyword => lower.Contains(keyword, StringComparison.Ordinal)))
            {
                matchedType = type;
                return true;
            }
        }

        matchedType = ContentType.Addon;
        return false;
    }

    private static bool IsIdeMatch(string s) =>
        s == "ide" || s.Contains(" ide") || s.Contains("ide ") || s.Contains("-ide") || s.Contains("ide-");

    private static bool IsModMatch(string s) =>
        s == "mod" || s == "mods" || s.StartsWith("mod ") || s.EndsWith(" mod");
}
