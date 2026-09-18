using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Publishers;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Represents the publish status of a catalog.
/// </summary>
[SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI.")]
public partial class CatalogPublishStatus : ObservableObject, IDisposable
{
    private readonly ILocalizationService? _localizationService;
    private bool _disposed;

    [ObservableProperty]
    private NamedCatalog _catalog;

    [ObservableProperty]
    private bool _isPublished;

    [ObservableProperty]
    private string? _publishedUrl;

    [ObservableProperty]
    private DateTime? _lastPublished;

    [ObservableProperty]
    private bool _hasChanges;

    /// <summary>
    /// Gets the display status text.
    /// </summary>
    public string StatusText
    {
        get
        {
            if (!IsPublished)
            {
                return _localizationService?.GetString("Tools.PublisherStudio.Status.NotPublished") ?? "Not Published";
            }

            if (HasChanges)
            {
                return _localizationService?.GetString("Tools.PublisherStudio.Status.ChangesPending") ?? "Changes Pending";
            }

            if (LastPublished.HasValue)
            {
                var dateStr = LastPublished.Value.ToString("MMM d, yyyy");
                return _localizationService != null
                    ? _localizationService.GetString("Tools.PublisherStudio.Status.PublishedDate", dateStr)
                    : $"Published {dateStr}";
            }

            return _localizationService?.GetString("Tools.PublisherStudio.Status.Published") ?? "Published";
        }
    }

    /// <summary>
    /// Gets the status color.
    /// </summary>
    public string StatusColor
    {
        get
        {
            if (!IsPublished)
            {
                return CatalogConstants.CatalogStatusNotPublishedColor;
            }

            if (HasChanges)
            {
                return CatalogConstants.CatalogStatusPendingColor;
            }

            return CatalogConstants.CatalogStatusPublishedColor;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogPublishStatus"/> class.
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="localizationService">The optional localization service.</param>
    public CatalogPublishStatus(NamedCatalog catalog, ILocalizationService? localizationService = null)
    {
        _catalog = catalog;
        _localizationService = localizationService;
        if (_localizationService != null)
        {
            _localizationService.PropertyChanged += OnLocalizationPropertyChanged;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_localizationService != null)
        {
            _localizationService.PropertyChanged -= OnLocalizationPropertyChanged;
        }

        _disposed = true;
    }

    partial void OnIsPublishedChanged(bool value) => NotifyStatusChanged();

    partial void OnHasChangesChanged(bool value) => NotifyStatusChanged();

    partial void OnLastPublishedChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    private void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
    }

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyStatusChanged();
    }
}
