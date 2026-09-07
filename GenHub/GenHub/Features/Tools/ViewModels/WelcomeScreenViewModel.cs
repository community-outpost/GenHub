using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for the Publisher Studio welcome screen shown to first-time users.
/// </summary>
public partial class WelcomeScreenViewModel : ObservableObject
{
    private readonly ILogger<WelcomeScreenViewModel> _logger;
    private readonly IPublisherStudioDialogService _dialogService;
    private readonly Action<WelcomeScreenResult> _closeAction;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="WelcomeScreenViewModel"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dialogService">The dialog service.</param>
    /// <param name="closeAction">Action to call when closing the welcome screen.</param>
    public WelcomeScreenViewModel(
        ILogger<WelcomeScreenViewModel> logger,
        IPublisherStudioDialogService dialogService,
        Action<WelcomeScreenResult> closeAction)
    {
        _logger = logger;
        _dialogService = dialogService;
        _closeAction = closeAction;
    }

    /// <summary>
    /// Creates a new publisher profile.
    /// </summary>
    [RelayCommand]
    private void CreateNewProfile()
    {
        _logger.LogInformation("User selected to create new publisher profile");
        _closeAction(new WelcomeScreenResult { Action = WelcomeAction.CreateNew });
    }

    /// <summary>
    /// Imports an existing publisher profile.
    /// </summary>
    [RelayCommand]
    private async Task ImportExistingProfileAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Select a publisher project file to import...";

            var filePath = await _dialogService.ShowProjectOpenPromptAsync("Import Publisher Profile");

            if (string.IsNullOrEmpty(filePath))
            {
                StatusMessage = string.Empty;
                IsLoading = false;
                return;
            }

            if (!File.Exists(filePath))
            {
                StatusMessage = "Selected file does not exist";
                _logger.LogWarning("Import failed: File not found at {Path}", filePath);
                IsLoading = false;
                return;
            }

            _logger.LogInformation("User selected to import profile from: {Path}", filePath);
            _closeAction(new WelcomeScreenResult
            {
                Action = WelcomeAction.Import,
                ImportPath = filePath,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error importing profile: {ex.Message}";
            _logger.LogError(ex, "Error importing publisher profile");
            IsLoading = false;
        }
    }

    /// <summary>
    /// Skips the welcome screen and proceeds with default setup.
    /// </summary>
    [RelayCommand]
    private void Skip()
    {
        _logger.LogInformation("User skipped welcome screen");
        _closeAction(new WelcomeScreenResult { Action = WelcomeAction.Skip });
    }
}

/// <summary>
/// Result from the welcome screen.
/// </summary>
public class WelcomeScreenResult
{
    /// <summary>
    /// Gets or sets the action selected by the user.
    /// </summary>
    public WelcomeAction Action { get; set; }

    /// <summary>
    /// Gets or sets the import path if importing an existing profile.
    /// </summary>
    public string? ImportPath { get; set; }
}

/// <summary>
/// Actions available on the welcome screen.
/// </summary>
public enum WelcomeAction
{
    /// <summary>
    /// Create a new publisher profile.
    /// </summary>
    CreateNew,

    /// <summary>
    /// Import an existing publisher profile.
    /// </summary>
    Import,

    /// <summary>
    /// Skip the welcome screen.
    /// </summary>
    Skip,
}
