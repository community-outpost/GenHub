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
    private ModDBSection _selectedSection = ModDBSection.Downloads;

    [ObservableProperty]
    private string? _selectedCategory;

    [ObservableProperty]
    private string? _selectedAddonCategory;

    [ObservableProperty]
    private string? _selectedLicense;

    [ObservableProperty]
    private string? _selectedTimeframe;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModDBFilterViewModel"/> class.
    /// <summary>
    /// Creates a ModDBFilterViewModel and populates its category, addon category, timeframe, and license option collections.
    /// </summary>
    public ModDBFilterViewModel()
    {
        InitializeDownloadsFilters();
        InitializeAddonsFilters();
        InitializeTimeframeOptions();
        InitializeLicenseOptions();
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

    /// <inheritdoc />
    public override bool HasActiveFilters =>
        !string.IsNullOrEmpty(SelectedCategory) ||
        !string.IsNullOrEmpty(SelectedAddonCategory) ||
        !string.IsNullOrEmpty(SelectedLicense) ||
        !string.IsNullOrEmpty(SelectedTimeframe);

    /// <summary>
    /// Applies the view model's selected ModDB section and filters (category, addon category, license, timeframe) to the provided ContentSearchQuery.
    /// </summary>
    /// <param name="baseQuery">The query to modify; must not be null.</param>
    /// <returns>The same <see cref="ContentSearchQuery"/> instance with ModDBSection, ModDBCategory, ModDBAddonCategory, ModDBLicense, and ModDBTimeframe set according to the current selections.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseQuery"/> is null.</exception>
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

        return baseQuery;
    }

    /// <summary>
    /// Clears all selected ModDB filters (category, addon category, license, and timeframe) and notifies observers of the change.
    /// </summary>
    /// <remarks>
    /// Invokes <see cref="NotifyFiltersChanged"/> and <see cref="OnFiltersCleared"/> after resetting the selections.
    /// </remarks>
    public override void ClearFilters()
    {
        SelectedCategory = null;
        SelectedAddonCategory = null;
        SelectedLicense = null;
        SelectedTimeframe = null;
        NotifyFiltersChanged();
        OnFiltersCleared();
    }

    /// <summary>
    /// Produces a concise summary for each currently selected ModDB filter.
    /// </summary>
    /// <returns>An enumeration of human-readable summary strings for each active filter (category, addon category, license, timeframe); empty if no filters are selected.</returns>
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
    }

    /// <summary>
/// Called when the SelectedCategory property changes to notify observers and react to the new selection.
/// </summary>
/// <param name="value">The new selected category value, or null if the selection was cleared.</param>
partial void OnSelectedCategoryChanged(string? value) { }

    /// <summary>
/// Invoked when the SelectedAddonCategory property changes to allow responding to the new selection.
/// </summary>
/// <param name="value">The new selected addon category, or null if the selection was cleared.</param>
partial void OnSelectedAddonCategoryChanged(string? value) { }

    /// <summary>
/// Invoked when the SelectedLicense property changes; receives the new selected license value or null.
/// </summary>
/// <param name="value">The new license value, or null if no license is selected.</param>
partial void OnSelectedLicenseChanged(string? value) { }

    /// <summary>
/// Called when the SelectedTimeframe property value changes.
/// </summary>
/// <param name="value">The new SelectedTimeframe value, or null if cleared.</param>
partial void OnSelectedTimeframeChanged(string? value) { }

    /// <summary>
    /// Toggles the current category selection: selects the given option's value or clears the selection if it's already selected.
    /// </summary>
    /// <param name="option">The category option to select or deselect; its Value is applied to SelectedCategory or cleared if already active.</param>
    [RelayCommand]
    private void SelectCategory(FilterOption option)
    {
        SelectedCategory = SelectedCategory == option.Value ? null : option.Value;
    }

    /// <summary>
    /// Sets the active addon category filter to the provided option's value, or clears it if that option is already selected.
    /// </summary>
    /// <param name="option">The addon category option to toggle; its Value becomes the active SelectedAddonCategory or is cleared if already active.</param>
    [RelayCommand]
    private void SelectAddonCategory(FilterOption option)
    {
        SelectedAddonCategory = SelectedAddonCategory == option.Value ? null : option.Value;
    }

    /// <summary>
    /// Sets SelectedLicense to the provided option's value, or clears SelectedLicense if that value is already selected.
    /// </summary>
    /// <param name="option">The license option to select or toggle; its Value is stored in SelectedLicense.</param>
    [RelayCommand]
    private void SelectLicense(FilterOption option)
    {
        SelectedLicense = SelectedLicense == option.Value ? null : option.Value;
    }

    /// <summary>
    /// Toggles the current timeframe selection between the provided option and cleared state.
    /// </summary>
    /// <param name="option">The timeframe option to select; if it is already selected the timeframe is cleared.</param>
    [RelayCommand]
    private void SelectTimeframe(FilterOption option)
    {
        SelectedTimeframe = SelectedTimeframe == option.Value ? null : option.Value;
    }

    /// <summary>
    /// Sets the active ModDB section; when the section changes, clears all currently selected filters.
    /// </summary>
    /// <param name="section">The ModDB section to select.</param>
    [RelayCommand]
    private void SetSection(ModDBSection section)
    {
        if (SelectedSection == section) return;

        SelectedSection = section;
        ClearFilters();
    }

    /// <summary>
    /// Notifies that filter visibility properties may have changed when the selected ModDB section changes.
    /// </summary>
    /// <param name="value">The newly selected ModDB section.</param>
    partial void OnSelectedSectionChanged(ModDBSection value)
    {
        OnPropertyChanged(nameof(ShowCategoryFilter));
        OnPropertyChanged(nameof(ShowAddonCategoryFilter));
        OnPropertyChanged(nameof(ShowLicenseFilter));
    }

    /// <summary>
    /// Gets a value indicating whether to show the Addon Category filter (Downloads, Mods, and Addons sections).
    /// </summary>
    public static bool ShowAddonCategoryFilter => true; // All sections support addon filtering

    /// <summary>
    /// Gets a value indicating whether to show the Category filter (Downloads and Mods sections).
    /// </summary>
    public bool ShowCategoryFilter => SelectedSection is ModDBSection.Downloads or ModDBSection.Mods;

    /// <summary>
    /// Gets a value indicating whether to show the License filter (Addons section only).
    /// </summary>
    public bool ShowLicenseFilter => SelectedSection == ModDBSection.Addons;

    /// <summary>
    /// Populates the CategoryOptions collection with the predefined set of ModDB categories used for Downloads and Mods filters.
    /// </summary>
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

    /// <summary>
    /// Populates AddonCategoryOptions with predefined addon categories and their ModDB category codes.
    /// </summary>
    /// <remarks>
    /// Each option's Value is the category code used by ModDB form fields (e.g., "categoryaddon" for Downloads or "category" for Addons).
    /// </remarks>
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

    /// <summary>
    /// Populates the LicenseOptions collection with available ModDB license filter options.
    /// </summary>
    /// <remarks>
    /// Each added FilterOption's Value is the ModDB license identifier used when applying filters (applies to Addons section).
    /// </remarks>
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

    /// <summary>
    /// Populates the TimeframeOptions collection with predefined timeframe filter options used for ModDB queries.
    /// </summary>
    private void InitializeTimeframeOptions()
    {
        TimeframeOptions.Add(new FilterOption("Past 24 hours", "1"));
        TimeframeOptions.Add(new FilterOption("Past week", "2"));
        TimeframeOptions.Add(new FilterOption("Past month", "3"));
        TimeframeOptions.Add(new FilterOption("Past year", "4"));
        TimeframeOptions.Add(new FilterOption("Year or older", "5"));
    }
}