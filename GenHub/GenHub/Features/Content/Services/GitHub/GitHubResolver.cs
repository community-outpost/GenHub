using System;
using System.Collections.Generic;
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
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.GitHub;

/// <summary>
/// Resolves a discovered GitHub release into a full ContentManifest.
/// </summary>
public partial class GitHubResolver(
    IGitHubApiClient gitHubApiClient,
    IServiceProvider serviceProvider,
    ILogger<GitHubResolver> logger)
    : IContentResolver
{
    // Regex breakdown:
    // ^https://github\.com/
    //   (?<owner>[^/]+) -> owner
    //   /(?<repo>[^/]+) -> repo
    //   (?:/releases/tag/(?<tag>[^/]+))? -> optional tag
    [GeneratedRegex(ApiConstants.GitHubUrlRegexPattern, RegexOptions.Compiled)]
    private static partial Regex GitHubUrlRegex();

    /// <summary>
    /// Gets the unique identifier for the GitHub release content resolver.
    /// </summary>
    public string ResolverId => GitHubConstants.GitHubReleaseResolverId;

    /// <summary>
    /// Resolves a discovered GitHub release into a full ContentManifest.
    /// Handles both full releases and individual release assets.
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

            // Check if this is a SINGLE ASSET selection (from multi-asset split)
            if (discoveredItem.ResolverMetadata.TryGetValue("asset-name", out var assetName))
            {
                logger.LogInformation(
                    "Resolving single asset: {AssetName} from {Owner}/{Repo}:{Tag}",
                    assetName,
                    owner,
                    repo,
                    tag);

                // Get the asset data from the discovered item
                var assetData = discoveredItem.GetData<GitHubArtifact>();
                if (assetData != null)
                {
                    return await ResolveSingleAssetAsync(discoveredItem, owner, repo, tag, assetData);
                }
            }

            // Check if this is a SINGLE RELEASE ASSET selection (legacy path)
            if (discoveredItem.Data is GitHubArtifact singleAsset && singleAsset.IsRelease)
            {
                logger.LogInformation(
                    "Resolving single release asset: {AssetName} from {Owner}/{Repo}:{Tag}",
                    singleAsset.Name,
                    owner,
                    repo,
                    tag);

                return await ResolveSingleAssetAsync(discoveredItem, owner, repo, tag, singleAsset);
            }

            // Otherwise, fetch the full release and include all assets
            logger.LogInformation("Resolving full release: {Owner}/{Repo}:{Tag}", owner, repo, tag);

            var release = string.IsNullOrEmpty(tag)
                ? await gitHubApiClient.GetLatestReleaseAsync(
                    owner,
                    repo,
                    cancellationToken)
                : await gitHubApiClient.GetReleaseByTagAsync(
                    owner,
                    repo,
                    tag,
                    cancellationToken);

            if (release == null)
            {
                return OperationResult<ContentManifest>.CreateFailure($"Release not found for {owner}/{repo}");
            }

            // Generate proper ManifestId using 5-segment format per manifest-id-system.md
            // Format: schemaVersion.userVersion.publisher.contentType.contentName
            var userVersion = ExtractVersionFromReleaseTag(release.TagName);

            // Publisher segment = owner (e.g. "thesuperhackers", "cnclabs")
            // This matches the ManifestIdGenerator logic and ensures correct attribution
            var publisherId = owner;

            // Content name = repo name (unique within the publisher/owner namespace)
            var contentName = repo;

            // Determine publisher type for factory resolution
            var publisherType = DeterminePublisherType(owner);

            // Create a new manifest builder for each resolve operation to ensure clean state
            var manifestBuilder = serviceProvider.GetRequiredService<IContentManifestBuilder>();

            var manifest = manifestBuilder
                .WithBasicInfo(
                    publisherId, // Publisher = owner
                    contentName, // Content name = repo
                    userVersion) // User version extracted from tag (e.g., 20251031)
                .WithContentType(discoveredItem.ContentType, discoveredItem.TargetGame)
                .WithPublisher(
            name: !string.IsNullOrEmpty(release.Author) ? release.Author : owner,
            website: $"https://github.com/{owner}",
            publisherType: publisherType)
                .WithMetadata(
            release.Body ?? discoveredItem.Description ?? string.Empty,
            tags: GitHubInferenceHelper.InferTagsFromRelease(release),
            changelogUrl: release.HtmlUrl ?? string.Empty)
                .WithInstallationInstructions(WorkspaceConstants.DefaultWorkspaceStrategy);

            // Validate assets collection
            if (release.Assets == null || release.Assets.Count == 0)
            {
                logger.LogWarning("No assets found for release {Owner}/{Repo}:{Tag}", owner, repo, release.TagName);
                return OperationResult<ContentManifest>.CreateSuccess(manifest.Build());
            }

            // Add files from GitHub assets
            logger.LogInformation(
                "Adding {AssetCount} assets from release {Owner}/{Repo}:{Tag}",
                release.Assets.Count,
                owner,
                repo,
                release.TagName);

            foreach (var asset in release.Assets)
            {
                logger.LogDebug(
                    "Adding asset: {AssetName} ({AssetUrl})",
                    asset.Name,
                    asset.BrowserDownloadUrl);

                await manifest.AddRemoteFileAsync(
                    asset.Name,
                    asset.BrowserDownloadUrl,
                    ContentSourceType.RemoteDownload,
                    isExecutable: GitHubInferenceHelper.IsExecutableFile(asset.Name));
            }

            var builtManifest = manifest.Build();
            logger.LogInformation("GitHubResolver: Built manifest with ID: {ManifestId}", builtManifest.Id);
            return OperationResult<ContentManifest>.CreateSuccess(builtManifest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve GitHub release for {ItemName}", discoveredItem.Name);
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Determines the publisher type based on the GitHub repository owner.
    /// This allows dynamic routing to publisher-specific manifest factories.
    /// </summary>
    /// <param name="owner">The repository owner (e.g., "thesuperhackers").</param>
    /// <returns>Publisher type identifier for factory resolution.</returns>
    private static string DeterminePublisherType(string owner)
    {
        // Check for known publishers that have custom manifest factories
        if (owner.Equals("thesuperhackers", StringComparison.OrdinalIgnoreCase))
        {
            return "thesuperhackers";
        }

        // Default to generic GitHub publisher
        return "github";
    }

    /// <summary>
    /// Extracts a numeric version from a release tag.
    /// Examples: "v1.2.3" -> 123, "weekly-2025-10-31" -> 20251031, "latest" -> 0.
    /// </summary>
    private static int ExtractVersionFromReleaseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Equals("latest", StringComparison.OrdinalIgnoreCase))
            return 0;

        // Remove common prefixes
        var cleaned = tag.TrimStart('v', 'V', 'r', 'R');

        // Extract all digits and concatenate
        var digits = DigitsOnlyRegex().Replace(cleaned, string.Empty);

        if (string.IsNullOrEmpty(digits))
            return 0;

        // Take first 9 digits to avoid overflow
        if (digits.Length > 9)
            digits = digits[..9];

        return int.TryParse(digits, out var version) ? version : 0;
    }

    [GeneratedRegex(@"[^\d]")]
    private static partial Regex DigitsOnlyRegex();

    /// <summary>
    /// Extracts a variant name from an asset filename.
    /// Examples: "0_ImprovedMenusEnglish.big" -> "English", "mod_russian.big" -> "Russian".
    /// </summary>
    private static string ExtractAssetVariant(string assetName)
    {
        var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(assetName);

        // Common language patterns
        var languagePatterns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "english", "English" },
            { "russian", "Russian" },
            { "spanish", "Spanish" },
            { "french", "French" },
            { "german", "German" },
            { "chinese", "Chinese" },
            { "japanese", "Japanese" },
            { "korean", "Korean" },
        };

        // Check if filename contains a language keyword
        foreach (var (pattern, displayName) in languagePatterns)
        {
            if (nameWithoutExt.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return displayName;
        }

        // Fallback: use the filename itself (cleaned up)
        return nameWithoutExt.Replace("_", " ").Replace("-", " ").Trim();
    }

    private static (ContentType Type, bool IsInferred) InferContentType(string repo, string? releaseName)
    {
        return GitHubInferenceHelper.InferContentType(repo, releaseName);
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

        var match = GitHubUrlRegex().Match(url);
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

    /// <summary>
    /// Resolves a single release asset into a ContentManifest.
    /// </summary>
    private async Task<OperationResult<ContentManifest>> ResolveSingleAssetAsync(
        ContentSearchResult discoveredItem,
        string owner,
        string repo,
        string tag,
        GitHubArtifact asset)
    {
        try
        {
            // Extract variant from asset name (e.g., "English" from "0_ImprovedMenusEnglish.big")
            var variant = ExtractAssetVariant(asset.Name);

            // Generate manifest ID matching the discoverer format
            // Publisher = owner
            var publisherId = owner;

            // Extract version from tag (Restored)
            var userVersion = ExtractVersionFromReleaseTag(tag);

            // Content name = repo + variant
            var contentName = $"{repo}{variant}";

            // Determine publisher type for factory resolution
            var publisherType = DeterminePublisherType(owner);

            // Create a new manifest builder for each resolve operation to ensure clean state
            var manifestBuilder = serviceProvider.GetRequiredService<IContentManifestBuilder>();

            var manifest = manifestBuilder
                .WithBasicInfo(
                    owner, // Publisher = owner (matches discoverer)
                    contentName, // Content name = repo + variant (matches discoverer)
                    userVersion) // User version extracted from tag
                .WithContentType(discoveredItem.ContentType, discoveredItem.TargetGame)
                .WithPublisher(
            name: owner,
            website: $"https://github.com/{owner}",
            publisherType: publisherType)
                .WithMetadata(
            discoveredItem.Description ?? $"Release asset from {owner}/{repo}",
            tags: ["github", "release", owner, repo, variant.ToLowerInvariant()],
            changelogUrl: $"https://github.com/{owner}/{repo}/releases/tag/{tag}")
                .WithInstallationInstructions(WorkspaceConstants.DefaultWorkspaceStrategy);

            // Add only the selected asset
            await manifest.AddRemoteFileAsync(
                asset.Name,
                asset.DownloadUrl,
                ContentSourceType.RemoteDownload,
                isExecutable: GitHubInferenceHelper.IsExecutableFile(asset.Name));

            logger.LogInformation("Successfully resolved single release asset: {AssetName}", asset.Name);

            var builtManifest = manifest.Build();
            logger.LogInformation("GitHubResolver (Single Asset): Built manifest with ID: {ManifestId}", builtManifest.Id);
            return OperationResult<ContentManifest>.CreateSuccess(builtManifest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve single release asset: {AssetName}", asset.Name);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to resolve asset: {ex.Message}");
        }
    }
}
