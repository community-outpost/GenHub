using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentResolvers;

/// <summary>
/// Resolves a discovered GitHub release into a full ContentManifest.
/// </summary>
public class GitHubResolver(IGitHubApiClient gitHubApiClient, IContentManifestBuilder manifestBuilder, ILogger<GitHubResolver> logger) : IContentResolver
{
    private static readonly Regex GitHubUrlRegex = new(
        @"^https://github\.com/([^/]+)/([^/]+)(?:/releases/tag/([^/]+))?",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, ContentType> ContentTypeKeywords = new()
    {
        { "patch", ContentType.Patch },
        { "fix", ContentType.Patch },
        { "update", ContentType.Patch },
        { "mod", ContentType.Mod },
        { "modification", ContentType.Mod },
        { "total conversion", ContentType.Mod },
        { "map", ContentType.MapPack },
        { "maps", ContentType.MapPack },
        { "campaign", ContentType.MapPack },
    };

    private readonly IGitHubApiClient _gitHubApiClient = gitHubApiClient;
    private readonly IContentManifestBuilder _manifestBuilder = manifestBuilder;
    private readonly ILogger<GitHubResolver> _logger = logger;
    private readonly ContentType _defaultContentType = ContentType.Mod;
    private readonly GameType _defaultTargetGame = GameType.ZeroHour;

    /// <summary>
    /// Gets the unique identifier for the GitHub release content resolver.
    /// </summary>
    public string ResolverId => "GitHubRelease";

    /// <summary>
    /// Resolves a discovered GitHub release into a full ContentManifest.
    /// </summary>
    /// <param name="discoveredItem">The discovered content to resolve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="ContentOperationResult{ContentManifest}"/> containing the resolved manifest or an error.</returns>
    public async Task<ContentOperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem, CancellationToken cancellationToken = default)
    {
        try
        {
            // Extract metadata from the discovered item
            if (!discoveredItem.ResolverMetadata.TryGetValue("owner", out var owner) ||
                !discoveredItem.ResolverMetadata.TryGetValue("repo", out var repo) ||
                !discoveredItem.ResolverMetadata.TryGetValue("tag", out var tag))
            {
                return ContentOperationResult<ContentManifest>.CreateFailure("Missing required metadata for GitHub resolution");
            }

            var release = string.IsNullOrEmpty(tag)
                ? await _gitHubApiClient.GetLatestReleaseAsync(owner, repo, cancellationToken)
                : await _gitHubApiClient.GetReleaseByTagAsync(owner, repo, tag, cancellationToken);

            if (release == null)
            {
                return ContentOperationResult<ContentManifest>.CreateFailure($"Release not found for {owner}/{repo}");
            }

            var manifest = _manifestBuilder
                .WithBasicInfo(discoveredItem.Id, release.Name ?? discoveredItem.Name, release.TagName)
                .WithContentType(discoveredItem.ContentType, discoveredItem.TargetGame)
                .WithPublisher(release.Author)
                .WithMetadata(
                    release.Body ?? discoveredItem.Description ?? string.Empty,
                    tags: InferTagsFromRelease(release),
                    changelogUrl: release.HtmlUrl ?? string.Empty)
                .WithInstallationInstructions(WorkspaceStrategy.HybridCopySymlink);

            // Validate assets collection
            if (release.Assets == null || !release.Assets.Any())
            {
                _logger.LogWarning("No assets found for release {Owner}/{Repo}:{Tag}", owner, repo, release.TagName);
                return ContentOperationResult<ContentManifest>.CreateSuccess(manifest.Build());
            }

            // Add files from GitHub assets
            foreach (var asset in release.Assets)
            {
                await manifest.AddFileAsync(
                    asset.Name,
                    ManifestFileSourceType.Download,
                    asset.BrowserDownloadUrl,
                    isExecutable: IsExecutableFile(asset.Name));
            }

            return ContentOperationResult<ContentManifest>.CreateSuccess(manifest.Build());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve GitHub release for {ItemName}", discoveredItem.Name);
            return ContentOperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }

    // Helper: Infer tags from a GitHub release (basic implementation)
    private static List<string> InferTagsFromRelease(GitHubRelease release)
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

    // Helper: Determine if a file is likely executable based on extension
    private static bool IsExecutableFile(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext == ".exe" || ext == ".dll" || ext == ".sh" || ext == ".bat" || ext == ".so";
    }

    private static (bool Success, (string Owner, string Repo, string? Tag) Value, string ErrorMessage) ParseGitHubUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return (false, default, "URL cannot be null or empty.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return (false, default, "Invalid URL format.");
        }

        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return (false, default, "URL must be from github.com.");
        }

        var match = GitHubUrlRegex.Match(url);
        if (!match.Success)
        {
            return (false, default, "Invalid GitHub repository URL format. Expected: https://github.com/owner/repo or https://github.com/owner/repo/releases/tag/version");
        }

        var owner = match.Groups[1].Value;
        var repo = match.Groups[2].Value;
        var tag = match.Groups[3].Success ? match.Groups[3].Value : null;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            return (false, default, "Owner and repository name cannot be empty.");
        }

        return (true, (owner, repo, tag), string.Empty);
    }

    private static string GenerateHashFallback(GitHubReleaseAsset asset, string tagName)
    {
        var fallbackData = $"{asset.Name}:{asset.Size}:{tagName}";
        return $"fallback:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(fallbackData))}";
    }

    private ContentType InferContentType(string repo, string? releaseName, string? description)
    {
        var searchText = $"{repo} {releaseName} {description}".ToLowerInvariant();
        var scores = new Dictionary<ContentType, int>();

        foreach (var (keyword, contentType) in ContentTypeKeywords)
        {
            var count = Regex.Matches(searchText, $@"\b{Regex.Escape(keyword)}\b").Count;
            if (count > 0)
            {
                scores[contentType] = scores.GetValueOrDefault(contentType, 0) + count;
            }
        }

        return scores.Any() ? scores.OrderByDescending(x => x.Value).First().Key : _defaultContentType;
    }

    private GameType InferTargetGame(string repo, string? releaseName, string? description)
    {
        var searchText = $"{repo} {releaseName} {description}".ToLowerInvariant();

        if (searchText.Contains("zero hour") || searchText.Contains("zh"))
        {
            return GameType.ZeroHour;
        }

        if (searchText.Contains("generals") && !searchText.Contains("zero hour"))
        {
            return GameType.Generals;
        }

        return _defaultTargetGame;
    }
}
