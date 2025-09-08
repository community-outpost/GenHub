using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Manifest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentDeliverers;

/// <summary>
/// Delivers local file system content.
/// Pure delivery - no discovery logic.
/// </summary>
public class FileSystemDeliverer(ILogger<FileSystemDeliverer> logger, IConfigurationProviderService configProvider, IFileHashProvider hashProvider) : IContentDeliverer
{
    private readonly ILogger<FileSystemDeliverer> _logger = logger;
    private readonly IConfigurationProviderService _configProvider = configProvider;
    private readonly IFileHashProvider _hashProvider = hashProvider;

    /// <inheritdoc />
    public string SourceName => "Local File System Deliverer";

    /// <inheritdoc />
    public string Description => "Delivers content from local file system";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.LocalFileDelivery;

    /// <inheritdoc />
    public bool CanDeliver(ContentManifest manifest)
    {
        if (manifest?.Files == null)
        {
            return false;
        }

        return manifest.Files.All(f =>
            f.SourceType == ContentSourceType.ContentAddressable);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> DeliverContentAsync(
        ContentManifest packageManifest,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deliveredFiles = new List<ManifestFile>();
            var totalFiles = packageManifest.Files.Count;
            var processedFiles = 0;

            foreach (var file in packageManifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourcePath = ResolveLocalPath(file, packageManifest.Id);
                if (!File.Exists(sourcePath))
                {
                    return OperationResult<ContentManifest>.CreateFailure(
                        $"Local file not found: {sourcePath}");
                }

                // Report progress
                progress?.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.Delivering,
                    ProgressPercentage = (double)processedFiles / totalFiles * 100,
                    CurrentOperation = $"Processing {file.RelativePath}",
                    CurrentFile = file.RelativePath,
                    FilesProcessed = processedFiles,
                    TotalFiles = totalFiles,
                });

                deliveredFiles.Add(new ManifestFile
                {
                    RelativePath = file.RelativePath,
                    Size = new FileInfo(sourcePath).Length,
                    Hash = file.Hash,
                    SourceType = ContentSourceType.ContentAddressable,
                    IsRequired = file.IsRequired,
                    SourcePath = sourcePath,
                });

                processedFiles++;
            }

            // Use ContentManifestBuilder to create delivered manifest
            var manifestBuilder = new ContentManifestBuilder(
                LoggerFactory.Create(builder => { }).CreateLogger<ContentManifestBuilder>(),
                _hashProvider,
                null!);

            int manifestVersionInt;
            if (!int.TryParse(packageManifest.Version, out manifestVersionInt))
            {
                _logger.LogError("Invalid manifest version format: {Version}", packageManifest.Version);
                return OperationResult<ContentManifest>.CreateFailure("Invalid manifest version format");
            }

            manifestBuilder
                .WithBasicInfo(packageManifest.Id, packageManifest.Name, manifestVersionInt)
                .WithContentType(packageManifest.ContentType, packageManifest.TargetGame)
                .WithPublisher(
                    packageManifest.Publisher?.Name ?? string.Empty,
                    packageManifest.Publisher?.Website ?? string.Empty,
                    packageManifest.Publisher?.SupportUrl ?? string.Empty,
                    packageManifest.Publisher?.ContactEmail ?? string.Empty)
                .WithMetadata(
                    packageManifest.Metadata?.Description ?? string.Empty,
                    packageManifest.Metadata?.Tags,
                    packageManifest.Metadata?.IconUrl ?? string.Empty,
                    packageManifest.Metadata?.ScreenshotUrls,
                    packageManifest.Metadata?.ChangelogUrl ?? string.Empty);

            // Add dependencies
            foreach (var dep in packageManifest.Dependencies)
            {
                manifestBuilder.AddDependency(
                    dep.Id,
                    dep.Name,
                    dep.DependencyType,
                    dep.InstallBehavior,
                    dep.MinVersion ?? string.Empty,
                    dep.MaxVersion ?? string.Empty,
                    dep.CompatibleVersions,
                    dep.IsExclusive,
                    dep.ConflictsWith);
            }

            // Add content references
            foreach (var reference in packageManifest.ContentReferences)
            {
                manifestBuilder.AddContentReference(
                    reference.ContentId,
                    reference.PublisherId ?? string.Empty,
                    reference.ContentType,
                    reference.MinVersion ?? string.Empty,
                    reference.MaxVersion ?? string.Empty);
            }

            // Add delivered files to the manifest
            foreach (var file in deliveredFiles)
            {
                await manifestBuilder.AddContentAddressableFileAsync(
                    file.RelativePath,
                    file.Hash ?? string.Empty,
                    file.Size,
                    isExecutable: file.IsExecutable,
                    permissions: file.Permissions);
            }

            // Add required directories
            manifestBuilder.AddRequiredDirectories(packageManifest.RequiredDirectories.ToArray());

            // Add installation instructions if present
            if (packageManifest.InstallationInstructions != null)
            {
                manifestBuilder.WithInstallationInstructions(packageManifest.InstallationInstructions.WorkspaceStrategy);
            }

            var deliveredManifest = manifestBuilder.Build();

            return OperationResult<ContentManifest>.CreateSuccess(deliveredManifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver local content for manifest {ManifestId}", packageManifest.Id);
            return OperationResult<ContentManifest>.CreateFailure($"Content delivery failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> ValidateContentAsync(
        ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var file in manifest.Files.Where(f => f.IsRequired))
            {
                var sourcePath = ResolveLocalPath(file, manifest.Id);
                if (!File.Exists(sourcePath))
                {
                    return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
                }
            }

            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed for local content manifest {ManifestId}", manifest.Id);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Resolves the local file path for a manifest file.
    /// Priority: SourcePath > DownloadUrl > RelativePath.
    /// Throws if no valid path is found.
    /// </summary>
    /// <param name="file">The manifest file.</param>
    /// <param name="manifestId">The manifest ID.</param>
    /// <returns>The resolved local file path.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no valid path is found.</exception>
    private string ResolveLocalPath(ManifestFile file, string manifestId)
    {
        // Priority: SourcePath > DownloadUrl > RelativePath
        var basePath = _configProvider.GetWorkspacePath();
        var localPath = file.SourcePath ?? file.DownloadUrl ?? file.RelativePath;

        if (string.IsNullOrEmpty(localPath))
        {
            throw new InvalidOperationException($"No valid path found for file in manifest {manifestId}");
        }

        if (!Path.IsPathRooted(localPath) && !string.IsNullOrEmpty(basePath))
        {
            return Path.Combine(basePath, localPath);
        }

        return localPath;
    }
}
