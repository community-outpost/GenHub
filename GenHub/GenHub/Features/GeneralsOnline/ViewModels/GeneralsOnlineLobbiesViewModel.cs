using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Messages;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GeneralsOnline.ViewModels;

/// <summary>
/// ViewModel for the Generals Online lobby browser: sign in through the
/// browser game-code flow, browse community lobbies, check local profile
/// compatibility by CRC, and launch the best matching profile.
/// </summary>
public sealed partial class GeneralsOnlineLobbiesViewModel : ViewModelBase,
    IDisposable,
    IRecipient<ProfileCreatedMessage>,
    IRecipient<ProfileUpdatedMessage>,
    IRecipient<ProfileDeletedMessage>,
    IRecipient<ProfileListUpdatedMessage>
{
    private readonly IGeneralsOnlineApiClient _apiClient;
    private readonly IGeneralsOnlineAuthService _authService;
    private readonly IGeneralsOnlineCompatibilityService _compatibilityService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<GeneralsOnlineLobbiesViewModel> _logger;
    private readonly GeneralsOnlineLobbiesDependencies? _dependencies;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SemaphoreSlim _sessionRecoveryLock = new(1, 1);
    private readonly Dictionary<long, GeneralsOnlineProfileMatch> _bestMatchByLobby = [];
    private readonly Dictionary<long, int> _latencyByLobby = [];
    private readonly Dictionary<long, ObservableCollection<GeneralsOnlineFriendChatMessage>> _dmThreads = [];
    private readonly HashSet<long> _dmUnread = [];
    private readonly HashSet<(string Operation, long UserId)> _inFlightSocialActions = [];
    private readonly object _debounceLock = new();
    private string? _lastLobbiesError;
    private CancellationTokenSource? _wsDebounceCts;
    private CancellationTokenSource? _friendsDebounceCts;
    private CancellationTokenSource? _selectedDetailCts;
    private int _refreshPending;
    private long _refreshArrivalTicket;
    private CancellationTokenSource? _signInCts;
    private DateTime _lastSessionRecoveryUtc = DateTime.MinValue;
    private long _refreshCoveredTicket;
    private long _lastHintRefreshTicks;
    private long? _detailLobbyId;
    private bool _disposed;
    private bool _silentLoginAttempted;
    private bool _wsSubscribed;
    private bool _roomSelected;
    private bool _chatRoomApplied;
    private int _appliedChatRoomId = int.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineLobbiesViewModel"/> class.
    /// </summary>
    /// <param name="apiClient">The Generals Online API client.</param>
    /// <param name="authService">The Generals Online auth service.</param>
    /// <param name="compatibilityService">The profile compatibility service.</param>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dependencies">Optional lobby dependencies.</param>
    public GeneralsOnlineLobbiesViewModel(
        IGeneralsOnlineApiClient apiClient,
        IGeneralsOnlineAuthService authService,
        IGeneralsOnlineCompatibilityService compatibilityService,
        INotificationService notificationService,
        ILogger<GeneralsOnlineLobbiesViewModel> logger,
        GeneralsOnlineLobbiesDependencies? dependencies = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _compatibilityService = compatibilityService ?? throw new ArgumentNullException(nameof(compatibilityService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dependencies = dependencies;

        AuthState = _authService.AuthState;
        DisplayName = _authService.CurrentDisplayName;
        _authService.AuthStateChanged += OnAuthServiceStateChanged;

        WeakReferenceMessenger.Default.Register<ProfileCreatedMessage>(this);
        WeakReferenceMessenger.Default.Register<ProfileUpdatedMessage>(this);
        WeakReferenceMessenger.Default.Register<ProfileDeletedMessage>(this);
        WeakReferenceMessenger.Default.Register<ProfileListUpdatedMessage>(this);

        BrowserOpener = url => BrowserHelper.TryOpenUrl(url, dependencies?.Logger ?? _logger);

        if (_dependencies?.WsListener is { } wsListener)
        {
            wsListener.LobbyListChanged += OnLobbyListHint;
            wsListener.CurrentLobbyChanged += OnLobbyListHint;
            wsListener.RoomChatReceived += OnRoomChatReceived;
            wsListener.FriendsChanged += OnFriendsHint;
            wsListener.FriendChatReceived += OnFriendChatReceived;
            wsListener.FriendPresenceChanged += OnFriendPresenceChanged;
            wsListener.RoomOccupantsChanged += OnRoomOccupantsChanged;
            wsListener.NewFriendRequestReceived += OnNewFriendRequestReceived;
            wsListener.ModerationNoticeReceived += OnModerationNoticeReceived;
            _wsSubscribed = true;
        }
    }

    [ObservableProperty]
    private ObservableCollection<GeneralsOnlineLobby> _lobbies = [];

    [ObservableProperty]
    private ObservableCollection<GeneralsOnlineLobby> _visibleLobbies = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedLobbyCompatibility))]
    [NotifyPropertyChangedFor(nameof(SelectedLobbyBestProfileName))]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    private GeneralsOnlineLobby? _selectedLobby;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _compatibleOnly;

    [ObservableProperty]
    private bool _showInProgress = true;

    [ObservableProperty]
    private bool _hideFull;

    [ObservableProperty]
    private bool _hidePassworded;

    [ObservableProperty]
    private string _selectedRegion = "All";

    [ObservableProperty]
    private ObservableCollection<string> _availableRegions = ["All"];

    [ObservableProperty]
    private string _selectedSort = "Players";

    [ObservableProperty]
    private ObservableCollection<GeneralsOnlineProfileMatch> _rankedProfilesForSelected = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedLobbyCompatibility))]
    [NotifyPropertyChangedFor(nameof(SelectedLobbyBestProfileName))]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    private GeneralsOnlineProfileMatch? _selectedLaunchProfile;

    [ObservableProperty]
    private ObservableCollection<GeneralsOnlineFriend> _friends = [];

    [ObservableProperty]
    private ObservableCollection<GeneralsOnlineFriend> _pendingRequests = [];

    [ObservableProperty]
    private ObservableCollection<GeneralsOnlineFriend> _blockedUsers = [];

    [ObservableProperty]
    private ObservableCollection<GeneralsOnlineRoom> _rooms = [];

    [ObservableProperty]
    private GeneralsOnlineRoom? _selectedRoom;

    [ObservableProperty]
    private GeneralsOnlineRoom? _chatRoom;

    [ObservableProperty]
    private ObservableCollection<GeneralsOnlineRoomOccupant> _roomOccupants = [];

    [ObservableProperty]
    private ObservableCollection<GeneralsOnlineLobbyMember> _selectedLobbyOccupants = [];

    [ObservableProperty]
    private ObservableCollection<GeneralsOnlineRoomChatMessage> _roomChatMessages = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendRoomChatCommand))]
    private string _roomChatInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDm))]
    [NotifyCanExecuteChangedFor(nameof(SendDmCommand))]
    private GeneralsOnlineFriend? _dmFriend;

    [ObservableProperty]
    private ObservableCollection<GeneralsOnlineFriendChatMessage> _dmMessages = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendDmCommand))]
    private string _dmInput = string.Empty;

    [ObservableProperty]
    private int _waitingCount;

    [ObservableProperty]
    private int _inProgressCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isBackgroundRefreshing;

    [ObservableProperty]
    private bool _isFriendsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLobbiesWarning))]
    private string? _lobbiesWarningText;

    [ObservableProperty]
    private long _selfUserId = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthenticated))]
    [NotifyPropertyChangedFor(nameof(CanShowSignInButton))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendRoomChatCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendDmCommand))]
    private GeneralsOnlineAuthState _authState = GeneralsOnlineAuthState.Unauthenticated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn))]
    [NotifyPropertyChangedFor(nameof(CanShowSignInButton))]
    [NotifyPropertyChangedFor(nameof(CanCancelSignIn))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSignInCommand))]
    private bool _isSigningIn;

    [ObservableProperty]
    private string? _displayName;

    [ObservableProperty]
    private int _publicLobbyCount;

    [ObservableProperty]
    private int _publicPlayerCount;

    [ObservableProperty]
    private int _publicPlayingCount;

    [ObservableProperty]
    private int _matchesToday;

    [ObservableProperty]
    private int _winsToday;

    [ObservableProperty]
    private string? _serviceUptimeText;

    [ObservableProperty]
    private string? _serviceStartTimeText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMotd))]
    private string? _motdText;

    [ObservableProperty]
    private IReadOnlyList<GeneralsOnlineMotdRun> _motdRuns = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlayerStats))]
    private string? _playerCardText;

    [ObservableProperty]
    private string? _playerDetailText;

    [ObservableProperty]
    private bool _hasLobbiesNotice;

    /// <summary>
    /// Gets a value indicating whether the user is authenticated.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool IsAuthenticated => AuthState == GeneralsOnlineAuthState.Authenticated;

    /// <summary>
    /// Gets a value indicating whether the sign-in button should be shown.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool CanShowSignInButton => !IsAuthenticated && !IsSigningIn;

    /// <summary>
    /// Gets a value indicating whether the filtered lobby list is non-empty.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool HasLobbies => VisibleLobbies.Count > 0;

    /// <summary>
    /// Gets a value indicating whether a message of the day is available.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool HasMotd => !string.IsNullOrWhiteSpace(MotdText);

    /// <summary>
    /// Gets a value indicating whether the signed-in player's stats are available.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool HasPlayerStats => !string.IsNullOrWhiteSpace(PlayerCardText);

    /// <summary>
    /// Gets the compatibility of the selected lobby against local profiles.
    /// Prefers the explicitly chosen launch profile, falling back to the best match.
    /// </summary>
    public GeneralsOnlineCompatibility SelectedLobbyCompatibility =>
        SelectedLaunchProfile?.Compatibility
            ?? (SelectedLobby is not null && _bestMatchByLobby.TryGetValue(SelectedLobby.LobbyId, out var match)
                ? match.Compatibility
                : GeneralsOnlineCompatibility.Unknown);

    /// <summary>
    /// Gets the best matching local profile name for the selected lobby.
    /// Prefers the explicitly chosen launch profile, falling back to the best match.
    /// </summary>
    public string SelectedLobbyBestProfileName =>
        SelectedLaunchProfile?.ProfileName
            ?? (SelectedLobby is not null && _bestMatchByLobby.TryGetValue(SelectedLobby.LobbyId, out var match)
                ? match.ProfileName
                : string.Empty);

    /// <summary>
    /// Gets the estimated latency for the selected lobby in milliseconds, or null when unknown.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public int? SelectedLobbyLatency =>
        SelectedLobby is not null && _latencyByLobby.TryGetValue(SelectedLobby.LobbyId, out var latency)
            ? latency
            : null;

    /// <summary>
    /// Gets the formatted lobby CRC pair for the selected lobby.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public string SelectedLobbyCrcText =>
        SelectedLobby is null
            ? string.Empty
            : $"EXE 0x{SelectedLobby.ExeCrc:X8} • INI 0x{SelectedLobby.IniCrc:X8}";

    /// <summary>
    /// Gets the formatted profile CRC pair for the chosen launch profile.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public string SelectedProfileCrcText
    {
        get
        {
            if (SelectedLaunchProfile is null)
            {
                return string.Empty;
            }

            var exe = SelectedLaunchProfile.ProfileExeCrc.HasValue
                ? $"0x{SelectedLaunchProfile.ProfileExeCrc.Value:X8}"
                : "unknown";
            var ini = SelectedLaunchProfile.ProfileIniCrc.HasValue
                ? $"0x{SelectedLaunchProfile.ProfileIniCrc.Value:X8}"
                : "unknown";
            return $"EXE {exe} • INI {ini}";
        }
    }

    /// <summary>
    /// Gets a value indicating whether the friends list is non-empty.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool HasFriends => Friends.Count > 0 || PendingRequests.Count > 0;

    /// <summary>
    /// Gets a value indicating whether room chat messages are available.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool HasRoomChat => RoomChatMessages.Count > 0;

    /// <summary>
    /// Gets a value indicating whether a transient lobby warning is shown.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool HasLobbiesWarning => !string.IsNullOrWhiteSpace(LobbiesWarningText);

    /// <summary>
    /// Gets a value indicating whether a direct message thread is open.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool HasDm => DmFriend is not null;

    /// <summary>
    /// Gets a value indicating whether blocked users are listed.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool HasBlocked => BlockedUsers.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the user id belongs to the signed-in user.
    /// </summary>
    /// <param name="userId">The user id to check.</param>
    /// <returns>True when the id is the signed-in user.</returns>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool IsSelf(long userId) => SelfUserId > 0 && userId == SelfUserId;

    /// <summary>
    /// Gets the number of DM threads with unread messages.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public int DmUnreadCount
    {
        get
        {
            lock (_debounceLock)
            {
                return _dmUnread.Count;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether any DM thread has unread messages.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool HasDmUnread => DmUnreadCount > 0;

    /// <summary>
    /// Gets the localized server-total lobby count shown under the joinable count.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public string TotalLobbiesText => GetString(
        "Online.GeneralsOnline.Stats.OfTotal",
        PublicLobbyCount.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Gets the compatibility verdict for a lobby id, or unknown when unevaluated.
    /// </summary>
    /// <param name="lobbyId">The lobby id.</param>
    /// <returns>The cached compatibility verdict.</returns>
    public GeneralsOnlineCompatibility GetCompatibilityFor(long lobbyId) =>
        _bestMatchByLobby.TryGetValue(lobbyId, out var match) ? match.Compatibility : GeneralsOnlineCompatibility.Unknown;

    /// <summary>
    /// Gets the estimated latency for a lobby id, or null when unknown.
    /// </summary>
    /// <param name="lobbyId">The lobby id.</param>
    /// <returns>The latency in milliseconds, or null.</returns>
    public int? GetLatencyFor(long lobbyId) =>
        _latencyByLobby.TryGetValue(lobbyId, out var latency) ? latency : null;

    /// <summary>
    /// Gets or sets the browser opener for community links. Defaults to system browser helper.
    /// </summary>
    internal Func<string, bool> BrowserOpener { get; set; }

    /// <summary>
    /// Refreshes the lobby list, attempting a silent token login first.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return RunRefreshAsync(background: false, cancellationToken);
    }

    /// <summary>
    /// Refreshes lobbies in the background without disabling commands.
    /// Hint-driven refreshes must never flicker buttons or toast on blips.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task RefreshBackgroundAsync(CancellationToken cancellationToken = default)
    {
        return RunRefreshAsync(background: true, cancellationToken);
    }

    /// <summary>
    /// Cancels an in-progress browser sign-in attempt.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelSignIn))]
    public void CancelSignIn()
    {
        _signInCts?.Cancel();
    }

    /// <summary>
    /// Signs in through the browser game-code flow.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(CanSignIn))]
    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        _signInCts?.Dispose();
        _signInCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _signInCts.Token;

        try
        {
            IsSigningIn = true;
            IsLoading = true;
            _notificationService.ShowInfo(
                GetString("Online.GeneralsOnline.Auth.SignInTitle"),
                GetString("Online.GeneralsOnline.Auth.BrowserMessage"),
                NotificationDurations.Medium);

            var result = await _authService.LoginWithBrowserAsync(token);
            if (!result.Success || result.Data is null)
            {
                ShowAuthFailureToast(result);
                return;
            }

            _silentLoginAttempted = true;
            SyncAuthProps();
            _notificationService.ShowSuccess(
                GetString("Online.GeneralsOnline.Auth.WelcomeTitle"),
                GetString("Online.GeneralsOnline.Auth.WelcomeMessage", DisplayName ?? string.Empty),
                NotificationDurations.Long);
            await ConnectWebSocketAsync(token);
            await RefreshAsync(token);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "Generals Online browser sign-in was cancelled.");
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generals Online browser sign-in failed.");
            ShowErrorToast("Online.GeneralsOnline.Auth.FailedTitle", null);
        }
        finally
        {
            IsSigningIn = false;
            IsLoading = false;
            _signInCts?.Dispose();
            _signInCts = null;
        }
    }

    /// <summary>
    /// Signs out and clears the lobby list.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(IsAuthenticated))]
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _authService.LogoutAsync(cancellationToken);
            _silentLoginAttempted = true;
            SyncAuthProps();
            ClearLobbies();
            _notificationService.ShowInfo(
                GetString("Online.GeneralsOnline.Auth.SignOutTitle"),
                GetString("Online.GeneralsOnline.Auth.SignOutMessage"),
                NotificationDurations.Medium);
            await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generals Online sign-out failed.");
            ShowErrorToast("Online.GeneralsOnline.Auth.SignOutTitle", null);
        }
    }

    /// <summary>
    /// Launches the best matching local profile for a lobby.
    /// </summary>
    /// <param name="lobby">The lobby to join, or null to use the selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    public async Task LaunchAsync(GeneralsOnlineLobby? lobby, CancellationToken cancellationToken = default)
    {
        var target = lobby ?? SelectedLobby;
        if (target is null)
        {
            return;
        }

        if (!IsAuthenticated)
        {
            _notificationService.ShowWarning(
                GetString("Online.GeneralsOnline.Auth.RequiredTitle"),
                GetString("Online.GeneralsOnline.Auth.RequiredMessage"),
                NotificationDurations.Long);
            return;
        }

        var launchService = _dependencies?.LaunchService;
        if (launchService is null)
        {
            _notificationService.ShowWarning(
                GetString("Online.GeneralsOnline.Launch.UnavailableTitle"),
                GetString("Online.GeneralsOnline.Launch.UnavailableMessage"),
                NotificationDurations.Long);
            return;
        }

        try
        {
            IsLoading = true;

            // Prefer the explicitly chosen launch profile when it targets the
            // selected lobby and is still compatible. Otherwise rank fresh so
            // list-row launches and stale selections resolve correctly.
            GeneralsOnlineProfileMatch? best = null;
            IReadOnlyList<GeneralsOnlineProfileMatch>? ranked = null;
            if (SelectedLaunchProfile is not null
                && target.LobbyId == SelectedLobby?.LobbyId
                && SelectedLaunchProfile.Compatibility == GeneralsOnlineCompatibility.Compatible)
            {
                best = SelectedLaunchProfile;
            }
            else
            {
                var rank = await _compatibilityService.RankProfilesAsync(target, cancellationToken);
                if (!rank.Success || rank.Data is null)
                {
                    ShowErrorToast("Online.GeneralsOnline.Launch.FailedTitle", rank.Errors.FirstOrDefault());
                    return;
                }

                ranked = rank.Data;
                best = rank.Data.FirstOrDefault(m => m.Compatibility == GeneralsOnlineCompatibility.Compatible);
            }

            if (best is null)
            {
                ranked ??= await RankOrEmptyAsync(target, cancellationToken);
                ShowNoCompatibleToast(target, ranked);
                return;
            }

            _notificationService.ShowInfo(
                GetString("Online.GeneralsOnline.Launch.LaunchingTitle"),
                GetString("Online.GeneralsOnline.Launch.LaunchingMessage", target.Name, best.ProfileName),
                NotificationDurations.Medium);

            var result = await launchService.PlayAsync(best.ProfileId, target.Name, string.Empty, string.Empty, cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.GeneralsOnline.Launch.FailedTitle", result.Errors.FirstOrDefault());
                return;
            }

            _notificationService.ShowSuccess(
                GetString("Online.GeneralsOnline.Launch.SuccessTitle"),
                GetString("Online.GeneralsOnline.Launch.SuccessMessage", target.Name),
                NotificationDurations.Long);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch lobby {LobbyId} from Generals Online.", target.LobbyId);
            ShowErrorToast("Online.GeneralsOnline.Launch.FailedTitle", null);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Opens a community link in the default browser.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    [RelayCommand]
    public void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var opened = false;
        try
        {
            opened = BrowserOpener(uri.AbsoluteUri);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            opened = false;
        }
        catch (ObjectDisposedException)
        {
            opened = false;
        }
        catch (InvalidOperationException)
        {
            opened = false;
        }
        catch (PlatformNotSupportedException)
        {
            opened = false;
        }

        if (!opened)
        {
            _notificationService.ShowError(
                GetString("Online.GeneralsOnline.Links.FailedTitle"),
                GetString("Online.GeneralsOnline.Links.FailedMessage", uri.AbsoluteUri),
                NotificationDurations.Long);
        }
    }

    /// <summary>
    /// Clears the lobby search text.
    /// </summary>
    [RelayCommand]
    public void ClearSearch()
    {
        SearchText = string.Empty;
    }

    /// <summary>
    /// Invalidates cached profile CRCs and recomputes compatibility.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task RecheckCompatibilityAsync(CancellationToken cancellationToken = default)
    {
        _compatibilityService.InvalidateCache();
        await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Sends the room chat input to the selected network room.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(CanSendChat))]
    public async Task SendRoomChatAsync(CancellationToken cancellationToken = default)
    {
        var wsListener = _dependencies?.WsListener;
        if (wsListener is null || !IsAuthenticated)
        {
            return;
        }

        var text = RoomChatInput.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            var result = await wsListener.SendRoomChatAsync(text, cancellationToken);
            if (result.Success)
            {
                RoomChatInput = string.Empty;
            }
            else
            {
                ShowErrorToast("Online.GeneralsOnline.Chat.FailedTitle", result.Errors.FirstOrDefault());
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generals Online room chat send failed.");
            ShowErrorToast("Online.GeneralsOnline.Chat.FailedTitle", null);
        }
    }

    /// <summary>
    /// Opens a direct message thread with a friend.
    /// </summary>
    /// <param name="friend">The friend to message.</param>
    [RelayCommand]
    public void OpenDm(GeneralsOnlineFriend? friend)
    {
        if (friend is null || friend.UserId <= 0)
        {
            return;
        }

        DmFriend = Friends.FirstOrDefault(f => f.UserId == friend.UserId) ?? friend;
    }

    /// <summary>
    /// Closes the open direct message thread.
    /// </summary>
    [RelayCommand]
    public void CloseDm()
    {
        DmFriend = null;
    }

    /// <summary>
    /// Sends the DM input to the open thread's friend.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(CanSendDm))]
    public async Task SendDmAsync(CancellationToken cancellationToken = default)
    {
        var wsListener = _dependencies?.WsListener;
        var peer = DmFriend;
        if (wsListener is null || !IsAuthenticated || peer is null || peer.UserId <= 0)
        {
            return;
        }

        var text = DmInput.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            var result = await wsListener.SendFriendChatAsync(peer.UserId, text, cancellationToken);
            if (result.Success)
            {
                DmInput = string.Empty;
            }
            else
            {
                ShowErrorToast("Online.GeneralsOnline.Dm.FailedTitle", result.Errors.FirstOrDefault());
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generals Online direct message send failed.");
            ShowErrorToast("Online.GeneralsOnline.Dm.FailedTitle", null);
        }
    }

    /// <summary>
    /// Sends a friend request to a user id.
    /// </summary>
    /// <param name="userId">The target user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task AddFriendAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (Friends.Any(f => f.UserId == userId))
        {
            _notificationService.ShowInfo(
                GetString("Online.GeneralsOnline.Friends.AlreadyTitle"),
                GetString("Online.GeneralsOnline.Friends.AlreadyMessage"),
                NotificationDurations.Medium);
            return;
        }

        await RunSocialActionAsync(
            userId,
            _apiClient.SendFriendRequestAsync,
            "Online.GeneralsOnline.Friends.RequestSentTitle",
            "Online.GeneralsOnline.Friends.RequestSentMessage",
            "friend request send",
            cancellationToken);
    }

    /// <summary>
    /// Accepts a pending friend request.
    /// </summary>
    /// <param name="friend">The requester.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task AcceptFriendAsync(GeneralsOnlineFriend? friend, CancellationToken cancellationToken = default)
    {
        if (friend is null)
        {
            return;
        }

        await RunSocialActionAsync(
            friend.UserId,
            _apiClient.AcceptFriendRequestAsync,
            "Online.GeneralsOnline.Friends.AcceptedTitle",
            "Online.GeneralsOnline.Friends.AcceptedMessage",
            "friend request accept",
            cancellationToken,
            friend.DisplayName);
    }

    /// <summary>
    /// Rejects a pending friend request.
    /// </summary>
    /// <param name="friend">The requester.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task RejectFriendAsync(GeneralsOnlineFriend? friend, CancellationToken cancellationToken = default)
    {
        if (friend is null)
        {
            return;
        }

        await RunSocialActionAsync(
            friend.UserId,
            _apiClient.RejectFriendRequestAsync,
            "Online.GeneralsOnline.Friends.RejectedTitle",
            "Online.GeneralsOnline.Friends.RejectedMessage",
            "friend request reject",
            cancellationToken,
            friend.DisplayName);
    }

    /// <summary>
    /// Removes a friend.
    /// </summary>
    /// <param name="friend">The friend to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task RemoveFriendAsync(GeneralsOnlineFriend? friend, CancellationToken cancellationToken = default)
    {
        if (friend is null)
        {
            return;
        }

        await RunSocialActionAsync(
            friend.UserId,
            _apiClient.RemoveFriendAsync,
            "Online.GeneralsOnline.Friends.RemovedTitle",
            "Online.GeneralsOnline.Friends.RemovedMessage",
            "friend remove",
            cancellationToken,
            friend.DisplayName);
    }

    /// <summary>
    /// Blocks a user id.
    /// </summary>
    /// <param name="userId">The target user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task BlockUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        await RunSocialActionAsync(
            userId,
            _apiClient.BlockUserAsync,
            "Online.GeneralsOnline.Friends.BlockedTitle",
            "Online.GeneralsOnline.Friends.BlockedMessage",
            "user block",
            cancellationToken);
    }

    /// <summary>
    /// Unblocks a user.
    /// </summary>
    /// <param name="friend">The blocked entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task UnblockUserAsync(GeneralsOnlineFriend? friend, CancellationToken cancellationToken = default)
    {
        if (friend is null)
        {
            return;
        }

        await RunSocialActionAsync(
            friend.UserId,
            _apiClient.UnblockUserAsync,
            "Online.GeneralsOnline.Friends.UnblockedTitle",
            "Online.GeneralsOnline.Friends.UnblockedMessage",
            "user unblock",
            cancellationToken,
            friend.DisplayName);
    }

    /// <summary>
    /// Refreshes friends and pending requests.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task RefreshFriendsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated || _disposed)
        {
            return;
        }

        try
        {
            IsFriendsLoading = true;
            await LoadFriendsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generals Online friends refresh failed.");
        }
        finally
        {
            IsFriendsLoading = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _authService.AuthStateChanged -= OnAuthServiceStateChanged;
        WeakReferenceMessenger.Default.Unregister<ProfileCreatedMessage>(this);
        WeakReferenceMessenger.Default.Unregister<ProfileUpdatedMessage>(this);
        WeakReferenceMessenger.Default.Unregister<ProfileDeletedMessage>(this);
        WeakReferenceMessenger.Default.Unregister<ProfileListUpdatedMessage>(this);

        var wsListener = _dependencies?.WsListener;
        if (wsListener is not null)
        {
            wsListener.LobbyListChanged -= OnLobbyListHint;
            wsListener.CurrentLobbyChanged -= OnLobbyListHint;
            wsListener.RoomChatReceived -= OnRoomChatReceived;
            wsListener.FriendsChanged -= OnFriendsHint;
            wsListener.FriendChatReceived -= OnFriendChatReceived;
            wsListener.FriendPresenceChanged -= OnFriendPresenceChanged;
            wsListener.RoomOccupantsChanged -= OnRoomOccupantsChanged;
            wsListener.NewFriendRequestReceived -= OnNewFriendRequestReceived;
            wsListener.ModerationNoticeReceived -= OnModerationNoticeReceived;
        }

        lock (_debounceLock)
        {
            _wsDebounceCts?.Cancel();
            _wsDebounceCts?.Dispose();
            _wsDebounceCts = null;
            _friendsDebounceCts?.Cancel();
            _friendsDebounceCts?.Dispose();
            _friendsDebounceCts = null;
            _selectedDetailCts?.Cancel();
            _selectedDetailCts?.Dispose();
            _selectedDetailCts = null;
        }

        _signInCts?.Cancel();
        _signInCts?.Dispose();
        _signInCts = null;
        _sessionRecoveryLock.Dispose();
        _refreshLock.Dispose();
    }

    /// <inheritdoc />
    public void Receive(ProfileCreatedMessage message) => OnProfilesChanged();

    /// <inheritdoc />
    public void Receive(ProfileUpdatedMessage message) => OnProfilesChanged();

    /// <inheritdoc />
    public void Receive(ProfileDeletedMessage message) => OnProfilesChanged();

    /// <inheritdoc />
    public void Receive(ProfileListUpdatedMessage message) => OnProfilesChanged();

    /// <summary>
    /// Picks the show-all network room from a room list, falling back to the
    /// first room. The backend filters lobby reads by the selected room and
    /// treats selecting its first room as viewing every room.
    /// </summary>
    /// <param name="rooms">The network rooms.</param>
    /// <returns>The room to select, or null when the list is empty.</returns>
    internal static GeneralsOnlineRoom? SelectShowAllRoom(IReadOnlyList<GeneralsOnlineRoom> rooms)
    {
        return rooms.FirstOrDefault(r => (r.Flags & GeneralsOnlineConstants.RoomFlagsShowAllMatches) != 0)
            ?? rooms.FirstOrDefault();
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; used as an instance CanExecute predicate.")]
    private bool CanSignIn => !IsLoading && !IsAuthenticated && !IsSigningIn;

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; used as an instance CanExecute predicate.")]
    private bool CanCancelSignIn => IsSigningIn;

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; used as an instance CanExecute predicate.")]
    private bool CanLaunch => SelectedLobby is not null && !IsLoading;

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; used as an instance CanExecute predicate.")]
    private bool CanSendChat => IsAuthenticated && !string.IsNullOrWhiteSpace(RoomChatInput);

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; used as an instance CanExecute predicate.")]
    private bool CanSendDm => IsAuthenticated && DmFriend is not null && !string.IsNullOrWhiteSpace(DmInput);

    private static void RunOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private void DecrementRefreshPending()
    {
        // The busy state spans the whole serialized batch so commands stay
        // disabled until the last queued refresh finishes.
        if (Interlocked.Decrement(ref _refreshPending) == 0)
        {
            IsLoading = false;
        }
    }

    private async Task RunRefreshAsync(bool background, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        // Every arrival takes a ticket so queued refreshes can tell whether a
        // pass that started after they arrived already covered their trigger.
        var ticket = Interlocked.Increment(ref _refreshArrivalTicket);
        if (background)
        {
            IsBackgroundRefreshing = true;
        }
        else if (Interlocked.Increment(ref _refreshPending) == 1)
        {
            IsLoading = true;
        }

        try
        {
            // Block instead of skipping: a refresh abandoned here would leave
            // IsLoading false while another refresh still updates the lists,
            // letting commands run against partially updated state.
            await _refreshLock.WaitAsync(cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            // Dispose ran while this detached refresh was queued.
            ClearRefreshState(background);
            return;
        }
        catch (OperationCanceledException)
        {
            ClearRefreshState(background);
            throw;
        }

        try
        {
            // Collapse bursts: when a pass that started after this request
            // arrived already ran to completion, its data covers this trigger.
            if (Volatile.Read(ref _refreshCoveredTicket) >= ticket)
            {
                return;
            }

            var coverTicket = Volatile.Read(ref _refreshArrivalTicket);
            if (background)
            {
                IsBackgroundRefreshing = true;
            }
            else
            {
                IsLoading = true;
            }

            await RefreshCoreAsync(background, cancellationToken);
            Volatile.Write(ref _refreshCoveredTicket, coverTicket);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (background)
            {
                _logger.LogWarning(ex, "Generals Online background refresh failed.");
            }
            else
            {
                _logger.LogError(ex, "Failed to refresh the Generals Online lobby list.");
                ShowErrorToast("Online.GeneralsOnline.Lobbies.FailedTitle", null);
            }
        }
        finally
        {
            ClearRefreshState(background);
            try
            {
                _refreshLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Dispose ran while this detached refresh was in flight.
            }
        }
    }

    private void ClearRefreshState(bool background)
    {
        if (background)
        {
            IsBackgroundRefreshing = false;
            return;
        }

        DecrementRefreshPending();
    }

    private async Task RunSocialActionAsync(
        long userId,
        Func<long, CancellationToken, Task<OperationResult<bool>>> action,
        string successTitleKey,
        string successMessageKey,
        string operation,
        CancellationToken cancellationToken,
        string? displayName = null)
    {
        if (!IsAuthenticated || userId <= 0 || IsSelf(userId) || _disposed)
        {
            return;
        }

        lock (_inFlightSocialActions)
        {
            if (!_inFlightSocialActions.Add((operation, userId)))
            {
                return;
            }
        }

        try
        {
            var result = await action(userId, cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.GeneralsOnline.Friends.FailedTitle", result.Errors.FirstOrDefault());
                return;
            }

            _notificationService.ShowSuccess(
                GetString(successTitleKey),
                displayName is null ? GetString(successMessageKey) : GetString(successMessageKey, displayName),
                NotificationDurations.Medium);
            await RefreshFriendsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generals Online {Operation} failed.", operation);
            ShowErrorToast("Online.GeneralsOnline.Friends.FailedTitle", null);
        }
        finally
        {
            lock (_inFlightSocialActions)
            {
                _inFlightSocialActions.Remove((operation, userId));
            }
        }
    }

    private void OnAuthServiceStateChanged(object? sender, GeneralsOnlineAuthState state)
    {
        RunOnUi(() =>
        {
            SyncAuthProps();
            if (IsAuthenticated)
            {
                _ = ConnectWebSocketAsync(CancellationToken.None);
                _ = RefreshAsync(CancellationToken.None);
            }
            else
            {
                ClearLobbies();
                _ = DisconnectWebSocketAsync();
            }
        });
    }

    private void OnLobbyListHint(object? sender, EventArgs e)
    {
        RunOnUi(() => _ = DebouncedRefreshAsync());
    }

    private void OnProfilesChanged()
    {
        _compatibilityService.InvalidateCache();
        SelectedLaunchProfile = null;
        if (IsAuthenticated && Lobbies.Count > 0)
        {
            RunOnUi(() => _ = RefreshBackgroundAsync(CancellationToken.None));
        }
    }

    private async Task RefreshCoreAsync(bool background, CancellationToken cancellationToken)
    {
        if (!IsAuthenticated && !_silentLoginAttempted)
        {
            await TrySilentLoginAsync(cancellationToken);
        }

        if (!IsAuthenticated)
        {
            await LoadPublicCountsAsync(cancellationToken);
            return;
        }

        // Rooms first: the lobby fetch dances through the show-all room and
        // back to the chat room, which needs both rooms resolved.
        await LoadRoomsAsync(!background, cancellationToken);
        await ApplyChatRoomAsync(ChatRoom, cancellationToken);
        if (background)
        {
            await Task.WhenAll(LoadLobbiesAsync(background: true, cancellationToken), LoadPublicCountsAsync(cancellationToken));
            return;
        }

        await Task.WhenAll(
            LoadLobbiesAsync(background: false, cancellationToken),
            LoadPublicCountsAsync(cancellationToken),
            LoadCommunityStatsAsync(cancellationToken),
            LoadFriendsAsync(cancellationToken),
            LoadMeAsync(cancellationToken));
    }

    private async Task TrySilentLoginAsync(CancellationToken cancellationToken)
    {
        _silentLoginAttempted = true;
        var login = await _authService.TryLoginWithStoredTokenAsync(cancellationToken);
        SyncAuthProps();
        if (login.Success && IsAuthenticated)
        {
            _logger.LogInformation("Restored the Generals Online session for {DisplayName}.", DisplayName);
            await ConnectWebSocketAsync(cancellationToken);
        }
    }

    private async Task<OperationResult<GeneralsOnlineLobbiesResult>?> EnsureValidLobbiesAsync(
        OperationResult<GeneralsOnlineLobbiesResult>? lobbies,
        bool background,
        CancellationToken cancellationToken)
    {
        if (lobbies is { Success: true, Data: not null })
        {
            return lobbies;
        }

        var isAuthRequired = string.Equals(
            lobbies?.Errors.FirstOrDefault(),
            GeneralsOnlineConstants.ErrorAuthRequired,
            StringComparison.Ordinal);

        if (!isAuthRequired)
        {
            HandleLobbiesLoadFailure(lobbies, background);
            return null;
        }

        if (!await TryRecoverSessionAsync(cancellationToken) || !IsAuthenticated)
        {
            return null;
        }

        var reloaded = await GetLobbiesWithRoomDanceAsync(cancellationToken);
        if (!IsAuthenticated)
        {
            return null;
        }

        if (reloaded is not { Success: true, Data: not null })
        {
            HandleLobbiesLoadFailure(reloaded, background);
            return null;
        }

        return reloaded;
    }

    private async Task LoadLobbiesAsync(bool background, CancellationToken cancellationToken)
    {
        var lobbies = await GetLobbiesWithRoomDanceAsync(cancellationToken);
        if (!IsAuthenticated)
        {
            // Signed out while loading; ClearLobbies already ran.
            return;
        }

        lobbies = await EnsureValidLobbiesAsync(lobbies, background, cancellationToken);
        if (lobbies?.Data is null)
        {
            return;
        }

        lobbies = await RetryEmptyLobbiesIfRoomNotSelectedAsync(lobbies, cancellationToken);
        if (lobbies.Data is null)
        {
            return;
        }

        HasLobbiesNotice = false;
        LobbiesWarningText = null;
        _lastLobbiesError = null;
        UpdateLatencies(lobbies.Data);
        SyncLobbies(lobbies.Data.Lobbies);
        UpdateLobbyCounts();
        UpdateAvailableRegions();
        await UpdateCompatibilityAsync(cancellationToken);
        ApplyFilter();
    }

    private void HandleLobbiesLoadFailure(OperationResult<GeneralsOnlineLobbiesResult>? lobbies, bool background)
    {
        var error = lobbies?.Errors.FirstOrDefault();
        if (string.Equals(error, GeneralsOnlineConstants.ErrorAuthRequired, StringComparison.Ordinal))
        {
            return;
        }

        // Keep stale lobbies on transient failures: clearing the list on
        // every blip flashes the UI empty during the refresh storm.
        var forbidden = string.Equals(
            lobbies?.Errors.FirstOrDefault(),
            GeneralsOnlineConstants.ErrorLobbiesForbidden,
            StringComparison.Ordinal);
        if (forbidden)
        {
            SyncLobbies([]);
            _latencyByLobby.Clear();
            HasLobbiesNotice = true;
        }
        else
        {
            LobbiesWarningText = GetString("Online.GeneralsOnline.Lobbies.StaleWarning");
            if (!background)
            {
                ShowLobbiesFailure(lobbies?.Errors.FirstOrDefault());
            }
        }
    }

    private async Task<OperationResult<GeneralsOnlineLobbiesResult>> RetryEmptyLobbiesIfRoomNotSelectedAsync(
        OperationResult<GeneralsOnlineLobbiesResult> lobbies,
        CancellationToken cancellationToken)
    {
        if (lobbies.Data is null || lobbies.Data.Lobbies.Count > 0 || _roomSelected)
        {
            return lobbies;
        }

        var wsListener = _dependencies?.WsListener;
        if (wsListener is null || !wsListener.IsConnected || !wsListener.IsSocketOpen)
        {
            return lobbies;
        }

        using var shortCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        shortCts.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            await SelectShowAllRoomAsync(wsListener, shortCts.Token);
            if (_roomSelected)
            {
                _chatRoomApplied = false;
                var retryResult = await _apiClient.GetLobbiesAsync(cancellationToken);
                if (retryResult.Success && retryResult.Data is not null)
                {
                    return retryResult;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Socket was not open or selection probe timed out; avoid stalling the refresh.
        }
        finally
        {
            if (_roomSelected)
            {
                await ApplyChatRoomAsync(ChatRoom, cancellationToken);
            }
        }

        return lobbies;
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private void UpdateLobbyCounts()
    {
        PublicPlayingCount = Lobbies.Where(l => l.IsInProgress).Sum(l => l.PlayerCount);
        WaitingCount = Lobbies.Count(l => l.IsWaiting);
        InProgressCount = Lobbies.Count(l => l.IsInProgress);
    }

    private async Task<OperationResult<GeneralsOnlineLobbiesResult>?> GetLobbiesWithRoomDanceAsync(CancellationToken cancellationToken)
    {
        // Lobby reads filter by the session's selected room, but room chat
        // only arrives for the room the session sits in. Briefly select the
        // show-all room for the read, then sit back in the chat room.
        var wsListener = _dependencies?.WsListener;
        var showAll = SelectedRoom;
        var chatRoom = ChatRoom;
        if (wsListener is null || !wsListener.IsConnected || showAll is null || chatRoom is null || showAll.Id == chatRoom.Id || !_roomSelected)
        {
            return await _apiClient.GetLobbiesAsync(cancellationToken);
        }

        if (showAll.Id < short.MinValue || showAll.Id > short.MaxValue)
        {
            return await _apiClient.GetLobbiesAsync(cancellationToken);
        }

        try
        {
            await wsListener.SelectNetworkRoomAsync((short)showAll.Id, cancellationToken);
            return await _apiClient.GetLobbiesAsync(cancellationToken);
        }
        finally
        {
            _chatRoomApplied = false;
            await ApplyChatRoomAsync(chatRoom, cancellationToken);
        }
    }

    private async Task<OperationResult<T>?> FetchWithAuthRecoveryAsync<T>(
        Func<CancellationToken, Task<OperationResult<T>>> fetchFunc,
        CancellationToken cancellationToken)
    {
        var result = await fetchFunc(cancellationToken);
        if (IsAuthenticated && result is { Success: true, Data: not null })
        {
            return result;
        }

        if (IsAuthenticated
            && string.Equals(result?.Errors.FirstOrDefault(), GeneralsOnlineConstants.ErrorAuthRequired, StringComparison.Ordinal)
            && await TryRecoverSessionAsync(cancellationToken))
        {
            var retried = await fetchFunc(cancellationToken);
            if (IsAuthenticated && retried is { Success: true, Data: not null })
            {
                return retried;
            }
        }

        return null;
    }

    private async Task LoadFriendsAsync(CancellationToken cancellationToken)
    {
        // Auxiliary decoration: failures stay silent so a social outage never
        // blocks the lobby list or spams error toasts.
        try
        {
            var friends = await FetchWithAuthRecoveryAsync(ct => _apiClient.GetFriendsAsync(ct), cancellationToken);
            if (friends?.Data is not null)
            {
                SyncFriends(friends.Data.Friends, friends.Data.PendingRequests);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Generals Online friends list unavailable.");
        }

        try
        {
            var blocked = await _apiClient.GetBlockedAsync(cancellationToken);
            if (IsAuthenticated && blocked.Success && blocked.Data is not null)
            {
                SyncBlocked(blocked.Data.Blocked);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Generals Online blocked list unavailable.");
        }
    }

    private async Task LoadRoomsAsync(bool force, CancellationToken cancellationToken)
    {
        if (!force && Rooms.Count > 0)
        {
            return;
        }

        try
        {
            var rooms = await FetchWithAuthRecoveryAsync(ct => _apiClient.GetRoomsAsync(ct), cancellationToken);
            if (rooms?.Data is not null)
            {
                SyncRooms(rooms.Data);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Generals Online room list unavailable.");
        }
    }

    private async Task LoadMeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var me = await _apiClient.GetMeAsync(cancellationToken);
            if (IsAuthenticated && me.Success && me.Data is not null && me.Data.UserId > 0)
            {
                SelfUserId = me.Data.UserId;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Generals Online current user unavailable.");
        }
    }

    private async Task LoadPublicCountsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var countsTask = _apiClient.GetPublicCountsAsync(cancellationToken);
            var uptimeTask = _apiClient.GetServiceUptimeAsync(cancellationToken);
            await Task.WhenAll(countsTask, uptimeTask);

            var counts = countsTask.Result;
            if (counts.Success && counts.Data is not null)
            {
                PublicLobbyCount = counts.Data.Lobbies;
                PublicPlayerCount = counts.Data.Players;
            }
            else
            {
                PublicLobbyCount = 0;
                PublicPlayerCount = 0;
            }

            var uptime = uptimeTask.Result;
            if (uptime.Success && uptime.Data is not null && !string.IsNullOrWhiteSpace(uptime.Data.Uptime))
            {
                ServiceUptimeText = uptime.Data.Uptime.Trim();
                ServiceStartTimeText = string.IsNullOrWhiteSpace(uptime.Data.StartTime)
                    ? null
                    : GetString("Online.GeneralsOnline.Stats.UptimeSince", uptime.Data.StartTime.Trim());
            }
            else
            {
                ServiceUptimeText = null;
                ServiceStartTimeText = null;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Generals Online public counts.");
        }
    }

    private async Task LoadCommunityStatsAsync(CancellationToken cancellationToken)
    {
        // Auxiliary decorations: failures stay silent so a stats outage
        // never blocks the lobby list or spams error toasts.
        try
        {
            var globalTask = _apiClient.GetGlobalStatsAsync(cancellationToken);
            var motdTask = _apiClient.GetMotdAsync(cancellationToken);

            var userId = _authService.CurrentUserId;
            var playerTask = userId is > 0
                ? _apiClient.GetPlayerStatsAsync(userId.Value, cancellationToken)
                : null;

            if (playerTask is not null)
            {
                await Task.WhenAll(globalTask, motdTask, playerTask);
            }
            else
            {
                await Task.WhenAll(globalTask, motdTask);
            }

            ApplyGlobalStats(globalTask.Result);
            ApplyMotd(motdTask.Result);
            ApplyPlayerStats(playerTask);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Generals Online community stats.");
        }
    }

    private void ApplyGlobalStats(OperationResult<GeneralsOnlineDailyStats> global)
    {
        if (IsAuthenticated && global.Success && global.Data is not null)
        {
            MatchesToday = global.Data.TotalMatches;
            WinsToday = global.Data.TotalWins;
        }
        else
        {
            MatchesToday = 0;
            WinsToday = 0;
        }
    }

    private void ApplyMotd(OperationResult<string> motd)
    {
        if (IsAuthenticated && motd.Success && !string.IsNullOrWhiteSpace(motd.Data))
        {
            MotdText = motd.Data.Trim();
            MotdRuns = GeneralsOnlineMotdParser.Parse(MotdText);
        }
        else
        {
            MotdText = null;
            MotdRuns = [];
        }
    }

    private void ApplyPlayerStats(Task<OperationResult<GeneralsOnlinePlayerStats>>? playerTask)
    {
        if (playerTask is null)
        {
            PlayerCardText = null;
            PlayerDetailText = null;
            return;
        }

        var player = playerTask.Result;
        if (IsAuthenticated && player.Success && player.Data is not null)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            PlayerCardText = GetString(
                "Online.GeneralsOnline.Stats.PlayerCard",
                player.Data.EloRating.ToString(culture),
                player.Data.TotalWins.ToString(culture),
                player.Data.TotalLosses.ToString(culture));
            PlayerDetailText = BuildPlayerDetailText(player.Data);
        }
        else
        {
            PlayerCardText = null;
            PlayerDetailText = null;
        }
    }

    private async Task UpdateCompatibilityAsync(CancellationToken cancellationToken)
    {
        _bestMatchByLobby.Clear();
        foreach (var lobby in Lobbies)
        {
            lobby.LocalCompatibility = GeneralsOnlineCompatibility.Unknown;
        }

        if (_dependencies?.ProfileManager is null)
        {
            return;
        }

        // Hundreds of lobbies usually share a handful of CRC pairs. Ranking
        // once per unique pair keeps refreshes fast instead of O(lobbies).
        var rankedByCrcs = new Dictionary<(uint Exe, uint Ini), GeneralsOnlineProfileMatch>();
        foreach (var group in Lobbies.GroupBy(l => (l.ExeCrc, l.IniCrc)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var representative = group.First();
            var matches = await RankOrEmptyAsync(representative, cancellationToken);
            if (matches.Count == 0)
            {
                continue;
            }

            var best = matches.FirstOrDefault(m => m.Compatibility == GeneralsOnlineCompatibility.Compatible)
                ?? matches[0];
            rankedByCrcs[group.Key] = best;
            foreach (var lobby in group)
            {
                _bestMatchByLobby[lobby.LobbyId] = best;
                lobby.LocalCompatibility = best.Compatibility;
            }
        }

        OnPropertyChanged(nameof(SelectedLobbyCompatibility));
        OnPropertyChanged(nameof(SelectedLobbyBestProfileName));
    }

    private void ApplyFilter()
    {
        // ComboBox two-way bindings push null while their item lists rebuild,
        // so coalesce here so a mid-refresh filter pass can never throw.
        var query = SearchText?.Trim() ?? string.Empty;
        var region = SelectedRegion?.Trim() ?? "All";
        var filterRegion = !string.IsNullOrEmpty(region) && !string.Equals(region, "All", StringComparison.OrdinalIgnoreCase);
        var filtered = Lobbies.Where(l =>
            (string.IsNullOrEmpty(query)
                || l.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || l.MapName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || l.HostName.Contains(query, StringComparison.OrdinalIgnoreCase))
            && (!CompatibleOnly
                || (_bestMatchByLobby.TryGetValue(l.LobbyId, out var match)
                    && match.Compatibility == GeneralsOnlineCompatibility.Compatible))
            && (ShowInProgress || !l.IsInProgress)
            && (!HideFull || !l.IsFull)
            && (!HidePassworded || !l.IsPassworded)
            && (!filterRegion || string.Equals(l.Region, region, StringComparison.OrdinalIgnoreCase)));

        var sorted = SortLobbies(filtered).ToList();
        var selectedId = SelectedLobby?.LobbyId;
        SyncVisibleLobbies(sorted);
        var next = VisibleLobbies.FirstOrDefault(l => l.LobbyId == selectedId)
            ?? VisibleLobbies.FirstOrDefault();
        if (!ReferenceEquals(SelectedLobby, next))
        {
            SelectedLobby = next;
        }
    }

    private IEnumerable<GeneralsOnlineLobby> SortLobbies(IEnumerable<GeneralsOnlineLobby> source)
    {
        return SelectedSort switch
        {
            "Name" => source.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase),
            "Map" => source.OrderBy(l => l.MapName, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase),
            "Latency" => source.OrderBy(l => GetLatencyFor(l.LobbyId) ?? int.MaxValue).ThenByDescending(l => l.PlayerCount),
            "Newest" => source.OrderByDescending(l => l.TimeCreated),
            "Setup" => source.OrderBy(l => l.ExeCrc).ThenBy(l => l.IniCrc).ThenByDescending(l => l.PlayerCount),
            _ => source.OrderByDescending(l => l.PlayerCount).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase),
        };
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private void RemoveStaleLobbies(ObservableCollection<GeneralsOnlineLobby> lobbies, Dictionary<long, GeneralsOnlineLobby> freshById)
    {
        for (var index = lobbies.Count - 1; index >= 0; index--)
        {
            if (!freshById.ContainsKey(lobbies[index].LobbyId))
            {
                lobbies.RemoveAt(index);
            }
        }
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private void UpdateLobbyAt(ObservableCollection<GeneralsOnlineLobby> lobbies, int index, GeneralsOnlineLobby incoming)
    {
        lobbies[index].LocalLatencyMs = incoming.LocalLatencyMs;
        if (!AreLobbiesEqual(lobbies[index], incoming))
        {
            incoming.LocalCompatibility = lobbies[index].LocalCompatibility;
            lobbies[index] = incoming;
        }
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private void UpsertLobby(int targetIndex, GeneralsOnlineLobby incoming)
    {
        if (targetIndex < Lobbies.Count && Lobbies[targetIndex].LobbyId == incoming.LobbyId)
        {
            UpdateLobbyAt(Lobbies, targetIndex, incoming);
            return;
        }

        var existingIndex = IndexOfLobby(Lobbies, incoming.LobbyId);
        if (existingIndex >= 0)
        {
            var destination = Math.Min(targetIndex, Lobbies.Count - 1);
            Lobbies.Move(existingIndex, destination);
            UpdateLobbyAt(Lobbies, destination, incoming);
        }
        else
        {
            Lobbies.Insert(Math.Min(targetIndex, Lobbies.Count), incoming);
        }
    }

    private void SyncLobbies(IReadOnlyList<GeneralsOnlineLobby> fresh)
    {
        // Incremental sync keeps the ListBox virtualized items, scroll offset,
        // and selection stable across 5-second background refreshes. Wholesale
        // replacement rebuilds every row and flickers with hundreds of lobbies.
        var byId = fresh.ToDictionary(l => l.LobbyId);
        RemoveStaleLobbies(Lobbies, byId);

        for (var index = 0; index < fresh.Count; index++)
        {
            UpsertLobby(index, fresh[index]);
        }

        if (SelectedLobby is not null)
        {
            if (byId.TryGetValue(SelectedLobby.LobbyId, out var freshLobby))
            {
                if (!ReferenceEquals(SelectedLobby, freshLobby))
                {
                    SelectedLobby = freshLobby;
                }
            }
            else
            {
                SelectedLobby = null;
            }
        }

        if (fresh.Count == 0)
        {
            _bestMatchByLobby.Clear();
            SyncVisibleLobbies([]);
            SelectedLobby = null;
        }
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private void RemoveStaleVisibleLobbies(ObservableCollection<GeneralsOnlineLobby> visible, HashSet<long> wanted)
    {
        for (var index = visible.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(visible[index].LobbyId))
            {
                visible.RemoveAt(index);
            }
        }
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private void UpsertVisibleLobby(ObservableCollection<GeneralsOnlineLobby> visible, int index, GeneralsOnlineLobby incoming)
    {
        if (index < visible.Count && visible[index].LobbyId == incoming.LobbyId)
        {
            if (!ReferenceEquals(visible[index], incoming))
            {
                visible[index] = incoming;
            }

            return;
        }

        var existingIndex = IndexOfLobby(visible, incoming.LobbyId);
        if (existingIndex >= 0)
        {
            var destination = Math.Min(index, visible.Count - 1);
            visible.Move(existingIndex, destination);
            if (!ReferenceEquals(visible[destination], incoming))
            {
                visible[destination] = incoming;
            }
        }
        else
        {
            visible.Insert(Math.Min(index, visible.Count), incoming);
        }
    }

    private void SyncVisibleLobbies(IReadOnlyList<GeneralsOnlineLobby> fresh)
    {
        // Incremental sync like SyncLobbies: clearing the bound list resets
        // the ListBox scroll offset on every background refresh.
        var hadLobbies = VisibleLobbies.Count > 0;
        var wanted = new HashSet<long>(fresh.Select(l => l.LobbyId));
        RemoveStaleVisibleLobbies(VisibleLobbies, wanted);

        for (var index = 0; index < fresh.Count; index++)
        {
            UpsertVisibleLobby(VisibleLobbies, index, fresh[index]);
        }

        if (hadLobbies != (VisibleLobbies.Count > 0))
        {
            OnPropertyChanged(nameof(HasLobbies));
        }
    }

    private void SyncFriends(IReadOnlyList<GeneralsOnlineFriend> friends, IReadOnlyList<GeneralsOnlineFriend> pending)
    {
        Friends.Clear();
        foreach (var friend in friends.OrderByDescending(f => f.IsOnline).ThenBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            Friends.Add(friend);
        }

        PendingRequests.Clear();
        foreach (var request in pending.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            PendingRequests.Add(request);
        }

        OnPropertyChanged(nameof(HasFriends));
    }

    private void SyncBlocked(IReadOnlyList<GeneralsOnlineFriend> blocked)
    {
        BlockedUsers.Clear();
        foreach (var entry in blocked.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            BlockedUsers.Add(entry);
        }

        OnPropertyChanged(nameof(HasBlocked));
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads and writes generated MVVM properties from CommunityToolkit source generators that Sonar analyzer does not resolve.")]
    private void SyncRooms(IReadOnlyList<GeneralsOnlineRoom> rooms)
    {
        var selectedId = SelectedRoom?.Id ?? SelectShowAllRoom(rooms)?.Id;
        var chatId = ChatRoom?.Id;
        Rooms.Clear();
        foreach (var room in rooms)
        {
            Rooms.Add(room);
        }

        SelectedRoom = Rooms.FirstOrDefault(r => r.Id == selectedId) ?? SelectShowAllRoom(rooms);

        // Chat lives in a real room: nobody else sits in the show-all room,
        // so its chat carries only the session's own echoes.
        var previousChat = Rooms.FirstOrDefault(r => r.Id == chatId);
        ChatRoom = previousChat ?? Rooms.FirstOrDefault(r => !IsShowAllRoom(r)) ?? SelectedRoom;
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private bool IsShowAllRoom(GeneralsOnlineRoom room)
    {
        return (room.Flags & GeneralsOnlineConstants.RoomFlagsShowAllMatches) != 0;
    }

    private async Task ApplyChatRoomAsync(GeneralsOnlineRoom? room, CancellationToken cancellationToken)
    {
        var wsListener = _dependencies?.WsListener;
        if (wsListener is null || !wsListener.IsConnected || room is null || _disposed)
        {
            return;
        }

        if (_chatRoomApplied && room.Id == _appliedChatRoomId)
        {
            return;
        }

        if (room.Id < short.MinValue || room.Id > short.MaxValue)
        {
            _logger.LogWarning("Generals Online chat room id {RoomId} out of range for short network room id.", room.Id);
            return;
        }

        try
        {
            var roomChanged = room.Id != _appliedChatRoomId;
            var applied = await wsListener.SelectNetworkRoomAsync((short)room.Id, cancellationToken);
            if (applied.Success)
            {
                _appliedChatRoomId = room.Id;
                _chatRoomApplied = true;
                if (roomChanged)
                {
                    RunOnUi(() =>
                    {
                        RoomChatMessages.Clear();
                        OnPropertyChanged(nameof(HasRoomChat));
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Generals Online chat room selection failed.");
        }
    }

    private void UpdateLatencies(GeneralsOnlineLobbiesResult result)
    {
        _latencyByLobby.Clear();
        for (var index = 0; index < result.Lobbies.Count && index < result.Latencies.Count; index++)
        {
            _latencyByLobby[result.Lobbies[index].LobbyId] = result.Latencies[index];
            result.Lobbies[index].LocalLatencyMs = result.Latencies[index];
        }

        OnPropertyChanged(nameof(SelectedLobbyLatency));
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads and writes generated MVVM properties from CommunityToolkit source generators that Sonar analyzer does not resolve.")]
    private void UpdateAvailableRegions()
    {
        var regions = Lobbies
            .Select(l => l.Region?.Trim())
            .OfType<string>()
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
        regions.Insert(0, "All");

        if (AvailableRegions.SequenceEqual(regions, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        // Incremental sync: clearing the bound list pushes a null selection
        // back through the two-way binding and drops the user's region.
        // Moves and inserts keep the current selection on its item.
        for (var index = 0; index < regions.Count; index++)
        {
            var existing = IndexOfString(AvailableRegions, regions[index]);
            if (existing == index)
            {
                continue;
            }

            if (existing >= 0)
            {
                AvailableRegions.Move(existing, index);
            }
            else
            {
                AvailableRegions.Insert(index, regions[index]);
            }
        }

        while (AvailableRegions.Count > regions.Count)
        {
            AvailableRegions.RemoveAt(AvailableRegions.Count - 1);
        }

        var selected = SelectedRegion ?? "All";
        if (!AvailableRegions.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            SelectedRegion = "All";
        }
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private int IndexOfString(ObservableCollection<string> source, string value)
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (string.Equals(source[index], value, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private bool AreMembersEqual(IReadOnlyList<GeneralsOnlineLobbyMember> left, IReadOnlyList<GeneralsOnlineLobbyMember> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var l = left[index];
            var r = right[index];
            if (l.UserId != r.UserId
                || l.SlotIndex != r.SlotIndex
                || l.SlotState != r.SlotState
                || l.IsReady != r.IsReady
                || l.HasMap != r.HasMap
                || l.Team != r.Team
                || l.Side != r.Side
                || l.Color != r.Color
                || l.StartingPosition != r.StartingPosition
                || !string.Equals(l.DisplayName, r.DisplayName, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private bool AreLobbiesEqual(GeneralsOnlineLobby left, GeneralsOnlineLobby right)
    {
        return left.LobbyId == right.LobbyId
            && left.Owner == right.Owner
            && left.State == right.State
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && string.Equals(left.MapName, right.MapName, StringComparison.Ordinal)
            && string.Equals(left.MapPath, right.MapPath, StringComparison.Ordinal)
            && string.Equals(left.Region, right.Region, StringComparison.Ordinal)
            && left.TimeCreated == right.TimeCreated
            && left.PlayerCount == right.PlayerCount
            && left.MaxPlayers == right.MaxPlayers
            && left.StartingCash == right.StartingCash
            && left.IsPassworded == right.IsPassworded
            && left.AllowObservers == right.AllowObservers
            && left.IsMapOfficial == right.IsMapOfficial
            && left.ExeCrc == right.ExeCrc
            && left.IniCrc == right.IniCrc
            && AreMembersEqual(left.Members, right.Members);
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private int IndexOfLobby(ObservableCollection<GeneralsOnlineLobby> source, long lobbyId)
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (source[index].LobbyId == lobbyId)
            {
                return index;
            }
        }

        return -1;
    }

    private async Task<IReadOnlyList<GeneralsOnlineProfileMatch>> RankOrEmptyAsync(GeneralsOnlineLobby lobby, CancellationToken cancellationToken)
    {
        try
        {
            var rank = await _compatibilityService.RankProfilesAsync(lobby, cancellationToken);
            return rank.Success && rank.Data is not null ? rank.Data : [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Compatibility ranking failed for lobby {LobbyId}.", lobby.LobbyId);
            return [];
        }
    }

    /// <summary>
    /// Attempts to recover an expired session by performing a stored-token login,
    /// reconnecting the WebSocket with the new session token, and falling back
    /// to unauthenticated if recovery fails.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the session was successfully recovered; false if unauthenticated.</returns>
    private async Task<bool> TryRecoverSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _sessionRecoveryLock.WaitAsync(cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        try
        {
            if (!IsAuthenticated)
            {
                return false;
            }

            if (DateTime.UtcNow - _lastSessionRecoveryUtc < TimeSpan.FromSeconds(2))
            {
                return IsAuthenticated;
            }

            _logger.LogInformation("Generals Online session expired. Attempting recovery via stored token.");
            var login = await _authService.TryLoginWithStoredTokenAsync(cancellationToken);
            SyncAuthProps();

            if (login.Success && IsAuthenticated)
            {
                _lastSessionRecoveryUtc = DateTime.UtcNow;
                _logger.LogInformation("Recovered Generals Online session for {DisplayName}.", DisplayName);
                await ConnectWebSocketAsync(cancellationToken, forceReconnect: true);
                return true;
            }

            _logger.LogWarning("Generals Online session recovery failed. Falling back to unauthenticated.");
            await _authService.LogoutAsync(cancellationToken);
            SyncAuthProps();
            ClearLobbies();
            await DisconnectWebSocketAsync();
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Generals Online session recovery.");
            try
            {
                await _authService.LogoutAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception logoutEx)
            {
                _logger.LogDebug(logoutEx, "Best-effort Generals Online logout during recovery failure skipped.");
            }

            SyncAuthProps();
            ClearLobbies();
            await DisconnectWebSocketAsync();
            return false;
        }
        finally
        {
            try
            {
                _sessionRecoveryLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed while session recovery was in flight.
            }
        }
    }

    private async Task ConnectWebSocketAsync(CancellationToken cancellationToken = default, bool forceReconnect = false)
    {
        var wsListener = _dependencies?.WsListener;
        if (wsListener is null)
        {
            return;
        }

        if (wsListener.IsConnected && !forceReconnect)
        {
            if (!_roomSelected)
            {
                await SelectShowAllRoomAsync(wsListener, cancellationToken);
            }

            return;
        }

        var uri = _authService.WebSocketUri;
        string? token;
        try
        {
            token = await _authService.GetSessionTokenAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or CryptographicException)
        {
            // Token storage I/O failed; a throwing fire-and-forget connect
            // must not fault the auth-state callback.
            _logger.LogWarning(ex, "Generals Online live updates are unavailable; the list refreshes manually.");
            return;
        }

        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        if (!_wsSubscribed)
        {
            wsListener.LobbyListChanged += OnLobbyListHint;
            wsListener.CurrentLobbyChanged += OnLobbyListHint;
            wsListener.RoomChatReceived += OnRoomChatReceived;
            wsListener.FriendsChanged += OnFriendsHint;
            wsListener.FriendChatReceived += OnFriendChatReceived;
            wsListener.FriendPresenceChanged += OnFriendPresenceChanged;
            wsListener.RoomOccupantsChanged += OnRoomOccupantsChanged;
            wsListener.NewFriendRequestReceived += OnNewFriendRequestReceived;
            wsListener.ModerationNoticeReceived += OnModerationNoticeReceived;
            _wsSubscribed = true;
        }

        var connected = await wsListener.ConnectAsync(uri, token, cancellationToken);
        if (connected is null || !connected.Success)
        {
            _logger.LogWarning("Generals Online live updates are unavailable; the list refreshes manually.");
            return;
        }

        await SelectShowAllRoomAsync(wsListener, cancellationToken);
        await SubscribeSocialAsync(wsListener, cancellationToken);
    }

    private async Task SubscribeSocialAsync(IGeneralsOnlineWebSocketListener wsListener, CancellationToken cancellationToken)
    {
        try
        {
            var subscribed = await wsListener.SubscribeSocialAsync(cancellationToken);
            if (!subscribed.Success)
            {
                _logger.LogDebug("Generals Online social subscribe failed; presence updates arrive with the friends list.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Generals Online social subscribe failed.");
        }
    }

    private async Task SelectShowAllRoomAsync(IGeneralsOnlineWebSocketListener wsListener, CancellationToken cancellationToken)
    {
        // Sessions without a selected room match no network room, so the
        // lobby list reads empty. The game client selects a room on entry;
        // the launcher selects the show-all room instead of joining one.
        IReadOnlyList<GeneralsOnlineRoom>? roomList = null;
        try
        {
            var rooms = await _apiClient.GetRoomsAsync(cancellationToken);
            if (rooms.Success && rooms.Data is not null)
            {
                roomList = rooms.Data;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generals Online room list unavailable; lobby browsing stays room-filtered.");
            return;
        }

        if (roomList is null)
        {
            _logger.LogWarning("Generals Online room list unavailable; lobby browsing stays room-filtered.");
            return;
        }

        var showAll = SelectShowAllRoom(roomList);
        if (showAll is null)
        {
            _logger.LogWarning("Generals Online room list was empty; lobby browsing stays room-filtered.");
            return;
        }

        if (showAll.Id < short.MinValue || showAll.Id > short.MaxValue)
        {
            _logger.LogWarning("Generals Online room id {RoomId} is out of range.", showAll.Id);
            return;
        }

        var selected = await wsListener.SelectNetworkRoomAsync((short)showAll.Id, cancellationToken);
        if (selected.Success)
        {
            _roomSelected = true;
            _logger.LogInformation("Selected the Generals Online {RoomName} room for lobby browsing.", showAll.Name);
        }
        else
        {
            _roomSelected = false;
            _logger.LogWarning("Generals Online room selection failed; lobby browsing stays room-filtered.");
        }
    }

    private async Task DisconnectWebSocketAsync()
    {
        _roomSelected = false;
        _chatRoomApplied = false;
        _appliedChatRoomId = int.MinValue;
        var wsListener = _dependencies?.WsListener;
        if (wsListener is null || !wsListener.IsConnected)
        {
            return;
        }

        await wsListener.DisconnectAsync(CancellationToken.None);
    }

    private async Task DebouncedRefreshAsync()
    {
        CancellationToken token = default;
        lock (_debounceLock)
        {
            if (_disposed)
            {
                return;
            }

            // Dispose tears down the same source under this lock, so neither
            // side can dispose the other's instance or create one after disposal.
            _wsDebounceCts?.Cancel();
            _wsDebounceCts?.Dispose();
            _wsDebounceCts = new CancellationTokenSource();
            token = _wsDebounceCts.Token;
        }

        try
        {
            await Task.Delay(GeneralsOnlineConstants.WebSocketRefreshDebounceMs, token);

            // Hints fire on every lobby event server-wide; without a floor the
            // client refetches several times per second. Wait out the remainder
            // of the minimum interval instead of skipping, so the list still
            // converges on busy servers.
            var elapsed = Environment.TickCount64 - Volatile.Read(ref _lastHintRefreshTicks);
            var remaining = GeneralsOnlineConstants.HintRefreshMinIntervalMs - elapsed;
            if (remaining > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(remaining), token);
            }

            Volatile.Write(ref _lastHintRefreshTicks, Environment.TickCount64);
            await RefreshBackgroundAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer hint superseded this refresh.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generals Online hint refresh failed.");
        }
    }

    private void OnRoomChatReceived(object? sender, GeneralsOnlineRoomChatMessage message)
    {
        RunOnUi(() =>
        {
            RoomChatMessages.Add(message);
            while (RoomChatMessages.Count > GeneralsOnlineConstants.RoomChatMaxMessages)
            {
                RoomChatMessages.RemoveAt(0);
            }

            OnPropertyChanged(nameof(HasRoomChat));
        });
    }

    [SuppressMessage("Cognitive Complexity", "S3776:Cognitive Complexity of methods should not be too high", Justification = "Chat routing split into helper methods.")]
    private void OnFriendChatReceived(object? sender, GeneralsOnlineFriendChatMessage message)
    {
        RunOnUi(() =>
        {
            if (!ResolveChatPeer(message, out var isOwn, out var peerId))
            {
                return;
            }

            message.IsOwn = isOwn;

            if (!_dmThreads.TryGetValue(peerId, out var thread))
            {
                thread = [];
                _dmThreads[peerId] = thread;
            }

            thread.Add(message);
            TrimCollection(thread, GeneralsOnlineConstants.FriendChatMaxMessages);

            if (DmFriend?.UserId == peerId)
            {
                DmMessages.Add(message);
                TrimCollection(DmMessages, GeneralsOnlineConstants.FriendChatMaxMessages);
            }
            else if (!message.IsOwn)
            {
                HandleUnreadFriendChat(peerId, message);
            }
        });
    }

    private bool ResolveChatPeer(GeneralsOnlineFriendChatMessage message, out bool isOwn, out long peerId)
    {
        isOwn = (SelfUserId > 0 && message.SourceUserId == SelfUserId)
            || (SelfUserId <= 0 && DmFriend is not null && message.TargetUserId == DmFriend.UserId);

        if (isOwn && SelfUserId <= 0 && message.SourceUserId > 0
            && message.SourceUserId != message.TargetUserId
            && Friends.All(f => f.UserId != message.SourceUserId))
        {
            SelfUserId = message.SourceUserId;
        }

        peerId = isOwn ? message.TargetUserId : message.SourceUserId;
        if (peerId <= 0)
        {
            peerId = DmFriend?.UserId ?? message.SourceUserId;
        }

        if (SelfUserId <= 0)
        {
            return TryResolveUnknownSelfPeer(message, ref isOwn, ref peerId);
        }

        return true;
    }

    private bool TryResolveUnknownSelfPeer(GeneralsOnlineFriendChatMessage message, ref bool isOwn, ref long peerId)
    {
        if (DmFriend is not null && (message.SourceUserId == DmFriend.UserId || message.TargetUserId == DmFriend.UserId))
        {
            return true;
        }

        if (message.TargetUserId > 0 && Friends.Any(f => f.UserId == message.TargetUserId))
        {
            if (message.SourceUserId > 0
                && message.SourceUserId != message.TargetUserId
                && Friends.All(f => f.UserId != message.SourceUserId))
            {
                SelfUserId = message.SourceUserId;
            }

            isOwn = true;
            peerId = message.TargetUserId;
            return true;
        }

        if (message.SourceUserId > 0 && Friends.Any(f => f.UserId == message.SourceUserId))
        {
            isOwn = false;
            peerId = message.SourceUserId;
            return true;
        }

        _logger.LogDebug(
            "Dropping friend chat message with indeterminate peer (SelfUserId={SelfUserId}, SourceUserId={SourceUserId}, TargetUserId={TargetUserId}).",
            SelfUserId,
            message.SourceUserId,
            message.TargetUserId);
        return false;
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private void TrimCollection<T>(IList<T> collection, int maxCount)
    {
        while (collection.Count > maxCount)
        {
            collection.RemoveAt(0);
        }
    }

    private void HandleUnreadFriendChat(long peerId, GeneralsOnlineFriendChatMessage message)
    {
        lock (_debounceLock)
        {
            _dmUnread.Add(peerId);
        }

        var peer = Friends.FirstOrDefault(f => f.UserId == peerId);
        _notificationService.ShowInfo(
            GetString("Online.GeneralsOnline.Dm.ReceivedTitle", peer?.DisplayName ?? peerId.ToString(CultureInfo.InvariantCulture)),
            message.Message,
            NotificationDurations.Medium);
        OnPropertyChanged(nameof(DmUnreadCount));
        OnPropertyChanged(nameof(HasDmUnread));
    }

    private void OnFriendPresenceChanged(object? sender, GeneralsOnlineFriendPresence presence)
    {
        RunOnUi(() =>
        {
            var index = FindFriendIndex(presence);
            if (index < 0)
            {
                return;
            }

            var friend = Friends[index];
            if (presence.IsOnline && !friend.IsOnline)
            {
                _notificationService.ShowInfo(
                    GetString("Online.GeneralsOnline.Friends.OnlineTitle"),
                    GetString("Online.GeneralsOnline.Friends.OnlineMessage", friend.DisplayName),
                    NotificationDurations.Medium);
            }

            friend.IsOnline = presence.IsOnline;
            Friends[index] = friend;
        });
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method accessing friends collection.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method accessing friends collection.")]
    private int FindFriendIndex(GeneralsOnlineFriendPresence presence)
    {
        if (presence.UserId > 0)
        {
            for (var i = 0; i < Friends.Count; i++)
            {
                if (Friends[i].UserId == presence.UserId)
                {
                    return i;
                }
            }
        }

        for (var i = 0; i < Friends.Count; i++)
        {
            if (string.Equals(Friends[i].DisplayName, presence.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates instance ObservableCollection.")]
    private void OnRoomOccupantsChanged(object? sender, GeneralsOnlineRoomMemberList members)
    {
        RunOnUi(() =>
        {
            RoomOccupants.Clear();
            foreach (var occupant in members.Occupants
                .OrderByDescending(o => o.IsAdmin)
                .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
            {
                RoomOccupants.Add(occupant);
            }
        });
    }

    private void OnNewFriendRequestReceived(object? sender, GeneralsOnlineIncomingFriendRequest request)
    {
        RunOnUi(() => _notificationService.ShowInfo(
            GetString("Online.GeneralsOnline.Friends.RequestReceivedTitle"),
            GetString("Online.GeneralsOnline.Friends.RequestReceivedMessage", request.DisplayName),
            NotificationDurations.Long));
    }

    private void OnModerationNoticeReceived(object? sender, GeneralsOnlineModerationNotice notice)
    {
        RunOnUi(() =>
        {
            var text = string.IsNullOrWhiteSpace(notice.Reason)
                ? notice.ActionType
                : $"{notice.ActionType}: {notice.Reason}";
            RoomChatMessages.Add(new GeneralsOnlineRoomChatMessage
            {
                Message = text,
                IsAdmin = true,
                ReceivedAtUtc = DateTime.UtcNow,
            });
            while (RoomChatMessages.Count > GeneralsOnlineConstants.RoomChatMaxMessages)
            {
                RoomChatMessages.RemoveAt(0);
            }

            OnPropertyChanged(nameof(HasRoomChat));
            _notificationService.ShowWarning(
                GetString("Online.GeneralsOnline.Chat.ModerationTitle"),
                text,
                NotificationDurations.Long);
        });
    }

    private void LoadDmThread(long friendUserId)
    {
        DmMessages.Clear();
        DmInput = string.Empty;
        if (friendUserId <= 0)
        {
            return;
        }

        lock (_debounceLock)
        {
            _dmUnread.Remove(friendUserId);
        }

        OnPropertyChanged(nameof(DmUnreadCount));
        OnPropertyChanged(nameof(HasDmUnread));
        if (_dmThreads.TryGetValue(friendUserId, out var thread))
        {
            foreach (var message in thread)
            {
                DmMessages.Add(message);
            }
        }
    }

    private void OnFriendsHint(object? sender, EventArgs e)
    {
        RunOnUi(() => _ = DebouncedFriendsRefreshAsync());
    }

    private async Task DebouncedFriendsRefreshAsync()
    {
        CancellationToken token = default;
        lock (_debounceLock)
        {
            if (_disposed)
            {
                return;
            }

            _friendsDebounceCts?.Cancel();
            _friendsDebounceCts?.Dispose();
            _friendsDebounceCts = new CancellationTokenSource();
            token = _friendsDebounceCts.Token;
        }

        try
        {
            await Task.Delay(GeneralsOnlineConstants.WebSocketRefreshDebounceMs, token);
            await RefreshFriendsAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer hint superseded this refresh.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generals Online friends hint refresh failed.");
        }
    }

    private void SyncAuthProps()
    {
        AuthState = _authService.AuthState;
        DisplayName = _authService.CurrentDisplayName;
    }

    private void ClearLobbies()
    {
        _roomSelected = false;
        _chatRoomApplied = false;
        _appliedChatRoomId = int.MinValue;
        _detailLobbyId = null;
        SelfUserId = -1;
        _dmThreads.Clear();
        lock (_debounceLock)
        {
            _dmUnread.Clear();
        }

        Lobbies.Clear();
        VisibleLobbies.Clear();
        SelectedLobby = null;
        RankedProfilesForSelected.Clear();
        SelectedLaunchProfile = null;
        Friends.Clear();
        PendingRequests.Clear();
        BlockedUsers.Clear();
        Rooms.Clear();
        SelectedRoom = null;
        ChatRoom = null;
        RoomOccupants.Clear();
        SelectedLobbyOccupants.Clear();
        RoomChatMessages.Clear();
        RoomChatInput = string.Empty;
        DmFriend = null;
        DmMessages.Clear();
        DmInput = string.Empty;
        WaitingCount = 0;
        InProgressCount = 0;
        PublicLobbyCount = 0;
        PublicPlayerCount = 0;
        PublicPlayingCount = 0;
        MatchesToday = 0;
        WinsToday = 0;
        ServiceUptimeText = null;
        ServiceStartTimeText = null;
        MotdText = null;
        MotdRuns = [];
        PlayerCardText = null;
        PlayerDetailText = null;
        HasLobbiesNotice = false;
        LobbiesWarningText = null;
        _lastLobbiesError = null;
        _bestMatchByLobby.Clear();
        _latencyByLobby.Clear();
        OnPropertyChanged(nameof(HasLobbies));
        OnPropertyChanged(nameof(HasFriends));
        OnPropertyChanged(nameof(HasBlocked));
        OnPropertyChanged(nameof(HasRoomChat));
        OnPropertyChanged(nameof(DmUnreadCount));
        OnPropertyChanged(nameof(HasDmUnread));
        OnPropertyChanged(nameof(SelectedLobbyCompatibility));
        OnPropertyChanged(nameof(SelectedLobbyBestProfileName));
        OnPropertyChanged(nameof(SelectedLobbyLatency));
        OnPropertyChanged(nameof(SelectedLobbyCrcText));
        OnPropertyChanged(nameof(SelectedProfileCrcText));
    }

    private void ShowAuthFailureToast<T>(OperationResult<T> result)
    {
        if (result.Errors.Any(e => string.Equals(e, GeneralsOnlineConstants.ErrorAccountBanned, StringComparison.Ordinal)))
        {
            _notificationService.ShowWarning(
                GetString("Online.GeneralsOnline.Auth.BannedTitle"),
                GetString("Online.GeneralsOnline.Auth.BannedMessage"),
                NotificationDurations.Long);
            return;
        }

        ShowErrorToast("Online.GeneralsOnline.Auth.FailedTitle", result.Errors.FirstOrDefault());
    }

    private void ShowNoCompatibleToast(GeneralsOnlineLobby lobby, IReadOnlyList<GeneralsOnlineProfileMatch> ranked)
    {
        if (ranked.Count == 0)
        {
            _notificationService.ShowWarning(
                GetString("Online.GeneralsOnline.Launch.NoProfilesTitle"),
                GetString("Online.GeneralsOnline.Launch.NoProfilesMessage"),
                NotificationDurations.Long);
            return;
        }

        var top = ranked[0];
        _notificationService.ShowWarning(
            GetString("Online.GeneralsOnline.Launch.MismatchTitle"),
            GetString(
                "Online.GeneralsOnline.Launch.MismatchMessage",
                top.ProfileName,
                $"0x{lobby.ExeCrc:X8}",
                $"0x{lobby.IniCrc:X8}"),
            NotificationDurations.Long);
    }

    private void ShowLobbiesFailure(string? error)
    {
        var forbidden = string.Equals(error, GeneralsOnlineConstants.ErrorLobbiesForbidden, StringComparison.Ordinal);
        HasLobbiesNotice = forbidden;
        if (!forbidden && !string.Equals(_lastLobbiesError, error, StringComparison.Ordinal))
        {
            ShowErrorToast("Online.GeneralsOnline.Lobbies.FailedTitle", error);
        }

        _lastLobbiesError = error;
    }

    private void ShowErrorToast(string titleKey, string? detail)
    {
        string detailText;
        if (!string.IsNullOrWhiteSpace(detail)
            && !detail.StartsWith(GeneralsOnlineConstants.ErrorCodePrefix, StringComparison.Ordinal)
            && !detail.StartsWith(OnlineConstants.ErrorCodePrefix, StringComparison.Ordinal)
            && _dependencies?.LocalizationService is { } loc
            && loc.TryGetString(detail, out var localized))
        {
            detailText = localized;
        }
        else
        {
            detailText = GetString("Online.Error.GenericDetail");
        }

        _notificationService.ShowError(GetString(titleKey), detailText, NotificationDurations.Long);
    }

    private string GetString(string key) => _dependencies?.LocalizationService?.GetString(key) ?? key;

    private string GetString(string key, string arg) =>
        _dependencies?.LocalizationService?.GetString(key, arg) ?? $"{key} ({arg})";

    private string GetString(string key, string first, string second) =>
        _dependencies?.LocalizationService?.GetString(key, first, second) ?? $"{key} ({first}, {second})";

    private string GetString(string key, string first, string second, string third) =>
        _dependencies?.LocalizationService?.GetString(key, first, second, third) ?? $"{key} ({first}, {second}, {third})";

    private string BuildPlayerDetailText(GeneralsOnlinePlayerStats stats)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var games = stats.TotalGames.ToString(culture);
        var winRate = stats.TotalGames > 0 ? stats.TotalWins * 100 / stats.TotalGames : 0;
        if (stats.MonthlyEloRating > 0)
        {
            return GetString(
                "Online.GeneralsOnline.Stats.YourRecordDetailMonthly",
                games,
                winRate.ToString(culture),
                stats.MonthlyEloRating.ToString(culture));
        }

        return GetString("Online.GeneralsOnline.Stats.YourRecordDetail", games, winRate.ToString(culture));
    }

    private async Task LoadSelectedLobbyProfilesAsync(GeneralsOnlineLobby? lobby, CancellationToken cancellationToken)
    {
        CancellationTokenSource? oldCts = null;
        CancellationTokenSource? newCts = null;
        lock (_debounceLock)
        {
            if (_disposed)
            {
                return;
            }

            oldCts = _selectedDetailCts;
            _selectedDetailCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            newCts = _selectedDetailCts;
        }

        if (oldCts is not null)
        {
            await oldCts.CancelAsync();
            oldCts.Dispose();
        }

        var token = newCts?.Token ?? cancellationToken;

        if (lobby is null || _dependencies?.ProfileManager is null)
        {
            ClearSelectedLobbyProfilesUi(lobby?.LobbyId);
            return;
        }

        try
        {
            var rank = await _compatibilityService.RankProfilesAsync(lobby, token);
            if (!rank.Success || rank.Data is null)
            {
                ClearSelectedLobbyProfilesUi(lobby.LobbyId);
                return;
            }

            ApplyRankedProfilesUi(lobby.LobbyId, rank.Data);
        }
        catch (OperationCanceledException)
        {
            // Selection moved on; a newer load supersedes this one.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Compatibility ranking failed for lobby {LobbyId}.", lobby.LobbyId);
            ClearSelectedLobbyProfilesUi(lobby.LobbyId);
        }
    }

    private void ClearSelectedLobbyProfilesUi(long? lobbyId = null)
    {
        RunOnUi(() =>
        {
            if (lobbyId.HasValue && SelectedLobby?.LobbyId != lobbyId.Value)
            {
                return;
            }

            RankedProfilesForSelected.Clear();
            SelectedLaunchProfile = null;
            OnPropertyChanged(nameof(SelectedLobbyLatency));
            OnPropertyChanged(nameof(SelectedLobbyCrcText));
            OnPropertyChanged(nameof(SelectedProfileCrcText));
        });
    }

    private void ApplyRankedProfilesUi(long lobbyId, IReadOnlyList<GeneralsOnlineProfileMatch> rankedProfiles)
    {
        RunOnUi(() =>
        {
            if (SelectedLobby?.LobbyId != lobbyId)
            {
                return;
            }

            RankedProfilesForSelected.Clear();
            foreach (var match in rankedProfiles)
            {
                RankedProfilesForSelected.Add(match);
            }

            SelectedLaunchProfile = DetermineSelectedLaunchProfile(rankedProfiles, SelectedLaunchProfile);

            if (SelectedLaunchProfile is not null)
            {
                _bestMatchByLobby[lobbyId] = rankedProfiles.FirstOrDefault(m => m.Compatibility == GeneralsOnlineCompatibility.Compatible)
                    ?? rankedProfiles[0];
            }

            OnPropertyChanged(nameof(SelectedLobbyCompatibility));
            OnPropertyChanged(nameof(SelectedLobbyBestProfileName));
            OnPropertyChanged(nameof(SelectedLobbyLatency));
            OnPropertyChanged(nameof(SelectedLobbyCrcText));
            OnPropertyChanged(nameof(SelectedProfileCrcText));
        });
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private GeneralsOnlineProfileMatch? DetermineSelectedLaunchProfile(
        IReadOnlyList<GeneralsOnlineProfileMatch> rankedProfiles,
        GeneralsOnlineProfileMatch? currentSelection)
    {
        var compatible = rankedProfiles.Where(m => m.Compatibility == GeneralsOnlineCompatibility.Compatible).ToList();
        if (compatible.Count == 1)
        {
            return compatible[0];
        }

        if (compatible.Count > 1)
        {
            return currentSelection is not null
                && compatible.Any(m => string.Equals(m.ProfileId, currentSelection.ProfileId, StringComparison.Ordinal))
                ? currentSelection
                : compatible[0];
        }

        return rankedProfiles.FirstOrDefault();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnCompatibleOnlyChanged(bool value) => ApplyFilter();

    partial void OnShowInProgressChanged(bool value) => ApplyFilter();

    partial void OnHideFullChanged(bool value) => ApplyFilter();

    partial void OnHidePasswordedChanged(bool value) => ApplyFilter();

    partial void OnSelectedRegionChanged(string value)
    {
        if (value is null)
        {
            SelectedRegion = "All";
            return;
        }

        ApplyFilter();
    }

    partial void OnSelectedSortChanged(string value)
    {
        if (value is null)
        {
            SelectedSort = "Players";
            return;
        }

        ApplyFilter();
    }

    partial void OnSelectedLobbyChanged(GeneralsOnlineLobby? value)
    {
        OnPropertyChanged(nameof(SelectedLobbyCompatibility));
        OnPropertyChanged(nameof(SelectedLobbyBestProfileName));
        OnPropertyChanged(nameof(SelectedLobbyLatency));
        OnPropertyChanged(nameof(SelectedLobbyCrcText));
        RefreshSelectedLobbyOccupants();

        // Background refreshes swap the instance while the id stays the same;
        // only a different lobby resets the setup picker and re-ranks.
        var lobbyId = value?.LobbyId;
        if (lobbyId == _detailLobbyId)
        {
            return;
        }

        _detailLobbyId = lobbyId;
        SelectedLaunchProfile = null;
        RankedProfilesForSelected.Clear();
        OnPropertyChanged(nameof(SelectedProfileCrcText));
        _ = LoadSelectedLobbyProfilesAsync(value, CancellationToken.None);
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates instance ObservableCollection based on SelectedLobby.")]
    private void RefreshSelectedLobbyOccupants()
    {
        SelectedLobbyOccupants.Clear();
        if (SelectedLobby is null)
        {
            return;
        }

        // Open and closed slots carry no useful row; the player count labels
        // already convey capacity, so list occupants only.
        foreach (var member in SelectedLobby.Members)
        {
            if (member.IsPlayer || member.IsAi)
            {
                SelectedLobbyOccupants.Add(member);
            }
        }
    }

    partial void OnSelectedLaunchProfileChanged(GeneralsOnlineProfileMatch? value)
    {
        OnPropertyChanged(nameof(SelectedLobbyCompatibility));
        OnPropertyChanged(nameof(SelectedLobbyBestProfileName));
        OnPropertyChanged(nameof(SelectedProfileCrcText));
    }

    partial void OnVisibleLobbiesChanged(ObservableCollection<GeneralsOnlineLobby> value) =>
        OnPropertyChanged(nameof(HasLobbies));

    partial void OnFriendsChanged(ObservableCollection<GeneralsOnlineFriend> value) =>
        OnPropertyChanged(nameof(HasFriends));

    partial void OnPendingRequestsChanged(ObservableCollection<GeneralsOnlineFriend> value) =>
        OnPropertyChanged(nameof(HasFriends));

    partial void OnRoomChatMessagesChanged(ObservableCollection<GeneralsOnlineRoomChatMessage> value) =>
        OnPropertyChanged(nameof(HasRoomChat));

    partial void OnBlockedUsersChanged(ObservableCollection<GeneralsOnlineFriend> value) =>
        OnPropertyChanged(nameof(HasBlocked));

    partial void OnPublicLobbyCountChanged(int value) =>
        OnPropertyChanged(nameof(TotalLobbiesText));

    partial void OnChatRoomChanged(GeneralsOnlineRoom? value)
    {
        var roomChanged = value?.Id != _appliedChatRoomId;
        if (roomChanged)
        {
            RoomChatMessages.Clear();
            OnPropertyChanged(nameof(HasRoomChat));
        }

        if (value is null || _disposed)
        {
            return;
        }

        _ = ApplyChatRoomAsync(value, CancellationToken.None);
    }

    partial void OnDmFriendChanged(GeneralsOnlineFriend? value)
    {
        LoadDmThread(value?.UserId ?? -1);
    }
}
