using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers content from GitHub releases.
/// </summary>
public class GitHubReleasesDiscoverer(IGitHubApiClient gitHubClient, ILogger<GitHubReleasesDiscoverer> logger, IConfigurationProviderService configurationProvider) : IContentDiscoverer
{
    private readonly IGitHubApiClient _gitHubClient = gitHubClient;
    private readonly ILogger<GitHubReleasesDiscoverer> _logger = logger;
    private readonly IConfigurationProviderService _configurationProvider = configurationProvider;

    /// <inheritdoc />
    public string SourceName => "GitHub Releases";

    /// <inheritdoc />
    public string Description => "Discovers content from GitHub releases";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery;

    /// <inheritdoc />
    public async Task<ContentOperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ContentSearchQuery query, CancellationToken cancellationToken = default)
    {
        var results = new List<ContentSearchResult>();
        var errors = new List<string>();

        // Use configuration for repositories
        var repoList = _configurationProvider.GetGitHubDiscoveryRepositories();
        var relevantRepos = repoList
            .Select(r =>
            {
                var parts = r.Split('/');
                if (parts.Length != 2)
                {
                    _logger.LogWarning("Invalid repository format: {Repository}. Expected 'owner/repo'", r);
                    return (owner: string.Empty, repo: string.Empty);
                }

                return (owner: parts[0].Trim(), repo: parts[1].Trim());
            })
            .Where(t => !string.IsNullOrEmpty(t.owner) && !string.IsNullOrEmpty(t.repo));
        foreach (var (owner, repo) in relevantRepos)
        {
            try
            {
                // Use GetLatestReleaseAsync instead of GetReleasesAsync since that method doesn't exist
                var release = await _gitHubClient.GetLatestReleaseAsync(owner, repo, cancellationToken);
                if (release != null)
                {
                    if (string.IsNullOrWhiteSpace(query.SearchTerm) ||
                        release.Name?.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        results.Add(new ContentSearchResult
                        {
                            Id = $"github.{owner}.{repo}.{release.TagName}",
                            Name = release.Name ?? $"{repo} {release.TagName}",
                            Description = "GitHub release - full details available after resolution",
                            Version = release.TagName,
                            AuthorName = release.Author,
                            ContentType = InferContentType(repo, release.Name),
                            TargetGame = InferTargetGame(repo, release.Name),
                            ProviderName = SourceName,
                            RequiresResolution = true,
                            ResolverId = "GitHubRelease",
                            SourceUrl = release.HtmlUrl,
                            LastUpdated = release.PublishedAt?.DateTime ?? release.CreatedAt.DateTime,
                            ResolverMetadata =
                            {
                                ["owner"] = owner,
                                ["repo"] = repo,
                                ["tag"] = release.TagName,
                            },
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover releases for {Owner}/{Repo}", owner, repo);
                errors.Add($"GitHub {owner}/{repo}: {ex.Message}");
            }
        }

        if (errors.Any())
        {
            _logger.LogWarning("Encountered {ErrorCount} errors during discovery: {Errors}", errors.Count, string.Join("; ", errors));
        }

        return errors.Any() && !results.Any()
            ? ContentOperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(string.Join(", ", errors))
            : ContentOperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
    }

    private ContentType InferContentType(string repo, string? releaseName)
    {
        var searchText = $"{repo} {releaseName ?? string.Empty}".ToLowerInvariant();

        if (searchText.Contains("patch") || searchText.Contains("fix"))
        {
            return ContentType.Patch;
        }

        if (searchText.Contains("map"))
        {
            return ContentType.MapPack;
        }

        if (searchText.Contains("mod") || searchText.Contains("addon"))
        {
            return ContentType.Mod;
        }

        return ContentType.Mod; // Default
    }

    private GameType InferTargetGame(string repo, string? releaseName)
    {
        var searchText = $"{repo} {releaseName ?? string.Empty}".ToLowerInvariant();

        if (searchText.Contains("zero hour") || searchText.Contains("zh"))
        {
            return GameType.ZeroHour;
        }

        if (searchText.Contains("generals") && !searchText.Contains("zero hour"))
        {
            return GameType.Generals;
        }

        return GameType.ZeroHour; // Default
    }
}
