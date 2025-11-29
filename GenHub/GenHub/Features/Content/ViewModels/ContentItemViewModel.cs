using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Results;

namespace GenHub.Features.Content.ViewModels;

/// <summary>
/// ViewModel for a single content item in the discovery browser.
/// </summary>
public partial class ContentItemViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ContentItemViewModel"/> class.
    /// </summary>
    /// <param name="model">The underlying content search result model.</param>
    public ContentItemViewModel(ContentSearchResult model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
    }

    /// <summary>
    /// Gets the underlying data model for the content item.
    /// </summary>
    public ContentSearchResult Model { get; }

    /// <summary>
    /// Gets the source result for installation.
    /// </summary>
    public ContentSearchResult SourceResult => Model;

    /// <summary>
    /// Gets the name of the content.
    /// </summary>
    public string Name => Model.Name ?? string.Empty;

    /// <summary>
    /// Gets the description of the content.
    /// </summary>
    public string Description => Model.Description ?? string.Empty;

    /// <summary>
    /// Gets the name of the content's author.
    /// </summary>
    public string AuthorName => Model.AuthorName ?? string.Empty;

    /// <summary>
    /// Gets the version of the content.
    /// </summary>
    public string Version => Model.Version ?? string.Empty;

    /// <summary>
    /// Gets the URL for the content's icon.
    /// </summary>
    public string IconUrl => Model.IconUrl ?? string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this content is already installed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalled;

    /// <summary>
    /// Gets a value indicating whether this content can be installed (not already installed).
    /// </summary>
    public bool CanInstall => !IsInstalled && !IsInstalling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalling;

    [ObservableProperty]
    private string _installStatus = string.Empty;

    [ObservableProperty]
    private int _installProgress;

    /// <summary>
    /// Gets or sets a value indicating whether the changelog view is expanded.
    /// </summary>
    [ObservableProperty]
    private bool _isChangelogExpanded;

    /// <summary>
    /// Gets a value indicating whether the changelog toggle button should be shown.
    /// Only show if the description is long enough to be truncated.
    /// </summary>
    public bool ShouldShowChangelogToggle => !string.IsNullOrEmpty(Description) && Description.Length > 150;

    [RelayCommand]
    private void ToggleChangelog()
    {
        IsChangelogExpanded = !IsChangelogExpanded;
    }
}
