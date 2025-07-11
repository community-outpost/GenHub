using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.ViewModels;

/// <summary>
/// Main view model for the application.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IGameInstallationDetectionOrchestrator _gameInstallationDetectionOrchestrator;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private List<GameInstallation> _installations = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="gameInstallationDetectionOrchestrator">The game installation detection orchestrator.</param>
    /// <param name="logger">The logger instance.</param>
    public MainViewModel(
        IGameInstallationDetectionOrchestrator gameInstallationDetectionOrchestrator,
        ILogger<MainViewModel> logger)
    {
        _gameInstallationDetectionOrchestrator = gameInstallationDetectionOrchestrator;
        _logger = logger;
        GameInstallations = new ObservableCollection<string>();
    }

    /// <summary>
    /// Gets the collection of detected game installations.
    /// </summary>
    public ObservableCollection<string> GameInstallations { get; }

    /// <summary>
    /// Performs asynchronous startup work.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        // Placeholder for future init logic
        await Task.CompletedTask;
    }

    /// <summary>
    /// Scans for game installations.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ScanForGamesAsync()
    {
        _logger.LogInformation("Starting game installation scan");

        try
        {
            GameInstallations.Clear();

            var result = await _gameInstallationDetectionOrchestrator.DetectAllInstallationsAsync();

            if (result.Success)
            {
                foreach (var installation in result.Items)
                {
                    var installationString = installation?.ToString();
                    if (!string.IsNullOrEmpty(installationString))
                    {
                        GameInstallations.Add(installationString);
                    }
                }

                _logger.LogInformation("Found {Count} game installations", result.Items.Count);
            }
            else
            {
                _logger.LogWarning("Game installation scan failed: {Errors}", string.Join("; ", result.Errors));
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error occurred during game installation scan");
        }
    }
}
