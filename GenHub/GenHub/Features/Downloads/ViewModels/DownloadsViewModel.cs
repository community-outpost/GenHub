using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using System.Windows.Input;
using GenHub.Common.ViewModels;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for the Downloads tab.
/// </summary>
public partial class DownloadsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Downloads";

    [ObservableProperty]
    private string _description = "Manage your downloads and installations";

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadsViewModel"/> class.
    /// </summary>
    public DownloadsViewModel()
    {
    }

    /// <summary>
    /// Performs asynchronous initialization for the Downloads tab.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void OpenGitHubBuilds()
    {
        // TODO: Implement navigation to GitHub builds page
    }
}
