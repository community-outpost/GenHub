using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// Fluent builder for creating comprehensive game manifests.
/// </summary>
public class ContentManifestBuilder(
    ILogger<ContentManifestBuilder> logger,
    IFileHashProvider hashProvider,
    IManifestIdService manifestIdService) : IContentManifestBuilder
{
    private readonly ILogger<ContentManifestBuilder> _logger = logger;
    private readonly ContentManifest _manifest = new();
    private readonly IFileHashProvider _hashProvider = hashProvider;
    private readonly IManifestIdService _manifestIdService = manifestIdService;

    /// <summary>
    /// Sets the basic information for the manifest for game installations (used internally by GenHub).
    /// This overload is specifically for generating manifests for EA/Steam game installations.
    /// </summary>
    /// <param name="installType">The game installation type (EA, Steam, etc.).</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder WithBasicInfo(GameInstallationType installType, GameType gameType, int manifestVersion)
    {
        // Build a temporary GameInstallation to generate the ID
        var tempInstallation = new GameInstallation(string.Empty, installType);
        tempInstallation.HasZeroHour = gameType == GameType.ZeroHour;

        // Use ManifestIdService for consistent ID generation with ResultBase pattern
        var idResult = _manifestIdService.GenerateGameInstallationId(tempInstallation, gameType, manifestVersion);
        if (idResult.Success)
        {
            _manifest.Id = idResult.Data;
        }
        else
        {
            _logger.LogWarning("Failed to generate game installation manifest ID: {Error}. Using fallback.", idResult.FirstError);

            // Fallback to direct generation if service fails
            _manifest.Id = ManifestId.Create(
                ManifestIdGenerator.GenerateGameInstallationId(tempInstallation, gameType, manifestVersion));
        }

        _manifest.Name = gameType.ToString().ToLowerInvariant();
        _manifest.Version = manifestVersion.ToString();

        _logger.LogDebug(
            "Set basic info for game installation: ID={Id}, Name={Name}, ManifestVersion={ManifestVersion}, InstallType={InstallType}, GameType={GameType}",
            _manifest.Id,
            _manifest.Name,
            _manifest.Version,
            installType,
            gameType);

        // Ensure the generated ID conforms to the project's validation rules.
        ManifestIdValidator.EnsureValid(_manifest.Id);

        return this;
    }

    /// <summary>
    /// Sets the basic information for the manifest for publisher content (used by external publishers).
    /// This overload is for developers, modders, mappers, and other publishers creating content manifests.
    /// </summary>
    /// <param name="publisherId">Publisher identifier.</param>
    /// <param name="contentName">Content display name.</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder WithBasicInfo(string publisherId, string contentName, int manifestVersion)
    {
        // Always generate the canonical ID from publisher name, content name, and version.
        var idResult = _manifestIdService.GeneratePublisherContentId(publisherId, contentName, manifestVersion);
        if (idResult.Success)
        {
            _manifest.Id = idResult.Data;
        }
        else
        {
            _logger.LogWarning("Failed to generate publisher content manifest ID: {Error}. Using fallback.", idResult.FirstError);

            // Fallback to direct generation if service fails
            _manifest.Id = ManifestId.Create(
                ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentName, manifestVersion));
        }

        _manifest.Name = contentName;
        _manifest.Version = manifestVersion.ToString();

        _logger.LogDebug(
            "Set basic info for publisher content: ID={Id}, Name={Name}, ManifestVersion={ManifestVersion}, Publisher={Publisher}",
            _manifest.Id,
            _manifest.Name,
            _manifest.Version,
            publisherId);

        // Ensure the generated ID conforms to the project's validation rules.
        ManifestIdValidator.EnsureValid(_manifest.Id);

        return this;
    }

    /// <summary>
    /// Sets the content type and target game for the manifest.
    /// </summary>
    /// <param name="contentType">Content type.</param>
    /// <param name="targetGame">Target game.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder WithContentType(ContentType contentType, GameType targetGame)
    {
        _manifest.ContentType = contentType;
        _manifest.TargetGame = targetGame;
        _logger.LogDebug("Set content type: {ContentType}, Target game: {TargetGame}", contentType, targetGame);
        return this;
    }

    /// <summary>
    /// Sets the publisher information for the manifest.
    /// </summary>
    /// <param name="name">Publisher name.</param>
    /// <param name="website">Publisher website.</param>
    /// <param name="supportUrl">Support URL.</param>
    /// <param name="contactEmail">Contact email.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder WithPublisher(
        string name,
        string website = "",
        string supportUrl = "",
        string contactEmail = "")
    {
        _manifest.Publisher = new PublisherInfo
        {
            Name = name,
            Website = website,
            SupportUrl = supportUrl,
            ContactEmail = contactEmail,
        };
        _logger.LogDebug("Set publisher: {PublisherName}", name);
        return this;
    }

    /// <summary>
    /// Sets the metadata for the manifest.
    /// </summary>
    /// <param name="description">Description.</param>
    /// <param name="tags">Tags.</param>
    /// <param name="iconUrl">Icon URL.</param>
    /// <param name="screenshotUrls">Screenshot URLs.</param>
    /// <param name="changelogUrl">Changelog URL.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder WithMetadata(
        string description,
        List<string>? tags = null,
        string iconUrl = "",
        List<string>? screenshotUrls = null,
        string changelogUrl = "")
    {
        _manifest.Metadata = new ContentMetadata
        {
            Description = description,
            Tags = tags ?? new List<string>(),
            IconUrl = iconUrl,
            ScreenshotUrls = screenshotUrls ?? new List<string>(),
            ChangelogUrl = changelogUrl,
            ReleaseDate = DateTime.UtcNow,
        };
        _logger.LogDebug("Set metadata with description length: {DescriptionLength}", description.Length);
        return this;
    }

    /// <summary>
    /// Adds a dependency to the manifest.
    /// </summary>
    /// <param name="id">Dependency ID.</param>
    /// <param name="name">Dependency name.</param>
    /// <param name="dependencyType">Dependency type.</param>
    /// <param name="installBehavior">Install behavior (single source of truth for required/optional).</param>
    /// <param name="minVersion">Minimum version.</param>
    /// <param name="maxVersion">Maximum version.</param>
    /// <param name="compatibleVersions">List of compatible versions.</param>
    /// <param name="isExclusive">Is exclusive.</param>
    /// <param name="conflictsWith">Conflicting dependency IDs.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder AddDependency(
        ManifestId id,
        string name,
        ContentType dependencyType,
        DependencyInstallBehavior installBehavior,
        string minVersion = "",
        string maxVersion = "",
        List<string>? compatibleVersions = null,
        bool isExclusive = false,
        List<ManifestId>? conflictsWith = null)
    {
        var dependency = new ContentDependency
        {
            Id = id,
            Name = name,
            DependencyType = dependencyType,
            MinVersion = minVersion,
            MaxVersion = maxVersion,
            CompatibleVersions = compatibleVersions ?? new List<string>(),
            IsExclusive = isExclusive,
            ConflictsWith = conflictsWith ?? new List<ManifestId>(),
            InstallBehavior = installBehavior,
        };
        _manifest.Dependencies.Add(dependency);
        _logger.LogDebug("Added dependency: {DependencyId} (InstallBehavior: {InstallBehavior}, Exclusive: {IsExclusive})", id, installBehavior, isExclusive);
        return this;
    }

    /// <summary>
    /// Adds a content reference for cross-publisher linking.
    /// </summary>
    /// <param name="contentId">Content ID.</param>
    /// <param name="publisherId">Publisher ID.</param>
    /// <param name="contentType">Content type.</param>
    /// <param name="minVersion">Minimum version.</param>
    /// <param name="maxVersion">Maximum version.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder AddContentReference(
        ManifestId contentId,
        string publisherId,
        ContentType contentType,
        string minVersion = "",
        string maxVersion = "")
    {
        var reference = new ContentReference
        {
            ContentId = contentId,
            PublisherId = publisherId,
            ContentType = contentType,
            MinVersion = minVersion,
            MaxVersion = maxVersion,
        };

        _manifest.ContentReferences.Add(reference);
        _logger.LogDebug(
            "Added content reference: {ContentId} from publisher {PublisherId}",
            contentId,
            publisherId);
        return this;
    }

    /// <summary>
    /// Adds files from a directory to the manifest.
    /// </summary>
    /// <param name="sourceDirectory">Source directory.</param>
    /// <param name="sourceType">Source type.</param>
    /// <param name="fileFilter">File filter.</param>
    /// <param name="isExecutable">Is executable.</param>
    /// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    public async Task<IContentManifestBuilder> AddFilesFromDirectoryAsync(
        string sourceDirectory,
        ContentSourceType sourceType = ContentSourceType.ContentAddressable,
        string fileFilter = "*",
        bool isExecutable = false)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            _logger.LogWarning("Source directory does not exist: {Directory}", sourceDirectory);
            return this;
        }

        var searchPattern = fileFilter == "*" ? "*.*" : fileFilter;
        var files = Directory.EnumerateFiles(sourceDirectory, searchPattern, SearchOption.AllDirectories);

        foreach (var filePath in files)
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
            var fileInfo = new FileInfo(filePath);
            var hash = await _hashProvider.ComputeFileHashAsync(filePath);

            var manifestFile = new ManifestFile
            {
                RelativePath = relativePath,
                Size = fileInfo.Length,
                Hash = hash,
                SourceType = sourceType,
                IsExecutable = isExecutable || IsExecutableFile(filePath),
                Permissions = new FilePermissions
                {
                    IsReadOnly = fileInfo.IsReadOnly,
                    UnixPermissions = isExecutable ? "755" : "644",
                },
            };

            _manifest.Files.Add(manifestFile);
        }

        _logger.LogInformation("Added {FileCount} files from directory: {Directory}", _manifest.Files.Count, sourceDirectory);
        return this;
    }

    /// <summary>
    /// Adds a local file from the filesystem.
    /// </summary>
    /// <param name="relativePath">The relative path of the file in the workspace (destination).</param>
    /// <param name="sourcePath">The source path of the file on the local filesystem.</param>
    /// <param name="sourceType">How this file should be handled.</param>
    /// <param name="isExecutable">Whether the file is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    public async Task<IContentManifestBuilder> AddLocalFileAsync(
        string relativePath,
        string sourcePath,
        ContentSourceType sourceType = ContentSourceType.ContentAddressable,
        bool isExecutable = false,
        FilePermissions? permissions = null)
    {
        return await AddFileAsync(relativePath, sourcePath, sourceType, string.Empty, isExecutable, permissions);
    }

    /// <summary>
    /// Adds a remote file to be downloaded.
    /// </summary>
    /// <param name="relativePath">The relative path of the file in the workspace (destination).</param>
    /// <param name="downloadUrl">Download URL for the remote file.</param>
    /// <param name="sourceType">How this file should be handled.</param>
    /// <param name="isExecutable">Whether the file is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    public async Task<IContentManifestBuilder> AddRemoteFileAsync(
        string relativePath,
        string downloadUrl,
        ContentSourceType sourceType = ContentSourceType.ContentAddressable,
        bool isExecutable = false,
        FilePermissions? permissions = null)
    {
        return await AddFileAsync(relativePath, string.Empty, sourceType, downloadUrl, isExecutable, permissions);
    }

    /// <summary>
    /// Adds a game installation file from the detected game installation.
    /// </summary>
    /// <param name="relativePath">The relative path of the file in the workspace (destination).</param>
    /// <param name="sourcePath">The source path of the file in the game installation.</param>
    /// <param name="isExecutable">Whether the file is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    public async Task<IContentManifestBuilder> AddGameInstallationFileAsync(
        string relativePath,
        string sourcePath,
        bool isExecutable = false,
        FilePermissions? permissions = null)
    {
        if (string.IsNullOrEmpty(sourcePath))
        {
            throw new ArgumentException("sourcePath cannot be null or empty for game installation files.", nameof(sourcePath));
        }

        return await AddFileAsync(relativePath, sourcePath, ContentSourceType.GameInstallation, string.Empty, isExecutable, permissions);
    }

    /// <summary>
    /// Adds a content-addressable file from the CAS system.
    /// </summary>
    /// <param name="relativePath">The relative path of the file in the workspace (destination).</param>
    /// <param name="hash">The content hash for CAS lookup.</param>
    /// <param name="size">The expected file size.</param>
    /// <param name="isExecutable">Whether the file is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    public Task<IContentManifestBuilder> AddContentAddressableFileAsync(
        string relativePath,
        string hash,
        long size,
        bool isExecutable = false,
        FilePermissions? permissions = null)
    {
        if (string.IsNullOrEmpty(hash))
        {
            throw new ArgumentException("hash cannot be null or empty for content-addressable files.", nameof(hash));
        }

        var manifestFile = new ManifestFile
        {
            RelativePath = relativePath,
            SourceType = ContentSourceType.ContentAddressable,
            IsExecutable = isExecutable,
            Hash = hash,
            Size = size,
            Permissions = permissions ?? new FilePermissions { UnixPermissions = isExecutable ? "755" : "644", },
        };

        _manifest.Files.Add(manifestFile);
        _logger.LogDebug("Added content-addressable file: {RelativePath} (Hash: {Hash})", relativePath, hash);
        return Task.FromResult(this as IContentManifestBuilder);
    }

    /// <summary>
    /// Adds a file from an extracted package/archive.
    /// </summary>
    /// <param name="relativePath">The relative path of the file in the workspace (destination).</param>
    /// <param name="packagePath">The path to the package file.</param>
    /// <param name="internalPath">The path within the package.</param>
    /// <param name="isExecutable">Whether the file is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    public async Task<IContentManifestBuilder> AddExtractedPackageFileAsync(
        string relativePath,
        string packagePath,
        string internalPath,
        bool isExecutable = false,
        FilePermissions? permissions = null)
    {
        if (string.IsNullOrEmpty(packagePath))
        {
            throw new ArgumentException("packagePath cannot be null or empty for extracted package files.", nameof(packagePath));
        }

        if (string.IsNullOrEmpty(internalPath))
        {
            throw new ArgumentException("internalPath cannot be null or empty for extracted package files.", nameof(internalPath));
        }

        var manifestFile = new ManifestFile
        {
            RelativePath = relativePath,
            SourcePath = packagePath,
            SourceType = ContentSourceType.ExtractedPackage,
            IsExecutable = isExecutable,
            Permissions = permissions ?? new FilePermissions { UnixPermissions = isExecutable ? "755" : "644", },
            PackageInfo = new ExtractionConfiguration
            {
                ExtractionPath = internalPath,
            },
        };

        // Try to compute hash if package file exists
        if (File.Exists(packagePath))
        {
            manifestFile.Hash = await _hashProvider.ComputeFileHashAsync(packagePath);
            var fileInfo = new FileInfo(packagePath);
            manifestFile.Size = fileInfo.Length;
        }

        _manifest.Files.Add(manifestFile);
        _logger.LogDebug(
            "Added extracted package file: {RelativePath} from {PackagePath}:{InternalPath}",
            relativePath,
            packagePath,
            internalPath);
        return this;
    }

    /// <inheritdoc/>
    public IContentManifestBuilder AddFile(ManifestFile file)
    {
        _manifest.Files.Add(file);
        _logger.LogDebug("Added pre-existing file: {RelativePath} (Source: {SourceType})", file.RelativePath, file.SourceType);
        return this;
    }

    /// <summary>
    /// Adds required directories to the manifest.
    /// </summary>
    /// <param name="directories">Directories.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder AddRequiredDirectories(params string[] directories)
    {
        foreach (var directory in directories)
        {
            if (!_manifest.RequiredDirectories.Contains(directory))
            {
                _manifest.RequiredDirectories.Add(directory);
            }
        }

        _logger.LogDebug("Added {DirectoryCount} required directories", directories.Length);
        return this;
    }

    /// <summary>
    /// Sets installation instructions for the manifest.
    /// </summary>
    /// <param name="workspaceStrategy">Workspace strategy.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder WithInstallationInstructions(
        WorkspaceStrategy workspaceStrategy = WorkspaceStrategy.HybridCopySymlink)
    {
        _manifest.InstallationInstructions = new InstallationInstructions
        {
            WorkspaceStrategy = workspaceStrategy,
        };
        _logger.LogDebug("Set workspace strategy: {Strategy}", workspaceStrategy);
        return this;
    }

    /// <summary>
    /// Adds a pre-installation step to the manifest.
    /// </summary>
    /// <param name="name">Step name.</param>
    /// <param name="command">Command.</param>
    /// <param name="arguments">Arguments.</param>
    /// <param name="workingDirectory">Working directory.</param>
    /// <param name="requiresElevation">Requires elevation.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder AddPreInstallStep(
        string name,
        string command,
        List<string>? arguments = null,
        string workingDirectory = "",
        bool requiresElevation = false)
    {
        var step = new InstallationStep
        {
            Name = name,
            Command = command,
            Arguments = arguments ?? new List<string>(),
            WorkingDirectory = workingDirectory,
            RequiresElevation = requiresElevation,
        };
        _manifest.InstallationInstructions.PreInstallSteps.Add(step);
        _logger.LogDebug("Added pre-install step: {StepName}", name);
        return this;
    }

    /// <summary>
    /// Adds a post-installation step to the manifest.
    /// </summary>
    /// <param name="name">Step name.</param>
    /// <param name="command">Command.</param>
    /// <param name="arguments">Arguments.</param>
    /// <param name="workingDirectory">Working directory.</param>
    /// <param name="requiresElevation">Requires elevation.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder AddPostInstallStep(
        string name,
        string command,
        List<string>? arguments = null,
        string workingDirectory = "",
        bool requiresElevation = false)
    {
        var step = new InstallationStep
        {
            Name = name,
            Command = command,
            Arguments = arguments ?? new List<string>(),
            WorkingDirectory = workingDirectory,
            RequiresElevation = requiresElevation,
        };
        _manifest.InstallationInstructions.PostInstallSteps.Add(step);
        _logger.LogDebug("Added post-install step: {StepName}", name);
        return this;
    }

    /// <inheritdoc/>
    public IContentManifestBuilder AddPatchFile(string targetRelativePath, string patchSourceFile)
    {
        var manifestFile = new ManifestFile
        {
            RelativePath = targetRelativePath,
            PatchSourceFile = patchSourceFile,
            SourceType = ContentSourceType.PatchFile,
        };

        _manifest.Files.Add(manifestFile);
        _logger.LogDebug("Added patch for {TargetFile} with source {PatchFile}", targetRelativePath, patchSourceFile);
        return this;
    }

    /// <summary>
    /// Builds and returns the manifest.
    /// </summary>
    /// <returns>The built manifest.</returns>
    public ContentManifest Build()
    {
        _logger.LogInformation(
            "Built manifest for '{ContentName}' with {FileCount} files and {DependencyCount} dependencies",
            _manifest.Name,
            _manifest.Files.Count,
            _manifest.Dependencies.Count);
        return _manifest;
    }

    private static bool IsExecutableFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return (extension == ".exe" || extension == ".dll" || extension == ".so" || extension == string.Empty) && File.Exists(filePath);
    }

    /// <summary>
    /// Adds a file to the manifest.
    /// </summary>
    /// <param name="relativePath">Relative path in workspace.</param>
    /// <param name="sourcePath">Source path for hash computation.</param>
    /// <param name="sourceType">Source type.</param>
    /// <param name="downloadUrl">Download URL.</param>
    /// <param name="isExecutable">Is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <returns>The builder instance.</returns>
    private async Task<IContentManifestBuilder> AddFileAsync(
        string relativePath,
        string sourcePath = "",
        ContentSourceType sourceType = ContentSourceType.ContentAddressable,
        string downloadUrl = "",
        bool isExecutable = false,
        FilePermissions? permissions = null)
    {
        var manifestFile = new ManifestFile
        {
            RelativePath = relativePath,
            SourcePath = !string.IsNullOrEmpty(sourcePath) ? sourcePath : null,
            SourceType = sourceType,
            IsExecutable = isExecutable,
            DownloadUrl = downloadUrl,
            Permissions = permissions ?? new FilePermissions { UnixPermissions = isExecutable ? "755" : "644", },
        };

        if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath))
        {
            var fileInfo = new FileInfo(sourcePath);
            manifestFile.Size = fileInfo.Length;
            manifestFile.Hash = await _hashProvider.ComputeFileHashAsync(sourcePath);
        }

        _manifest.Files.Add(manifestFile);
        _logger.LogDebug("Added file: {RelativePath} (Source: {SourceType})", relativePath, sourceType);
        return this;
    }
}
