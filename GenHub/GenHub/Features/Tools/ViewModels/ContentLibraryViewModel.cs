using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for the Content Library tab in Publisher Studio.
/// Scoped to the currently active catalog.
/// </summary>
public partial class ContentLibraryViewModel(
    PublisherStudioProject project,
    NamedCatalog activeCatalog,
    PublisherStudioViewModel parentViewModel,
    ILogger logger,
    IPublisherStudioDialogService dialogService) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<CatalogContentItem> _contentItems = activeCatalog?.Catalog?.Content != null
        ? [.. activeCatalog.Catalog.Content]
        : [];

    [ObservableProperty]
    private CatalogContentItem? _selectedContent;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Gets the name of the active catalog.
    /// </summary>
    public string ActiveCatalogName => activeCatalog?.Name ?? string.Empty;

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
        : this(project, project?.Catalogs.FirstOrDefault() ?? new NamedCatalog { Id = "default", Name = "Content", Catalog = project?.Catalog ?? new() }, parentViewModel, logger, dialogService)
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
        if (activeCatalog?.Catalog?.Content != null)
        {
            foreach (var item in activeCatalog.Catalog.Content)
            {
                ContentItems.Add(item);
            }
        }

        OnPropertyChanged(nameof(FilteredContent));
    }

    /// <summary>
    /// Gets the list of catalogs in the project.
    /// </summary>
    public ObservableCollection<NamedCatalog>? Catalogs => parentViewModel?.Catalogs;

    /// <summary>
    /// Gets or sets the selected catalog.
    /// </summary>
    public NamedCatalog? SelectedCatalog
    {
        get => parentViewModel?.SelectedCatalog;
        set
        {
            if (parentViewModel != null && parentViewModel.SelectedCatalog != value)
            {
                parentViewModel.SelectedCatalog = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the current catalog can be removed.
    /// </summary>
    public bool CanRemoveCatalog => parentViewModel?.CanRemoveCatalog ?? false;

    /// <summary>
    /// Gets the command to add a new catalog.
    /// </summary>
    public IRelayCommand? AddCatalogCommand => parentViewModel?.AddCatalogCommand;

    /// <summary>
    /// Gets the command to remove a catalog.
    /// </summary>
    public IAsyncRelayCommand<NamedCatalog>? RemoveCatalogCommand => parentViewModel?.RemoveCatalogCommand;

    /// <summary>
    /// Adds a new content item to the active catalog with an optional initial folder/file path.
    /// </summary>
    /// <param name="initialPath">Optional initial path of dropped or selected folder/file.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AddContentWithPathAsync(string? initialPath)
    {
        var newContent = await dialogService.ShowAddContentDialogAsync(initialPath);
        if (newContent != null)
        {
            if (activeCatalog.Catalog.Content.Any(c => string.Equals(c.Id, newContent.Id, System.StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogWarning("Content with ID '{ContentId}' already exists in catalog '{CatalogId}'", newContent.Id, activeCatalog.Id);
                return;
            }

            activeCatalog.Catalog.Content.Add(newContent);
            ContentItems.Add(newContent);
            OnPropertyChanged(nameof(FilteredContent));
            SelectedContent = newContent;

            parentViewModel?.MarkDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added new content item: {ContentId} to catalog: {CatalogId}", newContent.Id, activeCatalog.Id);
        }
    }

    /// <summary>
    /// Renames the active catalog.
    /// </summary>
    [RelayCommand]
    private async Task RenameCatalogAsync()
    {
        if (parentViewModel != null)
        {
            await parentViewModel.RenameCatalogCommand.ExecuteAsync(activeCatalog);
            OnPropertyChanged(nameof(ActiveCatalogName));
            OnPropertyChanged(nameof(SelectedCatalog));
        }
    }

    /// <summary>
    /// Adds a new content item to the active catalog.
    /// </summary>
    [RelayCommand]
    private async Task AddContentAsync()
    {
        await AddContentWithPathAsync(null);
    }

    /// <summary>
    /// Edits the selected content item using a pre-populated dialog.
    /// </summary>
    [RelayCommand]
    private async Task EditContentAsync()
    {
        if (SelectedContent == null) return;

        var edited = await dialogService.ShowEditContentDialogAsync(SelectedContent);
        if (edited != null)
        {
            // Update the existing item's properties
            SelectedContent.Name = edited.Name;
            SelectedContent.Description = edited.Description;
            SelectedContent.ContentType = edited.ContentType;
            SelectedContent.TargetGame = edited.TargetGame;
            SelectedContent.Tags = edited.Tags;
            SelectedContent.ExtendsContentId = edited.ExtendsContentId;

            // Trigger UI update
            OnPropertyChanged(nameof(FilteredContent));
            OnPropertyChanged(nameof(SelectedContent));

            parentViewModel?.MarkDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Updated content item: {ContentId}", SelectedContent.Id);
        }
    }

    /// <summary>
    /// Deletes the currently selected content item from the active catalog.
    /// </summary>
    [RelayCommand]
    private async Task DeleteContentAsync()
    {
        if (SelectedContent == null)
        {
            return;
        }

        var confirmed = await dialogService.ShowConfirmationDialogAsync(
            "Delete Content Item",
            $"Are you sure you want to delete '{SelectedContent.Name}' ({SelectedContent.Id})? This will also remove all its releases and artifacts.");

        if (!confirmed)
        {
            return;
        }

        var contentId = SelectedContent.Id;
        activeCatalog.Catalog.Content.Remove(SelectedContent);
        ContentItems.Remove(SelectedContent);
        SelectedContent = null;
        OnPropertyChanged(nameof(FilteredContent));

        parentViewModel?.MarkDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }

        logger.LogInformation("Deleted content item: {ContentId} from catalog: {CatalogId}", contentId, activeCatalog.Id);
    }

    /// <summary>
    /// Adds a release to the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task AddReleaseAsync()
    {
        if (SelectedContent == null)
        {
            return;
        }

        var newRelease = await dialogService.ShowAddReleaseDialogAsync(SelectedContent, activeCatalog.Catalog);
        if (newRelease != null)
        {
            // If marked as latest, unmark existing
            if (newRelease.IsLatest)
            {
                foreach (var rel in SelectedContent.Releases)
                {
                    rel.IsLatest = false;
                }
            }

            SelectedContent.Releases.Add(newRelease);
            OnPropertyChanged(nameof(SelectedContent));

            parentViewModel?.MarkDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added new release to content: {ContentId} in catalog: {CatalogId} (v{Version})", SelectedContent.Id, activeCatalog.Id, newRelease.Version);
        }
    }

    /// <summary>
    /// Adds a bundled item (dependency) to the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task AddBundledItemAsync()
    {
        if (SelectedContent == null) return;

        var dependency = await dialogService.ShowAddDependencyDialogAsync(activeCatalog.Catalog, SelectedContent);
        if (dependency != null)
        {
            SelectedContent.BundledItems.Add(dependency);
            OnPropertyChanged(nameof(SelectedContent));

            parentViewModel?.MarkDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added bundled item to {ContentId} in catalog: {CatalogId}: {DependencyId}", SelectedContent.Id, activeCatalog.Id, dependency.ContentId);
        }
    }

    /// <summary>
    /// Removes a bundled item (dependency) from the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task RemoveBundledItemAsync(CatalogDependency? dependency)
    {
        if (SelectedContent == null || dependency == null) return;

        SelectedContent.BundledItems.Remove(dependency);
        OnPropertyChanged(nameof(SelectedContent));

        parentViewModel?.MarkDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }

        logger.LogInformation("Removed bundled item {DependencyId} from {ContentId}", dependency.ContentId, SelectedContent.Id);
    }

    /// <summary>
    /// Removes a release from the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task RemoveReleaseAsync(ContentRelease? release)
    {
        if (SelectedContent == null || release == null)
        {
            return;
        }

        SelectedContent.Releases.Remove(release);
        OnPropertyChanged(nameof(SelectedContent));

        parentViewModel?.MarkDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }

        logger.LogInformation("Removed release v{Version} from content: {ContentId}", release.Version, SelectedContent.Id);
    }

    /// <summary>
    /// Edits an existing release using a pre-populated dialog.
    /// </summary>
    [RelayCommand]
    private async Task EditReleaseAsync(ContentRelease? release)
    {
        if (SelectedContent == null || release == null) return;

        var edited = await dialogService.ShowEditReleaseDialogAsync(release, SelectedContent, activeCatalog.Catalog);
        if (edited != null)
        {
            // If marked as latest, unmark other releases
            if (edited.IsLatest)
            {
                foreach (var r in SelectedContent.Releases.Where(r => r != release))
                {
                    r.IsLatest = false;
                }
            }

            // Update release properties in place
            release.Version = edited.Version;
            release.ReleaseDate = edited.ReleaseDate;
            release.IsLatest = edited.IsLatest;
            release.IsPrerelease = edited.IsPrerelease;
            release.IsFeatured = edited.IsFeatured;
            release.Changelog = edited.Changelog;
            release.Artifacts = edited.Artifacts;
            release.Dependencies = edited.Dependencies;

            OnPropertyChanged(nameof(SelectedContent));

            parentViewModel?.MarkDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Updated release v{Version} on content: {ContentId}", release.Version, SelectedContent.Id);
        }
    }

    /// <summary>
    /// Adds an artifact to an existing release.
    /// </summary>
    [RelayCommand]
    private async Task AddArtifactToReleaseAsync(ContentRelease? release)
    {
        if (release == null) return;

        var artifact = await dialogService.ShowAddArtifactDialogAsync();
        if (artifact != null)
        {
            if (artifact.IsPrimary)
            {
                foreach (var a in release.Artifacts)
                {
                    a.IsPrimary = false;
                }
            }

            release.Artifacts.Add(artifact);
            OnPropertyChanged(nameof(SelectedContent));

            parentViewModel?.MarkDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added artifact {Filename} to release v{Version}", artifact.Filename, release.Version);
        }
    }

    /// <summary>
    /// Removes an artifact from a release.
    /// </summary>
    [RelayCommand]
    private async Task RemoveArtifactFromReleaseAsync(ReleaseArtifact? artifact)
    {
        if (SelectedContent == null || artifact == null) return;

        var release = SelectedContent.Releases.FirstOrDefault(r => r.Artifacts.Contains(artifact));
        if (release != null)
        {
            release.Artifacts.Remove(artifact);
            OnPropertyChanged(nameof(SelectedContent));

            parentViewModel?.MarkDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Removed artifact {Filename} from release v{Version}", artifact.Filename, release.Version);
        }
    }

    /// <summary>
    /// Adds a dependency to an existing release.
    /// </summary>
    [RelayCommand]
    private async Task AddDependencyToReleaseAsync(ContentRelease? release)
    {
        if (SelectedContent == null || release == null) return;

        var dependency = await dialogService.ShowAddDependencyDialogAsync(activeCatalog.Catalog, SelectedContent);
        if (dependency != null)
        {
            release.Dependencies.Add(dependency);
            OnPropertyChanged(nameof(SelectedContent));

            parentViewModel?.MarkDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added dependency {DependencyId} to release v{Version}", dependency.ContentId, release.Version);
        }
    }

    /// <summary>
    /// Removes a dependency from a release.
    /// </summary>
    [RelayCommand]
    private async Task RemoveDependencyFromReleaseAsync(CatalogDependency? dependency)
    {
        if (SelectedContent == null || dependency == null) return;

        var release = SelectedContent.Releases.FirstOrDefault(r => r.Dependencies.Contains(dependency));
        if (release != null)
        {
            release.Dependencies.Remove(dependency);
            OnPropertyChanged(nameof(SelectedContent));

            parentViewModel?.MarkDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Removed dependency {DependencyId} from release v{Version}", dependency.ContentId, release.Version);
        }
    }
}
