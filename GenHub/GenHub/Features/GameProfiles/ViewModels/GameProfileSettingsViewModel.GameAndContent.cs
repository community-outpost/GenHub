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
    private List<string> _profileContentOrder = [];

    [ObservableProperty]
    private ObservableCollection<ProfileContentRow> _gameInstallationRows = [];

    [ObservableProperty]
    private ObservableCollection<ProfileContentRow> _gameClientRows = [];

    [ObservableProperty]
    private ObservableCollection<ProfileContentRow> _profileContentRows = [];

    [ObservableProperty]
    private ProfileContentFilterOption _selectedProfileContentFilter = ProfileContentFilterOption.All;

    [ObservableProperty]
    private ProfileContentSortOrder _selectedProfileContentSort = ProfileContentSortOrder.Name;

    /// <summary>
    /// Gets the content types in each Content page group, in the order the type sort lists them.
    /// </summary>
    public static IReadOnlyDictionary<ProfileContentGroup, IReadOnlyList<ContentType>> ProfileContentGroupTypes { get; } =
        new Dictionary<ProfileContentGroup, IReadOnlyList<ContentType>>
        {
            [ProfileContentGroup.Mods] = [ContentType.Mod],
            [ProfileContentGroup.Maps] = [ContentType.Map, ContentType.MapPack, ContentType.Mission],
            [ProfileContentGroup.Addons] = [ContentType.Addon, ContentType.Skin, ContentType.LanguagePack, ContentType.Patch],
            [ProfileContentGroup.Tools] = [ContentType.ModdingTool, ContentType.Executable],
        };

    /// <summary>
    /// Gets the content types listed on the Content page.
    /// </summary>
    public static IReadOnlyList<ContentType> ProfileContentTypes { get; } =
        [.. Enum.GetValues<ProfileContentGroup>().SelectMany(group => ProfileContentGroupTypes[group])];

    /// <summary>
    /// Gets the choices of the Content page filter.
    /// </summary>
    public static IReadOnlyList<ProfileContentFilterOption> ProfileContentFilterOptions { get; } =
        [ProfileContentFilterOption.All, .. Enum.GetValues<ProfileContentGroup>().Select(group => new ProfileContentFilterOption(group))];

    /// <summary>
    /// Gets a value indicating whether the sort menu offers sorting by type, which needs all content shown.
    /// </summary>
    public bool IsTypeSortAvailable => SelectedProfileContentFilter?.IsAll != false;

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
    /// Gets the Content page group a content type belongs to.
    /// </summary>
    /// <param name="type">The content type.</param>
    /// <returns>The group, or null when the type belongs to no group and only shows under all content.</returns>
    public static ProfileContentGroup? GetProfileContentGroup(ContentType type)
    {
        foreach (var (group, types) in ProfileContentGroupTypes)
        {
            if (types.Contains(type))
            {
                return group;
            }
        }

        return null;
    }

    /// <summary>
    /// Loads every installation, client and content item the Game and Content pages can offer.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected internal async Task LoadProfileContentCandidatesAsync()
    {
        if (_profileContentLoader == null)
        {
            RebuildProfileContentRows(resort: true);
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

        RebuildProfileContentRows(resort: true);
    }

    /// <summary>
    /// Rebuilds the rows of the Game and Content pages from the current profile state.
    /// Content rows keep their current order so a toggled row stays in place; new rows follow in sort order.
    /// </summary>
    /// <param name="resort">Whether to order every Content row by the selected sort instead of keeping the current order.</param>
    protected internal void RebuildProfileContentRows(bool resort = false)
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

        var filter = SelectedProfileContentFilter?.Group;
        var contentRows = BuildRows(
            enabledById,
            item => item.ContentType is not (ContentType.GameInstallation or ContentType.GameClient) &&
                    (filter == null || GetProfileContentGroup(item.ContentType) == filter) &&
                    (enabledById.ContainsKey(item.ManifestId.Value) ||
                     (ProfileContentTypes.Contains(item.ContentType) && IsForGame(item, gameType))));
        var orderedRows = OrderProfileContentRows(contentRows, resort);
        _profileContentOrder = orderedRows.Select(row => row.Item.ManifestId.Value).ToList();
        ProfileContentRows = new ObservableCollection<ProfileContentRow>(orderedRows);
        OnPropertyChanged(nameof(IsStandaloneToolProfile));
        OnPropertyChanged(nameof(CanSelectGameInstallation));
    }

    private static IEnumerable<ProfileContentRow> SortProfileContentRows(IEnumerable<ProfileContentRow> rows, ProfileContentSortOrder sort)
    {
        var byName = StringComparer.CurrentCultureIgnoreCase;
        return sort switch
        {
            ProfileContentSortOrder.ReleaseDate => rows
                .OrderBy(row => row.Item.ReleaseDate == null)
                .ThenByDescending(row => row.Item.ReleaseDate)
                .ThenBy(row => row.Item.DisplayName, byName),
            ProfileContentSortOrder.EnabledFirst => rows
                .OrderBy(row => !row.IsIncluded)
                .ThenBy(row => row.Item.DisplayName, byName),
            ProfileContentSortOrder.Type => rows
                .OrderBy(row => GetProfileContentTypeRank(row.Item.ContentType))
                .ThenBy(row => row.Item.DisplayName, byName),
            _ => rows.OrderBy(row => row.Item.DisplayName, byName),
        };
    }

    // ProfileContentTypes lists the groups in order and each group's types in order, so its index ranks group first, then type.
    private static int GetProfileContentTypeRank(ContentType type)
    {
        for (var i = 0; i < ProfileContentTypes.Count; i++)
        {
            if (ProfileContentTypes[i] == type)
            {
                return i;
            }
        }

        return ProfileContentTypes.Count;
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

    private List<ProfileContentRow> OrderProfileContentRows(List<ProfileContentRow> rows, bool resort)
    {
        var sort = SelectedProfileContentSort;
        if (resort || _profileContentOrder.Count == 0)
        {
            return SortProfileContentRows(rows, sort).ToList();
        }

        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _profileContentOrder.Count; i++)
        {
            positions.TryAdd(_profileContentOrder[i], i);
        }

        var kept = rows
            .Where(row => positions.ContainsKey(row.Item.ManifestId.Value))
            .OrderBy(row => positions[row.Item.ManifestId.Value]);
        var added = SortProfileContentRows(rows.Where(row => !positions.ContainsKey(row.Item.ManifestId.Value)), sort);
        return kept.Concat(added).ToList();
    }

    partial void OnSelectedProfileContentFilterChanged(ProfileContentFilterOption value)
    {
        if (!IsTypeSortAvailable && SelectedProfileContentSort == ProfileContentSortOrder.Type)
        {
            SelectedProfileContentSort = ProfileContentSortOrder.Name;
        }

        OnPropertyChanged(nameof(IsTypeSortAvailable));
        RebuildProfileContentRows(resort: true);
    }

    [RelayCommand]
    private void SetProfileContentSort(ProfileContentSortOrder order)
    {
        if (order == ProfileContentSortOrder.Type && !IsTypeSortAvailable)
        {
            return;
        }

        SelectedProfileContentSort = order;
    }

    partial void OnSelectedProfileContentSortChanged(ProfileContentSortOrder value)
    {
        RebuildProfileContentRows(resort: true);
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
