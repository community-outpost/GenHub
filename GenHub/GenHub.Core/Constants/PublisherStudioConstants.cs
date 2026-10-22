namespace GenHub.Core.Constants;

/// <summary>
/// Constants for Publisher Studio project storage paths, settings, and authoring defaults.
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
    /// Default initial version assigned to new content and first releases.
    /// </summary>
    public const string DefaultInitialVersion = "1.0.0";
}
