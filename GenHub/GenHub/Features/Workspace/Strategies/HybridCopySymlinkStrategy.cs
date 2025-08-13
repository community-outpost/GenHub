using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace.Strategies;

/// <summary>
/// Workspace strategy that copies essential files and creates symbolic links for others.
/// Balanced disk usage and compatibility.
/// </summary>
/// <remarks>
/// HybridCopySymlinkStrategy provides a balance between copying essential files and symlinking large files.
/// </remarks>
public sealed class HybridCopySymlinkStrategy : WorkspaceStrategyBase<HybridCopySymlinkStrategy>
{
    private const long LinkOverheadBytes = 1024L;

    /// <summary>
    /// Initializes a new instance of the <see cref="HybridCopySymlinkStrategy"/> class.
    /// </summary>
    /// <param name="fileOperations">Service for file operations.</param>
    /// <param name="logger">Logger instance for logging.</param>
    public HybridCopySymlinkStrategy(IFileOperationsService fileOperations, ILogger<HybridCopySymlinkStrategy> logger)
        : base(fileOperations, logger)
    {
    }

    /// <inheritdoc/>
    public override string Name => "Hybrid Copy-Symlink";

    /// <inheritdoc/>
    public override string Description => "Copies essential files (executables, configs, small files) and creates symlinks for large media files. Balanced disk usage and compatibility.";

    /// <inheritdoc/>
    public override bool RequiresAdminRights => OperatingSystem.IsWindows();

    /// <inheritdoc/>
    public override bool RequiresSameVolume => false;

    /// <inheritdoc/>
    public override bool CanHandle(WorkspaceConfiguration configuration)
    {
        return configuration.Strategy == WorkspaceStrategy.HybridCopySymlink;
    }

    /// <inheritdoc/>
    public override long EstimateDiskUsage(WorkspaceConfiguration configuration)
    {
        long totalUsage = 0;

        foreach (var manifest in configuration.Manifests)
        {
            foreach (var file in manifest.Files)
            {
                if (ShouldCopyFile(file))
                {
                    totalUsage += file.Size;
                }
                else
                {
                    totalUsage += LinkOverheadBytes;
                }
            }
        }

        return totalUsage;
    }

    /// <inheritdoc/>
    public override async Task<WorkspaceInfo> PrepareAsync(
        WorkspaceConfiguration configuration,
        IProgress<WorkspacePreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Logger.LogInformation("Preparing workspace using hybrid copy-symlink strategy for {WorkspaceId}", configuration.Id);
        var workspaceInfo = CreateBaseWorkspaceInfo(configuration);
        var workspacePath = workspaceInfo.WorkspacePath;

        try
        {
            // Allow cancellation to propagate for tests
            cancellationToken.ThrowIfCancellationRequested();

            // Clean existing workspace if force recreate is requested
            if (Directory.Exists(workspacePath) && configuration.ForceRecreate)
            {
                Logger.LogDebug("Removing existing workspace directory: {WorkspacePath}", workspacePath);
                Directory.Delete(workspacePath, true);
            }

            // Create workspace directory
            Directory.CreateDirectory(workspacePath);
            var totalFiles = configuration.Manifests.SelectMany(m => m.Files).Count();
            var processedFiles = 0;
            long totalBytesProcessed = 0;
            var copiedFiles = 0;
            var symlinkedFiles = 0;

            Logger.LogDebug("Processing {TotalFiles} files", totalFiles);
            ReportProgress(progress, 0, totalFiles, "Initializing", string.Empty);

            // Process each file
            foreach (var file in configuration.Manifests.SelectMany(m => m.Files))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationPath = Path.Combine(workspacePath, file.RelativePath);
                var isEssential = IsEssentialFile(file.RelativePath, file.Size);

                FileOperationsService.EnsureDirectoryExists(destinationPath);

                // Handle different source types
                if (file.SourceType == ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(file.Hash))
                {
                    // Use CAS content
                    var shouldCopy = ShouldCopyFile(file);
                    await CreateCasLinkAsync(file.Hash, destinationPath, cancellationToken);
                    if (shouldCopy)
                    {
                        copiedFiles++;
                        totalBytesProcessed += file.Size;
                    }
                    else
                    {
                        symlinkedFiles++;
                        totalBytesProcessed += LinkOverheadBytes;
                    }
                }
                else
                {
                    // Use regular file from base installation
                    var sourcePath = Path.Combine(configuration.BaseInstallationPath, file.RelativePath);

                    if (!ValidateSourceFile(sourcePath, file.RelativePath))
                    {
                        continue;
                    }

                    if (isEssential)
                    {
                        // Copy essential files
                        await FileOperations.CopyFileAsync(sourcePath, destinationPath, cancellationToken);
                        copiedFiles++;
                        totalBytesProcessed += file.Size;

                        // Verify file integrity if hash is provided
                        if (!string.IsNullOrEmpty(file.Hash))
                        {
                            var hashValid = await FileOperations.VerifyFileHashAsync(destinationPath, file.Hash, cancellationToken);
                            if (!hashValid)
                            {
                                Logger.LogWarning("Hash verification failed for essential file: {RelativePath}", file.RelativePath);
                            }
                        }
                    }
                    else
                    {
                        // Create symlinks for non-essential files
                        await FileOperations.CreateSymlinkAsync(destinationPath, sourcePath, cancellationToken);
                        symlinkedFiles++;
                        totalBytesProcessed += LinkOverheadBytes; // Approximate symlink overhead
                    }
                }

                processedFiles++;
                var currentOperation = isEssential ? "Copying essential file" : "Creating symlink";
                ReportProgress(progress, processedFiles, totalFiles, currentOperation, file.RelativePath);
            }

            UpdateWorkspaceInfo(workspaceInfo, processedFiles, totalBytesProcessed, configuration);
            workspaceInfo.Success = true;

            Logger.LogInformation(
                "Hybrid copy-symlink workspace prepared successfully at {WorkspacePath} with {CopiedCount} copied files and {SymlinkedCount} symlinks ({TotalSize} bytes)",
                workspacePath,
                copiedFiles,
                symlinkedFiles,
                totalBytesProcessed);
            return workspaceInfo;
        }
        catch (OperationCanceledException)
        {
            // Let cancellation propagate for tests
            CleanupWorkspaceOnFailure(workspacePath);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare hybrid copy-symlink workspace at {WorkspacePath}", workspacePath);
            CleanupWorkspaceOnFailure(workspacePath);
            workspaceInfo.Success = false;
            workspaceInfo.ValidationIssues.Add(new() { Message = ex.Message, Severity = Core.Models.Validation.ValidationSeverity.Error });
            return workspaceInfo;
        }
    }

    /// <summary>
    /// Copies or symlinks CAS content for the given hash and target path based on strategy logic.
    /// </summary>
    /// <param name="hash">CAS hash of the file.</param>
    /// <param name="targetPath">Target path for the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    protected override async Task CreateCasLinkAsync(string hash, string targetPath, CancellationToken cancellationToken)
    {
        if (ShouldCopyFile(targetPath))
        {
            var success = await FileOperations.CopyFromCasAsync(hash, targetPath, cancellationToken);
            if (!success)
            {
                throw new InvalidOperationException($"Failed to copy from CAS for hash {hash} to {targetPath}");
            }
        }
        else
        {
            var success = await FileOperations.LinkFromCasAsync(hash, targetPath, useHardLink: false, cancellationToken);
            if (!success)
            {
                throw new InvalidOperationException($"Failed to create symlink from CAS for hash {hash} to {targetPath}");
            }
        }
    }

    /// <inheritdoc/>
    protected override async Task ProcessLocalFileAsync(ManifestFile file, string targetPath, WorkspaceConfiguration configuration, CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(configuration.BaseInstallationPath, file.RelativePath);

        if (!ValidateSourceFile(sourcePath, file.RelativePath))
        {
            return;
        }

        FileOperationsService.EnsureDirectoryExists(targetPath);

        var isEssential = IsEssentialFile(file.RelativePath, file.Size);

        if (isEssential)
        {
            // Copy essential files
            await FileOperations.CopyFileAsync(sourcePath, targetPath, cancellationToken);

            // Verify file integrity if hash is provided
            if (!string.IsNullOrEmpty(file.Hash))
            {
                var hashValid = await FileOperations.VerifyFileHashAsync(targetPath, file.Hash, cancellationToken);
                if (!hashValid)
                {
                    Logger.LogWarning("Hash verification failed for essential file: {RelativePath}", file.RelativePath);
                }
            }
        }
        else
        {
            // Create symlinks for non-essential files
            await FileOperations.CreateSymlinkAsync(targetPath, sourcePath, cancellationToken);
        }
    }

    /// <summary>
    /// Determines if a file should be copied instead of symlinked.
    /// </summary>
    /// <param name="file">The manifest file to check.</param>
    /// <returns>True if the file should be copied, false if it should be symlinked.</returns>
    private static bool ShouldCopyFile(ManifestFile file)
    {
        return WorkspaceStrategyBase<HybridCopySymlinkStrategy>
            .IsEssentialFile(file.RelativePath, file.Size);
    }

    private bool ShouldCopyFile(string targetPath)
    {
        // Use manifest size if available, otherwise fallback to file size
        long fileSize = 0;
        try
        {
            if (File.Exists(targetPath))
            {
                fileSize = new FileInfo(targetPath).Length;
            }
        }
        catch
        {
            fileSize = 0;
            Logger.LogWarning("Failed to get file size for {TargetPath}, defaulting to 0", targetPath);
        }

        // Use the static IsEssentialFile logic for consistency
        return WorkspaceStrategyBase<HybridCopySymlinkStrategy>.IsEssentialFile(targetPath, fileSize);
    }
}
