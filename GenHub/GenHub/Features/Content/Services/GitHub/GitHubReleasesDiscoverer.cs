using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.GitHub;

/// <summary>
/// Discovers content from GitHub releases.
/// Optimized to minimize API calls by loading only the latest release by default.
/// </summary>
public class GitHubReleasesDiscoverer(IGitHubApiClient gitHubClient, ILogger<GitHubReleasesDiscoverer> logger, IConfigurationProviderService configurationProvider) : IContentDiscoverer
{
    /// <inheritdoc />
    public string SourceName => ContentSourceNames.GitHubDiscoverer;

    /// <inheritdoc />
    public string Description => GitHubConstants.GitHubReleasesDiscovererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery;

    /// <inheritdoc />
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query, CancellationToken cancellationToken = default)
    {
        var results = new List<ContentSearchResult>();
        var errors = new List<string>();

        // Use configuration for repositories
        var repoList = configurationProvider.GetGitHubDiscoveryRepositories();
        var relevantRepos = repoList
            .Select(r =>
            {
                var parts = r.Split('/');
                if (parts.Length != ContentConstants.GitHubRepoPartsCount)
                {
                    logger.LogWarning("Invalid repository format: {Repository}. Expected 'owner/repo'", r);
                    return (Owner: string.Empty, Repo: string.Empty);
                }

                return (Owner: parts[0].Trim(), Repo: parts[1].Trim());
            })
            .Where(t => !string.IsNullOrEmpty(t.Owner) && !string.IsNullOrEmpty(t.Repo))
            .ToList();

        // Determine whether to load all releases or just the latest
        // Page 1 with default Take = load only latest releases (1 per repo) to conserve API calls
        // LoadMore (page > 1 or explicitly requesting all) = load additional releases
        bool loadOnlyLatest = (query.Page ?? 1) == 1 && query.Take <= relevantRepos.Count;

        foreach (var (owner, repo) in relevantRepos)
        {
            try
            {
                // Get repository info for topics
                var repository = await gitHubClient.GetRepositoryAsync(owner, repo, cancellationToken);
                var topics = repository?.Topics ?? [];

                // Get all releases from GitHub
                IEnumerable<GitHubRelease> releases;

                if (loadOnlyLatest)
                {
                    // Only fetch the latest release to conserve API calls
                    logger.LogDebug("Fetching only latest release for {Owner}/{Repo}", owner, repo);
                    var latestRelease = await gitHubClient.GetLatestReleaseAsync(owner, repo, cancellationToken);
                    releases = latestRelease != null ? [latestRelease] : [];
                }
                else
                {
                    // Fetch all releases when explicitly requested (Load More)
                    logger.LogDebug("Fetching all releases for {Owner}/{Repo}", owner, repo);
                    releases = (await gitHubClient.GetReleasesAsync(owner, repo, cancellationToken)) ?? [];
                }

                foreach (var release in releases)
                {
                    if (string.IsNullOrWhiteSpace(query.SearchTerm) ||
                        release.Name?.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        // Calculate total size from release assets
                        var totalSize = release.Assets?.Sum(a => a.Size) ?? 0;

                        // Count variants (assets) for grouping display
                        var variantCount = release.Assets?.Count ?? 0;

                        // Infer content type from topics first, then fall back to name-based inference
                        var (contentType, isTypeInferred) = GitHubInferenceHelper.InferContentTypeFromTopics(topics);
                        if (!isTypeInferred)
                        {
                            var nameInference = GitHubInferenceHelper.InferContentType(repo, release.Name);
                            contentType = nameInference.Type;
                            isTypeInferred = nameInference.IsInferred;
                        }

                        // Infer game type
                        var (gameType, isGameInferred) = GitHubInferenceHelper.InferGameTypeFromTopics(topics);
                        if (!isGameInferred)
                        {
                            var nameInference = GitHubInferenceHelper.InferTargetGame(repo, release.Name);
                            gameType = nameInference.Type;
                            isGameInferred = nameInference.IsInferred;
                        }

                        results.Add(new ContentSearchResult
                        {
                            Id = $"github.{owner}.{repo}.{release.TagName}",
                            Name = release.Name ?? $"{repo} {release.TagName}",
                            Description = release.Body ?? "GitHub release - full details available after resolution",
                            Version = release.TagName.TrimStart('v', 'V'),
                            AuthorName = release.Author,
                            ContentType = contentType,
                            TargetGame = gameType,
                            IsInferred = isTypeInferred || isGameInferred,
                            ProviderName = SourceName,
                            RequiresResolution = true,
                            ResolverId = ContentSourceNames.GitHubResolverId,
                            SourceUrl = release.HtmlUrl,
                            IconUrl = "avares://GenHub/Assets/Logos/thesuperhackers-logo.png",
                            LastUpdated = release.PublishedAt?.DateTime ?? release.CreatedAt.DateTime,
                            DownloadSize = totalSize,
                            ResolverMetadata =
                            {
                                [GitHubConstants.OwnerMetadataKey] = owner,
                                [GitHubConstants.RepoMetadataKey] = repo,
                                [GitHubConstants.TagMetadataKey] = release.TagName,
                                ["VariantCount"] = variantCount.ToString(),
                            },
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to discover releases for {Owner}/{Repo}", owner, repo);
                errors.Add($"GitHub {owner}/{repo}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            logger.LogWarning("Encountered {ErrorCount} errors during discovery: {Errors}", errors.Count, string.Join("; ", errors));
        }

        // Sort by date descending (newest first)
        results = results.OrderByDescending(r => r.LastUpdated).ToList();

        // Apply pagination
        var totalItems = results.Count;
        int pageSize = query.Take > 0 ? query.Take : 24;
        int currentPage = query.Page ?? 1;
        if (currentPage < 1) currentPage = 1;
        int skip = (currentPage - 1) * pageSize;

        var paginatedResults = results.Skip(skip).Take(pageSize).ToList();

        // HasMoreItems is true if we loaded only latest releases (user can request more)
        // or if there are more items in the paginated results
        var hasMoreItems = loadOnlyLatest || (skip + paginatedResults.Count < totalItems);

        logger.LogInformation(
            "GitHubReleasesDiscoverer: Returning page {Page}, {ReturnCount} items of {TotalCount} total. HasMore: {HasMore}, LoadedOnlyLatest: {LoadedOnlyLatest}",
            query.Page,
            paginatedResults.Count,
            totalItems,
            hasMoreItems,
            loadOnlyLatest);

        return errors.Count > 0 && paginatedResults.Count == 0
            ? OperationResult<ContentDiscoveryResult>.CreateFailure(errors)
            : OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = paginatedResults,
                TotalItems = loadOnlyLatest ? -1 : totalItems, // -1 indicates unknown total when only latest loaded
                HasMoreItems = hasMoreItems,
            });
    }
}
