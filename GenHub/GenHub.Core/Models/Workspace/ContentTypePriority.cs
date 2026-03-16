using System;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Workspace;

/// <summary>
/// Provides priority values for ContentType when resolving file conflicts in workspaces.
/// Higher priority content wins conflicts (e.g., Mod files override GameInstallation files).
/// </summary>
public static class ContentTypePriority
{
    /// <summary>
    /// Gets the priority value for a given ContentType.
    /// Higher values = higher priority in conflict resolution.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <returns>Priority value (0-100).</returns>
    public static int GetPriority(ContentType contentType)
    {
        return contentType switch
        {
            ContentType.Mod => 100,                // Highest: User mods override everything
            ContentType.Patch => 90,               // Patches override base content
            ContentType.GameClient => 50,          // Community executables override official
            ContentType.Addon => 40,               // Addons (maps, etc.)
            ContentType.MapPack => 40,             // Map collections (same tier as Addon)
            ContentType.Mission => 40,             // Story missions (same tier as Addon)
            ContentType.Map => 40,                 // Individual maps (same tier as Addon)
            ContentType.LanguagePack => 30,        // Localization packs
            ContentType.Skin => 30,                // UI/visual customizations
            ContentType.Video => 20,               // Video content
            ContentType.Replay => 20,              // Replay files
            ContentType.Screensaver => 20,         // Screensaver files
            ContentType.Executable => 20,          // Standalone executables
            ContentType.ModdingTool => 20,         // Modding/mapping tools
            ContentType.ContentBundle => 0,        // Meta: collection of other content
            ContentType.PublisherReferral => 0,    // Meta: link to publisher content
            ContentType.ContentReferral => 0,      // Meta: link to specific content
            ContentType.UnknownContentType => 0,   // Unknown: lowest priority
            ContentType.GameInstallation => 10,    // Lowest physical: Base game files
            _ => throw new ArgumentOutOfRangeException(
                     nameof(contentType),
                     contentType,
                     $"ContentType '{contentType}' is not mapped in {nameof(ContentTypePriority)}. " +
                     "Add an explicit priority entry for this type.")
        };
    }

    /// <summary>
    /// Compares two ContentTypes by priority.
    /// </summary>
    /// <param name="a">First content type.</param>
    /// <param name="b">Second content type.</param>
    /// <returns>Negative if a &lt; b, positive if a &gt; b, zero if equal.</returns>
    public static int Compare(ContentType a, ContentType b)
    {
        return GetPriority(a).CompareTo(GetPriority(b));
    }
}