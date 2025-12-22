using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Features.GeneralsOnline.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GeneralsOnline.ViewModels;

/// <summary>
/// ViewModel for the service status section.
/// </summary>
public partial class ServiceStatusViewModel(
    IGeneralsOnlineApiClient apiClient,
    HtmlParsingService htmlParser,
    IExternalLinkService externalLinkService,
    ILogger<ServiceStatusViewModel> logger) : ViewModelBase
{
    private const double MaxProgressWidth = 300.0;

    [ObservableProperty]
    private ServiceStats stats = ServiceStats.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool isServiceOnline = true;

    [ObservableProperty]
    private string lastUpdated = string.Empty;

    [ObservableProperty]
    private int onlinePlayersCount;

    /// <summary>
    /// Gets the status indicator text.
    /// </summary>
    public string StatusText => IsServiceOnline ? "🟢 Online" : "🔴 Offline";

    /// <summary>
    /// Gets the status color key for styling.
    /// </summary>
    public string StatusColorKey => IsServiceOnline ? "OnlineColor" : "OfflineColor";

    /// <summary>
    /// Gets the width for the success rate progress bar.
    /// </summary>
    public double SuccessRateWidth => Stats.SuccessRate24h / 100.0 * MaxProgressWidth;

    /// <summary>
    /// Gets the width for the 30 day success rate progress bar.
    /// </summary>
    public double SuccessRate30dWidth => Stats.SuccessRate30d / 100.0 * MaxProgressWidth;

    /// <summary>
    /// Gets a value indicating whether connection statistics are available.
    /// </summary>
    public bool HasConnectionStats => Stats.TotalConnections24h > 0;

    /// <summary>
    /// Loads the service status data.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await LoadServiceStatsAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadServiceStatsAsync();
    }

    [RelayCommand]
    private void OpenServiceStatus()
    {
        // Open external service status page
        if (!externalLinkService.OpenUrl("https://status.playgenerals.online/"))
        {
            logger.LogWarning("Failed to open service status page");
        }
    }

    private async Task LoadServiceStatsAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var serviceStatsTask = apiClient.GetServiceStatsJsonAsync(cancellationToken);
            var playersHtmlTask = apiClient.GetActivePlayersAsync(cancellationToken);

            var serviceStatsResult = await serviceStatsTask;
            var playersHtmlResult = await playersHtmlTask;

            if (!serviceStatsResult.Success)
            {
                logger.LogError("Failed to get service stats: {Error}", serviceStatsResult.FirstError);
                ErrorMessage = UiConstants.FailedToLoadServiceStatus;
                IsServiceOnline = false;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColorKey));
                return;
            }

            if (!playersHtmlResult.Success)
            {
                logger.LogError("Failed to get active players: {Error}", playersHtmlResult.FirstError);
                ErrorMessage = UiConstants.FailedToLoadServiceStatus;
                IsServiceOnline = false;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColorKey));
                return;
            }

            var serviceStatsHtml = serviceStatsResult.Data;
            var playersHtml = playersHtmlResult.Data;

            // Parse the HTML responses
            var parsedStats = htmlParser.ParseServiceStats(serviceStatsHtml);
            var (activePlayers, onlineCount, lifetimeCount) = htmlParser.ParseActivePlayers(playersHtml);
            OnlinePlayersCount = onlineCount > 0 ? onlineCount : activePlayers.Count;

            // Create a new stats object with the online player count
            Stats = new ServiceStats(
                peakConcurrentPlayers: parsedStats.PeakConcurrentPlayers,
                totalLifetimePlayers: parsedStats.TotalLifetimePlayers,
                playersOnline24h: OnlinePlayersCount,
                playersOnline30d: parsedStats.PlayersOnline30d,
                totalConnections24h: parsedStats.TotalConnections24h,
                successfulConnections24h: parsedStats.SuccessfulConnections24h,
                failedConnections24h: parsedStats.FailedConnections24h,
                totalConnections30d: parsedStats.TotalConnections30d,
                successfulConnections30d: parsedStats.SuccessfulConnections30d,
                failedConnections30d: parsedStats.FailedConnections30d,
                ipv4Connections24h: parsedStats.Ipv4Connections24h,
                ipv6Connections24h: parsedStats.Ipv6Connections24h);

            IsServiceOnline = true;
            LastUpdated = DateTime.Now.ToString("HH:mm:ss");

            // Notify property changes for computed properties
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColorKey));
            OnPropertyChanged(nameof(SuccessRateWidth));
            OnPropertyChanged(nameof(SuccessRate30dWidth));
            OnPropertyChanged(nameof(HasConnectionStats));

            logger.LogInformation(
                "Loaded service stats: Peak={Peak}, Lifetime={Lifetime}, Online={Online}",
                Stats.PeakConcurrentPlayers,
                Stats.TotalLifetimePlayers,
                OnlinePlayersCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load service stats");
            ErrorMessage = "Failed to load service status. Please try again.";
            IsServiceOnline = false;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColorKey));
        }
        finally
        {
            IsLoading = false;
        }
    }
}
