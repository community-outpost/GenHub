using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for the offline downloaded-content library ("My Downloads").
/// Provides content-type and target-game filtering over locally stored manifests.
/// </summary>
public partial class DownloadedContentFilterViewModel : FilterPanelViewModelBase
{
    [ObservableProperty]
    private ContentType? _selectedContentType;

    [ObservableProperty]
    private GameType? _selectedGame;

    [ObservableProperty]
    private ObservableCollection<ContentTypeFilterItem> _contentTypeFilters = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadedContentFilterViewModel"/> class.
    /// </summary>
    public DownloadedContentFilterViewModel()
    {
        ContentTypeFilters = CreateDefaultContentTypeFilters();
    }

    /// <inheritdoc />
    public override string PublisherId => PublisherTypeConstants.Downloaded;

    /// <inheritdoc />
    public override bool HasActiveFilters => SelectedContentType.HasValue || SelectedGame.HasValue;

    /// <summary>
    /// Gets or sets a value indicating whether Zero Hour is selected.
    /// </summary>
    public bool IsZeroHourSelected
    {
        get => SelectedGame == GameType.ZeroHour;
        set => SetGame(value ? GameType.ZeroHour : null);
    }

    /// <summary>
    /// Gets or sets a value indicating whether Generals is selected.
    /// </summary>
    public bool IsGeneralsSelected
    {
        get => SelectedGame == GameType.Generals;
        set => SetGame(value ? GameType.Generals : null);
    }

    /// <inheritdoc />
    public override ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);

        baseQuery.ContentType = SelectedContentType;

        // The browser seeds TargetGame with the default game; an unset game filter
        // must clear it so every stored game is visible.
        baseQuery.TargetGame = SelectedGame;

        return baseQuery;
    }

    /// <inheritdoc />
    public override void ClearFilters()
    {
        SelectedContentType = null;
        SelectedGame = null;
        foreach (var filter in ContentTypeFilters)
        {
            filter.IsSelected = false;
        }

        NotifyFiltersChanged();
        OnPropertyChanged(nameof(IsZeroHourSelected));
        OnPropertyChanged(nameof(IsGeneralsSelected));
        OnFiltersCleared();
    }

    /// <inheritdoc />
    public override IEnumerable<string> GetActiveFilterSummary()
    {
        if (SelectedContentType.HasValue)
        {
            yield return $"Type: {SelectedContentType.Value}";
        }

        if (SelectedGame.HasValue)
        {
            yield return $"Game: {SelectedGame.Value}";
        }
    }

    private static ObservableCollection<ContentTypeFilterItem> CreateDefaultContentTypeFilters()
    {
        return
        [
            new ContentTypeFilterItem(ContentType.GameClient, "Game clients"),
            new ContentTypeFilterItem(ContentType.Mod, "Mods"),
            new ContentTypeFilterItem(ContentType.Map, "Maps"),
            new ContentTypeFilterItem(ContentType.MapPack, "Map packs"),
            new ContentTypeFilterItem(ContentType.Mission, "Missions"),
            new ContentTypeFilterItem(ContentType.Patch, "Patches"),
            new ContentTypeFilterItem(ContentType.Addon, "Add-ons"),
            new ContentTypeFilterItem(ContentType.LanguagePack, "Language packs"),
            new ContentTypeFilterItem(ContentType.ModdingTool, "Modding tools"),
            new ContentTypeFilterItem(ContentType.Skin, "Skins"),
        ];
    }

    [RelayCommand]
    private void SetGame(GameType? game)
    {
        SelectedGame = game;
        OnPropertyChanged(nameof(IsZeroHourSelected));
        OnPropertyChanged(nameof(IsGeneralsSelected));
        NotifyFiltersChanged();
    }

    [RelayCommand]
    private void ToggleContentType(ContentTypeFilterItem item)
    {
        if (item.IsSelected)
        {
            // Deselect - clear filter
            item.IsSelected = false;
            SelectedContentType = null;
        }
        else
        {
            // Select this type, deselect others
            foreach (var filter in ContentTypeFilters)
            {
                filter.IsSelected = filter == item;
            }

            SelectedContentType = item.ContentType;
        }

        NotifyFiltersChanged();
    }
}
