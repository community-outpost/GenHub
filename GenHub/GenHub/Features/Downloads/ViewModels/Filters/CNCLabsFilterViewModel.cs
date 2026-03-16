using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for CNCLabs publisher with map tag toggle filters.
/// </summary>
public partial class CNCLabsFilterViewModel : FilterPanelViewModelBase
{
    /// <inheritdoc />
    public override string PublisherId => PublisherTypeConstants.CncLabs;

    [ObservableProperty]
    private GameType? _targetGame = GameType.ZeroHour;

    /// <summary>
    /// Gets the collection of map tag filter items.
    /// </summary>
    public ObservableCollection<MapTagFilterItem> MapTagFilters { get; } =
    [
        new MapTagFilterItem(CNCLabsConstants.TagCramped, "1", "Map size"),
        new MapTagFilterItem(CNCLabsConstants.TagSpacious, "2", "Map size"),
        new MapTagFilterItem(CNCLabsConstants.TagWellBalanced, "3", "Layout"),
        new MapTagFilterItem(CNCLabsConstants.TagMoneyMap, "4", "Economy"),
        new MapTagFilterItem(CNCLabsConstants.TagDetailed, "5", "Quality"),
        new MapTagFilterItem(CNCLabsConstants.TagCustomScripted, "6", "Features"),
        new MapTagFilterItem(CNCLabsConstants.TagSymmetric, "7", "Layout"),
        new MapTagFilterItem(CNCLabsConstants.TagArtOfDefense, "8", "Mode"),
        new MapTagFilterItem(CNCLabsConstants.TagMultiplayerOnly, "9", "Mode"),
        new MapTagFilterItem(CNCLabsConstants.TagAsymmetric, "10", "Layout"),
        new MapTagFilterItem(CNCLabsConstants.TagNoobFriendly, "11", "Difficulty"),
        new MapTagFilterItem(CNCLabsConstants.TagVeteranSuitable, "12", "Difficulty"),
        new MapTagFilterItem(CNCLabsConstants.TagFunMap, "13", "Style"),
        new MapTagFilterItem(CNCLabsConstants.TagArtOfAttack, "14", "Mode"),
        new MapTagFilterItem(CNCLabsConstants.TagShellMap, "15", "Type"),
        new MapTagFilterItem(CNCLabsConstants.TagPortedMissionToZH, "16", "Type"),
        new MapTagFilterItem(CNCLabsConstants.TagCustomCoded, "17", "Features"),
        new MapTagFilterItem(CNCLabsConstants.TagCoopMission, "18", "Mode"),
    ];

    [ObservableProperty]
    private ContentType? _selectedContentType = ContentType.Map; // Default to Map

    [ObservableProperty]
    private int? _numberOfPlayers;

    /// <summary>
    /// Gets the available player count options.
    /// </summary>
    public ObservableCollection<PlayerOption> PlayerOptions { get; } =
    [
        new PlayerOption(CNCLabsConstants.PlayerOptionAny, null),
        new PlayerOption(CNCLabsConstants.PlayerOption1Player, 1),
        new PlayerOption(CNCLabsConstants.PlayerOption2Players, 2),
        new PlayerOption(CNCLabsConstants.PlayerOption3Players, 3),
        new PlayerOption(CNCLabsConstants.PlayerOption4Players, 4),
        new PlayerOption(CNCLabsConstants.PlayerOption5Players, 5),
        new PlayerOption(CNCLabsConstants.PlayerOption6Players, 6),
    ];

    [ObservableProperty]
    private PlayerOption? _selectedPlayerOption;

    partial void OnSelectedPlayerOptionChanged(PlayerOption? value)
    {
        NumberOfPlayers = value?.Value;
    }

    /// <summary>
    /// Gets the collection of content-type filter items (Patch, Map, etc.).
    /// </summary>
    public ObservableCollection<ContentTypeFilterItem> ContentTypeFilters { get; } =
    [
        new ContentTypeFilterItem(ContentType.Map, CNCLabsConstants.ContentTypeMaps) { IsSelected = true },
        new ContentTypeFilterItem(ContentType.Mission, CNCLabsConstants.ContentTypeMissions),
    ];

    /// <summary>
    /// Gets the active (selected) map tags.
    /// </summary>
    public IEnumerable<string> ActiveTags => MapTagFilters
        .Where(t => t.IsSelected)
        .Select(t => t.Tag);

    /// <inheritdoc />
    public override bool HasActiveFilters => MapTagFilters.Any(t => t.IsSelected) || TargetGame.HasValue || NumberOfPlayers.HasValue;

    /// <inheritdoc />
    public override ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);

        if (SelectedContentType.HasValue)
        {
            baseQuery.ContentType = SelectedContentType.Value;
        }

        // Apply Game Filter
        if (TargetGame.HasValue)
        {
            baseQuery.TargetGame = TargetGame.Value;
        }

        // Apply Player Count Filter
        if (NumberOfPlayers.HasValue)
        {
            baseQuery.NumberOfPlayers = NumberOfPlayers.Value;
        }

        // Apply Map Tags
        foreach (var tag in ActiveTags)
        {
            baseQuery.CNCLabsMapTags.Add(tag);
        }

        return baseQuery;
    }

    /// <inheritdoc />
    public override void ClearFilters()
    {
        TargetGame = GameType.ZeroHour; // Reset to default
        SelectedContentType = ContentType.Map; // Reset to default
        NumberOfPlayers = null;
        SelectedPlayerOption = PlayerOptions[0]; // Reset to "Any"

        foreach (var filter in ContentTypeFilters)
        {
            filter.IsSelected = filter.ContentType == ContentType.Map; // Reset Maps to selected
        }

        foreach (var tag in MapTagFilters)
        {
            tag.IsSelected = false;
        }

        NotifyFiltersChanged();
        OnPropertyChanged(nameof(ActiveTags));
        OnFiltersCleared();
    }

    /// <inheritdoc />
    public override IEnumerable<string> GetActiveFilterSummary()
    {
        if (TargetGame.HasValue)
        {
            yield return $"Game: {TargetGame}";
        }

        var activeTags = ActiveTags.ToList();
        if (activeTags.Count > 0)
        {
            yield return $"Tags: {string.Join(", ", activeTags)}";
        }
    }

    [RelayCommand]
    private void SetGame(GameType? game)
    {
        TargetGame = game;
        OnPropertyChanged(nameof(IsZeroHourSelected));
        OnPropertyChanged(nameof(IsGeneralsSelected));

        // REMOVED: Auto-refresh on game change
        // NotifyFiltersChanged();
    }

    /// <summary>
    /// Gets or sets a value indicating whether Zero Hour is selected.
    /// </summary>
    public bool IsZeroHourSelected
    {
        get => TargetGame == GameType.ZeroHour;
        set => SetGame(value ? GameType.ZeroHour : null);
    }

    /// <summary>
    /// Gets or sets a value indicating whether Generals is selected.
    /// </summary>
    public bool IsGeneralsSelected
    {
        get => TargetGame == GameType.Generals;
        set => SetGame(value ? GameType.Generals : null);
    }

    [RelayCommand]
    private void ToggleTag(MapTagFilterItem item)
    {
        // Don't toggle IsSelected here - TwoWay binding on IsChecked already handles it

        // REMOVED: Auto-refresh on tag toggle
        // NotifyFiltersChanged();
        OnPropertyChanged(nameof(ActiveTags));
    }

    [RelayCommand]
    private void ToggleContentType(ContentTypeFilterItem item)
    {
        // Enforce radio button behavior: deselect all others
        foreach (var filter in ContentTypeFilters)
        {
            if (filter != item)
            {
                filter.IsSelected = false;
            }
        }

        // Ensure current is selected (in case it was already selected, it stays selected)
        item.IsSelected = true;
        SelectedContentType = item.ContentType;

        // REMOVED: Auto-refresh on content type change
        // NotifyFiltersChanged();
    }
}
