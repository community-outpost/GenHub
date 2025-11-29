using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.GitHub;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GitHub.ViewModels.Items;

/// <summary>
/// View model for a GitHub release.
/// </summary>
public partial class GitHubReleaseDisplayItemViewModel : GitHubDisplayItemViewModel
{
    private readonly string _owner;
    private readonly string _repository;

    [ObservableProperty]
    private bool _isLoadingChildren;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubReleaseDisplayItemViewModel"/> class.
    /// </summary>
    /// <param name="release">The GitHub release to display.</param>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repository">The repository name.</param>
    /// <param name="logger">The logger instance.</param>
    public GitHubReleaseDisplayItemViewModel(
        GitHubRelease release,
        string owner,
        string repository,
        ILogger logger)
        : base(logger)
    {
        Release = release ?? throw new ArgumentNullException(nameof(release));
        _owner = owner;
        _repository = repository;

        // Eagerly load assets to fix UI databinding issues
        // This ensures assets are immediately available for tree expansion
        if (Release.Assets?.Any() == true)
        {
            _ = LoadChildrenAsync();
        }
    }

    /// <summary>
    /// Gets the underlying GitHub release.
    /// </summary>
    public GitHubRelease Release { get; }

    /// <summary>
    /// Gets the repository owner.
    /// </summary>
    public string Owner => _owner;

    /// <summary>
    /// Gets the repository name.
    /// </summary>
    public string Repository => _repository;

    /// <summary>
    /// Gets the display name for the release.
    /// </summary>
    public override string DisplayName => Release.TagName ?? Release.Name ?? "Unknown Release";

    /// <summary>
    /// Gets the description for the release.
    /// </summary>
    public override string Description
    {
        get
        {
            var assetCount = Release.Assets?.Count ?? 0;
            var publishedDate = Release.PublishedAt?.ToString("MMM dd, yyyy") ?? "Unknown date";
            return $"{Release.Name} - {assetCount} assets - Published {publishedDate}";
        }
    }

    /// <summary>
    /// Gets a value indicating whether the release is expandable.
    /// </summary>
    public override bool IsExpandable => true;

    /// <summary>
    /// Gets the sort date for the release.
    /// </summary>
    public override DateTime SortDate => Release.PublishedAt?.DateTime ?? Release.CreatedAt.DateTime;

    /// <summary>
    /// Gets a value indicating whether this is a release item.
    /// </summary>
    public override bool IsRelease => true;

    /// <summary>
    /// Gets a value indicating whether the release can be installed.
    /// </summary>
    public override bool CanInstall => Release.Assets?.Any() == true;

    /// <summary>
    /// Toggles the expanded state of the release view model.
    /// </summary>
    /// <returns>The task representing the operation.</returns>
    public override async Task ToggleExpandedAsync()
    {
        if (IsExpanded)
        {
            IsExpanded = false;
            Children.Clear();
        }
        else
        {
            await LoadChildrenAsync();
            IsExpanded = true;
        }
    }

    /// <summary>
    /// Loads the child artifacts for the release asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task representing the operation.</returns>
    public override async Task LoadChildrenAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoadingChildren || Children.Any()) return;

        IsLoadingChildren = true;
        try
        {
            if (Release.Assets != null)
            {
                foreach (var asset in Release.Assets)
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
                        CommitSha = Release.TagName ?? string.Empty,
                        RepositoryInfo = new GitHubRepository
                        {
                            RepoOwner = _owner,
                            RepoName = _repository,
                        },
                    };

                    var artifactVm = new GitHubArtifactDisplayItemViewModel(
                        artifact,
                        _owner,
                        _repository,
                        Logger);

                    Children.Add(artifactVm);
                }
            }

            // Set expanded AFTER children are added so TreeNode can see them
            IsExpanded = true;

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load release assets for {ReleaseName}", Release.Name);
        }
        finally
        {
            IsLoadingChildren = false;
        }
    }
}
