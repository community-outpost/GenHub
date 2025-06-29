using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.GameInstallations;
using GenHub.Features.GameInstallations;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GenHub.ViewModels;

/// <summary>
/// ViewModel for the main window of the application.
/// </summary>
public partial class MainViewModel
    : ViewModelBase
{
    private readonly GameInstallationDetectionOrchestrator _installationOrchestrator;

    [ObservableProperty]
    private string? _vanillaGamePath;

    [ObservableProperty]
    private string? _zeroHourGamePath;

    [ObservableProperty]
    private List<GameInstallation> _installations = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="installationOrchestrator">The orchestrator for installation detection.</param>
    public MainViewModel(GameInstallationDetectionOrchestrator installationOrchestrator)
    {
        _installationOrchestrator = installationOrchestrator;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class for design-time only.
    /// </summary>
    public MainViewModel()
        : this(new GameInstallationDetectionOrchestrator([]))
    {
    }

    [RelayCommand]
    private async Task Detect()
    {
        var detected = await _installationOrchestrator.GetDetectedInstallationsAsync();
        Installations = detected;
        VanillaGamePath = detected.Find(i => i.HasGenerals)?.GeneralsPath;
        ZeroHourGamePath = detected.Find(i => i.HasZeroHour)?.ZeroHourPath;
    }
}
