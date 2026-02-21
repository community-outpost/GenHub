using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.GameReplays;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameReplays;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameReplays.ViewModels;

/// <summary>
/// Main ViewModel for GameReplays tournaments feature.
/// Manages tournament data, authentication, and user interactions.
/// </summary>
public partial class GameReplaysViewModel(
    IGameReplaysService gameReplaysService,
    ILogger<GameReplaysViewModel> logger) : ViewModelBase
{
    private readonly IGameReplaysService _gameReplaysService = gameReplaysService;
    private readonly ILogger<GameReplaysViewModel> _logger = logger;

    private GameReplaysTournaments _tournaments = new();
    private bool _isLoading;
    private string? _errorMessage;
    private TournamentStatus _selectedFilter = TournamentStatus.SignupsOpen;

    /// <summary>
    /// Gets or sets the tournaments data.
    /// </summary>
    public GameReplaysTournaments Tournaments
    {
        get => _tournaments;
        set
        {
            if (SetProperty(ref _tournaments, value))
            {
                UpdateFilteredTournaments();
                OnPropertyChanged(nameof(SignupsOpenCount));
                OnPropertyChanged(nameof(UpcomingCount));
                OnPropertyChanged(nameof(ActiveCount));
                OnPropertyChanged(nameof(FinishedCount));
                OnPropertyChanged(nameof(TotalCount));
                OnPropertyChanged(nameof(HasTournaments));
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether data is currently loading.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    /// <summary>
    /// Gets or sets the error message to display.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>
    /// Gets or sets the selected filter for tournaments.
    /// </summary>
    public TournamentStatus SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                UpdateFilteredTournaments();
                OnPropertyChanged(nameof(IsSignupsOpenSelected));
                OnPropertyChanged(nameof(IsUpcomingSelected));
                OnPropertyChanged(nameof(IsActiveSelected));
                OnPropertyChanged(nameof(IsFinishedSelected));
            }
        }
    }

    /// <summary>
    /// Gets the tournaments for the selected filter.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TournamentItemViewModel> _filteredTournaments = [];

    private void UpdateFilteredTournaments()
    {
        var items = SelectedFilter switch
        {
            TournamentStatus.SignupsOpen => _tournaments.SignupsOpen,
            TournamentStatus.Upcoming => _tournaments.Upcoming,
            TournamentStatus.Active => _tournaments.Active,
            TournamentStatus.Finished => _tournaments.Finished,
            _ => Enumerable.Empty<Tournament>(),
        };

        var viewModels = items.Select(t => new TournamentItemViewModel(t)).ToList();

        FilteredTournaments.Clear();
        foreach (var vm in viewModels)
        {
            FilteredTournaments.Add(vm);
        }

        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(HasFilteredTournaments));
    }

    /// <summary>
    /// Gets the count of tournaments for the selected filter.
    /// </summary>
    public int FilteredCount => FilteredTournaments.Count;

    /// <summary>Gets a value indicating whether the user is authenticated.</summary>
    public bool IsAuthenticated => _gameReplaysService.IsAuthenticated();

    /// <summary>
    /// Gets the current authenticated user info.
    /// </summary>
    public OperationResult<OAuthUserInfo> CurrentUser => _gameReplaysService.GetCurrentUser();

    /// <summary>
    /// Gets the count of signups open tournaments.
    /// </summary>
    public int SignupsOpenCount => _tournaments.SignupsOpen.Count();

    /// <summary>
    /// Gets the count of upcoming tournaments.
    /// </summary>
    public int UpcomingCount => _tournaments.Upcoming.Count();

    /// <summary>
    /// Gets the count of active tournaments.
    /// </summary>
    public int ActiveCount => _tournaments.Active.Count();

    /// <summary>
    /// Gets the count of finished tournaments.
    /// </summary>
    public int FinishedCount => _tournaments.Finished.Count();

    /// <summary>
    /// Gets the total count of all tournaments.
    /// </summary>
    public int TotalCount => _tournaments.All.Count();

    /// <summary>Gets a value indicating whether there are any tournaments to display.</summary>
    public bool HasTournaments => TotalCount > 0;

    /// <summary>Gets a value indicating whether there are any tournaments in the selected filter.</summary>
    public bool HasFilteredTournaments => FilteredCount > 0;

    /// <summary>Gets a value indicating whether there is an error to display.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Gets a value indicating whether the signups open filter is selected.</summary>
    public bool IsSignupsOpenSelected => SelectedFilter == TournamentStatus.SignupsOpen;

    /// <summary>Gets a value indicating whether the upcoming filter is selected.</summary>
    public bool IsUpcomingSelected => SelectedFilter == TournamentStatus.Upcoming;

    /// <summary>Gets a value indicating whether the active filter is selected.</summary>
    public bool IsActiveSelected => SelectedFilter == TournamentStatus.Active;

    /// <summary>Gets a value indicating whether the finished filter is selected.</summary>
    public bool IsFinishedSelected => SelectedFilter == TournamentStatus.Finished;

    /// <summary>
    /// Command to load tournaments from GameReplays.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task LoadTournamentsAsync()
    {
        await ExecuteAsync(async () =>
        {
            IsLoading = true;
            ErrorMessage = null;

            var result = await _gameReplaysService.GetTournamentsAsync();

            if (!result.Success)
            {
                ErrorMessage = result.FirstError ?? "Failed to load tournaments";
                IsLoading = false;
                return;
            }

            Tournaments = result.Data;
            IsLoading = false;

            _logger.LogInformation(
                "Loaded tournaments: {SignupsOpen} signups open, {Upcoming} upcoming, {Active} active, {Finished} finished",
                SignupsOpenCount,
                UpcomingCount,
                ActiveCount,
                FinishedCount);
        });
    }

    /// <summary>
    /// Command to refresh tournaments.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task RefreshTournamentsAsync()
    {
        await LoadTournamentsAsync();
    }

    /// <summary>
    /// Command to initiate OAuth login.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task LoginAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _gameReplaysService.InitiateLoginAsync();

            if (!result.Success)
            {
                ErrorMessage = result.FirstError ?? "Failed to initiate login";
                return;
            }

            // TODO: Open authorization URL in browser
            _logger.LogInformation("Initiated OAuth login flow");
        });
    }

    /// <summary>
    /// Command to log out.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task LogoutAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _gameReplaysService.LogoutAsync();

            if (!result.Success)
            {
                ErrorMessage = result.FirstError ?? "Failed to logout";
                return;
            }

            _logger.LogInformation("Logged out from GameReplays");
        });
    }

    /// <summary>
    /// Command to select a filter.
    /// </summary>
    /// <param name="filter">The tournament status filter to select.</param>
    [RelayCommand]
    public void SelectFilter(TournamentStatus filter)
    {
        SelectedFilter = filter;
        _logger.LogDebug("Selected filter: {Filter}", filter);
    }

    /// <summary>
    /// Command to clear the error message.
    /// </summary>
    [RelayCommand]
    public void ClearError()
    {
        ErrorMessage = null;
    }

    /// <summary>
    /// Command to open a tournament in browser.
    /// </summary>
    /// <param name="tournament">The tournament item to open.</param>
    [RelayCommand]
    public void OpenTournament(TournamentItemViewModel tournament)
    {
        try
        {
            if (string.IsNullOrEmpty(tournament.TournamentUrl)) return;

            // In Avalonia, we typically use TopLevel for this, but ViewModels shouldn't know about it.
            // GenHub likely has a service. For now, we'll log it.
            _logger.LogInformation("Opening tournament URL: {Url}", tournament.TournamentUrl);

            // This is a placeholder for actual browser opening logic
            // In a real app, you'd use something like ILauncherService or similar.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open tournament URL");
            ErrorMessage = "Failed to open tournament URL";
        }
    }

    /// <summary>
    /// Command to copy a tournament URL to clipboard.
    /// </summary>
    /// <param name="tournament">The tournament item whose URL to copy.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task CopyTournamentUrl(TournamentItemViewModel tournament)
    {
        try
        {
            if (string.IsNullOrEmpty(tournament.TournamentUrl)) return;

            _logger.LogInformation("Copying tournament URL to clipboard: {Url}", tournament.TournamentUrl);

            // This is a placeholder for actual clipboard logic
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy tournament URL");
            ErrorMessage = "Failed to copy tournament URL";
        }
    }

    /// <summary>
    /// Performs asynchronous initialization for the GameReplays tab.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            await LoadTournamentsAsync();
            _logger.LogInformation("GameReplaysViewModel initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize GameReplays view");
            ErrorMessage = "Failed to initialize GameReplays view";
        }
    }

    /// <summary>
    /// Called when the tab is activated/navigated to.
    /// Refreshes tournament data.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task OnTabActivatedAsync()
    {
        try
        {
            await RefreshTournamentsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh tournaments on tab activation");
        }
    }

    /// <summary>
    /// Helper method to execute async commands with error handling.
    /// </summary>
    /// <param name="action">The async action to execute.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    private async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command");
            ErrorMessage = $"An error occurred: {ex.Message}";
        }
    }
}
