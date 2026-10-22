using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.GitHub;
using System;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel representing an individual release changelog item with expandable details.
/// </summary>
/// <param name="release">The underlying GitHub release.</param>
/// <param name="isLatest">Whether this release is the latest available release.</param>
/// <param name="openUrlAction">Action to launch the release URL.</param>
public partial class ChangelogItemViewModel(
    GitHubRelease release,
    bool isLatest,
    Action<string?> openUrlAction) : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded = isLatest;

    /// <summary>
    /// Gets the underlying GitHub release.
    /// </summary>
    public GitHubRelease Release { get; } = release;

    /// <summary>
    /// Gets a value indicating whether this release is the latest available.
    /// </summary>
    public bool IsLatest { get; } = isLatest;

    /// <summary>
    /// Toggles the expanded visibility of the release body notes.
    /// </summary>
    [RelayCommand]
    public void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    /// <summary>
    /// Opens the release on GitHub in the user's default browser.
    /// </summary>
    [RelayCommand]
    public void OpenReleaseUrl()
    {
        openUrlAction(Release.HtmlUrl);
    }
}
