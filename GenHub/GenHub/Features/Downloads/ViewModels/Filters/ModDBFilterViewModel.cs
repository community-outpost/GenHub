using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for ModDB publisher with section-based category, license, and timeframe filters.
/// </summary>
public partial class ModDBFilterViewModel : FilterPanelViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadsSelected))]
    [NotifyPropertyChangedFor(nameof(IsAddonsSelected))]
    [NotifyPropertyChangedFor(nameof(IsModsSelected))]
    [NotifyPropertyChangedFor(nameof(ShowCategoryFilter))]
    [NotifyPropertyChangedFor(nameof(ShowLicenseFilter))]
    private ModDBSection _selectedSection = ModDBSection.Downloads;

    [ObservableProperty]
    private string? _selectedCategory;

    [ObservableProperty]
    private string? _selectedAddonCategory;

    [ObservableProperty]
    private string? _selectedLicense;

    [ObservableProperty]
    private string? _selectedTimeframe;

    [ObservableProperty]
    private string? _selectedSort = ModDBConstants.DefaultSort;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModDBFilterViewModel"/> class.
    /// </summary>
    public ModDBFilterViewModel()
    {
        InitializeDownloadsFilters();
        InitializeAddonsFilters();
        InitializeTimeframeOptions();
        InitializeLicenseOptions();
        InitializeSortOptions();
    }

    /// <inheritdoc />
    public override string PublisherId => ModDBConstants.PublisherType;

    /// <summary>
    /// Gets the available category options.
    /// </summary>
    public ObservableCollection<FilterOption> CategoryOptions { get; } = [];

    /// <summary>
    /// Gets the available addon category options.
    /// </summary>
    public ObservableCollection<FilterOption> AddonCategoryOptions { get; } = [];

    /// <summary>
    /// Gets the available license options.
    /// </summary>
    public ObservableCollection<FilterOption> LicenseOptions { get; } = [];

    /// <summary>
    /// Gets the available timeframe options.
    /// </summary>
    public ObservableCollection<FilterOption> TimeframeOptions { get; } = [];

    /// <summary>
    /// Gets the available sort options.
    /// </summary>
    public ObservableCollection<FilterOption> SortOptions { get; } = [];

    /// <inheritdoc />
    public override bool HasActiveFilters =>
        !string.IsNullOrEmpty(SelectedCategory) ||
        !string.IsNullOrEmpty(SelectedAddonCategory) ||
        !string.IsNullOrEmpty(SelectedLicense) ||
        !string.IsNullOrEmpty(SelectedTimeframe) ||
        (!string.IsNullOrEmpty(SelectedSort) && !string.Equals(SelectedSort, ModDBConstants.DefaultSort, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public override ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);

        // Set the section for URL building
        baseQuery.ModDBSection = SelectedSection switch
        {
            ModDBSection.Mods => "mods",
            ModDBSection.Addons => "addons",
            _ => "downloads",
        };

        // Apply Category filter (for Downloads and Mods sections)
        if (!string.IsNullOrEmpty(SelectedCategory))
        {
            baseQuery.ModDBCategory = SelectedCategory;
        }

        // Apply Addon Category filter
        if (!string.IsNullOrEmpty(SelectedAddonCategory))
        {
            // For Addons section, use "category" param; for Downloads/Mods, use "categoryaddon"
            baseQuery.ModDBAddonCategory = SelectedAddonCategory;
        }

        // Apply License filter (Addons section only)
        if (!string.IsNullOrEmpty(SelectedLicense))
        {
            baseQuery.ModDBLicense = SelectedLicense;
        }

        // Apply Timeframe filter
        if (!string.IsNullOrEmpty(SelectedTimeframe))
        {
            baseQuery.ModDBTimeframe = SelectedTimeframe;
        }

        // Apply Sort filter
        if (!string.IsNullOrEmpty(SelectedSort))
        {
            baseQuery.Sort = SelectedSort;
        }

        return baseQuery;
    }

    /// <inheritdoc />
    public override void ClearFilters()
    {
        SelectedCategory = null;
        SelectedAddonCategory = null;
        SelectedLicense = null;
        SelectedTimeframe = null;
        SelectedSort = ModDBConstants.DefaultSort;
        NotifyFiltersChanged();
        OnFiltersCleared();
    }

    /// <inheritdoc />
    public override IEnumerable<string> GetActiveFilterSummary()
    {
        if (!string.IsNullOrEmpty(SelectedCategory))
        {
            yield return $"Category: {SelectedCategory}";
        }

        if (!string.IsNullOrEmpty(SelectedAddonCategory))
        {
            yield return $"Addon: {SelectedAddonCategory}";
        }

        if (!string.IsNullOrEmpty(SelectedLicense))
        {
            yield return $"License: {SelectedLicense}";
        }

        if (!string.IsNullOrEmpty(SelectedTimeframe))
        {
            yield return $"Time: {SelectedTimeframe}";
        }

        if (!string.IsNullOrEmpty(SelectedSort) && !string.Equals(SelectedSort, ModDBConstants.DefaultSort, StringComparison.OrdinalIgnoreCase))
        {
            yield return $"Sort: {SelectedSort}";
        }
    }

    partial void OnSelectedCategoryChanged(string? value)
    {
        NotifyFiltersChanged();
    }

    partial void OnSelectedAddonCategoryChanged(string? value)
    {
        NotifyFiltersChanged();
    }

    partial void OnSelectedLicenseChanged(string? value)
    {
        NotifyFiltersChanged();
    }

    partial void OnSelectedTimeframeChanged(string? value)
    {
        NotifyFiltersChanged();
    }

    partial void OnSelectedSortChanged(string? value)
    {
        NotifyFiltersChanged();
    }

    [RelayCommand]
    private void SelectSort(FilterOption option)
    {
        SelectedSort = SelectedSort == option.Value ? ModDBConstants.DefaultSort : option.Value;
    }

    [RelayCommand]
    private void SelectCategory(FilterOption option)
    {
        SelectedCategory = SelectedCategory == option.Value ? null : option.Value;
    }

    [RelayCommand]
    private void SelectAddonCategory(FilterOption option)
    {
        SelectedAddonCategory = SelectedAddonCategory == option.Value ? null : option.Value;
    }

    [RelayCommand]
    private void SelectLicense(FilterOption option)
    {
        SelectedLicense = SelectedLicense == option.Value ? null : option.Value;
    }

    [RelayCommand]
    private void SelectTimeframe(FilterOption option)
    {
        SelectedTimeframe = SelectedTimeframe == option.Value ? null : option.Value;
    }

    [RelayCommand]
    private void SetSection(ModDBSection section)
    {
        if (SelectedSection == section) return;

        SelectedSection = section;
        ClearFilters();
    }

    partial void OnSelectedSectionChanged(ModDBSection value)
    {
        NotifyFiltersChanged();
        OnPropertyChanged(nameof(ShowCategoryFilter));
        OnPropertyChanged(nameof(ShowAddonCategoryFilter));
        OnPropertyChanged(nameof(ShowLicenseFilter));
        OnPropertyChanged(nameof(IsDownloadsSelected));
        OnPropertyChanged(nameof(IsAddonsSelected));
        OnPropertyChanged(nameof(IsModsSelected));
    }

    /// <summary>
    /// Gets a value indicating whether the Downloads section is selected.
    /// </summary>
    public bool IsDownloadsSelected => SelectedSection == ModDBSection.Downloads;

    /// <summary>
    /// Gets a value indicating whether the Addons section is selected.
    /// </summary>
    public bool IsAddonsSelected => SelectedSection == ModDBSection.Addons;

    /// <summary>
    /// Gets a value indicating whether the Mods section is selected.
    /// </summary>
    public bool IsModsSelected => SelectedSection == ModDBSection.Mods;

    /// <summary>
    /// Gets a value indicating whether to show the Category filter (Downloads and Mods sections).
    /// </summary>
    public bool ShowCategoryFilter => SelectedSection is ModDBSection.Downloads or ModDBSection.Mods;

    /// <summary>
    /// Gets a value indicating whether to show the Addon Category filter (Downloads, Mods, and Addons sections).
    /// </summary>
    public bool ShowAddonCategoryFilter => true; // All sections support addon filtering

    /// <summary>
    /// Gets a value indicating whether to show the License filter (Addons section only).
    /// </summary>
    public bool ShowLicenseFilter => SelectedSection == ModDBSection.Addons;

    private void InitializeDownloadsFilters()
    {
        // Category options for Downloads/Mods - form select name="category"
        CategoryOptions.Add(new FilterOption("Releases", "1"));
        CategoryOptions.Add(new FilterOption("Full Version", "2"));
        CategoryOptions.Add(new FilterOption("Demo", "3"));
        CategoryOptions.Add(new FilterOption("Patch", "4"));
        CategoryOptions.Add(new FilterOption("Script", "28"));
        CategoryOptions.Add(new FilterOption("Trainer", "29"));
        CategoryOptions.Add(new FilterOption("Media", "6"));
        CategoryOptions.Add(new FilterOption("Trailer", "7"));
        CategoryOptions.Add(new FilterOption("Movie", "8"));
        CategoryOptions.Add(new FilterOption("Music", "9"));
        CategoryOptions.Add(new FilterOption("Audio", "25"));
        CategoryOptions.Add(new FilterOption("Wallpaper", "10"));
        CategoryOptions.Add(new FilterOption("Tools", "11"));
        CategoryOptions.Add(new FilterOption("Archive Tool", "20"));
        CategoryOptions.Add(new FilterOption("Graphics Tool", "13"));
        CategoryOptions.Add(new FilterOption("Mapping Tool", "14"));
        CategoryOptions.Add(new FilterOption("Modelling Tool", "15"));
        CategoryOptions.Add(new FilterOption("Installer Tool", "16"));
        CategoryOptions.Add(new FilterOption("Server Tool", "17"));
        CategoryOptions.Add(new FilterOption("IDE", "18"));
        CategoryOptions.Add(new FilterOption("SDK", "19"));
        CategoryOptions.Add(new FilterOption("Source Code", "26"));
        CategoryOptions.Add(new FilterOption("RTX Remix", "31"));
        CategoryOptions.Add(new FilterOption("RTX.conf", "32"));
        CategoryOptions.Add(new FilterOption("Miscellaneous", "21"));
        CategoryOptions.Add(new FilterOption("Guide", "22"));
        CategoryOptions.Add(new FilterOption("Tutorial", "23"));
        CategoryOptions.Add(new FilterOption("Language Pack", "30"));
        CategoryOptions.Add(new FilterOption("Other", "24"));
    }

    private void InitializeAddonsFilters()
    {
        // Addon category options - form select name="categoryaddon" (Downloads) or "category" (Addons)
        AddonCategoryOptions.Add(new FilterOption("Maps", "100"));
        AddonCategoryOptions.Add(new FilterOption("Multiplayer Map", "101"));
        AddonCategoryOptions.Add(new FilterOption("Singleplayer Map", "102"));
        AddonCategoryOptions.Add(new FilterOption("Prefab", "103"));
        AddonCategoryOptions.Add(new FilterOption("Models", "104"));
        AddonCategoryOptions.Add(new FilterOption("Player Model", "106"));
        AddonCategoryOptions.Add(new FilterOption("Prop Model", "132"));
        AddonCategoryOptions.Add(new FilterOption("Vehicle Model", "107"));
        AddonCategoryOptions.Add(new FilterOption("Weapon Model", "108"));
        AddonCategoryOptions.Add(new FilterOption("Model Pack", "131"));
        AddonCategoryOptions.Add(new FilterOption("Skins", "110"));
        AddonCategoryOptions.Add(new FilterOption("Player Skin", "112"));
        AddonCategoryOptions.Add(new FilterOption("Prop Skin", "133"));
        AddonCategoryOptions.Add(new FilterOption("Vehicle Skin", "113"));
        AddonCategoryOptions.Add(new FilterOption("Weapon Skin", "114"));
        AddonCategoryOptions.Add(new FilterOption("Skin Pack", "134"));
        AddonCategoryOptions.Add(new FilterOption("Audio", "116"));
        AddonCategoryOptions.Add(new FilterOption("Music", "117"));
        AddonCategoryOptions.Add(new FilterOption("Player Audio", "119"));
        AddonCategoryOptions.Add(new FilterOption("Audio Pack", "118"));
        AddonCategoryOptions.Add(new FilterOption("Graphics", "123"));
        AddonCategoryOptions.Add(new FilterOption("Decal", "124"));
        AddonCategoryOptions.Add(new FilterOption("Effects GFX", "136"));
        AddonCategoryOptions.Add(new FilterOption("GUI", "125"));
        AddonCategoryOptions.Add(new FilterOption("HUD", "126"));
        AddonCategoryOptions.Add(new FilterOption("Sprite", "128"));
        AddonCategoryOptions.Add(new FilterOption("Texture", "129"));
    }

    private void InitializeLicenseOptions()
    {
        LicenseOptions.Add(new FilterOption("BSD", "7"));
        LicenseOptions.Add(new FilterOption("Commercial", "1"));
        LicenseOptions.Add(new FilterOption("Creative Commons", "2"));
        LicenseOptions.Add(new FilterOption("GPL", "5"));
        LicenseOptions.Add(new FilterOption("L-GPL", "6"));
        LicenseOptions.Add(new FilterOption("MIT", "8"));
        LicenseOptions.Add(new FilterOption("Zlib", "9"));
        LicenseOptions.Add(new FilterOption("Proprietary", "3"));
        LicenseOptions.Add(new FilterOption("Public Domain", "4"));
    }

    private void InitializeTimeframeOptions()
    {
        TimeframeOptions.Add(new FilterOption("Past 24 hours", "1"));
        TimeframeOptions.Add(new FilterOption("Past week", "2"));
        TimeframeOptions.Add(new FilterOption("Past month", "3"));
        TimeframeOptions.Add(new FilterOption("Past year", "4"));
        TimeframeOptions.Add(new FilterOption("Year or older", "5"));
    }

    private void InitializeSortOptions()
    {
        SortOptions.Add(new FilterOption("Newest First", ModDBConstants.SortDateDesc));
        SortOptions.Add(new FilterOption("Most Popular", ModDBConstants.SortVisitDesc));
        SortOptions.Add(new FilterOption("Highest Rated", ModDBConstants.SortRatingDesc));
        SortOptions.Add(new FilterOption("Name (A-Z)", ModDBConstants.SortNameAsc));
        SortOptions.Add(new FilterOption("Name (Z-A)", ModDBConstants.SortNameDesc));
        SortOptions.Add(new FilterOption("Oldest First", ModDBConstants.SortDateAsc));
    }
}
