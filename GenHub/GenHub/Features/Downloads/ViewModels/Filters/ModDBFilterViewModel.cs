using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for ModDB publisher with section-based category, license, and timeframe filters.
/// </summary>
[SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Properties access CommunityToolkit MVVM generated instance properties.")]
public partial class ModDBFilterViewModel : FilterPanelViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadsSelected))]
    [NotifyPropertyChangedFor(nameof(IsAddonsSelected))]
    [NotifyPropertyChangedFor(nameof(IsModsSelected))]
    [NotifyPropertyChangedFor(nameof(ShowCategoryFilter))]
    [NotifyPropertyChangedFor(nameof(ShowAddonCategoryFilter))]
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
    public bool ShowAddonCategoryFilter => SelectedSection is ModDBSection.Downloads or ModDBSection.Mods or ModDBSection.Addons;

    /// <summary>
    /// Gets a value indicating whether to show the License filter (Addons section only).
    /// </summary>
    public bool ShowLicenseFilter => SelectedSection == ModDBSection.Addons;

    /// <inheritdoc />
    public override bool HasActiveFilters =>
        SelectedSection != ModDBSection.Downloads ||
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
            ModDBSection.Mods => ModDBConstants.ModsSection,
            ModDBSection.Addons => ModDBConstants.AddonsSection,
            _ => ModDBConstants.DownloadsSection,
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
            if (SelectedSection == ModDBSection.Addons)
            {
                baseQuery.ModDBCategory = SelectedAddonCategory;
            }
            else
            {
                baseQuery.ModDBAddonCategory = SelectedAddonCategory;
            }
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
        SelectedSection = ModDBSection.Downloads;
        ClearDropdownFilters();
        OnFiltersCleared();
    }

    /// <inheritdoc />
    public override IEnumerable<string> GetActiveFilterSummary()
    {
        if (SelectedSection != ModDBSection.Downloads)
        {
            yield return $"Section: {SelectedSection}";
        }

        if (!string.IsNullOrEmpty(SelectedCategory))
        {
            var match = CategoryOptions.FirstOrDefault(o => o.Value == SelectedCategory);
            yield return $"Category: {match?.DisplayName ?? SelectedCategory}";
        }

        if (!string.IsNullOrEmpty(SelectedAddonCategory))
        {
            var match = AddonCategoryOptions.FirstOrDefault(o => o.Value == SelectedAddonCategory);
            yield return $"Addon: {match?.DisplayName ?? SelectedAddonCategory}";
        }

        if (!string.IsNullOrEmpty(SelectedLicense))
        {
            var match = LicenseOptions.FirstOrDefault(o => o.Value == SelectedLicense);
            yield return $"License: {match?.DisplayName ?? SelectedLicense}";
        }

        if (!string.IsNullOrEmpty(SelectedTimeframe))
        {
            var match = TimeframeOptions.FirstOrDefault(o => o.Value == SelectedTimeframe);
            yield return $"Time: {match?.DisplayName ?? SelectedTimeframe}";
        }

        if (!string.IsNullOrEmpty(SelectedSort) && !string.Equals(SelectedSort, ModDBConstants.DefaultSort, StringComparison.OrdinalIgnoreCase))
        {
            var match = SortOptions.FirstOrDefault(o => o.Value == SelectedSort);
            yield return $"Sort: {match?.DisplayName ?? SelectedSort}";
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

    partial void OnSelectedSectionChanged(ModDBSection value)
    {
        NotifyFiltersChanged();
    }

    [RelayCommand]
    private void SetSection(ModDBSection section)
    {
        if (SelectedSection == section)
        {
            OnPropertyChanged(nameof(IsDownloadsSelected));
            OnPropertyChanged(nameof(IsAddonsSelected));
            OnPropertyChanged(nameof(IsModsSelected));
            return;
        }

        SelectedSection = section;
        ClearDropdownFilters();
    }

    private void ClearDropdownFilters()
    {
        SelectedCategory = null;
        SelectedAddonCategory = null;
        SelectedLicense = null;
        SelectedTimeframe = null;
        SelectedSort = ModDBConstants.DefaultSort;
        NotifyFiltersChanged();
    }

    private void InitializeDownloadsFilters()
    {
        // Category options for Downloads/Mods - form select name="category"
        CategoryOptions.Add(new FilterOption("All Categories", string.Empty));
        CategoryOptions.Add(new FilterOption("Releases", ModDBConstants.CategoryReleases));
        CategoryOptions.Add(new FilterOption("Full Version", ModDBConstants.CategoryFullVersion));
        CategoryOptions.Add(new FilterOption("Demo", ModDBConstants.CategoryDemo));
        CategoryOptions.Add(new FilterOption("Patch", ModDBConstants.CategoryPatch));
        CategoryOptions.Add(new FilterOption("Script", ModDBConstants.CategoryScript));
        CategoryOptions.Add(new FilterOption("Trainer", ModDBConstants.CategoryTrainer));
        CategoryOptions.Add(new FilterOption("Media", ModDBConstants.CategoryMedia));
        CategoryOptions.Add(new FilterOption("Trailer", ModDBConstants.CategoryTrailer));
        CategoryOptions.Add(new FilterOption("Movie", ModDBConstants.CategoryMovie));
        CategoryOptions.Add(new FilterOption("Music", ModDBConstants.CategoryMusic));
        CategoryOptions.Add(new FilterOption("Audio", ModDBConstants.CategoryAudio));
        CategoryOptions.Add(new FilterOption("Wallpaper", ModDBConstants.CategoryWallpaper));
        CategoryOptions.Add(new FilterOption("Tools", ModDBConstants.CategoryTools));
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
        CategoryOptions.Add(new FilterOption("Miscellaneous", ModDBConstants.CategoryMiscellaneous));
        CategoryOptions.Add(new FilterOption("Guide", ModDBConstants.CategoryGuide));
        CategoryOptions.Add(new FilterOption("Tutorial", ModDBConstants.CategoryTutorial));
        CategoryOptions.Add(new FilterOption("Language Pack", ModDBConstants.CategoryLanguagePack));
        CategoryOptions.Add(new FilterOption("Other", ModDBConstants.CategoryOther));
    }

    private void InitializeAddonsFilters()
    {
        // Addon category options - form select name="categoryaddon" (Downloads) or "category" (Addons)
        AddonCategoryOptions.Add(new FilterOption("All Addon Types", string.Empty));
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

    private void InitializeLicenseOptions()
    {
        LicenseOptions.Add(new FilterOption("All Licenses", string.Empty));
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

    private void InitializeTimeframeOptions()
    {
        TimeframeOptions.Add(new FilterOption("All Time", string.Empty));
        TimeframeOptions.Add(new FilterOption("Past 24 hours", ModDBConstants.TimeframePast24Hours));
        TimeframeOptions.Add(new FilterOption("Past week", ModDBConstants.TimeframePastWeek));
        TimeframeOptions.Add(new FilterOption("Past month", ModDBConstants.TimeframePastMonth));
        TimeframeOptions.Add(new FilterOption("Past year", ModDBConstants.TimeframePastYear));
        TimeframeOptions.Add(new FilterOption("Year or older", ModDBConstants.TimeframeYearOrOlder));
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
