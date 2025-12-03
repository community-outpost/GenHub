using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Notifications;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for the Downloads tab.
/// </summary>
public partial class DownloadsViewModel(INotificationService notificationService) : ViewModelBase
{
    private readonly INotificationService _notificationService = notificationService;
    [ObservableProperty]
    private string _title = "Downloads";

    [ObservableProperty]
    private string _description = "Manage your downloads and installations";

    /// <summary>
    /// Performs asynchronous initialization for the Downloads tab.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ShowTestNotification()
    {
        _notificationService?.ShowSuccess(
            "Download Complete",
            "The download has finished successfully.");
    }

    [RelayCommand]
    private void OpenGitHubBuilds()
    {
        // TODO: Implement navigation to GitHub builds page
    }
}