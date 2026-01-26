using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    /// Initializes a new instance of the <see cref="ModDBFilterViewModel"/> class and populates its category, addon category, timeframe, and license option collections.
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
        else
        {
            baseQuery.ModDBCategory = null;
        }

        // Apply Addon Category filter
        if (!string.IsNullOrEmpty(SelectedAddonCategory))
        {
            // For Addons section, use "category" param; for Downloads/Mods, use "categoryaddon"
            baseQuery.ModDBAddonCategory = SelectedAddonCategory;
        }
        else
        {
            baseQuery.ModDBAddonCategory = null;
        }

        // Apply License filter (Addons section only)
        if (!string.IsNullOrEmpty(SelectedLicense))
        {
            baseQuery.ModDBLicense = SelectedLicense;
        }
        else
        {
            baseQuery.ModDBLicense = null;
        }

        // Apply Timeframe filter
        if (!string.IsNullOrEmpty(SelectedTimeframe))
        {
            baseQuery.ModDBTimeframe = SelectedTimeframe;
        }
        else
        {
            baseQuery.ModDBTimeframe = null;
        }

        return baseQuery;
    }

    /// <summary>
    /// Clears all selected ModDB filters (category, addon category, license, and timeframe) and notifies observers of the change.
    /// </summary>
    /// <remarks>
    /// Invokes <see cref="FilterPanelViewModelBase.NotifyFiltersChanged"/> and <see cref="FilterPanelViewModelBase.OnFiltersCleared"/> after resetting the selections.
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
            yield return $"Category: {CategoryOptions.FirstOrDefault(o => o.Value == SelectedCategory)?.DisplayName ?? SelectedCategory}";
        }

        if (!string.IsNullOrEmpty(SelectedAddonCategory))
        {
            yield return $"Addon: {AddonCategoryOptions.FirstOrDefault(o => o.Value == SelectedAddonCategory)?.DisplayName ?? SelectedAddonCategory}";
        }

        if (!string.IsNullOrEmpty(SelectedLicense))
        {
            yield return $"License: {LicenseOptions.FirstOrDefault(o => o.Value == SelectedLicense)?.DisplayName ?? SelectedLicense}";
        }

        if (!string.IsNullOrEmpty(SelectedTimeframe))
        {
            yield return $"Time: {TimeframeOptions.FirstOrDefault(o => o.Value == SelectedTimeframe)?.DisplayName ?? SelectedTimeframe}";
        }
    }

    /// <summary>
    /// Called when the SelectedCategory property changes to notify observers and react to the new selection.
    /// </summary>
    /// <param name="value">The new selected category value, or null if the selection was cleared.</param>
    partial void OnSelectedCategoryChanged(string? value) => NotifyFiltersChanged();

    /// <summary>
    /// Invoked when the SelectedAddonCategory property changes to allow responding to the new selection.
    /// </summary>
    /// <param name="value">The new selected addon category, or null if the selection was cleared.</param>
    partial void OnSelectedAddonCategoryChanged(string? value) => NotifyFiltersChanged();

    /// <summary>
    /// Invoked when the SelectedLicense property changes; receives the new selected license value or null.
    /// </summary>
    /// <param name="value">The new license value, or null if no license is selected.</param>
    partial void OnSelectedLicenseChanged(string? value) => NotifyFiltersChanged();

    /// <summary>
    /// Called when the SelectedTimeframe property value changes.
    /// </summary>
    /// <param name="value">The new SelectedTimeframe value, or null if cleared.</param>
    partial void OnSelectedTimeframeChanged(string? value) => NotifyFiltersChanged();

    /// <summary>
    /// Toggles the current category selection: selects the given option's value or clears the selection if it's already selected.
    /// </summary>
    /// <param name="option">The category option to select or deselect; its Value is applied to SelectedCategory or cleared if already active.</param>
    [RelayCommand]
    private void SelectCategory(FilterOption option)
    {
        if (option == null) return;
        SelectedCategory = SelectedCategory == option.Value ? null : option.Value;
    }

    /// <summary>
    /// Sets the active addon category filter to the provided option's value, or clears it if that option is already selected.
    /// </summary>
    /// <param name="option">The addon category option to toggle; its Value becomes the active SelectedAddonCategory or is cleared if already active.</param>
    [RelayCommand]
    private void SelectAddonCategory(FilterOption option)
    {
        if (option == null) return;
        SelectedAddonCategory = SelectedAddonCategory == option.Value ? null : option.Value;
    }

    /// <summary>
    /// Sets SelectedLicense to the provided option's value, or clears SelectedLicense if that value is already selected.
    /// </summary>
    /// <param name="option">The license option to select or toggle; its Value is stored in SelectedLicense.</param>
    [RelayCommand]
    private void SelectLicense(FilterOption option)
    {
        if (option == null) return;
        SelectedLicense = SelectedLicense == option.Value ? null : option.Value;
    }

    /// <summary>
    /// Toggles the current timeframe selection between the provided option and cleared state.
    /// </summary>
    /// <param name="option">The timeframe option to select; if it is already selected the timeframe is cleared.</param>
    [RelayCommand]
    private void SelectTimeframe(FilterOption option)
    {
        if (option == null) return;
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
        CategoryOptions.Add(new FilterOption("Releases", ModDBConstants.CategoryFullVersion));
        CategoryOptions.Add(new FilterOption("Full Version", ModDBConstants.CategoryFullVersion));
        CategoryOptions.Add(new FilterOption("Demo", ModDBConstants.CategoryDemo));
        CategoryOptions.Add(new FilterOption("Patch", ModDBConstants.CategoryPatch));
        CategoryOptions.Add(new FilterOption("Script", ModDBConstants.CategoryScript));
        CategoryOptions.Add(new FilterOption("Trainer", ModDBConstants.CategoryTrainer));
        CategoryOptions.Add(new FilterOption("Media", ModDBConstants.CategoryMedia, IsHeader: true));
        CategoryOptions.Add(new FilterOption("Trailer", ModDBConstants.CategoryTrailer));
        CategoryOptions.Add(new FilterOption("Movie", ModDBConstants.CategoryMovie));
        CategoryOptions.Add(new FilterOption("Music", ModDBConstants.CategoryMusic));
        CategoryOptions.Add(new FilterOption("Audio", ModDBConstants.CategoryAudio));
        CategoryOptions.Add(new FilterOption("Wallpaper", ModDBConstants.CategoryWallpaper));
        CategoryOptions.Add(new FilterOption("Tools", ModDBConstants.CategoryTools, IsHeader: true));
        CategoryOptions.Add(new FilterOption("Archive Tool", ModDBConstants.CategoryArchiveTool));
        CategoryOptions.Add(new FilterOption("Graphics Tool", ModDBConstants.CategoryGraphicsTool));
        CategoryOptions.Add(new FilterOption("Mapping Tool", ModDBConstants.CategoryMappingTool));
        CategoryOptions.Add(new FilterOption("Modelling Tool", ModDBConstants.CategoryModellingTool));
        CategoryOptions.Add(new FilterOption("Installer Tool", ModDBConstants.CategoryInstallerTool));
        CategoryOptions.Add(new FilterOption("Server Tool", ModDBConstants.CategoryServerTool));
        CategoryOptions.Add(new FilterOption("IDE", ModDBConstants.CategoryIDE));
        CategoryOptions.Add(new FilterOption("SDK", ModDBConstants.CategorySDK));
        CategoryOptions.Add(new FilterOption("Source Code", ModDBConstants.CategorySourceCode));
        CategoryOptions.Add(new FilterOption("RTX Remix", ModDBConstants.CategoryRTXRemix));
        CategoryOptions.Add(new FilterOption("RTX.conf", ModDBConstants.CategoryRTXConf));
        CategoryOptions.Add(new FilterOption("Miscellaneous", ModDBConstants.CategoryMiscellaneous, IsHeader: true));
        CategoryOptions.Add(new FilterOption("Guide", ModDBConstants.CategoryGuide));
        CategoryOptions.Add(new FilterOption("Tutorial", ModDBConstants.CategoryTutorial));
        CategoryOptions.Add(new FilterOption("Language Pack", ModDBConstants.CategoryLanguagePack));
        CategoryOptions.Add(new FilterOption("Other", ModDBConstants.CategoryOther));
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
        AddonCategoryOptions.Add(new FilterOption("Maps", ModDBConstants.AddonMaps));
        AddonCategoryOptions.Add(new FilterOption("Multiplayer Map", ModDBConstants.AddonMultiplayerMap));
        AddonCategoryOptions.Add(new FilterOption("Singleplayer Map", ModDBConstants.AddonSingleplayerMap));
        AddonCategoryOptions.Add(new FilterOption("Prefab", ModDBConstants.AddonPrefab));
        AddonCategoryOptions.Add(new FilterOption("Models", ModDBConstants.AddonModels));
        AddonCategoryOptions.Add(new FilterOption("Player Model", ModDBConstants.AddonPlayerModel));
        AddonCategoryOptions.Add(new FilterOption("Prop Model", ModDBConstants.AddonPropModel));
        AddonCategoryOptions.Add(new FilterOption("Vehicle Model", ModDBConstants.AddonVehicleModel));
        AddonCategoryOptions.Add(new FilterOption("Weapon Model", ModDBConstants.AddonWeaponModel));
        AddonCategoryOptions.Add(new FilterOption("Model Pack", ModDBConstants.AddonModelPack));
        AddonCategoryOptions.Add(new FilterOption("Skins", ModDBConstants.AddonSkins));
        AddonCategoryOptions.Add(new FilterOption("Player Skin", ModDBConstants.AddonPlayerSkin));
        AddonCategoryOptions.Add(new FilterOption("Prop Skin", ModDBConstants.AddonPropSkin));
        AddonCategoryOptions.Add(new FilterOption("Vehicle Skin", ModDBConstants.AddonVehicleSkin));
        AddonCategoryOptions.Add(new FilterOption("Weapon Skin", ModDBConstants.AddonWeaponSkin));
        AddonCategoryOptions.Add(new FilterOption("Skin Pack", ModDBConstants.AddonSkinPack));
        AddonCategoryOptions.Add(new FilterOption("Audio", ModDBConstants.AddonAudio));
        AddonCategoryOptions.Add(new FilterOption("Music", ModDBConstants.AddonMusic));
        AddonCategoryOptions.Add(new FilterOption("Player Audio", ModDBConstants.AddonPlayerAudio));
        AddonCategoryOptions.Add(new FilterOption("Audio Pack", ModDBConstants.AddonAudioPack));
        AddonCategoryOptions.Add(new FilterOption("Graphics", ModDBConstants.AddonGraphics));
        AddonCategoryOptions.Add(new FilterOption("Decal", ModDBConstants.AddonDecal));
        AddonCategoryOptions.Add(new FilterOption("Effects GFX", ModDBConstants.AddonEffectsGFX));
        AddonCategoryOptions.Add(new FilterOption("GUI", ModDBConstants.AddonGUI));
        AddonCategoryOptions.Add(new FilterOption("HUD", ModDBConstants.AddonHUD));
        AddonCategoryOptions.Add(new FilterOption("Sprite", ModDBConstants.AddonSprite));
        AddonCategoryOptions.Add(new FilterOption("Texture", ModDBConstants.AddonTexture));
    }

    /// <summary>
    /// Populates the LicenseOptions collection with available ModDB license filter options.
    /// </summary>
    /// <remarks>
    /// Each added FilterOption's Value is the ModDB license identifier used when applying filters (applies to Addons section).
    /// </remarks>
    private void InitializeLicenseOptions()
    {
        LicenseOptions.Add(new FilterOption("Any", string.Empty));
        LicenseOptions.Add(new FilterOption("BSD", ModDBConstants.LicenseBSD));
        LicenseOptions.Add(new FilterOption("Commercial", ModDBConstants.LicenseCommercial));
        LicenseOptions.Add(new FilterOption("Creative Commons", ModDBConstants.LicenseCreativeCommons));
        LicenseOptions.Add(new FilterOption("GPL", ModDBConstants.LicenseGPL));
        LicenseOptions.Add(new FilterOption("L-GPL", ModDBConstants.LicenseLGPL));
        LicenseOptions.Add(new FilterOption("MIT", ModDBConstants.LicenseMIT));
        LicenseOptions.Add(new FilterOption("Zlib", ModDBConstants.LicenseZlib));
        LicenseOptions.Add(new FilterOption("Proprietary", ModDBConstants.LicenseProprietary));
        LicenseOptions.Add(new FilterOption("Public Domain", ModDBConstants.LicensePublicDomain));
    }

    /// <summary>
    /// Populates the TimeframeOptions collection with predefined timeframe filter options used for ModDB queries.
    /// </summary>
    private void InitializeTimeframeOptions()
    {
        TimeframeOptions.Add(new FilterOption("Past 24 hours", ModDBConstants.TimeframePast24Hours));
        TimeframeOptions.Add(new FilterOption("Past week", ModDBConstants.TimeframePastWeek));
        TimeframeOptions.Add(new FilterOption("Past month", ModDBConstants.TimeframePastMonth));
        TimeframeOptions.Add(new FilterOption("Past year", ModDBConstants.TimeframePastYear));
        TimeframeOptions.Add(new FilterOption("Year or older", ModDBConstants.TimeframeYearOrOlder));
    }
}