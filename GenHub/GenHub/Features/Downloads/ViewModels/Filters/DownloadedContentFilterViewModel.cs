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
/// Filter view model for the offline downloaded-content library ("My Downloads").
/// Provides content-type and target-game filtering over locally stored manifests.
/// </summary>
public sealed partial class DownloadedContentFilterViewModel : FilterPanelViewModelBase, IDisposable
{
    private readonly ILocalizationService? _localizationService;
    private bool _disposed;

    [ObservableProperty]
    private ContentType? _selectedContentType;

    [ObservableProperty]
    private GameType? _selectedGame;

    [ObservableProperty]
    private ObservableCollection<ContentTypeFilterItem> _contentTypeFilters = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadedContentFilterViewModel"/> class.
    /// </summary>
    /// <param name="localizationService">Optional localization service for filter labels.</param>
    public DownloadedContentFilterViewModel(ILocalizationService? localizationService = null)
    {
        _localizationService = localizationService;
        ContentTypeFilters = CreateContentTypeFilters();
        if (_localizationService != null)
        {
            _localizationService.PropertyChanged += OnLocalizationChanged;
        }
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
            yield return $"{ResolveLabel("Downloads.Filter.ContentType", "Content Type")}: {ResolveContentTypeName(SelectedContentType.Value)}";
        }

        if (SelectedGame.HasValue)
        {
            yield return $"{ResolveLabel("Downloads.Filter.Game", "Game")}: {ResolveGameName(SelectedGame.Value)}";
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
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

    private ObservableCollection<ContentTypeFilterItem> CreateContentTypeFilters()
    {
        return
        [
            new ContentTypeFilterItem(ContentType.GameClient, ResolveContentTypeName(ContentType.GameClient)),
            new ContentTypeFilterItem(ContentType.Mod, ResolveContentTypeName(ContentType.Mod)),
            new ContentTypeFilterItem(ContentType.Map, ResolveContentTypeName(ContentType.Map)),
            new ContentTypeFilterItem(ContentType.MapPack, ResolveContentTypeName(ContentType.MapPack)),
            new ContentTypeFilterItem(ContentType.Mission, ResolveContentTypeName(ContentType.Mission)),
            new ContentTypeFilterItem(ContentType.Patch, ResolveContentTypeName(ContentType.Patch)),
            new ContentTypeFilterItem(ContentType.Addon, ResolveContentTypeName(ContentType.Addon)),
            new ContentTypeFilterItem(ContentType.LanguagePack, ResolveContentTypeName(ContentType.LanguagePack)),
            new ContentTypeFilterItem(ContentType.ModdingTool, ResolveContentTypeName(ContentType.ModdingTool)),
            new ContentTypeFilterItem(ContentType.Skin, ResolveContentTypeName(ContentType.Skin)),
        ];
    }

    private string ResolveContentTypeName(ContentType contentType)
    {
        if (_localizationService != null && _localizationService.TryGetString($"ContentType.{contentType}", out var localized))
        {
            return localized;
        }

        return contentType.GetDisplayName();
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

    private string ResolveLabel(string key, string fallback)
    {
        if (_localizationService != null && _localizationService.TryGetString(key, out var localized))
        {
            return localized;
        }

        return fallback;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (e.PropertyName == nameof(ILocalizationService.CurrentCulture) || e.PropertyName == LocalizationConstants.IndexerPropertyName)
        {
            RefreshLabels();
        }
    }

    private void RefreshLabels()
    {
        var selected = SelectedContentType;
        ContentTypeFilters = CreateContentTypeFilters();
        if (selected.HasValue)
        {
            var match = ContentTypeFilters.FirstOrDefault(item => item.ContentType == selected.Value);
            if (match != null)
            {
                match.IsSelected = true;
            }
        }

        NotifyFiltersChanged();
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
        // Derive from the selection, not the toggle state: the IsSelected binding
        // updates before the command runs, so item.IsSelected already reflects the click.
        if (SelectedContentType == item.ContentType)
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
