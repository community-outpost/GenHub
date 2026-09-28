using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// Shared variant-axis picker used by the content and artifact dialogs.
/// Offers the well-known axes with explanations plus a custom free-text axis.
/// </summary>
public partial class VariantAxisSelector : ObservableObject
{
    [ObservableProperty]
    private VariantAxisOption? _selectedOption;

    [ObservableProperty]
    private string? _customValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="VariantAxisSelector"/> class.
    /// </summary>
    /// <param name="localizationService">Optional localization service for option descriptions.</param>
    /// <param name="initialValue">The axis identifier to preselect, if any.</param>
    public VariantAxisSelector(ILocalizationService? localizationService, string? initialValue = null)
    {
        LocalizationService = localizationService;
        Options = BuildOptions(localizationService);
        SetValue(initialValue);
    }

    /// <summary>
    /// Gets the selectable axis options.
    /// </summary>
    public IReadOnlyList<VariantAxisOption> Options { get; }

    /// <summary>
    /// Gets a value indicating whether the custom free-text input should be shown.
    /// </summary>
    public bool ShowCustomValue => SelectedOption?.IsCustom == true;

    /// <summary>
    /// Gets the effective axis identifier, or null when no axis applies.
    /// </summary>
    public string? EffectiveValue => ShowCustomValue
        ? (string.IsNullOrWhiteSpace(CustomValue) ? null : CustomValue.Trim())
        : SelectedOption?.Value;

    /// <summary>
    /// Gets the description of the currently selected option.
    /// </summary>
    public string? SelectedDescription => SelectedOption?.Description;

    private ILocalizationService? LocalizationService { get; }

    /// <summary>
    /// Selects the option matching the specified axis identifier.
    /// Unknown values select the custom option and fill the free-text input.
    /// </summary>
    /// <param name="value">The axis identifier to select, or null for none.</param>
    public void SetValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            SelectedOption = Options[0];
            CustomValue = null;
            return;
        }

        var trimmed = value.Trim();
        var known = Options.FirstOrDefault(o =>
            !o.IsCustom && string.Equals(o.Value, trimmed, StringComparison.OrdinalIgnoreCase));
        if (known != null)
        {
            SelectedOption = known;
            CustomValue = null;
            return;
        }

        CustomValue = trimmed;
        SelectedOption = Options.First(o => o.IsCustom);
    }

    private static IReadOnlyList<VariantAxisOption> BuildOptions(ILocalizationService? localizationService)
    {
        string Text(string key, string fallback) => localizationService?.GetString(key) ?? fallback;

        return
        [
            new VariantAxisOption(
                null,
                Text("Tools.PublisherStudio.Content.VariantAxis.None", "(None)"),
                Text(
                    "Tools.PublisherStudio.Content.VariantAxis.NoneDescription",
                    "No variants. Every artifact in the release is installed together.")),
            new VariantAxisOption(
                CatalogConstants.GameTypeVariantAxis,
                CatalogConstants.GameTypeVariantAxis,
                Text(
                    "Tools.PublisherStudio.Content.VariantAxis.GameTypeDescription",
                    "Same release for different games, e.g. Zero Hour and Generals builds.")),
            new VariantAxisOption(
                CatalogConstants.ResolutionVariantAxis,
                CatalogConstants.ResolutionVariantAxis,
                Text(
                    "Tools.PublisherStudio.Content.VariantAxis.ResolutionDescription",
                    "Same release in different resolutions, e.g. 720p, 1080p or 4K control bars.")),
            new VariantAxisOption(
                CatalogConstants.LanguageVariantAxis,
                CatalogConstants.LanguageVariantAxis,
                Text(
                    "Tools.PublisherStudio.Content.VariantAxis.LanguageDescription",
                    "Same release in different languages, e.g. English or German hotkey layouts.")),
            new VariantAxisOption(
                CatalogConstants.EditionVariantAxis,
                CatalogConstants.EditionVariantAxis,
                Text(
                    "Tools.PublisherStudio.Content.VariantAxis.EditionDescription",
                    "Same release in different editions, e.g. Standard or HD.")),
            new VariantAxisOption(
                "custom",
                Text("Tools.PublisherStudio.Content.VariantAxis.Custom", "Custom..."),
                Text(
                    "Tools.PublisherStudio.Content.VariantAxis.CustomDescription",
                    "Use your own axis name, e.g. compatibility or platform."),
                IsCustom: true),
        ];
    }

    partial void OnSelectedOptionChanged(VariantAxisOption? value)
    {
        OnPropertyChanged(nameof(ShowCustomValue));
        OnPropertyChanged(nameof(EffectiveValue));
        OnPropertyChanged(nameof(SelectedDescription));
    }

    partial void OnCustomValueChanged(string? value)
    {
        OnPropertyChanged(nameof(EffectiveValue));
    }
}
