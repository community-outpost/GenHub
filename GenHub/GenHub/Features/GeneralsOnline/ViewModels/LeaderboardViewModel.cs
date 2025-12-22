using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
/// ViewModel for the leaderboard section.
/// </summary>
public partial class LeaderboardViewModel(
    IGeneralsOnlineApiClient apiClient,
    HtmlParsingService htmlParser,
    ILogger<LeaderboardViewModel> logger) : ViewModelBase
{
    private List<LeaderboardEntry> _allEntries = [];

    [ObservableProperty]
    private ObservableCollection<LeaderboardEntry> entries = [];

    [ObservableProperty]
    private LeaderboardPeriod selectedPeriod = LeaderboardPeriod.Daily;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private int totalPlayers;

    [ObservableProperty]
    private string periodDisplayName = "Daily Ladder";

    partial void OnSearchTextChanged(string value)
    {
        FilterEntries();
    }

    /// <summary>
    /// Loads the leaderboard data.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await LoadLeaderboardAsync(cancellationToken);
    }

    /// <summary>
    /// Changes the leaderboard period.
    /// </summary>
    /// <param name="period">The new period to display.</param>
    [RelayCommand]
    private async Task ChangePeriodAsync(LeaderboardPeriod period)
    {
        SelectedPeriod = period;
        PeriodDisplayName = period switch
        {
            LeaderboardPeriod.Daily => "Daily Ladder",
            LeaderboardPeriod.Monthly => "Monthly Ladder",
            LeaderboardPeriod.Annual => "Annual Ladder",
            _ => "Leaderboard",
        };
        await LoadLeaderboardAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadLeaderboardAsync();
    }

    private async Task LoadLeaderboardAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var periodString = SelectedPeriod.ToString().ToLowerInvariant();
            var result = await apiClient.GetLeaderboardJsonAsync(periodString, cancellationToken);
            if (!result.Success)
            {
                logger.LogError("Failed to get leaderboard: {Error}", result.FirstError);
                ErrorMessage = UiConstants.FailedToLoadLeaderboard;
                return;
            }

            var html = result.Data;

            // Parse the HTML response
            var (parsedEntries, parsedTotalPlayers) = htmlParser.ParseLeaderboard(html);

            _allEntries = parsedEntries;
            FilterEntries();

            TotalPlayers = parsedTotalPlayers > 0 ? parsedTotalPlayers : parsedEntries.Count;

            logger.LogInformation("Loaded {Count} leaderboard entries for period: {Period}", parsedEntries.Count, periodString);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load leaderboard");
            ErrorMessage = UiConstants.FailedToLoadLeaderboard;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void FilterEntries()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            if (Entries.Count != _allEntries.Count)
            {
                Entries = new ObservableCollection<LeaderboardEntry>(_allEntries);
            }
            return;
        }

        var filtered = _allEntries.Where(p => 
            p.PlayerName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        Entries = new ObservableCollection<LeaderboardEntry>(filtered);
    }
}
