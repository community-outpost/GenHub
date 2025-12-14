using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GeneralsOnline;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GeneralsOnline.ViewModels;

/// <summary>
/// ViewModel for the Generals Online main tab.
/// </summary>
public partial class GeneralsOnlineViewModel : ViewModelBase
{
    private readonly IGeneralsOnlineApiClient _apiClient;
    private readonly IGeneralsOnlineAuthService _authService;
    private readonly LoginViewModel _loginViewModel;
    private readonly LeaderboardViewModel _leaderboardViewModel;
    private readonly MatchHistoryViewModel _matchHistoryViewModel;
    private readonly LobbiesViewModel _lobbiesViewModel;
    private readonly ServiceStatusViewModel _serviceStatusViewModel;
    private readonly IExternalLinkService _externalLinkService;
    private readonly ILogger<GeneralsOnlineViewModel> _logger;

    [ObservableProperty]
    private string username = "Loading...";

    [ObservableProperty]
    private string statusMessage = "Connecting...";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isAuthenticated;

    [ObservableProperty]
    private int selectedTabIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineViewModel"/> class.
    /// </summary>
    /// <param name="apiClient">The API client.</param>
    /// <param name="authService">The authentication service.</param>
    /// <param name="loginViewModel">The login ViewModel.</param>
    /// <param name="leaderboardViewModel">The leaderboard ViewModel.</param>
    /// <param name="matchHistoryViewModel">The match history ViewModel.</param>
    /// <param name="lobbiesViewModel">The lobbies ViewModel.</param>
    /// <param name="serviceStatusViewModel">The service status ViewModel.</param>
    /// <param name="externalLinkService">The external link service.</param>
    /// <param name="logger">The logger.</param>
    public GeneralsOnlineViewModel(
        IGeneralsOnlineApiClient apiClient,
        IGeneralsOnlineAuthService authService,
        LoginViewModel loginViewModel,
        LeaderboardViewModel leaderboardViewModel,
        MatchHistoryViewModel matchHistoryViewModel,
        LobbiesViewModel lobbiesViewModel,
        ServiceStatusViewModel serviceStatusViewModel,
        IExternalLinkService externalLinkService,
        ILogger<GeneralsOnlineViewModel> logger)
    {
        _apiClient = apiClient;
        _authService = authService;
        _loginViewModel = loginViewModel;
        _leaderboardViewModel = leaderboardViewModel;
        _matchHistoryViewModel = matchHistoryViewModel;
        _lobbiesViewModel = lobbiesViewModel;
        _serviceStatusViewModel = serviceStatusViewModel;
        _externalLinkService = externalLinkService;
        _logger = logger;

        // Subscribe to auth state changes from the auth service
        _authService.IsAuthenticated.Subscribe(OnAuthStateChanged);
    }

    private void OnAuthStateChanged(bool authenticated)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsAuthenticated = authenticated;
            if (authenticated)
            {
                Username = _authService.CurrentDisplayName ?? "Authenticated User";
                StatusMessage = "Connected to Generals Online";
                _logger.LogInformation("Auth state changed: authenticated as {Username}", Username);
            }
            else
            {
                Username = "Not Authenticated";
                StatusMessage = "Please log in";
                _logger.LogInformation("Auth state changed: not authenticated");
            }
        });
    }

    /// <summary>
    /// Gets the login ViewModel.
    /// </summary>
    public LoginViewModel Login => _loginViewModel;

    /// <summary>
    /// Gets the leaderboard ViewModel.
    /// </summary>
    public LeaderboardViewModel Leaderboard => _leaderboardViewModel;

    /// <summary>
    /// Gets the match history ViewModel.
    /// </summary>
    public MatchHistoryViewModel MatchHistory => _matchHistoryViewModel;

    /// <summary>
    /// Gets the lobbies ViewModel.
    /// </summary>
    public LobbiesViewModel Lobbies => _lobbiesViewModel;

    /// <summary>
    /// Gets the service status ViewModel.
    /// </summary>
    public ServiceStatusViewModel ServiceStatus => _serviceStatusViewModel;

    /// <summary>
    /// Initializes the ViewModel.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            // Load public data regardless of auth state
            await LoadPublicDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Generals Online view");
            StatusMessage = "Error connecting to service";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await InitializeAsync();
    }

    [RelayCommand]
    private void OpenDiscord()
    {
        if (!_externalLinkService.OpenUrl("https://discord.playgenerals.online/"))
        {
            _logger.LogWarning("Failed to open Discord link");
        }
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        if (!_externalLinkService.OpenUrl("https://www.playgenerals.online/"))
        {
            _logger.LogWarning("Failed to open website link");
        }
    }

    private async Task LoadPublicDataAsync()
    {
        // Load public data even without authentication
        await Task.WhenAll(
            ServiceStatus.LoadAsync(),
            Leaderboard.LoadAsync(),
            Lobbies.LoadAsync());
    }
}
