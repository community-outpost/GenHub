using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// Fluent builder for creating comprehensive game manifests.
/// </summary>
public partial class ContentManifestBuilder(
    ILogger<ContentManifestBuilder> logger,
    IFileHashProvider hashProvider,
    IManifestIdService manifestIdService,
    IDownloadService downloadService,
    IConfigurationProviderService configurationProvider) : IContentManifestBuilder
{
    private readonly ContentManifest _manifest = new();
    private readonly IFileHashProvider _hashProvider = hashProvider;
    private readonly IManifestIdService _manifestIdService = manifestIdService;
    private readonly IDownloadService _downloadService = downloadService;
    private readonly IConfigurationProviderService _configurationProvider = configurationProvider;

    // Temporary storage for ID generation
    private string? _publisherId;
    private string? _contentName;
    private int? _manifestVersion;

    /// <summary>
    /// Sets the basic information for the manifest for game installations (used internally by GenHub).
    /// This overload is specifically for generating manifests for EA/Steam game installations.
    /// </summary>
    /// <param name="installType">The game installation type (EA, Steam, etc.).</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder WithBasicInfo(GameInstallationType installType, GameType gameType, string? manifestVersion)
    {
        // Build a temporary GameInstallation to generate the ID
        // Note: We use "dummy" as a placeholder path for Zero Hour because ManifestIdGenerator
        // only needs the installation type and game type, not the actual path.
        // The "dummy" string is arbitrary and doesn't correspond to a real directory;
        // it's only used to satisfy SetPaths() parameter requirements for consistent object initialization.
        var tempInstallation = new GameInstallation(string.Empty, installType);
        tempInstallation.SetPaths(null, gameType == GameType.ZeroHour ? "dummy" : null);

        // Use ManifestIdService for consistent ID generation with ResultBase pattern
        logger.LogDebug("DEBUG: Calling GenerateGameInstallationId with {InstallationType}, {GameType}, {ManifestVersion}", tempInstallation.InstallationType, gameType, manifestVersion ?? "null");
        var idResult = _manifestIdService.GenerateGameInstallationId(tempInstallation, gameType, manifestVersion);
        if (idResult.Success)
        {
            _manifest.Id = idResult.Data;
        }
        else
        {
            logger.LogWarning("Failed to generate game installation manifest ID: {Error}. Using fallback.", idResult.FirstError);

            // Fallback to direct generation if service fails
            _manifest.Id = ManifestId.Create(
                ManifestIdGenerator.GenerateGameInstallationId(tempInstallation, gameType, manifestVersion));
        }

        _manifest.Name = gameType.ToString().ToLowerInvariant();
        _manifest.Version = manifestVersion ?? "0";

        logger.LogDebug(
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
    /// Sets the basic information for the manifest for game installations (used internally by GenHub).
    /// This overload is specifically for generating manifests for EA/Steam game installations.
    /// </summary>
    /// <param name="installType">The game installation type (EA, Steam, etc.).</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder WithBasicInfo(GameInstallationType installType, GameType gameType, int manifestVersion)
    {
        return WithBasicInfo(installType, gameType, manifestVersion.ToString());
    }

    /// <summary>
    /// Sets the basic information for the manifest for publisher content (used by external publishers).
    /// This overload is for developers, modders, mappers, and other publishers creating content manifests.
    /// </summary>
    /// <param name="publisherId">Publisher identifier.</param>
    /// <param name="contentName">Content display name.</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder WithBasicInfo(string publisherId, string contentName, string? manifestVersion)
    {
        // Store basic info for ID generation when ContentType is set
        _publisherId = publisherId;
        _contentName = contentName;
        _manifestVersion = int.TryParse(manifestVersion, out var v) ? v : 0;

        _manifest.Name = contentName;
        _manifest.Version = manifestVersion ?? "0";
        _manifest.ContentType = ContentType.Mod;

        logger.LogDebug(
            "Set basic info for publisher content: Name={Name}, ManifestVersion={ManifestVersion}, Publisher={Publisher}",
            _manifest.Name,
            _manifest.Version,
            publisherId);

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
        return WithBasicInfo(publisherId, contentName, manifestVersion.ToString());
    }

    /// <summary>
    /// Sets the basic information for the manifest with publisher info.
    /// </summary>
    /// <param name="publisher">Publisher information.</param>
    /// <param name="contentName">Content display name.</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder WithBasicInfo(PublisherInfo publisher, string contentName, int manifestVersion)
    {
        var publisherId = !string.IsNullOrEmpty(publisher.PublisherType)
            ? publisher.PublisherType
            : NormalizePublisherName(publisher.Name);

        return WithBasicInfo(publisherId, contentName, manifestVersion.ToString())
            .WithPublisher(publisher.Name, publisher.Website ?? string.Empty, publisher.SupportUrl ?? string.Empty, publisher.ContactEmail ?? string.Empty);
    }

    /// <summary>
    /// Sets the basic information for the manifest with publisher info.
    /// </summary>
    /// <param name="publisher">Publisher information.</param>
    /// <param name="contentName">Content display name.</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder WithBasicInfo(PublisherInfo publisher, string contentName, string? manifestVersion)
    {
        var publisherId = !string.IsNullOrEmpty(publisher.PublisherType)
            ? publisher.PublisherType
            : NormalizePublisherName(publisher.Name);

        return WithBasicInfo(publisherId, contentName, manifestVersion)
            .WithPublisher(publisher.Name, publisher.Website ?? string.Empty, publisher.SupportUrl ?? string.Empty, publisher.ContactEmail ?? string.Empty);
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

        // Generate ID now that we have all required information
        if (_publisherId != null && _contentName != null && _manifestVersion.HasValue)
        {
            logger.LogDebug(
                "Generating manifest ID with: Publisher={Publisher}, ContentType={ContentType}, ContentName={ContentName}, Version={Version}",
                _publisherId,
                contentType,
                _contentName,
                _manifestVersion.Value);

            var idResult = _manifestIdService.GeneratePublisherContentId(_publisherId, contentType, _contentName, _manifestVersion.Value);
            if (idResult.Success)
            {
                _manifest.Id = idResult.Data;
                logger.LogDebug("Generated manifest ID (from service): {ManifestId}", _manifest.Id);
            }
            else
            {
                logger.LogWarning("Failed to generate publisher content manifest ID: {Error}. Using fallback.", idResult.FirstError);

                // Fallback to direct generation if service fails
                _manifest.Id = ManifestId.Create(
                    ManifestIdGenerator.GeneratePublisherContentId(_publisherId, contentType, _contentName, _manifestVersion.Value));
                logger.LogDebug("Generated manifest ID (fallback): {ManifestId}", _manifest.Id);
            }

            // Ensure the generated ID conforms to the project's validation rules.
            ManifestIdValidator.EnsureValid(_manifest.Id);

            logger.LogDebug("Generated ID for publisher content: {Id}", _manifest.Id);

            // Clear the stored values to prevent regeneration in Build()
            _publisherId = null;
            _contentName = null;
            _manifestVersion = null;
        }

        logger.LogDebug("Set content type: {ContentType}, Target game: {TargetGame}", contentType, targetGame);
        return this;
    }

    /// <summary>
    /// Sets the publisher information for the manifest.
    /// </summary>
    /// <param name="name">Publisher name.</param>
    /// <param name="website">Publisher website.</param>
    /// <param name="supportUrl">Support URL.</param>
    /// <param name="contactEmail">Contact email.</param>
    /// <param name="publisherType">Publisher type identifier for dependency validation.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder WithPublisher(
        string name,
        string website = "",
        string supportUrl = "",
        string contactEmail = "",
        string publisherType = "")
    {
        _manifest.Publisher = new PublisherInfo
        {
            Name = name,
            Website = website,
            SupportUrl = supportUrl,
            ContactEmail = contactEmail,
            PublisherType = publisherType,
        };
        logger.LogDebug("Set publisher: {PublisherName} (Type: {PublisherType})", name, publisherType);
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
            Tags = tags ?? [],
            IconUrl = iconUrl,
            ScreenshotUrls = screenshotUrls ?? [],
            ChangelogUrl = changelogUrl,
            ReleaseDate = DateTime.UtcNow,
        };
        logger.LogDebug("Set metadata with description length: {DescriptionLength}", description.Length);
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
            CompatibleVersions = compatibleVersions ?? [],
            IsExclusive = isExclusive,
            ConflictsWith = conflictsWith ?? [],
            InstallBehavior = installBehavior,
        };
        _manifest.Dependencies.Add(dependency);
        logger.LogDebug("Added dependency: {DependencyId} (InstallBehavior: {InstallBehavior}, Exclusive: {IsExclusive})", id, installBehavior, isExclusive);
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
        logger.LogDebug(
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
            logger.LogWarning("Source directory does not exist: {Directory}", sourceDirectory);
            return this;
        }

        // TODO: Implement CSV-based authority system for GameInstallation manifests
        // Currently, scanning game installations is slow because we hash every file.
        // Future implementation will use a CSV file from GitHub as the source of truth,
        // specific to each installation type (EA/Steam) and language.
        // For now, we skip hashing for GameInstallation files to improve performance.
        var shouldComputeHash = sourceType != ContentSourceType.GameInstallation;

        logger.LogDebug("Adding files from directory: {Directory} (ComputeHash: {ComputeHash})", sourceDirectory, shouldComputeHash);
        var searchPattern = fileFilter == "*" ? "*.*" : fileFilter;
        var files = Directory.EnumerateFiles(sourceDirectory, searchPattern, SearchOption.AllDirectories);

        foreach (var filePath in files)
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
            var fileInfo = new FileInfo(filePath);

            // Skip hash computation for GameInstallation files to improve performance
            // Hash will be null for these files, which is acceptable since we'll use CSV authority in the future
            string? hash = null;
            if (shouldComputeHash)
            {
                hash = await _hashProvider.ComputeFileHashAsync(filePath);
            }

            var installTarget = DetermineInstallTarget(relativePath);

            var manifestFile = new ManifestFile
            {
                RelativePath = relativePath,
                Size = fileInfo.Length,
                Hash = hash ?? string.Empty, // Empty for GameInstallation files (CSV authority planned)
                SourceType = sourceType,
                SourcePath = sourceDirectory, // Set the source directory path for workspace preparation
                InstallTarget = installTarget,
                IsExecutable = isExecutable || IsExecutableFile(filePath),
                Permissions = new FilePermissions
                {
                    IsReadOnly = fileInfo.IsReadOnly,
                    UnixPermissions = isExecutable ? "755" : "644",
                },
            };

            _manifest.Files.Add(manifestFile);
        }

        logger.LogInformation("Added {FileCount} files from directory: {Directory} (Hashed: {Hashed})", _manifest.Files.Count, sourceDirectory, shouldComputeHash);
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
    /// Downloads a file from a remote URL, stores it in CAS, and adds it to the manifest.
    /// </summary>
    /// <param name="relativePath">The relative path of the file in the workspace (destination).</param>
    /// <param name="downloadUrl">Download URL for the remote file.</param>
    /// <param name="sourceType">How this file should be handled (typically ContentAddressable).</param>
    /// <param name="isExecutable">Whether the file is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <param name="refererUrl">Optional referer URL to use for the download request.</param>
    /// <param name="userAgent">Optional user agent to use for the download request.</param>
/// <returns>A task that yields the <see cref="IContentManifestBuilder"/> instance for chaining upon completion.</returns>
    public Task<IContentManifestBuilder> AddDownloadedFileAsync(
        string relativePath,
        string downloadUrl,
        ContentSourceType sourceType = ContentSourceType.ContentAddressable,
        bool isExecutable = false,
        FilePermissions? permissions = null,
        string? refererUrl = null,
        string? userAgent = null)
    {
        throw new NotImplementedException();
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
        logger.LogDebug("Added content-addressable file: {RelativePath} (Hash: {Hash})", relativePath, hash);
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
        logger.LogDebug(
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
        logger.LogDebug("Added pre-existing file: {RelativePath} (Source: {SourceType})", file.RelativePath, file.SourceType);
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

        logger.LogDebug("Added {DirectoryCount} required directories", directories.Length);
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
        logger.LogDebug("Set workspace strategy: {Strategy}", workspaceStrategy);
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
            Arguments = arguments ?? [],
            WorkingDirectory = workingDirectory,
            RequiresElevation = requiresElevation,
        };
        _manifest.InstallationInstructions.PreInstallSteps.Add(step);
        logger.LogDebug("Added pre-install step: {StepName}", name);
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
            Arguments = arguments ?? [],
            WorkingDirectory = workingDirectory,
            RequiresElevation = requiresElevation,
        };
        _manifest.InstallationInstructions.PostInstallSteps.Add(step);
        logger.LogDebug("Added post-install step: {StepName}", name);
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
        logger.LogDebug("Added patch for {TargetFile} with source {PatchFile}", targetRelativePath, patchSourceFile);
        return this;
    }

    /// <summary>
    /// Builds and returns the manifest.
    /// </summary>
    /// <returns>The built manifest.</returns>
    public ContentManifest Build()
    {
        // Ensure ID is generated if not already done
        if (string.IsNullOrEmpty(_manifest.Id.Value) && _publisherId != null && _contentName != null && _manifestVersion.HasValue)
        {
            var idResult = _manifestIdService.GeneratePublisherContentId(_publisherId, _manifest.ContentType, _contentName, _manifestVersion.Value);
            if (idResult.Success)
            {
                _manifest.Id = idResult.Data;
            }
            else
            {
                logger.LogWarning("Failed to generate publisher content manifest ID: {Error}. Using fallback.", idResult.FirstError);

                // Fallback to direct generation if service fails
                _manifest.Id = ManifestId.Create(
                    ManifestIdGenerator.GeneratePublisherContentId(_publisherId, _manifest.ContentType, _contentName, _manifestVersion.Value));
            }

            // Ensure the generated ID conforms to the project's validation rules.
            ManifestIdValidator.EnsureValid(_manifest.Id);

            logger.LogDebug("Generated ID during build: {Id}", _manifest.Id);
        }

        logger.LogInformation(
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

    private static string NormalizePublisherName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "unknown";

        // Lowercase and remove any non-alphanumeric characters to produce a
        // single-token publisher id (no dots). This avoids creating extra
        // dot-separated segments when the ID is constructed.
        var lower = input.ToLowerInvariant().Trim();
        var cleaned = PublisherIdRegex().Replace(lower, string.Empty);
        return string.IsNullOrEmpty(cleaned) ? "unknown" : cleaned;
    }

    [System.Text.RegularExpressions.GeneratedRegex("[^a-z0-9]")]
    private static partial System.Text.RegularExpressions.Regex PublisherIdRegex();

    /// <summary>
    /// Determines the installation target based on file extension or path.
    /// </summary>
    /// <param name="relativePath">The relative path of the file.</param>
    /// <returns>The determined installation target.</returns>
    private static ContentInstallTarget DetermineInstallTarget(string relativePath)
    {
        var extension = Path.GetExtension(relativePath).ToLowerInvariant();

        if (extension == ".map" ||
            (extension == ".tga" && relativePath.Contains("maps", StringComparison.OrdinalIgnoreCase)) ||
            (extension == ".wak" && relativePath.Contains("maps", StringComparison.OrdinalIgnoreCase)))
        {
            return ContentInstallTarget.UserMapsDirectory;
        }

        if (extension == ".rep")
        {
            return ContentInstallTarget.UserReplaysDirectory;
        }

        if (extension == ".bmp" && relativePath.Contains("screenshots", StringComparison.OrdinalIgnoreCase))
        {
            return ContentInstallTarget.UserScreenshotsDirectory;
        }

        return ContentInstallTarget.Workspace;
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
        var installTarget = DetermineInstallTarget(relativePath);

        var manifestFile = new ManifestFile
        {
            RelativePath = relativePath,
            SourcePath = !string.IsNullOrEmpty(sourcePath) ? sourcePath : null,
            SourceType = sourceType,
            IsExecutable = isExecutable,
            DownloadUrl = downloadUrl,
            InstallTarget = installTarget,
            Permissions = permissions ?? new FilePermissions { UnixPermissions = isExecutable ? "755" : "644", },
        };

        var shouldComputeHash = false;
        if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath))
        {
            var fileInfo = new FileInfo(sourcePath);
            manifestFile.Size = fileInfo.Length;

            // Always compute hash for executable files (critical for GameClient integrity validation)
            // For non-executable GameInstallation files, skip hash (CSV-based authority from GitHub planned)
            shouldComputeHash = isExecutable || sourceType != ContentSourceType.GameInstallation;
            if (shouldComputeHash)
            {
                manifestFile.Hash = await _hashProvider.ComputeFileHashAsync(sourcePath);
            }
        }

        // Check for duplicate relative paths before adding
        if (_manifest.Files.Any(f => f.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogWarning(
                "Skipping duplicate file: {RelativePath} (Source: {SourceType}). File already exists in manifest.",
                relativePath,
                sourceType);
            return this;
        }

        _manifest.Files.Add(manifestFile);
        logger.LogDebug("Added file: {RelativePath} (Source: {SourceType}, Hashed: {Hashed})", relativePath, sourceType, shouldComputeHash);
        return this;
    }
}
