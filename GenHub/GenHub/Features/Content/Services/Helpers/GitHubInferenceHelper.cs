using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        if (searchText.Contains("mod") || searchText.Contains("addon"))
            return (ContentType.Mod, true);

        return (ContentType.Mod, true);
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

        if (release.Prerelease)
        {
            tags.Add("Prerelease");
        }

        if (release.Draft)
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
}