using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using Microsoft.Extensions.Logging;
using Octokit;

namespace GenHub.Features.GitHub.Services;

/// <summary>
/// GitHub API client implementation using Octokit.
/// </summary>
public class OctokitGitHubApiClient(IGitHubClient gitHubClient, ILogger<OctokitGitHubApiClient> logger) : IGitHubApiClient
{
    private readonly IGitHubClient _gitHubClient = gitHubClient ?? throw new ArgumentNullException(nameof(gitHubClient));
    private readonly ILogger<OctokitGitHubApiClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Sets the authentication token for the GitHub client.
    /// </summary>
    /// <param name="token">The authentication token.</param>
    public void SetAuthenticationToken(string token)
    {
        if (_gitHubClient is GitHubClient concreteClient)
        {
            concreteClient.Credentials = new Credentials(token);
        }
        else
        {
            throw new InvalidOperationException("The provided IGitHubClient does not support setting credentials.");
        }
    }

    /// <summary>
    /// Gets the latest release for the specified repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repositoryName">The repository name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The latest <see cref="GitHubRelease"/> or null if not found.</returns>
    public async Task<GitHubRelease> GetLatestReleaseAsync(
        string owner,
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var octo = await _gitHubClient.Repository.Release.GetLatest(owner, repositoryName);
            return MapOctokitRelease(octo);
        }
        catch (Octokit.NotFoundException)
        {
            return null!;
        }
    }

    /// <summary>
    /// Gets a specific release by tag for the specified repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repositoryName">The repository name.</param>
    /// <param name="tag">The release tag.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <see cref="GitHubRelease"/> with the specified tag or null if not found.</returns>
    public async Task<GitHubRelease> GetReleaseByTagAsync(
        string owner,
        string repositoryName,
        string tag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var octo = await _gitHubClient.Repository.Release.Get(owner, repositoryName, tag);
            return MapOctokitRelease(octo);
        }
        catch (Octokit.NotFoundException)
        {
            _logger.LogDebug("Release with tag '{Tag}' not found for {Owner}/{Repo}", tag, owner, repositoryName);
            return null!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get release by tag '{Tag}' for {Owner}/{Repo}", tag, owner, repositoryName);
            throw;
        }
    }

    /// <summary>
    /// Gets all releases for the specified repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A collection of <see cref="GitHubRelease"/>.</returns>
    public async Task<IEnumerable<GitHubRelease>> GetReleasesAsync(
        string owner,
        string repo,
        CancellationToken cancellationToken = default)
    {
        var octos = await _gitHubClient.Repository.Release.GetAll(owner, repo);
        return octos.Select(MapOctokitRelease);
    }

    /// <summary>
    /// Maps an Octokit Release to our domain model.
    /// </summary>
    /// <param name="octo">The Octokit release.</param>
    /// <returns>The mapped release.</returns>
    private static GitHubRelease MapOctokitRelease(Release octo)
    {
        return new GitHubRelease
        {
            Id = octo.Id,
            TagName = octo.TagName,
            Name = octo.Name,
            Body = octo.Body,
            HtmlUrl = octo.HtmlUrl,
            IsPrerelease = octo.Prerelease,
            IsDraft = octo.Draft,
            CreatedAt = octo.CreatedAt,
            PublishedAt = octo.PublishedAt,
            Author = octo.Author?.Login ?? "Unknown",
            Assets = octo.Assets.Select(MapAsset).ToList(),
        };
    }

    /// <summary>
    /// Maps an Octokit ReleaseAsset to our domain model.
    /// </summary>
    /// <param name="asset">The Octokit asset.</param>
    /// <returns>The mapped asset.</returns>
    private static GitHubReleaseAsset MapAsset(ReleaseAsset asset)
    {
        return new GitHubReleaseAsset
        {
            Id = asset.Id,
            Name = asset.Name,
            Label = asset.Label,
            ContentType = asset.ContentType,
            Size = asset.Size,
            DownloadCount = asset.DownloadCount,
            BrowserDownloadUrl = asset.BrowserDownloadUrl,
            CreatedAt = asset.CreatedAt,
            UpdatedAt = asset.UpdatedAt,
        };
    }
}