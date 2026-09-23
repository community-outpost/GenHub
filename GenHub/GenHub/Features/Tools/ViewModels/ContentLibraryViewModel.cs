using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for the Content Library tab in Publisher Studio.
/// Scoped to the currently active catalog.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "ViewModel properties and methods mutate CommunityToolkit generated instance properties.")]
public partial class ContentLibraryViewModel(
    PublisherStudioProject project,
    NamedCatalog activeCatalog,
    PublisherStudioViewModel parentViewModel,
    ILogger logger,
    IPublisherStudioDialogService dialogService,
    INotificationService? notificationService = null,
    ILocalizationService? localizationService = null) : ObservableObject
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
        : this(project, project?.Catalogs.FirstOrDefault() ?? new NamedCatalog { Id = "default", Name = "Content", Catalog = project?.Catalog ?? new() }, parentViewModel, logger, dialogService, null, null)
    {
    }

    /// <summary>
    /// Gets the publisher studio project.
    /// </summary>
    public PublisherStudioProject Project => project;

    /// <summary>
    /// Gets the name of the active catalog.
    /// </summary>
    public string ActiveCatalogName => activeCatalog?.Name ?? string.Empty;

    /// <summary>
    /// Gets the localized catalog item count summary for the footer.
    /// </summary>
    public string CatalogSummaryText => string.Format(
        localizationService?.GetString("Tools.PublisherStudio.Library.ItemsInCatalog") ?? "{0} items in catalog",
        ContentItems.Count);

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
    /// Gets the number of local artifacts in the selected content that are still waiting for cloud upload.
    /// </summary>
    public int PendingUploadCount => SelectedContent?.Releases
        .SelectMany(r => r.Artifacts)
        .Count(a => !string.IsNullOrEmpty(a.LocalFilePath) && string.IsNullOrEmpty(a.DownloadUrl)) ?? 0;

    /// <summary>
    /// Gets a value indicating whether a hosting provider is currently connected.
    /// </summary>
    public bool IsHostingConnected => parentViewModel?.PublishShareViewModel?.IsProviderAuthenticated ?? false;

    /// <summary>
    /// Gets a value indicating whether the hosting hint banner should be shown.
    /// Shown when local files are pending upload but no hosting provider is connected.
    /// </summary>
    public bool ShowHostingHint => PendingUploadCount > 0 && !IsHostingConnected;

    /// <summary>
    /// Gets the localized hosting hint message for pending uploads.
    /// </summary>
    public string HostingHintMessage => string.Format(
        localizationService?.GetString("Tools.PublisherStudio.Library.HostingHintMessage")
            ?? "{0} file(s) are waiting for upload. Connect a hosting provider to publish them to the cloud.",
        PendingUploadCount);

    /// <summary>
    /// Refreshes hosting-related hint bindings, for example after a provider was connected on another tab.
    /// </summary>
    public void RefreshHostingHint()
    {
        OnPropertyChanged(nameof(PendingUploadCount));
        OnPropertyChanged(nameof(IsHostingConnected));
        OnPropertyChanged(nameof(ShowHostingHint));
        OnPropertyChanged(nameof(HostingHintMessage));
    }

    /// <summary>
    /// Adds a new content item to the active catalog with an optional initial folder/file path.
    /// </summary>
    /// <param name="initialPath">Optional initial path of dropped or selected folder/file.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task AddContentWithPathAsync(string? initialPath) =>
        AddContentWithPathsAsync(initialPath != null ? [initialPath] : null);

    /// <summary>
    /// Adds a new content item to the active catalog with optional initial folder/file paths.
    /// </summary>
    /// <param name="initialPaths">Optional initial paths of dropped or selected folders/files.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AddContentWithPathsAsync(IEnumerable<string>? initialPaths)
    {
        var pathsList = initialPaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (pathsList is { Count: > 0 } && !pathsList.Any(p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p)))
        {
            var title = GetLocalizedString("Tools.PublisherStudio.Content.InvalidPathTitle", "Invalid Path");
            var message = GetLocalizedString("Tools.PublisherStudio.Content.InvalidPathMessage", "The specified file or folder does not exist.");
            notificationService?.ShowWarning(title, message);
            return;
        }

        var newContent = await dialogService.ShowAddContentDialogAsync(pathsList);
        if (newContent != null)
        {
            if (activeCatalog.Catalog.Content.Any(c => string.Equals(c.Id, newContent.Id, StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogWarning("Content with ID '{ContentId}' already exists in catalog '{CatalogId}'", newContent.Id, activeCatalog.Id);
                var title = GetLocalizedString("Tools.PublisherStudio.Content.DuplicateIdTitle", "Duplicate Content ID");
                var message = string.Format(
                    GetLocalizedString(
                        "Tools.PublisherStudio.Content.DuplicateIdMessageFormat",
                        "A content item with ID '{0}' already exists in this catalog."),
                    newContent.Id);
                notificationService?.ShowWarning(title, message);
                return;
            }

            activeCatalog.Catalog.Content.Add(newContent);
            ContentItems.Add(newContent);
            OnPropertyChanged(nameof(FilteredContent));
            OnPropertyChanged(nameof(CatalogSummaryText));
            SelectedContent = newContent;

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added new content item: {ContentId} to catalog: {CatalogId}", newContent.Id, activeCatalog.Id);
        }
    }

    /// <summary>
    /// Refreshes localized display text after a culture change.
    /// </summary>
    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(CatalogSummaryText));
    }

    /// <summary>
    /// Gets a value indicating whether the active catalog needs publishing.
    /// </summary>
    public bool ActiveCatalogNeedsPublish =>
        parentViewModel?.PublishShareViewModel?.CatalogNeedsPublish(activeCatalog.Id) ?? true;

    /// <summary>
    /// Rebuilds the content detail display after artifact uploads mutate models in place.
    /// Invoked from the publish pipeline so pending badges and hints update without switching tabs.
    /// </summary>
    public void RefreshContentDisplay()
    {
        RefreshSelectedContent();
        OnPropertyChanged(nameof(CatalogSummaryText));
        OnPropertyChanged(nameof(ActiveCatalogNeedsPublish));
    }

    /// <summary>
    /// Handles dropped files onto the Addon section.
    /// </summary>
    /// <param name="paths">The dropped file or directory paths.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AddAddonWithPathsAsync(IEnumerable<string> paths)
    {
        if (SelectedContent == null)
        {
            return;
        }

        var created = await dialogService.ShowAddAddonDialogAsync(SelectedContent, activeCatalog.Catalog, paths);
        if (created != null)
        {
            SelectedContent.AddonReleases.Add(created);
            RefreshSelectedContent();
            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }
        }
    }

    /// <summary>
    /// Handles dropped files onto the Release section.
    /// </summary>
    /// <param name="paths">The dropped file or directory paths.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AddReleaseWithPathsAsync(IEnumerable<string> paths)
    {
        if (SelectedContent == null)
        {
            return;
        }

        var created = await dialogService.ShowAddReleaseDialogAsync(SelectedContent, activeCatalog.Catalog, paths);
        if (created != null)
        {
            if (created.IsLatest)
            {
                foreach (var rel in SelectedContent.Releases)
                {
                    rel.IsLatest = false;
                }
            }

            SelectedContent.Releases.Add(created);
            RefreshSelectedContent();
            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }
        }
    }

    private void MarkProjectAndCatalogDirty()
    {
        parentViewModel?.MarkDirty();
        parentViewModel?.PublishShareViewModel?.MarkCatalogChanged(activeCatalog.Id);
        OnPropertyChanged(nameof(ActiveCatalogNeedsPublish));
    }

    /// <summary>
    /// Uploads a single artifact directly from the Content Library pending list.
    /// </summary>
    /// <param name="artifact">The artifact to upload.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task UploadArtifactAsync(ReleaseArtifact? artifact)
    {
        if (artifact == null)
        {
            return;
        }

        var publishShare = GetPublishShareOrWarn();
        if (publishShare == null)
        {
            return;
        }

        await publishShare.UploadArtifactFromLibraryAsync(artifact);
    }

    /// <summary>
    /// Navigates to the Hosting &amp; Cloud Storage tab.
    /// </summary>
    [RelayCommand]
    private void GoToHosting()
    {
        if (parentViewModel != null)
        {
            parentViewModel.SelectedTabIndex = PublisherStudioViewModel.TabHostingStorage;
        }
    }

    /// <summary>
    /// Uploads the active catalog to the connected hosting provider.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task UploadActiveCatalogAsync()
    {
        var publishShare = GetPublishShareOrWarn();
        if (publishShare == null)
        {
            return;
        }

        if (publishShare.IsUploading)
        {
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Publish.PublishCatalog", "Publish Catalog"),
                GetLocalizedString("Tools.PublisherStudio.Publish.UploadAlreadyInProgress", "An upload is already in progress."));
            return;
        }

        await publishShare.PublishCatalogByIdCommand.ExecuteAsync(activeCatalog.Id);
    }

    private PublishShareViewModel? GetPublishShareOrWarn()
    {
        var publishShare = parentViewModel?.PublishShareViewModel;
        if (publishShare != null)
        {
            return publishShare;
        }

        var title = GetLocalizedString("Tools.PublisherStudio.Publish.ProviderNotConnected", "Provider Not Connected");
        var message = GetLocalizedString("Tools.PublisherStudio.Hosting.ConnectBeforeUpload", "Connect to your hosting provider before uploading files.");
        notificationService?.ShowWarning(title, message);
        return null;
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
    /// Edits a content item using a pre-populated dialog.
    /// </summary>
    /// <param name="item">The item to edit, or null to edit the selected item.</param>
    [RelayCommand]
    private async Task EditContentAsync(CatalogContentItem? item)
    {
        var target = item ?? SelectedContent;
        if (target == null) return;

        var edited = await dialogService.ShowEditContentDialogAsync(target);
        if (edited != null)
        {
            // Update the existing item's properties
            target.Name = edited.Name;
            target.Description = edited.Description;
            target.ContentType = edited.ContentType;
            target.TargetGame = edited.TargetGame;
            target.Tags = edited.Tags;
            target.ExtendsContentId = edited.ExtendsContentId;
            target.Metadata = edited.Metadata;

            // Trigger UI update
            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Updated content item: {ContentId}", target.Id);
        }
    }

    /// <summary>
    /// Deletes a content item from the active catalog.
    /// </summary>
    /// <param name="item">The item to delete, or null to delete the selected item.</param>
    [RelayCommand]
    private async Task DeleteContentAsync(CatalogContentItem? item)
    {
        var target = item ?? SelectedContent;
        if (target == null)
        {
            return;
        }

        var title = GetLocalizedString("Tools.PublisherStudio.Content.DeleteContentTitle", "Delete Content Item");
        var message = string.Format(
            GetLocalizedString(
                "Tools.PublisherStudio.Content.DeleteContentMessageFormat",
                "Are you sure you want to delete '{0}' ({1})? This will also remove all its releases and artifacts."),
            target.Name,
            target.Id);

        var confirmed = await dialogService.ShowConfirmationAsync(title, message);

        if (!confirmed)
        {
            return;
        }

        var contentId = target.Id;
        activeCatalog.Catalog.Content.Remove(target);
        ContentItems.Remove(target);
        if (SelectedContent == target)
        {
            SelectedContent = null;
        }

        OnPropertyChanged(nameof(FilteredContent));
        OnPropertyChanged(nameof(CatalogSummaryText));

        MarkProjectAndCatalogDirty();
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
            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
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
            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
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
        RefreshSelectedContent();

        MarkProjectAndCatalogDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }

        logger.LogInformation("Removed bundled item {DependencyId} from {ContentId}", dependency.ContentId, SelectedContent.Id);
    }

    /// <summary>
    /// Adds an addon dependency to the selected content item.
    /// </summary>
    /// <summary>
    /// Adds an addon release to the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task AddAddonAsync()
    {
        if (SelectedContent == null) return;

        var newAddon = await dialogService.ShowAddAddonDialogAsync(SelectedContent, activeCatalog.Catalog);
        if (newAddon != null)
        {
            SelectedContent.AddonReleases.Add(newAddon);
            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added addon {AddonTitle} (v{Version}) to {ContentId} in catalog: {CatalogId}", newAddon.Title, newAddon.Version, SelectedContent.Id, activeCatalog.Id);
        }
    }

    /// <summary>
    /// Edits an addon release on the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task EditAddonAsync(ContentRelease? addon)
    {
        if (SelectedContent == null || addon == null) return;

        var edited = await dialogService.ShowEditAddonDialogAsync(addon, SelectedContent, activeCatalog.Catalog);
        if (edited != null)
        {
            var index = SelectedContent.AddonReleases.IndexOf(addon);
            if (index >= 0)
            {
                SelectedContent.AddonReleases[index] = edited;
            }

            RefreshSelectedContent();
            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Updated addon {AddonTitle} (v{Version}) on {ContentId}", edited.Title, edited.Version, SelectedContent.Id);
        }
    }

    /// <summary>
    /// Deletes an addon release from the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAddonAsync(ContentRelease? addon)
    {
        if (SelectedContent == null || addon == null) return;

        var title = GetLocalizedString("Tools.PublisherStudio.Addon.DeleteTitle", "Delete Addon");
        var message = string.Format(
            GetLocalizedString(
                "Tools.PublisherStudio.Addon.DeleteMessageFormat",
                "Are you sure you want to delete addon '{0}'? This will also remove its artifacts and cannot be undone."),
            addon.Title ?? addon.Version);

        var confirmed = await dialogService.ShowConfirmationAsync(title, message);
        if (!confirmed) return;

        if (!SelectedContent.AddonReleases.Remove(addon)) return;

        RefreshSelectedContent();
        MarkProjectAndCatalogDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectSilentAsync();
        }

        logger.LogInformation("Deleted addon {AddonTitle} from {ContentId}", addon.Title, SelectedContent.Id);
    }

    /// <summary>
    /// Removes a legacy addon dependency from the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task RemoveAddonAsync(CatalogDependency? dependency)
    {
        if (SelectedContent == null || dependency == null) return;

        SelectedContent.Addons.Remove(dependency);
        RefreshSelectedContent();

        MarkProjectAndCatalogDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }

        logger.LogInformation("Removed addon dependency {DependencyId} from {ContentId}", dependency.ContentId, SelectedContent.Id);
    }

    /// <summary>
    /// Deletes a release from the selected content item after user confirmation.
    /// </summary>
    [RelayCommand]
    private async Task DeleteReleaseAsync(ContentRelease? release)
    {
        if (SelectedContent == null || release == null)
        {
            return;
        }

        var title = GetLocalizedString("Tools.PublisherStudio.Library.DeleteReleaseTitle", "Delete Release");
        var message = string.Format(
            GetLocalizedString(
                "Tools.PublisherStudio.Library.DeleteReleaseMessageFormat",
                "Are you sure you want to delete release v{0}? This will also remove its artifacts and cannot be undone."),
            release.Version);

        var confirmed = await dialogService.ShowConfirmationAsync(title, message);
        if (!confirmed)
        {
            return;
        }

        var contentId = SelectedContent.Id;
        var version = release.Version;
        if (!SelectedContent.Releases.Remove(release))
        {
            return;
        }

        RefreshSelectedContent();

        MarkProjectAndCatalogDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectSilentAsync();
        }

        var deletedTitle = GetLocalizedString("Tools.PublisherStudio.Library.ReleaseDeletedTitle", "Release Deleted");
        var deletedMessage = string.Format(
            GetLocalizedString(
                "Tools.PublisherStudio.Library.ReleaseDeletedMessageFormat",
                "Release v{0} was deleted."),
            version);
        notificationService?.ShowSuccess(deletedTitle, deletedMessage);

        logger.LogInformation("Removed release v{Version} from content: {ContentId}", version, contentId);
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

            // Artifact instances were replaced, so refresh the Publish tab statuses built from the old objects
            parentViewModel?.PublishShareViewModel?.RefreshUploadHierarchy();
            parentViewModel?.PublishShareViewModel?.RefreshArtifactStatuses();

            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
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
            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
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
            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
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
            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
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
            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Removed dependency {DependencyId} from release v{Version}", dependency.ContentId, release.Version);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredContent));
    }

    partial void OnSelectedContentChanged(CatalogContentItem? value)
    {
        _ = value;
        RefreshHostingHint();
    }

    /// <summary>
    /// Forces the content detail panel to rebuild.
    /// Selected content instances are mutable models, so mutating their releases, artifacts,
    /// or dependencies in place does not raise change notifications on its own.
    /// </summary>
    private void RefreshSelectedContent()
    {
        var selected = SelectedContent;
        if (selected == null)
        {
            return;
        }

        SelectedContent = null;
        SelectedContent = selected;
        OnPropertyChanged(nameof(FilteredContent));
        RefreshHostingHint();
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
        OnPropertyChanged(nameof(CatalogSummaryText));
    }

    partial void OnContentItemsChanged(ObservableCollection<CatalogContentItem> value)
    {
        OnPropertyChanged(nameof(CatalogSummaryText));
    }

    private string GetLocalizedString(string key, string fallback)
    {
        return localizationService?.GetString(key) ?? fallback;
    }
}
