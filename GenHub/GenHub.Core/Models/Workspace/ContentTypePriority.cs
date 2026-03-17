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
    /// <exception cref="ArgumentException">Thrown when the content type should not be in a workspace.</exception>
    public static int GetPriority(ContentType contentType)
    {
        return contentType switch
        {
            ContentType.Mod => 100,                // Highest: User mods override everything
            ContentType.Patch => 90,               // Patches override base content
            ContentType.GameClient => 50,          // Community executables override official
            ContentType.ModdingTool => 45,         // Modding tools (between Addon and GameClient)
            ContentType.Executable => 45,          // Executables (between Addon and GameClient)
            ContentType.Addon => 40,               // Addons (maps, etc.)
            ContentType.LanguagePack => 35,        // Language packs
            ContentType.MapPack => 30,             // Map packs (between GameInstallation and Addon)
            ContentType.Map => 30,                 // Individual maps
            ContentType.Mission => 30,             // Missions
            ContentType.Skin => 20,                // Skins
            ContentType.Video => 20,               // Videos
            ContentType.Replay => 20,              // Replays
            ContentType.Screensaver => 20,         // Screensavers
            ContentType.GameInstallation => 10,    // Lowest: Base game files

            // These types should not be in workspaces
            ContentType.ContentBundle => throw new ArgumentException($"ContentType {contentType} should not be used in workspace priority resolution"),
            ContentType.PublisherReferral => throw new ArgumentException($"ContentType {contentType} should not be used in workspace priority resolution"),
            ContentType.ContentReferral => throw new ArgumentException($"ContentType {contentType} should not be used in workspace priority resolution"),
            ContentType.UnknownContentType => throw new ArgumentException($"ContentType {contentType} should not be used in workspace priority resolution"),

            _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, $"ContentType '{contentType}' is not mapped in {nameof(ContentTypePriority)}. Add an explicit priority entry for this type."),
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