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
public partial class VariantAxisSelector : ObservableObject, IDisposable
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
        _options = BuildOptions(localizationService);
        SetValue(initialValue);

        if (localizationService != null)
        {
            localizationService.PropertyChanged += OnLocalizationChanged;
        }
    }

    /// <summary>
    /// Gets the selectable axis options.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<VariantAxisOption> _options;

    /// <summary>
    /// Gets a value indicating whether the custom free-text input should be shown.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads instance state through the CommunityToolkit-generated SelectedOption property.")]
    public bool ShowCustomValue => SelectedOption is { IsCustom: true };

    /// <summary>
    /// Gets the effective axis identifier, or null when no axis applies.
    /// </summary>
    public string? EffectiveValue
    {
        get
        {
            if (ShowCustomValue)
            {
                return string.IsNullOrWhiteSpace(CustomValue) ? null : CustomValue.Trim();
            }

            return SelectedOption?.Value;
        }
    }

    /// <summary>
    /// Gets the description of the currently selected option.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads instance state through the CommunityToolkit-generated SelectedOption property.")]
    public string? SelectedDescription => SelectedOption?.Description;

    private ILocalizationService? LocalizationService { get; }

    /// <summary>
    /// Selects the option matching the specified axis identifier.
    /// Unknown values select the custom option and fill the free-text input.
    /// </summary>
    /// <param name="value">The axis identifier to select, or null for none.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Assigns instance state through the CommunityToolkit-generated SelectedOption and CustomValue properties.")]
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

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases managed resources.
    /// </summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            var service = LocalizationService;
            if (service != null)
            {
                service.PropertyChanged -= OnLocalizationChanged;
            }
        }
    }

    private static IReadOnlyList<VariantAxisOption> BuildOptions(ILocalizationService? localizationService)
    {
        string Text(string key, string fallback) => localizationService?.GetString(key) ?? fallback;

        var options = new List<VariantAxisOption>
        {
            new(
                null,
                Text("Tools.PublisherStudio.Content.VariantAxis.None", "(None)"),
                Text(
                    "Tools.PublisherStudio.Content.VariantAxis.NoneDescription",
                    "No variants. Every artifact in the release is installed together.")),
        };

        foreach (var axis in CatalogConstants.KnownVariantAxes)
        {
            var descriptionKey = axis switch
            {
                CatalogConstants.GameTypeVariantAxis => "Tools.PublisherStudio.Content.VariantAxis.GameTypeDescription",
                CatalogConstants.ResolutionVariantAxis => "Tools.PublisherStudio.Content.VariantAxis.ResolutionDescription",
                CatalogConstants.LanguageVariantAxis => "Tools.PublisherStudio.Content.VariantAxis.LanguageDescription",
                CatalogConstants.EditionVariantAxis => "Tools.PublisherStudio.Content.VariantAxis.EditionDescription",
                _ => string.Empty,
            };

            var fallbackDescription = axis switch
            {
                CatalogConstants.GameTypeVariantAxis => "Same release for different games, e.g. Zero Hour and Generals builds.",
                CatalogConstants.ResolutionVariantAxis => "Same release in different resolutions, e.g. 720p, 1080p or 4K control bars.",
                CatalogConstants.LanguageVariantAxis => "Same release in different languages, e.g. English or German hotkey layouts.",
                CatalogConstants.EditionVariantAxis => "Same release in different editions, e.g. Standard or HD.",
                _ => axis,
            };

            var description = string.IsNullOrEmpty(descriptionKey)
                ? fallbackDescription
                : Text(descriptionKey, fallbackDescription);
            options.Add(new VariantAxisOption(
                axis,
                axis,
                description));
        }

        options.Add(new VariantAxisOption(
            "custom",
            Text("Tools.PublisherStudio.Content.VariantAxis.Custom", "Custom..."),
            Text(
                "Tools.PublisherStudio.Content.VariantAxis.CustomDescription",
                "Use your own axis name, e.g. compatibility or platform."),
            IsCustom: true));

        return options;
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

    /// <summary>
    /// Rebuilds localized option labels when the application language changes,
    /// preserving the current selection.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ILocalizationService.CurrentCulture))
        {
            return;
        }

        var current = EffectiveValue;
        Options = BuildOptions(LocalizationService);
        SetValue(current);
    }
}
