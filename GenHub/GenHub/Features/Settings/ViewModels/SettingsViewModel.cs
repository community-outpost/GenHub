using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;
using GenHub.Common.ViewModels;

namespace GenHub.Features.Settings.ViewModels;

/// <summary>
/// ViewModel for the Settings tab.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Settings";

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    public SettingsViewModel()
    {
    }

    /// <summary>
    /// Asynchronously initializes the view model.
    /// </summary>
    /// <returns>A completed <see cref="Task"/>.</returns>
    public virtual Task InitializeAsync() => Task.CompletedTask;
}
