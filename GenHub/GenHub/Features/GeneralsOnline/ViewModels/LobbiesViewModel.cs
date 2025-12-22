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
/// ViewModel for the active players and lobbies section.
/// </summary>
public partial class LobbiesViewModel(
    IGeneralsOnlineApiClient apiClient,
    HtmlParsingService htmlParser,
    ILogger<LobbiesViewModel> logger) : ViewModelBase
{
    private List<ActivePlayer> _allActivePlayers = [];

    [ObservableProperty]
    private ObservableCollection<ActivePlayer> activePlayers = [];

    [ObservableProperty]
    private ObservableCollection<LobbyInfo> openLobbies = [];

    [ObservableProperty]
    private ObservableCollection<MatchInfo> activeMatches = [];

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private int onlinePlayersCount;

    [ObservableProperty]
    private int totalOpenLobbies;

    [ObservableProperty]
    private int totalActiveMatches;

    [ObservableProperty]
    private int totalPlayersInGame;

    [ObservableProperty]
    private int totalLifetimePlayers;

    /// <summary>
    /// Gets a value indicating whether there are players online.
    /// </summary>
    public bool HasPlayersOnline => OnlinePlayersCount > 0;

    partial void OnSearchTextChanged(string value)
    {
        FilterPlayers();
    }

    /// <summary>
    /// Loads the players and active matches data.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await LoadActivePlayersAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadActivePlayersAsync();
    }

    private async Task LoadActivePlayersAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            // Load active players from the API
            var result = await apiClient.GetActivePlayersAsync(cancellationToken);
            if (!result.Success)
            {
                logger.LogError("Failed to get active players: {Error}", result.FirstError);
                ErrorMessage = UiConstants.FailedToLoadPlayerData;
                return;
            }

            var html = result.Data;
            var (players, onlineCount, lifetimeCount) = htmlParser.ParseActivePlayers(html);

            _allActivePlayers = players;
            FilterPlayers();

            OnlinePlayersCount = onlineCount > 0 ? onlineCount : players.Count;
            TotalLifetimePlayers = lifetimeCount;
            TotalPlayersInGame = players.Count;

            // Count active matches (unique lobbies with players in game)
            var uniqueLobbies = new HashSet<string>();
            foreach (var player in players)
            {
                if (player.Status == PlayerStatus.InGame && !string.IsNullOrEmpty(player.LobbyName))
                {
                    uniqueLobbies.Add(player.LobbyName);
                }
            }

            TotalActiveMatches = uniqueLobbies.Count;

            OnPropertyChanged(nameof(HasPlayersOnline));

            logger.LogInformation(
                "Loaded {Players} active players, {Matches} active matches",
                OnlinePlayersCount,
                TotalActiveMatches);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load active players");
            ErrorMessage = UiConstants.FailedToLoadPlayerData;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void FilterPlayers()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            if (ActivePlayers.Count != _allActivePlayers.Count)
            {
                ActivePlayers = new ObservableCollection<ActivePlayer>(_allActivePlayers);
            }
            return;
        }

        var filtered = _allActivePlayers.Where(p => 
            p.PlayerName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
            p.Status.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
            (p.LobbyName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));

        ActivePlayers = new ObservableCollection<ActivePlayer>(filtered);
    }
}
