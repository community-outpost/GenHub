using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Logging;

/// <summary>
/// Resolves a discovered GitHub release into a full ContentManifest.
/// </summary>
public class GitHubResolver(
    IGitHubApiClient gitHubApiClient,
    IContentManifestBuilder manifestBuilder,
    ILogger<GitHubResolver> logger) : IContentResolver
{
    // Regex breakdown:
    // ^https://github\.com/
    //   (?<owner>[^/]+) -> owner
    //   /(?<repo>[^/]+) -> repo
    //   (?:/releases/tag/(?<tag>[^/]+))? -> optional tag
    private static readonly Regex GitHubUrlRegex = new(
        ApiConstants.GitHubUrlRegexPattern,
        RegexOptions.Compiled);

    private readonly IGitHubApiClient _gitHubApiClient = gitHubApiClient;
    private readonly IContentManifestBuilder _manifestBuilder = manifestBuilder;
    private readonly ILogger<GitHubResolver> _logger = logger;

    /// <summary>
    /// Gets the unique identifier for the GitHub release content resolver.
    /// </summary>
    public string ResolverId => "GitHubRelease";

    /// <summary>
    /// Resolves a discovered GitHub release into a full ContentManifest.
    /// </summary>
    /// <param name="discoveredItem">The discovered content to resolve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="OperationResult{ContentManifest}"/> containing the resolved manifest or an error.</returns>
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Extract metadata from the discovered item
            if (!discoveredItem.ResolverMetadata.TryGetValue("owner", out var owner)
                || !discoveredItem.ResolverMetadata.TryGetValue("repo", out var repo)
                || !discoveredItem.ResolverMetadata.TryGetValue("tag", out var tag))
            {
                return OperationResult<ContentManifest>.CreateFailure("Missing required metadata for GitHub resolution");
            }

            var release = string.IsNullOrEmpty(tag)
                ? await _gitHubApiClient.GetLatestReleaseAsync(
                    owner,
                    repo,
                    cancellationToken)
                : await _gitHubApiClient.GetReleaseByTagAsync(
                    owner,
                    repo,
                    tag,
                    cancellationToken);

            if (release == null)
            {
                return OperationResult<ContentManifest>.CreateFailure($"Release not found for {owner}/{repo}");
            }

            var manifest = _manifestBuilder
                .WithBasicInfo(
                    discoveredItem.Id,
                    release.Name ?? discoveredItem.Name,
                    release.TagName)
                .WithContentType(discoveredItem.ContentType, discoveredItem.TargetGame)
                .WithPublisher(release.Author)
                    .WithMetadata(
                        release.Body ?? discoveredItem.Description ?? string.Empty,
                        tags: GitHubInferenceHelper.InferTagsFromRelease(release),
                        changelogUrl: release.HtmlUrl ?? string.Empty)
                .WithInstallationInstructions(WorkspaceStrategy.HybridCopySymlink);

            // Validate assets collection
            if (release.Assets == null || !release.Assets.Any())
            {
                _logger.LogWarning("No assets found for release {Owner}/{Repo}:{Tag}", owner, repo, release.TagName);
                return OperationResult<ContentManifest>.CreateSuccess(manifest.Build());
            }

            // Add files from GitHub assets
            foreach (var asset in release.Assets)
            {
                await manifest.AddRemoteFileAsync(
                    asset.Name,
                    asset.BrowserDownloadUrl,
                    ContentSourceType.RemoteDownload,
                    isExecutable: GitHubInferenceHelper.IsExecutableFile(asset.Name));
            }

            return OperationResult<ContentManifest>.CreateSuccess(manifest.Build());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve GitHub release for {ItemName}", discoveredItem.Name);
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }

    private static GitHubUrlParseResult ParseGitHubUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return GitHubUrlParseResult.CreateFailure("URL cannot be null or empty.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return GitHubUrlParseResult.CreateFailure("Invalid URL format.");
        }

        if (!uri.Host.Equals(ApiConstants.GitHubDomain, StringComparison.OrdinalIgnoreCase))
        {
            return GitHubUrlParseResult.CreateFailure("URL must be from github.com.");
        }

        var match = GitHubUrlRegex.Match(url);
        if (!match.Success)
        {
            return GitHubUrlParseResult.CreateFailure("Invalid GitHub repository URL format. Expected: https://github.com/owner/repo or https://github.com/owner/repo/releases/tag/version");
        }

        var owner = match.Groups["owner"].Value;
        var repo = match.Groups["repo"].Value;
        var tag = match.Groups["tag"].Success ? match.Groups["tag"].Value : null;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            return GitHubUrlParseResult.CreateFailure("Owner and repository name cannot be empty.");
        }

        return GitHubUrlParseResult.CreateSuccess(owner, repo, tag);
    }

    private static string GenerateHashFallback(GitHubReleaseAsset asset, string tagName)
    {
        var fallbackData = $"{asset.Name}:{asset.Size}:{tagName}";
        return $"fallback:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(fallbackData))}";
    }

    private (ContentType type, bool isInferred) InferContentType(string repo, string? releaseName, string? description)
    {
        return GitHubInferenceHelper.InferContentType(repo, releaseName);
    }
}
