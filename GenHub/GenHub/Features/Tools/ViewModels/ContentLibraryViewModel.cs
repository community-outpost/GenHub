using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for the Content Library tab.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI and CommunityToolkit ObservableProperty generated properties.")]
public partial class ContentLibraryViewModel : ObservableObject
{
    private readonly NamedCatalog _activeCatalog;
    private readonly PublisherStudioViewModel _parentViewModel;
    private readonly ILogger _logger;
    private readonly IPublisherStudioDialogService _dialogService;

    [ObservableProperty]
    private ObservableCollection<CatalogContentItem> _contentItems = [];

    [ObservableProperty]
    private CatalogContentItem? _selectedContent;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Gets the name of the active catalog.
    /// </summary>
    public string ActiveCatalogName => _activeCatalog.Name;

    /// <summary>
    /// Gets filtered content items based on the search query.
    /// </summary>
    public ObservableCollection<CatalogContentItem> FilteredContent
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return ContentItems;
            }

            var query = SearchText.Trim();
            return new ObservableCollection<CatalogContentItem>(
                ContentItems.Where(item =>
                    (item.Name is { } name && name.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (item.Id is { } id && id.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (item.Description is { } desc && desc.Contains(query, StringComparison.OrdinalIgnoreCase))));
        }
    }

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
        ArgumentNullException.ThrowIfNull(project);
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

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredContent));
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

        OnPropertyChanged(nameof(FilteredContent));
    }

    /// <summary>
    /// Renames the active catalog.
    /// </summary>
    [RelayCommand]
    private async Task RenameCatalogAsync()
    {
        await _parentViewModel.RenameCatalogCommand.ExecuteAsync(_activeCatalog);
        OnPropertyChanged(nameof(ActiveCatalogName));
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
            OnPropertyChanged(nameof(FilteredContent));
            SelectedContent = newContent;

            _parentViewModel.MarkDirty();
            await _parentViewModel.SaveProjectAsync();
            _logger.LogInformation("Added new content item: {ContentId} to catalog: {CatalogId}", newContent.Id, _activeCatalog.Id);
        }
    }

    /// <summary>
    /// Edits the selected content item using a pre-populated dialog.
    /// </summary>
    [RelayCommand]
    private async Task EditContentAsync()
    {
        if (SelectedContent == null) return;

        var edited = await _dialogService.ShowEditContentDialogAsync(SelectedContent);
        if (edited != null)
        {
            // Update the existing item's properties
            SelectedContent.Name = edited.Name;
            SelectedContent.Description = edited.Description;
            SelectedContent.ContentType = edited.ContentType;
            SelectedContent.TargetGame = edited.TargetGame;
            SelectedContent.Tags = edited.Tags;

            // Force UI refresh
            OnPropertyChanged(nameof(SelectedContent));

            _parentViewModel.MarkDirty();
            await _parentViewModel.SaveProjectAsync();
            _logger.LogInformation("Edited content item: {ContentId}", SelectedContent.Id);
        }
    }

    /// <summary>
    /// Deletes the selected content item from the active catalog.
    /// </summary>
    [RelayCommand]
    private async Task DeleteContentAsync()
    {
        if (SelectedContent == null)
        {
            return;
        }

        var contentId = SelectedContent.Id; // Capture ID before removal

        _activeCatalog.Catalog.Content.Remove(SelectedContent);
        ContentItems.Remove(SelectedContent);
        OnPropertyChanged(nameof(FilteredContent));

        _parentViewModel.MarkDirty();
        await _parentViewModel.SaveProjectAsync();
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
            await _parentViewModel.SaveProjectAsync();
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
            await _parentViewModel.SaveProjectAsync();
            _logger.LogInformation("Added bundled item to {ContentId} in catalog: {CatalogId}: {DependencyId}", SelectedContent.Id, _activeCatalog.Id, dependency.ContentId);
        }
    }

    /// <summary>
    /// Removes a bundled item from the selected content bundle.
    /// </summary>
    [RelayCommand]
    private async Task RemoveBundledItemAsync(CatalogDependency dependency)
    {
        if (SelectedContent == null || dependency == null)
        {
            return;
        }

        SelectedContent.BundledItems.Remove(dependency);
        _parentViewModel.MarkDirty();
        await _parentViewModel.SaveProjectAsync();
        _logger.LogInformation("Removed bundled item from {ContentId}: {DependencyId}", SelectedContent.Id, dependency.ContentId);
    }

    /// <summary>
    /// Deletes a specific release from the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task DeleteReleaseAsync(ContentRelease release)
    {
        if (SelectedContent == null || release == null)
        {
            return;
        }

        SelectedContent.Releases.Remove(release);
        _parentViewModel.MarkDirty();
        await _parentViewModel.SaveProjectAsync();
        _logger.LogInformation("Deleted release v{Version} from {ContentId}", release.Version, SelectedContent.Id);

        // Force UI refresh by re-selecting
        OnPropertyChanged(nameof(SelectedContent));
    }

    /// <summary>
    /// Edits a specific release using a pre-populated dialog.
    /// </summary>
    [RelayCommand]
    private async Task EditReleaseAsync(ContentRelease release)
    {
        if (SelectedContent == null || release == null) return;

        var edited = await _dialogService.ShowEditReleaseDialogAsync(release, SelectedContent, _activeCatalog.Catalog);
        if (edited != null)
        {
            release.Version = edited.Version;
            release.Changelog = edited.Changelog;
            release.IsLatest = edited.IsLatest;
            release.IsPrerelease = edited.IsPrerelease;
            release.IsFeatured = edited.IsFeatured;
            release.ReleaseDate = edited.ReleaseDate;
            release.Artifacts = edited.Artifacts;
            release.Dependencies = edited.Dependencies;

            // Force UI refresh
            OnPropertyChanged(nameof(SelectedContent));

            _parentViewModel.MarkDirty();
            await _parentViewModel.SaveProjectAsync();
            _logger.LogInformation("Edited release v{Version} of {ContentId}", release.Version, SelectedContent.Id);
        }
    }

    /// <summary>
    /// Adds an artifact to an existing release.
    /// </summary>
    [RelayCommand]
    private async Task AddArtifactToReleaseAsync(ContentRelease release)
    {
        if (release == null) return;

        var artifact = await _dialogService.ShowAddArtifactDialogAsync();
        if (artifact != null)
        {
            release.Artifacts.Add(artifact);
            _parentViewModel.MarkDirty();
            await _parentViewModel.SaveProjectAsync();

            // Force UI refresh
            OnPropertyChanged(nameof(SelectedContent));

            _logger.LogInformation("Added artifact to release v{Version}", release.Version);
        }
    }

    /// <summary>
    /// Removes an artifact from a release.
    /// </summary>
    [RelayCommand]
    private async Task DeleteArtifactFromReleaseAsync((ContentRelease Release, ReleaseArtifact Artifact) args)
    {
        if (args.Release == null || args.Artifact == null) return;

        args.Release.Artifacts.Remove(args.Artifact);
        _parentViewModel.MarkDirty();
        await _parentViewModel.SaveProjectAsync();

        OnPropertyChanged(nameof(SelectedContent));

        _logger.LogInformation("Removed artifact from release v{Version}", args.Release.Version);
    }

    /// <summary>
    /// Adds a dependency to an existing release.
    /// </summary>
    [RelayCommand]
    private async Task AddDependencyToReleaseAsync(ContentRelease release)
    {
        if (SelectedContent == null || release == null) return;

        var dependency = await _dialogService.ShowAddDependencyDialogAsync(_activeCatalog.Catalog, SelectedContent);
        if (dependency != null)
        {
            release.Dependencies.Add(dependency);
            _parentViewModel.MarkDirty();
            await _parentViewModel.SaveProjectAsync();

            OnPropertyChanged(nameof(SelectedContent));

            _logger.LogInformation("Added dependency to release v{Version}", release.Version);
        }
    }
}
