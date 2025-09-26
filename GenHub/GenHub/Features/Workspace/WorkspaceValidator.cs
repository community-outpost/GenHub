using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace;

/// <summary>
/// Validates workspace configurations and system prerequisites.
/// </summary>
public class WorkspaceValidator(ILogger<WorkspaceValidator> logger) : IWorkspaceValidator
{
    private readonly ILogger<WorkspaceValidator> _logger = logger;

    /// <summary>
    /// Validates a workspace configuration.
    /// </summary>
    /// <param name="configuration">The configuration to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation result.</returns>
    public Task<ValidationResult> ValidateConfigurationAsync(WorkspaceConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        // Validate required properties
        if (string.IsNullOrWhiteSpace(configuration.Id))
        {
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.UnexpectedFile,
                Severity = ValidationSeverity.Error,
                Message = "Workspace ID is required",
                Path = nameof(configuration.Id),
            });
        }

        if (string.IsNullOrWhiteSpace(configuration.BaseInstallationPath))
        {
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.UnexpectedFile,
                Severity = ValidationSeverity.Error,
                Message = "Source installation path is required",
                Path = nameof(configuration.BaseInstallationPath),
            });
        }

        if (string.IsNullOrWhiteSpace(configuration.WorkspaceRootPath))
        {
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.UnexpectedFile,
                Severity = ValidationSeverity.Error,
                Message = "Workspace root path is required",
                Path = nameof(configuration.WorkspaceRootPath),
            });
        }

        // Validate paths exist
        if (!string.IsNullOrWhiteSpace(configuration.BaseInstallationPath) && !Directory.Exists(configuration.BaseInstallationPath))
        {
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.DirectoryMissing,
                Severity = ValidationSeverity.Error,
                Message = $"Source installation path does not exist: {configuration.BaseInstallationPath}",
                Path = configuration.BaseInstallationPath,
            });
        }

        // Validate workspace base path is writable
        if (!string.IsNullOrWhiteSpace(configuration.WorkspaceRootPath))
        {
            try
            {
                Directory.CreateDirectory(configuration.WorkspaceRootPath);
                var testFile = Path.Combine(configuration.WorkspaceRootPath, "test_write.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue
                {
                    IssueType = ValidationIssueType.DirectoryMissing,
                    Severity = ValidationSeverity.Error,
                    Message = $"Workspace root path is not writable: {ex.Message}",
                    Path = configuration.WorkspaceRootPath,
                });
            }
        }

        // Validate that manifests have files (required for workspace preparation)
        if (configuration.Manifests.Any() &&
            configuration.Manifests.All(m => (m.Files?.Any() ?? false) == false))
        {
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.MissingFile,
                Severity = ValidationSeverity.Error,
                Message = "All manifests must contain at least one file to be processed by workspace strategies",
                Path = nameof(configuration.Manifests),
            });
        }

        return Task.FromResult(new ValidationResult(string.Empty, issues));
    }

    /// <summary>
    /// Validates system prerequisites for a workspace strategy.
    /// </summary>
    /// <param name="strategy">The workspace strategy to validate.</param>
    /// <param name="configuration">The full workspace configuration, including manifests for accurate estimation.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation result.</returns>
    public Task<ValidationResult> ValidatePrerequisitesAsync(IWorkspaceStrategy? strategy, WorkspaceConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        // Extract paths from configuration for validation
        var sourcePath = configuration.BaseInstallationPath;
        var destinationPath = Path.Combine(configuration.WorkspaceRootPath, configuration.Id);

        if (strategy != null)
        {
            // Use properties directly from the interface
            if (strategy.RequiresAdminRights)
            {
                if (!IsRunningAsAdministrator())
                {
                    issues.Add(new ValidationIssue
                    {
                        IssueType = ValidationIssueType.AccessDenied,
                        Severity = ValidationSeverity.Error,
                        Message = $"Strategy '{strategy.Name}' requires administrator privileges",
                        Path = "System",
                    });
                }
            }

            if (strategy.RequiresSameVolume)
            {
                var sourceRoot = Path.GetPathRoot(sourcePath);
                var destRoot = Path.GetPathRoot(destinationPath);
                if (!string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ValidationIssue
                    {
                        IssueType = ValidationIssueType.UnexpectedFile,
                        Severity = ValidationSeverity.Warning,
                        Message = $"Strategy '{strategy.Name ?? "Unknown"}' works best when source and destination are on the same volume. Source: {sourceRoot}, Destination: {destRoot}",
                        Path = "VolumeCheck",
                    });
                }
            }

            // Check available disk space
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(destinationPath) ?? destinationPath);
                long estimatedUsage = strategy.EstimateDiskUsage(configuration);

                var safetyMargin = estimatedUsage * 0.1; // 10% safety margin
                if (drive.AvailableFreeSpace < estimatedUsage + safetyMargin)
                {
                    issues.Add(new ValidationIssue
                    {
                        IssueType = ValidationIssueType.InsufficientSpace,
                        Severity = ValidationSeverity.Warning,
                        Message = $"Low disk space. Available: {drive.AvailableFreeSpace / 1024 / 1024:N0} MB, Estimated needed: {estimatedUsage / 1024 / 1024:N0} MB (with safety margin)",
                        Path = destinationPath,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not check disk space for {DestinationPath}", destinationPath);
            }
        }

        return Task.FromResult(new ValidationResult(string.Empty, issues));
    }

    /// <summary>
    /// Validates an existing workspace for integrity and completeness.
    /// </summary>
    /// <param name="workspaceInfo">The workspace to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation result.</returns>
    public async Task<OperationResult<ValidationResult>> ValidateWorkspaceAsync(WorkspaceInfo workspaceInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            var issues = new List<ValidationIssue>();

            // Validate workspace directory exists
            if (!Directory.Exists(workspaceInfo.WorkspacePath))
            {
                issues.Add(new ValidationIssue
                {
                    IssueType = ValidationIssueType.DirectoryMissing,
                    Severity = ValidationSeverity.Error,
                    Message = $"Workspace directory does not exist: {workspaceInfo.WorkspacePath}",
                    Path = workspaceInfo.WorkspacePath,
                });

                var result = new ValidationResult(workspaceInfo.Id, issues);
                return OperationResult<ValidationResult>.CreateSuccess(result);
            }

            // Validate executable exists if specified
            if (!string.IsNullOrEmpty(workspaceInfo.ExecutablePath))
            {
                var executablePath = Path.IsPathRooted(workspaceInfo.ExecutablePath)
                    ? workspaceInfo.ExecutablePath
                    : Path.Combine(workspaceInfo.WorkspacePath, workspaceInfo.ExecutablePath);

                if (!File.Exists(executablePath))
                {
                    issues.Add(new ValidationIssue
                    {
                        IssueType = ValidationIssueType.MissingFile,
                        Severity = ValidationSeverity.Error,
                        Message = $"Executable file not found: {executablePath}",
                        Path = executablePath,
                    });
                }
                else
                {
                    // Check if executable has execute permissions (on Unix systems)
                    if (!OperatingSystem.IsWindows())
                    {
                        try
                        {
                            var fileInfo = new FileInfo(executablePath);

                            // Check if file exists and has execute permission for the current user
                            if (!fileInfo.Exists)
                            {
                                issues.Add(new ValidationIssue
                                {
                                    IssueType = ValidationIssueType.AccessDenied,
                                    Severity = ValidationSeverity.Warning,
                                    Message = $"Cannot verify execute permissions for: {executablePath}",
                                    Path = executablePath,
                                });
                            }
                            else
                            {
                                // Properly check execute permission using Unix stat
                                // TODO: Make this a platform specific validation
                                if (!HasUnixExecutePermission(executablePath))
                                {
                                    issues.Add(new ValidationIssue
                                    {
                                        IssueType = ValidationIssueType.AccessDenied,
                                        Severity = ValidationSeverity.Warning,
                                        Message = $"File is not marked as executable: {executablePath}",
                                        Path = executablePath,
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not check execute permissions for {ExecutablePath}", executablePath);
                        }
                    }
                }
            }

            // Validate workspace file count matches expected
            var actualFileCount = Directory.GetFiles(workspaceInfo.WorkspacePath, "*", SearchOption.AllDirectories).Length;
            if (workspaceInfo.FileCount > 0 && actualFileCount != workspaceInfo.FileCount)
            {
                issues.Add(new ValidationIssue
                {
                    IssueType = ValidationIssueType.CorruptedFile,
                    Severity = ValidationSeverity.Warning,
                    Message = $"File count mismatch. Expected: {workspaceInfo.FileCount}, Actual: {actualFileCount}",
                    Path = workspaceInfo.WorkspacePath,
                });
            }

            // Check for broken symlinks (if strategy might use them)
            if (workspaceInfo.Strategy == WorkspaceStrategy.SymlinkOnly ||
                workspaceInfo.Strategy == WorkspaceStrategy.HybridCopySymlink)
            {
                await ValidateSymlinksAsync(workspaceInfo.WorkspacePath, issues, cancellationToken);
            }

            // Validate workspace is accessible
            try
            {
                var testFile = Path.Combine(workspaceInfo.WorkspacePath, $"test_access_{Guid.NewGuid()}.tmp");
                await File.WriteAllTextAsync(testFile, "test", cancellationToken);
                File.Delete(testFile);
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue
                {
                    IssueType = ValidationIssueType.AccessDenied,
                    Severity = ValidationSeverity.Error,
                    Message = $"Workspace is not writable: {ex.Message}",
                    Path = workspaceInfo.WorkspacePath,
                });
            }

            var validationResult = new ValidationResult(workspaceInfo.Id, issues);
            return OperationResult<ValidationResult>.CreateSuccess(validationResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate workspace {WorkspaceId}", workspaceInfo.Id);
            return OperationResult<ValidationResult>.CreateFailure($"Workspace validation failed: {ex.Message}");
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            // On Unix systems, check if running as root
            return Environment.UserName == "root";
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Add this helper method to check Unix execute permission
    private static bool HasUnixExecutePermission(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return true;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"[ -x '{filePath.Replace("'", "'\\''")}' ]\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);

            if (proc == null)
                return false;

            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch
        {
            // If all checks fail, assume not executable
            return false;
        }
    }

    private async Task ValidateSymlinksAsync(string workspacePath, List<ValidationIssue> issues, CancellationToken cancellationToken)
    {
        try
        {
            var files = Directory.GetFiles(workspacePath, "*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var fileInfo = new FileInfo(file);
                if (fileInfo.LinkTarget != null)
                {
                    // This is a symlink, check if target exists
                    var targetPath = fileInfo.LinkTarget;
                    if (!Path.IsPathRooted(targetPath))
                    {
                        targetPath = Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, targetPath);
                    }

                    if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
                    {
                        issues.Add(new ValidationIssue
                        {
                            IssueType = ValidationIssueType.MissingFile,
                            Severity = ValidationSeverity.Error,
                            Message = $"Broken symlink: {file} -> {targetPath}",
                            Path = file,
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not validate symlinks in workspace {WorkspacePath}", workspacePath);
        }

        await Task.CompletedTask;
    }
}
