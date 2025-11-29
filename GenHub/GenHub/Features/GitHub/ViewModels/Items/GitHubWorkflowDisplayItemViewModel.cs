using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GitHub.ViewModels.Items;

/// <summary>
/// View model for a GitHub workflow run.
/// </summary>
public partial class GitHubWorkflowDisplayItemViewModel : GitHubDisplayItemViewModel
{
    private readonly IGitHubApiClient _gitHubApiClient;
    private readonly string _owner;
    private readonly string _repository;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoadingChildren;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubWorkflowDisplayItemViewModel"/> class.
    /// </summary>
    /// <param name="workflowRun">The GitHub workflow run to display.</param>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repository">The repository name.</param>
    /// <param name="gitHubApiClient">The GitHub API client.</param>
    /// <param name="logger">The logger instance.</param>
    public GitHubWorkflowDisplayItemViewModel(
        GitHubWorkflowRun workflowRun,
        string owner,
        string repository,
        IGitHubApiClient gitHubApiClient,
        ILogger logger)
        : base(logger)
    {
        WorkflowRun = workflowRun ?? throw new ArgumentNullException(nameof(workflowRun));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _gitHubApiClient = gitHubApiClient ?? throw new ArgumentNullException(nameof(gitHubApiClient));
    }

    /// <summary>
    /// Gets the underlying GitHub workflow run.
    /// </summary>
    public GitHubWorkflowRun WorkflowRun { get; }

    /// <summary>
    /// Gets the display name for the workflow run.
    /// </summary>
    public override string DisplayName
    {
        get
        {
            var prInfo = WorkflowRun.PullRequestNumbers.Any()
                ? $" (PR #{string.Join(", #", WorkflowRun.PullRequestNumbers)})"
                : string.Empty;

            var name = WorkflowRun.DisplayTitle.Length > 0
                ? WorkflowRun.DisplayTitle
                : WorkflowRun.Name.Length > 0
                    ? WorkflowRun.Name
                    : $"Run #{WorkflowRun.RunNumber}";

            return $"{name}{prInfo}";
        }
    }

    /// <summary>
    /// Gets the description for the workflow run.
    /// </summary>
    public override string Description
    {
        get
        {
            var parts = new List<string>();

            // Status and conclusion
            var statusIcon = WorkflowRun.Conclusion.ToLowerInvariant() switch
            {
                "success" => "✅",
                "failure" => "❌",
                "cancelled" => "⚪",
                "skipped" => "⏭️",
                _ => "⏳",
            };
            parts.Add($"{statusIcon} {WorkflowRun.Conclusion}");

            // Branch info
            if (!string.IsNullOrEmpty(WorkflowRun.HeadBranch))
            {
                parts.Add($"🌿 {WorkflowRun.HeadBranch}");
            }

            // Event type
            if (!string.IsNullOrEmpty(WorkflowRun.Event))
            {
                parts.Add($"📋 {WorkflowRun.Event}");
            }

            // Actor
            if (!string.IsNullOrEmpty(WorkflowRun.Actor))
            {
                parts.Add($"👤 {WorkflowRun.Actor}");
            }

            // Commit SHA (short)
            if (!string.IsNullOrEmpty(WorkflowRun.HeadSha))
            {
                parts.Add($"#{WorkflowRun.HeadSha.Substring(0, Math.Min(7, WorkflowRun.HeadSha.Length))}");
            }

            // Date
            parts.Add($"🕒 {WorkflowRun.CreatedAt:g}");

            return string.Join(" • ", parts);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the workflow run is expandable.
    /// </summary>
    public override bool IsExpandable => true;

    /// <summary>
    /// Gets the sort date for the workflow run.
    /// </summary>
    public override DateTime SortDate => WorkflowRun.CreatedAt.DateTime;

    /// <summary>
    /// Gets a value indicating whether this is a release item.
    /// </summary>
    public override bool IsRelease => false;

    /// <summary>
    /// Gets a value indicating whether this is a workflow run item.
    /// </summary>
    public override bool IsWorkflowRun => true;

    /// <summary>
    /// Toggles the expanded state of the workflow run view model.
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
        }
    }

    /// <summary>
    /// Loads the child artifacts for the workflow run asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task representing the operation.</returns>
    public override async Task LoadChildrenAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoadingChildren || Children.Any()) return;

        IsLoadingChildren = true;
        try
        {
            var artifacts = await _gitHubApiClient.GetArtifactsForWorkflowRunAsync(
                _owner,
                _repository,
                WorkflowRun.Id,
                cancellationToken);

            foreach (var artifact in artifacts)
            {
                // Attach the WorkflowRun to the artifact for proper manifest generation
                artifact.WorkflowRun = WorkflowRun;
                artifact.WorkflowNumber = WorkflowRun.RunNumber;
                artifact.RunId = WorkflowRun.Id;

                var artifactVm = new GitHubArtifactDisplayItemViewModel(
                    artifact,
                    _owner,
                    _repository,
                    Logger);

                Children.Add(artifactVm);
            }

            IsExpanded = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load workflow artifacts for run {RunNumber}", WorkflowRun.RunNumber);
        }
        finally
        {
            IsLoadingChildren = false;
        }
    }
}
