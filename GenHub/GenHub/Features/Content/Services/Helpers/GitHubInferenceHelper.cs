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
        var searchText = $"{repo} {releaseName ?? string.Empty}".ToLowerInvariant();

        if (searchText.Contains("patch") || searchText.Contains("fix"))
            return (ContentType.Patch, true);

        if (searchText.Contains("map"))
            return (ContentType.MapPack, true);

        if (searchText.Contains("game") && (searchText.Contains("client") || searchText.Contains("code")))
            return (ContentType.GameClient, true);

        if (searchText.Contains("mod") || searchText.Contains("addon"))
            return (ContentType.Mod, true);

        // Default to GameClient for GitHub releases (most are standalone game builds)
        return (ContentType.GameClient, true);
    }

    /// <summary>
    /// Infer a likely <see cref="GameType"/> (Generals/ZeroHour) from repository and release name text.
    /// </summary>
    /// <param name="repo">Repository name or owner/repo segment used for inference.</param>
    /// <param name="releaseName">Optional release name/tag used for inference.</param>
    /// <returns>A tuple of the inferred <see cref="GameType"/> and a boolean indicating the value is inferred.</returns>
    public static (GameType type, bool isInferred) InferTargetGame(string repo, string? releaseName)
    {
        var searchText = $"{repo} {releaseName ?? string.Empty}".ToLowerInvariant();

        if (searchText.Contains("zero hour") || searchText.Contains("zh"))
            return (GameType.ZeroHour, true);

        if (searchText.Contains("generals") && !searchText.Contains("zero hour"))
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
        var text = $"{release.Name} {release.Body}".ToLowerInvariant();

        if (text.Contains("patch"))
        {
            tags.Add("Patch");
        }

        if (text.Contains("fix"))
        {
            tags.Add("Fix");
        }

        if (text.Contains("mod"))
        {
            tags.Add("Mod");
        }

        if (text.Contains("map"))
        {
            tags.Add("Map");
        }

        if (text.Contains("campaign"))
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
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext == ".exe"
            || ext == ".dll"
            || ext == ".sh"
            || ext == ".bat"
            || ext == ".so";
    }

    /// <summary>
    /// Infer the game type from an asset name or filename.
    /// Detects Generals vs Zero Hour based on executable names and filename patterns.
    /// </summary>
    /// <param name="assetName">The asset name to analyze.</param>
    /// <returns>The inferred GameType, or null if unable to determine.</returns>
    public static GameType? InferGameTypeFromAsset(string assetName)
    {
        var name = assetName.ToLowerInvariant();

        // Check for SuperHackers executables (most specific)
        if (name.Contains(GameClientConstants.SuperHackersGeneralsExecutable.ToLowerInvariant()))
            return GameType.Generals;

        if (name.Contains(GameClientConstants.SuperHackersZeroHourExecutable.ToLowerInvariant()))
            return GameType.ZeroHour;

        // Check for GeneralsOnline executables
        if (name.Contains(GameClientConstants.GeneralsOnline30HzExecutable.ToLowerInvariant()) ||
            name.Contains(GameClientConstants.GeneralsOnline60HzExecutable.ToLowerInvariant()) ||
            name.Contains(GameClientConstants.GeneralsOnlineDefaultExecutable.ToLowerInvariant()))
            return GameType.ZeroHour;

        // Check filename patterns for "zh" or "zerohour"
        if (name.Contains("zh") || name.Contains("zerohour") || name.Contains("zero-hour"))
            return GameType.ZeroHour;

        // Check for "generals" without "zh" or "zerohour"
        if (name.Contains("generals") && !name.Contains("zh") && !name.Contains("zerohour"))
            return GameType.Generals;

        // Unable to determine
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
