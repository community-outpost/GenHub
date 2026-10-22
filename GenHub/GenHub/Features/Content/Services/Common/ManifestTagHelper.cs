using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using Slugify;
using System;
using System.Collections.Generic;
using System.IO;

namespace GenHub.Features.Content.Services.Common;

/// <summary>
/// Shared slug, tag, and filename helpers for publisher manifest factories.
/// </summary>
public static class ManifestTagHelper
{
    /// <summary>
    /// Converts a title into a URL-friendly slug, falling back when empty or on failure.
    /// </summary>
    /// <param name="title">The content title.</param>
    /// <param name="fallback">The fallback name when slugification yields nothing.</param>
    /// <returns>A slugified version of the title.</returns>
    public static string SlugifyTitle(string? title, string fallback)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return fallback;
        }

        try
        {
            var slugHelper = new SlugHelper();
            var slug = slugHelper.GenerateSlug(title);
            return string.IsNullOrEmpty(slug) ? fallback : slug;
        }
        catch
        {
            // Fallback to default if slugification fails
            return fallback;
        }
    }

    /// <summary>
    /// Appends the game and content-type tags shared by map/content publishers.
    /// </summary>
    /// <param name="tags">The tag list to append to.</param>
    /// <param name="targetGame">The target game type.</param>
    /// <param name="contentType">The content type.</param>
    public static void AddGameAndContentTypeTags(List<string> tags, GameType targetGame, ContentType contentType)
    {
        ArgumentNullException.ThrowIfNull(tags);

        // Add game-specific tag
        tags.Add(targetGame == GameType.Generals ? GameClientConstants.GeneralsShortName : GameClientConstants.ZeroHourShortName);

        // Add content type tag
        tags.Add(contentType switch
        {
            ContentType.Mod => ManifestConstants.ModTag,
            ContentType.Patch => ManifestConstants.PatchTag,
            ContentType.Map => ManifestConstants.MapTag,
            ContentType.MapPack => ManifestConstants.MapPackTag,
            ContentType.Mission => ManifestConstants.MissionTag,
            ContentType.Skin => ManifestConstants.SkinTag,
            ContentType.Video => ManifestConstants.VideoTag,
            ContentType.Screensaver => ManifestConstants.ScreensaverTag,
            ContentType.Replay => ManifestConstants.ReplayTag,
            ContentType.ModdingTool => ManifestConstants.ModdingToolTag,
            ContentType.LanguagePack => ManifestConstants.LanguagePackTag,
            ContentType.Addon => ManifestConstants.AddonTag,
            _ => ManifestConstants.OtherTag,
        });
    }

    /// <summary>
    /// Sanitizes a filename by replacing invalid characters with underscores.
    /// </summary>
    /// <param name="fileName">The filename to sanitize.</param>
    /// <param name="fallback">The fallback when the filename is empty.</param>
    /// <returns>A sanitized filename.</returns>
    public static string SanitizeFileName(string? fileName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return fallback;
        }

        // Remove invalid path characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
