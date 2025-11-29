using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.GitHub;
using System.Collections.ObjectModel;

namespace GenHub.Features.GitHub.ViewModels;

/// <summary>
/// View model for content mode filtering.
/// </summary>
public partial class ContentModeFilterViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<string> _displayModes = new()
    {
        "All Content",
        "Releases Only",
        "Workflow Builds Only",
    };

    [ObservableProperty]
    private string _currentDisplayMode = "All Content";

    [ObservableProperty]
    private GitHubRepository? _selectedWorkflow;

    [ObservableProperty]
    private bool _showWorkflowSelector;

    [ObservableProperty]
    private ObservableCollection<string> _searchCriteriaOptions = new()
    {
        "Name",
        "Description",
        "Tag",
        "Workflow Name",
    };

    [ObservableProperty]
    private string _selectedSearchCriteria = "Name";

    [ObservableProperty]
    private bool _showSearchControls = true;

    /// <summary>
    /// Gets the available workflows.
    /// </summary>
    public ObservableCollection<GitHubRepository> AvailableWorkflows { get; } = new();

    partial void OnCurrentDisplayModeChanged(string value)
    {
        ShowWorkflowSelector = value == "Workflow Builds Only";
    }
}
