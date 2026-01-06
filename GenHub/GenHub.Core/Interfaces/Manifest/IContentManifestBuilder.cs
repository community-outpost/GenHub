using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Interfaces.Manifest;

/// <summary>
/// Provides a fluent API for building comprehensive game manifests for different content types.
/// </summary>
public interface IContentManifestBuilder
{
    /// <summary>
    /// Sets basic content information for game installations (used internally by GenHub).
    /// This overload is specifically for generating manifests for EA/Steam game installations.
    /// </summary>
    /// <param name="installType">The game installation type (EA, Steam, etc.).</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder WithBasicInfo(GameInstallationType installType, GameType gameType, string? manifestVersion);

    /// <summary>
    /// Sets basic content information for game installations (used internally by GenHub).
    /// This overload is specifically for generating manifests for EA/Steam game installations.
    /// </summary>
    /// <param name="installType">The game installation type (EA, Steam, etc.).</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder WithBasicInfo(GameInstallationType installType, GameType gameType, int manifestVersion);

    /// <summary>
    /// Sets basic content information for publisher content (used by external publishers).
    /// This overload is for developers, modders, mappers, and other publishers creating content manifests.
    /// </summary>
    /// <param name="publisherId">Publisher identifier.</param>
    /// <param name="contentName">Content display name.</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder WithBasicInfo(string publisherId, string contentName, string? manifestVersion);

    /// <summary>
    /// Sets basic content information for publisher content (used by external publishers).
    /// This overload is for developers, modders, mappers, and other publishers creating content manifests.
    /// </summary>
    /// <param name="publisherId">Publisher identifier.</param>
    /// <param name="contentName">Content display name.</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder WithBasicInfo(string publisherId, string contentName, int manifestVersion);

    /// <summary>
    /// Sets basic content information with publisher info.
    /// </summary>
    /// <param name="publisher">Publisher information.</param>
    /// <param name="contentName">Content display name.</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder WithBasicInfo(PublisherInfo publisher, string contentName, string? manifestVersion);

    /// <summary>
    /// Sets basic content information with publisher info.
    /// </summary>
    /// <param name="publisher">Publisher information.</param>
    /// <param name="contentName">Content display name.</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder WithBasicInfo(PublisherInfo publisher, string contentName, int manifestVersion);

    /// <summary>
    /// Sets the content type and target game.
    /// </summary>
    /// <param name="contentType">The type of content (GameInstallation, Mod, Addon, etc.).</param>
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
    /// <param name="publisherType">Publisher type identifier for dependency validation.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder WithPublisher(string name, string website = "", string supportUrl = "", string contactEmail = "", string publisherType = "");

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
        ManifestId id,
        string name,
        ContentType dependencyType,
        DependencyInstallBehavior installBehavior,
        string minVersion = "",
        string maxVersion = "",
        List<string>? compatibleVersions = null,
        bool isExclusive = false,
        List<ManifestId>? conflictsWith = null);

    /// <summary>
    /// Scans a directory and adds files with the specified source type.
    /// </summary>
    /// <param name="sourceDirectory">The directory to scan.</param>
    /// <param name="sourceType">How these files should be handled during workspace preparation.</param>
    /// <param name="fileFilter">Optional file filter (e.g., "*.dll", "*.exe").</param>
    /// <param name="isExecutable">Whether files should be marked as executable.</param>
    /// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    Task<IContentManifestBuilder> AddFilesFromDirectoryAsync(string sourceDirectory, ContentSourceType sourceType = ContentSourceType.ContentAddressable, string fileFilter = "*", bool isExecutable = false);

    /// <summary>
    /// Adds a local file from the filesystem.
    /// </summary>
    /// <param name="relativePath">The relative path of the file in the workspace (destination).</param>
    /// <param name="sourcePath">The source path of the file on the local filesystem.</param>
    /// <param name="sourceType">How this file should be handled.</param>
    /// <param name="isExecutable">Whether the file is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    Task<IContentManifestBuilder> AddLocalFileAsync(string relativePath, string sourcePath, ContentSourceType sourceType = ContentSourceType.ContentAddressable, bool isExecutable = false, FilePermissions? permissions = null);

    /// <summary>
    /// Adds a remote file to be downloaded.
    /// </summary>
    /// <param name="relativePath">The relative path of the file in the workspace (destination).</param>
    /// <param name="downloadUrl">Download URL for the remote file.</param>
    /// <param name="sourceType">How this file should be handled.</param>
    /// <param name="isExecutable">Whether the file is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    Task<IContentManifestBuilder> AddRemoteFileAsync(string relativePath, string downloadUrl, ContentSourceType sourceType = ContentSourceType.ContentAddressable, bool isExecutable = false, FilePermissions? permissions = null);

    /// <summary>
    /// Downloads a file from a remote URL, stores it in CAS, and adds it to the manifest.
    /// This method actually downloads the file, computes its hash, stores it in Content-Addressable Storage,
    /// and adds a proper file entry with CAS reference to the manifest.
    /// </summary>
    /// <param name="relativePath">The relative path of the file in the workspace (destination).</param>
    /// <param name="downloadUrl">Download URL for the remote file.</param>
    /// <param name="sourceType">How this file should be handled (typically ContentAddressable).</param>
    /// <param name="isExecutable">Whether the file is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <param name="refererUrl">Optional referer URL to use for the download request.</param>
    /// <param name="userAgent">The User-Agent string to use for the download (optional).</param>
    /// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    Task<IContentManifestBuilder> AddDownloadedFileAsync(
        string relativePath,
        string downloadUrl,
        ContentSourceType sourceType = ContentSourceType.ContentAddressable,
        bool isExecutable = false,
        FilePermissions? permissions = null,
        string? refererUrl = null,
        string? userAgent = null);

    /// <summary>
    /// Adds a game installation file from the detected game installation.
    /// </summary>
    /// <param name="relativePath">The relative path of the file in the workspace (destination).</param>
    /// <param name="sourcePath">The source path of the file in the game installation.</param>
    /// <param name="isExecutable">Whether the file is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    Task<IContentManifestBuilder> AddGameInstallationFileAsync(string relativePath, string sourcePath, bool isExecutable = false, FilePermissions? permissions = null);

    /// <summary>
    /// Adds a content-addressable file from the CAS system.
    /// </summary>
    /// <param name="relativePath">The relative path of the file in the workspace (destination).</param>
    /// <param name="hash">The content hash for CAS lookup.</param>
    /// <param name="size">The expected file size.</param>
    /// <param name="isExecutable">Whether the file is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    Task<IContentManifestBuilder> AddContentAddressableFileAsync(string relativePath, string hash, long size, bool isExecutable = false, FilePermissions? permissions = null);

    /// <summary>
    /// Adds a file from an extracted package/archive.
    /// </summary>
    /// <param name="relativePath">The relative path of the file in the workspace (destination).</param>
    /// <param name="packagePath">The path to the package file.</param>
    /// <param name="internalPath">The path within the package.</param>
    /// <param name="isExecutable">Whether the file is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    Task<IContentManifestBuilder> AddExtractedPackageFileAsync(string relativePath, string packagePath, string internalPath, bool isExecutable = false, FilePermissions? permissions = null);

    /// <summary>
    /// Adds a pre-existing ManifestFile object to the manifest.
    /// </summary>
    /// <param name="file">The ManifestFile to add.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder AddFile(ManifestFile file);

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
    IContentManifestBuilder WithInstallationInstructions(WorkspaceStrategy workspaceStrategy = WorkspaceStrategy.HybridCopySymlink);

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
        ManifestId contentId,
        string publisherId,
        ContentType contentType,
        string minVersion = "",
        string maxVersion = "");

    /// <summary>
    /// Adds a file patching operation to the manifest.
    /// </summary>
    /// <param name="targetRelativePath">The relative path of the file in the workspace to be patched.</param>
    /// <param name="patchSourceFile">The path to the patch file, relative to the mod's content root.</param>
    /// <returns>The builder instance for chaining.</returns>
    IContentManifestBuilder AddPatchFile(string targetRelativePath, string patchSourceFile);

    /// <summary>
    /// Builds the final ContentManifest.
    /// </summary>
    /// <returns>The constructed ContentManifest.</returns>
    ContentManifest Build();
}