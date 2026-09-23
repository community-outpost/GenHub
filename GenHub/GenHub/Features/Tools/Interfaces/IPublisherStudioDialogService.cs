using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Interfaces;

/// <summary>
/// Service interface for displaying Publisher Studio dialogs and prompts.
/// </summary>
public interface IPublisherStudioDialogService
{
    /// <summary>
    /// Gets or sets an optional lookup function for duplicate hosted assets by SHA-256 hash.
    /// Returns file name, URL, and size if an identical asset is already hosted.
    /// </summary>
    Func<string, (string Name, string Url, long Size)?>? DuplicateAssetLookup { get; set; }

    /// <summary>
    /// Shows a confirmation dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The confirmation message.</param>
    /// <param name="confirmText">The confirm button text.</param>
    /// <param name="cancelText">The cancel button text.</param>
    /// <param name="sessionKey">Optional session key for "do not ask again".</param>
    /// <returns>True if confirmed, false otherwise.</returns>
    Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string? confirmText = null,
        string? cancelText = null,
        string? sessionKey = null);

    /// <summary>
    /// Shows the setup wizard for initial publisher profile configuration.
    /// </summary>
    /// <param name="project">The project to configure.</param>
    /// <returns>True if configuration was completed; otherwise false.</returns>
    Task<bool> ShowSetupWizardAsync(PublisherStudioProject project);

    /// <summary>
    /// Shows the hosting settings dialog.
    /// </summary>
    /// <returns>True if settings were saved; otherwise false.</returns>
    Task<bool> ShowHostingSettingsDialogAsync();

    /// <summary>
    /// Shows the add content dialog to create a new content item.
    /// </summary>
    /// <param name="initialPath">Optional initial folder or file path to populate from.</param>
    /// <param name="catalog">Optional parent catalog.</param>
    /// <returns>The created content item, or null if cancelled.</returns>
    Task<CatalogContentItem?> ShowAddContentDialogAsync(string? initialPath = null, PublisherCatalog? catalog = null);

    /// <summary>
    /// Shows the add content dialog to create a new content item from multiple initial paths.
    /// </summary>
    /// <param name="initialPaths">Optional initial folder or file paths to populate from.</param>
    /// <param name="catalog">Optional parent catalog.</param>
    /// <returns>The created content item, or null if cancelled.</returns>
    Task<CatalogContentItem?> ShowAddContentDialogAsync(IEnumerable<string>? initialPaths = null, PublisherCatalog? catalog = null);

    /// <summary>
    /// Shows the edit content dialog for an existing content item.
    /// </summary>
    /// <param name="existing">The existing content item to edit.</param>
    /// <param name="catalog">Optional parent catalog.</param>
    /// <param name="onDelete">Optional callback to delete the content item from the catalog.</param>
    /// <returns>The updated content item, or null if cancelled.</returns>
    Task<CatalogContentItem?> ShowEditContentDialogAsync(CatalogContentItem existing, PublisherCatalog? catalog = null, Func<CatalogContentItem, Task>? onDelete = null);

    /// <summary>
    /// Shows the add release dialog for a content item.
    /// </summary>
    /// <param name="contentItem">The parent content item.</param>
    /// <param name="catalog">The parent catalog.</param>
    /// <param name="initialPaths">Optional file or directory paths to pre-populate as artifacts.</param>
    /// <returns>The created release, or null if cancelled.</returns>
    Task<ContentRelease?> ShowAddReleaseDialogAsync(CatalogContentItem contentItem, PublisherCatalog catalog, IEnumerable<string>? initialPaths = null);

    /// <summary>
    /// Shows the edit release dialog for an existing release.
    /// </summary>
    /// <param name="existing">The existing release.</param>
    /// <param name="parent">The parent content item.</param>
    /// <param name="catalog">The parent catalog.</param>
    /// <returns>The updated release, or null if cancelled.</returns>
    Task<ContentRelease?> ShowEditReleaseDialogAsync(ContentRelease existing, CatalogContentItem parent, PublisherCatalog catalog);

    /// <summary>
    /// Shows the add addon dialog for a content item.
    /// </summary>
    /// <param name="contentItem">The parent content item.</param>
    /// <param name="catalog">The parent catalog.</param>
    /// <param name="initialPaths">Optional file or directory paths to pre-populate as artifacts.</param>
    /// <returns>The created addon release, or null if cancelled.</returns>
    Task<ContentRelease?> ShowAddAddonDialogAsync(CatalogContentItem contentItem, PublisherCatalog catalog, IEnumerable<string>? initialPaths = null);

    /// <summary>
    /// Shows the edit addon dialog for an existing addon release.
    /// </summary>
    /// <param name="existing">The existing addon release.</param>
    /// <param name="parent">The parent content item.</param>
    /// <param name="catalog">The parent catalog.</param>
    /// <returns>The updated addon release, or null if cancelled.</returns>
    Task<ContentRelease?> ShowEditAddonDialogAsync(ContentRelease existing, CatalogContentItem parent, PublisherCatalog catalog);

    /// <summary>
    /// Shows the add artifact dialog to attach a file to a release.
    /// </summary>
    /// <returns>The created release artifact, or null if cancelled.</returns>
    Task<ReleaseArtifact?> ShowAddArtifactDialogAsync();

    /// <summary>
    /// Shows the add dependency dialog for a content item.
    /// </summary>
    /// <param name="catalog">The parent catalog.</param>
    /// <param name="currentContent">The current content item.</param>
    /// <returns>The created dependency, or null if cancelled.</returns>
    Task<CatalogDependency?> ShowAddDependencyDialogAsync(PublisherCatalog catalog, CatalogContentItem currentContent);

    /// <summary>
    /// Shows the add referral dialog to recommend a publisher.
    /// </summary>
    /// <returns>The created referral, or null if cancelled.</returns>
    Task<PublisherReferral?> ShowAddReferralDialogAsync();

    /// <summary>
    /// Shows an open file prompt for loading a project file.
    /// </summary>
    /// <param name="title">Title of the prompt.</param>
    /// <returns>The selected file path, or null if cancelled.</returns>
    Task<string?> ShowProjectOpenPromptAsync(string title);

    /// <summary>
    /// Shows a save file prompt for saving a project file.
    /// </summary>
    /// <param name="title">Title of the prompt.</param>
    /// <returns>The selected file path, or null if cancelled.</returns>
    Task<string?> ShowProjectSavePromptAsync(string title);

    /// <summary>
    /// Shows a file picker dialog for selecting a catalog JSON file.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <returns>The selected file path, or null if cancelled.</returns>
    Task<string?> ShowCatalogFilePickerAsync(string title);

    /// <summary>
    /// Shows a file picker dialog for selecting artifact files.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <returns>The selected file path, or null if cancelled.</returns>
    Task<string?> ShowFilePickerAsync(string title);

    /// <summary>
    /// Shows a file picker dialog for selecting multiple content files.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <returns>The selected file paths, or an empty list if cancelled.</returns>
    Task<IReadOnlyList<string>> ShowFilesPickerAsync(string title);

    /// <summary>
    /// Shows a file picker dialog filtered to artwork image files.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <returns>The selected file path, or null if cancelled.</returns>
    Task<string?> ShowImagePickerAsync(string title);

    /// <summary>
    /// Shows a file picker dialog filtered to video files.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <returns>The selected file path, or null if cancelled.</returns>
    Task<string?> ShowVideoPickerAsync(string title);

    /// <summary>
    /// Shows a folder picker dialog.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <returns>The selected directory path, or null if cancelled.</returns>
    Task<string?> ShowFolderPickerAsync(string title);
}
