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
using GenHub.Core.Models.Online;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Online.ViewModels;

/// <summary>
/// ViewModel for the Online tab: browse networks, create password-protected
/// networks, join them, and share one virtual LAN for in-game LAN lobbies.
/// </summary>
/// <param name="networkService">The online network service.</param>
/// <param name="launchService">The online launch service.</param>
/// <param name="profileManager">The game profile manager for expected-profile lookup.</param>
/// <param name="p2pService">The P2P connection service for reachability checks.</param>
/// <param name="notificationService">The notification service for toasts.</param>
/// <param name="dialogService">The dialog service for confirmations.</param>
/// <param name="logger">The logger.</param>
/// <param name="localizationService">The optional localization service.</param>
public sealed partial class OnlineViewModel(
    IOnlineNetworkService networkService,
    IOnlineLaunchService launchService,
    IGameProfileManager profileManager,
    IP2PConnectionService p2pService,
    INotificationService notificationService,
    IDialogService dialogService,
    ILogger<OnlineViewModel> logger,
    ILocalizationService? localizationService = null) : ViewModelBase, IDisposable
{
    private const int SearchDebounceMs = 350;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _detailCts;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<OnlineNetworkSummary> _networks = [];

    [ObservableProperty]
    private OnlineNetworkSummary? _selectedNetwork;

    [ObservableProperty]
    private OnlineNetworkDetail? _selectedDetail;

    [ObservableProperty]
    private bool _detailLoading;

    [ObservableProperty]
    private string _expectedProfileName = string.Empty;

    [ObservableProperty]
    private bool _expectedProfileInstalled;

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
    private bool _isJoined;

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
    private bool _preferRelay;

    [ObservableProperty]
    private string _joinPassword = string.Empty;

    [ObservableProperty]
    private string _createName = string.Empty;

    [ObservableProperty]
    private string _createPassword = string.Empty;

    [ObservableProperty]
    private string _createDescription = string.Empty;

    [ObservableProperty]
    private int _createSlots = OnlineConstants.DefaultSlotCap;

    [ObservableProperty]
    private bool _createIsPublic = true;

    [ObservableProperty]
    private bool _isCreatePanelOpen;

    [ObservableProperty]
    private string _hostDescription = string.Empty;

    [ObservableProperty]
    private string _hostExpectedProfileId = string.Empty;

    [ObservableProperty]
    private bool _isHostPanelOpen;

    /// <summary>
    /// Initializes the view model by subscribing to roster updates.
    /// </summary>
    public void Initialize()
    {
        networkService.RosterChanged += OnRosterChanged;
        networkService.ConnectionLost += OnConnectionLost;
    }

    /// <summary>
    /// Refreshes the public network directory.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task RefreshNetworksAsync(CancellationToken cancellationToken = default)
    {
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
        }
    }

    /// <summary>
    /// Joins the selected network with the entered password.
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

        try
        {
            IsLoading = true;
            var result = await networkService.JoinNetworkAsync(
                SelectedNetwork.Id, JoinPassword, PreferRelay, cancellationToken);
            if (!result.Success)
            {
                ShowJoinErrorToast(result.Errors.FirstOrDefault());
                return;
            }

            ApplyJoin(result.Data);
            JoinPassword = string.Empty;
            notificationService.ShowSuccess(
                GetString("Online.Join.SuccessTitle"),
                GetString("Online.Join.SuccessMessage", result.Data.OverlayIp),
                NotificationDurations.Long);
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
        if (CreateName.Length < OnlineConstants.MinNetworkNameLength ||
            CreateName.Length > OnlineConstants.MaxNetworkNameLength)
        {
            ShowErrorToast("Online.Error.CreateTitle", GetString("Online.Error.NameLength"));
            return;
        }

        if (CreateIsPublic && CreatePassword.Length < OnlineConstants.MinPasswordLength)
        {
            ShowErrorToast("Online.Error.CreateTitle", GetString("Online.Error.PasswordRequired"));
            return;
        }

        if (CreatePassword.Length > 0 && CreatePassword.Length < OnlineConstants.MinPasswordLength)
        {
            ShowErrorToast("Online.Error.CreateTitle", GetString("Online.Error.PasswordTooShort"));
            return;
        }

        try
        {
            IsLoading = true;
            var request = new OnlineCreateNetworkRequest
            {
                Name = CreateName.Trim(),
                Password = CreatePassword,
                SlotsMax = Math.Clamp(CreateSlots, 2, OnlineConstants.MaxSlotCap),
                IsPublic = CreateIsPublic,
                Description = CreateDescription.Trim(),
            };

            var result = await networkService.CreateNetworkAsync(request, cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.Error.CreateTitle", result.Errors.FirstOrDefault());
                return;
            }

            ApplyJoin(result.Data);
            CreateName = string.Empty;
            CreatePassword = string.Empty;
            CreateDescription = string.Empty;
            IsCreatePanelOpen = false;
            notificationService.ShowSuccess(
                GetString("Online.Create.SuccessTitle"),
                GetString("Online.Create.SuccessMessage", result.Data.OverlayIp),
                NotificationDurations.Long);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create online network.");
            ShowErrorToast("Online.Error.CreateTitle", null);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Launches the expected game profile for the joined network.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(IsJoined))]
    public async Task PlayAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IsLoading = true;
            notificationService.ShowInfo(
                GetString("Online.Play.LaunchingTitle"),
                GetString("Online.Play.LaunchingMessage", CurrentNetworkName),
                NotificationDurations.Medium);

            var result = await launchService.PlayAsync(ExpectedProfileId, CurrentNetworkName, cancellationToken);
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

            notificationService.ShowSuccess(
                GetString("Online.Play.SuccessTitle"),
                GetString("Online.Play.SuccessMessage", CurrentNetworkName),
                NotificationDurations.Long);
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
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(IsCurrentUserHost))]
    public async Task SaveNetworkAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IsLoading = true;
            var result = await networkService.UpdateNetworkAsync(
                HostDescription, HostExpectedProfileId, cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.Error.UpdateTitle", GetString("Online.Error.UpdateFailed"));
                return;
            }

            ExpectedProfileId = HostExpectedProfileId;
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
    /// Tests direct UDP reachability to the selected member.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(CanTest))]
    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMember is null || !TryParseEndpoint(SelectedMember.Endpoint, out var ip, out var port))
        {
            return;
        }

        try
        {
            IsLoading = true;
            var result = await p2pService.ConnectToPeerAsync(ip, port, cancellationToken);
            if (!result.Success)
            {
                ShowErrorToast("Online.Error.TestTitle", GetString("Online.Error.TestFailed", SelectedMember.DisplayName));
                return;
            }

            notificationService.ShowSuccess(
                GetString("Online.Test.SuccessTitle"),
                GetString("Online.Test.SuccessMessage", SelectedMember.DisplayName),
                NotificationDurations.Medium);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to test peer connection.");
            ShowErrorToast("Online.Error.TestTitle", null);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Toggles the create-network panel.
    /// </summary>
    [RelayCommand]
    public void ToggleCreatePanel()
    {
        IsCreatePanelOpen = !IsCreatePanelOpen;
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
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _detailCts?.Cancel();
        _detailCts?.Dispose();
        _refreshLock.Dispose();
    }

    private static TopLevel? GetMainWindowTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            return TopLevel.GetTopLevel(mainWindow);
        }

        return null;
    }

    private static bool TryParseEndpoint(string endpoint, out string ip, out int port)
    {
        ip = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        var separator = endpoint.LastIndexOf(':');
        if (separator <= 0 || separator == endpoint.Length - 1)
        {
            return false;
        }

        ip = endpoint.Substring(0, separator).Trim('[', ']', ' ');
        return IPAddress.TryParse(ip, out _) && int.TryParse(endpoint.Substring(separator + 1), out port) && port is >= 1 and <= 65535;
    }

    private bool CanJoin() => !IsJoined && SelectedNetwork is not null;

    private bool CanTest() =>
        IsJoined && SelectedMember is not null && TryParseEndpoint(SelectedMember.Endpoint, out _, out _)
        && SelectedMember.Endpoint != networkService.LocalEndpoint;

    private bool CanModerate() => IsJoined && SelectedMember is not null && SelectedMember.OverlayIp != OverlayIp;

    private bool CanBan() => CanModerate() && IsCurrentUserHost;

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
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    private void ApplyJoin(OnlineJoinResult join)
    {
        IsJoined = true;
        CurrentNetworkName = Networks.FirstOrDefault(n => n.Id == join.NetworkId)?.Name ?? join.NetworkId;
        ExpectedProfileId = join.ExpectedProfileId;
        HostExpectedProfileId = join.ExpectedProfileId;
        OverlayIp = join.OverlayIp;
        Members = new ObservableCollection<OnlineMember>(join.Members);
        RefreshHostState();
        JoinNetworkCommand.NotifyCanExecuteChanged();
        LeaveNetworkCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        CopyOverlayIpCommand.NotifyCanExecuteChanged();
        ReportMemberCommand.NotifyCanExecuteChanged();
        BanMemberCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        SaveNetworkCommand.NotifyCanExecuteChanged();
        ToggleHostPanelCommand.NotifyCanExecuteChanged();
    }

    private void ClearJoin()
    {
        IsJoined = false;
        IsCurrentUserHost = false;
        CurrentNetworkName = string.Empty;
        ExpectedProfileId = string.Empty;
        HostDescription = string.Empty;
        HostExpectedProfileId = string.Empty;
        IsHostPanelOpen = false;
        OverlayIp = string.Empty;
        JoinPassword = string.Empty;
        ConnectionQuality = OnlineConnectionQuality.Unknown;
        Members = [];
        SelectedMember = null;
        JoinNetworkCommand.NotifyCanExecuteChanged();
        LeaveNetworkCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        CopyOverlayIpCommand.NotifyCanExecuteChanged();
        ReportMemberCommand.NotifyCanExecuteChanged();
        BanMemberCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        SaveNetworkCommand.NotifyCanExecuteChanged();
        ToggleHostPanelCommand.NotifyCanExecuteChanged();
    }

    private void RefreshHostState()
    {
        IsCurrentUserHost = Members.Any(m => m.OverlayIp == OverlayIp && m.IsHost);
        ConnectionQuality = Members.FirstOrDefault(m => m.OverlayIp == OverlayIp)?.Quality ?? OnlineConnectionQuality.Unknown;
        SaveNetworkCommand.NotifyCanExecuteChanged();
        ToggleHostPanelCommand.NotifyCanExecuteChanged();
        BanMemberCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
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

    private void ShowErrorToast(string titleKey, string? detail)
    {
        var detailText = string.IsNullOrWhiteSpace(detail) || detail.StartsWith("online.", StringComparison.Ordinal)
            ? GetString("Online.Error.GenericDetail")
            : OnlineLogScrubber.Scrub(detail);
        notificationService.ShowError(GetString(titleKey), detailText, NotificationDurations.Long);
    }

    private string GetString(string key) => localizationService?.GetString(key) ?? key;

    private string GetString(string key, string arg) =>
        localizationService?.GetString(key, arg) ?? $"{key} ({arg})";

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
            ExpectedProfileName = string.Empty;
            ExpectedProfileInstalled = false;

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
            if (!string.IsNullOrWhiteSpace(result.Data.ExpectedProfileId))
            {
                await ResolveExpectedProfileAsync(result.Data.ExpectedProfileId, cancellationToken);
            }
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
            DetailLoading = false;
        }
    }

    private async Task ResolveExpectedProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await profileManager.GetProfileAsync(profileId, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (profile.Success && profile.Data is not null)
            {
                ExpectedProfileName = profile.Data.Name;
                ExpectedProfileInstalled = true;
            }
            else
            {
                ExpectedProfileName = profileId;
                ExpectedProfileInstalled = false;
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve expected profile.");
            ExpectedProfileName = profileId;
            ExpectedProfileInstalled = false;
        }
    }

    partial void OnSelectedNetworkChanged(OnlineNetworkSummary? value)
    {
        JoinNetworkCommand.NotifyCanExecuteChanged();
        _detailCts?.Cancel();
        _detailCts?.Dispose();
        if (value is null)
        {
            SelectedDetail = null;
            return;
        }

        _detailCts = new CancellationTokenSource();
        _ = LoadDetailAsync(value, _detailCts.Token);
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
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
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsJoinedChanged(bool value)
    {
        JoinNetworkCommand.NotifyCanExecuteChanged();
        LeaveNetworkCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        CopyOverlayIpCommand.NotifyCanExecuteChanged();
        ReportMemberCommand.NotifyCanExecuteChanged();
        BanMemberCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCurrentUserHostChanged(bool value)
    {
        SaveNetworkCommand.NotifyCanExecuteChanged();
        ToggleHostPanelCommand.NotifyCanExecuteChanged();
        BanMemberCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
    }
}
