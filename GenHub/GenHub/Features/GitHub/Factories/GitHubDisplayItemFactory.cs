using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using GenHub.Features.GitHub.ViewModels.Items;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GitHub.Factories;

/// <summary>
/// Factory for creating display items for GitHub objects.
/// </summary>
public class GitHubDisplayItemFactory(
    IGitHubApiClient gitHubApiClient,
    ILogger<GitHubDisplayItemFactory> logger)
{
    /// <summary>
    /// Creates display items from GitHub artifacts.
    /// </summary>
    /// <param name="artifacts">The artifacts to create display items from.</param>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repository">The repository name.</param>
    /// <returns>The collection of artifact display item view models.</returns>
    public IEnumerable<GitHubArtifactDisplayItemViewModel> CreateFromArtifacts(IEnumerable<GitHubArtifact> artifacts, string owner, string repository)
    {
        if (artifacts == null)
            return Enumerable.Empty<GitHubArtifactDisplayItemViewModel>();

        return artifacts.Select(artifact => new GitHubArtifactDisplayItemViewModel(
            artifact, owner, repository, logger));
    }

    /// <summary>
    /// Creates display items from GitHub releases.
    /// </summary>
    /// <param name="releases">The releases to create display items from.</param>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repository">The repository name.</param>
    /// <returns>The collection of release display item view models.</returns>
    public IEnumerable<GitHubReleaseDisplayItemViewModel> CreateFromReleases(IEnumerable<GitHubRelease> releases, string owner, string repository)
    {
        if (releases == null)
            return Enumerable.Empty<GitHubReleaseDisplayItemViewModel>();

        return releases.Select(release => new GitHubReleaseDisplayItemViewModel(
            release, owner, repository, logger));
    }

    /// <summary>
    /// Creates display items from GitHub workflow runs.
    /// </summary>
    /// <param name="workflowRuns">The workflow runs to create display items for.</param>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repository">The repository name.</param>
    /// <returns>An enumerable of workflow display item view models.</returns>
    public IEnumerable<GitHubWorkflowDisplayItemViewModel> CreateFromWorkflowRuns(
        IEnumerable<GitHubWorkflowRun> workflowRuns,
        string owner,
        string repository)
    {
        if (workflowRuns == null)
            return Enumerable.Empty<GitHubWorkflowDisplayItemViewModel>();

        return workflowRuns.Select(workflowRun => new GitHubWorkflowDisplayItemViewModel(
            workflowRun, owner, repository, gitHubApiClient, logger));
    }

    /// <summary>
    /// Creates an artifact display item from a GitHub release asset.
    /// </summary>
    /// <param name="asset">The release asset.</param>
    /// <param name="releaseTag">The release tag.</param>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repository">The repository name.</param>
    /// <returns>An artifact display item view model representing the release asset.</returns>
    public GitHubArtifactDisplayItemViewModel CreateArtifactFromReleaseAsset(
        GitHubReleaseAsset asset,
        string releaseTag,
        string owner,
        string repository)
    {
        var artifact = new GitHubArtifact
        {
            IsRelease = true,
            Id = asset.Id,
            Name = asset.Name,
            SizeInBytes = asset.Size,
            DownloadUrl = asset.BrowserDownloadUrl,
            CreatedAt = asset.CreatedAt.DateTime,
            IsInstalled = false,
            CommitSha = releaseTag,
            RepositoryInfo = new GitHubRepository
            {
                RepoOwner = owner,
                RepoName = repository,
            },
        };

        return new GitHubArtifactDisplayItemViewModel(
            artifact,
            owner,
            repository,
            logger);
    }
}
