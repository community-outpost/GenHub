using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for selecting a variant when content has multiple game type variants.
/// </summary>
public sealed partial class VariantSelectionViewModel : ObservableObject
{
    private readonly ILogger<VariantSelectionViewModel> _logger;

    [ObservableProperty]
    private string _contentName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<VariantOptionViewModel> _variants = [];

    [ObservableProperty]
    private VariantOptionViewModel? _selectedVariant;

    partial void OnSelectedVariantChanged(VariantOptionViewModel? value)
    {
        // Synchronize IsSelected property with SelectedVariant
        foreach (var variant in Variants)
        {
            variant.IsSelected = variant == value;
        }
    }

    [ObservableProperty]
    private bool _wasSuccessful;

    /// <summary>
    /// Event raised when the dialog should be closed.
    /// </summary>
    public event EventHandler? RequestClose;

    /// <summary>
    /// Initializes a new instance of the <see cref="VariantSelectionViewModel"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="contentName">The name of the content.</param>
    /// <param name="manifests">The available variant manifests.</param>
    public VariantSelectionViewModel(
        ILogger<VariantSelectionViewModel> logger,
        string contentName,
        ObservableCollection<ContentManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ContentName = contentName;

        // Convert manifests to variant options
        foreach (var manifest in manifests)
        {
            Variants.Add(new VariantOptionViewModel
            {
                Name = manifest.Name,
                ManifestId = manifest.Id.Value,
                GameType = manifest.TargetGame.ToString(),
                Description = $"{manifest.ContentType} for {manifest.TargetGame}",
                Version = manifest.Version,
                Manifest = manifest,
            });
        }

        // Auto-select first variant if only one
        if (Variants.Count == 1)
        {
            SelectedVariant = Variants[0];
        }

        _logger.LogInformation("Variant selection initialized with {Count} variants for {ContentName}", Variants.Count, contentName);
    }

    /// <summary>
    /// Command to select a variant and proceed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void Select()
    {
        if (SelectedVariant == null)
        {
            _logger.LogWarning("No variant selected");
            return;
        }

        _logger.LogInformation("Variant selected: {VariantName} ({ManifestId})", SelectedVariant.Name, SelectedVariant.ManifestId);
        WasSuccessful = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets a value indicating whether a variant can be selected.
    /// </summary>
    private bool CanSelect => SelectedVariant != null;

    /// <summary>
    /// Command to cancel the selection.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        WasSuccessful = false;
        SelectedVariant = null;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Command to select a specific variant option.
    /// </summary>
    [RelayCommand]
    private void SelectVariant(VariantOptionViewModel variant)
    {
        SelectedVariant = variant;
        SelectCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>
/// Represents a single variant option.
/// </summary>
public partial class VariantOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _manifestId = string.Empty;

    [ObservableProperty]
    private string _gameType = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _version = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Gets or sets the underlying manifest.
    /// </summary>
    public ContentManifest? Manifest { get; set; }
}
