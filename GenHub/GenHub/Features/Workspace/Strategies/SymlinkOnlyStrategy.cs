using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace.Strategies;

/// <summary>
/// Workspace strategy that creates symbolic links to all game files.
/// Minimal disk usage, requires administrator privileges.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SymlinkOnlyStrategy"/> class.
/// </remarks>
/// <param name="fileOperations">The file operations service.</param>
/// <param name="logger">The logger instance.</param>
public sealed class SymlinkOnlyStrategy(
    IFileOperationsService fileOperations,
    ILogger<SymlinkOnlyStrategy> logger) : WorkspaceStrategyBase<SymlinkOnlyStrategy>(fileOperations, logger)
{
    private const long LinkOverheadBytes = 1024L;

    /// <inheritdoc/>
    public override string Name => "Symlink Only";

    /// <inheritdoc/>
    public override string Description => "Creates symbolic links to all game files. Minimal disk usage but requires administrator privileges.";

    /// <inheritdoc/>
    public override bool RequiresAdminRights => OperatingSystem.IsWindows();

    /// <inheritdoc/>
    public override bool RequiresSameVolume => false;

    /// <inheritdoc/>
    public override bool CanHandle(WorkspaceConfiguration configuration)
    {
        return configuration.Strategy == WorkspaceStrategy.SymlinkOnly;
    }

    /// <inheritdoc/>
    public override long EstimateDiskUsage(WorkspaceConfiguration configuration)
    {
        // Symbolic links use minimal space - approximate 1KB per link for metadata
        return configuration.Manifest.Files.Count * LinkOverheadBytes;
    }

    /// <inheritdoc/>
    public override async Task<WorkspaceInfo> PrepareAsync(
        WorkspaceConfiguration configuration,
        IProgress<WorkspacePreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Logger.LogInformation("Preparing workspace using symlink-only strategy for {WorkspaceId}", configuration.Id);

        var workspaceInfo = CreateBaseWorkspaceInfo(configuration);
        var workspacePath = workspaceInfo.WorkspacePath;

        try
        {
            // Clean existing workspace if force recreate is requested
            if (Directory.Exists(workspacePath) && configuration.ForceRecreate)
            {
                Logger.LogDebug("Removing existing workspace directory: {WorkspacePath}", workspacePath);
                Directory.Delete(workspacePath, true);
            }

            // Create workspace directory
            Directory.CreateDirectory(workspacePath);

            var totalFiles = configuration.Manifest.Files.Count;
            var processedFiles = 0;
            long totalBytesProcessed = 0;
            var estimatedTotalBytes = EstimateDiskUsage(configuration);

            Logger.LogDebug("Processing {TotalFiles} files with estimated size {EstimatedSize} bytes", totalFiles, estimatedTotalBytes);

            ReportProgress(progress, 0, totalFiles, "Initializing", string.Empty);

            // Process each file
            foreach (var file in configuration.Manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourcePath = Path.Combine(configuration.BaseInstallationPath, file.RelativePath);
                var destinationPath = Path.Combine(workspacePath, file.RelativePath);

                if (!ValidateSourceFile(sourcePath, file.RelativePath))
                {
                    continue;
                }

                try
                {
                    FileOperationsService.EnsureDirectoryExists(destinationPath);
                    await FileOperations.CreateSymlinkAsync(destinationPath, sourcePath, cancellationToken);
                    totalBytesProcessed += LinkOverheadBytes; // Approximate symlink overhead
                }
                catch (Exception ex)
                {
                    Logger.LogError(
                        ex,
                        "Failed to create symlink for file {RelativePath} from {SourcePath} to {DestinationPath}",
                        file.RelativePath,
                        sourcePath,
                        destinationPath);
                    throw new InvalidOperationException($"Failed to create symlink for file {file.RelativePath}: {ex.Message}", ex);
                }

                processedFiles++;
                ReportProgress(progress, processedFiles, totalFiles, "Creating symlink", file.RelativePath);
            }

            UpdateWorkspaceInfo(workspaceInfo, processedFiles, totalBytesProcessed, configuration);

            Logger.LogInformation(
                "Symlink-only workspace prepared successfully at {WorkspacePath} with {FileCount} symlinks",
                workspacePath,
                processedFiles);

            return workspaceInfo;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare symlink-only workspace at {WorkspacePath}", workspacePath);

            CleanupWorkspaceOnFailure(workspacePath);

            throw;
        }
    }
}
