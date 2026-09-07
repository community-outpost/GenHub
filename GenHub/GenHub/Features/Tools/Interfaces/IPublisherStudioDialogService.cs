using System.Threading.Tasks;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;

namespace GenHub.Features.Tools.Interfaces;

/// <summary>
/// Service for displaying Publisher Studio specific dialogs.
/// </summary>
public interface IPublisherStudioDialogService
{
    /// <summary>
    /// Shows the publisher setup wizard.
    /// </summary>
    /// <param name="project">The project to setup.</param>
    /// <returns>True if setup completed, false if cancelled.</returns>
    Task<bool> ShowSetupWizardAsync(PublisherStudioProject project);

    /// <summary>
    /// Shows the add content dialog.
    /// </summary>
    /// <returns>The created content item, or null if cancelled.</returns>
    Task<CatalogContentItem?> ShowAddContentDialogAsync();

    /// <summary>
    /// Shows a dialog to configure hosting provider credentials.
    /// </summary>
    /// <returns>True if settings were saved.</returns>
    Task<bool> ShowHostingSettingsDialogAsync();

    /// <summary>
    /// Shows the edit content dialog pre-populated with an existing content item's data.
    /// </summary>
    /// <param name="existing">The existing content item to edit.</param>
    /// <returns>The edited content item, or null if cancelled.</returns>
    Task<CatalogContentItem?> ShowEditContentDialogAsync(CatalogContentItem existing);

    /// <summary>
    /// Shows the add release dialog.
    /// </summary>
    /// <param name="contentItem">The content item to add release to.</param>
    /// <param name="catalog">The publisher catalog.</param>
    /// <returns>The created release, or null if cancelled.</returns>
    Task<ContentRelease?> ShowAddReleaseDialogAsync(CatalogContentItem contentItem, PublisherCatalog catalog);

    /// <summary>
    /// Shows the edit release dialog pre-populated with an existing release's data.
    /// </summary>
    /// <param name="existing">The existing release to edit.</param>
    /// <param name="parent">The parent content item.</param>
    /// <param name="catalog">The publisher catalog.</param>
    /// <returns>The edited release, or null if cancelled.</returns>
    Task<ContentRelease?> ShowEditReleaseDialogAsync(ContentRelease existing, CatalogContentItem parent, PublisherCatalog catalog);

    /// <summary>
    /// Shows the add artifact dialog.
    /// </summary>
    /// <returns>The created artifact, or null if cancelled.</returns>
    Task<ReleaseArtifact?> ShowAddArtifactDialogAsync();

    /// <summary>
    /// Shows the add dependency dialog.
    /// </summary>
    /// <param name="catalog">The publisher catalog.</param>
    /// <param name="currentContent">The current content item.</param>
    /// <returns>The created dependency, or null if cancelled.</returns>
    Task<CatalogDependency?> ShowAddDependencyDialogAsync(PublisherCatalog catalog, CatalogContentItem currentContent);

    /// <summary>
    /// Shows a prompt to select a save location for the project.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <returns>The selected file path, or null if cancelled.</returns>
    Task<string?> ShowProjectSavePromptAsync(string title);

    /// <summary>
    /// Shows the add referral dialog.
    /// </summary>
    /// <returns>The created referral, or null if cancelled.</returns>
    Task<PublisherReferral?> ShowAddReferralDialogAsync();

    /// <summary>
    /// Shows a prompt to select a project file to open.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <returns>The selected file path, or null if cancelled.</returns>
    Task<string?> ShowProjectOpenPromptAsync(string title);

    /// <summary>
    /// Shows a file picker dialog for selecting artifact files.
    /// </summary>
    /// <param name="title">Title of the dialog.</param>
    /// <returns>The selected file path, or null if cancelled.</returns>
    Task<string?> ShowFilePickerAsync(string title);

    /// <summary>
    /// Shows the welcome screen for first-time users.
    /// </summary>
    /// <returns>The result of the welcome screen interaction.</returns>
    Task<ViewModels.WelcomeScreenResult?> ShowWelcomeScreenAsync();
}
