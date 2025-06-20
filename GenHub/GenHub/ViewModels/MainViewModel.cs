using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core;
using GenHub.Services;

namespace GenHub.ViewModels;

/// <summary>
/// ViewModel for the main window of the application.
/// </summary>
/// <param name="gameDetectionService">A <see cref="GameDetectionService"/> to manage installations.</param>
public partial class MainViewModel(GameDetectionService gameDetectionService)
    : ViewModelBase
{
    [ObservableProperty]
    private string? _vanillaGamePath;

    [ObservableProperty]
    private string? _zeroHourGamePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.<br/>
    /// Design-time only constructor.
    /// </summary>
    public MainViewModel()
        : this(new GameDetectionService(new DummyGameDetector()))
    { }

    [RelayCommand]
    private void Detect()
    {
        gameDetectionService.DetectGames();

        VanillaGamePath = gameDetectionService.VanillaGamePath;
        ZeroHourGamePath = gameDetectionService.ZerHourGamePath;
    }
}