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
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Features.GeneralsOnline.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GeneralsOnline.ViewModels;

/// <summary>
/// ViewModel for the match history section.
/// </summary>
public partial class MatchHistoryViewModel(
    IGeneralsOnlineApiClient apiClient,
    HtmlParsingService htmlParser,
    IExternalLinkService externalLinkService,
    ILogger<MatchHistoryViewModel> logger) : ViewModelBase
{
    private List<MatchInfo> _allMatches = [];

    [ObservableProperty]
    private ObservableCollection<MatchInfo> matches = [];

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private string searchPlayerName = string.Empty;

    [ObservableProperty]
    private MatchInfo? selectedMatch;

    partial void OnSearchPlayerNameChanged(string value)
    {
        FilterMatches();
    }

    /// <summary>
    /// Loads the match history data.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await LoadMatchHistoryAsync(cancellationToken);
    }

    /// <summary>
    /// Searches for a specific player's match history.
    /// </summary>
    [RelayCommand]
    private void SearchPlayer()
    {
        FilterMatches();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadMatchHistoryAsync();
    }

    [RelayCommand]
    private void ViewMatchDetails(MatchInfo match)
    {
        if (match == null) return;

        var url = $"https://www.playgenerals.online/viewmatch?match={match.MatchId}";
        if (!externalLinkService.OpenUrl(url))
        {
            logger.LogError("Failed to open match details link");
        }
    }

    private async Task LoadMatchHistoryAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await apiClient.GetMatchHistoryAsync(cancellationToken);
            if (!result.Success)
            {
                logger.LogError("Failed to get match history: {Error}", result.FirstError);
                ErrorMessage = UiConstants.FailedToLoadMatchHistory;
                return;
            }

            var html = result.Data;
            var parsedMatches = htmlParser.ParseMatchHistory(html);

            _allMatches = parsedMatches;
            FilterMatches();

            logger.LogInformation("Loaded {Count} recent matches", parsedMatches.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load match history");
            ErrorMessage = UiConstants.FailedToLoadMatchHistory;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void FilterMatches()
    {
        if (string.IsNullOrWhiteSpace(SearchPlayerName))
        {
            if (Matches.Count != _allMatches.Count)
            {
                Matches = new ObservableCollection<MatchInfo>(_allMatches);
            }
            return;
        }

        var search = SearchPlayerName.Trim();
        var isIdSearch = int.TryParse(search, out var searchId);

        var filtered = _allMatches.Where(m => 
            (isIdSearch && m.MatchId == searchId) ||
            m.Players.Any(p => p.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
            m.LobbyName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            m.MapName.Contains(search, StringComparison.OrdinalIgnoreCase));

        Matches = new ObservableCollection<MatchInfo>(filtered);
    }
}
