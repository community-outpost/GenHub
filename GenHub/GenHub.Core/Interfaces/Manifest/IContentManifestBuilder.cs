using System.Collections.Generic;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Interfaces.Manifest;

/// <summary>
/// Provides a fluent API for building comprehensive game manifests for different content types.
/// </summary>
public interface IContentManifestBuilder
{
    /// <summary>
    /// Sets basic content information.
    /// </summary>
    /// <param name="id">The unique content identifier.</param>
    /// <param name="name">The display name.</param>
    /// <param name="version">The content version.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder WithBasicInfo(string id, string name, string version);

    /// <summary>
    /// Sets the content type and target game.
    /// </summary>
    /// <param name="contentType">The type of content (BaseGame, Mod, Addon, etc.).</param>
    /// <param name="targetGame">The target game type.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder WithContentType(ContentType contentType, GameType targetGame);

    /// <summary>
    /// Sets publisher information.
    /// </summary>
    /// <param name="name">Publisher name.</param>
    /// <param name="website">Publisher website.</param>
    /// <param name="supportUrl">Support URL.</param>
    /// <param name="contactEmail">Contact email.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder WithPublisher(string name, string website = "", string supportUrl = "", string contactEmail = "");

    /// <summary>
    /// Sets content metadata.
    /// </summary>
    /// <param name="description">Content description.</param>
    /// <param name="tags">Content tags.</param>
    /// <param name="iconUrl">Icon URL.</param>
    /// <param name="screenshotUrls">Screenshot URLs.</param>
    /// <param name="changelogUrl">Changelog URL.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder WithMetadata(string description, List<string>? tags = null, string iconUrl = "", List<string>? screenshotUrls = null, string changelogUrl = "");

    /// <summary>
    /// Adds a content dependency.
    /// </summary>
    /// <param name="id">Dependency ID.</param>
    /// <param name="name">Dependency name.</param>
    /// <param name="dependencyType">The type of dependency.</param>
    /// <param name="installBehavior">Defines the requirement and installation action for this dependency.</param>
    /// <param name="minVersion">Minimum required version.</param>
    /// <param name="maxVersion">Maximum allowed version.</param>
    /// <param name="compatibleVersions">List of compatible versions.</param>
    /// <param name="isExclusive">Whether the dependency is exclusive.</param>
    /// <param name="conflictsWith">List of conflicting dependency IDs.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder AddDependency(
        string id,
        string name,
        ContentType dependencyType,
        DependencyInstallBehavior installBehavior,
        string minVersion = "",
        string maxVersion = "",
        List<string>? compatibleVersions = null,
        bool isExclusive = false,
        List<string>? conflictsWith = null);

    /// <summary>
    /// Scans a directory and adds files with the specified source type.
    /// </summary>
    /// <param name="sourceDirectory">The directory to scan.</param>
    /// <param name="sourceType">How these files should be handled during workspace preparation.</param>
    /// <param name="fileFilter">Optional file filter (e.g., "*.dll", "*.exe").</param>
    /// <param name="isExecutable">Whether files should be marked as executable.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task<IContentManifestBuilder> AddFilesFromDirectoryAsync(string sourceDirectory, ManifestFileSourceType sourceType = ManifestFileSourceType.CopyUnique, string fileFilter = "*", bool isExecutable = false);

    /// <summary>
    /// Adds a single file with specific properties.
    /// </summary>
    /// <param name="relativePath">The relative path of the file.</param>
    /// <param name="sourceType">How this file should be handled.</param>
    /// <param name="downloadUrl">Download URL for remote files.</param>
    /// <param name="isExecutable">Whether the file is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task<IContentManifestBuilder> AddFileAsync(string relativePath, ManifestFileSourceType sourceType = ManifestFileSourceType.CopyUnique, string downloadUrl = "", bool isExecutable = false, FilePermissions? permissions = null);

    /// <summary>
    /// Adds required directories.
    /// </summary>
    /// <param name="directories">List of required directory paths.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder AddRequiredDirectories(params string[] directories);

    /// <summary>
    /// Sets installation instructions and workspace strategy.
    /// </summary>
    /// <param name="workspaceStrategy">The workspace preparation strategy.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder WithInstallationInstructions(WorkspaceStrategy workspaceStrategy = WorkspaceStrategy.HybridSymlink);

    /// <summary>
    /// Adds a pre-installation step.
    /// </summary>
    /// <param name="name">Step name.</param>
    /// <param name="command">Command to execute.</param>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="workingDirectory">Working directory for the command.</param>
    /// <param name="requiresElevation">Whether elevation is required.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder AddPreInstallStep(string name, string command, List<string>? arguments = null, string workingDirectory = "", bool requiresElevation = false);

    /// <summary>
    /// Adds a post-installation step.
    /// </summary>
    /// <param name="name">Step name.</param>
    /// <param name="command">Command to execute.</param>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="workingDirectory">Working directory for the command.</param>
    /// <param name="requiresElevation">Whether elevation is required.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder AddPostInstallStep(string name, string command, List<string>? arguments = null, string workingDirectory = "", bool requiresElevation = false);

    /// <summary>
    /// Adds a content reference for cross-publisher linking.
    /// </summary>
    /// <param name="contentId">The referenced content ID.</param>
    /// <param name="publisherId">The publisher ID.</param>
    /// <param name="contentType">The content type of the reference.</param>
    /// <param name="minVersion">The minimum compatible version.</param>
    /// <param name="maxVersion">The maximum compatible version.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder AddContentReference(
        string contentId,
        string publisherId,
        ContentType contentType,
        string minVersion = "",
        string maxVersion = "");

    /// <summary>
    /// Builds the final GameManifest.
    /// </summary>
    /// <returns>The constructed GameManifest.</returns>
    GameManifest Build();
}
