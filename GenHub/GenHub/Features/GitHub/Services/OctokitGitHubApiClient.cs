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
            return new GitHubRelease
            {
                Id = octo.Id,
                TagName = octo.TagName,
                Name = octo.Name,
                Body = octo.Body,
                HtmlUrl = octo.HtmlUrl,
                Prerelease = octo.Prerelease,
                Draft = octo.Draft,
                CreatedAt = octo.CreatedAt,
                PublishedAt = octo.PublishedAt,
                Assets = octo.Assets.Select(a => new GitHubReleaseAsset
                {
                    Id = a.Id,
                    Name = a.Name,
                    Label = a.Label,
                    ContentType = a.ContentType,
                    Size = a.Size,
                    DownloadCount = a.DownloadCount,
                    BrowserDownloadUrl = a.BrowserDownloadUrl,
                    CreatedAt = a.CreatedAt,
                    UpdatedAt = a.UpdatedAt,
                }).ToList(),
            };
        }
        catch (Octokit.NotFoundException)
        {
            return null!;
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
        return octos.Select(octo => new GitHubRelease
        {
            Id = octo.Id,
            TagName = octo.TagName,
            Name = octo.Name,
            Body = octo.Body,
            HtmlUrl = octo.HtmlUrl,
            Prerelease = octo.Prerelease,
            Draft = octo.Draft,
            CreatedAt = octo.CreatedAt,
            PublishedAt = octo.PublishedAt,
            Assets = octo.Assets.Select(a => new GitHubReleaseAsset
            {
                Id = a.Id,
                Name = a.Name,
                Label = a.Label,
                ContentType = a.ContentType,
                Size = a.Size,
                DownloadCount = a.DownloadCount,
                BrowserDownloadUrl = a.BrowserDownloadUrl,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
            }).ToList(),
        });
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
