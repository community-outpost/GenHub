using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// Fluent builder for creating comprehensive game manifests.
/// </summary>
public class ContentManifestBuilder(ILogger<ContentManifestBuilder> logger) : IContentManifestBuilder
{
    private readonly ILogger<ContentManifestBuilder> _logger = logger;
    private readonly GameManifest _manifest = new();

    /// <summary>
    /// Sets the basic information for the manifest.
    /// </summary>
    /// <param name="id">Manifest ID.</param>
    /// <param name="name">Manifest name.</param>
    /// <param name="version">Manifest version.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder WithBasicInfo(string id, string name, string version)
    {
        _manifest.Id = id;
        _manifest.Name = name;
        _manifest.Version = version;
        _logger.LogDebug("Set basic info: ID={Id}, Name={Name}, Version={Version}", id, name, version);
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
    /// <param name="minVersion">Minimum version.</param>
    /// <param name="maxVersion">Maximum version.</param>
    /// <param name="isRequired">Is required.</param>
    /// <param name="dependencyType">Dependency type.</param>
    /// <returns>The builder instance.</returns>
    public IContentManifestBuilder AddDependency(
        string id,
        string name,
        string minVersion = "",
        string maxVersion = "",
        bool isRequired = true,
        ContentType dependencyType = ContentType.BaseGame)
    {
        var dependency = new ContentDependency
        {
            Id = id,
            Name = name,
            MinVersion = minVersion,
            MaxVersion = maxVersion,
            IsRequired = isRequired,
            DependencyType = dependencyType,
        };
        _manifest.Dependencies.Add(dependency);
        _logger.LogDebug("Added dependency: {DependencyId} (Required: {IsRequired})", id, isRequired);
        return this;
    }

    /// <summary>
    /// Adds files from a directory to the manifest.
    /// </summary>
    /// <param name="sourceDirectory">Source directory.</param>
    /// <param name="sourceType">Source type.</param>
    /// <param name="fileFilter">File filter.</param>
    /// <param name="isExecutable">Is executable.</param>
    /// <returns>The builder instance.</returns>
    public async Task<IContentManifestBuilder> AddFilesFromDirectoryAsync(
        string sourceDirectory,
        ManifestFileSourceType sourceType = ManifestFileSourceType.CopyUnique,
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
            var hash = await ComputeSha256Async(filePath);

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
    /// Adds a file to the manifest.
    /// </summary>
    /// <param name="relativePath">Relative path.</param>
    /// <param name="sourceType">Source type.</param>
    /// <param name="downloadUrl">Download URL.</param>
    /// <param name="isExecutable">Is executable.</param>
    /// <param name="permissions">File permissions.</param>
    /// <returns>The builder instance.</returns>
    public async Task<IContentManifestBuilder> AddFileAsync(
        string relativePath,
        ManifestFileSourceType sourceType = ManifestFileSourceType.CopyUnique,
        string downloadUrl = "",
        bool isExecutable = false,
        FilePermissions? permissions = null)
    {
        var manifestFile = new ManifestFile
        {
            RelativePath = relativePath,
            SourceType = sourceType,
            IsExecutable = isExecutable,
            DownloadUrl = downloadUrl,
            Permissions = permissions ?? new FilePermissions { UnixPermissions = isExecutable ? "755" : "644", },
        };

        if (sourceType != ManifestFileSourceType.Download && File.Exists(relativePath))
        {
            var fileInfo = new FileInfo(relativePath);
            manifestFile.Size = fileInfo.Length;
            manifestFile.Hash = await ComputeSha256Async(relativePath);
        }

        _manifest.Files.Add(manifestFile);
        _logger.LogDebug("Added file: {RelativePath} (Source: {SourceType})", relativePath, sourceType);
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
        WorkspaceStrategy workspaceStrategy = WorkspaceStrategy.HybridSymlink)
    {
        _manifest.Installation = new InstallationInstructions
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
        _manifest.Installation.PreInstallSteps.Add(step);
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
        _manifest.Installation.PostInstallSteps.Add(step);
        _logger.LogDebug("Added post-install step: {StepName}", name);
        return this;
    }

    /// <summary>
    /// Builds and returns the manifest.
    /// </summary>
    /// <returns>The built manifest.</returns>
    public GameManifest Build()
    {
        _logger.LogInformation(
            "Built manifest for '{ContentName}' with {FileCount} files and {DependencyCount} dependencies",
            _manifest.Name,
            _manifest.Files.Count,
            _manifest.Dependencies.Count);
        return _manifest;
    }

    private static async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static bool IsExecutableFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return (extension == ".exe" || extension == ".dll" || extension == ".so" || extension == string.Empty) && File.Exists(filePath);
    }
}
