using GenHub.Core.Models.Enums;

namespace GenHub.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="ContentType"/> enum.
/// </summary>
public static class ContentTypeExtensions
{
    /// <summary>
    /// Gets the user-friendly display name for a content type.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <returns>The display name string.</returns>
    public static string GetDisplayName(this ContentType contentType)
    {
        return contentType switch
        {
            ContentType.GameInstallation => "Game Installation",
            ContentType.GameClient => "Game Client",
            ContentType.Mod => "Modification",
            ContentType.Patch => "Patch",
            ContentType.Addon => "Add-on",
            ContentType.MapPack => "Map Pack",
            ContentType.Map => "Map",
            ContentType.Mission => "Mission",
            ContentType.LanguagePack => "Language Pack",
            ContentType.ContentBundle => "Content Bundle",
            ContentType.PublisherReferral => "Publisher Referral",
            ContentType.ContentReferral => "Content Referral",
            _ => contentType.ToString(),
        };
    }

    /// <summary>
    /// Gets a lowercase string representation of a content type for manifest IDs.
    /// This is the canonical method for converting ContentType to string in manifest IDs.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <returns>A stable lowercase string representation (e.g., "gameinstallation", "gameclient").</returns>
    public static string ToManifestIdString(this ContentType contentType)
    {
        return contentType switch
        {
            ContentType.GameInstallation => "gameinstallation",
            ContentType.GameClient => "gameclient",
            ContentType.Mod => "mod",
            ContentType.Patch => "patch",
            ContentType.Addon => "addon",
            ContentType.MapPack => "mappack",
            ContentType.LanguagePack => "languagepack",
            ContentType.ContentBundle => "contentbundle",
            ContentType.PublisherReferral => "publisherreferral",
            ContentType.ContentReferral => "contentreferral",
            ContentType.Mission => "mission",
            ContentType.Map => "map",
            ContentType.UnknownContentType => "unknown",
            _ => "unknown",
        };
    }
}