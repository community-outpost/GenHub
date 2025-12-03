using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.ModDB;

/// <summary>
/// Represents the detailed information about ModDB content.
/// This is an internal data structure used by the resolver and factory.
/// </summary>
/// <param name="Name">The name of the content.</param>
/// <param name="Description">The description.</param>
/// <param name="Author">The author's/creator's name.</param>
/// <param name="PreviewImage">Preview image URL.</param>
/// <param name="Screenshots">List of screenshot URLs.</param>
/// <param name="Videos">List of video embed URLs (YouTube, Vimeo).</param>
/// <param name="Articles">List of article URLs or titles.</param>
/// <param name="Addons">List of addon URLs or titles.</param>
/// <param name="FileSize">File size in bytes.</param>
/// <param name="DownloadCount">Number of downloads (if available).</param>
/// <param name="SubmissionDate">Submission/upload date.</param>
/// <param name="DownloadUrl">Download URL.</param>
/// <param name="DetailPageUrl">URL of the detail page.</param>
/// <param name="ModDBId">ModDB's internal content ID.</param>
/// <param name="Category">Original ModDB category string.</param>
/// <param name="TargetGame">Target game (Generals or Zero Hour).</param>
/// <param name="ContentType">Mapped content type.</param>
public record ModDBContentDetails(
    string Name,
    string Description,
    string Author,
    string PreviewImage,
    List<string>? Screenshots,
    List<string>? Videos,
    List<string>? Articles,
    List<string>? Addons,
    long FileSize,
    int DownloadCount,
    DateTime SubmissionDate,
    string DownloadUrl,
    string DetailPageUrl,
    string ModDBId,
    string Category,
    GameType TargetGame,
    ContentType ContentType);

/// <summary>
/// Represents filter parameters for ModDB content discovery.
/// </summary>
public class ModDBFilter
{
    /// <summary>Gets or sets the search keyword.</summary>
    public string? Keyword { get; set; }

    /// <summary>Gets or sets the category filter (downloads section).</summary>
    public string? Category { get; set; }

    /// <summary>Gets or sets the addon category filter (addons section).</summary>
    public string? AddonCategory { get; set; }

    /// <summary>Gets or sets the timeframe filter.</summary>
    public string? Timeframe { get; set; }

    /// <summary>Gets or sets the licence filter.</summary>
    public string? Licence { get; set; }

    /// <summary>Gets or sets the sort parameter.</summary>
    public string? Sort { get; set; }

    /// <summary>Gets or sets the page number (1-based).</summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Builds a query string from the filter parameters.
    /// </summary>
    /// <returns>Query string (e.g., "?kw=test&category=2").</returns>
    public string ToQueryString()
    {
        var parameters = new List<string>();

        if (!string.IsNullOrWhiteSpace(Keyword))
        {
            parameters.Add($"kw={Uri.EscapeDataString(Keyword)}");
        }

        if (!string.IsNullOrWhiteSpace(Category))
        {
            parameters.Add($"category={Category}");
        }

        if (!string.IsNullOrWhiteSpace(AddonCategory))
        {
            parameters.Add($"categoryaddon={AddonCategory}");
        }

        if (!string.IsNullOrWhiteSpace(Timeframe))
        {
            parameters.Add($"timeframe={Timeframe}");
        }

        if (!string.IsNullOrWhiteSpace(Licence))
        {
            parameters.Add($"licence={Licence}");
        }

        if (!string.IsNullOrWhiteSpace(Sort))
        {
            parameters.Add($"sort={Sort}");
        }

        if (Page > 1)
        {
            parameters.Add($"page={Page}");
        }

        // Add filter=t when any filter is applied
        if (parameters.Count > 0 && (Page == 1 || parameters.Count > 1))
        {
            parameters.Insert(0, "filter=t");
        }

        return parameters.Count > 0 ? "?" + string.Join("&", parameters) : string.Empty;
    }
}

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
            _ => ContentType.Addon
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
            var s when s.Contains("patch") => ContentType.Patch,
            var s when s.Contains("script") => ContentType.Patch,
            var s when s.Contains("trainer") => ContentType.Addon,
            
            var s when s.Contains("trailer") => ContentType.Video,
            var s when s.Contains("movie") => ContentType.Video,
            var s when s.Contains("video") => ContentType.Video,
            
            var s when s.Contains("multiplayer map") => ContentType.Map,
            var s when s.Contains("singleplayer map") => ContentType.Map,
            var s when s.Contains("map") => ContentType.Map,
            var s when s.Contains("prefab") => ContentType.Map,
            
            var s when s.Contains("skin") => ContentType.Skin,
            var s when s.Contains("gui") => ContentType.Skin,
            var s when s.Contains("hud") => ContentType.Skin,
            
            var s when s.Contains("language pack") => ContentType.LanguagePack,
            var s when s.Contains("language sounds") => ContentType.LanguagePack,
            
            var s when s.Contains("tool") => ContentType.ModdingTool,
            var s when s.Contains("sdk") => ContentType.ModdingTool,
            var s when s.Contains("ide") => ContentType.ModdingTool,
            var s when s.Contains("source code") => ContentType.ModdingTool,
            
            _ => ContentType.Addon
        };
    }
}

/// <summary>
/// Represents detailed information about a ModDB content item parsed from a detail page.
/// Used internally by the resolver.
/// </summary>
/// <param name="Name">Content name.</param>
/// <param name="Description">Full description.</param>
/// <param name="Author">Author/creator name.</param>
/// <param name="PreviewImage">Main preview image URL.</param>
/// <param name="Screenshots">List of screenshot URLs.</param>
/// <param name="FileSize">File size in bytes.</param>
/// <param name="DownloadCount">Number of downloads.</param>
/// <param name="SubmissionDate">Date submitted/released.</param>
/// <param name="DownloadUrl">Direct download URL.</param>
/// <param name="TargetGame">Target game type.</param>
/// <param name="ContentType">Mapped content type.</param>
public record MapDetails(
    string Name,
    string Description,
    string Author,
    string PreviewImage,
    List<string> Screenshots,
    long FileSize,
    int DownloadCount,
    DateTime SubmissionDate,
    string DownloadUrl,
    GameType TargetGame,
    ContentType ContentType);
