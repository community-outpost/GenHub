using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Features.GitHub.ViewModels.Items;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GitHub.ViewModels;

/// <summary>
/// View model for the GitHub details view.
/// </summary>
public partial class GitHubDetailsViewModel : ObservableObject
{
    private readonly ILogger<GitHubDetailsViewModel> _logger;

    [ObservableProperty]
    private GitHubDisplayItemViewModel? _selectedItem;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _statusMessage = GitHubConstants.SelectItemMessage;

    [ObservableProperty]
    private ObservableCollection<DetailItem> _details = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubDetailsViewModel"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public GitHubDetailsViewModel(ILogger<GitHubDetailsViewModel> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets a value indicating whether the install operation can be executed.
    /// </summary>
    public bool CanInstall => SelectedItem?.CanInstall == true && !IsLoading;

    /// <summary>
    /// Sets the selected item and loads its details.
    /// </summary>
    /// <param name="item">The item to select.</param>
    public void SetSelectedItem(GitHubDisplayItemViewModel? item)
    {
        SelectedItem = item;
        Details.Clear();

        if (item == null)
        {
            StatusMessage = GitHubConstants.SelectItemMessage;
            return;
        }

        // Load details for the selected item
        LoadItemDetails(item);
        StatusMessage = string.Format(GitHubConstants.ViewingDetailsFormat, item.DisplayName);
        _logger.LogDebug("Set selected item: {DisplayName}", item.DisplayName);
    }

    /// <summary>
    /// Clears the selected item and resets the details view.
    /// </summary>
    [RelayCommand]
    public void ClearSelection()
    {
        SelectedItem = null;
        Details.Clear();
        StatusMessage = GitHubConstants.SelectItemMessage;
    }

    private void LoadItemDetails(GitHubDisplayItemViewModel item)
    {
        // Add basic information
        Details.Add(new DetailItem(GitHubConstants.NameLabel, item.DisplayName));
        Details.Add(new DetailItem(GitHubConstants.DescriptionLabel, item.Description));
        Details.Add(new DetailItem(GitHubConstants.TypeLabel, GetItemTypeDisplayName(item)));

        // Format date properly, handling default DateTime values
        var dateDisplay = item.SortDate == DateTime.MinValue || item.SortDate == default(DateTime)
            ? "Not available"
            : item.SortDate.ToString("MMM dd, yyyy HH:mm");
        Details.Add(new DetailItem(GitHubConstants.DateLabel, dateDisplay));

        // Add type-specific details
        if (item.IsRelease)
        {
            Details.Add(new DetailItem(GitHubConstants.ReleaseTypeLabel, GitHubConstants.ReleaseItemType));

            // Add changelog/release notes if available
            if (item is GitHubReleaseDisplayItemViewModel releaseVm && !string.IsNullOrWhiteSpace(releaseVm.Release.Body))
            {
                Details.Add(new DetailItem("Changelog", releaseVm.Release.Body));
            }
        }
        else if (item.IsWorkflowRun)
        {
            Details.Add(new DetailItem(GitHubConstants.WorkflowTypeLabel, GitHubConstants.WorkflowRunItemType));
            if (item is GitHubWorkflowDisplayItemViewModel workflowVm)
            {
                Details.Add(new DetailItem(GitHubConstants.RunNumberLabel, workflowVm.WorkflowRun.RunNumber.ToString()));
            }
        }

        // Add install capability
        Details.Add(new DetailItem(GitHubConstants.CanInstallLabel, item.CanInstall ? GitHubConstants.CapabilityYes : GitHubConstants.CapabilityNo));
    }

    private string GetItemTypeDisplayName(GitHubDisplayItemViewModel item)
    {
        if (item.IsRelease)
        {
            return GitHubConstants.ReleaseItemType;
        }

        if (item.IsWorkflowRun)
        {
            return GitHubConstants.WorkflowRunItemType;
        }

        return GitHubConstants.UnknownItemType;
    }
}

/// <summary>
/// Simple detail item for display in the details view.
/// </summary>
public class DetailItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DetailItem"/> class.
    /// </summary>
    /// <param name="label">The label for the detail item.</param>
    /// <param name="value">The value for the detail item.</param>
    public DetailItem(string label, string value)
    {
        Label = label;
        Value = value;
    }

    /// <summary>
    /// Gets the label for the detail item.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the value for the detail item.
    /// </summary>
    public string Value { get; }
}
