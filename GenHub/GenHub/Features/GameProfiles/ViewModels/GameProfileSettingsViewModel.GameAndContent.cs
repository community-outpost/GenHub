using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Game and Content pages of the profile settings window.
/// </summary>
public partial class GameProfileSettingsViewModel
{
    private List<ContentDisplayItem> _profileContentCandidates = [];
    private int _profileContentCandidatesVersion;

    [ObservableProperty]
    private ObservableCollection<ProfileContentRow> _gameInstallationRows = [];

    [ObservableProperty]
    private ObservableCollection<ProfileContentRow> _gameClientRows = [];

    [ObservableProperty]
    private ObservableCollection<ProfileContentRow> _profileContentRows = [];

    [ObservableProperty]
    private ProfileContentFilterOption _selectedProfileContentFilter = ProfileContentFilterOption.All;

    /// <summary>
    /// Gets the content types listed on the Content page.
    /// </summary>
    public static IReadOnlyList<ContentType> ProfileContentTypes { get; } =
    [
        ContentType.Mod,
        ContentType.Patch,
        ContentType.Addon,
        ContentType.Map,
        ContentType.MapPack,
        ContentType.Mission,
        ContentType.LanguagePack,
        ContentType.Skin,
        ContentType.ModdingTool,
        ContentType.Executable,
    ];

    /// <summary>
    /// Gets the choices of the Content page type filter.
    /// </summary>
    public static IReadOnlyList<ProfileContentFilterOption> ProfileContentFilterOptions { get; } =
        [ProfileContentFilterOption.All, .. ProfileContentTypes.Select(type => new ProfileContentFilterOption(type))];

    /// <summary>
    /// Gets a value indicating whether the Content page has no rows to show.
    /// </summary>
    public bool IsProfileContentEmpty => ProfileContentRows.Count == 0;

    /// <summary>
    /// Gets a value indicating whether no game installation was found.
    /// </summary>
    public bool IsGameInstallationListEmpty => GameInstallationRows.Count == 0;

    /// <summary>
    /// Gets a value indicating whether no game client was found for the profile's game.
    /// </summary>
    public bool IsGameClientListEmpty => GameClientRows.Count == 0;

    /// <summary>
    /// Gets a value indicating whether a game installation can be chosen for this profile.
    /// </summary>
    public bool CanSelectGameInstallation => !IsStandaloneToolProfile;

    /// <summary>
    /// Gets a value indicating whether the profile is a standalone tool profile, which takes no game installation.
    /// </summary>
    public bool IsStandaloneToolProfile => ToolProfileHelper.IsToolProfile(EnabledContent
        .Where(item => item.ContentType != ContentType.GameInstallation)
        .Select(item => (item.ManifestId.Value, item.ContentType)));

    /// <summary>
    /// Loads every installation, client and content item the Game and Content pages can offer.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected internal async Task LoadProfileContentCandidatesAsync()
    {
        if (_profileContentLoader == null)
        {
            RebuildProfileContentRows();
            return;
        }

        var version = Interlocked.Increment(ref _profileContentCandidatesVersion);
        try
        {
            var installations = new ObservableCollection<Core.Models.Content.ContentDisplayItem>(
                AvailableGameInstallations.Select(ToCoreContentDisplayItem));
            var enabledIds = EnabledContent.Select(item => item.ManifestId.Value).ToList();
            var candidates = new List<ContentDisplayItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var type in ProfileContentTypes.Prepend(ContentType.GameClient))
            {
                var coreItems = await _profileContentLoader.LoadAvailableContentAsync(type, installations, enabledIds);
                foreach (var coreItem in coreItems ?? [])
                {
                    if (coreItem.ContentType != type || !seen.Add(coreItem.ManifestId))
                    {
                        continue;
                    }

                    try
                    {
                        candidates.Add(ConvertToViewModelContentDisplayItem(coreItem));
                    }
                    catch (ArgumentException ex)
                    {
                        _logger?.LogWarning(ex, "Skipping invalid content item {DisplayName}", coreItem.DisplayName);
                    }
                }
            }

            if (version != Volatile.Read(ref _profileContentCandidatesVersion))
            {
                return;
            }

            UpdateContentItemsHotswapState(candidates, IsHotswapMode);
            _profileContentCandidates = candidates;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.LogError(ex, "Error loading profile content candidates");
            _notificationService?.ShowError(GetErrorLoadingContentTitle(), ex.Message);
        }

        RebuildProfileContentRows();
    }

    /// <summary>
    /// Rebuilds the rows of the Game and Content pages from the current profile state.
    /// </summary>
    protected internal void RebuildProfileContentRows()
    {
        var gameType = GameTypeFilter;
        var enabledById = new Dictionary<string, ContentDisplayItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in EnabledContent)
        {
            enabledById.TryAdd(item.ManifestId.Value, item);
        }

        var selectedInstallationId = SelectedGameInstallation?.ManifestId.Value;
        GameInstallationRows = new ObservableCollection<ProfileContentRow>(AvailableGameInstallations
            .OrderBy(item => item.GameType != gameType)
            .ThenBy(item => item.GameType)
            .ThenBy(GetInstallationPriority)
            .Select(item => new ProfileContentRow(
                item,
                string.Equals(item.ManifestId.Value, selectedInstallationId, StringComparison.OrdinalIgnoreCase))));

        GameClientRows = new ObservableCollection<ProfileContentRow>(BuildRows(
            enabledById,
            item => item.ContentType == ContentType.GameClient && IsForGame(item, gameType)));

        var filter = SelectedProfileContentFilter?.ContentType;
        ProfileContentRows = new ObservableCollection<ProfileContentRow>(BuildRows(
                enabledById,
                item => item.ContentType is not (ContentType.GameInstallation or ContentType.GameClient) &&
                        (filter == null || item.ContentType == filter) &&
                        (enabledById.ContainsKey(item.ManifestId.Value) ||
                         (ProfileContentTypes.Contains(item.ContentType) && IsForGame(item, gameType))))
            .OrderByDescending(row => row.IsIncluded)
            .ThenBy(row => row.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase));
        OnPropertyChanged(nameof(IsStandaloneToolProfile));
        OnPropertyChanged(nameof(CanSelectGameInstallation));
    }

    private static string GetDisplayIdentity(ContentDisplayItem item) =>
        string.Join('\u001F', item.ContentType, item.GameType, item.DisplayName, item.Publisher ?? string.Empty, item.Version ?? string.Empty);

    private static bool IsForGame(ContentDisplayItem item, Core.Models.Enums.GameType gameType) =>
        item.GameType == gameType || item.GameType == Core.Models.Enums.GameType.Unknown;

    private List<ProfileContentRow> BuildRows(
        Dictionary<string, ContentDisplayItem> enabledById,
        Func<ContentDisplayItem, bool> include)
    {
        var rows = new List<ProfileContentRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in enabledById.Values.Concat(_profileContentCandidates).Where(include))
        {
            if (!seen.Add(item.ManifestId.Value))
            {
                continue;
            }

            var isIncluded = enabledById.ContainsKey(item.ManifestId.Value);
            if (!seenIdentities.Add(GetDisplayIdentity(item)) && !isIncluded)
            {
                continue;
            }

            if (!isIncluded)
            {
                item.IsEnabled = false;
            }

            rows.Add(new ProfileContentRow(item, isIncluded));
        }

        return rows;
    }

    partial void OnSelectedProfileContentFilterChanged(ProfileContentFilterOption value)
    {
        RebuildProfileContentRows();
    }

    partial void OnProfileContentRowsChanged(ObservableCollection<ProfileContentRow> value)
    {
        OnPropertyChanged(nameof(IsProfileContentEmpty));
    }

    partial void OnGameInstallationRowsChanged(ObservableCollection<ProfileContentRow> value)
    {
        OnPropertyChanged(nameof(IsGameInstallationListEmpty));
    }

    partial void OnGameClientRowsChanged(ObservableCollection<ProfileContentRow> value)
    {
        OnPropertyChanged(nameof(IsGameClientListEmpty));
    }

    [RelayCommand]
    private async Task RefreshProfileContentAsync()
    {
        await LoadAvailableContentAsync();
        await LoadProfileContentCandidatesAsync();
    }

    [RelayCommand]
    private async Task ToggleProfileContentAsync(ContentDisplayItem? item)
    {
        if (item == null)
        {
            return;
        }

        try
        {
            if (EnabledContent.Any(enabled => string.Equals(enabled.ManifestId.Value, item.ManifestId.Value, StringComparison.OrdinalIgnoreCase)))
            {
                await DisableContentAsync(item);
            }
            else
            {
                await EnableContentInternal(item);
            }
        }
        finally
        {
            RebuildProfileContentRows();
        }
    }

    [RelayCommand]
    private async Task SelectGameInstallationAsync(ContentDisplayItem? installation)
    {
        if (installation == null ||
            string.Equals(SelectedGameInstallation?.ManifestId.Value, installation.ManifestId.Value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (IsLockedWhileRunning(installation) || IsLockedWhileRunning(SelectedGameInstallation))
        {
            return;
        }

        if (IsStandaloneToolProfile)
        {
            _localNotificationService.ShowWarning(
                _localizationService.GetLocalizedString("GameProfiles.Game.ToolProfile.Title", "Tool profile"),
                _localizationService.GetLocalizedString("GameProfiles.Game.ToolProfile.Message", "Standalone tool profiles do not use a game installation."));
            return;
        }

        try
        {
            if (await ConfirmGameSwitchAsync(installation) &&
                !await IsLockedAfterConfirmationAsync(installation, SelectedGameInstallation))
            {
                await EnableContentInternal(installation);
            }
        }
        finally
        {
            RebuildProfileContentRows();
        }
    }

    [RelayCommand]
    private async Task SelectGameClientAsync(ContentDisplayItem? client)
    {
        if (client == null)
        {
            return;
        }

        var current = EnabledContent.FirstOrDefault(item => item.ContentType == ContentType.GameClient);
        if (current != null && string.Equals(current.ManifestId.Value, client.ManifestId.Value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (IsLockedWhileRunning(client) || IsLockedWhileRunning(current))
        {
            return;
        }

        try
        {
            if (current != null && _dialogService != null)
            {
                var confirmed = await _dialogService.ShowConfirmationAsync(
                    _localizationService.GetLocalizedString("GameProfiles.Game.ReplaceClient.Title", "Replace the game client?"),
                    _localizationService.GetLocalizedString(
                        "GameProfiles.Game.ReplaceClient.Message",
                        $"'{client.DisplayName}' will replace '{current.DisplayName}' as this profile's game client.",
                        client.DisplayName,
                        current.DisplayName),
                    _localizationService.GetLocalizedString("GameProfiles.Game.ReplaceClient.Confirm", "Replace"),
                    _localizationService.GetLocalizedString("Common.Cancel", "Cancel"));
                if (!confirmed)
                {
                    return;
                }
            }

            if (await IsLockedAfterConfirmationAsync(client, current))
            {
                return;
            }

            await EnableContentInternal(client);
        }
        finally
        {
            RebuildProfileContentRows();
        }
    }

    private bool IsLockedWhileRunning(ContentDisplayItem? item)
    {
        if (item == null || (!item.IsLocked && item.CanToggle))
        {
            return false;
        }

        _localNotificationService.ShowWarning(
            _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ContentLocked.Title", ContentLockedTitle),
            _localizationService.GetLocalizedString(
                "GameProfiles.Settings.Notification.ContentLocked.Message",
                $"'{item.DisplayName}' is locked and cannot be modified while the game is running.",
                item.DisplayName));
        return true;
    }

    private async Task<bool> IsLockedAfterConfirmationAsync(ContentDisplayItem item, ContentDisplayItem? current)
    {
        await RefreshHotswapStateAsync();
        return IsLockedWhileRunning(item) || IsLockedWhileRunning(current);
    }

    private async Task<bool> ConfirmGameSwitchAsync(ContentDisplayItem installation)
    {
        var newGame = installation.GameType;
        if (_dialogService == null || newGame == Core.Models.Enums.GameType.Unknown || newGame == GameTypeFilter)
        {
            return true;
        }

        var removed = EnabledContent.Any(item =>
            item.ContentType != ContentType.GameInstallation &&
            item.GameType != Core.Models.Enums.GameType.Unknown &&
            item.GameType != newGame);
        if (!removed)
        {
            return true;
        }

        return await _dialogService.ShowConfirmationAsync(
            _localizationService.GetLocalizedString("GameProfiles.Game.SwitchGame.Title", "Switch this profile's game?"),
            _localizationService.GetLocalizedString(
                "GameProfiles.Game.SwitchGame.Message",
                $"'{installation.DisplayName}' is a {GetGameName(newGame)} installation. Content selected for {GetGameName(GameTypeFilter)} will be removed from this profile.",
                installation.DisplayName,
                GetGameName(newGame),
                GetGameName(GameTypeFilter)),
            _localizationService.GetLocalizedString("GameProfiles.Game.SwitchGame.Confirm", "Switch game"),
            _localizationService.GetLocalizedString("Common.Cancel", "Cancel"));
    }

    private string GetGameName(Core.Models.Enums.GameType gameType) => gameType == Core.Models.Enums.GameType.Generals
        ? _localizationService.GetLocalizedString("Common.Game.Generals", "Generals")
        : _localizationService.GetLocalizedString("Common.Game.ZeroHour", "Zero Hour");
}
