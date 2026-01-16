using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for the Content Library tab.
/// </summary>
public partial class ContentLibraryViewModel : ObservableObject
{
    private readonly PublisherStudioProject _project;
    private readonly NamedCatalog _activeCatalog;
    private readonly PublisherStudioViewModel _parentViewModel;
    private readonly ILogger _logger;
    private readonly IPublisherStudioDialogService _dialogService;

    [ObservableProperty]
    private ObservableCollection<CatalogContentItem> _contentItems = [];

    [ObservableProperty]
    private CatalogContentItem? _selectedContent;

    /// <summary>
    /// Gets the name of the active catalog.
    /// </summary>
    public string ActiveCatalogName => _activeCatalog.Name;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentLibraryViewModel"/> class with multi-catalog support.
    /// </summary>
    /// <param name="project">The publisher studio project.</param>
    /// <param name="activeCatalog">The active catalog to scope operations to.</param>
    /// <param name="parentViewModel">The parent view model.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="dialogService">The dialog service.</param>
    public ContentLibraryViewModel(
        PublisherStudioProject project,
        NamedCatalog activeCatalog,
        PublisherStudioViewModel parentViewModel,
        ILogger logger,
        IPublisherStudioDialogService dialogService)
    {
        _project = project;
        _activeCatalog = activeCatalog;
        _parentViewModel = parentViewModel;
        _logger = logger;
        _dialogService = dialogService;

        // Load existing content
        LoadContent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentLibraryViewModel"/> class with default catalog.
    /// </summary>
    /// <param name="project">The publisher studio project.</param>
    /// <param name="parentViewModel">The parent view model.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="dialogService">The dialog service.</param>
    public ContentLibraryViewModel(
        PublisherStudioProject project,
        PublisherStudioViewModel parentViewModel,
        ILogger logger,
        IPublisherStudioDialogService dialogService)
        : this(project, project.Catalogs.FirstOrDefault() ?? new NamedCatalog { Id = "default", Name = "Content", Catalog = project.Catalog }, parentViewModel, logger, dialogService)
    {
    }

    /// <summary>
    /// Loads content items from the active catalog.
    /// </summary>
    private void LoadContent()
    {
        ContentItems.Clear();
        foreach (var item in _activeCatalog.Catalog.Content)
        {
            ContentItems.Add(item);
        }
    }

    /// <summary>
    /// Adds a new content item to the active catalog.
    /// </summary>
    [RelayCommand]
    private async Task AddContentAsync()
    {
        var newContent = await _dialogService.ShowAddContentDialogAsync();
        if (newContent != null)
        {
            _activeCatalog.Catalog.Content.Add(newContent);
            ContentItems.Add(newContent);
            SelectedContent = newContent;

            _parentViewModel.MarkDirty();
            _logger.LogInformation("Added new content item: {ContentId} to catalog: {CatalogId}", newContent.Id, _activeCatalog.Id);
        }
    }

    /// <summary>
    /// Deletes the selected content item from the active catalog.
    /// </summary>
    [RelayCommand]
    private void DeleteContent()
    {
        if (SelectedContent == null)
        {
            return;
        }

        var contentId = SelectedContent.Id; // Capture ID before removal

        _activeCatalog.Catalog.Content.Remove(SelectedContent);
        ContentItems.Remove(SelectedContent);

        _parentViewModel.MarkDirty();
        _logger.LogInformation("Deleted content item: {ContentId} from catalog: {CatalogId}", contentId, _activeCatalog.Id);

        SelectedContent = ContentItems.FirstOrDefault();
    }

    /// <summary>
    /// Adds a new release to the selected content in the active catalog.
    /// </summary>
    [RelayCommand]
    private async Task AddReleaseAsync()
    {
        if (SelectedContent == null)
        {
            return;
        }

        var newRelease = await _dialogService.ShowAddReleaseDialogAsync(SelectedContent, _activeCatalog.Catalog);
        if (newRelease != null)
        {
            SelectedContent.Releases.Add(newRelease);

            _parentViewModel.MarkDirty();
            _logger.LogInformation("Added new release to content: {ContentId} in catalog: {CatalogId} (v{Version})", SelectedContent.Id, _activeCatalog.Id, newRelease.Version);
        }
    }

    /// <summary>
    /// Adds a bundled item to the selected content bundle in the active catalog.
    /// </summary>
    [RelayCommand]
    private async Task AddBundledItemAsync()
    {
        if (SelectedContent == null || SelectedContent.ContentType != GenHub.Core.Models.Enums.ContentType.ContentBundle)
        {
            return;
        }

        var dependency = await _dialogService.ShowAddDependencyDialogAsync(_activeCatalog.Catalog, SelectedContent);
        if (dependency != null)
        {
            SelectedContent.BundledItems.Add(dependency);
            _parentViewModel.MarkDirty();
            _logger.LogInformation("Added bundled item to {ContentId} in catalog: {CatalogId}: {DependencyId}", SelectedContent.Id, _activeCatalog.Id, dependency.ContentId);
        }
    }

    /// <summary>
    /// Removes a bundled item from the selected content bundle.
    /// </summary>
    [RelayCommand]
    private void RemoveBundledItem(CatalogDependency dependency)
    {
        if (SelectedContent == null || dependency == null)
        {
            return;
        }

        SelectedContent.BundledItems.Remove(dependency);
        _parentViewModel.MarkDirty();
        _logger.LogInformation("Removed bundled item from {ContentId}: {DependencyId}", SelectedContent.Id, dependency.ContentId);
    }

    /// <summary>
    /// Deletes a specific release from the selected content item.
    /// </summary>
    [RelayCommand]
    private void DeleteRelease(ContentRelease release)
    {
        if (SelectedContent == null || release == null)
        {
            return;
        }

        SelectedContent.Releases.Remove(release);
        _parentViewModel.MarkDirty();
        _logger.LogInformation("Deleted release v{Version} from {ContentId}", release.Version, SelectedContent.Id);

        // Force UI refresh by re-selecting
        var current = SelectedContent;
        SelectedContent = null;
        SelectedContent = current;
    }
}
