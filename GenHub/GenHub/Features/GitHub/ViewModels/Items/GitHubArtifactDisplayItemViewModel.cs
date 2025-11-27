using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.GitHub;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GitHub.ViewModels.Items;

/// <summary>
/// View model for a GitHub workflow artifact.
/// </summary>
public partial class GitHubArtifactDisplayItemViewModel : GitHubDisplayItemViewModel
{
    private readonly string _owner;
    private readonly string _repository;

    [ObservableProperty]
    private bool _isInstalled;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubArtifactDisplayItemViewModel"/> class.
    /// </summary>
    /// <param name="artifact">The GitHub artifact to display.</param>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repository">The repository name.</param>
    /// <param name="logger">The logger instance.</param>
    public GitHubArtifactDisplayItemViewModel(
        GitHubArtifact artifact,
        string owner,
        string repository,
        ILogger logger)
        : base(logger)
    {
        Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        IsInstalled = artifact.IsInstalled;
    }

    /// <summary>
    /// Gets the underlying GitHub artifact.
    /// </summary>
    public GitHubArtifact Artifact { get; }

    /// <summary>
    /// Gets the repository owner.
    /// </summary>
    public string Owner => _owner;

    /// <summary>
    /// Gets the repository name.
    /// </summary>
    public string Repository => _repository;

    /// <summary>
    /// Gets the display name of the artifact.
    /// </summary>
    public override string DisplayName => Artifact.Name ?? "Unknown Artifact";

    /// <summary>
    /// Gets the description of the artifact.
    /// </summary>
    public override string Description
    {
        get
        {
            var sizeText = FormatFileSize(Artifact.SizeInBytes);
            var dateText = Artifact.CreatedAt == DateTime.MinValue || Artifact.CreatedAt == default(DateTime)
                ? "Unknown date"
                : Artifact.CreatedAt.ToString("g");
            return $"{sizeText} - Created {dateText}";
        }
    }

    /// <summary>
    /// Gets a value indicating whether this artifact is expandable.
    /// </summary>
    public override bool IsExpandable => false;

    /// <summary>
    /// Gets the sort date for the artifact.
    /// </summary>
    public override DateTime SortDate => Artifact.CreatedAt;

    /// <summary>
    /// Gets a value indicating whether this is a release.
    /// </summary>
    public override bool IsRelease => false;

    /// <summary>
    /// Gets a value indicating whether this artifact can be installed.
    /// </summary>
    public override bool CanInstall => !IsInstalled;

    /// <summary>
    /// Loads child items for this artifact (artifacts don't have children).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public override Task LoadChildrenAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }
}
