using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;

namespace GenHub.Features.Content.Services.Helpers;

/// <summary>
/// Shared inference helpers for GitHub-related discoverers/resolvers.
/// Centralises heuristics for ContentType and GameType detection.
/// </summary>
public static class GitHubInferenceHelper
{
    /// <summary>
    /// Infer a likely <see cref="ContentType"/> from repository and release name text.
    /// </summary>
    /// <param name="repo">Repository name or owner/repo segment used for inference.</param>
    /// <param name="releaseName">Optional release name/tag used for inference.</param>
    /// <returns>A tuple of the inferred <see cref="ContentType"/> and a boolean indicating the value is inferred.</returns>
    public static (ContentType type, bool isInferred) InferContentType(string repo, string? releaseName)
    {
        var searchText = $"{repo} {releaseName ?? string.Empty}";

        if (searchText.Contains("patch", StringComparison.OrdinalIgnoreCase) || searchText.Contains("fix", StringComparison.OrdinalIgnoreCase))
            return (ContentType.Patch, true);

        if (searchText.Contains("map", StringComparison.OrdinalIgnoreCase))
            return (ContentType.MapPack, true);

        if (searchText.Contains("game", StringComparison.OrdinalIgnoreCase) && (searchText.Contains("client", StringComparison.OrdinalIgnoreCase) || searchText.Contains("code", StringComparison.OrdinalIgnoreCase)))
            return (ContentType.GameClient, true);

        if (searchText.Contains("mod", StringComparison.OrdinalIgnoreCase) || searchText.Contains("addon", StringComparison.OrdinalIgnoreCase))
            return (ContentType.Mod, true);

        // Default to Addon for GitHub releases (most community content are addons/mods, not full game clients)
        return (ContentType.Addon, true);
    }

    /// <summary>
    /// Infer a likely <see cref="GameType"/> (Generals/ZeroHour) from repository and release name text.
    /// </summary>
    /// <param name="repo">Repository name or owner/repo segment used for inference.</param>
    /// <param name="releaseName">Optional release name/tag used for inference.</param>
    /// <returns>A tuple of the inferred <see cref="GameType"/> and a boolean indicating the value is inferred.</returns>
    public static (GameType type, bool isInferred) InferTargetGame(string repo, string? releaseName)
    {
        var searchText = $"{repo} {releaseName ?? string.Empty}";

        if (searchText.Contains("zero hour", StringComparison.OrdinalIgnoreCase) || searchText.Contains("zh", StringComparison.OrdinalIgnoreCase))
            return (GameType.ZeroHour, true);

        if (searchText.Contains("generals", StringComparison.OrdinalIgnoreCase) && !searchText.Contains("zero hour", StringComparison.OrdinalIgnoreCase))
            return (GameType.Generals, true);

        return (GameType.ZeroHour, true);
    }

    /// <summary>
    /// Infer a set of tags from a <see cref="GitHubRelease"/> (basic heuristics).
    /// </summary>
    /// <param name="release">The release to inspect.</param>
    /// <returns>A list of inferred tags.</returns>
    public static List<string> InferTagsFromRelease(GitHubRelease release)
    {
        var tags = new List<string>();
        var text = $"{release.Name} {release.Body}";

        if (text.Contains("patch", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("Patch");
        }

        if (text.Contains("fix", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("Fix");
        }

        if (text.Contains("mod", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("Mod");
        }

        if (text.Contains("map", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("Map");
        }

        if (text.Contains("campaign", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("Campaign");
        }

        if (release.IsPrerelease)
        {
            tags.Add("Prerelease");
        }

        if (release.IsDraft)
        {
            tags.Add("Draft");
        }

        return tags.Distinct().ToList();
    }

    /// <summary>
    /// Determine if a filename likely represents an executable artifact by extension.
    /// </summary>
    /// <param name="fileName">The filename to inspect.</param>
    /// <returns>True when the extension matches a known executable type.</returns>
    public static bool IsExecutableFile(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".sh", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".so", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Infer the game type from an asset name or filename.
    /// Detects Generals vs Zero Hour based on executable names and filename patterns.
    /// </summary>
    /// <param name="assetName">The asset name to analyze.</param>
    /// <returns>The inferred GameType, or null if unable to determine.</returns>
    public static GameType? InferGameTypeFromAsset(string assetName)
    {
        // Check for SuperHackers executables
        if (assetName.Contains(GameClientConstants.SuperHackersGeneralsExecutable, StringComparison.OrdinalIgnoreCase))
            return GameType.Generals;

        if (assetName.Contains(GameClientConstants.SuperHackersZeroHourExecutable, StringComparison.OrdinalIgnoreCase))
            return GameType.ZeroHour;

        // Check for GeneralsOnline executables
        if (assetName.Contains(GameClientConstants.GeneralsOnline30HzExecutable, StringComparison.OrdinalIgnoreCase) ||
            assetName.Contains(GameClientConstants.GeneralsOnline60HzExecutable, StringComparison.OrdinalIgnoreCase) ||
            assetName.Contains(GameClientConstants.GeneralsOnlineDefaultExecutable, StringComparison.OrdinalIgnoreCase))
            return GameType.ZeroHour;

        // Check filename patterns
        if (assetName.Contains("zh", StringComparison.OrdinalIgnoreCase) ||
            assetName.Contains("zerohour", StringComparison.OrdinalIgnoreCase) ||
            assetName.Contains("zero-hour", StringComparison.OrdinalIgnoreCase))
            return GameType.ZeroHour;

        if (assetName.Contains("generals", StringComparison.OrdinalIgnoreCase) &&
            !assetName.Contains("zh", StringComparison.OrdinalIgnoreCase) &&
            !assetName.Contains("zerohour", StringComparison.OrdinalIgnoreCase))
            return GameType.Generals;

        return null;
    }

    /// <summary>
    /// Checks if a release contains assets for multiple game types.
    /// </summary>
    /// <param name="assets">The list of release assets.</param>
    /// <returns>True if the release contains assets for both Generals and Zero Hour.</returns>
    public static bool IsMultiGameRelease(IEnumerable<GitHubReleaseAsset> assets)
    {
        var assetNames = assets.Select(a => a.Name).ToList();
        var detectedGames = new HashSet<GameType>();

        foreach (var assetName in assetNames)
        {
            var gameType = InferGameTypeFromAsset(assetName);
            if (gameType.HasValue)
            {
                detectedGames.Add(gameType.Value);
            }
        }

        // Multi-game release if we detected both Generals and Zero Hour
        return detectedGames.Contains(GameType.Generals) && detectedGames.Contains(GameType.ZeroHour);
    }
}
