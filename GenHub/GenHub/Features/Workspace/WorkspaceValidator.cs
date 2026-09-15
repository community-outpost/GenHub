using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
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
        if (configuration.Manifests.Count > 0 &&
            configuration.Manifests.All(m => m.Files?.Count == 0))
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
            if (strategy.RequiresAdminRights && !IsRunningAsAdministrator())
            {
                issues.Add(new ValidationIssue
                {
                    IssueType = ValidationIssueType.AccessDenied,
                    Severity = ValidationSeverity.Error,
                    Message = $"Strategy '{strategy.Name}' requires administrator privileges",
                    Path = "System",
                });
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
                        Message = $"Strategy '{strategy.Name ?? GameClientConstants.UnknownVersion}' works best when source and destination are on the same volume. Source: {sourceRoot}, Destination: {destRoot}",
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
                logger.LogWarning(ex, "Could not check disk space for {DestinationPath}", destinationPath);
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
                if (!TryResolveContainedEntryPointPath(workspaceInfo, out var executablePath))
                {
                    issues.Add(new ValidationIssue
                    {
                        IssueType = ValidationIssueType.UnexpectedFile,
                        Severity = ValidationSeverity.Error,
                        Message = $"Executable path '{workspaceInfo.ExecutablePath}' resolves outside the workspace root '{workspaceInfo.WorkspacePath}'",
                        Path = workspaceInfo.ExecutablePath,
                    });
                }
                else if (!File.Exists(executablePath))
                {
                    issues.Add(new ValidationIssue
                    {
                        IssueType = ValidationIssueType.MissingFile,
                        Severity = ValidationSeverity.Error,
                        Message = $"Executable file not found: {executablePath}",
                        Path = executablePath,
                    });
                }
                else if (!OperatingSystem.IsWindows())
                {
                    // A lost execute bit is repaired rather than merely reported: the
                    // entry point is a workspace-owned copy, so restoring its mode cannot
                    // reach a shared content-store blob.
                    var repairResult = await EnsureEntryPointExecutableAsync(workspaceInfo, cancellationToken);
                    if (!repairResult.Success)
                    {
                        issues.Add(new ValidationIssue
                        {
                            IssueType = ValidationIssueType.AccessDenied,
                            Severity = ValidationSeverity.Warning,
                            Message = $"File is not executable by the current process: {executablePath}",
                            Path = executablePath,
                        });
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
            logger.LogError(ex, "Failed to validate workspace {WorkspaceId}", workspaceInfo.Id);
            return OperationResult<ValidationResult>.CreateFailure($"Workspace validation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures the workspace entry point is executable by the current process, restoring
    /// the Unix execute mode on a workspace-owned copy when the file exists without it.
    /// <para>
    /// This exists for workspaces materialised before executable modes were applied
    /// atomically: their entry point can be present with the execute bit lost, and no
    /// later materialisation ever runs to restore it. The repair swaps the file for a
    /// private executable copy, so even an entry point that is still hard-linked into
    /// the content store never has its shared blob touched.
    /// </para>
    /// <para>
    /// A missing entry point is reported as a failure, never created — materialisation
    /// owns producing the file; this method only restores its mode. An entry point that
    /// resolves outside the workspace root is likewise refused without touching it, so
    /// stale or corrupted metadata can never redirect the repair at a foreign file.
    /// </para>
    /// </summary>
    /// <param name="workspaceInfo">The workspace whose entry point is checked.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A successful result whose data indicates whether a repair was performed, or a
    /// failed result when the entry point is missing or could not be made executable.
    /// </returns>
    public async Task<OperationResult<bool>> EnsureEntryPointExecutableAsync(WorkspaceInfo workspaceInfo, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(workspaceInfo.ExecutablePath))
        {
            return OperationResult<bool>.CreateSuccess(false);
        }

        if (!TryResolveContainedEntryPointPath(workspaceInfo, out var executablePath))
        {
            return OperationResult<bool>.CreateFailure(
                $"Workspace entry point '{workspaceInfo.ExecutablePath}' resolves outside the workspace root '{workspaceInfo.WorkspacePath}'");
        }

        if (!File.Exists(executablePath))
        {
            return OperationResult<bool>.CreateFailure($"Workspace entry point not found: {executablePath}");
        }

        if (HasUnixExecutePermission(executablePath))
        {
            return OperationResult<bool>.CreateSuccess(false);
        }

        if (TryFindLinkedParentDirectory(workspaceInfo.WorkspacePath, executablePath, out var linkedDirectory))
        {
            return OperationResult<bool>.CreateFailure(
                $"Cannot repair workspace entry point '{executablePath}': directory '{linkedDirectory}' is a symlink, so the file may resolve outside the workspace root '{workspaceInfo.WorkspacePath}'");
        }

        try
        {
            var quarantineCleared = await Task.Run(
                () => ExecutableFileSwap.MakeExecutable(executablePath),
                cancellationToken);
            if (!quarantineCleared)
            {
                logger.LogWarning(
                    "Could not clear the macOS quarantine attribute from workspace entry point {ExecutablePath}; " +
                    "macOS may refuse to launch it until it is cleared manually",
                    executablePath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not restore the execute mode on workspace entry point {ExecutablePath}", executablePath);
            return OperationResult<bool>.CreateFailure($"Could not restore the execute mode on {executablePath}: {ex.Message}");
        }

        if (!HasUnixExecutePermission(executablePath))
        {
            return OperationResult<bool>.CreateFailure($"Workspace entry point is still not executable after repair: {executablePath}");
        }

        logger.LogInformation("Restored the execute mode on workspace entry point {ExecutablePath}", executablePath);
        return OperationResult<bool>.CreateSuccess(true);
    }

    /// <summary>
    /// Resolves the workspace entry point to a full path and requires it to be strictly
    /// inside the workspace root.
    /// <para>
    /// Containment is load-bearing here because the entry point is not only read but
    /// repaired in place: stale or corrupted workspace metadata must never cause a file
    /// outside the workspace to be replaced. The check appends the directory separator
    /// to the root before comparing, so a sibling such as <c>foo-evil</c> cannot pass
    /// for a root named <c>foo</c>.
    /// </para>
    /// </summary>
    /// <param name="workspaceInfo">The workspace whose entry point is resolved.</param>
    /// <param name="executablePath">The fully resolved entry point path, when contained.</param>
    /// <returns><c>true</c> when the entry point resolves inside the workspace root.</returns>
    private static bool TryResolveContainedEntryPointPath(WorkspaceInfo workspaceInfo, out string executablePath)
    {
        executablePath = string.Empty;

        try
        {
            var workspaceRoot = Path.GetFullPath(workspaceInfo.WorkspacePath);
            var candidate = Path.IsPathRooted(workspaceInfo.ExecutablePath)
                ? workspaceInfo.ExecutablePath
                : Path.Combine(workspaceRoot, workspaceInfo.ExecutablePath);
            var resolved = Path.GetFullPath(candidate);

            var rootWithSeparator = Path.EndsInDirectorySeparator(workspaceRoot)
                ? workspaceRoot
                : workspaceRoot + Path.DirectorySeparatorChar;

            // Ordinal on Unix; Windows path comparisons are case-insensitive.
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!resolved.StartsWith(rootWithSeparator, comparison))
            {
                return false;
            }

            executablePath = resolved;
            return true;
        }
        catch (ArgumentException)
        {
            // An entry point that cannot even be resolved is treated as escaping.
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Walks from the workspace root down to the entry point's parent directory looking
    /// for a symlinked directory.
    /// <para>
    /// Lexical containment cannot see through links: a symlinked directory inside the
    /// workspace can point anywhere, so repairing through one could replace a file
    /// outside the workspace root. The leaf itself is deliberately not checked —
    /// replacing a leaf symlink with a private executable copy is the intended,
    /// store-safe repair. Workspace strategies materialise directories with
    /// <c>Directory.CreateDirectory</c> and only ever symlink individual files
    /// (SymlinkOnlyStrategy and HybridCopySymlinkStrategy pass per-file manifest
    /// targets to <c>CreateSymlinkAsync</c>), so no normally generated workspace is
    /// refused by this check.
    /// </para>
    /// </summary>
    /// <param name="workspaceRootPath">The workspace root directory.</param>
    /// <param name="executablePath">The fully resolved, lexically contained entry point path.</param>
    /// <param name="linkedDirectory">The first symlinked directory found, when any.</param>
    /// <returns><c>true</c> when the root or an intermediate directory is a symlink.</returns>
    private static bool TryFindLinkedParentDirectory(string workspaceRootPath, string executablePath, out string linkedDirectory)
    {
        linkedDirectory = string.Empty;

        var workspaceRoot = Path.GetFullPath(workspaceRootPath);
        if (IsLinkedDirectory(workspaceRoot))
        {
            linkedDirectory = workspaceRoot;
            return true;
        }

        var parentDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrEmpty(parentDirectory))
        {
            return false;
        }

        var relative = Path.GetRelativePath(workspaceRoot, parentDirectory);
        if (relative == ".")
        {
            return false;
        }

        var current = workspaceRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (IsLinkedDirectory(current))
            {
                linkedDirectory = current;
                return true;
            }
        }

        return false;
    }

    private static bool IsLinkedDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static bool IsRunningAsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            // geteuid rather than comparing Environment.UserName to the literal "root",
            // which is wrong under `sudo -E` (USER stays the invoking account) and for
            // any uid-0 account not named root.
            return UnixNativeMethods.GetEffectiveUserId() == 0;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether the effective process identity may execute a Unix file.
    /// <para>
    /// Uses <c>faccessat(AT_EACCESS)</c> rather than merely checking whether any execute
    /// bit is present. The kernel therefore evaluates ownership, group membership and
    /// access-control rules for the identity that will actually launch the process.
    /// </para>
    /// </summary>
    /// <param name="filePath">The file to inspect.</param>
    /// <returns><c>true</c> when the file is executable by the current user.</returns>
    private static bool HasUnixExecutePermission(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        return UnixNativeMethods.CanExecute(filePath);
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
            logger.LogWarning(ex, "Could not validate symlinks in workspace {WorkspacePath}", workspacePath);
        }

        await Task.CompletedTask;
    }
}
