using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Content;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for a single missing dependency item in the list.
/// </summary>
public partial class MissingDependencyItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _versionConstraint = string.Empty;

    [ObservableProperty]
    private bool _isOptional;

    [ObservableProperty]
    private bool _canAutoInstall;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="MissingDependencyItemViewModel"/> class.
    /// </summary>
    /// <param name="dependency">The missing dependency.</param>
    public MissingDependencyItemViewModel(MissingDependency dependency)
    {
        Name = dependency.Dependency.Name ?? "Unknown Dependency";
        VersionConstraint = GetVersionConstraint(dependency);
        IsOptional = dependency.Dependency.IsOptional;
        CanAutoInstall = dependency.CanAutoInstall;
        Status = CanAutoInstall ? "Available" : "Not found in subscriptions";
    }

    private static string GetVersionConstraint(MissingDependency dependency)
    {
        if (!string.IsNullOrEmpty(dependency.Dependency.ExactVersion))
        {
            return dependency.Dependency.ExactVersion;
        }

        if (!string.IsNullOrEmpty(dependency.Dependency.MinVersion))
        {
            return $">= {dependency.Dependency.MinVersion}";
        }

        return "Any";
    }
}
