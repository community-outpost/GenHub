using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Features.GeneralsOnline.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GeneralsOnline.ViewModels;

/// <summary>
/// ViewModel for displaying active matches/lobbies in the Generals Online section.
/// </summary>
public partial class ActiveMatchesViewModel(
    IGeneralsOnlineApiClient apiClient,
    HtmlParsingService htmlParser,
    ILogger<ActiveMatchesViewModel> logger) : ViewModelBase, IDisposable
{
    private readonly PeriodicTimer _refreshTimer = new(TimeSpan.FromMinutes(1));
    private CancellationTokenSource? _timerCts;
    private bool _disposed;

    /// <summary>
    /// Gets the collection of active lobbies.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ActiveLobby> lobbies = [];

    /// <summary>
    /// Gets or sets the total lobby count.
    /// </summary>
    [ObservableProperty]
    private int lobbyCount;

    /// <summary>
    /// Gets or sets the count of lobbies waiting for players.
    /// </summary>
    [ObservableProperty]
    private int waitingLobbiesCount;

    /// <summary>
    /// Gets or sets the count of matches in progress.
    /// </summary>
    [ObservableProperty]
    private int inProgressCount;

    /// <summary>
    /// Gets or sets whether data is currently loading.
    /// </summary>
    [ObservableProperty]
    private bool isLoading;

    /// <summary>
    /// Gets or sets the error message if loading fails.
    /// </summary>
    [ObservableProperty]
    private string errorMessage = string.Empty;

    /// <summary>
    /// Gets or sets the last update time.
    /// </summary>
    [ObservableProperty]
    private DateTime lastUpdated;

    /// <summary>
    /// Gets or sets the selected status filter.
    /// </summary>
    [ObservableProperty]
    private string selectedStatusFilter = "All";

    /// <summary>
    /// Gets a value indicating whether there are any lobbies.
    /// </summary>
    public bool HasLobbies => LobbyCount > 0;

    /// <summary>
    /// Gets the status filter options.
    /// </summary>
    public string[] StatusFilters { get; } = ["All", "Waiting", "In Progress"];

    partial void OnSelectedStatusFilterChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// Loads the active matches data and starts auto-refresh.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await LoadActiveMatchesAsync(cancellationToken);
        StartAutoRefresh();
    }

    /// <summary>
    /// Starts the auto-refresh timer (every minute).
    /// </summary>
    public void StartAutoRefresh()
    {
        StopAutoRefresh();
        _timerCts = new CancellationTokenSource();
        _ = AutoRefreshLoopAsync(_timerCts.Token);
    }

    /// <summary>
    /// Stops the auto-refresh timer.
    /// </summary>
    public void StopAutoRefresh()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;
    }

    /// <summary>
    /// Refreshes the data manually.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadActiveMatchesAsync();
    }

    private async Task AutoRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(cancellationToken))
            {
                await LoadActiveMatchesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping the timer
        }
    }

    private async Task LoadActiveMatchesAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await apiClient.GetActiveMatchesAsync(cancellationToken);
            if (!result.Success)
            {
                logger.LogError("Failed to get active matches: {Error}", result.FirstError);
                ErrorMessage = "Failed to load active matches";
                return;
            }

            var html = result.Data;
            var (lobbies, count) = htmlParser.ParseActiveLobbies(html);

            // Update on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Lobbies.Clear();
                foreach (var lobby in lobbies)
                {
                    Lobbies.Add(lobby);
                }

                LobbyCount = count > 0 ? count : lobbies.Count;
                WaitingLobbiesCount = lobbies.Count(l => l.Status == LobbyStatus.Waiting);
                InProgressCount = lobbies.Count(l => l.Status == LobbyStatus.InProgress);
                LastUpdated = DateTime.Now;
                OnPropertyChanged(nameof(HasLobbies));
            });

            logger.LogInformation(
                "Loaded {Count} active matches ({Waiting} waiting, {InProgress} in progress)",
                LobbyCount,
                WaitingLobbiesCount,
                InProgressCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load active matches");
            ErrorMessage = "Failed to load active matches";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        // Filter is handled in the view via binding
        OnPropertyChanged(nameof(Lobbies));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources.
    /// </summary>
    /// <param name="disposing">Whether to dispose managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                StopAutoRefresh();
                _refreshTimer.Dispose();
            }

            _disposed = true;
        }
    }
}
