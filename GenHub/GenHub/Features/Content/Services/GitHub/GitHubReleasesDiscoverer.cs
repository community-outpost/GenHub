using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers content from GitHub releases.
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
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
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
                    return (owner: string.Empty, repo: string.Empty);
                }

                return (owner: parts[0].Trim(), repo: parts[1].Trim());
            })
            .Where(t => !string.IsNullOrEmpty(t.owner) && !string.IsNullOrEmpty(t.repo));
        foreach (var (owner, repo) in relevantRepos)
        {
            try
            {
                // Get all releases from GitHub
                var releases = await gitHubClient.GetReleasesAsync(owner, repo, cancellationToken);

                if (releases != null)
                {
                    foreach (var release in releases)
                    {
                        if (string.IsNullOrWhiteSpace(query.SearchTerm) ||
                            release.Name?.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            var inferred = GitHubInferenceHelper.InferContentType(repo, release.Name);
                            var inferredGame = GitHubInferenceHelper.InferTargetGame(repo, release.Name);
                            results.Add(new ContentSearchResult
                            {
                                Id = $"github.{owner}.{repo}.{release.TagName}",
                                Name = release.Name ?? $"{repo} {release.TagName}",
                                Description = release.Body ?? "GitHub release - full details available after resolution",
                                Version = release.TagName,
                                AuthorName = release.Author,
                                ContentType = inferred.type,
                                TargetGame = inferredGame.type,
                                IsInferred = inferred.isInferred || inferredGame.isInferred,
                                ProviderName = SourceName,
                                RequiresResolution = true,
                                ResolverId = ContentSourceNames.GitHubResolverId,
                                SourceUrl = release.HtmlUrl,
                                LastUpdated = release.PublishedAt?.DateTime ?? release.CreatedAt.DateTime,
                                ResolverMetadata =
                                {
                                    [GitHubConstants.OwnerMetadataKey] = owner,
                                    [GitHubConstants.RepoMetadataKey] = repo,
                                    [GitHubConstants.TagMetadataKey] = release.TagName,
                                },
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to discover releases for {Owner}/{Repo}", owner, repo);
                errors.Add($"GitHub {owner}/{repo}: {ex.Message}");
            }
        }

        if (errors.Any())
        {
            logger.LogWarning("Encountered {ErrorCount} errors during discovery: {Errors}", errors.Count, string.Join("; ", errors));
        }

        return errors.Any() && !results.Any()
            ? OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(errors)
            : OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
    }
}
