using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers content from GitHub repositories by searching for specific topics.
/// This enables community-contributed content to be discovered automatically
/// when users tag their repositories with topics like "genhub" or "generalsonline".
/// </summary>
public partial class GitHubTopicsDiscoverer(
    IGitHubApiClient gitHubApiClient,
    ILogger<GitHubTopicsDiscoverer> logger,
    IMemoryCache cache) : IContentDiscoverer
{
    [System.Text.RegularExpressions.GeneratedRegex(@"[^\d]")]
    private static partial System.Text.RegularExpressions.Regex NonDigitRegex();

    /// <summary>Maximum number of tags to include in search result.</summary>
    private const int MaxTagsToInclude = 10;

    /// <summary>Rate limit delay between API calls in milliseconds.</summary>
    private static readonly TimeSpan RateLimitDelay = TimeSpan.FromMilliseconds(100);

    private readonly SemaphoreSlim _rateLimitSemaphore = new(1, 1);

    // Topics to search for, in priority order
    private readonly List<string> _discoveryTopics =
    [
        GitHubTopicsConstants.GenHubTopic,
        GitHubTopicsConstants.GeneralsOnlineTopic,
        GitHubTopicsConstants.GeneralsModTopic,
        GitHubTopicsConstants.ZeroHourModTopic,
    ];

    /// <inheritdoc />
    public string SourceName => GitHubTopicsConstants.DiscovererSourceName;

    /// <inheritdoc />
    public string Description => GitHubTopicsConstants.DiscovererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ContentSearchResult>();
        var processedRepoIds = new HashSet<long>(); // Avoid duplicates across topics

        try
        {
            logger.LogInformation("Starting GitHub Topics discovery for topics: {Topics}", string.Join(", ", _discoveryTopics));

            foreach (var topic in _discoveryTopics)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var searchResponse = await SearchRepositoriesByTopicWithCacheAsync(
                    topic,
                    perPage: GitHubTopicsConstants.DefaultPerPage,
                    page: 1,
                    cancellationToken).ConfigureAwait(false);

                foreach (var repo in searchResponse.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Skip if already processed (repo might have multiple matching topics)
                    if (!processedRepoIds.Add(repo.Id))
                    {
                        continue;
                    }

                    // Skip archived or disabled repos
                    if (repo.IsArchived || repo.IsDisabled)
                    {
                        logger.LogDebug("Skipping archived/disabled repository: {Repo}", repo.FullName);
                        continue;
                    }

                    // Skip forks (unless they have GenHub topic explicitly)
                    if (repo.IsFork && !repo.Topics.Contains(GitHubTopicsConstants.GenHubTopic, StringComparer.OrdinalIgnoreCase))
                    {
                        logger.LogDebug("Skipping fork without genhub topic: {Repo}", repo.FullName);
                        continue;
                    }

                    if (repo.Name.Equals(AppConstants.GitHubRepositoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogDebug("Skipping system repository: {Repo}", repo.FullName);
                        continue;
                    }

                    // Try to get latest release for version info
                    GitHubRelease? latestRelease = null;
                    try
                    {
                        // Apply rate limiting
                        await _rateLimitSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            latestRelease = await gitHubApiClient.GetLatestReleaseAsync(
                                repo.Owner.Login,
                                repo.Name,
                                cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            // Add delay before releasing semaphore to maintain rate limit
                            await Task.Delay(RateLimitDelay, cancellationToken).ConfigureAwait(false);
                            _rateLimitSemaphore.Release();
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "No releases found for {Repo}, will use repo info", repo.FullName);
                    }

                    // Create search results (may return multiple for multi-asset releases)
                    var contentResults = CreateSearchResults(repo, latestRelease, topic);

                    // Apply search filters and add matching results
                    foreach (var contentResult in contentResults)
                    {
                        if (MatchesQuery(contentResult, query))
                        {
                            results.Add(contentResult);
                        }
                    }
                }
            }

            logger.LogInformation("GitHub Topics discovery found {Count} repositories", results.Count);
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("GitHub Topics discovery was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GitHub Topics discovery failed");
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure($"GitHub Topics discovery failed: {ex.Message}");
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(\d{3,4}x\d{3,4})")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();

    /// <summary>
    /// Infers ContentType from repository topics.
    /// </summary>
    private static (ContentType Type, bool IsInferred) InferContentTypeFromTopics(List<string> topics)
    {
        // Check for explicit type topics
        if (topics.Contains(GitHubTopicsConstants.GameClientTopic, StringComparer.OrdinalIgnoreCase))
        {
            return (ContentType.GameClient, false);
        }

        if (topics.Contains(GitHubTopicsConstants.ModTopic, StringComparer.OrdinalIgnoreCase) ||
            topics.Contains(GitHubTopicsConstants.GeneralsModTopic, StringComparer.OrdinalIgnoreCase) ||
            topics.Contains(GitHubTopicsConstants.ZeroHourModTopic, StringComparer.OrdinalIgnoreCase))
        {
            return (ContentType.Mod, false);
        }

        if (topics.Contains(GitHubTopicsConstants.MapPackTopic, StringComparer.OrdinalIgnoreCase))
        {
            return (ContentType.MapPack, false);
        }

        if (topics.Contains(GitHubTopicsConstants.AddonTopic, StringComparer.OrdinalIgnoreCase))
        {
            return (ContentType.Addon, false);
        }

        if (topics.Contains(GitHubTopicsConstants.PatchTopic, StringComparer.OrdinalIgnoreCase))
        {
            return (ContentType.Patch, false);
        }

        if (topics.Contains(GitHubTopicsConstants.LanguagePackTopic, StringComparer.OrdinalIgnoreCase))
        {
            return (ContentType.LanguagePack, false);
        }

        if (topics.Contains(GitHubTopicsConstants.MissionTopic, StringComparer.OrdinalIgnoreCase))
        {
            return (ContentType.Mission, false);
        }

        if (topics.Contains(GitHubTopicsConstants.MapTopic, StringComparer.OrdinalIgnoreCase))
        {
            return (ContentType.Map, false);
        }

        // No explicit type found, will need inference
        return (ContentType.Addon, true);
    }

    /// <summary>
    /// Infers GameType from repository topics.
    /// </summary>
    private static (GameType Type, bool IsInferred) InferGameTypeFromTopics(List<string> topics)
    {
        // Check for game-specific topics
        if (topics.Contains(GitHubTopicsConstants.ZeroHourModTopic, StringComparer.OrdinalIgnoreCase))
        {
            return (GameType.ZeroHour, false);
        }

        if (topics.Contains(GitHubTopicsConstants.GeneralsModTopic, StringComparer.OrdinalIgnoreCase))
        {
            // Check if also has ZH topic - use exact matching instead of substring matching
            if (topics.Any(t => t.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
                               t.Equals("zerohour", StringComparison.OrdinalIgnoreCase) ||
                               t.Equals("zero-hour", StringComparison.OrdinalIgnoreCase)))
            {
                return (GameType.ZeroHour, false);
            }

            return (GameType.Generals, false);
        }

        // Generals Online content is typically for Zero Hour
        if (topics.Contains(GitHubTopicsConstants.GeneralsOnlineTopic, StringComparer.OrdinalIgnoreCase))
        {
            return (GameType.ZeroHour, false);
        }

        // Default to ZeroHour (most common) with inference flag
        return (GameType.ZeroHour, true);
    }

    /// <summary>
    /// Checks if a search result matches the query criteria.
    /// </summary>
    private static bool MatchesQuery(ContentSearchResult result, ContentSearchQuery query)
    {
        // Filter by search term
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var searchTerm = query.SearchTerm;
            var matchesName = result.Name?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true;
            var matchesDescription = result.Description?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true;
            var matchesAuthor = result.AuthorName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true;
            var matchesTags = result.Tags.Any(t => t.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));

            if (!matchesName && !matchesDescription && !matchesAuthor && !matchesTags)
            {
                return false;
            }
        }

        // Filter by content type
        if (query.ContentType.HasValue && result.ContentType != query.ContentType.Value)
        {
            return false;
        }

        // Filter by game type
        if (query.TargetGame.HasValue && result.TargetGame != query.TargetGame.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Searches for repositories by topic with caching to reduce API calls.
    /// </summary>
    private static bool ShouldSplitAssets(GitHubRelease release)
    {
        if (release.Assets == null || release.Assets.Count <= 1)
            return false;

        // Count standalone files (non-archive extensions)
        string[] standaloneExtensions = [".big", ".csf", ".ini", ".w3d", ".dds", ".tga", ".zip"];
        var standaloneCount = release.Assets.Count(a =>
            standaloneExtensions.Any(ext => a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

        // If we have multiple standalone files, split them
        return standaloneCount > 1;
    }

    /// <summary>
    /// Extracts a numeric version from a release tag string.
    /// Examples: "v1.2.3" -> 123, "1.0" -> 10, "v2" -> 2, "latest" -> 0.
    /// </summary>
    private static int ExtractVersionFromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Equals("latest", StringComparison.OrdinalIgnoreCase))
            return 0;

        // Extract all digits and concatenate
        var digits = NonDigitRegex().Replace(tag, string.Empty);

        if (string.IsNullOrEmpty(digits))
            return 0;

        // Take first 9 digits to avoid overflow
        if (digits.Length > 9)
            digits = digits[..9];

        return int.TryParse(digits, out var version) ? version : 0;
    }

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

        // Check if filename contains a resolution (e.g., 1920x1080)
        var resolutionMatch = MyRegex().Match(nameWithoutExt);
        if (resolutionMatch.Success)
        {
            return resolutionMatch.Value;
        }

        // Check if filename contains a language keyword
        foreach (var (pattern, displayName) in languagePatterns)
        {
            if (nameWithoutExt.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return displayName;
        }

        // Fallback: use the filename itself (cleaned up)
        return nameWithoutExt.Replace("_", " ").Replace("-", " ").Trim();
    }

    /// <summary>
    /// Searches for repositories by topic with caching to reduce API calls.
    /// </summary>
    private async Task<GitHubRepositorySearchResponse> SearchRepositoriesByTopicWithCacheAsync(
        string topic,
        int perPage,
        int page,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"github_topic_{topic}_{perPage}_{page}";
        var result = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(GitHubTopicsConstants.CacheDurationMinutes);
            return await gitHubApiClient.SearchRepositoriesByTopicAsync(topic, perPage, page, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        return result ?? new GitHubRepositorySearchResponse();
    }

    /// <summary>
    /// Creates ContentSearchResults from a repository and optional release.
    /// Detects multi-asset releases and creates separate results for each standalone asset.
    /// </summary>
    private List<ContentSearchResult> CreateSearchResults(
        GitHubRepositorySearchItem repo,
        GitHubRelease? latestRelease,
        string sourceTopic)
    {
        var results = new List<ContentSearchResult>();

        // Check if this is a multi-asset release with standalone files
        if (latestRelease != null && ShouldSplitAssets(latestRelease))
        {
            logger.LogInformation(
                "Detected multi-asset release for {Repo}: {AssetCount} standalone assets",
                repo.FullName,
                latestRelease.Assets.Count);

            // Create separate result for each asset
            foreach (var asset in latestRelease.Assets)
            {
                var assetResult = CreateSearchResultForAsset(repo, latestRelease, asset, sourceTopic);
                results.Add(assetResult);
            }
        }
        else
        {
            // Single result for the entire release
            var result = CreateSearchResult(repo, latestRelease, sourceTopic);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Creates a ContentSearchResult for a single release asset.
    /// </summary>
    private ContentSearchResult CreateSearchResultForAsset(
        GitHubRepositorySearchItem repo,
        GitHubRelease release,
        GitHubReleaseAsset asset,
        string sourceTopic)
    {
        // Infer content type from topics first, then fall back to name-based inference
        var (contentType, isTypeInferred) = InferContentTypeFromTopics(repo.Topics);
        if (isTypeInferred)
        {
            var nameInference = GitHubInferenceHelper.InferContentType(repo.Name, release.Name);
            contentType = nameInference.Type;
        }

        // Infer game type
        var (gameType, isGameInferred) = InferGameTypeFromTopics(repo.Topics);
        if (isGameInferred)
        {
            var nameInference = GitHubInferenceHelper.InferTargetGame(repo.Name, release.Name);
            gameType = nameInference.Type;
        }

        // Extract asset variant name (e.g., "English" from "0_ImprovedMenusEnglish.big")
        var assetVariant = ExtractAssetVariant(asset.Name);

        // Generate unique manifest ID including asset variant
        // Content name: reponame + variantname (owner is publisher, tag is version)
        // This ensures each variant gets a unique ID after normalization
        var version = release.TagName ?? "latest";
        var userVersion = ExtractVersionFromTag(version);
        var contentName = $"{repo.Name}{assetVariant}";

        var manifestId = ManifestIdGenerator.GeneratePublisherContentId(
            repo.Owner.Login,
            contentType,
            contentName,
            userVersion);

        var result = new ContentSearchResult
        {
            Id = manifestId,
            Name = $"{repo.Name} ({assetVariant})", // Show variant in name
            Description = repo.Description ?? $"Community content from {repo.Owner.Login}/{repo.Name}",
            Version = version,
            AuthorName = repo.Owner.Login,
            ContentType = contentType,
            TargetGame = gameType,
            IsInferred = isTypeInferred || isGameInferred,
            ProviderName = SourceName,
            RequiresResolution = true,
            ResolverId = GitHubConstants.GitHubReleaseResolverId,
            SourceUrl = repo.HtmlUrl,
            LastUpdated = release.PublishedAt?.DateTime ?? repo.UpdatedAt,
            DownloadSize = asset.Size,
        };

        // Add tags from topics
        foreach (var topic in repo.Topics.Take(MaxTagsToInclude))
        {
            result.Tags.Add(topic);
        }

        // Add variant tag
        result.Tags.Add(assetVariant.ToLowerInvariant());

        // Add resolver metadata
        result.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = repo.Owner.Login;
        result.ResolverMetadata[GitHubConstants.RepoMetadataKey] = repo.Name;
        result.ResolverMetadata[GitHubConstants.TagMetadataKey] = version;
        result.ResolverMetadata[GitHubTopicsConstants.SourceTopicMetadataKey] = sourceTopic;
        result.ResolverMetadata[GitHubTopicsConstants.StarCountMetadataKey] = repo.StargazersCount.ToString();
        result.ResolverMetadata[GitHubTopicsConstants.ForkCountMetadataKey] = repo.ForksCount.ToString();
        result.ResolverMetadata["asset-name"] = asset.Name; // Store asset name for resolution
        if (!string.IsNullOrEmpty(repo.Language))
        {
            result.ResolverMetadata[GitHubTopicsConstants.LanguageMetadataKey] = repo.Language;
        }

        // Store the single asset for resolution
        result.SetData(new GitHubArtifact
        {
            Name = asset.Name,
            DownloadUrl = asset.BrowserDownloadUrl,
            SizeInBytes = asset.Size,
            IsRelease = true,
        });

        return result;
    }

    /// <summary>
    /// Creates a ContentSearchResult from a repository and optional release.
    /// </summary>
    private ContentSearchResult CreateSearchResult(
        GitHubRepositorySearchItem repo,
        GitHubRelease? latestRelease,
        string sourceTopic)
    {
        // Infer content type from topics first, then fall back to name-based inference
        var (contentType, isTypeInferred) = InferContentTypeFromTopics(repo.Topics);
        if (isTypeInferred)
        {
            var nameInference = GitHubInferenceHelper.InferContentType(repo.Name, latestRelease?.Name);
            contentType = nameInference.Type;
        }

        // Infer game type
        var (gameType, isGameInferred) = InferGameTypeFromTopics(repo.Topics);
        if (isGameInferred)
        {
            var nameInference = GitHubInferenceHelper.InferTargetGame(repo.Name, latestRelease?.Name);
            gameType = nameInference.Type;
        }

        // Generate manifest ID
        var version = latestRelease?.TagName ?? "latest";
        var manifestId = ManifestIdGenerator.GenerateGitHubContentId(
            repo.Owner.Login,
            repo.Name,
            contentType,
            version);

        var result = new ContentSearchResult
        {
            Id = manifestId,
            Name = repo.Name,
            Description = repo.Description ?? $"Community content from {repo.Owner.Login}/{repo.Name}",
            Version = version,
            AuthorName = repo.Owner.Login,
            ContentType = contentType,
            TargetGame = gameType,
            IsInferred = isTypeInferred || isGameInferred,
            ProviderName = SourceName,
            RequiresResolution = true,
            ResolverId = GitHubConstants.GitHubReleaseResolverId, // Use existing GitHub resolver
            SourceUrl = repo.HtmlUrl,
            LastUpdated = latestRelease?.PublishedAt?.DateTime ?? repo.UpdatedAt,
            DownloadSize = latestRelease?.Assets.Sum(a => a.Size) ?? 0,
        };

        // Add tags from topics
        foreach (var topic in repo.Topics.Take(MaxTagsToInclude))
        {
            result.Tags.Add(topic);
        }

        // Add resolver metadata
        result.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = repo.Owner.Login;
        result.ResolverMetadata[GitHubConstants.RepoMetadataKey] = repo.Name;
        result.ResolverMetadata[GitHubConstants.TagMetadataKey] = version;
        result.ResolverMetadata[GitHubTopicsConstants.SourceTopicMetadataKey] = sourceTopic;
        result.ResolverMetadata[GitHubTopicsConstants.StarCountMetadataKey] = repo.StargazersCount.ToString();
        result.ResolverMetadata[GitHubTopicsConstants.ForkCountMetadataKey] = repo.ForksCount.ToString();
        if (!string.IsNullOrEmpty(repo.Language))
        {
            result.ResolverMetadata[GitHubTopicsConstants.LanguageMetadataKey] = repo.Language;
        }

        // Store full release data for resolution
        if (latestRelease != null)
        {
            result.SetData(latestRelease);
        }
        else
        {
            result.SetData(repo.ToRepository());
        }

        return result;
    }
}
