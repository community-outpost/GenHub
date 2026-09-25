using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Interfaces.Tools.Checksum;
using GenHub.Core.Messages;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Online.ViewModels;

/// <summary>
/// ViewModel for the Online tab: browse networks, create lobbies, join them,
/// and share one virtual LAN for in-game LAN lobbies. Anyone can join any
/// lobby; profile match state only tells members whose setup fits the game.
/// </summary>
public sealed partial class OnlineViewModel : ViewModelBase,
    IDisposable,
    IRecipient<ProfileCreatedMessage>,
    IRecipient<ProfileUpdatedMessage>,
    IRecipient<ProfileDeletedMessage>,
    IRecipient<ProfileListUpdatedMessage>
{
    private sealed record OnlineProfileSetup(
        string Fingerprint,
        string ClientKey,
        IReadOnlyList<string> GameplayContentIds);

    private readonly record struct ProfileCandidate(
        GameProfile Profile,
        OnlineProfileSetup Setup,
        bool IsExactFingerprintMatch);

    private const int SearchDebounceMs = 350;
    private const string CreateErrorTitleKey = "Online.Error.CreateTitle";

    private readonly IOnlineNetworkService _networkService;
    private readonly IOnlineLaunchService _launchService;
    private readonly IGameProfileManager _profileManager;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<OnlineViewModel> _logger;
    private readonly OnlineViewModelDependencies? _dependencies;
    private readonly IGameCrcCalculatorService? _crcCalculator;
    private readonly IGameInstallationService? _installationService;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SemaphoreSlim _profileLock = new(1, 1);
    private readonly Dictionary<string, IReadOnlyDictionary<string, ContentType>> _contentTypeCache = new(StringComparer.Ordinal);
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _nicknameCts;
    private CancellationTokenSource? _detailCts;
    private bool _disposed;
    private bool _joinInFlight;
    private bool _profilesLoaded;
    private bool _syncingPlayProfile;
    private string? _launchedProfileId;
    private IReadOnlyList<string> _expectedContentIds = [];

    /// <summary>
    /// Gets or sets the debounce delay in milliseconds for nickname edits.
    /// </summary>
    internal int NicknameDebounceMs { get; set; } = 350;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnlineViewModel"/> class.
    /// </summary>
    /// <param name="networkService">The online network service.</param>
    /// <param name="launchService">The online launch service.</param>
    /// <param name="profileManager">The game profile manager.</param>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="dialogService">The dialog service.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dependencies">Optional view model dependencies.</param>
    public OnlineViewModel(
        IOnlineNetworkService networkService,
        IOnlineLaunchService launchService,
        IGameProfileManager profileManager,
        INotificationService notificationService,
        IDialogService dialogService,
        ILogger<OnlineViewModel> logger,
        OnlineViewModelDependencies? dependencies = null)
    {
        _networkService = networkService ?? throw new ArgumentNullException(nameof(networkService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dependencies = dependencies;
        _crcCalculator = dependencies?.CrcCalculator;
        _installationService = dependencies?.GameInstallationService;

        WeakReferenceMessenger.Default.Register<ProfileCreatedMessage>(this);
        WeakReferenceMessenger.Default.Register<ProfileUpdatedMessage>(this);
        WeakReferenceMessenger.Default.Register<ProfileDeletedMessage>(this);
        WeakReferenceMessenger.Default.Register<ProfileListUpdatedMessage>(this);
    }

    [ObservableProperty]
    private ObservableCollection<OnlineNetworkSummary> _networks = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JoinRequiresPassword))]
    private OnlineNetworkSummary? _selectedNetwork;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailVisible))]
    [NotifyPropertyChangedFor(nameof(JoinRequiresPassword))]
    private OnlineNetworkDetail? _selectedDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailVisible))]
    private bool _detailLoading;

    [ObservableProperty]
    private string _expectedProfileName = string.Empty;

    [ObservableProperty]
    private string _expectedProfileFingerprint = string.Empty;

    [ObservableProperty]
    private string _expectedGameClientId = string.Empty;

    [ObservableProperty]
    private OnlineProfileMatch _profileMatchState = OnlineProfileMatch.Unknown;

    [ObservableProperty]
    private string? _profileMatchDetail;

    [ObservableProperty]
    private bool _isLobbyOnly;

    [ObservableProperty]
    private string? _adapterStatusTooltip;

    [ObservableProperty]
    private ObservableCollection<GameProfile> _availableProfiles = [];

    [ObservableProperty]
    private GameProfile? _selectedCreateProfile;

    [ObservableProperty]
    private GameProfile? _selectedHostProfile;

    [ObservableProperty]
    private GameProfile? _selectedPlayProfile;

    [ObservableProperty]
    private ObservableCollection<OnlineMember> _members = [];

    [ObservableProperty]
    private OnlineMember? _selectedMember;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Directory-only refresh indicator. The network-list spinner binds to
    /// this instead of the shared operation flag so joining, playing, or
    /// moderating never looks like a directory refresh.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshingDirectory;

    [ObservableProperty]
    private bool _directoryEmpty;

    [ObservableProperty]
    private bool _directoryFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailVisible))]
    private bool _isJoined;

    [ObservableProperty]
    private bool _isGameRunning;

    [ObservableProperty]
    private bool _isCurrentUserHost;

    [ObservableProperty]
    private string _currentNetworkName = string.Empty;

    [ObservableProperty]
    private string _expectedProfileId = string.Empty;

    [ObservableProperty]
    private string _overlayIp = string.Empty;

    [ObservableProperty]
    private OnlineConnectionQuality _connectionQuality = OnlineConnectionQuality.Unknown;

    /// <summary>
    /// The player's LAN nickname, synced into the launched game's Network.ini
    /// on Play. Blank leaves the game's stored name untouched.
    /// </summary>
    [ObservableProperty]
    private string _nickname = string.Empty;

    [ObservableProperty]
    private string _createName = string.Empty;

    [ObservableProperty]
    private string _createDescription = string.Empty;

    [ObservableProperty]
    private string _createPassword = string.Empty;

    [ObservableProperty]
    private string _joinPassword = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the currently selected network requires a password.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool JoinRequiresPassword => SelectedDetail?.RequiresPassword ?? SelectedNetwork?.RequiresPassword ?? false;

    [ObservableProperty]
    private int _createSlots = OnlineConstants.DefaultSlotCap;

    [ObservableProperty]
    private bool _createIsPublic = true;

    [ObservableProperty]
    private bool _isCreatePanelOpen;

    [ObservableProperty]
    private string _hostDescription = string.Empty;

    [ObservableProperty]
    private bool _isHostPanelOpen;

    /// <summary>
    /// Gets a value indicating whether the detail card is visible: a loaded
    /// detail, or the loading state while one is being fetched. Hidden while
    /// joined so browsing the directory cannot clobber the live lobby state;
    /// the joined banner carries the lobby game instead.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; bound from XAML as an instance property.")]
    public bool IsDetailVisible => !IsJoined && (SelectedDetail is not null || DetailLoading);

    /// <summary>
    /// Initializes the view model by subscribing to roster updates.
    /// </summary>
    public void Initialize()
    {
        _networkService.RosterChanged += OnRosterChanged;
        _networkService.ConnectionLost += OnConnectionLost;
        _networkService.ExpectedProfileChanged += OnExpectedProfileChanged;
        if (!WeakReferenceMessenger.Default.IsRegistered<ProfileCreatedMessage>(this))
        {
            WeakReferenceMessenger.Default.Register<ProfileCreatedMessage>(this);
        }

        if (!WeakReferenceMessenger.Default.IsRegistered<ProfileUpdatedMessage>(this))
        {
            WeakReferenceMessenger.Default.Register<ProfileUpdatedMessage>(this);
        }

        if (!WeakReferenceMessenger.Default.IsRegistered<ProfileDeletedMessage>(this))
        {
            WeakReferenceMessenger.Default.Register<ProfileDeletedMessage>(this);
        }

        if (!WeakReferenceMessenger.Default.IsRegistered<ProfileListUpdatedMessage>(this))
        {
            WeakReferenceMessenger.Default.Register<ProfileListUpdatedMessage>(this);
        }

        Nickname = LanNicknameCodec.Normalize(_dependencies?.UserSettingsService?.Get().OnlineNickname ?? string.Empty);
    }

    /// <summary>
    /// Refreshes the public network directory.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task RefreshNetworksAsync(CancellationToken cancellationToken = default)
    {
        if (!OnlineConstants.IsOnlineEnabled || _disposed)
        {
            return;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            IsLoading = true;
            IsRefreshingDirectory = true;
            DirectoryFailed = false;
            var result = await _networkService.GetNetworksAsync(SearchText, cancellationToken);
            if (!result.Success)
            {
                DirectoryFailed = true;
                ShowErrorToast("Online.Error.DirectoryTitle", result.Errors.FirstOrDefault());
                return;
            }

            Networks = new ObservableCollection<OnlineNetworkSummary>(result.Data);
            DirectoryEmpty = Networks.Count == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh online directory.");
            DirectoryFailed = true;
            ShowErrorToast("Online.Error.DirectoryTitle", null);
        }
        finally
        {
            IsLoading = false;
            IsRefreshingDirectory = false;
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Joins the selected network. Lobbies are open: anyone can join, and the
    /// profile setup only advertises match state to the roster.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(CanJoin))]
    public async Task JoinNetworkAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedNetwork is null)
        {
            return;
        }

        if (IsJoined)
        {
            return;
        }

        // The Join button stays enabled for the whole round-trip; a second
        // activation while one is in flight would mint a second membership
        // the client never tracks.
        if (!OnlineConstants.IsOnlineEnabled || _joinInFlight)
        {
            return;
        }

        // Snapshot before the first await: the list selection and password box
        // stay live during the round-trip, and joining whatever is selected
        // when the awaits complete could target the wrong lobby.
        var target = SelectedNetwork;
        _joinInFlight = true;
        try
        {
            IsLoading = true;
            var advertisement = await ResolveAdvertisementAsync(cancellationToken);

            var result = await _networkService.JoinNetworkAsync(
                target.Id, JoinPassword.Trim(), true, advertisement.Fingerprint, advertisement.Name, Nickname, cancellationToken);
            if (!result.Success)
            {
                ShowJoinErrorToast(result.Errors.FirstOrDefault());
                return;
            }

            JoinPassword = string.Empty;
            await ApplyJoinAsync(result.Data, target.Name, cancellationToken);
            _notificationService.ShowSuccess(
                GetString("Online.Join.SuccessTitle"),
                GetString("Online.Join.SuccessMessage", result.Data.OverlayIp),
                NotificationDurations.Long);
            NotifyAdapterState();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join online network.");
            ShowErrorToast("Online.Error.JoinTitle", null);
        }
        finally
        {
            IsLoading = false;
            _joinInFlight = false;
        }
    }

    /// <summary>
    /// Leaves the currently joined network.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(IsJoined))]
    public async Task LeaveNetworkAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IsLoading = true;
            var result = await _networkService.LeaveNetworkAsync(cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.Error.LeaveTitle", result.Errors.FirstOrDefault());
                return;
            }

            ClearJoin();
            _notificationService.ShowInfo(
                GetString("Online.Leave.SuccessTitle"),
                GetString("Online.Leave.SuccessMessage"),
                NotificationDurations.Medium);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave online network.");
            ShowErrorToast("Online.Error.LeaveTitle", null);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Creates a new network and joins it as host.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task CreateNetworkAsync(CancellationToken cancellationToken = default)
    {
        if (!OnlineConstants.IsOnlineEnabled)
        {
            return;
        }

        // Creating while joined would orphan the active network: the second
        // bring-up fails and its teardown kills the live sidecar.
        if (IsJoined)
        {
            ShowErrorToast(CreateErrorTitleKey, GetString("Online.Error.AlreadyJoined"));
            return;
        }

        var trimmedName = CreateName.Trim();
        if (trimmedName.Length < OnlineConstants.MinNetworkNameLength ||
            trimmedName.Length > OnlineConstants.MaxNetworkNameLength)
        {
            ShowErrorToast(CreateErrorTitleKey, GetString("Online.Error.NameLength"));
            return;
        }

        // Every lobby carries a game setup so members can verify their own
        // profile against it; lobbies without one cannot show match state.
        if (SelectedCreateProfile is null)
        {
            ShowErrorToast(CreateErrorTitleKey, GetString("Online.Error.ProfileRequired"));
            return;
        }

        // Shares the join guard: creating while a join is in flight would
        // mint two memberships the client cannot track.
        if (_joinInFlight)
        {
            return;
        }

        // Snapshot before the first await: the create panel stays editable
        // during profile resolution, and the request must describe what the
        // user submitted, not what the fields hold when awaits complete.
        var networkName = CreateName.Trim();
        var description = CreateDescription.Trim();
        var password = CreatePassword.Trim();
        var createProfile = SelectedCreateProfile;
        var slotsMax = Math.Clamp(CreateSlots, 2, OnlineConstants.MaxSlotCap);
        var isPublic = CreateIsPublic;
        _joinInFlight = true;
        try
        {
            IsLoading = true;
            await EnsureProfilesLoadedAsync(cancellationToken);
            var setup = await DescribeProfileAsync(createProfile, cancellationToken, includeCompatibilityCrcs: true);
            var request = new OnlineCreateNetworkRequest
            {
                Name = networkName,
                Password = password,
                SlotsMax = slotsMax,
                IsPublic = isPublic,
                Description = description,
                PreferRelay = true,
                ExpectedProfileId = createProfile?.Id ?? string.Empty,
                ExpectedProfileFingerprint = setup.Fingerprint,
                ExpectedProfileName = createProfile?.Name ?? string.Empty,
                ExpectedGameClientId = setup.ClientKey,
                ExpectedContentIds = setup.GameplayContentIds,
                ProfileFingerprint = setup.Fingerprint,
                ProfileName = createProfile?.Name ?? string.Empty,
                DisplayName = Nickname,
            };

            var result = await _networkService.CreateNetworkAsync(request, cancellationToken);
            if (!result.Success)
            {
                ShowCreateErrorToast(result.Errors.FirstOrDefault());
                return;
            }

            SetPlayProfile(createProfile);
            await ApplyJoinAsync(result.Data, networkName, cancellationToken);
            CreateName = string.Empty;
            CreateDescription = string.Empty;
            CreatePassword = string.Empty;
            IsCreatePanelOpen = false;
            _notificationService.ShowSuccess(
                GetString("Online.Create.SuccessTitle"),
                GetString("Online.Create.SuccessMessage", result.Data.OverlayIp),
                NotificationDurations.Long);
            NotifyAdapterState();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create online network.");
            ShowErrorToast(CreateErrorTitleKey, null);
        }
        finally
        {
            IsLoading = false;
            _joinInFlight = false;
        }
    }

    /// <summary>
    /// Launches the matched local profile for the joined network, preselecting
    /// the lobby IP so no manual adapter choice is needed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(CanPlay))]
    public async Task PlayAsync(CancellationToken cancellationToken = default)
    {
        if (!OnlineConstants.IsOnlineEnabled)
        {
            return;
        }

        try
        {
            IsLoading = true;
            var profileId = SelectedPlayProfile?.Id;
            if (string.IsNullOrEmpty(profileId))
            {
                profileId = await ResolveHostProfileIdAsync(cancellationToken);
            }

            if (string.IsNullOrEmpty(profileId))
            {
                _notificationService.ShowWarning(
                    GetString("Online.Play.NoProfileTitle"),
                    GetString("Online.Play.NoProfileMessage"),
                    NotificationDurations.Long);
                return;
            }

            // Launching without tunneling only produces a game that cannot see
            // the lobby, so stop here with the concrete adapter diagnosis.
            if (_networkService.AdapterState != OnlineAdapterState.Up)
            {
                ShowNoTunnelToast();
                return;
            }

            await WarnOnUnreachableMeshAsync(cancellationToken);

            _notificationService.ShowInfo(
                GetString("Online.Play.LaunchingTitle"),
                GetString("Online.Play.LaunchingMessage", CurrentNetworkName),
                NotificationDurations.Medium);

            var result = await _launchService.PlayAsync(profileId, CurrentNetworkName, OverlayIp, Nickname, cancellationToken);
            if (!result.Success)
            {
                if (result.Errors.Any(e => e == OnlineConstants.ErrorProfileMissing))
                {
                    _notificationService.ShowWarning(
                        GetString("Online.Play.NoProfileTitle"),
                        GetString("Online.Play.NoProfileMessage"),
                        NotificationDurations.Long);
                    return;
                }

                ShowErrorToast("Online.Error.LaunchTitle", GetString("Online.Error.LaunchFailed"));
                return;
            }

            _launchedProfileId = profileId;
            IsGameRunning = true;
            _notificationService.ShowSuccess(
                GetString("Online.Play.SuccessTitle"),
                GetString("Online.Play.SuccessMessage", CurrentNetworkName),
                NotificationDurations.Long);
            NotifyAdapterState();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch from the Online tab.");
            ShowErrorToast("Online.Error.LaunchTitle", null);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Stops the game launched from the Online tab.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(IsGameRunning))]
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var profileId = _launchedProfileId ?? SelectedPlayProfile?.Id;
        if (string.IsNullOrEmpty(profileId))
        {
            _notificationService.ShowWarning(
                GetString("Online.Play.NoProfileTitle"),
                GetString("Online.Play.NoProfileMessage"),
                NotificationDurations.Long);
            return;
        }

        try
        {
            IsLoading = true;
            var result = await _launchService.StopAsync(profileId, cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.Error.StopTitle", GetString("Online.Error.StopFailed"));
                return;
            }

            _launchedProfileId = null;
            IsGameRunning = false;
            _notificationService.ShowSuccess(
                GetString("Online.Stop.SuccessTitle"),
                GetString("Online.Stop.SuccessMessage", CurrentNetworkName),
                NotificationDurations.Medium);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop the game from the Online tab.");
            ShowErrorToast("Online.Error.StopTitle", null);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Copies the overlay IP address to the clipboard.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(IsJoined))]
    public async Task CopyOverlayIpAsync()
    {
        try
        {
            var topLevel = GetMainWindowTopLevel();
            if (topLevel?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(OverlayIp);
                _notificationService.ShowSuccess(
                    GetString("Online.Copy.SuccessTitle"),
                    GetString("Online.Copy.SuccessMessage", OverlayIp),
                    NotificationDurations.Short);
            }
            else
            {
                _notificationService.ShowError(
                    GetString("Online.Error.CopyTitle"),
                    GetString("Online.Error.CopyUnavailable"),
                    NotificationDurations.Medium);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy overlay IP.");
            _notificationService.ShowError(
                GetString("Online.Error.CopyTitle"),
                GetString("Online.Error.CopyUnavailable"),
                NotificationDurations.Medium);
        }
    }

    /// <summary>
    /// Reports the selected member for abuse, offering reason choices.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(CanModerate))]
    public async Task ReportMemberAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMember is null)
        {
            return;
        }

        var reason = await PickReportReasonAsync(SelectedMember.DisplayName);
        if (string.IsNullOrEmpty(reason))
        {
            return;
        }

        try
        {
            IsLoading = true;
            var result = await _networkService.ReportMemberAsync(SelectedMember.OverlayIp, reason, cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.Error.ReportTitle", GetString("Online.Error.ReportFailed"));
                return;
            }

            _notificationService.ShowSuccess(
                GetString("Online.Report.SuccessTitle"),
                GetString("Online.Report.SuccessMessage", SelectedMember.DisplayName),
                NotificationDurations.Medium);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report online member.");
            ShowErrorToast("Online.Error.ReportTitle", null);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Bans the selected member after confirmation (host only).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(CanBan))]
    public async Task BanMemberAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMember is null)
        {
            return;
        }

        var confirmed = await _dialogService.ShowConfirmationAsync(
            GetString("Online.Ban.ConfirmTitle"),
            GetString("Online.Ban.ConfirmMessage", SelectedMember.DisplayName),
            GetString("Online.Action.Ban"),
            GetString("Common.Button.Cancel"));
        if (!confirmed)
        {
            return;
        }

        try
        {
            IsLoading = true;
            var result = await _networkService.BanMemberAsync(SelectedMember.OverlayIp, cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.Error.BanTitle", GetString("Online.Error.BanFailed"));
                return;
            }

            SelectedMember = null;
            _notificationService.ShowSuccess(
                GetString("Online.Ban.SuccessTitle"),
                GetString("Online.Ban.SuccessMessage"),
                NotificationDurations.Medium);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ban online member.");
            ShowErrorToast("Online.Error.BanTitle", null);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Saves the host network settings (description and expected profile).
    /// Members are notified about profile switches over presence.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(IsCurrentUserHost))]
    public async Task SaveNetworkAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IsLoading = true;

            // The lobby always carries a game setup so members can verify
            // their own profile against it; clearing it is not allowed.
            if (SelectedHostProfile is null)
            {
                ShowErrorToast("Online.Error.UpdateTitle", GetString("Online.Error.ProfileRequired"));
                return;
            }

            await EnsureProfilesLoadedAsync(cancellationToken);
            var setup = await DescribeProfileAsync(SelectedHostProfile, cancellationToken, includeCompatibilityCrcs: true);
            var expected = new OnlineExpectedProfile
            {
                ExpectedProfileId = SelectedHostProfile?.Id ?? string.Empty,
                ExpectedProfileFingerprint = setup.Fingerprint,
                ExpectedProfileName = SelectedHostProfile?.Name ?? string.Empty,
                ExpectedGameClientId = setup.ClientKey,
                ExpectedContentIds = setup.GameplayContentIds,
            };
            var result = await _networkService.UpdateNetworkAsync(HostDescription, expected, cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.Error.UpdateTitle", GetString("Online.Error.UpdateFailed"));
                return;
            }

            ApplyExpectedProfile(expected);
            SetPlayProfile(SelectedHostProfile);
            await AdvertiseSelectedProfileAsync(cancellationToken);
            IsHostPanelOpen = false;
            _notificationService.ShowSuccess(
                GetString("Online.Host.SuccessTitle"),
                GetString("Online.Host.SuccessMessage"),
                NotificationDurations.Medium);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update online network.");
            ShowErrorToast("Online.Error.UpdateTitle", null);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Toggles the create-network panel, loading the profile picker entries.
    /// </summary>
    [RelayCommand]
    public void ToggleCreatePanel()
    {
        IsCreatePanelOpen = !IsCreatePanelOpen;
        if (IsCreatePanelOpen)
        {
            // Safe to detach: the loader reports its own errors, and the
            // panel outlives any scoped token, so loading is uncancellable.
            _ = EnsureProfilesLoadedAsync(CancellationToken.None, forceReload: true);
        }
    }

    /// <summary>
    /// Toggles the host settings panel.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsCurrentUserHost))]
    public void ToggleHostPanel()
    {
        IsHostPanelOpen = !IsHostPanelOpen;
        if (IsHostPanelOpen)
        {
            _ = EnsureProfilesLoadedAsync(CancellationToken.None, forceReload: true);
        }
    }

    /// <summary>
    /// Receives game profile creation notifications so newly added profiles appear
    /// in the profile picker dropdowns immediately.
    /// </summary>
    /// <param name="message">The profile created message.</param>
    public void Receive(ProfileCreatedMessage message)
    {
        if (_disposed || message.Profile.IsToolProfile)
        {
            return;
        }

        RunOnUi(() =>
        {
            if (!_profilesLoaded)
            {
                return;
            }

            var createdProfile = message.Profile;
            var existing = AvailableProfiles.FirstOrDefault(p => string.Equals(p.Id, createdProfile.Id, StringComparison.Ordinal));
            if (existing is not null)
            {
                var idx = AvailableProfiles.IndexOf(existing);
                AvailableProfiles[idx] = createdProfile;
            }
            else
            {
                InsertProfileSorted(createdProfile);
            }
        });
    }

    /// <summary>
    /// Receives game profile updates so an edited play or hosted profile
    /// re-matches and re-advertises while the lobby stays open, and updates
    /// the profile picker dropdown entries.
    /// </summary>
    /// <param name="message">The updated profile.</param>
    public void Receive(ProfileUpdatedMessage message)
    {
        if (_disposed)
        {
            return;
        }

        // Catalog or content changes can reclassify gameplay ids; drop the
        // cached map so the next fingerprint reflects the edited profile.
        _contentTypeCache.Clear();

        var updatedProfile = message.Profile;
        var updatedId = updatedProfile.Id;

        RunOnUi(() =>
        {
            if (_profilesLoaded)
            {
                UpdateAvailableProfilesList(updatedProfile);
                UpdateSelectedProfileReferences(updatedProfile);
            }

            var isPlayProfile = string.Equals(SelectedPlayProfile?.Id, updatedId, StringComparison.Ordinal);
            var isHostProfile = IsCurrentUserHost && string.Equals(SelectedHostProfile?.Id, updatedId, StringComparison.Ordinal);
            if ((!isPlayProfile && !isHostProfile) || !IsJoined)
            {
                return;
            }

            // Safe to detach: the refresh reports its own errors, and profile
            // saves outlive any scoped token, so matching is uncancellable.
            _ = RefreshAfterProfileUpdateAsync(isHostProfile);
        });
    }

    /// <summary>
    /// Receives game profile deletion notifications so removed profiles are
    /// pruned from the available profile list immediately.
    /// </summary>
    /// <param name="message">The deleted profile notice.</param>
    public void Receive(ProfileDeletedMessage message)
    {
        if (_disposed)
        {
            return;
        }

        var deletedId = message.ProfileId;
        RunOnUi(() =>
        {
            if (_profilesLoaded)
            {
                var existing = AvailableProfiles.FirstOrDefault(p => string.Equals(p.Id, deletedId, StringComparison.Ordinal));
                if (existing is not null)
                {
                    AvailableProfiles.Remove(existing);
                }
            }

            if (string.Equals(SelectedCreateProfile?.Id, deletedId, StringComparison.Ordinal))
            {
                SelectedCreateProfile = null;
            }

            if (string.Equals(SelectedPlayProfile?.Id, deletedId, StringComparison.Ordinal))
            {
                SelectedPlayProfile = null;
            }

            if (string.Equals(SelectedHostProfile?.Id, deletedId, StringComparison.Ordinal))
            {
                SelectedHostProfile = null;
            }
        });
    }

    /// <summary>
    /// Receives bulk game profile list updates (such as imports or restores)
    /// and refetches all available profiles.
    /// </summary>
    /// <param name="message">The profile list update notification.</param>
    public void Receive(ProfileListUpdatedMessage message)
    {
        if (_disposed)
        {
            return;
        }

        _contentTypeCache.Clear();

        if (_profilesLoaded)
        {
            _ = EnsureProfilesLoadedAsync(CancellationToken.None, forceReload: true);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WeakReferenceMessenger.Default.Unregister<ProfileCreatedMessage>(this);
        WeakReferenceMessenger.Default.Unregister<ProfileUpdatedMessage>(this);
        WeakReferenceMessenger.Default.Unregister<ProfileDeletedMessage>(this);
        WeakReferenceMessenger.Default.Unregister<ProfileListUpdatedMessage>(this);
        _networkService.RosterChanged -= OnRosterChanged;
        _networkService.ConnectionLost -= OnConnectionLost;
        _networkService.ExpectedProfileChanged -= OnExpectedProfileChanged;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _nicknameCts?.Cancel();
        _nicknameCts?.Dispose();
        _detailCts?.Cancel();
        _detailCts?.Dispose();
        _refreshLock.Dispose();
        _profileLock.Dispose();
    }

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

    private static TopLevel? GetMainWindowTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            return TopLevel.GetTopLevel(mainWindow);
        }

        return null;
    }

    private static bool IsElevationRequired(string adapterError) =>
        adapterError.Contains(OnlineConstants.AdapterElevationRequired, StringComparison.Ordinal);

    private static (string? GameRoot, string? ExePath) ResolveProfileRootAndExe(GameProfile profile)
    {
        string? gameRoot = null;
        string? exePath = null;

        var fullExe = ReplayCrcMatchingHelper.ResolveProfileFullExePath(profile.GameClient);
        if (!string.IsNullOrEmpty(fullExe) && Path.IsPathRooted(fullExe))
        {
            exePath = fullExe;
            gameRoot = Path.GetDirectoryName(fullExe);
        }

        if ((string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) &&
            !string.IsNullOrWhiteSpace(profile.WorkingDirectory) &&
            Directory.Exists(profile.WorkingDirectory))
        {
            gameRoot = profile.WorkingDirectory;
        }

        if ((string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) &&
            !string.IsNullOrWhiteSpace(profile.GameClient?.WorkingDirectory) &&
            Directory.Exists(profile.GameClient.WorkingDirectory))
        {
            gameRoot = profile.GameClient.WorkingDirectory;
        }

        return (gameRoot, exePath);
    }

    private static string? ResolveExePathCandidate(GameProfile profile, string gameRoot)
    {
        var candidates = new List<string?>();
        if (!string.IsNullOrWhiteSpace(profile.CustomExecutablePath))
        {
            candidates.Add(Path.IsPathRooted(profile.CustomExecutablePath) ? profile.CustomExecutablePath : Path.Combine(gameRoot, profile.CustomExecutablePath));
        }

        if (!string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            candidates.Add(Path.IsPathRooted(profile.ExecutablePath) ? profile.ExecutablePath : Path.Combine(gameRoot, profile.ExecutablePath));
        }

        if (profile.GameClient != null)
        {
            if (!string.IsNullOrWhiteSpace(profile.GameClient.ExecutablePath))
            {
                candidates.Add(Path.IsPathRooted(profile.GameClient.ExecutablePath) ? profile.GameClient.ExecutablePath : Path.Combine(gameRoot, profile.GameClient.ExecutablePath));
            }

            var defaultName = ReplayCrcMatchingHelper.GetDefaultExecutableName(profile.GameClient.GameType, profile.GameClient.PublisherType);
            candidates.Add(Path.Combine(gameRoot, defaultName));
        }

        candidates.Add(Path.Combine(gameRoot, GameClientConstants.SuperHackersZeroHourExecutable));
        candidates.Add(Path.Combine(gameRoot, GameClientConstants.ZeroHourExecutable));
        candidates.Add(Path.Combine(gameRoot, GameClientConstants.GeneralsExecutable));
        candidates.Add(Path.Combine(gameRoot, GameClientConstants.SteamGameDatExecutable));

        return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c));
    }

    private static string? GetSpecificInstallationPath(GameInstallation installation, GameType? gameType)
    {
        var targetPath = gameType == GameType.Generals
            ? installation.GeneralsPath
            : installation.ZeroHourPath;

        return !string.IsNullOrWhiteSpace(targetPath) && Directory.Exists(targetPath)
            ? targetPath
            : null;
    }

    private static string? GetGenericInstallationPath(GameInstallation installation)
    {
        return !string.IsNullOrWhiteSpace(installation.InstallationPath) && Directory.Exists(installation.InstallationPath)
            ? installation.InstallationPath
            : null;
    }

    private void InsertProfileSorted(GameProfile profile)
    {
        var insertIndex = 0;
        while (insertIndex < AvailableProfiles.Count &&
               string.Compare(AvailableProfiles[insertIndex].Name, profile.Name, StringComparison.OrdinalIgnoreCase) < 0)
        {
            insertIndex++;
        }

        AvailableProfiles.Insert(insertIndex, profile);
    }

    private void UpdateAvailableProfilesList(GameProfile updatedProfile)
    {
        var existing = AvailableProfiles.FirstOrDefault(p => string.Equals(p.Id, updatedProfile.Id, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (updatedProfile.IsToolProfile)
            {
                AvailableProfiles.Remove(existing);
                return;
            }

            var oldIndex = AvailableProfiles.IndexOf(existing);
            if (!string.Equals(existing.Name, updatedProfile.Name, StringComparison.OrdinalIgnoreCase))
            {
                AvailableProfiles.RemoveAt(oldIndex);
                InsertProfileSorted(updatedProfile);
            }
            else
            {
                AvailableProfiles[oldIndex] = updatedProfile;
            }
        }
        else if (!updatedProfile.IsToolProfile)
        {
            InsertProfileSorted(updatedProfile);
        }
    }

    private void UpdateSelectedProfileReferences(GameProfile updatedProfile)
    {
        var target = updatedProfile.IsToolProfile ? null : updatedProfile;
        if (string.Equals(SelectedCreateProfile?.Id, updatedProfile.Id, StringComparison.Ordinal))
        {
            SelectedCreateProfile = target;
        }

        if (string.Equals(SelectedPlayProfile?.Id, updatedProfile.Id, StringComparison.Ordinal))
        {
            SelectedPlayProfile = target;
        }

        if (string.Equals(SelectedHostProfile?.Id, updatedProfile.Id, StringComparison.Ordinal))
        {
            SelectedHostProfile = target;
        }
    }

    private void ApplyLoadedProfiles(List<GameProfile> sorted)
    {
        RunOnUi(() =>
        {
            var selectedCreateId = SelectedCreateProfile?.Id;
            var selectedPlayId = SelectedPlayProfile?.Id;
            var selectedHostId = SelectedHostProfile?.Id;

            AvailableProfiles = new ObservableCollection<GameProfile>(sorted);
            _profilesLoaded = true;

            if (selectedCreateId != null)
            {
                SelectedCreateProfile = sorted.FirstOrDefault(p => string.Equals(p.Id, selectedCreateId, StringComparison.Ordinal));
            }

            if (selectedPlayId != null)
            {
                SelectedPlayProfile = sorted.FirstOrDefault(p => string.Equals(p.Id, selectedPlayId, StringComparison.Ordinal));
            }

            if (selectedHostId != null)
            {
                SelectedHostProfile = sorted.FirstOrDefault(p => string.Equals(p.Id, selectedHostId, StringComparison.Ordinal));
            }
        });
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; wired as an instance CanExecute predicate.")]
    private bool CanJoin() => !IsJoined && SelectedNetwork is not null;

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; wired as an instance CanExecute predicate.")]
    private bool CanModerate() => IsJoined && SelectedMember is not null && SelectedMember.OverlayIp != OverlayIp;

    private bool CanBan() => CanModerate() && IsCurrentUserHost;

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; wired as an instance CanExecute predicate.")]
    private bool CanPlay() => IsJoined && !IsGameRunning;

    private void OnConnectionLost(object? sender, EventArgs e)
    {
        RunOnUi(HandleConnectionLost);
    }

    private void HandleConnectionLost()
    {
        ClearJoin();
        _notificationService.ShowWarning(
            GetString("Online.Connection.LostTitle"),
            GetString("Online.Connection.LostMessage"),
            NotificationDurations.Long);
    }

    private void OnExpectedProfileChanged(object? sender, OnlineExpectedProfile expected)
    {
        RunOnUi(() => _ = HandleExpectedProfileChangedAsync(expected));
    }

    private async Task HandleExpectedProfileChangedAsync(OnlineExpectedProfile expected)
    {
        if (!IsJoined)
        {
            return;
        }

        // The room broadcasts to every socket including the saver, so the
        // host would toast its own save. A no-op payload skips everything.
        if (string.Equals(ExpectedProfileFingerprint, expected.ExpectedProfileFingerprint, StringComparison.Ordinal) &&
            string.Equals(ExpectedProfileId, expected.ExpectedProfileId, StringComparison.Ordinal) &&
            string.Equals(ExpectedProfileName, expected.ExpectedProfileName, StringComparison.Ordinal))
        {
            return;
        }

        ApplyExpectedProfile(expected);

        // Presence events carry no scoped token; the re-match is uncancellable.
        await UpdateMatchAndAdvertiseAsync(CancellationToken.None);
        var message = string.IsNullOrWhiteSpace(expected.ExpectedProfileName)
            ? GetString("Online.Profile.SwitchedClearedMessage")
            : GetString("Online.Profile.SwitchedMessage", expected.ExpectedProfileName);
        _notificationService.ShowInfo(
            GetString("Online.Profile.SwitchedTitle"),
            message,
            NotificationDurations.Long);
    }

    private void OnRosterChanged(object? sender, IReadOnlyList<OnlineMember> members)
    {
        RunOnUi(() => ApplyRoster(members));
    }

    private void ApplyRoster(IReadOnlyList<OnlineMember> members)
    {
        Members = new ObservableCollection<OnlineMember>(members);
        RefreshHostState();
        ReportMemberCommand.NotifyCanExecuteChanged();
        BanMemberCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplyJoinAsync(OnlineJoinResult join, string? knownName, CancellationToken cancellationToken)
    {
        IsJoined = true;
        CurrentNetworkName = Networks.FirstOrDefault(n => n.Id == join.NetworkId)?.Name ?? knownName ?? join.NetworkId;
        ApplyExpectedProfile(new OnlineExpectedProfile
        {
            ExpectedProfileId = join.ExpectedProfileId,
            ExpectedProfileFingerprint = join.ExpectedProfileFingerprint,
            ExpectedProfileName = join.ExpectedProfileName,
            ExpectedGameClientId = join.ExpectedGameClientId,
            ExpectedContentIds = join.ExpectedContentIds,
        });
        OverlayIp = join.OverlayIp;
        Members = new ObservableCollection<OnlineMember>(join.Members);
        RefreshHostState();
        JoinNetworkCommand.NotifyCanExecuteChanged();
        LeaveNetworkCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        CopyOverlayIpCommand.NotifyCanExecuteChanged();
        ReportMemberCommand.NotifyCanExecuteChanged();
        BanMemberCommand.NotifyCanExecuteChanged();
        SaveNetworkCommand.NotifyCanExecuteChanged();
        ToggleHostPanelCommand.NotifyCanExecuteChanged();

        if (IsCurrentUserHost)
        {
            SelectedHostProfile = SelectedPlayProfile;
            HostDescription = SelectedDetail?.Description ?? string.Empty;
        }

        await UpdateMatchAndAdvertiseAsync(cancellationToken);
    }

    // Only generated MVVM members are touched, so Sonar cannot see the
    // instance usage; the property sets must stay instance for bindings.
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Resets instance-bound MVVM state and refreshes instance commands.")]
    private void ClearJoin()
    {
        IsJoined = false;
        IsCurrentUserHost = false;
        CurrentNetworkName = string.Empty;
        ExpectedProfileId = string.Empty;
        ExpectedProfileName = string.Empty;
        ExpectedProfileFingerprint = string.Empty;
        ExpectedGameClientId = string.Empty;
        ProfileMatchState = OnlineProfileMatch.Unknown;
        ProfileMatchDetail = null;
        IsLobbyOnly = false;
        SetPlayProfile(null);
        SelectedHostProfile = null;
        HostDescription = string.Empty;
        IsHostPanelOpen = false;
        OverlayIp = string.Empty;
        ConnectionQuality = OnlineConnectionQuality.Unknown;
        Members = [];
        SelectedMember = null;
        JoinPassword = string.Empty;
        _networkService.SetLocalProfileAdvertisement(string.Empty, string.Empty, Nickname);
        JoinNetworkCommand.NotifyCanExecuteChanged();
        LeaveNetworkCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        CopyOverlayIpCommand.NotifyCanExecuteChanged();
        ReportMemberCommand.NotifyCanExecuteChanged();
        BanMemberCommand.NotifyCanExecuteChanged();
        SaveNetworkCommand.NotifyCanExecuteChanged();
        ToggleHostPanelCommand.NotifyCanExecuteChanged();
    }

    // Only generated MVVM members are touched, so Sonar cannot see the
    // instance usage; the property sets must stay instance for bindings.
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Derives instance-bound host state and refreshes instance commands.")]
    private void RefreshHostState()
    {
        IsCurrentUserHost = Members.Any(m => m.OverlayIp == OverlayIp && m.IsHost);
        ConnectionQuality = Members.FirstOrDefault(m => m.OverlayIp == OverlayIp)?.Quality ?? OnlineConnectionQuality.Unknown;
        SaveNetworkCommand.NotifyCanExecuteChanged();
        ToggleHostPanelCommand.NotifyCanExecuteChanged();
        BanMemberCommand.NotifyCanExecuteChanged();
    }

    private void ShowJoinErrorToast(string? errorCode)
    {
        var messageKey = errorCode switch
        {
            OnlineConstants.ErrorWrongPassword => "Online.Error.WrongPassword",
            OnlineConstants.ErrorNetworkFull => "Online.Error.NetworkFull",
            OnlineConstants.ErrorNetworkBanned => "Online.Error.NetworkBanned",
            OnlineConstants.ErrorNetworkNotFound => "Online.Error.NetworkNotFound",
            OnlineConstants.ErrorServiceUnavailable => "Online.Error.ServiceUnavailable",
            OnlineConstants.ErrorAdapterFailed => "Online.Error.AdapterFailed",
            _ => "Online.Error.JoinFailed",
        };
        ShowErrorToast("Online.Error.JoinTitle", GetString(messageKey));
    }

    private void ShowCreateErrorToast(string? errorCode)
    {
        var messageKey = errorCode switch
        {
            OnlineConstants.ErrorInvalidName => "Online.Error.NameLength",
            OnlineConstants.ErrorInvalidSlots => "Online.Error.SlotsRange",
            OnlineConstants.ErrorPasswordTooLong => "Online.Error.PasswordTooLong",
            OnlineConstants.ErrorPasswordTooShort => "Online.Error.PasswordTooShort",
            OnlineConstants.ErrorPasswordRequired => "Online.Error.PasswordRequired",
            _ => "Online.Error.CreateFailed",
        };
        ShowErrorToast(CreateErrorTitleKey, GetString(messageKey));
    }

    private void ShowErrorToast(string titleKey, string? detail)
    {
        var detailText = string.IsNullOrWhiteSpace(detail) || detail.StartsWith(OnlineConstants.ErrorCodePrefix, StringComparison.Ordinal)
            ? GetString("Online.Error.GenericDetail")
            : OnlineLogScrubber.Scrub(detail);
        _notificationService.ShowError(GetString(titleKey), detailText, NotificationDurations.Long);
    }

    private void ShowNoTunnelToast()
    {
        var adapterErr = _networkService.AdapterError;
        var detail = string.IsNullOrWhiteSpace(adapterErr)
            ? GetString("Online.Play.NoTunnelMessage")
            : $"{GetString("Online.Play.NoTunnelMessage")} {OnlineLogScrubber.Scrub(adapterErr)}";
        ShowErrorToast("Online.Play.NoTunnelTitle", detail);
    }

    private bool IsOverlayPending()
    {
        var config = _networkService.CurrentJoin?.AdapterConfig ?? string.Empty;
        return OverlayConfigInspector.TryGetOverlayName(config) == OnlineConstants.OverlayPendingSelection;
    }

    private void NotifyAdapterState()
    {
        IsLobbyOnly = _networkService.AdapterState != OnlineAdapterState.Up;
        if (!IsLobbyOnly)
        {
            AdapterStatusTooltip = null;
            return;
        }

        var adapterErr = _networkService.AdapterError;
        if (!string.IsNullOrWhiteSpace(adapterErr))
        {
            if (IsElevationRequired(adapterErr))
            {
                var elevationMessage = GetString("Online.Adapter.ElevationMessage");
                AdapterStatusTooltip = elevationMessage;
                _notificationService.ShowWarning(
                    GetString("Online.Adapter.ElevationTitle"),
                    elevationMessage,
                    NotificationDurations.Long);
                return;
            }

            AdapterStatusTooltip = adapterErr;
            _notificationService.ShowWarning(
                GetString("Online.Adapter.UnavailableTitle"),
                adapterErr,
                NotificationDurations.Long);
            return;
        }

        if (IsOverlayPending())
        {
            // Expected pre-overlay state: lobby works, tunneling waits.
            AdapterStatusTooltip = GetString("Online.Adapter.PendingMessage");
            _notificationService.ShowInfo(
                GetString("Online.Adapter.PendingTitle"),
                GetString("Online.Adapter.PendingMessage"),
                NotificationDurations.Long);
            return;
        }

        AdapterStatusTooltip = GetString("Online.Adapter.UnavailableMessage");
        _notificationService.ShowWarning(
            GetString("Online.Adapter.UnavailableTitle"),
            GetString("Online.Adapter.UnavailableMessage"),
            NotificationDurations.Long);
    }

    private async Task WarnOnUnreachableMeshAsync(CancellationToken cancellationToken)
    {
        // Advisory preflight: any mesh failure skips the warning and the
        // launch proceeds. Mixed-version peers cannot answer probes yet.
        var meshTask = _networkService.RunMeshCheckAsync(cancellationToken);
        if (meshTask is null)
        {
            return;
        }

        OperationResult<OnlineMeshCheckResult> mesh;
        try
        {
            mesh = await meshTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or TimeoutException or JsonException)
        {
            _logger.LogWarning(ex, "Mesh check skipped.");
            return;
        }

        if (mesh is null || !mesh.Success || mesh.Data is null || mesh.Data.AllReachable)
        {
            return;
        }

        _notificationService.ShowWarning(
            GetString("Online.Play.ConnectivityTitle"),
            GetString("Online.Play.ConnectivityMessage", mesh.Data.UnreachableCount, mesh.Data.Peers.Count),
            NotificationDurations.Long);
    }

    /// <summary>
    /// Clamps the nickname to the game's length limit and persists it with debouncing.
    /// </summary>
    /// <param name="value">The edited nickname.</param>
    partial void OnNicknameChanged(string value)
    {
        var normalized = LanNicknameCodec.Normalize(value);
        if (!string.Equals(normalized, value, StringComparison.Ordinal))
        {
            Nickname = normalized;
            return;
        }

        _nicknameCts?.Cancel();
        _nicknameCts?.Dispose();
        var cts = new CancellationTokenSource();
        _nicknameCts = cts;
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(NicknameDebounceMs, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                await PersistNicknameAsync(normalized).ConfigureAwait(false);

                if (IsJoined && !token.IsCancellationRequested)
                {
                    await RefreshAdvertisementAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by newer keystrokes.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to debounce nickname change.");
            }
        }, token);
    }

    private async Task PersistNicknameAsync(string nickname)
    {
        if (_dependencies?.UserSettingsService is null)
        {
            return;
        }

        await _dependencies.UserSettingsService.TryUpdateAndSaveAsync(settings =>
        {
            if (string.Equals(settings.OnlineNickname ?? string.Empty, nickname, StringComparison.Ordinal))
            {
                return false;
            }

            settings.OnlineNickname = nickname;
            return true;
        });
    }

    private string GetString(string key) => _dependencies?.LocalizationService?.GetString(key) ?? key;

    private string GetString(string key, string arg) =>
        _dependencies?.LocalizationService?.GetString(key, arg) ?? $"{key} ({arg})";

    private string GetString(string key, int first, int second) =>
        _dependencies?.LocalizationService?.GetString(key, first, second) ?? $"{key} ({first}, {second})";

    private async Task<string?> PickReportReasonAsync(string displayName)
    {
        string? reason = null;
        var reasons = new[]
        {
            GetString("Online.Report.Reason.Cheating"),
            GetString("Online.Report.Reason.Harassment"),
            GetString("Online.Report.Reason.Griefing"),
        };

        var actions = reasons.Select(text => new DialogAction
        {
            Text = text,
            Style = NotificationActionStyle.Secondary,
            Action = () => reason = text,
        }).ToList();
        actions.Add(new DialogAction
        {
            Text = GetString("Common.Button.Cancel"),
            Style = NotificationActionStyle.Secondary,
        });

        await _dialogService.ShowMessageAsync(
            GetString("Online.Report.DialogTitle"),
            GetString("Online.Report.DialogMessage", displayName),
            actions);
        return reason;
    }

    private async Task LoadDetailAsync(OnlineNetworkSummary network, CancellationToken cancellationToken)
    {
        try
        {
            DetailLoading = true;
            SelectedDetail = null;
            ApplyExpectedProfile(null);
            ProfileMatchState = OnlineProfileMatch.Unknown;
            ProfileMatchDetail = null;
            SetPlayProfile(null);

            var result = await _networkService.GetNetworkDetailAsync(network.Id, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!result.Success)
            {
                _logger.LogWarning("Failed to load details for network {NetworkId}.", network.Id);
                return;
            }

            SelectedDetail = result.Data;
            ApplyExpectedProfile(new OnlineExpectedProfile
            {
                ExpectedProfileId = result.Data.ExpectedProfileId,
                ExpectedProfileFingerprint = result.Data.ExpectedProfileFingerprint,
                ExpectedProfileName = result.Data.ExpectedProfileName,
                ExpectedGameClientId = result.Data.ExpectedGameClientId,
                ExpectedContentIds = result.Data.ExpectedContentIds,
            });
            await AutoMatchProfileAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load network detail.");
        }
        finally
        {
            // A superseded load must not hide the newer load's progress.
            if (!cancellationToken.IsCancellationRequested)
            {
                DetailLoading = false;
            }
        }
    }

    private void ApplyExpectedProfile(OnlineExpectedProfile? expected)
    {
        ExpectedProfileId = expected?.ExpectedProfileId ?? string.Empty;
        ExpectedProfileFingerprint = expected?.ExpectedProfileFingerprint ?? string.Empty;
        ExpectedProfileName = expected?.ExpectedProfileName ?? string.Empty;
        ExpectedGameClientId = expected?.ExpectedGameClientId ?? string.Empty;
        _expectedContentIds = expected?.ExpectedContentIds ?? [];
    }

    private void ApplyMatch(OnlineProfileMatch match, IReadOnlyList<string>? localContentIds)
    {
        ProfileMatchState = match;
        if (string.IsNullOrEmpty(ExpectedProfileFingerprint))
        {
            if (match == OnlineProfileMatch.Exact)
            {
                ProfileMatchDetail = null;
                return;
            }

            if (!string.IsNullOrEmpty(ExpectedProfileName) || !string.IsNullOrEmpty(ExpectedProfileId))
            {
                ProfileMatchDetail = match switch
                {
                    OnlineProfileMatch.Mismatch => GetString("Online.Detail.MatchDetailMismatch"),
                    _ => GetString("Online.Detail.MatchDetailNoProfile"),
                };
                return;
            }

            ProfileMatchDetail = GetString("Online.Detail.MatchDetailAny");
            return;
        }

        if (SelectedPlayProfile is null || localContentIds is null)
        {
            ProfileMatchDetail = GetString("Online.Detail.MatchDetailNoProfile");
            return;
        }

        ProfileMatchDetail = match switch
        {
            OnlineProfileMatch.Exact => null,
            OnlineProfileMatch.Mismatch => GetString("Online.Detail.MatchDetailMismatch"),
            _ => GetString("Online.Detail.MatchDetailNoProfile"),
        };
    }

    private void SetPlayProfile(GameProfile? profile)
    {
        // Programmatic sets must not retrigger the picker handler: the callers
        // already re-match and advertise as part of their own flow.
        _syncingPlayProfile = true;
        try
        {
            SelectedPlayProfile = profile;
        }
        finally
        {
            _syncingPlayProfile = false;
        }
    }

    private async Task RefreshMatchAfterPickerChangeAsync()
    {
        try
        {
            // Presence advertisements carry no scoped token; the re-match is uncancellable.
            await UpdateMatchAndAdvertiseAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-match the selected launch profile.");
        }
    }

    private async Task RefreshAdvertisementAsync()
    {
        try
        {
            // The heartbeat rebuilds its payload every beat, so the renamed
            // roster entry propagates within one interval without rejoining.
            await AdvertiseSelectedProfileAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-advertise the player nickname.");
        }
    }

    private async Task RefreshAfterProfileUpdateAsync(bool refreshHostExpected)
    {
        try
        {
            // Re-match the edited launch profile and re-advertise; heartbeats
            // propagate the new fingerprint within one interval.
            await UpdateMatchAndAdvertiseAsync(CancellationToken.None);
            if (refreshHostExpected)
            {
                await RefreshHostExpectedProfileAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh the lobby match after a profile update.");
        }
    }

    private async Task RefreshHostExpectedProfileAsync(CancellationToken cancellationToken)
    {
        var hostProfile = SelectedHostProfile;
        if (!IsCurrentUserHost || hostProfile is null)
        {
            return;
        }

        var setup = await DescribeProfileAsync(hostProfile, cancellationToken, includeCompatibilityCrcs: true);
        var expected = new OnlineExpectedProfile
        {
            ExpectedProfileId = hostProfile.Id,
            ExpectedProfileFingerprint = setup.Fingerprint,
            ExpectedProfileName = hostProfile.Name,
            ExpectedGameClientId = setup.ClientKey,
            ExpectedContentIds = setup.GameplayContentIds,
        };
        var result = await _networkService.UpdateNetworkAsync(HostDescription, expected, cancellationToken);
        if (!result.Success)
        {
            _logger.LogWarning("Online host setup refresh was not applied by the network service.");
            return;
        }

        // Members re-match on the profile-changed event the update broadcasts.
        ApplyExpectedProfile(expected);
    }

    private async Task AutoMatchProfileAsync(CancellationToken cancellationToken = default)
    {
        SetPlayProfile(null);
        ApplyMatch(OnlineProfileMatch.Unknown, null);
        if (string.IsNullOrEmpty(ExpectedProfileFingerprint))
        {
            await TryMatchLegacyProfileByNameOrIdAsync(cancellationToken);
            return;
        }

        try
        {
            await EnsureProfilesLoadedAsync(cancellationToken);
            var best = await FindBestProfileMatchAsync(cancellationToken);
            if (best is not null)
            {
                var candidate = best.Value;
                SetPlayProfile(candidate.Profile);
                var match = candidate.IsExactFingerprintMatch
                    ? OnlineProfileMatch.Exact
                    : OnlineProfileMatcher.Compare(
                        ExpectedProfileFingerprint,
                        ExpectedGameClientId,
                        candidate.Setup.Fingerprint,
                        OnlineProfileMatcher.GetGameClientKey(candidate.Profile));
                ApplyMatch(match, candidate.Setup.GameplayContentIds);
            }
            else if (AvailableProfiles.Count > 0)
            {
                ApplyMatch(OnlineProfileMatch.Mismatch, null);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-match a local profile.");
        }
    }

    private async Task<bool> TryMatchLegacyProfileByNameOrIdAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ExpectedProfileName) && string.IsNullOrWhiteSpace(ExpectedProfileId))
        {
            return false;
        }

        try
        {
            await EnsureProfilesLoadedAsync(cancellationToken);
            var matched = AvailableProfiles.FirstOrDefault(IsLegacyProfileMatch);
            if (matched is not null)
            {
                SetPlayProfile(matched);
                var setup = await DescribeProfileAsync(matched, cancellationToken);
                ApplyMatch(OnlineProfileMatch.Exact, setup.GameplayContentIds);
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to match profile by name or id.");
        }

        return false;
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; part of profile matching instance flow.")]
    private bool IsLegacyProfileMatch(GameProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(ExpectedProfileId) &&
            (string.Equals(profile.Id, ExpectedProfileId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(profile.Name, ExpectedProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(ExpectedProfileName) &&
               string.Equals(profile.Name, ExpectedProfileName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ProfileCandidate?> FindBestProfileMatchAsync(CancellationToken cancellationToken)
    {
        ProfileCandidate? best = null;
        var bestOverlap = -1;

        foreach (var profile in AvailableProfiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var setup = await DescribeProfileAsync(profile, cancellationToken, includeCompatibilityCrcs: true);
            if (string.Equals(setup.Fingerprint, ExpectedProfileFingerprint, StringComparison.Ordinal) ||
                (OnlineProfileMatcher.CrcConfirmsCompatible(ExpectedProfileFingerprint, setup.Fingerprint) &&
                 OnlineProfileMatcher.AreGameTypesCompatible(ExpectedGameClientId, setup.ClientKey, ExpectedProfileFingerprint, setup.Fingerprint)))
            {
                return new ProfileCandidate(profile, setup, true);
            }

            if (!IsCompatibleClient(setup.ClientKey))
            {
                continue;
            }

            var overlap = OnlineProfileMatcher.ScoreOverlap(_expectedContentIds, setup.GameplayContentIds);
            if (best is null || overlap > bestOverlap)
            {
                best = new ProfileCandidate(profile, setup, false);
                bestOverlap = overlap;
            }
        }

        return best;
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads generated MVVM properties Sonar cannot see; part of profile matching instance flow.")]
    private bool IsCompatibleClient(string? clientKey) =>
        !string.IsNullOrEmpty(clientKey) &&
        string.Equals(clientKey, ExpectedGameClientId, StringComparison.Ordinal);

    private async Task UpdateMatchAndAdvertiseAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedPlayProfile is null)
        {
            await AutoMatchProfileAsync(cancellationToken);
        }
        else
        {
            var setup = await DescribeProfileAsync(SelectedPlayProfile, cancellationToken, includeCompatibilityCrcs: true);
            ApplyMatch(
                OnlineProfileMatcher.Compare(
                    ExpectedProfileFingerprint,
                    ExpectedGameClientId,
                    setup.Fingerprint,
                    setup.ClientKey),
                setup.GameplayContentIds);
        }

        await AdvertiseSelectedProfileAsync(cancellationToken);
    }

    private async Task AdvertiseSelectedProfileAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedPlayProfile is null)
        {
            _networkService.SetLocalProfileAdvertisement(string.Empty, string.Empty, Nickname);
            return;
        }

        // Same map-aware fingerprint the join body carries, so heartbeats
        // never flap between two advertisements for one profile.
        var setup = await DescribeProfileAsync(SelectedPlayProfile, cancellationToken, includeCompatibilityCrcs: true);
        _networkService.SetLocalProfileAdvertisement(setup.Fingerprint, SelectedPlayProfile.Name, Nickname);
    }

    private async Task<(string Fingerprint, string Name)> ResolveAdvertisementAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (SelectedPlayProfile is null && SelectedDetail is not null)
            {
                await AutoMatchProfileAsync(cancellationToken);
            }

            if (SelectedPlayProfile is null)
            {
                return (string.Empty, string.Empty);
            }

            var setup = await DescribeProfileAsync(SelectedPlayProfile, cancellationToken, includeCompatibilityCrcs: true);
            _networkService.SetLocalProfileAdvertisement(setup.Fingerprint, SelectedPlayProfile.Name, Nickname);
            return (setup.Fingerprint, SelectedPlayProfile.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve the join advertisement; joining unadvertised.");
            return (string.Empty, string.Empty);
        }
    }

    private async Task<string?> ResolveHostProfileIdAsync(CancellationToken cancellationToken)
    {
        if (!IsCurrentUserHost || string.IsNullOrWhiteSpace(ExpectedProfileId))
        {
            return null;
        }

        // Profile ids are machine-local, so only the host can resolve the id.
        var profile = await _profileManager.GetProfileAsync(ExpectedProfileId, cancellationToken);
        return profile.Success && profile.Data is not null ? profile.Data.Id : null;
    }

    private Task EnsureProfilesLoadedAsync(CancellationToken cancellationToken = default)
    {
        return EnsureProfilesLoadedAsync(cancellationToken, forceReload: false);
    }

    private async Task EnsureProfilesLoadedAsync(CancellationToken cancellationToken, bool forceReload)
    {
        if (_disposed || (_profilesLoaded && !forceReload))
        {
            return;
        }

        await _profileLock.WaitAsync(cancellationToken);
        try
        {
            if (_profilesLoaded && !forceReload)
            {
                return;
            }

            var profiles = await _profileManager.GetAllProfilesAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (profiles.Success && profiles.Data is not null)
            {
                var sorted = profiles.Data
                    .Where(p => !p.IsToolProfile)
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                ApplyLoadedProfiles(sorted);
            }
            else
            {
                // Stay unloaded so the next panel open or join retries; the
                // lock already prevents concurrent hammering.
                _logger.LogWarning("Failed to load game profiles for the Online tab.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load game profiles for the Online tab.");
        }
        finally
        {
            _profileLock.Release();
        }
    }

    private async Task<OnlineProfileSetup> DescribeProfileAsync(
        GameProfile? profile,
        CancellationToken cancellationToken,
        bool includeCompatibilityCrcs = true)
    {
        if (profile is null)
        {
            return new OnlineProfileSetup(string.Empty, string.Empty, []);
        }

        var map = await GetContentTypeMapAsync(profile, cancellationToken);
        var clientKey = OnlineProfileMatcher.GetGameClientKey(profile);
        var gameplayIds = OnlineProfileMatcher.GetGameplayContentIds(profile, map);
        var fingerprint = includeCompatibilityCrcs
            ? await CreateCompatibilityFingerprintAsync(profile, clientKey, gameplayIds, cancellationToken)
            : OnlineProfileMatcher.CreateFingerprint(clientKey, gameplayIds);
        return new OnlineProfileSetup(
            fingerprint,
            clientKey,
            OnlineProfileMatcher.BoundContentIds(gameplayIds));
    }

    /// <summary>
    /// Builds the advertised fingerprint with the engine compatibility CRCs
    /// when the calculator can resolve them. Any failure falls back to the
    /// id-only fingerprint: a missing CRC must never block matching.
    /// </summary>
    private async Task<string> CreateCompatibilityFingerprintAsync(
        GameProfile profile,
        string clientKey,
        IReadOnlyList<string> gameplayIds,
        CancellationToken cancellationToken)
    {
        var (iniCrc, exeCrc) = await ResolveCompatibilityCrcsAsync(profile, gameplayIds, cancellationToken);
        return OnlineProfileMatcher.CreateFingerprint(clientKey, gameplayIds, iniCrc, exeCrc);
    }

    private async Task<(string? GameRoot, string? ExePath)> ResolveGamePathsAsync(
        GameProfile profile,
        CancellationToken cancellationToken)
    {
        var (gameRoot, exePath) = ResolveProfileRootAndExe(profile);

        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
        {
            gameRoot = await ResolveInstallationRootAsync(profile, cancellationToken);
        }

        if (!string.IsNullOrEmpty(gameRoot) && (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)))
        {
            exePath = ResolveExePathCandidate(profile, gameRoot);
        }

        return (gameRoot, exePath);
    }

    private async Task<string?> ResolveInstallationRootAsync(GameProfile profile, CancellationToken cancellationToken)
    {
        if (_installationService is null)
        {
            return null;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(profile.GameInstallationId))
            {
                var installResult = await _installationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
                if (installResult.Success && installResult.Data is not null)
                {
                    var specific = GetSpecificInstallationPath(installResult.Data, profile.GameClient?.GameType);
                    if (specific is not null)
                    {
                        return specific;
                    }

                    var generic = GetGenericInstallationPath(installResult.Data);
                    if (generic is not null)
                    {
                        return generic;
                    }
                }
            }

            // Fallback: search all available installations for one matching the game type
            var allResult = await _installationService.GetAllInstallationsAsync(cancellationToken);
            if (allResult.Success && allResult.Data is not null)
            {
                var specific = allResult.Data
                    .Select(inst => GetSpecificInstallationPath(inst, profile.GameClient?.GameType))
                    .FirstOrDefault(path => path is not null);
                if (specific is not null)
                {
                    return specific;
                }

                return allResult.Data
                    .Select(GetGenericInstallationPath)
                    .FirstOrDefault(path => path is not null);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "Failed to resolve installation path for profile {ProfileId}", profile.Id);
        }

        return null;
    }

    private async Task<(string IniCrc, string ExeCrc)> ResolveCompatibilityCrcsAsync(
        GameProfile profile,
        IReadOnlyList<string> gameplayIds,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_crcCalculator is null || profile.GameClient is null)
            {
                return (string.Empty, string.Empty);
            }

            var (gameRoot, exePath) = await ResolveGamePathsAsync(profile, cancellationToken);
            if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
            {
                return (string.Empty, string.Empty);
            }

            var exeCrc = string.Empty;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var exeResult = await _crcCalculator.CalculateExeCrcAsync(exePath, gameRoot, ct: cancellationToken);
                if (exeResult.Success && !string.IsNullOrEmpty(exeResult.Data))
                {
                    exeCrc = exeResult.Data;
                }
            }

            var sideloads = await ResolveGameplaySideloadsAsync(profile, gameplayIds, cancellationToken);
            var iniResult = await _crcCalculator.CalculateIniCrcAsync(gameRoot, profile.GameClient.GameType, sideloads, null, cancellationToken);
            var iniCrc = iniResult.Success && !string.IsNullOrEmpty(iniResult.Data) ? iniResult.Data : string.Empty;
            return (iniCrc, exeCrc);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogDebug(ex, "Falling back to the id-only fingerprint; engine CRCs unavailable.");
            return (string.Empty, string.Empty);
        }
    }

    private async Task<IReadOnlyList<string>> ResolveGameplaySideloadsAsync(
        GameProfile profile,
        IReadOnlyList<string> gameplayIds,
        CancellationToken cancellationToken)
    {
        var client = profile.GameClient;
        if (client is null || gameplayIds.Count == 0)
        {
            return [];
        }

        var available = await _profileManager.GetAvailableContentAsync(client, cancellationToken);
        if (!available.Success || available.Data is null)
        {
            return [];
        }

        var wanted = new HashSet<string>(gameplayIds, StringComparer.Ordinal);
        return available.Data
            .Where(m => wanted.Contains(m.Id.ToString(), StringComparer.Ordinal))
            .Select(m => m.SourcePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<IReadOnlyDictionary<string, ContentType>?> GetContentTypeMapAsync(
        GameProfile profile,
        CancellationToken cancellationToken)
    {
        var client = profile.GameClient;
        if (client is null)
        {
            return null;
        }

        var cacheKey = OnlineProfileMatcher.GetGameClientKey(profile);
        if (_contentTypeCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var available = await _profileManager.GetAvailableContentAsync(client, cancellationToken);
        if (!available.Success || available.Data is null)
        {
            return null;
        }

        IReadOnlyDictionary<string, ContentType> map = available.Data
            .GroupBy(m => m.Id.ToString(), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => OnlineProfileMatcher.ResolveDeclaredType(g.Select(m => m.ContentType)),
                StringComparer.Ordinal);
        _contentTypeCache[cacheKey] = map;
        return map;
    }

    partial void OnSelectedNetworkChanged(OnlineNetworkSummary? value)
    {
        JoinPassword = string.Empty;
        JoinNetworkCommand.NotifyCanExecuteChanged();

        // While joined the detail card hides and the live lobby state stays
        // pinned: browsing must not overwrite the joined lobby's game.
        if (IsJoined)
        {
            return;
        }

        // Cancel without disposing: the superseded load may still register on
        // the token, and a CTS without timers holds no native resources.
        _detailCts?.Cancel();
        if (value is null)
        {
            SelectedDetail = null;
            DetailLoading = false;
            return;
        }

        _detailCts = new CancellationTokenSource();
        _ = LoadDetailAsync(value, _detailCts.Token);
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SearchDebounceMs, token);
                await Dispatcher.UIThread.InvokeAsync(() => RefreshNetworksAsync(token));
            }
            catch (OperationCanceledException)
            {
                // Superseded by newer keystrokes.
            }
        }, token);
    }

    partial void OnSelectedMemberChanged(OnlineMember? value)
    {
        ReportMemberCommand.NotifyCanExecuteChanged();
        BanMemberCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPlayProfileChanged(GameProfile? value)
    {
        if (_syncingPlayProfile)
        {
            return;
        }

        if (!IsJoined && SelectedDetail is null)
        {
            return;
        }

        // Safe to detach: the refresh reports its own errors, and picker
        // changes outlive any scoped token, so matching is uncancellable.
        _ = RefreshMatchAfterPickerChangeAsync();
    }

    partial void OnIsJoinedChanged(bool value)
    {
        JoinNetworkCommand.NotifyCanExecuteChanged();
        LeaveNetworkCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        CopyOverlayIpCommand.NotifyCanExecuteChanged();
        ReportMemberCommand.NotifyCanExecuteChanged();
        BanMemberCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsGameRunningChanged(bool value)
    {
        PlayCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCurrentUserHostChanged(bool value)
    {
        SaveNetworkCommand.NotifyCanExecuteChanged();
        ToggleHostPanelCommand.NotifyCanExecuteChanged();
        BanMemberCommand.NotifyCanExecuteChanged();
    }
}
