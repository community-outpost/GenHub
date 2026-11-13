using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for the Steam Workshop publisher with target-game and sort filters.
/// </summary>
public sealed partial class SteamWorkshopFilterViewModel : FilterPanelViewModelBase, IDisposable
{
    private readonly ILocalizationService? _localizationService;
    private bool _disposed;
    private ObservableCollection<FilterOption> _sortOptions;

    [ObservableProperty]
    private GameType? _targetGame = GameType.ZeroHour;

    [ObservableProperty]
    private string? _selectedSort = SteamWorkshopConstants.DefaultSort;

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamWorkshopFilterViewModel"/> class.
    /// </summary>
    /// <param name="localizationService">Optional localization service for filter labels.</param>
    public SteamWorkshopFilterViewModel(ILocalizationService? localizationService = null)
    {
        _localizationService = localizationService;
        _sortOptions = CreateSortOptions();
        if (_localizationService != null)
        {
            _localizationService.PropertyChanged += OnLocalizationChanged;
        }
    }

    /// <summary>
    /// Gets or sets the available sort options.
    /// </summary>
    public ObservableCollection<FilterOption> SortOptions
    {
        get => _sortOptions;

        set => SetProperty(ref _sortOptions, value);
    }

    /// <inheritdoc />
    public override string PublisherId => SteamWorkshopConstants.PublisherType;

    /// <summary>
    /// Gets the collection of workshop tag filter items.
    /// </summary>
    public ObservableCollection<MapTagFilterItem> TagFilters { get; } =
        new(SteamWorkshopConstants.WorkshopTagFilters.Select(tag => new MapTagFilterItem(tag, tag, string.Empty)));

    /// <summary>
    /// Gets the active (selected) workshop tags.
    /// </summary>
    public IReadOnlyCollection<string> ActiveTags => TagFilters
        .Where(t => t.IsSelected)
        .Select(t => t.Tag)
        .ToArray();

    /// <inheritdoc />
    public override bool HasActiveFilters =>
        (TargetGame.HasValue && TargetGame.Value != GameType.ZeroHour) ||
        (!string.IsNullOrEmpty(SelectedSort) && !string.Equals(SelectedSort, SteamWorkshopConstants.DefaultSort, StringComparison.OrdinalIgnoreCase)) ||
        TagFilters.Any(t => t.IsSelected);

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

    /// <inheritdoc />
    public override ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);

        if (TargetGame.HasValue)
        {
            baseQuery.TargetGame = TargetGame.Value;
        }

        if (!string.IsNullOrEmpty(SelectedSort))
        {
            baseQuery.SteamWorkshopSort = SelectedSort;
        }

        foreach (var tag in ActiveTags)
        {
            baseQuery.SteamWorkshopRequiredTags.Add(tag);
        }

        return baseQuery;
    }

    /// <inheritdoc />
    public override void ClearFilters()
    {
        SetGame(GameType.ZeroHour);
        SelectedSort = SteamWorkshopConstants.DefaultSort;

        foreach (var tag in TagFilters)
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
            yield return $"{_localizationService.GetLocalizedString("Downloads.Filter.Game", "Game")}: {ResolveGameName(TargetGame.Value)}";
        }

        if (!string.IsNullOrEmpty(SelectedSort) && !string.Equals(SelectedSort, SteamWorkshopConstants.DefaultSort, StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{_localizationService.GetLocalizedString("Downloads.Filter.Sort", "Sort")}: {ResolveSortName(SelectedSort)}";
        }

        var activeTags = ActiveTags.ToList();
        if (activeTags.Count > 0)
        {
            yield return $"{_localizationService.GetLocalizedString("Downloads.Filter.Tags", "Tags")}: {string.Join(", ", activeTags)}";
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    private void SetGame(GameType? game)
    {
        TargetGame = game;
        OnPropertyChanged(nameof(IsZeroHourSelected));
        OnPropertyChanged(nameof(IsGeneralsSelected));
        NotifyFiltersChanged();
    }

    [RelayCommand]
    private void ToggleTag(MapTagFilterItem item)
    {
        // Don't toggle IsSelected here - TwoWay binding on IsChecked already handles it
        OnPropertyChanged(nameof(ActiveTags));
        NotifyFiltersChanged();
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing && _localizationService != null)
        {
            _localizationService.PropertyChanged -= OnLocalizationChanged;
        }

        _disposed = true;
    }

    private ObservableCollection<FilterOption> CreateSortOptions()
    {
        return
        [
            new FilterOption(ResolveSortName(SteamWorkshopConstants.SortTrend), SteamWorkshopConstants.SortTrend),
            new FilterOption(ResolveSortName(SteamWorkshopConstants.SortMostRecent), SteamWorkshopConstants.SortMostRecent),
            new FilterOption(ResolveSortName(SteamWorkshopConstants.SortLastUpdated), SteamWorkshopConstants.SortLastUpdated),
            new FilterOption(ResolveSortName(SteamWorkshopConstants.SortMostSubscribed), SteamWorkshopConstants.SortMostSubscribed),
            new FilterOption(ResolveSortName(SteamWorkshopConstants.SortTopRated), SteamWorkshopConstants.SortTopRated),
        ];
    }

    private string ResolveSortName(string sort)
    {
        var key = sort switch
        {
            SteamWorkshopConstants.SortTrend => "Downloads.Filter.SteamWorkshop.Sort.Trending",
            SteamWorkshopConstants.SortMostRecent => "Downloads.Filter.SteamWorkshop.Sort.MostRecent",
            SteamWorkshopConstants.SortLastUpdated => "Downloads.Filter.SteamWorkshop.Sort.LastUpdated",
            SteamWorkshopConstants.SortMostSubscribed => "Downloads.Filter.SteamWorkshop.Sort.MostSubscribed",
            SteamWorkshopConstants.SortTopRated => "Downloads.Filter.SteamWorkshop.Sort.TopRated",
            _ => null,
        };

        if (key != null && _localizationService != null && _localizationService.TryGetString(key, out var localized))
        {
            return localized;
        }

        return sort switch
        {
            SteamWorkshopConstants.SortTrend => "Trending",
            SteamWorkshopConstants.SortMostRecent => "Most recent",
            SteamWorkshopConstants.SortLastUpdated => "Last updated",
            SteamWorkshopConstants.SortMostSubscribed => "Most subscribed",
            SteamWorkshopConstants.SortTopRated => "Top rated",
            _ => sort,
        };
    }

    private string ResolveGameName(GameType game)
    {
        var key = game switch
        {
            GameType.ZeroHour => "Common.Game.ZeroHour",
            GameType.Generals => "Common.Game.Generals",
            _ => null,
        };

        if (key != null && _localizationService != null && _localizationService.TryGetString(key, out var localized))
        {
            return localized;
        }

        return game.ToString();
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (e.PropertyName == nameof(ILocalizationService.CurrentCulture) || e.PropertyName == LocalizationConstants.IndexerPropertyName)
        {
            var selected = SelectedSort;
            SortOptions = CreateSortOptions();
            SelectedSort = SortOptions.Any(option => string.Equals(option.Value, selected, StringComparison.OrdinalIgnoreCase))
                ? selected
                : SteamWorkshopConstants.DefaultSort;
            NotifyFiltersChanged();
        }
    }

    partial void OnSelectedSortChanged(string? value)
    {
        NotifyFiltersChanged();
    }
}
