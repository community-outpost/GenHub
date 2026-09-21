using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
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
/// <param name="networkService">The online network service.</param>
/// <param name="launchService">The online launch service.</param>
/// <param name="profileManager">The game profile manager for profile lookup.</param>
/// <param name="notificationService">The notification service for toasts.</param>
/// <param name="dialogService">The dialog service for confirmations.</param>
/// <param name="logger">The logger.</param>
/// <param name="localizationService">The optional localization service.</param>
public sealed partial class OnlineViewModel(
    IOnlineNetworkService networkService,
    IOnlineLaunchService launchService,
    IGameProfileManager profileManager,
    INotificationService notificationService,
    IDialogService dialogService,
    ILogger<OnlineViewModel> logger,
    ILocalizationService? localizationService = null) : ViewModelBase, IDisposable
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

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SemaphoreSlim _profileLock = new(1, 1);
    private readonly Dictionary<string, IReadOnlyDictionary<string, ContentType>> _contentTypeCache = new(StringComparer.Ordinal);
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _detailCts;
    private bool _disposed;
    private bool _joinInFlight;
    private bool _profilesLoaded;
    private bool _syncingPlayProfile;
    private string? _launchedProfileId;
    private IReadOnlyList<string> _expectedContentIds = [];

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
        networkService.RosterChanged += OnRosterChanged;
        networkService.ConnectionLost += OnConnectionLost;
        networkService.ExpectedProfileChanged += OnExpectedProfileChanged;
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
            DirectoryFailed = false;
            var result = await networkService.GetNetworksAsync(SearchText, cancellationToken);
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
            logger.LogError(ex, "Failed to refresh online directory.");
            DirectoryFailed = true;
            ShowErrorToast("Online.Error.DirectoryTitle", null);
        }
        finally
        {
            IsLoading = false;
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
        if (_joinInFlight)
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

            var result = await networkService.JoinNetworkAsync(
                target.Id, JoinPassword.Trim(), true, advertisement.Fingerprint, advertisement.Name, cancellationToken);
            if (!result.Success)
            {
                ShowJoinErrorToast(result.Errors.FirstOrDefault());
                return;
            }

            JoinPassword = string.Empty;
            await ApplyJoinAsync(result.Data, target.Name, cancellationToken);
            notificationService.ShowSuccess(
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
            logger.LogError(ex, "Failed to join online network.");
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
            var result = await networkService.LeaveNetworkAsync(cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.Error.LeaveTitle", result.Errors.FirstOrDefault());
                return;
            }

            ClearJoin();
            notificationService.ShowInfo(
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
            logger.LogError(ex, "Failed to leave online network.");
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
        // Creating while joined would orphan the active network: the second
        // bring-up fails and its teardown kills the live sidecar.
        if (IsJoined)
        {
            ShowErrorToast(CreateErrorTitleKey, GetString("Online.Error.AlreadyJoined"));
            return;
        }

        if (CreateName.Length < OnlineConstants.MinNetworkNameLength ||
            CreateName.Length > OnlineConstants.MaxNetworkNameLength)
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
        var createProfile = SelectedCreateProfile;
        var slotsMax = Math.Clamp(CreateSlots, 2, OnlineConstants.MaxSlotCap);
        var isPublic = CreateIsPublic;
        _joinInFlight = true;
        try
        {
            IsLoading = true;
            await EnsureProfilesLoadedAsync(cancellationToken);
            var setup = await DescribeProfileAsync(createProfile, cancellationToken);
            var request = new OnlineCreateNetworkRequest
            {
                Name = networkName,
                Password = CreatePassword.Trim(),
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
            };

            var result = await networkService.CreateNetworkAsync(request, cancellationToken);
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
            notificationService.ShowSuccess(
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
            logger.LogError(ex, "Failed to create online network.");
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
                notificationService.ShowWarning(
                    GetString("Online.Play.NoProfileTitle"),
                    GetString("Online.Play.NoProfileMessage"),
                    NotificationDurations.Long);
                return;
            }

            await WarnOnUnreachableMeshAsync(cancellationToken);

            notificationService.ShowInfo(
                GetString("Online.Play.LaunchingTitle"),
                GetString("Online.Play.LaunchingMessage", CurrentNetworkName),
                NotificationDurations.Medium);

            var result = await launchService.PlayAsync(profileId, CurrentNetworkName, OverlayIp, cancellationToken);
            if (!result.Success)
            {
                if (result.Errors.Any(e => e == OnlineConstants.ErrorProfileMissing))
                {
                    notificationService.ShowWarning(
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
            notificationService.ShowSuccess(
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
            logger.LogError(ex, "Failed to launch from the Online tab.");
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
            notificationService.ShowWarning(
                GetString("Online.Play.NoProfileTitle"),
                GetString("Online.Play.NoProfileMessage"),
                NotificationDurations.Long);
            return;
        }

        try
        {
            IsLoading = true;
            var result = await launchService.StopAsync(profileId, cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.Error.StopTitle", GetString("Online.Error.StopFailed"));
                return;
            }

            _launchedProfileId = null;
            IsGameRunning = false;
            notificationService.ShowSuccess(
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
            logger.LogError(ex, "Failed to stop the game from the Online tab.");
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
                notificationService.ShowSuccess(
                    GetString("Online.Copy.SuccessTitle"),
                    GetString("Online.Copy.SuccessMessage", OverlayIp),
                    NotificationDurations.Short);
            }
            else
            {
                notificationService.ShowError(
                    GetString("Online.Error.CopyTitle"),
                    GetString("Online.Error.CopyUnavailable"),
                    NotificationDurations.Medium);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to copy overlay IP.");
            notificationService.ShowError(
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
            var result = await networkService.ReportMemberAsync(SelectedMember.OverlayIp, reason, cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.Error.ReportTitle", GetString("Online.Error.ReportFailed"));
                return;
            }

            notificationService.ShowSuccess(
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
            logger.LogError(ex, "Failed to report online member.");
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

        var confirmed = await dialogService.ShowConfirmationAsync(
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
            var result = await networkService.BanMemberAsync(SelectedMember.OverlayIp, cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.Error.BanTitle", GetString("Online.Error.BanFailed"));
                return;
            }

            SelectedMember = null;
            notificationService.ShowSuccess(
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
            logger.LogError(ex, "Failed to ban online member.");
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
            var setup = await DescribeProfileAsync(SelectedHostProfile, cancellationToken);
            var expected = new OnlineExpectedProfile
            {
                ExpectedProfileId = SelectedHostProfile?.Id ?? string.Empty,
                ExpectedProfileFingerprint = setup.Fingerprint,
                ExpectedProfileName = SelectedHostProfile?.Name ?? string.Empty,
                ExpectedGameClientId = setup.ClientKey,
                ExpectedContentIds = setup.GameplayContentIds,
            };
            var result = await networkService.UpdateNetworkAsync(HostDescription, expected, cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.Error.UpdateTitle", GetString("Online.Error.UpdateFailed"));
                return;
            }

            ApplyExpectedProfile(expected);
            SetPlayProfile(SelectedHostProfile);
            await AdvertiseSelectedProfileAsync(cancellationToken);
            IsHostPanelOpen = false;
            notificationService.ShowSuccess(
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
            logger.LogError(ex, "Failed to update online network.");
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
            _ = EnsureProfilesLoadedAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Toggles the host settings panel.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsCurrentUserHost))]
    public void ToggleHostPanel()
    {
        IsHostPanelOpen = !IsHostPanelOpen;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        networkService.RosterChanged -= OnRosterChanged;
        networkService.ConnectionLost -= OnConnectionLost;
        networkService.ExpectedProfileChanged -= OnExpectedProfileChanged;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _detailCts?.Cancel();
        _detailCts?.Dispose();
        _refreshLock.Dispose();
        _profileLock.Dispose();
    }

    private static TopLevel? GetMainWindowTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            return TopLevel.GetTopLevel(mainWindow);
        }

        return null;
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
        if (Dispatcher.UIThread.CheckAccess())
        {
            HandleConnectionLost();
        }
        else
        {
            Dispatcher.UIThread.Post(HandleConnectionLost);
        }
    }

    private void HandleConnectionLost()
    {
        ClearJoin();
        notificationService.ShowWarning(
            GetString("Online.Connection.LostTitle"),
            GetString("Online.Connection.LostMessage"),
            NotificationDurations.Long);
    }

    private void OnExpectedProfileChanged(object? sender, OnlineExpectedProfile expected)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _ = HandleExpectedProfileChangedAsync(expected);
        }
        else
        {
            Dispatcher.UIThread.Post(() => _ = HandleExpectedProfileChangedAsync(expected));
        }
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
        notificationService.ShowInfo(
            GetString("Online.Profile.SwitchedTitle"),
            message,
            NotificationDurations.Long);
    }

    private void OnRosterChanged(object? sender, IReadOnlyList<OnlineMember> members)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyRoster(members);
        }
        else
        {
            Dispatcher.UIThread.Post(() => ApplyRoster(members));
        }
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
        networkService.SetLocalProfileAdvertisement(string.Empty, string.Empty);
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
            _ => null,
        };
        ShowErrorToast(CreateErrorTitleKey, messageKey is null ? errorCode : GetString(messageKey));
    }

    private void ShowErrorToast(string titleKey, string? detail)
    {
        var detailText = string.IsNullOrWhiteSpace(detail) || detail.StartsWith(OnlineConstants.ErrorCodePrefix, StringComparison.Ordinal)
            ? GetString("Online.Error.GenericDetail")
            : OnlineLogScrubber.Scrub(detail);
        notificationService.ShowError(GetString(titleKey), detailText, NotificationDurations.Long);
    }

    private bool IsOverlayPending()
    {
        var config = networkService.CurrentJoin?.AdapterConfig ?? string.Empty;
        return OverlayConfigInspector.TryGetOverlayName(config) == OnlineConstants.OverlayPendingSelection;
    }

    private void NotifyAdapterState()
    {
        IsLobbyOnly = networkService.AdapterState != OnlineAdapterState.Up;
        if (!IsLobbyOnly)
        {
            return;
        }

        if (IsOverlayPending())
        {
            // Expected pre-overlay state: lobby works, tunneling waits.
            notificationService.ShowInfo(
                GetString("Online.Adapter.PendingTitle"),
                GetString("Online.Adapter.PendingMessage"),
                NotificationDurations.Long);
            return;
        }

        notificationService.ShowWarning(
            GetString("Online.Adapter.UnavailableTitle"),
            GetString("Online.Adapter.UnavailableMessage"),
            NotificationDurations.Long);
    }

    private async Task WarnOnUnreachableMeshAsync(CancellationToken cancellationToken)
    {
        // Advisory preflight: any mesh failure skips the warning and the
        // launch proceeds. Mixed-version peers cannot answer probes yet.
        var meshTask = networkService.RunMeshCheckAsync(cancellationToken);
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
            logger.LogWarning(ex, "Mesh check skipped.");
            return;
        }

        if (mesh is null || !mesh.Success || mesh.Data is null || mesh.Data.AllReachable)
        {
            return;
        }

        notificationService.ShowWarning(
            GetString("Online.Play.ConnectivityTitle"),
            GetString("Online.Play.ConnectivityMessage", mesh.Data.UnreachableCount, mesh.Data.Peers.Count),
            NotificationDurations.Long);
    }

    private string GetString(string key) => localizationService?.GetString(key) ?? key;

    private string GetString(string key, string arg) =>
        localizationService?.GetString(key, arg) ?? $"{key} ({arg})";

    private string GetString(string key, int first, int second) =>
        localizationService?.GetString(key, first, second) ?? $"{key} ({first}, {second})";

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

        await dialogService.ShowMessageAsync(
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

            var result = await networkService.GetNetworkDetailAsync(network.Id, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!result.Success)
            {
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
            logger.LogWarning(ex, "Failed to load network detail.");
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

        var shared = OnlineProfileMatcher.ScoreOverlap(_expectedContentIds, localContentIds);
        ProfileMatchDetail = match switch
        {
            OnlineProfileMatch.Exact => null,
            OnlineProfileMatch.SameClient => GetString("Online.Detail.MatchDetailSameClient", shared, _expectedContentIds.Count),
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
            logger.LogWarning(ex, "Failed to re-match the selected launch profile.");
        }
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
            logger.LogWarning(ex, "Failed to auto-match a local profile.");
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
            logger.LogWarning(ex, "Failed to match profile by name or id.");
        }

        return false;
    }

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
            var setup = await DescribeProfileAsync(profile, cancellationToken);
            if (string.Equals(setup.Fingerprint, ExpectedProfileFingerprint, StringComparison.Ordinal))
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
            var setup = await DescribeProfileAsync(SelectedPlayProfile, cancellationToken);
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
            networkService.SetLocalProfileAdvertisement(string.Empty, string.Empty);
            return;
        }

        // Same map-aware fingerprint the join body carries, so heartbeats
        // never flap between two advertisements for one profile.
        var setup = await DescribeProfileAsync(SelectedPlayProfile, cancellationToken);
        networkService.SetLocalProfileAdvertisement(setup.Fingerprint, SelectedPlayProfile.Name);
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

            var setup = await DescribeProfileAsync(SelectedPlayProfile, cancellationToken);
            networkService.SetLocalProfileAdvertisement(setup.Fingerprint, SelectedPlayProfile.Name);
            return (setup.Fingerprint, SelectedPlayProfile.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve the join advertisement; joining unadvertised.");
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
        var profile = await profileManager.GetProfileAsync(ExpectedProfileId, cancellationToken);
        return profile.Success && profile.Data is not null ? profile.Data.Id : null;
    }

    private async Task EnsureProfilesLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _profilesLoaded)
        {
            return;
        }

        await _profileLock.WaitAsync(cancellationToken);
        try
        {
            if (_profilesLoaded)
            {
                return;
            }

            var profiles = await profileManager.GetAllProfilesAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (profiles.Success && profiles.Data is not null)
            {
                AvailableProfiles = new ObservableCollection<GameProfile>(
                    profiles.Data.Where(p => !p.IsToolProfile).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase));
                _profilesLoaded = true;
            }
            else
            {
                // Stay unloaded so the next panel open or join retries; the
                // lock already prevents concurrent hammering.
                logger.LogWarning("Failed to load game profiles for the Online tab.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load game profiles for the Online tab.");
        }
        finally
        {
            _profileLock.Release();
        }
    }

    private async Task<OnlineProfileSetup> DescribeProfileAsync(GameProfile? profile, CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            return new OnlineProfileSetup(string.Empty, string.Empty, []);
        }

        var map = await GetContentTypeMapAsync(profile, cancellationToken);
        return new OnlineProfileSetup(
            OnlineProfileMatcher.ComputeFingerprint(profile, map),
            OnlineProfileMatcher.GetGameClientKey(profile),
            OnlineProfileMatcher.BoundContentIds(OnlineProfileMatcher.GetGameplayContentIds(profile, map)));
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

        var available = await profileManager.GetAvailableContentAsync(client, cancellationToken);
        if (!available.Success || available.Data is null)
        {
            return null;
        }

        IReadOnlyDictionary<string, ContentType> map = available.Data
            .GroupBy(m => m.Id.ToString(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ContentType, StringComparer.Ordinal);
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
