using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
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
[SuppressMessage("Naming", "S101:Types should be named in PascalCase", Justification = "CNCLabs is an established domain name")]
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
        new MapTagFilterItem(CNCLabsConstants.TagCoopMission, "19", "Mode"),
    ];

    [ObservableProperty]
    private ContentType? _selectedContentType = ContentType.Map; // Default to Map

    /// <summary>
    /// Gets or sets the selected player count (2–8 on the maps listing; the site
    /// returns 404 for 1-player content, so that option is intentionally excluded).
    /// </summary>
    [ObservableProperty]
    private int? _selectedPlayerCount;

    [RelayCommand]
    private void SetPlayerCount(string? count)
    {
        // CommandParameter arrives as a string from XAML ("2".."8", or null for "Any").
        int? parsed = int.TryParse(count, out var n) ? n : null;
        if (SelectedPlayerCount == parsed)
        {
            return;
        }

        SelectedPlayerCount = parsed;
        OnPropertyChanged(nameof(HasActiveFilters));
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
    public override bool HasActiveFilters => MapTagFilters.Any(t => t.IsSelected) || (TargetGame.HasValue && TargetGame.Value != GameType.ZeroHour) || SelectedPlayerCount.HasValue;

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
        if (SelectedPlayerCount.HasValue)
        {
            baseQuery.NumberOfPlayers = SelectedPlayerCount.Value;
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
        SelectedPlayerCount = null;

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
        if (TargetGame.HasValue && TargetGame.Value != GameType.ZeroHour)
        {
            yield return $"Game: {TargetGame}";
        }

        var activeTags = ActiveTags.ToList();
        if (activeTags.Count > 0)
        {
            yield return $"Tags: {string.Join(", ", activeTags)}";
        }

        if (SelectedPlayerCount.HasValue)
        {
            yield return $"{SelectedPlayerCount.Value} Players";
        }
    }

    [RelayCommand]
    private void SetGame(GameType? game)
    {
        TargetGame = game;
        OnPropertyChanged(nameof(IsZeroHourSelected));
        OnPropertyChanged(nameof(IsGeneralsSelected));
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
        OnPropertyChanged(nameof(ActiveTags));
    }

    [RelayCommand]
    private void ToggleContentType(ContentTypeFilterItem item)
    {
        // Enforce radio button behavior: deselect all others
        foreach (var filter in ContentTypeFilters.Where(f => f != item))
        {
            filter.IsSelected = false;
        }

        // Ensure current is selected (in case it was already selected, it stays selected)
        item.IsSelected = true;
        SelectedContentType = item.ContentType;
    }
}
