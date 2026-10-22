using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Represents a single dependency item in the preview.
/// </summary>
public partial class DependencyItemViewModel : ObservableObject
{
    /// <summary>
    /// Gets or sets the dependency name.
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// Gets or sets the dependency ID.
    /// </summary>
    [ObservableProperty]
    private string _id = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the dependency is resolved.
    /// </summary>
    [ObservableProperty]
    private bool _isResolved;

    /// <summary>
    /// Gets or sets the dependency description.
    /// </summary>
    [ObservableProperty]
    private string _description = string.Empty;
}
