namespace GenHub.Core.Constants;

/// <summary>
/// Constants for Publisher Studio project storage paths and settings.
/// </summary>
public static class PublisherStudioConstants
{
    /// <summary>
    /// Folder name for Publisher Studio data under the application data directory.
    /// </summary>
    public const string StudioFolderName = "PublisherStudio";

    /// <summary>
    /// Folder name for Publisher Studio projects under the studio folder.
    /// </summary>
    public const string ProjectsFolderName = "projects";

    /// <summary>
    /// File name of the default publisher project.
    /// </summary>
    public const string DefaultProjectFileName = "default-publisher.json";

    /// <summary>
    /// File name of the Publisher Studio settings file under the application data directory.
    /// </summary>
    public const string SettingsFileName = "publisher_studio_settings.json";

    /// <summary>
    /// Resource key for the error shown when a content item without releases blocks catalog export or publish.
    /// </summary>
    public const string ExportDraftMessageKey = "Tools.PublisherStudio.Validation.ExportDraft";

    /// <summary>
    /// English fallback for <see cref="ExportDraftMessageKey"/>. Placeholder {0} is the item name, or its ID when the name is empty.
    /// </summary>
    public const string ExportDraftMessage = "'{0}' has no releases. Add a release or remove this draft from the catalog before exporting. Your project can still be saved.";
}
