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
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers content from GitHub releases in configured repositories.
/// </summary>
public class GitHubDiscoverer : IContentDiscoverer
{
    private readonly IGitHubApiClient _gitHubApiClient;
    private readonly ILogger<GitHubDiscoverer> _logger;
    private readonly IConfigurationProviderService _configurationProvider;

    private readonly List<(string owner, string repo)> _repositories;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubDiscoverer"/> class.
    /// </summary>
    /// <param name="gitHubApiClient">The GitHub API client.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configurationProvider">The configuration provider.</param>
    public GitHubDiscoverer(IGitHubApiClient gitHubApiClient, ILogger<GitHubDiscoverer> logger, IConfigurationProviderService configurationProvider)
    {
        _gitHubApiClient = gitHubApiClient;
        _logger = logger;
        _configurationProvider = configurationProvider;

        _repositories = _configurationProvider.GetGitHubDiscoveryRepositories()
            .Select(r =>
            {
                var parts = r.Split('/');
                return parts.Length == ContentConstants.GitHubRepoPartsCount ? (owner: parts[0], repo: parts[1]) : (owner: string.Empty, repo: string.Empty);
            })
            .Where(t => !string.IsNullOrEmpty(t.owner) && !string.IsNullOrEmpty(t.repo))
            .ToList();
    }

    /// <inheritdoc />
    public string SourceName => "GitHub";

    /// <inheritdoc />
    public string Description => "Discovers content from GitHub releases.";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <summary>
    /// Discovers content from configured GitHub repositories based on the search query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result containing discovered content search results.</returns>
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ContentSearchQuery query, CancellationToken cancellationToken = default)
    {
        var discoveredItems = new List<ContentSearchResult>();

        foreach (var (owner, repo) in _repositories)
        {
            try
            {
                var latestRelease = await _gitHubApiClient.GetLatestReleaseAsync(owner, repo, cancellationToken);
                if (latestRelease != null)
                {
                    var inferred = GitHubInferenceHelper.InferContentType(repo, latestRelease.Name);
                    var inferredGame = GitHubInferenceHelper.InferTargetGame(repo, latestRelease.Name);
                    var discovered = new ContentSearchResult
                    {
                        Id = $"github.{owner}.{repo}.{latestRelease.TagName}",
                        Name = latestRelease.Name ?? repo,
                        Description = "GitHub release - full details available after resolution",
                        Version = latestRelease.TagName,
                        AuthorName = latestRelease.Author,
                        ContentType = inferred.type,
                        TargetGame = inferredGame.type,
                        IsInferred = inferred.isInferred || inferredGame.isInferred,
                        ProviderName = SourceName,
                        RequiresResolution = true,
                        ResolverId = "GitHubRelease",
                        SourceUrl = latestRelease.HtmlUrl,
                        LastUpdated = latestRelease.PublishedAt?.DateTime ?? latestRelease.CreatedAt.DateTime,
                        ResolverMetadata =
                        {
                            ["owner"] = owner,
                            ["repo"] = repo,
                            ["tag"] = latestRelease.TagName,
                        },
                    };

                    if (MatchesQuery(discovered, query))
                    {
                        discoveredItems.Add(discovered);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover releases for {Owner}/{Repo}", owner, repo);
            }
        }

        return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(discoveredItems);
    }

    private bool MatchesQuery(ContentSearchResult result, ContentSearchQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.SearchTerm) &&
            !result.Name.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.ContentType.HasValue && result.ContentType != query.ContentType.Value)
        {
            return false;
        }

        if (query.TargetGame.HasValue && result.TargetGame != query.TargetGame.Value)
        {
            return false;
        }

        return true;
    }

    // Inference logic extracted to GitHubInferenceHelper
}