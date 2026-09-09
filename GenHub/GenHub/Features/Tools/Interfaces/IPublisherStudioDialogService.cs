using System.Threading.Tasks;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;

namespace GenHub.Features.Tools.Interfaces;

/// <summary>
/// Service interface for displaying Publisher Studio dialogs and prompts.
/// </summary>
public interface IPublisherStudioDialogService
{
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
    /// <returns>The created content item, or null if cancelled.</returns>
    Task<CatalogContentItem?> ShowAddContentDialogAsync();

    /// <summary>
    /// Shows the edit content dialog for an existing content item.
    /// </summary>
    /// <param name="existing">The existing content item to edit.</param>
    /// <returns>The updated content item, or null if cancelled.</returns>
    Task<CatalogContentItem?> ShowEditContentDialogAsync(CatalogContentItem existing);

    /// <summary>
    /// Shows the add release dialog for a content item.
    /// </summary>
    /// <param name="contentItem">The parent content item.</param>
    /// <param name="catalog">The parent catalog.</param>
    /// <returns>The created release, or null if cancelled.</returns>
    Task<ContentRelease?> ShowAddReleaseDialogAsync(CatalogContentItem contentItem, PublisherCatalog catalog);

    /// <summary>
    /// Shows the edit release dialog for an existing release.
    /// </summary>
    /// <param name="existing">The existing release.</param>
    /// <param name="parent">The parent content item.</param>
    /// <param name="catalog">The parent catalog.</param>
    /// <returns>The updated release, or null if cancelled.</returns>
    Task<ContentRelease?> ShowEditReleaseDialogAsync(ContentRelease existing, CatalogContentItem parent, PublisherCatalog catalog);

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
    /// Shows a file picker dialog for selecting artifact files.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <returns>The selected file path, or null if cancelled.</returns>
    Task<string?> ShowFilePickerAsync(string title);

    /// <summary>
    /// Shows the rename catalog dialog.
    /// </summary>
    /// <param name="currentName">The current name of the catalog.</param>
    /// <returns>The new catalog name, or null if cancelled.</returns>
    Task<string?> ShowRenameCatalogDialogAsync(string currentName);
}
